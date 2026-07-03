"""后端全链路测试: 识图 -> 建模 -> 算量 -> 评测。"""

import math

import pytest

from app.core.drawing import (
    load_sample,
    parse_door_window_tag,
    parse_drawing,
    parse_section_tag,
    polygon_area,
)
from app.core.qto import QtoParams, round_qty
from app.eval.benchmark import evaluate, match_items, perturb_raw
from app.pipeline.orchestrator import run_pipeline, run_to_result


# ---------------------------------------------------------------------------
# 图纸表达体系解析
# ---------------------------------------------------------------------------

def test_parse_section_tag():
    assert parse_section_tag("KL1 250x500") == ("KL1", 0.25, 0.5)
    assert parse_section_tag("KZ1 400×400") == ("KZ1", 0.4, 0.4)


def test_parse_door_window_tag():
    assert parse_door_window_tag("M1021") == ("door", 1.0, 2.1)
    assert parse_door_window_tag("C1815") == ("window", 1.8, 1.5)
    with pytest.raises(ValueError):
        parse_door_window_tag("X999")


def test_polygon_area():
    assert polygon_area([(0, 0), (2, 0), (2, 3), (0, 3)]) == pytest.approx(6.0)


def test_rounding_follows_gb50500():
    assert round_qty(2.304, "m³") == 2.30
    assert round_qty(2.305, "m³") == 2.31   # 四舍五入而非银行家舍入
    assert round_qty(1.5, "樘") == 2


# ---------------------------------------------------------------------------
# 识图
# ---------------------------------------------------------------------------

def test_parse_drawing_case01():
    dwg = parse_drawing(load_sample("case01_single_room"))
    assert len(dwg.columns) == 4
    assert len(dwg.beams) == 4
    assert len(dwg.walls) == 4
    assert len(dwg.slabs) == 1
    openings = [o for w in dwg.walls for o in w.openings]
    assert {o.tag for o in openings} == {"M1021", "C1815"}
    # 门归属到南墙(y=0)
    south = next(w for w in dwg.walls if w.y1 == 0 and w.y2 == 0)
    assert any(o.kind == "door" for o in south.openings)


def test_snap_absorbs_jitter():
    raw = perturb_raw(load_sample("case01_single_room"), seed=7, jitter_mm=1.0)
    dwg = parse_drawing(raw, snap_tol_mm=5.0)
    xs = sorted({c.x for c in dwg.columns})
    assert xs == [0.0, 6.0]


# ---------------------------------------------------------------------------
# 建模与算量: 与人工手算基准核对
# ---------------------------------------------------------------------------

def boq_qty(result: dict, code9: str, spec_key: str = "") -> float:
    for item in result["boq"]:
        if item["code"].startswith(code9):
            if spec_key and not any(spec_key in s for s in item["spec"]):
                continue
            return item["qty"]
    raise AssertionError(f"清单缺少子目 {code9} {spec_key}")


def test_case01_quantities_match_hand_calc():
    result = run_to_result(load_sample("case01_single_room"), QtoParams())
    assert boq_qty(result, "010502001") == pytest.approx(2.30)   # 柱
    assert boq_qty(result, "010503002") == pytest.approx(1.84)   # 梁(净长, 算至板底)
    assert boq_qty(result, "010505003") == pytest.approx(3.76)   # 板
    assert boq_qty(result, "010402001") == pytest.approx(11.07)  # 墙(扣洞口)
    assert boq_qty(result, "010801001") == pytest.approx(2.10)   # 门
    assert boq_qty(result, "010807001") == pytest.approx(2.70)   # 窗
    assert boq_qty(result, "011702002") == pytest.approx(23.04)  # 柱模板
    assert boq_qty(result, "011702006") == pytest.approx(19.59)  # 梁模板
    assert result["model"]["issues"] == []


def test_case03_small_opening_not_deducted():
    result = run_to_result(load_sample("case03_masonry"), QtoParams())
    assert boq_qty(result, "010401003") == pytest.approx(8.73)
    # 小窗仍单独列项
    assert boq_qty(result, "010807001", "C0505") == pytest.approx(0.25)
    # 无柱无梁 -> 不产生模板措施项目
    assert not any(i["code"].startswith("0117") for i in result["boq"])


def test_calc_trace_is_reviewable():
    """计算书必须含 规则引用 / 公式 / 代入 / 汇总, 模拟手算可复核。"""
    result = run_to_result(load_sample("case01_single_room"), QtoParams())
    wall = next(i for i in result["boq"] if i["code"].startswith("010402001"))
    kinds = {line["kind"] for line in wall["calc"]}
    assert {"rule", "formula", "subst", "deduct", "sum"} <= kinds
    assert any("M1021" in line["text"] for line in wall["calc"])


def test_pipeline_event_stream_order():
    events = list(run_pipeline(load_sample("case01_single_room")))
    types = [e["type"] for e in events]
    assert types.count("phase") == 2
    assert types[-1] == "result"
    # 步骤成对: 每个 step_start 都有对应 step_done
    started = [e["step"] for e in events if e["type"] == "step_start"]
    done = [e["step"] for e in events if e["type"] == "step_done"]
    assert set(started) <= set(done)
    # 事件应可 JSON 序列化
    import json
    json.dumps(events, ensure_ascii=False)


# ---------------------------------------------------------------------------
# 评测体系
# ---------------------------------------------------------------------------

def test_match_items_flags_missing_and_extra():
    gt = [{"code": "010502001", "name": "矩形柱", "unit": "m³", "qty": 1.0}]
    boq = [{"code": "010503002001", "name": "矩形梁", "spec": [], "unit": "m³", "qty": 2.0}]
    rows = match_items(gt, boq)
    assert {r["status"] for r in rows} == {"漏项", "多项"}


def test_full_benchmark_reaches_target():
    report = evaluate(QtoParams(), with_stability=True)
    s = report["summary"]
    assert s["accuracy"] == 1.0, report
    assert s["recall"] == 1.0
    assert s["stability"] == 1.0
    assert s["score"] == 1.0


def test_naive_params_score_lower():
    """朴素参数(不扣减/不吸附)分数应显著更低 —— 迭代循环有生长空间。"""
    naive = QtoParams(snap_tol_mm=0.0, min_deduct_area_m2=0.0,
                      beam_to_column_face=False, beam_height_to_slab_bottom=False)
    report = evaluate(naive, with_stability=False)
    assert report["summary"]["accuracy"] < 0.85
