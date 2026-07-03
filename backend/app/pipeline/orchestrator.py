"""流水线编排器: 建模步骤链 + 算量步骤链, 以事件流形式输出。

事件协议(SSE 每条 data 为一个 JSON):
  {"type": "phase",      "phase": "modeling"|"qto", "title": ...}
  {"type": "step_start", "step": id, "title": ...}
  {"type": "step_log",   "step": id, "text": ...}
  {"type": "step_done",  "step": id, "summary": ..., "payload": {...}?}
  {"type": "result",     "payload": {model, boq, checks, ...}}
  {"type": "error",      "message": ...}

步骤链刻意模拟人工过程:
  建模 = 识图 -> 轴网 -> 楼层 -> 柱 -> 梁 -> 墙 -> 开洞 -> 板 -> 自检
  算量 = 定规范 -> 列项 -> 逐项列式 -> 扣减 -> 汇总取舍 -> 编清单 -> 双算复核
"""

from __future__ import annotations

from collections.abc import Iterator

from ..core import qto as qto_mod
from ..core.drawing import ParsedDrawing, parse_drawing
from ..core.geometry import (
    BuildingModel,
    build_beam,
    build_column,
    build_opening_panel,
    build_slab,
    build_wall,
    check_model,
)
from ..core.qto import BoqItem, QtoParams, _CodeSeq, cross_check

Event = dict


def _start(step: str, title: str) -> Event:
    return {"type": "step_start", "step": step, "title": title}


def _log(step: str, text: str) -> Event:
    return {"type": "step_log", "step": step, "text": text}


def _done(step: str, summary: str, payload: dict | None = None) -> Event:
    ev: Event = {"type": "step_done", "step": step, "summary": summary}
    if payload is not None:
        ev["payload"] = payload
    return ev


# ---------------------------------------------------------------------------
# 建模步骤链
# ---------------------------------------------------------------------------

