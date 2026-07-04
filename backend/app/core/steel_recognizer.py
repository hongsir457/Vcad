"""真实钢结构图纸识别器。

输入: dwg2json.mjs 提取的通用实体流(模型空间递归展开, 世界坐标, mm)
输出: VCAD 标准原始图纸 JSON(含钢构件扩展层 SCOL/SBEAM)

识别策略(针对"平面布置图 + 钢材型号表"类钢结构施工图):

1. 钢材型号表   以"钢材型号表"标题定位, 按行重建表格:
                编号(钢柱-C1/钢梁-B2) -> 型号(310UC118) -> 截面(H314.6x307.0x11.9x18.7)
                澳标 UB/UC 型号尾数即每米质量(kg/m), 并可由截面尺寸交叉验证
2. 平面定位     选取构件最密集的平面区域(轴线+梁线聚类)
3. 轴网         AXIS 图层直线 -> 纵横轴位置; 轴号气泡属性文字 -> 轴号
4. 钢柱         "钢柱-Cn"标注 -> 吸附最近轴网交点, 交点去重
5. 钢梁         梁图层多段线 -> 线段; "钢梁-Bn"标注 -> 最近梁段;
                未标注梁段继承最近平行已标注梁段的编号
6. 标高分区     "此区域结构板顶标高为X"文本 -> 构件按最近分区取顶标高
"""

from __future__ import annotations

import math
import re
from collections import Counter

# ---------------------------------------------------------------------------
# 型钢截面
# ---------------------------------------------------------------------------

H_SECTION_RE = re.compile(
    r"H\s*(\d+(?:\.\d+)?)[xX×](\d+(?:\.\d+)?)[xX×](\d+(?:\.\d+)?)[xX×](\d+(?:\.\d+)?)")
MODEL_RE = re.compile(r"(\d+)\s*(UB|UC|PFC)\s*(\d+(?:\.\d+)?)?")
MEMBER_TAG_RE = re.compile(r"^钢(柱|梁)-([A-Z]+\d+)$")

STEEL_DENSITY = 7850.0  # kg/m³


def parse_h_section(text: str) -> dict | None:
    """解析 H602.0x228.0x10.6x14.8 -> 截面几何(mm)与理论米重。"""
    m = H_SECTION_RE.search(text or "")
    if not m:
        return None
    h, b, tw, tf = (float(m.group(i)) for i in range(1, 5))
    area_mm2 = 2 * b * tf + (h - 2 * tf) * tw
    return {
        "h": h, "b": b, "tw": tw, "tf": tf,
        "area_mm2": round(area_mm2, 1),
        "kg_per_m_calc": round(area_mm2 * STEEL_DENSITY * 1e-6, 2),
    }


def model_kg_per_m(model: str) -> float | None:
    """澳标型号 250UB37 -> 37.0 kg/m (型号尾数即名义米重)。"""
    m = MODEL_RE.search(model or "")
    if m and m.group(3):
        return float(m.group(3))
    return None


# ---------------------------------------------------------------------------
# 几何工具
# ---------------------------------------------------------------------------

def _dist_point_seg(px, py, x1, y1, x2, y2) -> float:
    dx, dy = x2 - x1, y2 - y1
    l2 = dx * dx + dy * dy
    if l2 == 0:
        return math.hypot(px - x1, py - y1)
    t = max(0.0, min(1.0, ((px - x1) * dx + (py - y1) * dy) / l2))
    return math.hypot(px - (x1 + t * dx), py - (y1 + t * dy))


def _texts(entities: list[dict]) -> list[dict]:
    return [e for e in entities
            if e.get("type") in ("TEXT", "MTEXT", "ATTRIB")
            and isinstance(e.get("text"), str) and e.get("at")]


# ---------------------------------------------------------------------------
# 1. 钢材型号表
# ---------------------------------------------------------------------------

SCHEDULE_HEADERS = ("编号", "型号", "钢材等级", "截面尺寸")


