"""自动测试-迭代循环: 让系统在评测反馈下自动"生长"。

流程(每一代):
  1. 用当前参数跑全量评测 -> 得到综合分
  2. 对可调参数逐个生成候选变异(邻域取值)
  3. 评测每个候选, 若最优候选优于当前 -> 采纳(爬山)
  4. 记录到 history.jsonl; 无改进或达到目标分则收敛停止

可调参数空间:
  snap_tol_mm                  识图坐标吸附容差
  min_deduct_area_m2           洞口扣减最小面积口径
  beam_to_column_face          梁长是否算至柱侧面
  beam_height_to_slab_bottom   整浇梁高是否算至板底

用法:
  python -m app.eval.loop                 # 从当前 params.json 继续迭代
  python -m app.eval.loop --from-scratch  # 从"朴素基线"起步, 演示自动生长过程
"""

from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

from ..core.qto import QtoParams
from .benchmark import REPORTS_DIR, evaluate, save_report

PARAMS_PATH = Path(__file__).resolve().parent / "params.json"
HISTORY_PATH = REPORTS_DIR / "history.jsonl"

TARGET_SCORE = 0.999
MAX_GENERATIONS = 12

# 每个参数的候选邻域
NEIGHBORS = {
    "snap_tol_mm": [0.0, 1.0, 2.0, 5.0, 10.0],
    "min_deduct_area_m2": [0.0, 0.1, 0.2, 0.3, 0.4, 0.5],
    "beam_to_column_face": [True, False],
    "beam_height_to_slab_bottom": [True, False],
}

NAIVE_BASELINE = {
    # 朴素起点: 不吸附、不做任何扣减口径, 模拟"未调教"的系统
    "snap_tol_mm": 0.0,
    "min_deduct_area_m2": 0.0,
    "beam_to_column_face": False,
    "beam_height_to_slab_bottom": False,
}


def load_params() -> QtoParams:
    if PARAMS_PATH.exists():
        return QtoParams.from_dict(json.loads(PARAMS_PATH.read_text(encoding="utf-8")))
    return QtoParams()


def save_params(params: QtoParams) -> None:
    PARAMS_PATH.write_text(
        json.dumps(params.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")


def _append_history(record: dict) -> None:
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
    with HISTORY_PATH.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


def _score(params: QtoParams, with_stability: bool) -> tuple[float, dict]:
    report = evaluate(params, with_stability=with_stability)
    return report["summary"]["score"], report


def run_loop(from_scratch: bool = False, max_generations: int = MAX_GENERATIONS,
             with_stability: bool = True, log=print) -> dict:
    """执行爬山迭代, 返回最终报告; 过程写入 history.jsonl。"""
    params = (QtoParams.from_dict(NAIVE_BASELINE) if from_scratch else load_params())
    score, report = _score(params, with_stability)
    log(f"[gen 0] 起始参数 {params.to_dict()}")
    log(f"[gen 0] 综合分 {score:.4f} "
        f"(准确率 {report['summary']['accuracy']:.4f}, 召回 {report['summary']['recall']:.4f})")
    _append_history({"ts": time.time(), "generation": 0, "event": "baseline",
                     "params": params.to_dict(), "summary": report["summary"]})

    def single_mutations(base: QtoParams):
        for key, values in NEIGHBORS.items():
            current = getattr(base, key)
            for v in values:
                if v != current:
                    yield (QtoParams.from_dict({**base.to_dict(), key: v}),
                           f"{key}: {current} -> {v}")

    def pair_mutations(base: QtoParams):
        """二阶变异: 两个参数同时变, 用于跳出单参数无法改进的局部最优。"""
        keys = list(NEIGHBORS)
        for i in range(len(keys)):
            for j in range(i + 1, len(keys)):
                k1, k2 = keys[i], keys[j]
                c1, c2 = getattr(base, k1), getattr(base, k2)
                for v1 in NEIGHBORS[k1]:
                    if v1 == c1:
                        continue
                    for v2 in NEIGHBORS[k2]:
                        if v2 == c2:
                            continue
                        yield (QtoParams.from_dict({**base.to_dict(), k1: v1, k2: v2}),
                               f"{k1}: {c1} -> {v1}, {k2}: {c2} -> {v2}")

    for gen in range(1, max_generations + 1):
        if score >= TARGET_SCORE:
            log(f"[gen {gen}] 已达目标分 {TARGET_SCORE}, 收敛停止")
            break

        best_cand: QtoParams | None = None
        best_score, best_report, best_change = score, report, ""

        for stage, mutations in (("一阶", single_mutations(params)),
                                 ("二阶", pair_mutations(params))):
            for cand, change in mutations:
                cand_score, cand_report = _score(cand, with_stability)
                if cand_score > best_score + 1e-9:
                    best_cand, best_score, best_report = cand, cand_score, cand_report
                    best_change = f"[{stage}] {change}"
            if best_cand is not None:
                break  # 一阶已有改进则不进入二阶

        if best_cand is None:
            log(f"[gen {gen}] 一阶/二阶邻域内均无改进, 收敛停止")
            _append_history({"ts": time.time(), "generation": gen, "event": "converged",
                             "params": params.to_dict(), "summary": report["summary"]})
            break

        params, score, report = best_cand, best_score, best_report
        log(f"[gen {gen}] 采纳 {best_change}, 综合分 {score:.4f}")
        _append_history({"ts": time.time(), "generation": gen, "event": "improved",
                         "change": best_change, "params": params.to_dict(),
                         "summary": report["summary"]})

    save_params(params)
    save_report(report)
    log(f"最终参数已写入 {PARAMS_PATH.name}, 报告已写入 reports/report.json")
    return report


def read_history() -> list[dict]:
    if not HISTORY_PATH.exists():
        return []
    return [json.loads(line) for line in
            HISTORY_PATH.read_text(encoding="utf-8").splitlines() if line.strip()]


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="自动测试-迭代循环")
    ap.add_argument("--from-scratch", action="store_true",
                    help="从朴素基线起步, 演示自动生长")
    ap.add_argument("--max-gen", type=int, default=MAX_GENERATIONS)
    args = ap.parse_args()
    rep = run_loop(from_scratch=args.from_scratch, max_generations=args.max_gen)
    print(json.dumps(rep["summary"], ensure_ascii=False, indent=2))
