"""三维几何建模核心。

把识图得到的结构化构件(ParsedDrawing)升维成三维实体模型(BuildingModel):

- 柱: 楼面 -> 上层楼面 的长方体
- 梁: 与板整浇, 板底 -> 梁底 的长方体, 梁长算至柱侧面(净长)
- 墙: 楼面 -> 梁底(无梁时 -> 板底) 的长方体, 按洞口切分为若干实体块
- 板: 板边界多边形拉伸
- 门窗: 洞口内的薄板(用于三维表达)

所有净长/扣减在建模阶段确定并记录在构件参数里,
算量阶段直接引用建模结果 —— 即"算量基于三维模型"。

输出的图元(primitive)为前端 three.js 可直接消费的参数化描述:
  {"kind": "box", "center": [x,y,z], "size": [l,w,h], "rot": θz}
  {"kind": "extrude", "points": [[x,y]...], "z0": z0, "z1": z1}
坐标系: x/y 为平面坐标, z 向上, 单位 m。
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

from .drawing import Beam, Column, Opening, ParsedDrawing, Wall

EPS = 1e-6


@dataclass
class ModelElement:
    eid: str
    category: str            # column | beam | wall | slab | door | window
    tag: str
    level: str
    params: dict             # 建模参数: 尺寸/净长/扣减明细等, 供算量引用
    primitives: list[dict] = field(default_factory=list)


@dataclass
class BuildingModel:
    drawing_name: str
    region: str
    elements: list[ModelElement] = field(default_factory=list)
    issues: list[str] = field(default_factory=list)   # 模型自检问题

    def by_category(self, cat: str) -> list[ModelElement]:
        return [e for e in self.elements if e.category == cat]

    def to_dict(self) -> dict:
        return {
            "drawing_name": self.drawing_name,
            "region": self.region,
            "issues": self.issues,
            "elements": [
                {
                    "eid": e.eid, "category": e.category, "tag": e.tag,
                    "level": e.level, "params": e.params, "primitives": e.primitives,
                }
                for e in self.elements
            ],
        }


def box(eid: str, cat: str, cx: float, cy: float, cz: float,
        sx: float, sy: float, sz: float, rot: float = 0.0) -> dict:
    return {
        "kind": "box", "eid": eid, "category": cat,
        "center": [round(cx, 5), round(cy, 5), round(cz, 5)],
        "size": [round(sx, 5), round(sy, 5), round(sz, 5)],
        "rot": round(rot, 6),
    }


# ---------------------------------------------------------------------------
# 净长计算: 墙/梁端头与柱相交时, 算至柱侧面
# ---------------------------------------------------------------------------

def _column_contains(col: Column, px: float, py: float, margin: float) -> bool:
    return (abs(px - col.x) <= col.b / 2 + margin and
            abs(py - col.y) <= col.h / 2 + margin)


def _trim_to_column_face(col: Column, px: float, py: float,
                         ux: float, uy: float) -> tuple[float, float, float]:
    """端点 (px,py) 在柱内, 沿方向 (ux,uy) 前进, 返回柱侧面出口点及裁掉的长度。"""
    ts = []
    if abs(ux) > EPS:
        for face_x in (col.x - col.b / 2, col.x + col.b / 2):
            t = (face_x - px) / ux
            if t > EPS:
                ts.append(t)
    if abs(uy) > EPS:
        for face_y in (col.y - col.h / 2, col.y + col.h / 2):
            t = (face_y - py) / uy
            if t > EPS:
                ts.append(t)
    if not ts:
        return px, py, 0.0
    t = min(ts)
    return px + ux * t, py + uy * t, t


def net_segment(x1: float, y1: float, x2: float, y2: float,
                columns: list[Column], margin: float = 1e-4
                ) -> tuple[tuple[float, float, float, float], list[dict]]:
    """把线状构件两端裁至柱侧面, 返回净段坐标与扣减明细。"""
    length = math.hypot(x2 - x1, y2 - y1)
    if length < EPS:
        return (x1, y1, x2, y2), []
    ux, uy = (x2 - x1) / length, (y2 - y1) / length
    deductions: list[dict] = []

    for col in columns:
        if _column_contains(col, x1, y1, margin):
            nx, ny, cut = _trim_to_column_face(col, x1, y1, ux, uy)
            if cut > EPS:
                x1, y1 = nx, ny
                deductions.append({"at": "start", "column": col.tag, "cut": round(cut, 5)})
            break
    for col in columns:
        if _column_contains(col, x2, y2, margin):
            nx, ny, cut = _trim_to_column_face(col, x2, y2, -ux, -uy)
            if cut > EPS:
                x2, y2 = nx, ny
                deductions.append({"at": "end", "column": col.tag, "cut": round(cut, 5)})
            break
    return (x1, y1, x2, y2), deductions


# ---------------------------------------------------------------------------
# 构件建模
# ---------------------------------------------------------------------------

def build_column(col: Column, elev: float, story_h: float) -> ModelElement:
    e = ModelElement(
        eid=col.eid, category="column", tag=col.tag, level=col.level,
        params={"b": col.b, "h": col.h, "H": story_h, "x": col.x, "y": col.y},
    )
    e.primitives.append(box(col.eid, "column", col.x, col.y,
                            elev + story_h / 2, col.b, col.h, story_h))
    return e


def build_beam(beam: Beam, elev: float, story_h: float, slab_t: float,
               columns: list[Column]) -> ModelElement:
    (x1, y1, x2, y2), ded = net_segment(beam.x1, beam.y1, beam.x2, beam.y2, columns)
    net_len = math.hypot(x2 - x1, y2 - y1)
    web_h = beam.h - slab_t          # 与板整浇: 梁高算至板底
    z_top = elev + story_h - slab_t  # 板底标高
    rot = math.atan2(y2 - y1, x2 - x1)
    e = ModelElement(
        eid=beam.eid, category="beam", tag=beam.tag, level=beam.level,
        params={
            "b": beam.b, "h": beam.h, "web_h": round(web_h, 5),
            "gross_len": round(beam.length, 5), "net_len": round(net_len, 5),
            "slab_t": slab_t, "deductions": ded,
        },
    )
    e.primitives.append(box(beam.eid, "beam", (x1 + x2) / 2, (y1 + y2) / 2,
                            z_top - web_h / 2, net_len, beam.b, web_h, rot))
    return e


def build_wall(wall: Wall, elev: float, wall_h: float,
               columns: list[Column]) -> ModelElement:
    """墙体建模: 净长裁剪 + 洞口切分。

    wall_h: 墙净高(楼面至梁底/板底)。洞口按沿墙一维位置切分墙体为实体块,
    形成真实的三维开洞效果。
    """
    (x1, y1, x2, y2), ded = net_segment(wall.x1, wall.y1, wall.x2, wall.y2, columns)
    net_len = math.hypot(x2 - x1, y2 - y1)
    rot = math.atan2(y2 - y1, x2 - x1)
    ux, uy = math.cos(rot), math.sin(rot)
    # 净段起点相对原段起点的偏移(洞口 offset 以原段起点计)
    start_shift = math.hypot(x1 - wall.x1, y1 - wall.y1)

    e = ModelElement(
        eid=wall.eid, category="wall", tag=wall.material, level=wall.level,
        params={
            "thickness": wall.thickness, "height": round(wall_h, 5),
            "gross_len": round(wall.length, 5), "net_len": round(net_len, 5),
            "material": wall.material, "deductions": ded,
            "openings": [
                {"eid": o.eid, "tag": o.tag, "kind": o.kind, "w": o.width,
                 "h": o.height, "sill": o.sill}
                for o in wall.openings
            ],
        },
    )

    def seg_box(t0: float, t1: float, z0: float, z1: float) -> None:
        if t1 - t0 < EPS or z1 - z0 < EPS:
            return
        tm = (t0 + t1) / 2
        e.primitives.append(box(
            wall.eid, "wall",
            x1 + ux * tm, y1 + uy * tm, elev + (z0 + z1) / 2,
            t1 - t0, wall.thickness, z1 - z0, rot,
        ))

    ops = sorted(wall.openings, key=lambda o: o.offset)
    cursor = 0.0
    for o in ops:
        o0 = max(0.0, o.offset - start_shift - o.width / 2)
        o1 = min(net_len, o.offset - start_shift + o.width / 2)
        seg_box(cursor, o0, 0.0, wall_h)                       # 洞口左侧整段
        seg_box(o0, o1, 0.0, min(o.sill, wall_h))              # 窗台下
        seg_box(o0, o1, min(o.sill + o.height, wall_h), wall_h)  # 洞口上
        cursor = o1
    seg_box(cursor, net_len, 0.0, wall_h)

    return e


def build_opening_panel(wall: Wall, op: Opening, elev: float,
                        columns: list[Column]) -> ModelElement:
    """门窗本体: 洞口内的薄板, 用于三维表达与门窗清单。"""
    (x1, y1, x2, y2), _ = net_segment(wall.x1, wall.y1, wall.x2, wall.y2, columns)
    rot = math.atan2(y2 - y1, x2 - x1)
    ux, uy = math.cos(rot), math.sin(rot)
    start_shift = math.hypot(x1 - wall.x1, y1 - wall.y1)
    t = op.offset - start_shift
    e = ModelElement(
        eid=op.eid, category=op.kind, tag=op.tag, level=wall.level,
        params={"w": op.width, "h": op.height, "sill": op.sill,
                "wall": wall.eid, "area": round(op.width * op.height, 5)},
    )
    e.primitives.append(box(
        op.eid, op.kind,
        x1 + ux * t, y1 + uy * t, elev + op.sill + op.height / 2,
        op.width, wall.thickness * 0.35, op.height, rot,
    ))
    return e


def build_slab(slab, elev: float, story_h: float) -> ModelElement:
    z_top = elev + story_h
    e = ModelElement(
        eid=slab.eid, category="slab", tag=f"h={int(round(slab.thickness / 0.001))}",
        level=slab.level,
        params={"thickness": slab.thickness, "area": round(slab.area, 5),
                "polygon": [[round(x, 5), round(y, 5)] for x, y in slab.polygon]},
    )
    e.primitives.append({
        "kind": "extrude", "eid": slab.eid, "category": "slab",
        "points": [[round(x, 5), round(y, 5)] for x, y in slab.polygon],
        "z0": round(z_top - slab.thickness, 5), "z1": round(z_top, 5),
    })
    return e


# ---------------------------------------------------------------------------
# 模型自检
# ---------------------------------------------------------------------------

def check_model(model: BuildingModel, dwg: ParsedDrawing) -> list[str]:
    issues: list[str] = []

    # 1. 洞口必须完全位于其归属墙段内
    for wall in dwg.walls:
        for op in wall.openings:
            if op.offset - op.width / 2 < -EPS or op.offset + op.width / 2 > wall.length + EPS:
                issues.append(f"洞口 {op.tag} 超出墙段 {wall.eid} 范围")

    # 2. 柱不应互相重叠
    cols = dwg.columns
    for i in range(len(cols)):
        for j in range(i + 1, len(cols)):
            a, b_ = cols[i], cols[j]
            if (abs(a.x - b_.x) < (a.b + b_.b) / 2 - EPS and
                    abs(a.y - b_.y) < (a.h + b_.h) / 2 - EPS):
                issues.append(f"柱 {a.eid} 与 {b_.eid} 平面重叠")

    # 3. 梁端宜有竖向支承(柱)
    for beam in dwg.beams:
        for (px, py, tag) in [(beam.x1, beam.y1, "起点"), (beam.x2, beam.y2, "终点")]:
            if cols and not any(_column_contains(c, px, py, 1e-4) for c in cols):
                issues.append(f"梁 {beam.eid}({beam.tag}) {tag}悬空(无柱支承)")

    # 4. 板厚应小于层高
    for slab in dwg.slabs:
        lv = dwg.level(slab.level)
        if slab.thickness >= lv.height:
            issues.append(f"板 {slab.eid} 厚度异常: {slab.thickness}m")

    return issues