def parse_schedule(entities: list[dict], log: list[str]) -> dict[str, dict]:
    """重建钢材型号表: 构件编号 -> {model, grade, section, kg_per_m}。

    以表头("编号/型号/钢材等级/截面尺寸")的 x 坐标划定列带,
    每个编号单元格在各列带内取 y 基线最近的单元格 —— 相邻行距很小
    (实际图纸可低至 400 以下)时, 固定行高容差会串行, 列带+最近基线不会。
    """
    texts = _texts(entities)
    titles = [t for t in texts if "钢材型号表" in t["text"]]
    schedule: dict[str, dict] = {}
    if not titles:
        log.append("未找到钢材型号表")
        return schedule

    for title in titles:
        tx, ty = title["at"]
        near = [t for t in texts
                if abs(t["at"][0] - tx) < 40000 and -30000 < t["at"][1] - ty < 2000]

        headers: dict[str, float] = {}
        for t in near:
            s = t["text"].replace(" ", "").strip()
            if s in SCHEDULE_HEADERS and s not in headers:
                headers[s] = t["at"][0]
        col_x = {k: headers.get(k) for k in SCHEDULE_HEADERS}

        def cell_in_band(band_x: float | None, y0: float,
                         pred) -> str:
            """列带内 y 最近且满足语义校验的单元格。"""
            best, bd = "", 1e18
            for t in near:
                s = t["text"].strip()
                if not s or not pred(s):
                    continue
                if band_x is not None and abs(t["at"][0] - band_x) > 3500:
                    continue
                dy = abs(t["at"][1] - y0)
                if dy < bd and dy < 600:
                    best, bd = s, dy
            return best

        tags = [t for t in near if MEMBER_TAG_RE.match(t["text"].strip())]
        for tag_cell in tags:
            tag = MEMBER_TAG_RE.match(tag_cell["text"].strip()).group(2)
            if tag in schedule:
                continue
            y0 = tag_cell["at"][1]
            model = cell_in_band(col_x["型号"], y0,
                                 lambda s: bool(MODEL_RE.search(s)) and "H" not in s)
            grade = cell_in_band(col_x["钢材等级"], y0,
                                 lambda s: "Gr" in s or s.startswith(("Q", "BHP")))
            section = cell_in_band(col_x["截面尺寸"], y0,
                                   lambda s: bool(H_SECTION_RE.search(s)))
            sec = parse_h_section(section)
            kg = model_kg_per_m(model)
            if kg is None and sec:
                kg = sec["kg_per_m_calc"]
            if kg is None:
                continue
            # 语义校验: 名义米重与截面理论米重偏差应 <15%, 否则视为串行, 丢弃截面
            if sec and abs(sec["kg_per_m_calc"] - kg) / kg > 0.15:
                log.append(f"⚠ {tag}: 型号 {model} 与截面 {section} 不匹配"
                           f"(名义 {kg} vs 理论 {sec['kg_per_m_calc']} kg/m), 按型号名义米重计")
                section, sec = "", None
            schedule[tag] = {
                "model": model, "grade": grade,
                "section": section, "kg_per_m": kg,
                "section_geom": sec,
            }
    log.append(f"钢材型号表解析: {len(schedule)} 个构件规格 "
               f"({', '.join(sorted(schedule))})")
    return schedule


# ---------------------------------------------------------------------------
# 2-6. 平面识别
# ---------------------------------------------------------------------------

BEAM_LAYER_RE = re.compile(r"BEAM", re.IGNORECASE)


