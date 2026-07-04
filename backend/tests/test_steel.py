"""钢结构识别与算量测试。"""

import math

import pytest

from app.core.qto import QtoParams
from app.core.steel_recognizer import (
    model_kg_per_m,
    parse_h_section,
    parse_schedule,
    recognize,
)
from app.pipeline.orchestrator import run_to_result


def test_parse_h_section():
    sec = parse_h_section("H256.2x146.0x6.4x10.9")
    assert sec["h"] == 256.2 and sec["b"] == 146.0
    # 理论米重应接近型号名义值 37 kg/m
    assert sec["kg_per_m_calc"] == pytest.approx(37.0, rel=0.03)


def test_model_kg_per_m():
    assert model_kg_per_m("250UB37") == 37.0
    assert model_kg_per_m("310UC118") == 118.0
    assert model_kg_per_m("150PFC") is None  # 无名义米重后缀


def _text(x, y, s):
    return {"type": "TEXT", "layer": "TEXT", "at": [x, y], "text": s, "height": 350}


def test_schedule_dense_rows_no_cross_match():
    """回归: 行距小于 500 时不得串行(此前用固定行高容差会把相邻行配错)。"""
    ents = [
        _text(0, 0, "钢材型号表"),
        _text(0, -800, "编号"), _text(6000, -800, "型号"),
        _text(11000, -800, "钢材等级"), _text(16000, -800, "截面尺寸"),
        # 两行仅相距 420
        _text(0, -1600, "钢柱-C1"), _text(6000, -1600, "200UC46"),
        _text(11000, -1600, "BHP Gr350"), _text(16000, -1600, "H203.4x203.0x7.3x11.0"),
        _text(0, -2020, "钢柱-C2"), _text(6000, -2020, "250UC73"),
        _text(11000, -2020, "BHP Gr350"), _text(16000, -2020, "H253.8x254.0x8.6x14.2"),
    ]
    log: list[str] = []
    sched = parse_schedule(ents, log)
    assert sched["C1"]["model"] == "200UC46"
    assert sched["C1"]["section"] == "H203.4x203.0x7.3x11.0"
    assert sched["C2"]["model"] == "250UC73"
    assert sched["C2"]["section"] == "H253.8x254.0x8.6x14.2"
    assert sched["C1"]["kg_per_m"] == 46.0


def test_schedule_rejects_mismatched_section():
    """型号与截面理论米重偏差 >15% 时判为串行, 丢截面保名义米重。"""
    ents = [
        _text(0, 0, "钢材型号表"),
        _text(0, -800, "编号"), _text(6000, -800, "型号"), _text(16000, -800, "截面尺寸"),
        _text(0, -1600, "钢梁-B2"), _text(6000, -1600, "250UB37"),
        _text(16000, -1600, "H602.0x228.0x10.6x14.8"),  # 这是 610UB101 的截面
    ]
    log: list[str] = []
    sched = parse_schedule(ents, log)
    assert sched["B2"]["kg_per_m"] == 37.0
    assert sched["B2"]["section"] == ""      # 截面被判为不可信
    assert any("不匹配" in line for line in log)


# ---------------------------------------------------------------------------
# 合成钢结构图纸 -> 全链路
# ---------------------------------------------------------------------------

def steel_raw() -> dict:
    """两根钢柱 + 一根钢梁的最小钢结构图纸(手算基准见断言)。"""
    return {
        "meta": {"name": "合成钢结构测试图", "units": "mm", "region": "上海", "kind": "steel"},
        "levels": [{"name": "1F", "elev": 0, "height": 4000}],
        "entities": [
            {"layer": "AXIS", "type": "line", "p1": [0, -500], "p2": [0, 1000], "label": "1"},
            {"layer": "AXIS", "type": "line", "p1": [6000, -500], "p2": [6000, 1000], "label": "2"},
            {"layer": "AXIS", "type": "line", "p1": [-500, 0], "p2": [6500, 0], "label": "A"},
            {"layer": "SCOL", "type": "insert", "at": [0, 0], "tag": "C1", "top": 4000,
             "model": "200UC46", "grade": "BHP Gr350",
             "section": "H203.4x203.0x7.3x11.0", "kg_per_m": 46.0},
            {"layer": "SCOL", "type": "insert", "at": [6000, 0], "tag": "C1", "top": 4000,
             "model": "200UC46", "grade": "BHP Gr350",
             "section": "H203.4x203.0x7.3x11.0", "kg_per_m": 46.0},
            {"layer": "SBEAM", "type": "line", "p1": [0, 0], "p2": [6000, 0], "tag": "B1",
             "top": 4000, "model": "250UB31", "grade": "BHP Gr350",
             "section": "H251.6x146.0x6.1x8.6", "kg_per_m": 31.0},
        ],
    }


