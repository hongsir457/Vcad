import { useEffect, useRef, useState } from "react";
import type { ChatMessage, StepState } from "../types";

function StepCard({ step }: { step: StepState }) {
  const [open, setOpen] = useState(false);
  const hasLogs = step.logs.length > 0;
  return (
    <div className="step-card">
      <div className="step-head" onClick={() => hasLogs && setOpen(!open)}>
        <span className="icon">
          {step.status === "running" ? <span className="spinner" /> : <span className="check">✓</span>}
        </span>
        <span className="title">{step.title}</span>
        {step.summary && <span className="summary">{step.summary}</span>}
        {hasLogs && <span className="caret">{open ? "▲" : "▼"}</span>}
      </div>
      {(open || step.status === "running") && hasLogs && (
        <div className="step-body">
          {step.logs.map((log, i) => (
            <div key={i} className={"log" + (log.startsWith("⚠") ? " warn" : "")}>
              {log}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Message({ msg }: { msg: ChatMessage }) {
  if (msg.role === "user") {
    return (
      <div className="msg">
        <div className="msg-user">{msg.text}</div>
      </div>
    );
  }
  return (
    <div className="msg msg-agent">
      {msg.text && <div className="agent-text" style={{ whiteSpace: "pre-wrap" }}>{msg.text}</div>}
      {msg.phases?.map((phase) => (
        <div className="phase-block" key={phase.id}>
          <div className="phase-title">{phase.title}</div>
          {phase.steps.map((s) => (
            <StepCard key={s.id} step={s} />
          ))}
        </div>
      ))}
      {msg.error && <div className="error-banner">执行出错: {msg.error}</div>}
      {!msg.done && !msg.phases?.length && !msg.text && (
        <div style={{ padding: 8 }}>
          <span className="spinner" style={{ display: "inline-block" }} />
        </div>
      )}
    </div>
  );
}

export function ChatPanel(props: {
  messages: ChatMessage[];
  busy: boolean;
  disabled: boolean;
  onSend: (text: string) => void;
}) {
  const [input, setInput] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = scrollRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [props.messages]);

  const send = (text: string) => {
    props.onSend(text);
    setInput("");
  };

  const quick = [
    "对当前图纸自动建模并计算工程量",
    "运行基准评测",
    "自动迭代调参",
    "从朴素基线自动生长",
  ];

  return (
    <section className="col-chat">
      <div className="chat-scroll" ref={scrollRef}>
        {props.messages.map((m, i) => (
          <Message key={i} msg={m} />
        ))}
      </div>
      <div className="chat-input-zone">
        <div className="quick-actions">
          {quick.map((q) => (
            <button key={q} disabled={props.busy || props.disabled} onClick={() => send(q)}>
              {q}
            </button>
          ))}
        </div>
        <div className="input-row">
          <textarea
            value={input}
            placeholder="输入指令, 如: 对当前图纸自动建模并计算工程量  (Enter 发送)"
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                if (!props.busy && input.trim()) send(input);
              }
            }}
          />
          <button
            className="send"
            disabled={props.busy || props.disabled || !input.trim()}
            onClick={() => send(input)}
          >
            {props.busy ? "运行中" : "发送"}
          </button>
        </div>
      </div>
    </section>
  );
}
