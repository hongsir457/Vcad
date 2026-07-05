import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError, streamRun, tokenStore, type UserInfo } from "./api";
import { AccountModal, type AccountTab } from "./components/AccountModal";
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

  const [user, setUser] = useState<UserInfo | null>(null);
  const [accountTab, setAccountTab] = useState<AccountTab | null>(null);
  const [accountNotice, setAccountNotice] = useState("");

  const [leftOpen, setLeftOpen] = useState(true);
  const [rightOpen, setRightOpen] = useState(true);

  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      role: "agent",
      text:
        "你好, 我是 VCAD 建模算量助手。\n" +
        "在左侧选择一张二维图纸, 然后让我「自动建模并计算工程量」——\n" +
        "我会像人工建模一样逐步建立三维模型, 再像手算一样逐项列式计算, " +
        "最终输出符合 GB50500 清单体系的工程量清单。\n" +
        "(运行与上传需要登录, 注册即送 ¥50 体验金)",
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
  const gotEventRef = useRef(false);

  const openAccount = useCallback((t: AccountTab, notice = "") => {
    setAccountNotice(notice);
    setAccountTab(t);
  }, []);

  /** 401/402 统一入口: 未登录弹登录, 余额不足弹充值。 */
  const handleAuthError = useCallback(
    (e: unknown): boolean => {
      if (e instanceof ApiError && e.status === 401) {
        openAccount("login", "请先登录后再操作");
        return true;
      }
      if (e instanceof ApiError && e.status === 402) {
        openAccount("recharge", e.message);
        return true;
      }
      return false;
    },
    [openAccount],
  );

  const refreshUser = useCallback(() => {
    if (!tokenStore.get()) return;
    api.me().then(setUser).catch(() => {
      tokenStore.clear();
      setUser(null);
    });
  }, []);

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
    refreshUser();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!selectedId) return;
    api.drawing(selectedId).then(setRawDrawing);
    setResult(null);
    setTab("2d");
  }, [selectedId]);

  /** 把 SSE 事件折叠进当前 agent 消息的阶段/步骤树。 */
  const applyEvent = useCallback((ev: PipelineEvent) => {
    gotEventRef.current = true;
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
      if (!user) {
        openAccount("login", "运行建模算量需要登录(注册即送 ¥50)");
        return;
      }
      setRunning(true);
      setResult(null);
      gotEventRef.current = false;
      setMessages((prev) => [
        ...prev,
        { role: "user", text: userText },
        { role: "agent", phases: [], done: false },
      ]);
      cancelRef.current = streamRun(selectedId, applyEvent, () => {
        setRunning(false);
        refreshUser();
        setMessages((prev) => {
          const msgs = [...prev];
          const last = { ...msgs[msgs.length - 1], done: true };
          if (!gotEventRef.current && !last.error) {
            last.error =
              "运行未启动: 请确认已登录且余额充足(建模算量 ¥1/次), 右上角可登录/充值";
          }
          msgs[msgs.length - 1] = last;
          return msgs;
        });
      });
    },
    [running, selectedId, user, applyEvent, refreshUser, openAccount],
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
        {user ? (
          <button className="chip user-chip" onClick={() => openAccount("profile")}>
            {user.nickname || user.username} · ¥{user.balance.toFixed(2)}
          </button>
        ) : (
          <button className="chip user-chip" onClick={() => openAccount("login")}>
            登录 / 注册
          </button>
        )}
      </header>

      <div className="columns">
        {leftOpen && (
          <Sidebar
            drawings={drawings}
            selectedId={selectedId}
            onSelect={(id) => !busy && setSelectedId(id)}
            onChanged={refreshDrawings}
            onAuthError={handleAuthError}
            onBalanceChanged={refreshUser}
            params={params}
          />
        )}
        <div
          className="col-toggle"
          title={leftOpen ? "折叠图纸栏" : "展开图纸栏"}
          onClick={() => setLeftOpen(!leftOpen)}
        >
          {leftOpen ? "‹" : "›"}
        </div>

        <ChatPanel
          messages={messages}
          busy={busy}
          disabled={!selectedId}
          expand={!rightOpen}
          onSend={handleSend}
        />

        <div
          className="col-toggle"
          title={rightOpen ? "折叠工作区" : "展开工作区"}
          onClick={() => setRightOpen(!rightOpen)}
        >
          {rightOpen ? "›" : "‹"}
        </div>
        {rightOpen && (
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
        )}
      </div>

      {accountTab && (
        <AccountModal
          tab={user ? (accountTab === "login" ? "profile" : accountTab) : "login"}
          user={user}
          notice={accountNotice}
          onClose={() => setAccountTab(null)}
          onUser={setUser}
        />
      )}
    </div>
  );
}
