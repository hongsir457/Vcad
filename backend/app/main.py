"""VCAD 后端服务: 图纸 -> 三维建模 -> 工程量清单 的流式 API。"""

from __future__ import annotations

import csv
import io
import json
import time
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response, StreamingResponse
from fastapi.staticfiles import StaticFiles

from .core.drawing import list_samples, load_sample
from .core.qto import QtoParams
from .eval import loop as eval_loop
from .eval.benchmark import evaluate, list_cases, save_report
from .pipeline.orchestrator import run_pipeline, run_to_result

app = FastAPI(title="VCAD — 图纸自动建模算量", version="0.1.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"], allow_methods=["*"], allow_headers=["*"],
)

STEP_DELAY_S = 0.08  # 步骤间的最小间隔, 让前端步骤流有可读的节奏


def current_params() -> QtoParams:
    return eval_loop.load_params()


# ---------------------------------------------------------------------------
# 图纸
# ---------------------------------------------------------------------------

@app.get("/api/drawings")
def api_drawings() -> list[dict]:
    return list_samples()


@app.get("/api/drawings/{drawing_id}")
def api_drawing(drawing_id: str) -> dict:
    try:
        return load_sample(drawing_id)
    except FileNotFoundError as e:
        raise HTTPException(404, str(e)) from e


# ---------------------------------------------------------------------------
# 流水线执行(SSE)
# ---------------------------------------------------------------------------

@app.get("/api/run/{drawing_id}/stream")
def api_run_stream(drawing_id: str) -> StreamingResponse:
    try:
        raw = load_sample(drawing_id)
    except FileNotFoundError as e:
        raise HTTPException(404, str(e)) from e
    params = current_params()

    def gen():
        try:
            for ev in run_pipeline(raw, params):
                yield f"data: {json.dumps(ev, ensure_ascii=False)}\n\n"
                if ev["type"] in ("step_start", "step_done", "phase"):
                    time.sleep(STEP_DELAY_S)
            yield "event: end\ndata: {}\n\n"
        except Exception as e:  # noqa: BLE001 — 把异常转成事件反馈给前端
            err = {"type": "error", "message": f"{type(e).__name__}: {e}"}
            yield f"data: {json.dumps(err, ensure_ascii=False)}\n\n"

    return StreamingResponse(gen(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache",
                                      "X-Accel-Buffering": "no"})


@app.get("/api/run/{drawing_id}/result")
def api_run_result(drawing_id: str) -> dict:
    try:
        raw = load_sample(drawing_id)
    except FileNotFoundError as e:
        raise HTTPException(404, str(e)) from e
    return run_to_result(raw, current_params())


@app.get("/api/run/{drawing_id}/boq.csv")
def api_boq_csv(drawing_id: str) -> Response:
    try:
        raw = load_sample(drawing_id)
    except FileNotFoundError as e:
        raise HTTPException(404, str(e)) from e
    result = run_to_result(raw, current_params())

    buf = io.StringIO()
    w = csv.writer(buf)
    w.writerow(["序号", "项目编码", "项目名称", "项目特征", "计量单位", "工程量", "备注"])
    for i, item in enumerate(result["boq"], 1):
        extra = "; ".join(f"{k}{v}" for k, v in item.get("extra", {}).items())
        w.writerow([i, item["code"], item["name"], "; ".join(item["spec"]),
                    item["unit"], item["qty"], extra])
    data = "\ufeff" + buf.getvalue()  # BOM 使 Excel 正确识别 UTF-8
    return Response(
        content=data, media_type="text/csv; charset=utf-8",
        headers={"Content-Disposition":
                 f"attachment; filename={drawing_id}_boq.csv"},
    )


# ---------------------------------------------------------------------------
# 评测体系
# ---------------------------------------------------------------------------

@app.get("/api/eval/cases")
def api_eval_cases() -> list[str]:
    return list_cases()


@app.post("/api/eval/run")
def api_eval_run(with_stability: bool = True) -> dict:
    report = evaluate(current_params(), with_stability=with_stability)
    save_report(report)
    return report


@app.post("/api/eval/loop")
def api_eval_loop(from_scratch: bool = False, max_gen: int = 8) -> dict:
    logs: list[str] = []
    report = eval_loop.run_loop(from_scratch=from_scratch, max_generations=max_gen,
                                log=logs.append)
    return {"logs": logs, "report": report, "history": eval_loop.read_history()}


@app.get("/api/eval/history")
def api_eval_history() -> list[dict]:
    return eval_loop.read_history()


@app.get("/api/params")
def api_params() -> dict:
    return current_params().to_dict()


# ---------------------------------------------------------------------------
# 前端静态资源(生产模式: 先 pnpm build)
# ---------------------------------------------------------------------------

_dist = Path(__file__).resolve().parents[2] / "frontend" / "dist"
if _dist.exists():
    app.mount("/", StaticFiles(directory=_dist, html=True), name="frontend")