def test_steel_pipeline_tonnage_matches_hand_calc():
    result = run_to_result(steel_raw(), QtoParams())
    col = next(i for i in result["boq"] if i["code"].startswith("010603001"))
    beam = next(i for i in result["boq"] if i["code"].startswith("010604001"))
    # 手算: 柱 2 根 × 4.0m × 46kg/m = 368kg = 0.368t
    assert col["qty"] == pytest.approx(0.368)
    assert col["extra"]["根数"] == 2
    # 手算: 梁 6.0m × 31kg/m = 186kg = 0.186t
    assert beam["qty"] == pytest.approx(0.186)
    # 双算复核: H 截面理论质量 vs 名义米重, 偏差 < 5%
    steel_checks = [c for c in result["checks"] if "钢" in c["item"]]
    assert steel_checks and all(c["pass"] for c in steel_checks)
    # 计算书含规则引用与米重来源
    assert any("以质量计算" in line["text"] for line in col["calc"])


def test_steel_model_has_h_profiles():
    result = run_to_result(steel_raw(), QtoParams())
    cols = [e for e in result["model"]["elements"] if e["category"] == "steel_column"]
    assert len(cols) == 2
    assert all(len(e["primitives"]) == 3 for e in cols)  # 两翼缘 + 腹板


# ---------------------------------------------------------------------------
# 合成"真实图纸"实体流 -> 识别器
# ---------------------------------------------------------------------------

def test_recognize_synthetic_plan():
    """模拟真实图纸结构: 轴网 + 交点柱标注 + 梁多段线 + 型号表。"""
    ents = [
        _text(50000, 50000, "钢材型号表"),
        _text(50000, 49200, "编号"), _text(56000, 49200, "型号"),
        _text(61000, 49200, "钢材等级"), _text(66000, 49200, "截面尺寸"),
        _text(50000, 48400, "钢柱-C1"), _text(56000, 48400, "200UC46"),
        _text(61000, 48400, "BHP Gr350"), _text(66000, 48400, "H203.4x203.0x7.3x11.0"),
        _text(50000, 47600, "钢梁-B1"), _text(56000, 47600, "250UB31"),
        _text(61000, 47600, "BHP Gr350"), _text(66000, 47600, "H251.6x146.0x6.1x8.6"),
    ]
    # 轴网 2×2
    for x, lab in ((0, "1"), (6000, "2")):
        ents.append({"type": "LINE", "layer": "AXIS", "p1": [x, -1000], "p2": [x, 7000]})
        ents.append(_text(x, 7600, lab))
    for y, lab in ((0, "A"), (6000, "B")):
        ents.append({"type": "LINE", "layer": "AXIS", "p1": [-1000, y], "p2": [7000, y]})
        ents.append(_text(-1800, y, lab))
    # 柱标注(带引出偏移)
    for x, y in ((0, 0), (6000, 0), (0, 6000), (6000, 6000)):
        ents.append(_text(x + 350, y + 350, "钢柱-C1"))
    # 梁多段线(一条被标注, 一条平行未标注)
    ents.append({"type": "LWPOLYLINE", "layer": "STPM_SBEAM_THICK",
                 "pts": [[0, 0], [6000, 0]], "closed": False, "width": 50})
    ents.append({"type": "LWPOLYLINE", "layer": "STPM_SBEAM_THICK",
                 "pts": [[0, 6000], [6000, 6000]], "closed": False, "width": 50})
    ents.append(_text(3000, 400, "钢梁-B1"))
    ents.append(_text(3000, 3000, "此区域结构板顶标高为4.220"))

    raw = recognize({"entities": ents}, "合成平面")
    scols = [e for e in raw["entities"] if e["layer"] == "SCOL"]
    sbeams = [e for e in raw["entities"] if e["layer"] == "SBEAM"]
    assert len(scols) == 4
    assert all(c["kg_per_m"] == 46.0 for c in scols)
    assert len(sbeams) == 2                      # 未标注梁继承平行梁编号
    assert all(b["tag"] == "B1" for b in sbeams)
    assert any(b.get("inherited") for b in sbeams)
    assert scols[0]["top"] == 4220

    # 识别结果可进入全链路
    result = run_to_result(raw, QtoParams())
    beam_item = next(i for i in result["boq"] if i["code"].startswith("010604001"))
    assert beam_item["qty"] == pytest.approx(12.0 * 31.0 / 1000)
