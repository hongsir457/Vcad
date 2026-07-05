import type {
  DrawingMeta,
  EvalReport,
  HistoryRecord,
  PipelineEvent,
  RawDrawing,
} from "./types";

export interface UserInfo {
  username: string;
  nickname: string;
  region: string;
  balance: number;
}

export interface Transaction {
  amount: number;
  balance: number;
  note: string;
  ts: number;
}

// ---- 令牌管理 ----

const TOKEN_KEY = "vcad_token";

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY) ?? "",
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => localStorage.removeItem(TOKEN_KEY),
};

function authHeaders(): Record<string, string> {
  const t = tokenStore.get();
  return t ? { Authorization: `Bearer ${t}` } : {};
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function getJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    ...init,
    headers: { ...(init?.headers ?? {}), ...authHeaders() },
  });
  if (!res.ok) {
    let msg = `${res.status}`;
    try {
      const body = await res.json();
      msg = body.detail ?? JSON.stringify(body);
    } catch {
      msg = await res.text().catch(() => `${res.status}`);
    }
    throw new ApiError(res.status, msg);
  }
  return res.json() as Promise<T>;
}

function postJson<T>(url: string, body: unknown): Promise<T> {
  return getJson<T>(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export const api = {
  drawings: () => getJson<DrawingMeta[]>("/api/drawings"),
  drawing: (id: string) => getJson<RawDrawing>(`/api/drawings/${id}`),
  upload: (file: File) => {
    const fd = new FormData();
    fd.append("file", file);
    return getJson<{ id: string; name: string; recognition: string[] }>(
      "/api/drawings/upload",
      { method: "POST", body: fd },
    );
  },
  deleteDrawing: (id: string) =>
    getJson<{ ok: boolean }>(`/api/drawings/${id}`, { method: "DELETE" }),
  evalRun: () => getJson<EvalReport>("/api/eval/run", { method: "POST" }),
  evalLoop: (fromScratch: boolean) =>
    getJson<{ logs: string[]; report: EvalReport; history: HistoryRecord[] }>(
      `/api/eval/loop?from_scratch=${fromScratch}`,
      { method: "POST" },
    ),
  evalHistory: () => getJson<HistoryRecord[]>("/api/eval/history"),
  params: () => getJson<Record<string, unknown>>("/api/params"),
  boqCsvUrl: (id: string) =>
    `/api/run/${id}/boq.csv?token=${encodeURIComponent(tokenStore.get())}`,

  // ---- 账号 ----
  register: (username: string, password: string, nickname: string) =>
    postJson<{ token: string; user: UserInfo }>("/api/auth/register", {
      username, password, nickname,
    }),
  login: (username: string, password: string) =>
    postJson<{ token: string; user: UserInfo }>("/api/auth/login", {
      username, password,
    }),
  me: () => getJson<UserInfo>("/api/auth/me"),
  recharge: (amount: number) =>
    postJson<{ balance: number; note: string }>("/api/account/recharge", { amount }),
  transactions: () => getJson<Transaction[]>("/api/account/transactions"),
  updateProfile: (nickname: string, region: string) =>
    postJson<UserInfo>("/api/account/profile", { nickname, region }),
  changePassword: (old: string, newPw: string) =>
    postJson<{ ok: boolean }>("/api/account/password", { old, new: newPw }),
  pricing: () => getJson<Record<string, number>>("/api/pricing"),
};

/** 订阅流水线 SSE 事件流(令牌经查询参数传递), 返回取消函数。 */
export function streamRun(
  drawingId: string,
  onEvent: (ev: PipelineEvent) => void,
  onEnd: () => void,
): () => void {
  const es = new EventSource(
    `/api/run/${drawingId}/stream?token=${encodeURIComponent(tokenStore.get())}`,
  );
  es.onmessage = (msg) => {
    try {
      onEvent(JSON.parse(msg.data) as PipelineEvent);
    } catch {
      /* 忽略无法解析的心跳行 */
    }
  };
  es.addEventListener("end", () => {
    es.close();
    onEnd();
  });
  es.onerror = () => {
    es.close();
    onEnd();
  };
  return () => es.close();
}
