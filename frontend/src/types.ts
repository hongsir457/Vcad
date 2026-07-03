// 与后端事件协议/数据结构对应的类型定义

export interface DrawingMeta {
  id: string;
  name: string;
  region: string;
  desc: string;
}

export interface RawEntity {
  layer: string;
  type: string;
  p1?: number[];
  p2?: number[];
  at?: number[];
  pts?: number[][];
  tag?: string;
  label?: string;
  width?: number;
  sill?: number;
}

export interface RawDrawing {
  meta: { name: string; desc?: string; region?: string; scale?: string };
  levels: { name: string; elev: number; height: number }[];
  entities: RawEntity[];
}

export interface Primitive {
  kind: "box" | "extrude";
  eid: string;
  category: string;
  center?: number[];
  size?: number[];
  rot?: number;
  points?: number[][];
  z0?: number;
  z1?: number;
}

export interface ModelElement {
  eid: string;
  category: string;
  tag: string;
  level: string;
  params: Record<string, unknown>;
  primitives: Primitive[];
}

export interface BuildingModel {
  drawing_name: string;
  region: string;
  issues: string[];
  elements: ModelElement[];
}

export interface CalcLine {
  kind: "rule" | "formula" | "subst" | "deduct" | "sum" | "result";
  text: string;
}

export interface BoqItem {
  code: string;
  name: string;
  spec: string[];
  unit: string;
  qty: number;
  calc: CalcLine[];
  elements: string[];
  extra: Record<string, number>;
}

export interface CrossCheck {
  item: string;
  geometry: number;
  boq: number;
  deviation: number;
  pass: boolean;
  note: string;
}

export interface RunResult {
  model: BuildingModel;
  boq: BoqItem[];
  checks: CrossCheck[];
  params: Record<string, unknown>;
}

// --- 流水线事件 ---

export interface PipelineEvent {
  type: "phase" | "step_start" | "step_log" | "step_done" | "result" | "error";
  phase?: string;
  step?: string;
  title?: string;
  text?: string;
  summary?: string;
  message?: string;
  payload?: Record<string, unknown>;
}

export interface StepState {
  id: string;
  title: string;
  status: "running" | "done";
  logs: string[];
  summary?: string;
}

export interface PhaseState {
  id: string;
  title: string;
  steps: StepState[];
}

export interface ChatMessage {
  role: "user" | "agent";
  text?: string;
  phases?: PhaseState[];
  error?: string;
  done?: boolean;
}

// --- 评测 ---

export interface EvalRow {
  code: string;
  name: string;
  match: string;
  unit: string;
  gt_qty: number | null;
  qty: number | null;
  rel_err: number | null;
  status: string;
}

export interface EvalCase {
  case: string;
  name: string;
  rows: EvalRow[];
  accuracy: number;
  recall: number;
  precision: number;
  issues: string[];
  stability?: { runs: number; consistent: number; stability: number; max_dev: number };
}

export interface EvalReport {
  params: Record<string, unknown>;
  cases: EvalCase[];
  summary: {
    case_count: number;
    accuracy: number;
    recall: number;
    stability: number | null;
    score: number;
  };
}

export interface HistoryRecord {
  ts: number;
  generation: number;
  event: string;
  change?: string;
  params: Record<string, unknown>;
  summary: EvalReport["summary"];
}
