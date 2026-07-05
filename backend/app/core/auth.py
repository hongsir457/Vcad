"""账号体系: 注册/登录/令牌 + 余额计费 + 个人设置。

本地单机实现, 零外部依赖:
- 存储: SQLite (backend/app/data/runtime/vcad.db)
- 口令: PBKDF2-HMAC-SHA256 (16 字节盐, 12 万次迭代)
- 令牌: HMAC 签名 "username|过期时间戳", 密钥持久化在 runtime/secret.key
- 充值: 本地演示为模拟支付(直接到账并记账), 接真实支付时替换 recharge 实现

计费口径(演示定价, 见 PRICING):
- 上传解析: DWG/DXF 2 元, PDF(含 OCR) 5 元, JSON 免费
- 运行建模算量流水线: 1 元/次
- 注册赠送 50 元
"""

from __future__ import annotations

import hashlib
import hmac
import secrets
import sqlite3
import time
from dataclasses import dataclass
from pathlib import Path

RUNTIME_DIR = Path(__file__).resolve().parent.parent / "data" / "runtime"
DB_PATH = RUNTIME_DIR / "vcad.db"
SECRET_PATH = RUNTIME_DIR / "secret.key"

TOKEN_TTL_S = 7 * 24 * 3600
SIGNUP_BONUS = 50.0

PRICING = {
    "upload_dwg": 2.0,
    "upload_pdf": 5.0,
    "upload_json": 0.0,
    "run_pipeline": 1.0,
}


class AuthError(Exception):
    """认证/口令错误。"""


class BalanceError(Exception):
    """余额不足。"""


def _db() -> sqlite3.Connection:
    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.executescript("""
        CREATE TABLE IF NOT EXISTS users (
            username TEXT PRIMARY KEY,
            pw_hash  BLOB NOT NULL,
            pw_salt  BLOB NOT NULL,
            nickname TEXT DEFAULT '',
            region   TEXT DEFAULT '上海',
            balance  REAL DEFAULT 0,
            created  REAL
        );
        CREATE TABLE IF NOT EXISTS transactions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL,
            amount   REAL NOT NULL,      -- 正=充值/赠送, 负=消费
            balance  REAL NOT NULL,      -- 交易后余额
            note     TEXT,
            ts       REAL
        );
    """)
    return conn


def _secret() -> bytes:
    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
    if not SECRET_PATH.exists():
        SECRET_PATH.write_bytes(secrets.token_bytes(32))
    return SECRET_PATH.read_bytes()


def _hash_pw(password: str, salt: bytes) -> bytes:
    return hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 120_000)


@dataclass
class User:
    username: str
    nickname: str
    region: str
    balance: float

    def to_dict(self) -> dict:
        return {"username": self.username, "nickname": self.nickname,
                "region": self.region, "balance": round(self.balance, 2)}


# ---------------------------------------------------------------------------
# 注册 / 登录 / 令牌
# ---------------------------------------------------------------------------

def register(username: str, password: str, nickname: str = "") -> str:
    username = username.strip()
    if not (3 <= len(username) <= 32) or not username.replace("_", "").isalnum():
        raise AuthError("用户名需为 3~32 位字母/数字/下划线")
    if len(password) < 6:
        raise AuthError("密码至少 6 位")
    salt = secrets.token_bytes(16)
    with _db() as db:
        if db.execute("SELECT 1 FROM users WHERE username=?", (username,)).fetchone():
            raise AuthError("用户名已存在")
        db.execute(
            "INSERT INTO users (username, pw_hash, pw_salt, nickname, balance, created) "
            "VALUES (?,?,?,?,?,?)",
            (username, _hash_pw(password, salt), salt,
             nickname or username, SIGNUP_BONUS, time.time()))
        db.execute(
            "INSERT INTO transactions (username, amount, balance, note, ts) "
            "VALUES (?,?,?,?,?)",
            (username, SIGNUP_BONUS, SIGNUP_BONUS, "注册赠送", time.time()))
    return issue_token(username)


def login(username: str, password: str) -> str:
    with _db() as db:
        row = db.execute("SELECT pw_hash, pw_salt FROM users WHERE username=?",
                         (username.strip(),)).fetchone()
    if row is None or not hmac.compare_digest(
            row["pw_hash"], _hash_pw(password, row["pw_salt"])):
        raise AuthError("用户名或密码错误")
    return issue_token(username.strip())


