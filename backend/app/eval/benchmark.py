"""准确率与稳定性评价体系。

准确率(accuracy):
  以人工手算基准(GT)为标尺, 逐条比对清单量:
  - 匹配: 按 9 位清单编码 + 项目特征关键字(区分同码不同规格)
  - 单项偏差率 rel_err = |算出量 − 基准量| / 基准量
  - 单项判定: rel_err ≤ 0.5% 为通过(清单量取舍误差以内)
  - 漏项(GT 有而结果无) / 多项(结果有而 GT 无) 分别计入召回率/精确率

稳定性(stability):
  对同一图纸做 N 次扰动重跑(实体顺序打乱 + 坐标 ±1mm 抖动, 模拟绘图误差),
  清单结果(编码, 数量)应完全一致。稳定性 = 一致轮次 / 总轮次。

综合分 = 0.6 × 准确率 + 0.2 × 召回率 + 0.2 × 稳定性
"""

from __future__ import annotations

import json
import random
from pathlib import Path

from ..core.drawing import load_sample
from ..core.qto import QtoParams
from ..pipeline.orchestrator import run_to_result

CASES_DIR = Path(__file__).resolve().parent / "cases"
REPORTS_DIR = Path(__file__).resolve().parent / "reports"

REL_ERR_PASS = 0.005   # 单项偏差 ≤ 0.5% 判为通过
STABILITY_RUNS = 5


def list_cases() -> list[str]:
    return sorted(p.stem.replace(".gt", "") for p in CASES_DIR.glob("*.gt.json"))


def load_gt(case_id: str) -> dict:
    return json.loads((CASES_DIR / f"{case_id}.gt.json").read_text(encoding="utf-8"))


# ---------------------------------------------------------------------------
# 匹配与准确率
# ---------------------------------------------------------------------------

def match_items(gt_items: list[dict], boq: list[dict]) -> list[dict]:
    """把 GT 条目与产出清单一一匹配, 返回逐项比对结果。"""
    used: set[int] = set()
    rows: list[dict] = []

    for gt in gt_items:
        found_idx = None
        for i, item in enumerate(boq):
            if i in used or not item["code"].startswith(gt["code"]):
                continue
            key = gt.get("match")
            if key and not any(key in s for s in item["spec"]):
                continue
            found_idx = i
            break
        if found_idx is None:
            rows.append({
                "code": gt["code"], "name": gt["name"], "match": gt.get("match", ""),
                "unit": gt["unit"], "gt_qty": gt["qty"], "qty": None,
                "rel_err": None, "status": "漏项",
            })
        else:
            used.add(found_idx)
            item = boq[found_idx]
            rel_err = abs(item["qty"] - gt["qty"]) / gt["qty"] if gt["qty"] else 0.0
            rows.append({
                "code": gt["code"], "name": gt["name"], "match": gt.get("match", ""),
                "unit": gt["unit"], "gt_qty": gt["qty"], "qty": item["qty"],
                "rel_err": round(rel_err, 5),
                "status": "通过" if rel_err <= REL_ERR_PASS else "超差",
            })

    for i, item in enumerate(boq):
        if i not in used:
            rows.append({
                "code": item["code"][:9], "name": item["name"],
                "match": "", "unit": item["unit"],
                "gt_qty": None, "qty": item["qty"], "rel_err": None, "status": "多项",
            })
    return rows


# ---------------------------------------------------------------------------
# 稳定性: 扰动重跑
# ---------------------------------------------------------------------------

def perturb_raw(raw: dict, seed: int, jitter_mm: float = 1.0) -> dict:
    """打乱实体顺序并对坐标做亚容差抖动, 模拟不同绘图人/导出器的差异。"""
    rng = random.Random(seed)
    out = json.loads(json.dumps(raw))
    ents = out["entities"]
    rng.shuffle(ents)

    def jig(p: list[float]) -> list[float]:
        return [p[0] + rng.uniform(-jitter_mm, jitter_mm),
                p[1] + rng.uniform(-jitter_mm, jitter_mm)]

    for ent in ents:
        for key in ("p1", "p2", "at"):
            if key in ent:
                ent[key] = jig(ent[key])
        if "pts" in ent:
            ent["pts"] = [jig(p) for p in ent["pts"]]
    return out


def boq_signature(boq: list[dict]) -> tuple:
    return tuple(sorted((i["code"][:9], tuple(i["spec"]), i["qty"]) for i in boq))


def stability_for_case(raw: dict, params: QtoParams, runs: int = STABILITY_RUNS) -> dict:
    base = boq_signature(run_to_result(raw, params)["boq"])
    consistent = 0
    max_dev = 0.0
    for k in range(runs):
        sig = boq_signature(run_to_result(perturb_raw(raw, seed=1000 + k), params)["boq"])
        if sig == base:
            consistent += 1
        else:
            base_q = {s[:2]: s[2] for s in base}
            for s in sig:
                if s[:2] in base_q and base_q[s[:2]]:
                    max_dev = max(max_dev, abs(s[2] - base_q[s[:2]]) / base_q[s[:2]])
    return {"runs": runs, "consistent": consistent,
            "stability": round(consistent / runs, 4), "max_dev": round(max_dev, 5)}


# ---------------------------------------------------------------------------
# 整体评测
# ---------------------------------------------------------------------------

def evaluate(params: QtoParams | None = None, with_stability: bool = True) -> dict:
    """跑全部评测用例, 返回结构化报告。"""
    params = params or QtoParams()
    cases_report: list[dict] = []

    for case_id in list_cases():
        raw = load_sample(case_id)
        gt = load_gt(case_id)
        result = run_to_result(raw, params)
        rows = match_items(gt["items"], result["boq"])

        n_gt = len(gt["items"])
        n_pass = sum(1 for r in rows if r["status"] == "通过")
        n_missing = sum(1 for r in rows if r["status"] == "漏项")
        n_extra = sum(1 for r in rows if r["status"] == "多项")
        matched = n_gt - n_missing
        produced = matched + n_extra

        case = {
            "case": case_id,
            "name": raw.get("meta", {}).get("name", case_id),
            "rows": rows,
            "accuracy": round(n_pass / n_gt, 4) if n_gt else 0.0,
            "recall": round(matched / n_gt, 4) if n_gt else 0.0,
            "precision": round(matched / produced, 4) if produced else 0.0,
            "issues": result["model"]["issues"],
        }
        if with_stability:
            case["stability"] = stability_for_case(raw, params)
        cases_report.append(case)

    n = len(cases_report)
    accuracy = sum(c["accuracy"] for c in cases_report) / n if n else 0.0
    recall = sum(c["recall"] for c in cases_report) / n if n else 0.0
    stability = (sum(c["stability"]["stability"] for c in cases_report) / n
                 if n and with_stability else None)
    score = (0.6 * accuracy + 0.2 * recall +
             0.2 * (stability if stability is not None else recall))

    return {
        "params": params.to_dict(),
        "cases": cases_report,
        "summary": {
            "case_count": n,
            "accuracy": round(accuracy, 4),
            "recall": round(recall, 4),
            "stability": round(stability, 4) if stability is not None else None,
            "score": round(score, 4),
        },
    }


def save_report(report: dict, name: str = "report.json") -> Path:
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
    path = REPORTS_DIR / name
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


if __name__ == "__main__":
    rep = evaluate()
    save_report(rep)
    print(json.dumps(rep["summary"], ensure_ascii=False, indent=2))
