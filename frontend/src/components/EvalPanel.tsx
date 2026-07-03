import type { EvalReport, HistoryRecord } from "../types";

function pct(v: number | null | undefined): string {
  return v == null ? "—" : (v * 100).toFixed(1) + "%";
}

export function EvalPanel(props: {
  report: EvalReport | null;
  history: HistoryRecord[];
  loopLogs: string[];
  busy: boolean;
  onRunEval: () => void;
  onRunLoop: (fromScratch: boolean) => void;
}) {
  const { report, history } = props;
  const s = report?.summary;

  return (
    <div className="eval-panel">
      <div className="eval-actions">
        <button className="btn primary" disabled={props.busy} onClick={props.onRunEval}>
          运行基准评测
        </button>
        <button className="btn" disabled={props.busy} onClick={() => props.onRunLoop(false)}>
          自动迭代调参
        </button>
        <button className="btn copper" disabled={props.busy} onClick={() => props.onRunLoop(true)}>
          从朴素基线自动生长
        </button>
      </div>

      {!report && (
        <div style={{ color: "var(--c-text-muted)", fontSize: 13, lineHeight: 1.9 }}>
          评价体系: 以人工手算基准为标尺, 逐条比对清单量。
          <br />· 准确率 — 单项偏差 ≤ 0.5% 的条目占比
          <br />· 召回率 — 无漏项; 精确率 — 无多项
          <br />· 稳定性 — 实体乱序 + 坐标抖动扰动重跑, 结果应完全一致
          <br />· 迭代循环 — 评测 → 调参(爬山+二阶变异) → 再评测, 自动收敛
        </div>
      )}

      {s && (
        <div className="stat-row">
          <div className="stat-tile">
            <div className="label">综合分</div>
            <div className={"value " + (s.score >= 0.999 ? "good" : s.score < 0.9 ? "bad" : "")}>
              {(s.score * 100).toFixed(1)}
            </div>
          </div>
          <div className="stat-tile">
            <div className="label">准确率(±0.5%以内)</div>
            <div className="value">{pct(s.accuracy)}</div>
          </div>
          <div className="stat-tile">
            <div className="label">召回率(无漏项)</div>
            <div className="value">{pct(s.recall)}</div>
          </div>
          <div className="stat-tile">
            <div className="label">稳定性(扰动重跑)</div>
            <div className="value">{pct(s.stability)}</div>
          </div>
        </div>
      )}

      {props.loopLogs.length > 0 && (
        <div className="loop-log">{props.loopLogs.join("\n")}</div>
      )}

      {history.length > 0 && (
        <div style={{ margin: "18px 0" }}>
          <div style={{ fontWeight: 600, marginBottom: 6 }}>迭代生长史</div>
          <div className="history-list">
            {history.slice(-14).map((h, i) => (
              <div key={i} className={"history-item " + h.event}>
                <span className="gen">gen {h.generation}</span>
                <span>
                  {h.event === "baseline" && "基线评测"}
                  {h.event === "improved" && (h.change ?? "参数改进")}
                  {h.event === "converged" && "收敛停止"}
                </span>
                <span className="score">{(h.summary.score * 100).toFixed(1)}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {report?.cases.map((c) => (
        <div className="eval-case" key={c.case}>
          <div className="head">
            {c.name}
            <span className="sub">
              准确率 {pct(c.accuracy)} · 召回 {pct(c.recall)} · 精确率 {pct(c.precision)}
              {c.stability && <> · 稳定性 {pct(c.stability.stability)} ({c.stability.consistent}/{c.stability.runs})</>}
            </span>
          </div>
          <table className="data-table">
            <thead>
              <tr>
                <th>编码</th>
                <th>名称</th>
                <th style={{ textAlign: "right" }}>基准量</th>
                <th style={{ textAlign: "right" }}>计算量</th>
                <th style={{ textAlign: "right" }}>偏差</th>
                <th>判定</th>
              </tr>
            </thead>
            <tbody>
              {c.rows.map((r, i) => (
                <tr key={i}>
                  <td className="code">
                    {r.code}
                    {r.match && <span className="spec"> {r.match}</span>}
                  </td>
                  <td>{r.name}</td>
                  <td className="num">{r.gt_qty == null ? "—" : r.gt_qty}</td>
                  <td className="num">{r.qty == null ? "—" : r.qty}</td>
                  <td className="num">{r.rel_err == null ? "—" : (r.rel_err * 100).toFixed(2) + "%"}</td>
                  <td>
                    <span
                      className={
                        "status-chip " +
                        (r.status === "通过" ? "ok" : r.status === "超差" ? "fail" : "warn")
                      }
                    >
                      {r.status}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}
    </div>
  );
}
