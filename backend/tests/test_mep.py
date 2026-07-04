"""全专业(给排水/暖通/电气)建模与算量测试。"""

import pytest

from app.core.drawing import load_sample, parse_drawing
from app.core.qto import QtoParams
from app.eval.benchmark import evaluate
from app.pipeline.orchestrator import run_to_result


def boq(result, code9, spec_key=""):
    for item in result["boq"]:
        if item["code"].startswith(code9):
            if spec_key and not any(spec_key in s for s in item["spec"]):
                continue
            return item
    raise AssertionError(f"缺少清单子目 {code9} {spec_key}")


def test_parse_mep_entities():
    dwg = parse_drawing(load_sample("case04_full_mep"))
    assert len(dwg.pipes) == 2
    assert len(dwg.ducts) == 1
    assert len(dwg.trays) == 1
    assert len(dwg.devices) == 13  # 阀1 + 器具2 + 风口2 + 灯4 + 开关1 + 插座3
    # 精确分类
    kinds = {}
    for d in dwg.devices:
        kinds[d.kind] = kinds.get(d.kind, 0) + 1
    assert kinds == {"valve": 1, "fixture": 2, "air_terminal": 2,
                     "luminaire": 4, "switch": 1, "socket": 3}


def test_mep_quantities_match_hand_calc():
    result = run_to_result(load_sample("case04_full_mep"), QtoParams())
    # 给排水
    assert boq(result, "031001001")["qty"] == pytest.approx(5.25)   # 给水管 3.0+2.25
    assert boq(result, "031001006")["qty"] == pytest.approx(5.00)   # 排水管 2.0+3.0
    assert boq(result, "031003001")["qty"] == 1
    assert boq(result, "031004003")["qty"] == 1
    assert boq(result, "031004006")["qty"] == 1
    # 暖通: S = 2×(0.4+0.25)×5.0
    assert boq(result, "030902001")["qty"] == pytest.approx(6.50)
    assert boq(result, "030903013")["qty"] == 2
    # 电气
    assert boq(result, "030411003")["qty"] == pytest.approx(5.00)
    assert boq(result, "030412001")["qty"] == 4
    assert boq(result, "030404034")["qty"] == 1
    assert boq(result, "030404035")["qty"] == 3
    # 结构部分不受安装叠加影响(同 case01)
    assert boq(result, "010502001")["qty"] == pytest.approx(2.30)
    assert boq(result, "010402001")["qty"] == pytest.approx(11.07)


def test_discipline_labels():
    result = run_to_result(load_sample("case04_full_mep"), QtoParams())
    disc = {i["code"][:9]: i["discipline"] for i in result["boq"]}
    assert disc["010502001"] == "结构"
    assert disc["010402001"] == "建筑"
    assert disc["031001001"] == "给排水"
    assert disc["030902001"] == "暖通"
    assert disc["030411003"] == "电气"


def test_mep_model_primitives():
    result = run_to_result(load_sample("case04_full_mep"), QtoParams())
    cats = {e["category"] for e in result["model"]["elements"]}
    assert {"pipe", "duct", "tray", "device_luminaire", "device_fixture"} <= cats
    pipes = [e for e in result["model"]["elements"] if e["category"] == "pipe"]
    # 管道图元为圆柱
    assert all(p["kind"] == "cylinder" for e in pipes for p in e["primitives"])


def test_full_benchmark_with_mep_case():
    report = evaluate(QtoParams(), with_stability=True)
    assert report["summary"]["case_count"] == 4
    assert report["summary"]["accuracy"] == 1.0, report["summary"]
    assert report["summary"]["stability"] == 1.0
