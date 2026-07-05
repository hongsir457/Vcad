"""柱加固识别(PDF 路线)与算量测试。"""

import pytest

from app.core.qto import QtoParams
from app.core.reinforcement_recognizer import (
    angle_kg_per_m,
    assign_table_cells,
    parse_enlarge_table,
    recognize,
)
from app.pipeline.orchestrator import run_to_result


def _t(x, y, s):
    return {"type": "TEXT", "layer": "PDF_OCR", "at": [x, y], "text": s}


def test_angle_kg_per_m():
    # L150x7: A = 7×(300−7) = 2051mm² -> 16.10 kg/m
    assert angle_kg_per_m(150, 7) == pytest.approx(16.10, abs=0.01)


def synth_texts():
    """合成: 相邻的增大截面表与外包钢表 + 平面标注。"""
    ents = [
        _t(50000, 30000, "柱增大截面加固表(首层结构)："),
        _t(50000, 28000, "JKZ2"), _t(56000, 28000, "400x400"),
        _t(60000, 28000, "100"), _t(63000, 28000, "100"),
        _t(66000, 28000, "100"), _t(69000, 28000, "100"),
        _t(73000, 28000, "10@100"), _t(77000, 28000, "25"),
        _t(50000, 27200, "JKZ3"), _t(56000, 27200, "400x400"),
        _t(60000, 27200, "100"), _t(63000, 27200, "按实际"),
        _t(66000, 27200, "100"), _t(69000, 27200, "100"),
        _t(73000, 27200, "10@100"),
        # 外包钢表(与增大截面表相邻)
        _t(95000, 30000, "外包钢法加固柱表"),
        _t(95000, 28000, "编号"), _t(99000, 28000, "JKZ1"),
        _t(95000, 26000, "外包角钢型号"), _t(99000, 26000, "4L150x7"),
    ]
    # 平面标注
    for i, (x, y) in enumerate([(0, 0), (6000, 0), (12000, 0)]):
        ents.append(_t(x, y, "JKZ1"))
    ents.append(_t(0, 6000, "JKZ2"))
    ents.append(_t(6000, 6000, "JKZ3"))
    return ents


def test_adjacent_tables_no_cross_swallow():
    """回归: 外包钢表不得把相邻增大截面表的编号吞进来(最近标题归属)。"""
    texts = synth_texts()
    enl, jak, used = assign_table_cells(texts)
    enl_tags = {t["text"] for t in enl}
    jak_tags = {t["text"] for t in jak}
    assert enl_tags == {"JKZ2", "JKZ3"}
    assert jak_tags == {"JKZ1"}


def test_ocr_fragment_rejected():
    """回归: 分块 OCR 残片("100"→"00")不得挤占增大截面尺寸列。"""
    texts = synth_texts()
    # 在 JKZ2 行的 100 旁边插入残片 "00"
    texts.append(_t(60600, 28000, "00"))
    enl, _, _ = assign_table_cells(texts)
    log: list[str] = []
    table = parse_enlarge_table(enl, texts, log)
    assert table["JKZ2"]["deltas"] == [100.0, 100.0, 100.0, 100.0]


def test_recognize_and_pipeline_hand_calc():
    raw = recognize({"entities": synth_texts()}, "合成加固图")
    rcols = [e for e in raw["entities"] if e["layer"] == "RCOL"]
    assert len(rcols) == 5
    result = run_to_result(raw, QtoParams())

    # JKZ2 增大截面 1 根: ΔA = 0.6×0.6 − 0.16 = 0.20 m²; V = 0.20×4.0 = 0.80 m³
    jkz2 = next(i for i in result["boq"]
                if i["code"].startswith("01B001") and "JKZ2" in " ".join(i["spec"]))
    assert jkz2["qty"] == pytest.approx(0.80)
    # JKZ3 "按实际"按 100 假设 -> 同 0.80 m³, 且假设进入模型问题清单
    assert any("按实际" in s or "假设" in s for s in result["model"]["issues"])
    # JKZ1 外包钢 3 根: 4×16.10×4.0×3 = 772.8 kg = 0.773 t
    jkz1 = next(i for i in result["boq"] if i["code"].startswith("01B002"))
    assert jkz1["qty"] == pytest.approx(0.773, abs=0.001)
    assert jkz1["extra"]["根数"] == 3
    # 计算书含假设标注
    assert any(line["kind"] == "deduct" and "假设" in line["text"]
               for line in jkz1["calc"])


def test_pdf_import_native_path():
    """合成矢量 PDF(真文字): 提取线段/文本并完成比例标定。"""
    fitz = pytest.importorskip("fitz")
    doc = fitz.open()
    page = doc.new_page(width=800, height=600)
    # 三道竖直"轴线"(75pt 间距) + 标注 6300
    for i in range(3):
        x = 100 + i * 75
        page.draw_line(fitz.Point(x, 100), fitz.Point(x, 500))
    page.insert_text(fitz.Point(130, 90), "6300", fontsize=8)
    page.insert_text(fitz.Point(205, 90), "6300", fontsize=8)
    page.insert_text(fitz.Point(120, 300), "JKZ1", fontsize=8)
    data = doc.tobytes()

    from app.core.pdf_import import extract_pdf
    ej = extract_pdf(data, use_ocr=False)
    lines = [e for e in ej["entities"] if e["type"] == "LINE"]
    texts = [e for e in ej["entities"] if e["type"] == "TEXT"]
    assert len(lines) >= 3
    assert any(t["text"] == "JKZ1" for t in texts)
    assert any("比例标定" in line and "6300" in line for line in ej["log"])
    # 标定后轴距应为 6300mm
    xs = sorted({round(e["p1"][0]) for e in lines
                 if abs(e["p1"][0] - e["p2"][0]) < 1})
    assert xs[1] - xs[0] == pytest.approx(6300, rel=0.01)
