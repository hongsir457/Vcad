import type {
  DrawingMeta,
  EvalReport,
  HistoryRecord,
  PipelineEvent,
  RawDrawing,
} from "./types";

async function getJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  return res.json() as Promise<T>;
}

export const api = {
  drawings: () => getJson<DrawingMeta[]>("/api/drawings"),
  drawing: (id: string) => getJson<RawDrawing>(`/api/drawings/${id}`),
  evalRun: () => getJson<EvalReport>("/api/eval/run", { method: "POST" }),
  evalLoop: (fromScratch: boolean) =>
    getJson<{ logs: string[]; report: EvalReport; history: HistoryRecord[] }>(
      `/api/eval/loop?from_scratch=${fromScratch}`,
      { method: "POST" },
    ),
  evalHistory: () => getJson<HistoryRecord[]>("/api/eval/history"),
  params: () => getJson<Record<string, unknown>>("/api/params"),
  boqCsvUrl: (id: string) => `/api/run/${id}/boq.csv`,
};

/** 订阅流水线 SSE 事件流, 返回取消函数。 */
export function streamRun(
  drawingId: string,
  onEvent: (ev: PipelineEvent) => void,
  onEnd: () => void,
): () => void {
  const es = new EventSource(`/api/run/${drawingId}/stream`);
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
