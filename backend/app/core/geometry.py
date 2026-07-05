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

from .drawing import (
    Beam,
    Column,
    Device,
    Duct,
    Opening,
    ParsedDrawing,
    Pipe,
    SteelBeam,
    SteelColumn,
    Tray,
    Wall,
)

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
# 加固柱建模: 原柱芯 + 增大截面外圈 / 外包角钢
# ---------------------------------------------------------------------------

def build_reinforced_column(rc, elev: float) -> ModelElement:
    """加固柱: 原柱(灰) + 加固层(增大截面=四侧围套; 外包钢=四角角钢)。"""
    H = rc.height
    zm = elev + H / 2
    e = ModelElement(
        eid=rc.eid, category="rcol", tag=rc.tag, level=rc.level,
        params={
            "method": rc.method, "b": rc.b, "h": rc.h, "height": H,
            "db1": rc.db1, "db2": rc.db2, "dh1": rc.dh1, "dh2": rc.dh2,
            "hoop": rc.hoop, "bars": rc.bars,
            "angle_n": rc.angle_n, "angle_b": rc.angle_b, "angle_t": rc.angle_t,
            "angle_spec": rc.angle_spec, "kg_per_m_each": rc.kg_per_m_each,
            "assumed_delta": rc.assumed_delta,
            "delta_area": round((rc.b + rc.db1 + rc.db2) * (rc.h + rc.dh1 + rc.dh2)
                                - rc.b * rc.h, 5),
        },
    )
    # 原柱芯
    e.primitives.append(box(rc.eid, "rcol_core", rc.x, rc.y, zm, rc.b, rc.h, H))

    if rc.method == "增大截面":
        B = rc.b + rc.db1 + rc.db2
        cx = rc.x + (rc.db2 - rc.db1) / 2  # 两侧增量不等时外圈中心偏移
        cy = rc.y + (rc.dh2 - rc.dh1) / 2
        if rc.db1 > EPS:
            e.primitives.append(box(rc.eid, "rcol_jacket",
                                    rc.x - rc.b / 2 - rc.db1 / 2, rc.y, zm,
                                    rc.db1, rc.h, H))
        if rc.db2 > EPS:
            e.primitives.append(box(rc.eid, "rcol_jacket",
                                    rc.x + rc.b / 2 + rc.db2 / 2, rc.y, zm,
                                    rc.db2, rc.h, H))
        if rc.dh1 > EPS:
            e.primitives.append(box(rc.eid, "rcol_jacket",
                                    cx, rc.y - rc.h / 2 - rc.dh1 / 2, zm, B, rc.dh1, H))
        if rc.dh2 > EPS:
            e.primitives.append(box(rc.eid, "rcol_jacket",
                                    cx, rc.y + rc.h / 2 + rc.dh2 / 2, zm, B, rc.dh2, H))
    elif rc.method == "外包钢" and rc.angle_b > 0:
        bb = rc.angle_b * 0.001
        tt = max(rc.angle_t * 0.001, 0.008)
        for sx in (-1, 1):
            for sy in (-1, 1):
                cx = rc.x + sx * rc.b / 2
                cy = rc.y + sy * rc.h / 2
                # 每角两块肢板(L 形)
                e.primitives.append(box(rc.eid, "rcol_steel",
                                        cx - sx * bb / 2, cy + sy * tt / 2,
                                        zm, bb, tt, H))
                e.primitives.append(box(rc.eid, "rcol_steel",
                                        cx + sx * tt / 2, cy - sy * bb / 2,
                                        zm, tt, bb, H))
    return e


# ---------------------------------------------------------------------------
# 安装专业建模: 管道(圆柱) / 风管、桥架(矩形) / 点式器具
# ---------------------------------------------------------------------------

PIPE_DN_RADIUS = {  # 公称直径 -> 外半径近似, m
    "DN15": 0.011, "DN20": 0.013, "DN25": 0.017, "DN32": 0.021,
    "DN40": 0.024, "DN50": 0.030, "DN65": 0.038, "DN80": 0.044,
    "DN100": 0.057, "DN110": 0.055, "DN125": 0.070, "DN150": 0.084,
}