def issue_token(username: str) -> str:
    exp = int(time.time()) + TOKEN_TTL_S
    payload = f"{username}|{exp}"
    sig = hmac.new(_secret(), payload.encode(), hashlib.sha256).hexdigest()
    return f"{payload}|{sig}"


def verify_token(token: str) -> str:
    """校验令牌, 返回用户名; 失败抛 AuthError。"""
    try:
        username, exp_s, sig = token.rsplit("|", 2)
    except (ValueError, AttributeError) as e:
        raise AuthError("令牌格式错误") from e
    payload = f"{username}|{exp_s}"
    expect = hmac.new(_secret(), payload.encode(), hashlib.sha256).hexdigest()
    if not hmac.compare_digest(sig, expect):
        raise AuthError("令牌无效")
    if int(exp_s) < time.time():
        raise AuthError("登录已过期, 请重新登录")
    return username


def get_user(username: str) -> User:
    with _db() as db:
        row = db.execute(
            "SELECT username, nickname, region, balance FROM users WHERE username=?",
            (username,)).fetchone()
    if row is None:
        raise AuthError("用户不存在")
    return User(row["username"], row["nickname"], row["region"], row["balance"])


# ---------------------------------------------------------------------------
# 余额与计费
# ---------------------------------------------------------------------------

def charge(username: str, amount: float, note: str) -> float:
    """扣费(amount>0 表示消费金额), 余额不足抛 BalanceError, 返回新余额。"""
    if amount <= 0:
        return get_user(username).balance
    with _db() as db:
        row = db.execute("SELECT balance FROM users WHERE username=?",
                         (username,)).fetchone()
        if row is None:
            raise AuthError("用户不存在")
        if row["balance"] < amount - 1e-9:
            raise BalanceError(
                f"余额不足: 本次需 ¥{amount:.2f}, 当前余额 ¥{row['balance']:.2f}, 请先充值")
        new_balance = row["balance"] - amount
        db.execute("UPDATE users SET balance=? WHERE username=?",
                   (new_balance, username))
        db.execute("INSERT INTO transactions (username, amount, balance, note, ts) "
                   "VALUES (?,?,?,?,?)",
                   (username, -amount, new_balance, note, time.time()))
    return new_balance


def recharge(username: str, amount: float) -> float:
    """模拟充值(本地演示直接到账); 接入真实支付时在此对接渠道回调。"""
    if not (0 < amount <= 10000):
        raise AuthError("充值金额需在 0~10000 之间")
    with _db() as db:
        row = db.execute("SELECT balance FROM users WHERE username=?",
                         (username,)).fetchone()
        if row is None:
            raise AuthError("用户不存在")
        new_balance = row["balance"] + amount
        db.execute("UPDATE users SET balance=? WHERE username=?",
                   (new_balance, username))
        db.execute("INSERT INTO transactions (username, amount, balance, note, ts) "
                   "VALUES (?,?,?,?,?)",
                   (username, amount, new_balance, "充值(模拟支付)", time.time()))
    return new_balance


def transactions(username: str, limit: int = 50) -> list[dict]:
    with _db() as db:
        rows = db.execute(
            "SELECT amount, balance, note, ts FROM transactions "
            "WHERE username=? ORDER BY id DESC LIMIT ?", (username, limit)).fetchall()
    return [{"amount": round(r["amount"], 2), "balance": round(r["balance"], 2),
             "note": r["note"], "ts": r["ts"]} for r in rows]


# ---------------------------------------------------------------------------
# 个人设置
# ---------------------------------------------------------------------------

def update_profile(username: str, nickname: str | None = None,
                   region: str | None = None) -> User:
    with _db() as db:
        if nickname is not None:
            db.execute("UPDATE users SET nickname=? WHERE username=?",
                       (nickname.strip()[:32], username))
        if region is not None:
            db.execute("UPDATE users SET region=? WHERE username=?",
                       (region.strip()[:32], username))
    return get_user(username)


def change_password(username: str, old: str, new: str) -> None:
    if len(new) < 6:
        raise AuthError("新密码至少 6 位")
    with _db() as db:
        row = db.execute("SELECT pw_hash, pw_salt FROM users WHERE username=?",
                         (username,)).fetchone()
        if row is None or not hmac.compare_digest(
                row["pw_hash"], _hash_pw(old, row["pw_salt"])):
            raise AuthError("原密码错误")
        salt = secrets.token_bytes(16)
        db.execute("UPDATE users SET pw_hash=?, pw_salt=? WHERE username=?",
                   (_hash_pw(new, salt), salt, username))
