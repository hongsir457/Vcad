"""VCAD 后端服务: 图纸 -> 三维建模 -> 工程量清单 的流式 API。"""

from __future__ import annotations

import csv
import hashlib
import io
import json
import re
import subprocess
import tempfile
import time
from pathlib import Path

from fastapi import FastAPI, HTTPException, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import Response, StreamingResponse
from fastapi.staticfiles import StaticFiles

from pydantic import BaseModel

from .core import auth as auth_mod
from .core import reinforcement_recognizer, steel_recognizer
from .core.auth import AuthError, BalanceError
from .core.drawing import delete_upload, list_samples, load_sample, save_upload
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


@app.exception_handler(Exception)
async def _unhandled(request, exc):  # noqa: ANN001 — FastAPI 约定签名
    """兜底: 任何未捕获异常都返回可读 JSON 并在服务端打印堆栈。"""
    import traceback
    traceback.print_exc()
    from fastapi.responses import JSONResponse
    return JSONResponse(
        status_code=500,
        content={"detail": f"服务端错误: {type(exc).__name__}: {exc}. "
                           f"完整堆栈见后端终端输出"})


def current_params() -> QtoParams:
    return eval_loop.load_params()


# ---------------------------------------------------------------------------
# 账号 / 计费
# ---------------------------------------------------------------------------

def _require_user(request: Request) -> str:
    """从 Authorization: Bearer 头或 ?token= 查询参数取用户(GET/SSE 用查询参数)。"""
    token = ""
    authz = request.headers.get("authorization", "")
    if authz.lower().startswith("bearer "):
        token = authz[7:]
    if not token:
        token = request.query_params.get("token", "")
    if not token:
        raise HTTPException(401, "请先登录")
    try:
        return auth_mod.verify_token(token)
    except AuthError as e:
        raise HTTPException(401, str(e)) from e


def _charge_or_402(username: str, amount: float, note: str) -> None:
    try:
        auth_mod.charge(username, amount, note)
    except BalanceError as e:
        raise HTTPException(402, str(e)) from e


class Credentials(BaseModel):
    username: str
    password: str
    nickname: str = ""


class RechargeReq(BaseModel):
    amount: float


class ProfileReq(BaseModel):
    nickname: str | None = None
    region: str | None = None


class PasswordReq(BaseModel):
    old: str
    new: str


@app.post("/api/auth/register")
def api_register(body: Credentials) -> dict:
    try:
        token = auth_mod.register(body.username, body.password, body.nickname)
    except AuthError as e:
        raise HTTPException(400, str(e)) from e
    return {"token": token, "user": auth_mod.get_user(body.username.strip()).to_dict()}


@app.post("/api/auth/login")
def api_login(body: Credentials) -> dict:
    try:
        token = auth_mod.login(body.username, body.password)
    except AuthError as e:
        raise HTTPException(401, str(e)) from e
    return {"token": token, "user": auth_mod.get_user(body.username.strip()).to_dict()}


@app.get("/api/auth/me")
def api_me(request: Request) -> dict:
    return auth_mod.get_user(_require_user(request)).to_dict()


@app.post("/api/account/recharge")
def api_recharge(body: RechargeReq, request: Request) -> dict:
    user = _require_user(request)
    try:
        balance = auth_mod.recharge(user, body.amount)
    except AuthError as e:
        raise HTTPException(400, str(e)) from e
    return {"balance": round(balance, 2), "note": "模拟支付已到账(本地演示版)"}


@app.get("/api/account/transactions")
def api_transactions(request: Request) -> list[dict]:
    return auth_mod.transactions(_require_user(request))


@app.post("/api/account/profile")
def api_profile(body: ProfileReq, request: Request) -> dict:
    user = _require_user(request)
    return auth_mod.update_profile(user, body.nickname, body.region).to_dict()


@app.post("/api/account/password")
def api_password(body: PasswordReq, request: Request) -> dict:
    user = _require_user(request)
    try:
        auth_mod.change_password(user, body.old, body.new)
    except AuthError as e:
        raise HTTPException(400, str(e)) from e
    return {"ok": True}


@app.get("/api/pricing")
def api_pricing() -> dict:
    return auth_mod.PRICING


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


DWG_TOOL = Path(__file__).resolve().parents[1] / "tools" / "dwg2json.mjs"
TOOLS_DIR = DWG_TOOL.parent
MAX_UPLOAD_BYTES = 80 * 1024 * 1024


def _dwg_to_entities(data: bytes, suffix: str) -> dict:
    """DWG/DXF -> 通用实体 JSON (Node + WASM LibreDWG)。"""
    import shutil

    node = shutil.which("node")
    if node is None:
        raise HTTPException(
            422, "未找到 node 命令: DWG/DXF 解析需要 Node 20+, "
                 "请安装 Node 并确保 node 在 PATH 中(安装后需重启终端)")
    if not (TOOLS_DIR / "node_modules" / "@mlightcad").exists():
        raise HTTPException(
            422, f"DWG 转换工具依赖未安装: 请在 {TOOLS_DIR} 目录执行 npm install")
    with tempfile.TemporaryDirectory() as td:
        src = Path(td) / f"input{suffix}"
        dst = Path(td) / "entities.json"
        src.write_bytes(data)
        try:
            proc = subprocess.run(
                [node, str(DWG_TOOL), str(src), str(dst)],
                capture_output=True, text=True, timeout=180,
                cwd=str(TOOLS_DIR), encoding="utf-8", errors="replace")
        except subprocess.TimeoutExpired as e:
            raise HTTPException(422, "DWG 解析超时(180s), 图纸可能过大") from e
        if proc.returncode != 0 or not dst.exists():
            raise HTTPException(422, f"图纸解析失败: {(proc.stderr or '')[-400:]}")
        return json.loads(dst.read_text(encoding="utf-8"))


