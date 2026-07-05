"""账号 / 计费 / 令牌测试(使用临时数据库)。"""

import pytest

from app.core import auth


@pytest.fixture(autouse=True)
def temp_db(tmp_path, monkeypatch):
    monkeypatch.setattr(auth, "RUNTIME_DIR", tmp_path)
    monkeypatch.setattr(auth, "DB_PATH", tmp_path / "vcad.db")
    monkeypatch.setattr(auth, "SECRET_PATH", tmp_path / "secret.key")


def test_register_login_and_token_roundtrip():
    token = auth.register("zhang_san", "secret1", "张工")
    assert auth.verify_token(token) == "zhang_san"
    u = auth.get_user("zhang_san")
    assert u.nickname == "张工"
    assert u.balance == auth.SIGNUP_BONUS
    token2 = auth.login("zhang_san", "secret1")
    assert auth.verify_token(token2) == "zhang_san"
    with pytest.raises(auth.AuthError):
        auth.login("zhang_san", "wrong")
    with pytest.raises(auth.AuthError):
        auth.register("zhang_san", "secret1")  # 重名


def test_invalid_token_rejected():
    auth.register("li_si", "secret1")
    good = auth.issue_token("li_si")
    with pytest.raises(auth.AuthError):
        auth.verify_token(good[:-4] + "0000")  # 篡改签名
    with pytest.raises(auth.AuthError):
        auth.verify_token("not-a-token")


def test_charge_and_balance_guard():
    auth.register("wang_wu", "secret1")
    balance = auth.charge("wang_wu", 2.0, "上传解析 .dwg 图纸")
    assert balance == auth.SIGNUP_BONUS - 2.0
    # 余额不足
    with pytest.raises(auth.BalanceError):
        auth.charge("wang_wu", 10000, "big")
    # 充值(模拟)后可继续消费
    balance = auth.recharge("wang_wu", 100)
    assert balance == auth.SIGNUP_BONUS - 2.0 + 100
    txs = auth.transactions("wang_wu")
    assert txs[0]["note"] == "充值(模拟支付)"
    assert any(t["note"] == "注册赠送" for t in txs)


def test_profile_and_password():
    auth.register("zhao_liu", "secret1")
    u = auth.update_profile("zhao_liu", nickname="赵工", region="杭州")
    assert (u.nickname, u.region) == ("赵工", "杭州")
    auth.change_password("zhao_liu", "secret1", "newpass6")
    auth.login("zhao_liu", "newpass6")
    with pytest.raises(auth.AuthError):
        auth.change_password("zhao_liu", "secret1", "xxxxxx")  # 旧密码已失效


def test_api_gating():
    """未登录 401; 登录后运行扣费; 余额耗尽 402。"""
    pytest.importorskip("httpx")  # TestClient 依赖 httpx, 未安装则跳过
    from fastapi.testclient import TestClient

    from app.main import app

    client = TestClient(app, raise_server_exceptions=False)
    # 未登录
    assert client.get("/api/run/case01_single_room/stream").status_code == 401
    # 注册登录
    r = client.post("/api/auth/register",
                    json={"username": "api_user", "password": "secret1"})
    assert r.status_code == 200
    token = r.json()["token"]
    hdr = {"Authorization": f"Bearer {token}"}
    me = client.get("/api/auth/me", headers=hdr).json()
    assert me["balance"] == auth.SIGNUP_BONUS
    # 运行一次流水线(¥1)
    r = client.get(f"/api/run/case01_single_room/stream?token={token}")
    assert r.status_code == 200
    assert client.get("/api/auth/me", headers=hdr).json()["balance"] == \
        auth.SIGNUP_BONUS - auth.PRICING["run_pipeline"]
    # 把余额充公耗尽 -> 402
    for _ in range(int(auth.SIGNUP_BONUS)):
        client.get(f"/api/run/case01_single_room/stream?token={token}")
    r = client.get(f"/api/run/case01_single_room/stream?token={token}")
    assert r.status_code == 402
    # 充值解锁
    client.post("/api/account/recharge", json={"amount": 10}, headers=hdr)
    assert client.get(
        f"/api/run/case01_single_room/stream?token={token}").status_code == 200