def build_pipe(pipe: Pipe, elev0: float) -> ModelElement:
    r = PIPE_DN_RADIUS.get(pipe.dn.upper(), 0.03)
    z = elev0 + pipe.elev
    e = ModelElement(
        eid=pipe.eid, category="pipe", tag=f"{pipe.system} {pipe.dn}",
        level=pipe.level,
        params={"system": pipe.system, "material": pipe.material, "dn": pipe.dn,
                "length": round(pipe.length, 4), "elev": pipe.elev,
                "segments": [[list(a), list(b)] for a, b in
                             zip(pipe.pts[:-1], pipe.pts[1:])]},
    )
    for (x1, y1), (x2, y2) in zip(pipe.pts[:-1], pipe.pts[1:]):
        e.primitives.append({
            "kind": "cylinder", "eid": pipe.eid, "category": "pipe",
            "system": pipe.system,
            "p1": [round(x1, 5), round(y1, 5), round(z, 5)],
            "p2": [round(x2, 5), round(y2, 5), round(z, 5)],
            "r": r,
        })
    return e


def _run_boxes(e: ModelElement, cat: str, pts, w: float, h: float, z: float) -> None:
    for (x1, y1), (x2, y2) in zip(pts[:-1], pts[1:]):
        L = math.hypot(x2 - x1, y2 - y1)
        if L < EPS:
            continue
        rot = math.atan2(y2 - y1, x2 - x1)
        e.primitives.append(box(e.eid, cat, (x1 + x2) / 2, (y1 + y2) / 2,
                                z, L, w, h, rot))


def build_duct(duct: Duct, elev0: float) -> ModelElement:
    e = ModelElement(
        eid=duct.eid, category="duct",
        tag=f"{duct.system} {int(duct.w * 1000)}×{int(duct.h * 1000)}",
        level=duct.level,
        params={"system": duct.system, "w": duct.w, "h": duct.h,
                "length": round(duct.length, 4), "elev": duct.elev},
    )
    _run_boxes(e, "duct", duct.pts, duct.w, duct.h, elev0 + duct.elev)
    return e


def build_tray(tray: Tray, elev0: float) -> ModelElement:
    e = ModelElement(
        eid=tray.eid, category="tray",
        tag=f"桥架 {int(tray.w * 1000)}×{int(tray.h * 1000)}",
        level=tray.level,
        params={"w": tray.w, "h": tray.h,
                "length": round(tray.length, 4), "elev": tray.elev},
    )
    _run_boxes(e, "tray", tray.pts, tray.w, tray.h, elev0 + tray.elev)
    return e


DEVICE_SIZE = {  # 点式项的三维示意尺寸 (x, y, z), m
    "valve": (0.12, 0.12, 0.12),
    "fixture": (0.55, 0.45, 0.40),
    "air_terminal": (0.30, 0.30, 0.06),
    "luminaire": (0.60, 0.60, 0.05),
    "switch": (0.09, 0.04, 0.09),
    "socket": (0.09, 0.04, 0.09),
}


def build_device(dev: Device, elev0: float) -> ModelElement:
    sx, sy, sz = DEVICE_SIZE.get(dev.kind, (0.2, 0.2, 0.2))
    e = ModelElement(
        eid=dev.eid, category=f"device_{dev.kind}", tag=dev.tag, level=dev.level,
        params={"kind": dev.kind, "tag": dev.tag, "elev": dev.elev,
                "x": dev.x, "y": dev.y},
    )
    e.primitives.append(box(dev.eid, f"device_{dev.kind}", dev.x, dev.y,
                            elev0 + dev.elev + sz / 2, sx, sy, sz))
    return e


# ---------------------------------------------------------------------------
# 钢构件建模: H 型钢 = 上翼缘 + 腹板 + 下翼缘 三块实体
# ---------------------------------------------------------------------------

from .steel_recognizer import STEEL_DENSITY, parse_h_section  # noqa: E402


