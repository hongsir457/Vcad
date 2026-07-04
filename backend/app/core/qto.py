"""工程量清单计算(QTO)与清单规则库。

模拟造价员手算过程: 列项 -> 逐项列式 -> 代入数值 -> 扣减 -> 汇总取舍,
每一步都生成可复核的计算书行(calc trace)。

清单编码与计算规则依据《建设工程工程量清单计价规范》GB50500-2013
及《房屋建筑与装饰工程工程量计算规范》GB50854-2013(简化实现):

- 010401003 实心砖墙   m³  按体积, 扣除 >0.3m² 门窗洞口及嵌入墙内的柱梁
- 010402001 砌块墙     m³  同上
- 010502001 矩形柱     m³  按断面面积 × 柱高(楼面至上层楼面)
- 010503002 矩形梁     m³  梁长: 梁与柱连接时算至柱侧面; 与板整浇时梁高算至板底
- 010505003 平板       m³  按板体积, 不扣除柱、垛所占体积
- 010801001 木质门     m²  按洞口面积(同时给出樘数)
- 010807001 金属(塑钢)窗 m² 按洞口面积(同时给出樘数)
- 011702002 矩形柱模板 m²  按模板与构件接触面积(周长 × 柱高)
- 011702006 矩形梁模板 m²  (梁底宽 + 2 × 梁侧净高) × 净长

工程量有效位数(GB50500): m³/m²/m 保留两位小数, 樘/个取整数, t 保留三位。
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from decimal import ROUND_HALF_UP, Decimal

from .geometry import BuildingModel, ModelElement

# ---------------------------------------------------------------------------
# 可调参数(评测迭代循环的调参对象)
# ---------------------------------------------------------------------------

@dataclass
class QtoParams:
    snap_tol_mm: float = 5.0          # 识图坐标吸附容差
    min_deduct_area_m2: float = 0.3   # 墙体洞口扣减的最小单孔面积
    beam_to_column_face: bool = True  # 梁长是否算至柱侧面
    beam_height_to_slab_bottom: bool = True  # 整浇梁高是否算至板底

    def to_dict(self) -> dict:
        return {
            "snap_tol_mm": self.snap_tol_mm,
            "min_deduct_area_m2": self.min_deduct_area_m2,
            "beam_to_column_face": self.beam_to_column_face,
            "beam_height_to_slab_bottom": self.beam_height_to_slab_bottom,
        }

    @classmethod
    def from_dict(cls, d: dict) -> "QtoParams":
        p = cls()
        for k, v in d.items():
            if hasattr(p, k):
                setattr(p, k, v)
        return p


# ---------------------------------------------------------------------------
# 清单规则库
# ---------------------------------------------------------------------------

RULES: dict[str, dict] = {
    "010401003": {"name": "实心砖墙", "unit": "m³",
                  "rule": "按设计图示尺寸以体积计算, 扣除单个面积>0.3m²的门窗洞口, 扣除嵌入墙内的钢筋混凝土柱、梁所占体积"},
    "010402001": {"name": "砌块墙", "unit": "m³",
                  "rule": "按设计图示尺寸以体积计算, 扣除单个面积>0.3m²的门窗洞口, 扣除嵌入墙内的钢筋混凝土柱、梁所占体积"},
    "010502001": {"name": "矩形柱", "unit": "m³",
                  "rule": "按设计图示断面面积乘以柱高以体积计算, 柱高自楼面算至上层楼面"},
    "010503002": {"name": "矩形梁", "unit": "m³",
                  "rule": "按设计图示尺寸以体积计算; 梁与柱连接时梁长算至柱侧面, 梁与板整浇时梁高算至板底"},
    "010505003": {"name": "平板", "unit": "m³",
                  "rule": "按设计图示尺寸以体积计算, 不扣除柱、垛及单孔面积≤0.3m²的孔洞所占体积"},
    "010801001": {"name": "木质门", "unit": "m²",
                  "rule": "按设计图示洞口尺寸以面积计算(附樘数)"},
    "010807001": {"name": "金属(塑钢)窗", "unit": "m²",
                  "rule": "按设计图示洞口尺寸以面积计算(附樘数)"},
    "010603001": {"name": "实腹钢柱", "unit": "t",
                  "rule": "按设计图示尺寸以质量计算, 不扣除孔眼的质量, 焊条、铆钉、螺栓等不另增加质量"},
    "010604001": {"name": "钢梁", "unit": "t",
                  "rule": "按设计图示尺寸以质量计算, 不扣除孔眼的质量, 焊条、铆钉、螺栓等不另增加质量"},
    "011702002": {"name": "矩形柱模板", "unit": "m²",
                  "rule": "按模板与现浇混凝土构件的接触面积计算: 断面周长 × 柱高"},
    "011702006": {"name": "矩形梁模板", "unit": "m²",
                  "rule": "按模板与现浇混凝土构件的接触面积计算: (梁底宽 + 2×梁侧净高) × 梁净长"},
    # ---- 安装工程 GB50856-2013 (简化实现) ----
    "031001001": {"name": "镀锌钢管", "unit": "m",
                  "rule": "按设计图示管道中心线以长度计算, 不扣除阀门、管件及附件所占长度"},
    "031001006": {"name": "塑料管", "unit": "m",
                  "rule": "按设计图示管道中心线以长度计算, 不扣除阀门、管件及附件所占长度"},
    "031003001": {"name": "螺纹阀门", "unit": "个", "rule": "按设计图示数量计算"},
    "031004003": {"name": "洗脸盆", "unit": "组", "rule": "按设计图示数量计算(成组安装)"},
    "031004006": {"name": "大便器", "unit": "组", "rule": "按设计图示数量计算(成组安装)"},
    "030902001": {"name": "碳钢通风管道(矩形)", "unit": "m²",
                  "rule": "按设计图示内径尺寸以展开面积计算: S = 2×(宽+高)×长, 不扣除检查孔、测定孔、送风口、吸风口等所占面积"},
    "030903013": {"name": "风口、散流器", "unit": "个", "rule": "按设计图示数量计算"},
    "030411003": {"name": "电缆桥架", "unit": "m", "rule": "按设计图示中心线以长度计算"},
    "030412001": {"name": "普通灯具", "unit": "套", "rule": "按设计图示数量计算"},
    "030404034": {"name": "照明开关", "unit": "个", "rule": "按设计图示数量计算"},
    "030404035": {"name": "插座", "unit": "个", "rule": "按设计图示数量计算"},
}

PIPE_MATERIAL_CODE = {"镀锌钢管": "031001001", "钢管": "031001001",
                      "UPVC": "031001006", "PPR": "031001006", "塑料": "031001006"}

DEVICE_CODE = {
    "valve": "031003001",
    "air_terminal": "030903013",
    "luminaire": "030412001",
    "switch": "030404034",
    "socket": "030404035",
}
FIXTURE_CODE = {"洗脸盆": "031004003", "大便器": "031004006", "坐便器": "031004006"}

WALL_MATERIAL_CODE = {"砖": "010401003", "砌块": "010402001"}

ROUNDING = {"m³": 2, "m²": 2, "m": 2, "樘": 0, "t": 3, "个": 0, "套": 0, "组": 0}


def round_qty(value: float, unit: str) -> float:
    digits = ROUNDING.get(unit, 2)
    q = Decimal(str(value)).quantize(Decimal("1." + "0" * digits) if digits else Decimal("1"),
                                     rounding=ROUND_HALF_UP)
    return float(q)


# ---------------------------------------------------------------------------
# 清单条目与计算书
# ---------------------------------------------------------------------------

@dataclass
class BoqItem:
    code: str            # 12 位清单编码 = 9位子目 + 3位顺序码
    name: str
    spec: list[str]      # 项目特征
    unit: str
    qty: float
    calc: list[dict] = field(default_factory=list)   # 计算书行
    elements: list[str] = field(default_factory=list)
    extra: dict = field(default_factory=dict)        # 附加量(如门窗樘数)
    discipline: str = "结构"                          # 专业: 结构/建筑/给排水/暖通/电气

    def to_dict(self) -> dict:
        return {
            "code": self.code, "name": self.name, "spec": self.spec,
            "unit": self.unit, "qty": self.qty, "calc": self.calc,
            "elements": self.elements, "extra": self.extra,
            "discipline": self.discipline,
        }


def _line(kind: str, text: str) -> dict:
    return {"kind": kind, "text": text}


def _f(v: float, nd: int = 3) -> str:
    """公式代入用的数值格式: 去掉多余的尾零。"""
    s = f"{v:.{nd}f}".rstrip("0").rstrip(".")
    return s if s else "0"


class _CodeSeq:
    """同一 9 位子目下按项目特征分列时的顺序码分配器。"""

    def __init__(self) -> None:
        self._n: dict[str, int] = {}

    def next(self, code9: str) -> str:
        self._n[code9] = self._n.get(code9, 0) + 1
        return f"{code9}{self._n[code9]:03d}"


# ---------------------------------------------------------------------------
# 分部计算: 每个函数 = 一类构件的手算过程
# ---------------------------------------------------------------------------

def calc_columns(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    items: list[BoqItem] = []
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.by_category("column"):
        groups.setdefault((e.tag, e.params["b"], e.params["h"], e.params["H"]), []).append(e)

    for (tag, b, h, H), els in sorted(groups.items()):
        rule = RULES["010502001"]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "V = b × h × H × n"),
        ]
        v_each = b * h * H
        calc.append(_line("subst",
                          f"单根: {_f(b)} × {_f(h)} × {_f(H)} = {_f(v_each, 4)} m³"))
        total = v_each * len(els)
        calc.append(_line("sum",
                          f"{tag} 共 {len(els)} 根: {_f(v_each, 4)} × {len(els)} = {_f(total, 4)} m³"))
        qty = round_qty(total, "m³")
        calc.append(_line("result", f"工程量取 {qty:.2f} m³"))
        items.append(BoqItem(
            code=seq.next("010502001"), name=rule["name"],
            spec=[f"截面 {int(b * 1000)}×{int(h * 1000)}", f"柱高 {_f(H)}m", "C30 商品混凝土"],
            unit="m³", qty=qty, calc=calc, elements=[e.eid for e in els],
        ))
    return items


def calc_beams(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    items: list[BoqItem] = []
    # 按截面归并列项(同截面不同编号合为一个清单子目, 项目特征一致)
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.by_category("beam"):
        groups.setdefault((e.params["b"], e.params["h"]), []).append(e)

    for (b, h), els in sorted(groups.items()):
        rule = RULES["010503002"]
        slab_t = els[0].params["slab_t"]
        web_h = h - slab_t if params.beam_height_to_slab_bottom else h
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
        ]
        if params.beam_height_to_slab_bottom and slab_t > 0:
            calc.append(_line("deduct",
                              f"与板整浇, 梁计算高 = {_f(h)} − 板厚 {_f(slab_t)} = {_f(web_h)} m"))
        calc.append(_line("formula", "V = Σ( 净长 L × b × 计算高 )"))
        total = 0.0
        for e in els:
            L = e.params["net_len"] if params.beam_to_column_face else e.params["gross_len"]
            ded = e.params["deductions"]
            note = ""
            if params.beam_to_column_face and ded:
                cut = sum(d["cut"] for d in ded)
                note = f" (原长 {_f(e.params['gross_len'])} 扣柱侧 {_f(cut)})"
            v = L * b * web_h
            total += v
            calc.append(_line("subst",
                              f"{e.eid}({e.tag}): {_f(L)}{note} × {_f(b)} × {_f(web_h)} = {_f(v, 4)} m³"))
        qty = round_qty(total, "m³")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m³, 工程量取 {qty:.2f} m³"))
        items.append(BoqItem(
            code=seq.next("010503002"), name=rule["name"],
            spec=[f"截面 {int(b * 1000)}×{int(h * 1000)}", "C30 商品混凝土", "与板整浇"],
            unit="m³", qty=qty, calc=calc, elements=[e.eid for e in els],
        ))
    return items


def calc_slabs(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    items: list[BoqItem] = []
    groups: dict[float, list[ModelElement]] = {}
    for e in model.by_category("slab"):
        groups.setdefault(e.params["thickness"], []).append(e)

    for t, els in sorted(groups.items()):
        rule = RULES["010505003"]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "V = 板面积 A × 板厚 t"),
        ]
        total = 0.0
        for e in els:
            v = e.params["area"] * t
            total += v
            calc.append(_line("subst",
                              f"{e.eid}: A = {_f(e.params['area'])} m², "
                              f"{_f(e.params['area'])} × {_f(t)} = {_f(v, 4)} m³"))
        qty = round_qty(total, "m³")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m³, 工程量取 {qty:.2f} m³"))
        items.append(BoqItem(
            code=seq.next("010505003"), name=rule["name"],
            spec=[f"板厚 {int(t * 1000)}mm", "C30 商品混凝土"],
            unit="m³", qty=qty, calc=calc, elements=[e.eid for e in els],
        ))
    return items


def calc_walls(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    items: list[BoqItem] = []
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.by_category("wall"):
        mat = "砖" if "砖" in e.params["material"] else "砌块"
        groups.setdefault((mat, e.params["thickness"]), []).append(e)

    for (mat, t), els in sorted(groups.items()):
        code9 = WALL_MATERIAL_CODE[mat]
        rule = RULES[code9]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "V = Σ( 净长 L × 净高 H × 墙厚 t ) − Σ扣减"),
        ]
        gross = 0.0
        deduct = 0.0
        for e in els:
            L, H = e.params["net_len"], e.params["height"]
            ded = e.params["deductions"]
            note = ""
            if ded:
                cut = sum(d["cut"] for d in ded)
                note = f" (扣柱侧 {_f(cut)})"
            v = L * H * t
            gross += v
            calc.append(_line("subst",
                              f"{e.eid}: {_f(L)}{note} × {_f(H)} × {_f(t)} = {_f(v, 4)} m³"))
        for e in els:
            for op in e.params["openings"]:
                area = op["w"] * op["h"]
                if area > params.min_deduct_area_m2:
                    dv = area * t
                    deduct += dv
                    calc.append(_line("deduct",
                                      f"扣 {e.eid} 洞口 {op['tag']}: "
                                      f"{_f(op['w'])} × {_f(op['h'])} × {_f(t)} = {_f(dv, 4)} m³"))
                else:
                    calc.append(_line("deduct",
                                      f"洞口 {op['tag']} 面积 {_f(area)} m² ≤ "
                                      f"{_f(params.min_deduct_area_m2)} m², 不扣除"))
        total = gross - deduct
        qty = round_qty(total, "m³")
        calc.append(_line("sum",
                          f"小计 {_f(gross, 4)} − 扣减 {_f(deduct, 4)} = {_f(total, 4)} m³, "
                          f"工程量取 {qty:.2f} m³"))
        items.append(BoqItem(
            code=seq.next(code9), name=rule["name"],
            spec=[f"墙厚 {int(t * 1000)}mm", f"{mat}砌体", "M5.0 混合砂浆砌筑"],
            unit="m³", qty=qty, calc=calc, elements=[e.eid for e in els],
            discipline="建筑",
        ))
    return items


def calc_doors_windows(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    items: list[BoqItem] = []
    for cat, code9 in (("door", "010801001"), ("window", "010807001")):
        groups: dict[str, list[ModelElement]] = {}
        for e in model.by_category(cat):
            groups.setdefault(e.tag, []).append(e)
        for tag, els in sorted(groups.items()):
            rule = RULES[code9]
            w, h = els[0].params["w"], els[0].params["h"]
            calc = [
                _line("rule", f"计算规则: {rule['rule']}"),
                _line("formula", "S = 洞口宽 × 洞口高 × 樘数"),
                _line("subst",
                      f"{tag}: {_f(w)} × {_f(h)} × {len(els)} 樘 = {_f(w * h * len(els), 4)} m²"),
            ]
            qty = round_qty(w * h * len(els), "m²")
            calc.append(_line("result", f"工程量取 {qty:.2f} m², 共 {len(els)} 樘"))
            items.append(BoqItem(
                code=seq.next(code9), name=rule["name"],
                spec=[f"洞口尺寸 {int(w * 1000)}×{int(h * 1000)}", f"编号 {tag}"],
                unit="m²", qty=qty, calc=calc,
                elements=[e.eid for e in els], extra={"樘数": len(els)},
                discipline="建筑",
            ))
    return items


def calc_formwork(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    """措施项目: 柱、梁模板。"""
    items: list[BoqItem] = []

    cols = model.by_category("column")
    if cols:
        rule = RULES["011702002"]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "S = 2 × (b + h) × H × n"),
        ]
        total = 0.0
        groups: dict[tuple, list[ModelElement]] = {}
        for e in cols:
            groups.setdefault((e.params["b"], e.params["h"], e.params["H"]), []).append(e)
        for (b, h, H), els in sorted(groups.items()):
            s = 2 * (b + h) * H * len(els)
            total += s
            calc.append(_line("subst",
                              f"2 × ({_f(b)} + {_f(h)}) × {_f(H)} × {len(els)} 根 = {_f(s, 4)} m²"))
        qty = round_qty(total, "m²")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m², 工程量取 {qty:.2f} m²"))
        items.append(BoqItem(
            code=seq.next("011702002"), name=rule["name"], spec=["复合模板、钢支撑"],
            unit="m²", qty=qty, calc=calc, elements=[e.eid for e in cols],
        ))

    beams = model.by_category("beam")
    if beams:
        rule = RULES["011702006"]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "S = Σ( (b + 2 × 梁侧净高) × 净长 )"),
        ]
        total = 0.0
        for e in beams:
            b, web_h, L = e.params["b"], e.params["web_h"], e.params["net_len"]
            s = (b + 2 * web_h) * L
            total += s
            calc.append(_line("subst",
                              f"{e.eid}: ({_f(b)} + 2×{_f(web_h)}) × {_f(L)} = {_f(s, 4)} m²"))
        qty = round_qty(total, "m²")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m², 工程量取 {qty:.2f} m²"))
        items.append(BoqItem(
            code=seq.next("011702006"), name=rule["name"], spec=["复合模板、钢支撑"],
            unit="m²", qty=qty, calc=calc, elements=[e.eid for e in beams],
        ))
    return items


def calc_pipes(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    """给排水管道: 按系统+材质+管径分列, 中心线长度计量。"""
    items: list[BoqItem] = []
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.by_category("pipe"):
        groups.setdefault((e.params["system"], e.params["material"], e.params["dn"]),
                          []).append(e)

    for (system, material, dn), els in sorted(groups.items()):
        code9 = "031001001"
        for key, code in PIPE_MATERIAL_CODE.items():
            if key in material:
                code9 = code
                break
        rule = RULES[code9]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "L = Σ 各段中心线长"),
        ]
        total = 0.0
        for e in els:
            for (x1, y1), (x2, y2) in e.params["segments"]:
                seg = math.hypot(x2 - x1, y2 - y1)
                total += seg
                calc.append(_line("subst",
                                  f"{e.eid}: ({_f(x1)},{_f(y1)}) → ({_f(x2)},{_f(y2)}) = {_f(seg)} m"))
        qty = round_qty(total, "m")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m, 工程量取 {qty:.2f} m"))
        items.append(BoqItem(
            code=seq.next(code9), name=rule["name"],
            spec=[f"系统: {system}", f"材质 {material}", f"规格 {dn}", "螺纹/热熔连接"],
            unit="m", qty=qty, calc=calc, elements=[e.eid for e in els],
            discipline="给排水",
        ))
    return items


def calc_ducts(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    """通风管道: 按断面分列, 展开面积计量 S = 2(w+h)L。"""
    items: list[BoqItem] = []
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.by_category("duct"):
        groups.setdefault((e.params["w"], e.params["h"], e.params["system"]), []).append(e)

    for (w, h, system), els in sorted(groups.items()):
        rule = RULES["030902001"]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "S = 2 × (宽 + 高) × L"),
        ]
        total = 0.0
        for e in els:
            L = e.params["length"]
            s = 2 * (w + h) * L
            total += s
            calc.append(_line("subst",
                              f"{e.eid}: 2 × ({_f(w)} + {_f(h)}) × {_f(L)} = {_f(s, 4)} m²"))
        qty = round_qty(total, "m²")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m², 工程量取 {qty:.2f} m²"))
        items.append(BoqItem(
            code=seq.next("030902001"), name=rule["name"],
            spec=[f"系统: {system}", f"断面 {int(w * 1000)}×{int(h * 1000)}",
                  "镀锌钢板 δ=0.6", "咬口连接"],
            unit="m²", qty=qty, calc=calc, elements=[e.eid for e in els],
            discipline="暖通",
        ))
    return items


def calc_trays(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    items: list[BoqItem] = []
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.by_category("tray"):
        groups.setdefault((e.params["w"], e.params["h"]), []).append(e)

    for (w, h), els in sorted(groups.items()):
        rule = RULES["030411003"]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("formula", "L = Σ 各段中心线长"),
        ]
        total = 0.0
        for e in els:
            total += e.params["length"]
            calc.append(_line("subst", f"{e.eid}: {_f(e.params['length'])} m"))
        qty = round_qty(total, "m")
        calc.append(_line("sum", f"合计 {_f(total, 4)} m, 工程量取 {qty:.2f} m"))
        items.append(BoqItem(
            code=seq.next("030411003"), name=rule["name"],
            spec=[f"规格 {int(w * 1000)}×{int(h * 1000)}", "热镀锌槽式桥架"],
            unit="m", qty=qty, calc=calc, elements=[e.eid for e in els],
            discipline="电气",
        ))
    return items


DEVICE_DISCIPLINE = {"valve": "给排水", "fixture": "给排水",
                     "air_terminal": "暖通",
                     "luminaire": "电气", "switch": "电气", "socket": "电气"}


def calc_devices(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    """点式安装项: 阀门/器具/风口/灯具/开关/插座, 按数量计算。"""
    items: list[BoqItem] = []
    groups: dict[tuple, list[ModelElement]] = {}
    for e in model.elements:
        if not e.category.startswith("device_"):
            continue
        kind = e.params["kind"]
        if kind == "fixture":
            code9 = FIXTURE_CODE.get(e.params["tag"], "031004003")
        else:
            code9 = DEVICE_CODE[kind]
        groups.setdefault((code9, kind, e.params["tag"]), []).append(e)

    for (code9, kind, tag), els in sorted(groups.items()):
        rule = RULES[code9]
        calc = [
            _line("rule", f"计算规则: {rule['rule']}"),
            _line("subst", f"{tag or rule['name']}: 图示共 {len(els)} {rule['unit']}"),
            _line("result", f"工程量取 {len(els)} {rule['unit']}"),
        ]
        items.append(BoqItem(
            code=seq.next(code9), name=rule["name"],
            spec=[f"规格/型号: {tag}" if tag else "规格详见设计"],
            unit=rule["unit"], qty=float(len(els)), calc=calc,
            elements=[e.eid for e in els],
            discipline=DEVICE_DISCIPLINE[kind],
        ))
    return items


def calc_steel(model: BuildingModel, seq: _CodeSeq, params: QtoParams) -> list[BoqItem]:
    """金属结构工程: 钢柱/钢梁按吨计量, 米重取自型号表(澳标型号尾数即 kg/m)。"""
    items: list[BoqItem] = []

    for cat, code9 in (("steel_column", "010603001"), ("steel_beam", "010604001")):
        groups: dict[str, list[ModelElement]] = {}
        for e in model.by_category(cat):
            groups.setdefault(e.tag, []).append(e)
        for tag, els in sorted(groups.items()):
            rule = RULES[code9]
            p0 = els[0].params
            calc = [
                _line("rule", f"计算规则: {rule['rule']}"),
                _line("formula", "W = Σ( 长度 L × 米重 g ),  g 取自钢材型号表"),
            ]
            if p0["kg_per_m"] > 0:
                calc.append(_line("rule",
                                  f"{tag}: 型号 {p0['model']} ({p0['section']}), "
                                  f"g = {_f(p0['kg_per_m'])} kg/m"))
            total_kg = 0.0
            total_len = 0.0
            for e in els:
                L = e.params.get("H") or e.params.get("length") or 0.0
                w = e.params["weight_kg"]
                total_kg += w
                total_len += L
            n = len(els)
            if cat == "steel_column":
                calc.append(_line("subst",
                                  f"共 {n} 根, 合计柱长 {_f(total_len)} m: "
                                  f"{_f(total_len)} × {_f(p0['kg_per_m'])} = {_f(total_kg, 1)} kg"))
            else:
                calc.append(_line("subst",
                                  f"共 {n} 段, 合计梁长 {_f(total_len)} m: "
                                  f"{_f(total_len)} × {_f(p0['kg_per_m'])} = {_f(total_kg, 1)} kg"))
                inherited = [e for e in els if e.params.get("inherited")]
                if inherited:
                    calc.append(_line("deduct",
                                      f"其中 {len(inherited)} 段编号由相邻平行梁继承, 建议人工复核"))
            qty = round_qty(total_kg / 1000.0, "t")
            calc.append(_line("sum", f"工程量取 {qty:.3f} t"))
            items.append(BoqItem(
                code=seq.next(code9),
                name=f"{rule['name']}",
                spec=[f"编号 {tag}", f"型号 {p0['model']}" if p0['model'] else "型号待定",
                      p0["section"], p0["grade"] or "钢材等级见设计"],
                unit="t", qty=qty, calc=calc,
                elements=[e.eid for e in els],
                extra={"根数" if cat == "steel_column" else "段数": len(els)},
            ))
    return items


# ---------------------------------------------------------------------------
# 复核: 双算对比(独立口径粗算 vs 清单精算)
# ---------------------------------------------------------------------------

def cross_check(model: BuildingModel, items: list[BoqItem]) -> list[dict]:
    """用三维模型体积做独立复核: 图元几何体积 与 清单量 的偏差应可解释。"""
    checks: list[dict] = []

    def prim_volume(cat: str) -> float:
        v = 0.0
        for e in model.by_category(cat):
            for p in e.primitives:
                if p["kind"] == "box":
                    v += p["size"][0] * p["size"][1] * p["size"][2]
                elif p["kind"] == "extrude":
                    from .drawing import polygon_area
                    v += polygon_area([tuple(q) for q in p["points"]]) * (p["z1"] - p["z0"])
        return v

    boq_by_cat = {
        "column": sum(i.qty for i in items if i.code.startswith("010502")),
        "beam": sum(i.qty for i in items if i.code.startswith("010503")),
        "slab": sum(i.qty for i in items if i.code.startswith("010505")),
        "wall": sum(i.qty for i in items if i.code.startswith("0104")),
    }
    labels = {"column": "柱", "beam": "梁", "slab": "板", "wall": "墙"}
    for cat, boq_v in boq_by_cat.items():
        if boq_v <= 0:
            continue
        geo_v = prim_volume(cat)
        dev = abs(geo_v - boq_v) / boq_v if boq_v else 0.0
        checks.append({
            "item": f"{labels[cat]}体积双算对比",
            "geometry": round(geo_v, 3), "boq": round(boq_v, 3),
            "deviation": round(dev, 4),
            "pass": bool(dev < 0.02),
            "note": "三维实体体积与清单量一致" if dev < 0.02 else "偏差超 2%, 需人工复核",
        })

    # 钢构件: 三维实体体积×密度(理论质量) vs 型号名义米重清单量
    steel_boq = {
        "steel_column": sum(i.qty for i in items if i.code.startswith("010603")),
        "steel_beam": sum(i.qty for i in items if i.code.startswith("010604")),
    }
    steel_labels = {"steel_column": "钢柱", "steel_beam": "钢梁"}
    for cat, boq_t in steel_boq.items():
        if boq_t <= 0:
            continue
        geo_t = prim_volume(cat) * 7.85  # m³ × 7850kg/m³ / 1000
        dev = abs(geo_t - boq_t) / boq_t
        checks.append({
            "item": f"{steel_labels[cat]}质量双算对比(截面理论质量 vs 型号名义米重)",
            "geometry": round(geo_t, 3), "boq": round(boq_t, 3),
            "deviation": round(dev, 4),
            "pass": bool(dev < 0.05),
            "note": "截面几何质量与型号名义质量一致" if dev < 0.05
                    else "偏差超 5%, 检查型号表匹配",
        })
    return checks
