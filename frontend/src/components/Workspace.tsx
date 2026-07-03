import { api } from "../api";
import type { EvalReport, HistoryRecord, RawDrawing, RunResult } from "../types";
import { BoqTable } from "./BoqTable";
import { CalcSheet } from "./CalcSheet";
import { Drawing2D } from "./Drawing2D";
import { CATEGORY_LEGEND, Viewer3D } from "./Viewer3D";
import { EvalPanel } from "./EvalPanel";

export type WorkTab = "2d" | "3d" | "boq" | "calc" | "eval";

export function Workspace(props: {
  tab: WorkTab;
  onTab: (t: WorkTab) => void;
  rawDrawing: RawDrawing | null;
  result: RunResult | null;
  drawingId: string;
  evalReport: EvalReport | null;
  evalHistory: HistoryRecord[];
  loopLogs: string[];
  evalBusy: boolean;
  onRunEval: () => void;
  onRunLoop: (fromScratch: boolean) => void;
}) {
  const { tab, result } = props;
  const dark = tab === "2d" || tab === "3d";

  return (
    <main className="col-work">
      <div className="tabs">
        <button className={tab === "2d" ? "active" : ""} onClick={() => props.onTab("2d")}>
          二维图纸
        </button>
        <button className={tab === "3d" ? "active" : ""} onClick={() => props.onTab("3d")}>
          三维模型
          {result && <span className="badge">{result.model.elements.length}</span>}
        </button>
        <button className={tab === "boq" ? "active" : ""} onClick={() => props.onTab("boq")}>
          工程量清单
          {result && <span className="badge">{result.boq.length}</span>}
        </button>
        <button className={tab === "calc" ? "active" : ""} onClick={() => props.onTab("calc")}>
          计算书
        </button>
        <button className={tab === "eval" ? "active" : ""} onClick={() => props.onTab("eval")}>
          评测
          {props.evalReport && (
            <span className="badge">{(props.evalReport.summary.score * 100).toFixed(0)}</span>
          )}
        </button>
      </div>

      <div className={"work-body" + (dark ? " dark" : "")}>
        {tab === "2d" &&
          (props.rawDrawing ? (
            <div style={{ height: "100%", background: "#f8fafb" }}>
              <Drawing2D drawing={props.rawDrawing} />
            </div>
          ) : (
            <div className="canvas-empty">左侧选择图纸后在此预览</div>
          ))}

        {tab === "3d" &&
          (result ? (
            <>
              <Viewer3D model={result.model} />
              <div className="legend">
                {CATEGORY_LEGEND.map(([k, color, label]) => (
                  <span key={k}>
                    <span className="sw" style={{ background: color }} />
                    {label}
                  </span>
                ))}
              </div>
              {result.model.issues.length > 0 && (
                <div className="toolbar-float">
                  <span className="status-chip warn">
                    自检 {result.model.issues.length} 项待复核
                  </span>
                </div>
              )}
            </>
          ) : (
            <div className="canvas-empty">
              尚无三维模型
              <br />
              在中间对话框发送「对当前图纸自动建模并计算工程量」
            </div>
          ))}

        {tab === "boq" &&
          (result ? (
            <>
              <div className="toolbar-float">
                <a
                  className="btn"
                  href={api.boqCsvUrl(props.drawingId)}
                  download
                  style={{ textDecoration: "none" }}
                >
                  导出清单 CSV
                </a>
              </div>
              {result.model.issues.length > 0 && (
                <div className="issues-banner">
                  模型自检发现 {result.model.issues.length} 项问题:{" "}
                  {result.model.issues.join("；")}
                </div>
              )}
              <BoqTable boq={result.boq} checks={result.checks} />
            </>
          ) : (
            <div className="canvas-empty">运行流水线后在此查看清单</div>
          ))}

        {tab === "calc" &&
          (result ? (
            <CalcSheet boq={result.boq} />
          ) : (
            <div className="canvas-empty">运行流水线后在此查看逐项手算过程</div>
          ))}

        {tab === "eval" && (
          <EvalPanel
            report={props.evalReport}
            history={props.evalHistory}
            loopLogs={props.loopLogs}
            busy={props.evalBusy}
            onRunEval={props.onRunEval}
            onRunLoop={props.onRunLoop}
          />
        )}
      </div>
    </main>
  );
}
