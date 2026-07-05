"""柱加固图纸识别器(增大截面法 / 外包钢法)。

适用图纸: "柱加固施工图" —— 平面布置图标注加固柱编号(JKZ1/JKZ2...),
配套两张表:
- 柱增大截面加固表: 编号 | 原柱截面(bxh) | 新增b1/b2/h1/h2 | 箍筋 | 角筋/纵筋
- 外包钢法加固柱表: 编号 | 外包角钢型号(4L150x7) | 缀板 | 加固标高范围

输出 VCAD 原始图纸 JSON(RCOL 实体), 无法从图面确定的量(加固高度、
"按实际"尺寸)按显式假设计入并标记待人工复核。
"""

from __future__ import annotations

import math
import re
from collections import Counter

RCOL_TAG_RE = re.compile(r"^J?KZ?\s*(\d{1,2})$|^JKZ\d{1,2}$", re.IGNORECASE)
SECTION_RE = re.compile(r"(\d{3,4})\s*[xX×]\s*(\d{3,4})")
ANGLE_RE = re.compile(r"(\d)\s*L\s*(\d{2,3})\s*[xX×]\s*(\d{1,2})", re.IGNORECASE)

DEFAULT_STOREY_MM = 4000     # 加固高度假设(图面未注明层高时)
DEFAULT_DELTA_MM = 100       # "按实际" 尺寸的计算假设
STEEL_DENSITY = 7850.0


def _texts(entities: list[dict]) -> list[dict]:
    return [e for e in entities
            if e.get("type") == "TEXT" and isinstance(e.get("text"), str)
            and e.get("at")]


def _norm_tag(s: str) -> str | None:
    s = s.strip().upper().replace("Ｊ", "J").replace("IKZ", "JKZ").replace("JK2", "JKZ")
    m = re.match(r"^JKZ(\d{1,2})$", s)
    return f"JKZ{m.group(1)}" if m else None


def angle_kg_per_m(b_mm: float, t_mm: float) -> float:
    """等边角钢理论米重: A = t(2b − t)。"""
    area_mm2 = t_mm * (2 * b_mm - t_mm)
    return round(area_mm2 * STEEL_DENSITY * 1e-6, 2)


# ---------------------------------------------------------------------------
# 表格解析
# ---------------------------------------------------------------------------

def assign_table_cells(texts: list[dict]) -> tuple[list, list, set]:
    """把编号单元格按语义归属到 增大截面表/外包钢表。

    修复记录:
    - v1 固定矩形窗口: 两表相邻时外包钢表吞掉增大截面表全部编号;
    - v2 最近标题归属 + 距离上限: 上限依赖比例标定, 不同页比例不一时
      会把平面标注吞进表格、或漏掉表格行;
    - v3(现行) 语义判据: 编号右侧同行存在截面尺寸(400x400) => 增大截面表行;
      附近同时存在角钢型号与外包钢表标题 => 外包钢表行; 其余视为平面标注。
    """
    enlarge_cells, jacket_cells, used = [], [], set()
    for t in texts:
        if not _norm_tag(t["text"]):
            continue
        row_has_section = any(
            SECTION_RE.search(o["text"])
            and abs(o["at"][1] - t["at"][1]) < 450
            and 0 < o["at"][0] - t["at"][0] < 70000
            for o in texts)
        if row_has_section:
            enlarge_cells.append(t)
            used.add(id(t))
            continue
        near_angle = any(
            ANGLE_RE.search(o["text"])
            and math.hypot(o["at"][0] - t["at"][0], o["at"][1] - t["at"][1]) < 30000
            for o in texts)
        near_jacket_title = any(
            "外包钢法加固柱表" in o["text"]
            and math.hypot(o["at"][0] - t["at"][0], o["at"][1] - t["at"][1]) < 30000
            for o in texts)
        if near_angle and near_jacket_title:
            jacket_cells.append(t)
            used.add(id(t))
    return enlarge_cells, jacket_cells, used