def modeling_steps(raw: dict, params: QtoParams) -> Iterator[Event]:
    """执行建模, 最后一个事件的 payload 携带 (dwg, model) 引用。"""
    yield {"type": "phase", "phase": "modeling", "title": "第一阶段 · 三维建模(模拟人工建模)"}

    # M1 识图
    yield _start("m1", "读图识图: 解析图层、标注与构件表达")
    dwg = parse_drawing(raw, snap_tol_mm=params.snap_tol_mm)
    yield _log("m1", f"图纸: {dwg.name} (地区: {dwg.region})")
    yield _log("m1", f"识别实体: 轴线 {len(dwg.grids)}, 柱 {len(dwg.columns)}, "
                     f"梁 {len(dwg.beams)}, 墙 {len(dwg.walls)}, 板 {len(dwg.slabs)}, "
                     f"门窗 {sum(len(w.openings) for w in dwg.walls)}")
    for c in dwg.columns[:1]:
        yield _log("m1", f"示例: 柱标注 “{c.tag}” 识别为截面 "
                         f"{int(c.b * 1000)}×{int(c.h * 1000)}")
    yield _done("m1", f"识图完成, 共 {len(dwg.columns) + len(dwg.beams) + len(dwg.walls) + len(dwg.slabs)} 个构件")

    # M2 轴网
    yield _start("m2", "建立轴网")
    xs = [g for g in dwg.grids if g.axis == "x"]
    ys = [g for g in dwg.grids if g.axis == "y"]
    yield _log("m2", "横向轴线: " + ", ".join(f"{g.label}@{g.offset:g}m" for g in xs))
    yield _log("m2", "纵向轴线: " + ", ".join(f"{g.label}@{g.offset:g}m" for g in ys))
    yield _done("m2", f"轴网 {len(xs)} × {len(ys)} 建立完成")

    # M3 楼层标高
    yield _start("m3", "建立楼层标高体系")
    for lv in dwg.levels:
        yield _log("m3", f"{lv.name}: 标高 {lv.elevation:+.3f}m, 层高 {lv.height:g}m")
    yield _done("m3", f"共 {len(dwg.levels)} 个楼层")

    model = BuildingModel(drawing_name=dwg.name, region=dwg.region)
    lv = dwg.levels[0]

    # M4 柱
    yield _start("m4", "布置竖向构件: 柱")
    for col in dwg.columns:
        model.elements.append(build_column(col, lv.elevation, lv.height))
    if dwg.columns:
        yield _log("m4", f"柱高取层高 {lv.height:g}m (楼面至上层楼面)")
    yield _done("m4", f"布置柱 {len(dwg.columns)} 根")

    # M5 梁
    yield _start("m5", "布置水平构件: 梁(净长算至柱侧面)")
    slab_t = dwg.slabs[0].thickness if dwg.slabs else 0.0
    n_trim = 0
    for beam in dwg.beams:
        e = build_beam(beam, lv.elevation, lv.height, slab_t, dwg.columns)
        model.elements.append(e)
        if e.params["deductions"]:
            n_trim += 1
            cut = sum(d["cut"] for d in e.params["deductions"])
            yield _log("m5", f"{e.eid}({beam.tag}): 原长 {e.params['gross_len']:g}m, "
                             f"扣柱侧 {cut:g}m, 净长 {e.params['net_len']:g}m")
    yield _done("m5", f"布置梁 {len(dwg.beams)} 根, 其中 {n_trim} 根做了柱侧裁剪")

    # M6 墙
    yield _start("m6", "布置墙体(净高 = 楼面至梁底/板底)")
    has_beam_over: dict[str, float] = {}
    for beam in dwg.beams:
        has_beam_over["*"] = max(has_beam_over.get("*", 0.0), beam.h)
    beam_h = has_beam_over.get("*", 0.0)
    wall_h = lv.height - (beam_h if beam_h > 0 else slab_t)
    yield _log("m6", f"墙净高 = 层高 {lv.height:g} − "
                     f"{'梁高 ' + format(beam_h, 'g') if beam_h > 0 else '板厚 ' + format(slab_t, 'g')} "
                     f"= {wall_h:g}m")
    for wall in dwg.walls:
        model.elements.append(build_wall(wall, lv.elevation, wall_h, dwg.columns))
    yield _done("m6", f"布置墙 {len(dwg.walls)} 段")

    # M7 开洞
    yield _start("m7", "墙体开洞并放置门窗")
    n_op = 0
    for wall in dwg.walls:
        for op in wall.openings:
            model.elements.append(build_opening_panel(wall, op, lv.elevation, dwg.columns))
            n_op += 1
            yield _log("m7", f"{op.tag} ({'门' if op.kind == 'door' else '窗'} "
                             f"{op.width:g}×{op.height:g}m, 窗台 {op.sill:g}m) -> 墙 {wall.eid}")
    yield _done("m7", f"开洞 {n_op} 处")

    # M8 板
    yield _start("m8", "布置楼板")
    for slab in dwg.slabs:
        e = build_slab(slab, lv.elevation, lv.height)
        model.elements.append(e)
        yield _log("m8", f"{e.eid}: 板厚 {slab.thickness:g}m, 面积 {slab.area:.2f}m²")
    yield _done("m8", f"布置板 {len(dwg.slabs)} 块")

    # M9 自检
    yield _start("m9", "模型自检(悬空/重叠/洞口越界)")
    issues = check_model(model, dwg)
    model.issues = issues
    for it in issues:
        yield _log("m9", "⚠ " + it)
    yield _done("m9",
                "自检通过, 未发现问题" if not issues else f"发现 {len(issues)} 个问题, 已标记待复核",
                payload={"issues": issues})

    yield _done("modeling", f"三维模型完成: {len(model.elements)} 个构件实体",
                payload={"_dwg": dwg, "_model": model})


# ---------------------------------------------------------------------------
# 算量步骤链
# ---------------------------------------------------------------------------

