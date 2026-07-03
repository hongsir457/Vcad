"""二维图纸数据模型与"识图"解析。

原始图纸(raw drawing)模拟从 DWG/PDF 解析出来的分层实体流:
按图层(AXIS/COL/BEAM/WALL/DOOR/WIN/SLAB)组织的线、块、多段线、填充,
构件信息通过图面标注(tag)表达, 遵循中国施工图表示习惯:

- 柱:   块参照 "KZ1 400x400"           -> 截面 400mm x 400mm
- 梁:   梁线 + 集中标注 "KL1 250x500"   -> 截面宽x高
- 门:   "M1021"  -> 洞口宽 1000mm, 高 2100mm
- 窗:   "C1815"  -> 洞口宽 1800mm, 高 1500mm, sill 为窗台高
- 板:   填充区域 + "h=120"              -> 板厚 120mm

parse_drawing() 把原始实体流"识别"为结构化构件模型(ParsedDrawing),
这一过程即流水线中"读图/识图"步骤的实现。
"""

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass, field
from pathlib import Path

MM = 0.001  # mm -> m

SAMPLES_DIR = Path(__file__).resolve().parent.parent / "data" / "samples"


# ---------------------------------------------------------------------------
# 结构化构件模型(识图结果)
# ---------------------------------------------------------------------------

@dataclass
class Level:
    name: str
    elevation: float  # m
    height: float     # 层高, m


@dataclass
class GridLine:
    axis: str    # "x" | "y"
    label: str
    offset: float  # m


@dataclass
class Column:
    eid: str
    tag: str          # KZ1
    x: float
    y: float
    b: float          # 截面 x 向尺寸, m
    h: float          # 截面 y 向尺寸, m
    level: str = "1F"


@dataclass
class Beam:
    eid: str
    tag: str          # KL1
    x1: float
    y1: float
    x2: float
    y2: float
    b: float          # 梁宽, m
    h: float          # 梁高(含板厚), m
    level: str = "1F"

    @property
    def length(self) -> float:
        return math.hypot(self.x2 - self.x1, self.y2 - self.y1)


@dataclass
class Opening:
    eid: str
    tag: str          # M1021 / C1815
    kind: str         # "door" | "window"
    width: float      # m
    height: float     # m
    sill: float       # 窗台高/门槛高, m
    offset: float     # 洞口中心距墙段起点的距离, m


@dataclass
class Wall:
    eid: str
    x1: float
    y1: float
    x2: float
    y2: float
    thickness: float  # m
    material: str     # "砌块" | "砖"
    level: str = "1F"
    openings: list[Opening] = field(default_factory=list)

    @property
    def length(self) -> float:
        return math.hypot(self.x2 - self.x1, self.y2 - self.y1)


@dataclass
class Slab:
    eid: str
    polygon: list[tuple[float, float]]  # 板边界(外边线), m
    thickness: float                    # m
    level: str = "1F"

    @property
    def area(self) -> float:
        return polygon_area(self.polygon)


@dataclass
class ParsedDrawing:
    name: str
    region: str
    levels: list[Level]
    grids: list[GridLine]
    columns: list[Column]
    beams: list[Beam]
    walls: list[Wall]
    slabs: list[Slab]

    def level(self, name: str) -> Level:
        for lv in self.levels:
            if lv.name == name:
                return lv
        raise KeyError(name)


# ---------------------------------------------------------------------------
# 几何工具
# ---------------------------------------------------------------------------

def polygon_area(pts: list[tuple[float, float]]) -> float:
    """鞋带公式, 返回绝对面积(m²)。"""
    n = len(pts)
    s = 0.0
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        s += x1 * y2 - x2 * y1
    return abs(s) / 2.0


# ---------------------------------------------------------------------------
# 图面标注解析(图纸表达体系)
# ---------------------------------------------------------------------------

_SECTION_RE = re.compile(r"(\d+)\s*[xX×]\s*(\d+)")
_DOOR_WIN_RE = re.compile(r"^([MC])(\d{2})(\d{2})$")


def parse_section_tag(tag: str) -> tuple[str, float, float]:
    """解析 "KL1 250x500" -> ("KL1", 0.25, 0.50)。"""
    m = _SECTION_RE.search(tag)
    if not m:
        raise ValueError(f"无法从标注解析截面尺寸: {tag!r}")
    name = tag[: m.start()].strip() or tag
    return name, int(m.group(1)) * MM, int(m.group(2)) * MM


def parse_door_window_tag(tag: str) -> tuple[str, float, float]:
    """解析门窗编号 M1021 -> ("door", 1.0, 2.1); C1815 -> ("window", 1.8, 1.5)。

    编号规则: 首位字母 M=门 C=窗, 其后四位数字为 洞口宽(dm) + 洞口高(dm)。
    """
    m = _DOOR_WIN_RE.match(tag.strip())
    if not m:
        raise ValueError(f"无法识别门窗编号: {tag!r}")
    kind = "door" if m.group(1) == "M" else "window"
    return kind, int(m.group(2)) * 0.1, int(m.group(3)) * 0.1


# ---------------------------------------------------------------------------
# 识图: 原始实体流 -> 结构化构件
# ---------------------------------------------------------------------------

def snap(value: float, tol: float) -> float:
    """把坐标吸附到 tol 的整数倍, 消除图纸绘制误差。"""
    if tol <= 0:
        return value
    return round(value / tol) * tol


