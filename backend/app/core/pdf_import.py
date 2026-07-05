"""PDF 图纸导入: 矢量几何 + 原生文本 + OCR 兜底。

CAD 打印的 PDF 常见两种文字形态:
- 真文字(TrueType 嵌入): 直接用 PyMuPDF 提取, 带坐标
- 矢量化文字(SHX 字体被打印成线条): 原生提取为空, 必须 OCR

本模块的测试循环产物:
- Round A: 低倍整页 OCR -> CAD 小字号无法识别
- Round B: 5 倍局部渲染 -> 可识别
- Round C: 全图分块扫描 + 置信度过滤 + 邻近去重 -> 稳定恢复标注

输出与 dwg2json.mjs 同构的通用实体流(坐标单位: 图纸 mm)。
比例标定: 用轴网平行长线的相邻间距 与 尺寸标注众数(如 6300)对齐;
标定失败时按 1:100 出图假设(pt -> mm ×35.28), 并写入识别日志。
"""

from __future__ import annotations

import re

import fitz  # PyMuPDF

PT_TO_PAPER_MM = 25.4 / 72.0
DEFAULT_PLOT_SCALE = 100.0  # 1:100

_DIM_RE = re.compile(r"^\d{4,5}$")


def _extract_vectors(page: fitz.Page) -> list[dict]:
    """矢量线段(直线/折线段), 坐标 pt。"""
    out: list[dict] = []
    for path in page.get_drawings():
        for item in path["items"]:
            if item[0] == "l":  # line
                p1, p2 = item[1], item[2]
                out.append({"type": "LINE", "layer": "PDF",
                            "p1": [p1.x, p1.y], "p2": [p2.x, p2.y],
                            "w": path.get("width") or 0})
            elif item[0] == "re":  # rect
                r = item[1]
                out.append({"type": "RECT", "layer": "PDF",
                            "p1": [r.x0, r.y0], "p2": [r.x1, r.y1]})
    return out


def _extract_native_text(page: fitz.Page) -> list[dict]:
    out = []
    for x0, y0, x1, y1, word, *_ in page.get_text("words"):
        out.append({"type": "TEXT", "layer": "PDF_TEXT",
                    "at": [(x0 + x1) / 2, (y0 + y1) / 2], "text": word,
                    "native": True})
    return out


def _ocr_page(page: fitz.Page, zoom: float = 5.0,
              tile_w: int = 400, tile_h: int = 200,
              min_conf: float = 0.5) -> list[dict]:
    """分块高倍 OCR, 返回文本实体(坐标 pt)。"""
    import numpy as np
    from rapidocr_onnxruntime import RapidOCR

    ocr = _get_ocr()
    W, H = page.rect.width, page.rect.height
    found: list[dict] = []
    step_x, step_y = tile_w - 40, tile_h - 30  # 少量重叠防切字
    x0 = 0.0
    while x0 < W:
        y0 = 0.0
        while y0 < H:
            clip = fitz.Rect(x0, y0, min(x0 + tile_w, W), min(y0 + tile_h, H))
            pix = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), clip=clip)
            img = np.frombuffer(pix.samples, dtype=np.uint8).reshape(
                pix.height, pix.width, pix.n)
            result, _ = ocr(img)
            for box, txt, conf in (result or []):
                if conf < min_conf or not txt.strip():
                    continue
                cx = x0 + sum(p[0] for p in box) / 4 / zoom
                cy = y0 + sum(p[1] for p in box) / 4 / zoom
                found.append({"type": "TEXT", "layer": "PDF_OCR",
                              "at": [cx, cy], "text": txt.strip(),
                              "conf": round(float(conf), 3)})
            y0 += step_y
        x0 += step_x

    # 邻近去重(分块重叠会产生同文本双检, 以及切边残片如 "100"->"00")
    dedup: list[dict] = []
    for t in sorted(found, key=lambda t: (-len(t["text"]), -t.get("conf", 0))):
        dup = False
        for d in dedup:
            if (abs(d["at"][0] - t["at"][0]) < 12
                    and abs(d["at"][1] - t["at"][1]) < 8
                    and (t["text"] == d["text"] or t["text"] in d["text"])):
                dup = True
                break
        if not dup:
            dedup.append(t)
    return dedup