def _h_profile_boxes(eid: str, cat: str, sec: dict,
                     cx: float, cy: float, z0: float, z1: float,
                     length: float | None = None, rot: float = 0.0,
                     vertical: bool = True) -> list[dict]:
    """生成 H 型钢三块实体。竖直构件沿 z, 水平构件沿局部 x(rot 方位)。"""
    h, b, tw, tf = (sec[k] * 0.001 for k in ("h", "b", "tw", "tf"))
    out = []
    if vertical:
        H = z1 - z0
        zm = (z0 + z1) / 2
        # 翼缘位于截面 y 两侧, 腹板居中 (截面局部: x=b 方向, y=h 方向)
        out.append(box(eid, cat, cx, cy + (h - tf) / 2, zm, b, tf, H, rot))
        out.append(box(eid, cat, cx, cy - (h - tf) / 2, zm, b, tf, H, rot))
        out.append(box(eid, cat, cx, cy, zm, tw, h - 2 * tf, H, rot))
    else:
        L = length or 0.0
        zm_top = z1 - tf / 2
        zm_bot = z1 - h + tf / 2
        zm_web = z1 - h / 2
        out.append(box(eid, cat, cx, cy, zm_top, L, b, tf, rot))
        out.append(box(eid, cat, cx, cy, zm_bot, L, b, tf, rot))
        out.append(box(eid, cat, cx, cy, zm_web, L, tw, h - 2 * tf, rot))
    return out


def build_steel_column(col: SteelColumn, elev: float) -> ModelElement:
    sec = parse_h_section(col.section) or {"h": 300, "b": 300, "tw": 10, "tf": 16}
    H = col.top - elev
    weight = col.kg_per_m * H
    e = ModelElement(
        eid=col.eid, category="steel_column", tag=col.tag, level=col.level,
        params={
            "model": col.model, "grade": col.grade, "section": col.section,
            "kg_per_m": col.kg_per_m, "H": round(H, 4),
            "weight_kg": round(weight, 2), "x": col.x, "y": col.y,
            "sec": sec,
        },
    )
    e.primitives = _h_profile_boxes(col.eid, "steel_column", sec,
                                    col.x, col.y, elev, col.top)
    return e


def build_steel_beam(beam: SteelBeam, elev: float) -> ModelElement:
    sec = parse_h_section(beam.section) or {"h": 300, "b": 150, "tw": 8, "tf": 12}
    L = beam.length
    weight = beam.kg_per_m * L
    rot = math.atan2(beam.y2 - beam.y1, beam.x2 - beam.x1)
    e = ModelElement(
        eid=beam.eid, category="steel_beam", tag=beam.tag, level=beam.level,
        params={
            "model": beam.model, "grade": beam.grade, "section": beam.section,
            "kg_per_m": beam.kg_per_m, "length": round(L, 4),
            "weight_kg": round(weight, 2), "top": beam.top,
            "inherited": beam.inherited, "sec": sec,
        },
    )
    e.primitives = _h_profile_boxes(
        beam.eid, "steel_beam", sec,
        (beam.x1 + beam.x2) / 2, (beam.y1 + beam.y2) / 2,
        0.0, beam.top, length=L, rot=rot, vertical=False)
    return e


def steel_model_volume(e: ModelElement) -> float:
    """钢构件三维实体体积(m³), 用于双算复核(体积×密度 vs 米重×长度)。"""
    v = 0.0
    for p in e.primitives:
        v += p["size"][0] * p["size"][1] * p["size"][2]
    return v


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

    # 5. 钢梁端部应有支承(钢柱或另一根钢梁)
    import math as _m

    def _near_seg(px, py, sb: SteelBeam, tol: float) -> bool:
        dx, dy = sb.x2 - sb.x1, sb.y2 - sb.y1
        l2 = dx * dx + dy * dy
        if l2 == 0:
            return False
        t = max(0.0, min(1.0, ((px - sb.x1) * dx + (py - sb.y1) * dy) / l2))
        return _m.hypot(px - (sb.x1 + t * dx), py - (sb.y1 + t * dy)) < tol

    for sb in dwg.steel_beams:
        for px, py, side in ((sb.x1, sb.y1, "起点"), (sb.x2, sb.y2, "终点")):
            ok = any(_m.hypot(px - c.x, py - c.y) < 0.45 for c in dwg.steel_columns) or \
                 any(o is not sb and _near_seg(px, py, o, 0.25) for o in dwg.steel_beams)
            if not ok:
                issues.append(f"钢梁 {sb.eid}({sb.tag}) {side}无支承构件, 需人工复核")

    # 6. 缺规格的钢构件
    for sc in dwg.steel_columns:
        if sc.kg_per_m <= 0:
            issues.append(f"钢柱 {sc.eid}({sc.tag}) 未匹配到型号表规格")
    for sb in dwg.steel_beams:
        if sb.kg_per_m <= 0:
            issues.append(f"钢梁 {sb.eid}({sb.tag}) 未匹配到型号表规格")

    return issues
