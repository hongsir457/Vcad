import { useEffect, useState } from "react";
import { api, tokenStore, type Transaction, type UserInfo } from "../api";

export type AccountTab = "login" | "profile" | "recharge" | "bills";

/** 登录/注册 + 个人设置 + 充值 + 账单 的账号弹窗。 */
export function AccountModal(props: {
  tab: AccountTab;
  user: UserInfo | null;
  notice?: string;
  onClose: () => void;
  onUser: (u: UserInfo | null) => void;
}) {
  const [tab, setTab] = useState<AccountTab>(props.tab);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState(props.notice ?? "");
  const [ok, setOk] = useState("");

  // 登录/注册表单
  const [mode, setMode] = useState<"login" | "register">("login");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [nickname, setNickname] = useState("");

  // 设置表单
  const [pNick, setPNick] = useState(props.user?.nickname ?? "");
  const [pRegion, setPRegion] = useState(props.user?.region ?? "上海");
  const [oldPw, setOldPw] = useState("");
  const [newPw, setNewPw] = useState("");

  // 充值/账单
  const [amount, setAmount] = useState(100);
  const [bills, setBills] = useState<Transaction[]>([]);

  useEffect(() => {
    if (tab === "bills" && props.user) {
      api.transactions().then(setBills).catch(() => {});
    }
  }, [tab, props.user]);

  const run = async (fn: () => Promise<void>) => {
    setBusy(true);
    setMsg("");
    setOk("");
    try {
      await fn();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  };

  const doAuth = () =>
    run(async () => {
      const res =
        mode === "login"
          ? await api.login(username, password)
          : await api.register(username, password, nickname);
      tokenStore.set(res.token);
      props.onUser(res.user);
      props.onClose();
    });

  const logged = !!props.user;

  return (
    <div className="modal-mask" onClick={props.onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <span>{logged ? "账号中心" : "登录 VCAD"}</span>
          <button className="modal-close" onClick={props.onClose}>×</button>
        </div>

        {logged && (
          <div className="modal-tabs">
            {([["profile", "个人设置"], ["recharge", "充值"], ["bills", "账单明细"]] as const).map(
              ([k, label]) => (
                <button key={k} className={tab === k ? "active" : ""}
                  onClick={() => setTab(k)}>{label}</button>
              ),
            )}
          </div>
        )}

        <div className="modal-body">
          {!logged && (
            <>
              <div className="form-row">
                <label>用户名</label>
                <input value={username} onChange={(e) => setUsername(e.target.value)}
                  placeholder="3~32 位字母/数字/下划线" />
              </div>
              <div className="form-row">
                <label>密码</label>
                <input type="password" value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="至少 6 位"
                  onKeyDown={(e) => e.key === "Enter" && doAuth()} />
              </div>
              {mode === "register" && (
                <div className="form-row">
                  <label>昵称</label>
                  <input value={nickname} onChange={(e) => setNickname(e.target.value)}
                    placeholder="可选" />
                </div>
              )}
              <button className="btn primary" style={{ width: "100%", marginTop: 8 }}
                disabled={busy || !username || !password} onClick={doAuth}>
                {mode === "login" ? "登录" : "注册(赠送 ¥50 体验金)"}
              </button>
              <div className="modal-switch">
                {mode === "login" ? (
                  <>没有账号? <a onClick={() => setMode("register")}>注册</a></>
                ) : (
                  <>已有账号? <a onClick={() => setMode("login")}>登录</a></>
                )}
              </div>
            </>
          )}

          {logged && tab === "profile" && (
            <>
              <div className="form-row">
                <label>用户名</label>
                <input value={props.user!.username} disabled />
              </div>
              <div className="form-row">
                <label>昵称</label>
                <input value={pNick} onChange={(e) => setPNick(e.target.value)} />
              </div>
              <div className="form-row">
                <label>项目地区</label>
                <input value={pRegion} onChange={(e) => setPRegion(e.target.value)}
                  placeholder="用于清单地区口径" />
              </div>
              <button className="btn primary" disabled={busy}
                onClick={() => run(async () => {
                  const u = await api.updateProfile(pNick, pRegion);
                  props.onUser(u);
                  setOk("已保存");
                })}>保存设置</button>

              <div className="modal-divider">修改密码</div>
              <div className="form-row">
                <label>原密码</label>
                <input type="password" value={oldPw}
                  onChange={(e) => setOldPw(e.target.value)} />
              </div>
              <div className="form-row">
                <label>新密码</label>
                <input type="password" value={newPw}
                  onChange={(e) => setNewPw(e.target.value)} />
              </div>
              <button className="btn" disabled={busy || !oldPw || !newPw}
                onClick={() => run(async () => {
                  await api.changePassword(oldPw, newPw);
                  setOldPw(""); setNewPw("");
                  setOk("密码已修改");
                })}>修改密码</button>

              <div className="modal-divider" />
              <button className="btn" style={{ color: "var(--c-error)" }}
                onClick={() => {
                  tokenStore.clear();
                  props.onUser(null);
                  props.onClose();
                }}>退出登录</button>
            </>
          )}

          {logged && tab === "recharge" && (
            <>
              <div className="balance-line">
                当前余额 <b>¥{props.user!.balance.toFixed(2)}</b>
              </div>
              <div className="recharge-options">
                {[50, 100, 300, 500].map((v) => (
                  <button key={v} className={"btn" + (amount === v ? " primary" : "")}
                    onClick={() => setAmount(v)}>¥{v}</button>
                ))}
              </div>
              <div className="form-row">
                <label>金额</label>
                <input type="number" min={1} max={10000} value={amount}
                  onChange={(e) => setAmount(Number(e.target.value))} />
              </div>
              <button className="btn copper" style={{ width: "100%" }} disabled={busy}
                onClick={() => run(async () => {
                  const res = await api.recharge(amount);
                  props.onUser({ ...props.user!, balance: res.balance });
                  setOk(res.note);
                })}>确认充值</button>
              <div className="modal-note">
                本地演示版为模拟支付, 点击后直接到账并计入账单;
                部署生产环境时在 <code>core/auth.py::recharge</code> 对接支付渠道。
                <br />计费: DWG/DXF 解析 ¥2 · PDF(OCR) ¥5 · 建模算量 ¥1/次 · JSON 与评测免费。
              </div>
            </>
          )}

          {logged && tab === "bills" && (
            <table className="data-table">
              <thead>
                <tr><th>时间</th><th>事项</th>
                  <th style={{ textAlign: "right" }}>金额</th>
                  <th style={{ textAlign: "right" }}>余额</th></tr>
              </thead>
              <tbody>
                {bills.map((b, i) => (
                  <tr key={i}>
                    <td className="spec">{new Date(b.ts * 1000).toLocaleString()}</td>
                    <td>{b.note}</td>
                    <td className="num" style={{
                      color: b.amount >= 0 ? "var(--c-success)" : "var(--c-text)" }}>
                      {b.amount >= 0 ? "+" : ""}{b.amount.toFixed(2)}
                    </td>
                    <td className="num">{b.balance.toFixed(2)}</td>
                  </tr>
                ))}
                {bills.length === 0 && (
                  <tr><td colSpan={4} className="spec">暂无交易记录</td></tr>
                )}
              </tbody>
            </table>
          )}

          {msg && <div className="error-banner" style={{ marginTop: 10 }}>{msg}</div>}
          {ok && <div className="ok-banner">{ok}</div>}
        </div>
      </div>
    </div>
  );
}