def parse_enlarge_table(tag_cells: list[dict], texts: list[dict],
                        log: list[str]) -> dict[str, dict]:
    """柱增大截面加固表: 编号 -> {b, h, db1, db2, dh1, dh2, 箍筋, 纵筋}。"""
    out: dict[str, dict] = {}
    for cell in tag_cells:
        tag = _norm_tag(cell["text"])
        if not tag or tag in out:
            continue
        row = sorted((t for t in texts
                      if abs(t["at"][1] - cell["at"][1]) < 450
                      and 0 < t["at"][0] - cell["at"][0] < 70000),
                     key=lambda t: t["at"][0])
        # 剔除 OCR 分块残片: 与相邻单元格过近且互为子串的短文本
        cleaned: list[dict] = []
        for t in row:
            if cleaned and (t["at"][0] - cleaned[-1]["at"][0]) < 1500 and (
                    t["text"] in cleaned[-1]["text"]
                    or cleaned[-1]["text"] in t["text"]):
                if len(t["text"]) > len(cleaned[-1]["text"]):
                    cleaned[-1] = t
                continue
            cleaned.append(t)
        row = cleaned
        sec = None
        deltas: list[float | None] = []
        hoop = ""
        bars: list[str] = []
        for c in row:
            s = c["text"].strip()
            if sec is None and SECTION_RE.search(s):
                m = SECTION_RE.search(s)
                sec = (float(m.group(1)), float(m.group(2)))
            elif len(deltas) < 4 and re.fullmatch(r"\d{2,3}", s) and int(s) >= 20:
                deltas.append(float(s))
            elif len(deltas) < 4 and "按实际" in s:
                deltas.append(None)
            elif not hoop and re.search(r"\d+@\d+", s):
                hoop = s
            elif re.search(r"\d[#女Φφ⌀]?\d{2}$", s) or re.fullmatch(r"\d{2}", s):
                bars.append(s)
        if sec is None:
            continue
        while len(deltas) < 4:
            deltas.append(None)
        out[tag] = {
            "b": sec[0], "h": sec[1],
            "deltas": deltas, "hoop": hoop, "bars": bars,
            "assumed": [i for i, d in enumerate(deltas) if d is None],
        }
    if out:
        log.append(f"增大截面加固表: {len(out)} 个编号 ({', '.join(sorted(out))})")
    return out


def parse_jacket_table(tag_cells: list[dict], texts: list[dict],
                       log: list[str]) -> dict[str, dict]:
    """外包钢法加固柱表: 编号 -> {angle_n, angle_b, angle_t}。"""
    out: dict[str, dict] = {}
    for tag_cell in tag_cells:
        tag = _norm_tag(tag_cell["text"])
        if tag in out:
            continue
        angles = [t for t in texts if ANGLE_RE.search(t["text"])
                  and math.hypot(t["at"][0] - tag_cell["at"][0],
                                 t["at"][1] - tag_cell["at"][1]) < 30000]
        if not angles:
            continue
        ang = min(angles, key=lambda a: abs(a["at"][1] - tag_cell["at"][1])
                  + abs(a["at"][0] - tag_cell["at"][0]) * 0.01)
        m = ANGLE_RE.search(ang["text"])
        n, b, t = int(m.group(1)), float(m.group(2)), float(m.group(3))
        out[tag] = {"angle_n": n, "angle_b": b, "angle_t": t,
                    "angle_spec": f"{n}∠{b:g}×{t:g}",
                    "kg_per_m_each": angle_kg_per_m(b, t)}
    if out:
        specs = ", ".join(f"{k}:{v['angle_spec']}" for k, v in out.items())
        log.append(f"外包钢加固表: {len(out)} 个编号 ({specs})")
    return out


# ---------------------------------------------------------------------------
# 识别主入口
# ---------------------------------------------------------------------------