def recognize(entity_json: dict, source_name: str = "真实图纸") -> dict:
    """识别主入口, 返回 VCAD 原始图纸 JSON。"""
    ents = entity_json["entities"]
    log: list[str] = []

    schedule = parse_schedule(ents, log)
    texts = _texts(ents)

    # --- 平面定位: 以钢柱标注最密集的簇为平面区域 ---
    col_labels = [t for t in texts if t["text"].strip().startswith("钢柱-")]
    beam_labels = [t for t in texts if t["text"].strip().startswith("钢梁-")]
    if not col_labels:
        raise ValueError("图中未找到钢柱标注, 无法识别平面")

    # 简单密度聚类: 网格化计数取最大簇, 再向外扩展
    def cluster_center(labels: list[dict]) -> tuple[float, float]:
        c = Counter((round(t["at"][0] / 20000), round(t["at"][1] / 20000)) for t in labels)
        (gx, gy), _ = c.most_common(1)[0]
        return gx * 20000, gy * 20000

    cx, cy = cluster_center(col_labels + beam_labels)
    R = 40000
    win = lambda p: abs(p[0] - cx) < R and abs(p[1] - cy) < R  # noqa: E731

    # --- 轴网 ---
    vx, hy = [], []
    for e in ents:
        if e.get("type") == "LINE" and e.get("layer", "").upper() in ("AXIS", "S-AXIS"):
            p1, p2 = e["p1"], e["p2"]
            if not (win(p1) or win(p2)):
                continue
            if abs(p1[0] - p2[0]) < 10:
                vx.append(round(p1[0]))
            elif abs(p1[1] - p2[1]) < 10:
                hy.append(round(p1[1]))
    vx = sorted(set(vx))
    hy = sorted(set(hy))

    def dedup(vals: list[int], tol: int = 500) -> list[int]:
        out: list[int] = []
        for v in vals:
            if not out or v - out[-1] > tol:
                out.append(v)
        return out

    vx, hy = dedup(vx), dedup(hy)
    if len(vx) < 2 or len(hy) < 2:
        raise ValueError(f"轴网识别失败: 纵轴 {len(vx)} 横轴 {len(hy)}")
    log.append(f"轴网: 纵轴 {len(vx)} 道, 横轴 {len(hy)} 道, "
               f"跨度 {(vx[-1] - vx[0]) / 1000:.1f}m × {(hy[-1] - hy[0]) / 1000:.1f}m")

    # 轴号: 单字符属性文字, 数字沿 x, 字母沿 y
    ax_labels_x: dict[int, str] = {}
    ax_labels_y: dict[int, str] = {}
    for t in texts:
        s = t["text"].strip()
        if len(s) > 2 or not s:
            continue
        x, y = t["at"]
        if s.isdigit():
            near = min(vx, key=lambda v: abs(v - x))
            if abs(near - x) < 3200:
                ax_labels_x.setdefault(near, s)
        elif s.isalpha() and s.isupper():
            near = min(hy, key=lambda v: abs(v - y))
            if abs(near - y) < 4500:
                ax_labels_y.setdefault(near, s)

    # --- 标高分区 ---
    LEVEL_RE = re.compile(r"标高为?\s*([-+]?\d+\.\d+)")
    zones: list[tuple[float, float, float]] = []
    for t in texts:
        m = LEVEL_RE.search(t["text"])
        if m and win(t["at"]):
            zones.append((t["at"][0], t["at"][1], float(m.group(1))))
    default_top = max((z[2] for z in zones), default=4.5) * 1000
    log.append(f"板顶标高分区: {sorted(set(z[2] for z in zones))}" if zones
               else "未找到标高标注, 采用默认层高")

    def top_at(x: float, y: float) -> float:
        if not zones:
            return default_top
        zx, zy, zv = min(zones, key=lambda z: (z[0] - x) ** 2 + (z[1] - y) ** 2)
        return zv * 1000

    # --- 钢柱: 标注 -> 最近轴网交点 ---
    columns: dict[tuple[int, int], dict] = {}
    for t in col_labels:
        if not win(t["at"]):
            continue
        tag = t["text"].strip().replace("钢柱-", "")
        x, y = t["at"]
        gx = min(vx, key=lambda v: abs(v - x))
        gy = min(hy, key=lambda v: abs(v - y))
        if abs(gx - x) > 4000 or abs(gy - y) > 4000:
            continue
        key = (gx, gy)
        if key not in columns:
            columns[key] = {"at": [gx, gy], "tag": tag, "top": top_at(gx, gy)}
    log.append(f"钢柱: 识别 {len(columns)} 根 "
               f"({dict(Counter(c['tag'] for c in columns.values()))})")

    # --- 钢梁: 梁图层多段线/直线 -> 线段, 标注就近赋名 ---
    segs: list[dict] = []
    for e in ents:
        if not BEAM_LAYER_RE.search(e.get("layer", "")) or "SIG" in e.get("layer", ""):
            continue
        pts: list[list[float]] = []
        if e["type"] in ("LWPOLYLINE",) and e.get("pts"):
            pts = e["pts"]
        elif e["type"] == "LINE":
            pts = [e["p1"], e["p2"]]
        for i in range(len(pts) - 1):
            (x1, y1), (x2, y2) = pts[i], pts[i + 1]
            if not (win((x1, y1)) and win((x2, y2))):
                continue
            L = math.hypot(x2 - x1, y2 - y1)
            if L < 800:  # 过滤符号短线
                continue
            segs.append({"p1": [x1, y1], "p2": [x2, y2], "len": L, "tag": None})

    # 标注赋名
    for t in beam_labels:
        if not win(t["at"]):
            continue
        tag = t["text"].strip().replace("钢梁-", "")
        px, py = t["at"]
        best, bd = None, 1e18
        for s in segs:
            d = _dist_point_seg(px, py, *s["p1"], *s["p2"])
            if d < bd:
                best, bd = s, d
        if best is not None and bd < 2500 and best["tag"] is None:
            best["tag"] = tag

    # 未标注梁段: 继承最近平行已标注梁段
    def angle(s: dict) -> float:
        return math.atan2(s["p2"][1] - s["p1"][1], s["p2"][0] - s["p1"][0]) % math.pi

    tagged = [s for s in segs if s["tag"]]
    for s in segs:
        if s["tag"]:
            continue
        mx = ((s["p1"][0] + s["p2"][0]) / 2, (s["p1"][1] + s["p2"][1]) / 2)
        cands = [t for t in tagged if abs(angle(t) - angle(s)) < 0.05
                 or abs(abs(angle(t) - angle(s)) - math.pi) < 0.05]
        if cands:
            near = min(cands, key=lambda t: _dist_point_seg(mx[0], mx[1], *t["p1"], *t["p2"]))
            if _dist_point_seg(mx[0], mx[1], *near["p1"], *near["p2"]) < 6500:
                s["tag"] = near["tag"] + ""
                s["inherited"] = True

    n_tagged = sum(1 for s in segs if s["tag"])
    log.append(f"钢梁: 线段 {len(segs)} 段, 直接标注 {len([s for s in segs if s['tag'] and not s.get('inherited')])} 段, "
               f"继承编号 {n_tagged - len([s for s in segs if s['tag'] and not s.get('inherited')])} 段, "
               f"未识别 {len(segs) - n_tagged} 段")

    # --- 输出 VCAD 原始图纸 JSON (坐标平移到原点附近) ---
    ox, oy = vx[0], hy[0]
    entities_out: list[dict] = []
    for i, x in enumerate(vx):
        entities_out.append({
            "layer": "AXIS", "type": "line",
            "p1": [x - ox, hy[0] - oy - 1500], "p2": [x - ox, hy[-1] - oy + 1500],
            "label": ax_labels_x.get(x, str(i + 1)),
        })
    for i, y in enumerate(hy):
        entities_out.append({
            "layer": "AXIS", "type": "line",
            "p1": [vx[0] - ox - 1500, y - oy], "p2": [vx[-1] - ox + 1500, y - oy],
            "label": ax_labels_y.get(y, chr(ord("A") + i)),
        })
    for c in columns.values():
        spec = schedule.get(c["tag"], {})
        entities_out.append({
            "layer": "SCOL", "type": "insert",
            "at": [c["at"][0] - ox, c["at"][1] - oy],
            "tag": c["tag"], "top": round(c["top"]),
            "model": spec.get("model", ""), "grade": spec.get("grade", ""),
            "section": spec.get("section", ""), "kg_per_m": spec.get("kg_per_m"),
        })
    for s in segs:
        if not s["tag"]:
            continue
        spec = schedule.get(s["tag"], {})
        mx = ((s["p1"][0] + s["p2"][0]) / 2, (s["p1"][1] + s["p2"][1]) / 2)
        entities_out.append({
            "layer": "SBEAM", "type": "line",
            "p1": [round(s["p1"][0] - ox, 1), round(s["p1"][1] - oy, 1)],
            "p2": [round(s["p2"][0] - ox, 1), round(s["p2"][1] - oy, 1)],
            "tag": s["tag"], "top": round(top_at(*mx)),
            "model": spec.get("model", ""), "grade": spec.get("grade", ""),
            "section": spec.get("section", ""), "kg_per_m": spec.get("kg_per_m"),
            "inherited": bool(s.get("inherited")),
        })

    story = round(default_top)
    return {
        "meta": {
            "name": source_name,
            "desc": "真实 DWG 识别结果: 钢结构平面(钢柱/钢梁/轴网/型号表)",
            "units": "mm", "region": "上海", "kind": "steel",
            "recognition": {"log": log,
                            "schedule": {k: {kk: vv for kk, vv in v.items()
                                             if kk != "section_geom"}
                                         for k, v in schedule.items()}},
        },
        "levels": [{"name": "1F", "elev": 0, "height": story}],
        "entities": entities_out,
    }