def parse_drawing(raw: dict, snap_tol_mm: float = 5.0) -> ParsedDrawing:
    """把原始图纸实体流识别为结构化构件模型。

    snap_tol_mm: 坐标吸附容差(mm), 是评测循环可调参数之一。
    """
    tol = snap_tol_mm * MM

    def pt(p: list[float]) -> tuple[float, float]:
        return snap(p[0] * MM, tol), snap(p[1] * MM, tol)

    meta = raw.get("meta", {})
    levels = [
        Level(lv["name"], lv.get("elev", 0) * MM, lv["height"] * MM)
        for lv in raw.get("levels", [{"name": "1F", "elev": 0, "height": 3000}])
    ]

    grids: list[GridLine] = []
    columns: list[Column] = []
    beams: list[Beam] = []
    walls: list[Wall] = []
    slabs: list[Slab] = []
    pending_openings: list[tuple[Opening, tuple[float, float]]] = []

    counters: dict[str, int] = {}

    def next_eid(prefix: str) -> str:
        counters[prefix] = counters.get(prefix, 0) + 1
        return f"{prefix}-{counters[prefix]}"

    for ent in raw.get("entities", []):
        layer = ent.get("layer", "").upper()
        level = ent.get("level", "1F")

        if layer == "AXIS":
            (x1, y1), (x2, y2) = pt(ent["p1"]), pt(ent["p2"])
            axis = "y" if abs(y2 - y1) < abs(x2 - x1) else "x"
            offset = y1 if axis == "y" else x1
            grids.append(GridLine(axis=axis, label=ent.get("label", "?"), offset=offset))

        elif layer == "COL":
            name, b, h = parse_section_tag(ent["tag"])
            x, y = pt(ent["at"])
            columns.append(Column(next_eid("KZ"), name, x, y, b, h, level))

        elif layer == "BEAM":
            name, b, h = parse_section_tag(ent["tag"])
            (x1, y1), (x2, y2) = pt(ent["p1"]), pt(ent["p2"])
            beams.append(Beam(next_eid("KL"), name, x1, y1, x2, y2, b, h, level))

        elif layer == "WALL":
            (x1, y1), (x2, y2) = pt(ent["p1"]), pt(ent["p2"])
            walls.append(Wall(
                next_eid("Q"), x1, y1, x2, y2,
                thickness=ent["width"] * MM,
                material=ent.get("tag", "砌块"),
                level=level,
            ))

        elif layer in ("DOOR", "WIN"):
            kind, w, h = parse_door_window_tag(ent["tag"])
            op = Opening(
                eid=next_eid("M" if kind == "door" else "C"),
                tag=ent["tag"].strip(), kind=kind, width=w, height=h,
                sill=ent.get("sill", 0) * MM, offset=0.0,
            )
            pending_openings.append((op, pt(ent["at"])))

        elif layer == "SLAB":
            th_m = re.search(r"h\s*=\s*(\d+)", ent.get("tag", "h=100"))
            slabs.append(Slab(
                eid=next_eid("B"),
                polygon=[pt(p) for p in ent["pts"]],
                thickness=(int(th_m.group(1)) if th_m else 100) * MM,
                level=level,
            ))

    # 门窗归属: 找插入点最近的墙段, 并计算沿墙偏移
    for op, (ox, oy) in pending_openings:
        best: Wall | None = None
        best_d = 1e9
        best_t = 0.0
        for w in walls:
            d, t = _point_to_segment(ox, oy, w.x1, w.y1, w.x2, w.y2)
            if d < best_d:
                best, best_d, best_t = w, d, t
        if best is None or best_d > 0.5:
            raise ValueError(f"门窗 {op.tag} 找不到归属墙体 (最近距离 {best_d:.3f}m)")
        op.offset = best_t
        best.openings.append(op)

    return ParsedDrawing(
        name=meta.get("name", "未命名图纸"),
        region=meta.get("region", "全国"),
        levels=levels,
        grids=sorted(grids, key=lambda g: (g.axis, g.offset)),
        columns=columns,
        beams=beams,
        walls=walls,
        slabs=slabs,
    )


def _point_to_segment(px, py, x1, y1, x2, y2) -> tuple[float, float]:
    """返回 (点到线段距离, 投影点距线段起点的弧长)。"""
    dx, dy = x2 - x1, y2 - y1
    seg_len2 = dx * dx + dy * dy
    if seg_len2 == 0:
        return math.hypot(px - x1, py - y1), 0.0
    t = max(0.0, min(1.0, ((px - x1) * dx + (py - y1) * dy) / seg_len2))
    cx, cy = x1 + t * dx, y1 + t * dy
    return math.hypot(px - cx, py - cy), t * math.sqrt(seg_len2)


# ---------------------------------------------------------------------------
# 样例图纸加载
# ---------------------------------------------------------------------------

def list_samples() -> list[dict]:
    out = []
    for p in sorted(SAMPLES_DIR.glob("*.json")):
        raw = json.loads(p.read_text(encoding="utf-8"))
        out.append({
            "id": p.stem,
            "name": raw.get("meta", {}).get("name", p.stem),
            "region": raw.get("meta", {}).get("region", ""),
            "desc": raw.get("meta", {}).get("desc", ""),
        })
    return out


def load_sample(sample_id: str) -> dict:
    p = SAMPLES_DIR / f"{sample_id}.json"
    if not p.exists():
        raise FileNotFoundError(f"图纸不存在: {sample_id}")
    return json.loads(p.read_text(encoding="utf-8"))