_OCR_SINGLETON = None


def _get_ocr():
    global _OCR_SINGLETON
    if _OCR_SINGLETON is None:
        from rapidocr_onnxruntime import RapidOCR
        _OCR_SINGLETON = RapidOCR()
    return _OCR_SINGLETON


def calibrate_scale(entities: list[dict], log: list[str]) -> float:
    """求 pt -> 图纸mm 的比例因子。

    取竖直长线的相邻间距众数 与 尺寸标注众数 对齐; 失败按 1:100。
    """
    from collections import Counter

    # 轴线常为点划线(大量短段), 先按 x 聚合竖直段的总展布再筛选
    spans: dict[float, list[float]] = {}
    for e in entities:
        if e["type"] != "LINE":
            continue
        (x1, y1), (x2, y2) = e["p1"], e["p2"]
        if abs(x1 - x2) < 0.5:
            spans.setdefault(round(x1, 0), []).extend([y1, y2])
    xs = sorted(x for x, ys in spans.items()
                if max(ys) - min(ys) > 150)  # 总展布超过 150pt 视为轴线族
    gaps = Counter()
    for a, b in zip(xs, xs[1:]):
        g = round(b - a, 0)
        if g > 20:
            gaps[g] += 1

    dims = Counter(t["text"] for t in entities
                   if t["type"] == "TEXT" and _DIM_RE.match(t.get("text", "")))
    if gaps and dims:
        gap_pt, _ = gaps.most_common(1)[0]
        dim_mm = float(dims.most_common(1)[0][0])
        k = dim_mm / gap_pt
        # 合理性: 出图比例应在 1:20 ~ 1:500 之间
        if 20 * PT_TO_PAPER_MM <= k <= 500 * PT_TO_PAPER_MM:
            log.append(f"比例标定: 轴距 {gap_pt}pt ↔ 标注 {dim_mm:g}mm, "
                       f"k = {k:.3f} mm/pt (≈1:{k / PT_TO_PAPER_MM:.0f})")
            return k
    k = DEFAULT_PLOT_SCALE * PT_TO_PAPER_MM
    log.append(f"比例标定失败, 按 1:{DEFAULT_PLOT_SCALE:.0f} 出图假设 k={k:.3f} mm/pt")
    return k


def extract_pdf(data: bytes, use_ocr: bool = True,
                max_pages: int = 6) -> dict:
    """PDF -> 通用实体流(mm, y 轴翻转为向上), 每个实体带 page 序号。"""
    doc = fitz.open(stream=data, filetype="pdf")
    log: list[str] = []
    all_ents: list[dict] = []

    for pno in range(min(len(doc), max_pages)):
        page = doc[pno]
        vecs = _extract_vectors(page)
        native = _extract_native_text(page)
        ents = vecs + native
        n_native_chars = sum(len(t["text"]) for t in native)
        if use_ocr and n_native_chars < 3000:
            ocr_texts = _ocr_page(page)
            ents += ocr_texts
            log.append(f"第{pno + 1}页: 矢量 {len(vecs)}, 原生文本 {len(native)} "
                       f"(疑似矢量化文字), OCR 恢复 {len(ocr_texts)} 条")
        else:
            log.append(f"第{pno + 1}页: 矢量 {len(vecs)}, 原生文本 {len(native)}")
        H = page.rect.height
        for e in ents:
            e["page"] = pno
            e["_H"] = H
        all_ents += ents

    # 比例标定(按页) + 坐标转换: pt -> mm, y 翻转(图纸坐标系 y 向上)
    for pno in range(min(len(doc), max_pages)):
        page_ents = [e for e in all_ents if e["page"] == pno]
        k = calibrate_scale(page_ents, log)
        for e in page_ents:
            H = e.pop("_H")
            for key in ("p1", "p2", "at"):
                if key in e:
                    x, y = e[key]
                    e[key] = [round(x * k, 1), round((H - y) * k, 1)]
            # 不同页错开放置, 避免坐标重叠
            off = pno * 2_000_000
            for key in ("p1", "p2", "at"):
                if key in e:
                    e[key][0] += off

    return {"source": "pdf", "entities": all_ents, "log": log}