def qto_steps(dwg: ParsedDrawing, model: BuildingModel,
              params: QtoParams) -> Iterator[Event]:
    yield {"type": "phase", "phase": "qto", "title": "第二阶段 · 工程量计算(模拟手算)"}

    # Q1 确定规范
    yield _start("q1", "确定清单规范与计量口径")
    yield _log("q1", "计价规范: GB50500-2013《建设工程工程量清单计价规范》")
    yield _log("q1", "计算规范: GB50854-2013《房屋建筑与装饰工程工程量计算规范》")
    yield _log("q1", f"项目地区: {model.region}; 有效位数: m³/m² 两位小数, 樘取整")
    yield _log("q1", f"扣减口径: 洞口单孔 > {params.min_deduct_area_m2:g}m² 扣除; "
                     f"梁长{'算至柱侧面' if params.beam_to_column_face else '按轴线'}")
    yield _done("q1", "计量口径确定")

    seq = _CodeSeq()
    items: list[BoqItem] = []

    # Q2 列项
    yield _start("q2", "列项: 按构件归并清单子目")
    plan: list[tuple[str, str]] = []
    if model.by_category("column"):
        plan.append(("010502001", "矩形柱"))
    if model.by_category("beam"):
        plan.append(("010503002", "矩形梁"))
    if model.by_category("slab"):
        plan.append(("010505003", "平板"))
    mats = {("砖" if "砖" in e.params["material"] else "砌块")
            for e in model.by_category("wall")}
    for m in sorted(mats):
        code = qto_mod.WALL_MATERIAL_CODE[m]
        plan.append((code, qto_mod.RULES[code]["name"]))
    if model.by_category("door"):
        plan.append(("010801001", "木质门"))
    if model.by_category("window"):
        plan.append(("010807001", "金属(塑钢)窗"))
    if model.by_category("column"):
        plan.append(("011702002", "矩形柱模板(措施)"))
    if model.by_category("beam"):
        plan.append(("011702006", "矩形梁模板(措施)"))
    for code, name in plan:
        yield _log("q2", f"{code} {name}")
    yield _done("q2", f"共列 {len(plan)} 个清单子目")

    # Q3~Q5 逐项列式计算
    calcs = [
        ("q3", "混凝土构件: 柱、梁、板 逐项列式",
         [qto_mod.calc_columns, qto_mod.calc_beams, qto_mod.calc_slabs]),
        ("q4", "砌体墙: 列式并做洞口/嵌入构件扣减",
         [qto_mod.calc_walls]),
        ("q5", "门窗与措施项目(模板)",
         [qto_mod.calc_doors_windows, qto_mod.calc_formwork]),
    ]
    for step_id, title, fns in calcs:
        yield _start(step_id, title)
        for fn in fns:
            for item in fn(model, seq, params):
                items.append(item)
                yield _log(step_id, f"{item.code} {item.name}: {item.qty:g} {item.unit}")
        yield _done(step_id, "列式完成")

    # Q6 汇总编清单
    yield _start("q6", "汇总取舍, 编制工程量清单表")
    items.sort(key=lambda i: i.code)
    yield _log("q6", f"清单条目 {len(items)} 条, 编码按 GB50500 十二位编码规则编排")
    yield _done("q6", "清单编制完成")

    # Q7 复核
    yield _start("q7", "复核: 三维模型体积双算对比")
    checks = cross_check(model, items)
    for c in checks:
        mark = "✓" if c["pass"] else "✗"
        yield _log("q7", f"{mark} {c['item']}: 几何 {c['geometry']:g} vs 清单 {c['boq']:g}, "
                         f"偏差 {c['deviation'] * 100:.2f}%")
    n_fail = sum(1 for c in checks if not c["pass"])
    yield _done("q7",
                "双算复核通过" if n_fail == 0 else f"{n_fail} 项复核超差, 需人工介入",
                payload={"checks": checks})

    yield _done("qto", f"工程量清单完成, 共 {len(items)} 条",
                payload={"_items": items, "_checks": checks})


# ---------------------------------------------------------------------------
# 完整流水线
# ---------------------------------------------------------------------------

def run_pipeline(raw: dict, params: QtoParams | None = None) -> Iterator[Event]:
    """完整执行: 建模 -> 算量 -> 输出最终结果事件。"""
    params = params or QtoParams()
    dwg: ParsedDrawing | None = None
    model: BuildingModel | None = None
    items: list[BoqItem] = []
    checks: list[dict] = []

    for ev in modeling_steps(raw, params):
        payload = ev.get("payload") or {}
        if "_model" in payload:
            dwg, model = payload.pop("_dwg"), payload.pop("_model")
            ev["payload"] = {"element_count": len(model.elements)}
        yield ev

    assert dwg is not None and model is not None

    for ev in qto_steps(dwg, model, params):
        payload = ev.get("payload") or {}
        if "_items" in payload:
            items = payload.pop("_items")
            checks = payload.pop("_checks", checks)
            ev["payload"] = {"item_count": len(items)}
        elif "checks" in payload:
            checks = payload["checks"]
        yield ev

    yield {
        "type": "result",
        "payload": {
            "model": model.to_dict(),
            "boq": [i.to_dict() for i in items],
            "checks": checks,
            "params": params.to_dict(),
        },
    }


def run_to_result(raw: dict, params: QtoParams | None = None) -> dict:
    """非流式执行, 返回最终结果(供评测使用)。"""
    result: dict = {}
    for ev in run_pipeline(raw, params):
        if ev["type"] == "result":
            result = ev["payload"]
    return result