def recognize(entity_json: dict, source_name: str = "柱加固图") -> dict:
    ents = entity_json["entities"]
    texts = _texts(ents)
    log: list[str] = list(entity_json.get("log", []))

    enlarge_cells, jacket_cells, table_cell_ids = assign_table_cells(texts)
    enlarge = parse_enlarge_table(enlarge_cells, texts, log)
    jacket = parse_jacket_table(jacket_cells, texts, log)
    if not enlarge and not jacket:
        raise ValueError("未找到柱加固表(增大截面/外包钢)")

    # 平面标注: JKZn 标签 -> 加固柱位置(排除表格单元格, 邻近去重)
    plan_labels: list[tuple[str, float, float]] = []
    for t in texts:
        tag = _norm_tag(t["text"])
        if not tag or id(t) in table_cell_ids:
            continue
        x, y = t["at"]
        if any(tt == tag and math.hypot(x - px, y - py) < 800
               for tt, px, py in plan_labels):
            continue
        plan_labels.append((tag, x, y))

    if not plan_labels:
        raise ValueError("平面上未找到加固柱标注(JKZn)")
    counts = Counter(t for t, _, _ in plan_labels)
    log.append(f"平面加固柱标注: 共 {len(plan_labels)} 处 "
               f"({dict(sorted(counts.items()))})")

    # 输出 RCOL 实体(坐标平移到原点附近)
    ox = min(x for _, x, _ in plan_labels) - 2000
    oy = min(y for _, _, y in plan_labels) - 2000
    entities_out: list[dict] = []
    assumptions = [f"加固高度按 {DEFAULT_STOREY_MM / 1000:g}m 假设(图面未注明层高), 需按层高表复核"]
    if any(v["assumed"] for v in enlarge.values()):
        assumptions.append(f"表中“按实际”尺寸按 {DEFAULT_DELTA_MM}mm 假设计入, 需现场实测复核")

    for tag, x, y in plan_labels:
        spec = enlarge.get(tag)
        jspec = jacket.get(tag)
        ent: dict = {
            "layer": "RCOL", "type": "insert",
            "at": [round(x - ox, 1), round(y - oy, 1)],
            "tag": tag, "height": DEFAULT_STOREY_MM,
        }
        if spec:
            deltas = [d if d is not None else DEFAULT_DELTA_MM for d in spec["deltas"]]
            ent.update({
                "method": "增大截面", "b": spec["b"], "h": spec["h"],
                "db1": deltas[0], "db2": deltas[1],
                "dh1": deltas[2], "dh2": deltas[3],
                "hoop": spec["hoop"], "bars": spec["bars"],
                "assumed_delta": bool(spec["assumed"]),
            })
        elif jspec:
            ent.update({
                "method": "外包钢",
                "b": 400, "h": 400,   # 原截面"详平面", 按同工程柱截面假设
                "angle_n": jspec["angle_n"], "angle_b": jspec["angle_b"],
                "angle_t": jspec["angle_t"], "angle_spec": jspec["angle_spec"],
                "kg_per_m_each": jspec["kg_per_m_each"],
            })
        else:
            ent.update({"method": "未知", "b": 400, "h": 400})
        entities_out.append(ent)

    unknown = [t for t, _, _ in plan_labels
               if t not in enlarge and t not in jacket]
    if unknown:
        log.append(f"⚠ {len(unknown)} 处标注在两张加固表中均无对应编号: "
                   f"{sorted(set(unknown))}")

    return {
        "meta": {
            "name": source_name,
            "desc": "真实 PDF 识别结果: 柱加固平面(增大截面/外包钢)",
            "units": "mm", "region": "上海", "kind": "reinforcement",
            "recognition": {"log": log, "assumptions": assumptions,
                            "enlarge_table": enlarge,
                            "jacket_table": {k: {kk: vv for kk, vv in v.items()}
                                             for k, v in jacket.items()}},
        },
        "levels": [{"name": "1F", "elev": 0, "height": DEFAULT_STOREY_MM}],
        "entities": entities_out,
    }
