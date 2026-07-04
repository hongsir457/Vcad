import { useCallback, useEffect, useRef, useState } from "react";
import { api, streamRun } from "./api";
import { ChatPanel } from "./components/ChatPanel";
import { Sidebar } from "./components/Sidebar";
import { Workspace, type WorkTab } from "./components/Workspace";
import type {
  ChatMessage,
  DrawingMeta,
  EvalReport,
  HistoryRecord,
  PipelineEvent,
  RawDrawing,
  RunResult,
} from "./types";

export default function App() {
  const [drawings, setDrawings] = useState<DrawingMeta[]>([]);
  const [selectedId, setSelectedId] = useState<string>("");
  const [rawDrawing, setRawDrawing] = useState<RawDrawing | null>(null);
  const [params, setParams] = useState<Record<string, unknown>>({});

  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      role: "agent",
      text:
        "你好, 我是 VCAD 建模算量助手。\n" +
        "在左侧选择一张二维图纸, 然后让我「自动建模并计算工程量」——\n" +
        "我会像人工建模一样逐步建立三维模型, 再像手算一样逐项列式计算, " +
        "最终输出符合 GB50500 清单体系的工程量清单。",
      done: true,
    },
  ]);
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<RunResult | null>(null);

  const [evalReport, setEvalReport] = useState<EvalReport | null>(null);
  const [evalHistory, setEvalHistory] = useState<HistoryRecord[]>([]);
  const [loopLogs, setLoopLogs] = useState<string[]>([]);
  const [evalBusy, setEvalBusy] = useState(false);

  const [tab, setTab] = useState<WorkTab>("2d");
  const cancelRef = useRef<(() => void) | null>(null);

  const refreshDrawings = useCallback((selectId?: string) => {
    api.drawings().then((list) => {
      setDrawings(list);
      if (selectId && list.some((d) => d.id === selectId)) {
        setSelectedId(selectId);
      } else if (list.length && !list.some((d) => d.id === selectedId)) {
        setSelectedId(list[0].id);
      }
    });
  }, [selectedId]);

  useEffect(() => {
    api.drawings().then((list) => {
      setDrawings(list);
      if (list.length) setSelectedId(list[0].id);
    });
    api.params().then(setParams).catch(() => {});
    api.evalHistory().then(setEvalHistory).catch(() => {});
  }, []);

  useEffect(() => {
    if (!selectedId) return;
    api.drawing(selectedId).then(setRawDrawing);
    setResult(null);
    setTab("2d");
  }, [selectedId]);

  /** 把 SSE 事件折叠进当前 agent 消息的阶段/步骤树。 */
  const applyEvent = useCallback((ev: PipelineEvent) => {
    if (ev.type === "result") {
      setResult(ev.payload as unknown as RunResult);
      setTab("3d");
      return;
    }
    setMessages((prev) => {
      const msgs = [...prev];
      const msg = { ...msgs[msgs.length - 1] };
      const phases = [...(msg.phases ?? [])];

      if (ev.type === "phase") {
        phases.push({ id: ev.phase!, title: ev.title!, steps: [] });
      } else if (ev.type === "error") {
        msg.error = ev.message;
      } else if (phases.length) {
        const phase = { ...phases[phases.length - 1] };
        const steps = [...phase.steps];
        if (ev.type === "step_start") {
          steps.push({ id: ev.step!, title: ev.title!, status: "running", logs: [] });
        } else {
          const idx = steps.findIndex((s) => s.id === ev.step);
          if (idx >= 0) {
            const st = { ...steps[idx] };
            if (ev.type === "step_log") st.logs = [...st.logs, ev.text!];
            if (ev.type === "step_done") {
              st.status = "done";
              st.summary = ev.summary;
            }
            steps[idx] = st;
          }
        }
        phase.steps = steps;
        phases[phases.length - 1] = phase;
      }
      msg.phases = phases;
      msgs[msgs.length - 1] = msg;
      return msgs;
    });
  }, []);

  const runPipeline = useCallback(
    (userText: string) => {
      if (running || !selectedId) return;
      setRunning(true);
      setResult(null);
      setMessages((prev) => [
        ...prev,
        { role: "user", text: userText },
        { role: "agent", phases: [], done: false },
      ]);
      cancelRef.current = streamRun(selectedId, applyEvent, () => {
        setRunning(false);
        setMessages((prev) => {
          const msgs = [...prev];
          msgs[msgs.length - 1] = { ...msgs[msgs.length - 1], done: true };
          return msgs;
        });
      });
    },
    [running, selectedId, applyEvent],
  );

  const runEval = useCallback(async (userText: string) => {
    setEvalBusy(true);
    setMessages((prev) => [
      ...prev,
      { role: "user", text: userText },
      { role: "agent", text: "正在对全部基准用例执行评测(含扰动稳定性重跑)…", done: false },
    ]);
    setTab("eval");
    try {
      const report = await api.evalRun();
      setEvalReport(report);
      const s = report.summary;
      setMessages((prev) => {
        const msgs = [...prev];
        msgs[msgs.length - 1] = {
          role: "agent",
          text:
            `评测完成: ${s.case_count} 个用例\n` +
            `准确率 ${(s.accuracy * 100).toFixed(1)}% · 召回率 ${(s.recall * 100).toFixed(1)}%` +
            ` · 稳定性 ${((s.stability ?? 0) * 100).toFixed(1)}% · 综合分 ${(s.score * 100).toFixed(1)}\n` +
            "明细见右侧「评测」页签。",
          done: true,
        };
        return msgs;
      });
    } catch (e) {
      setMessages((prev) => {
        const msgs = [...prev];
        msgs[msgs.length - 1] = { role: "agent", error: String(e), done: true };
        return msgs;
      });
    } finally {
      setEvalBusy(false);
    }
  }, []);

  const runLoop = useCallback(async (fromScratch: boolean, userText: string) => {
    setEvalBusy(true);
    setLoopLogs([]);
    setMessages((prev) => [
      ...prev,
      { role: "user", text: userText },
      {
        role: "agent",
        text: fromScratch
          ? "从朴素基线出发, 进入 评测 → 调参 → 再评测 的自动迭代循环…"
          : "以当前参数为起点, 继续自动迭代寻优…",
        done: false,
      },
    ]);
    setTab("eval");
    try {
      const { logs, report, history } = await api.evalLoop(fromScratch);
      setLoopLogs(logs);
      setEvalReport(report);
      setEvalHistory(history);
      api.params().then(setParams).catch(() => {});
      const s = report.summary;
      setMessages((prev) => {
        const msgs = [...prev];
        msgs[msgs.length - 1] = {
          role: "agent",
          text:
            `迭代结束, 最终综合分 ${(s.score * 100).toFixed(1)}` +
            ` (准确率 ${(s.accuracy * 100).toFixed(1)}%, 稳定性 ${((s.stability ?? 0) * 100).toFixed(1)}%)。\n` +
            logs.map((l) => `· ${l}`).join("\n"),
          done: true,
        };
        return msgs;
      });
    } catch (e) {
      setMessages((prev) => {
        const msgs = [...prev];
        msgs[msgs.length - 1] = { role: "agent", error: String(e), done: true };
        return msgs;
      });
    } finally {
      setEvalBusy(false);
    }
  }, []);

  /** 简单意图路由: 评测/迭代类指令走评测接口, 其余触发建模算量流水线。 */
  const handleSend = useCallback(
    (text: string) => {
      const t = text.trim();
      if (!t) return;
      if (/生长|从零|朴素|scratch/i.test(t)) return void runLoop(true, t);
      if (/迭代|调参|寻优|loop/i.test(t)) return void runLoop(false, t);
      if (/评测|评估|测试|基准|benchmark/i.test(t)) return void runEval(t);
      runPipeline(t);
    },
    [runPipeline, runEval, runLoop],
  );

  const busy = running || evalBusy;

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="logo">V</span>
          VCAD
          <span className="sub">二维图纸 · 自动三维建模 · 自动算量</span>
        </div>
        <div className="spacer" />
        {evalReport && (
          <span className="chip">
            评测综合分 {(evalReport.summary.score * 100).toFixed(1)}
          </span>
        )}
        <span className="chip">{busy ? "运行中…" : "就绪"}</span>
      </header>

      <div className="columns">
        <Sidebar
          drawings={drawings}
          selectedId={selectedId}
          onSelect={(id) => !busy && setSelectedId(id)}
          onChanged={refreshDrawings}
          params={params}
        />
        <ChatPanel
          messages={messages}
          busy={busy}
          disabled={!selectedId}
          onSend={handleSend}
        />
        <Workspace
          tab={tab}
          onTab={setTab}
          rawDrawing={rawDrawing}
          result={result}
          drawingId={selectedId}
          evalReport={evalReport}
          evalHistory={evalHistory}
          loopLogs={loopLogs}
          evalBusy={evalBusy}
          onRunEval={() => runEval("运行基准评测")}
          onRunLoop={(fs) =>
            runLoop(fs, fs ? "从朴素基线自动生长" : "自动迭代调参")
          }
        />
      </div>
    </div>
  );
}