@app.post("/api/drawings/upload")
async def api_upload(file: UploadFile, request: Request) -> dict:
    user = _require_user(request)
    name = file.filename or "drawing"
    suffix = Path(name).suffix.lower()
    data = await file.read()
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(413, "文件超过 80MB 限制")
    price = {".dwg": auth_mod.PRICING["upload_dwg"], ".dxf": auth_mod.PRICING["upload_dwg"],
             ".pdf": auth_mod.PRICING["upload_pdf"]}.get(suffix, 0.0)
    if price > 0:
        _charge_or_402(user, price, f"上传解析 {suffix} 图纸")

    stem = re.sub(r"[^\w一-鿿]+", "_", Path(name).stem).strip("_")[:48] or "drawing"
    drawing_id = f"up_{stem}_{hashlib.md5(data).hexdigest()[:6]}"

    if suffix == ".json":
        try:
            raw = json.loads(data)
        except json.JSONDecodeError as e:
            raise HTTPException(422, f"JSON 解析失败: {e}") from e
    elif suffix in (".dwg", ".dxf", ".pdf"):
        if suffix == ".pdf":
            try:
                from .core.pdf_import import extract_pdf
            except ImportError as e:
                raise HTTPException(
                    422, f"PDF 解析依赖缺失({e}): 请执行 "
                         f"pip install pymupdf rapidocr-onnxruntime onnxruntime") from e
            try:
                entities = extract_pdf(data)  # 矢量+原生文本+分块 OCR, 可能耗时数分钟
            except Exception as e:  # noqa: BLE001 — OCR/解析环境问题需给出可读诊断
                raise HTTPException(
                    422, f"PDF 提取失败: {type(e).__name__}: {e}") from e
        else:
            entities = _dwg_to_entities(data, suffix)
        raw = None
        errors: list[str] = []
        for recog in (steel_recognizer.recognize, reinforcement_recognizer.recognize):
            try:
                raw = recog(entities, Path(name).stem)
                break
            except ValueError as e:
                errors.append(str(e))
            except Exception as e:  # noqa: BLE001 — 识别器内部错误也转成诊断
                errors.append(f"{type(e).__name__}: {e}")
        if raw is None:
            diag = "; ".join(errors)
            extra = "; ".join(entities.get("log", [])[:4])
            raise HTTPException(
                422, f"识别失败: {diag}。已尝试识别器: 钢结构平面 / 柱加固平面。"
                     f"提取诊断: {extra}") from None
    else:
        raise HTTPException(415, f"不支持的文件类型: {suffix}")

    try:
        save_upload(drawing_id, raw)
    except Exception as e:  # noqa: BLE001 — 解析验证失败要给出可读错误
        raise HTTPException(422, f"图纸校验失败: {e}") from e

    meta = raw.get("meta", {})
    return {"id": drawing_id, "name": meta.get("name", stem),
            "desc": meta.get("desc", ""), "region": meta.get("region", ""),
            "recognition": meta.get("recognition", {}).get("log", [])}


@app.delete("/api/drawings/{drawing_id}")
def api_delete_drawing(drawing_id: str, request: Request) -> dict:
    _require_user(request)
    if not drawing_id.startswith("up_"):
        raise HTTPException(403, "内置样例不可删除")
    if not delete_upload(drawing_id):
        raise HTTPException(404, "图纸不存在")
    return {"ok": True}


# ---------------------------------------------------------------------------
# 流水线执行(SSE)
# ---------------------------------------------------------------------------

@app.get("/api/run/{drawing_id}/stream")
def api_run_stream(drawing_id: str, request: Request) -> StreamingResponse:
    user = _require_user(request)
    try:
        raw = load_sample(drawing_id)
    except FileNotFoundError as e:
        raise HTTPException(404, str(e)) from e
    _charge_or_402(user, auth_mod.PRICING["run_pipeline"], f"建模算量: {drawing_id}")
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
def api_boq_csv(drawing_id: str, request: Request) -> Response:
    _require_user(request)
    try:
        raw = load_sample(drawing_id)
    except FileNotFoundError as e:
        raise HTTPException(404, str(e)) from e
    result = run_to_result(raw, current_params())

    buf = io.StringIO()
    w = csv.writer(buf)
    w.writerow(["序号", "项目编码", "专业", "项目名称", "项目特征", "计量单位", "工程量", "备注"])
    for i, item in enumerate(result["boq"], 1):
        extra = "; ".join(f"{k}{v}" for k, v in item.get("extra", {}).items())
        w.writerow([i, item["code"], item.get("discipline", "结构"), item["name"],
                    "; ".join(item["spec"]), item["unit"], item["qty"], extra])
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
