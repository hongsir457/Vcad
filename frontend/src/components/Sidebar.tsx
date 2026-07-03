import type { DrawingMeta } from "../types";

const PARAM_LABELS: Record<string, string> = {
  snap_tol_mm: "坐标吸附容差 (mm)",
  min_deduct_area_m2: "洞口扣减下限 (m²)",
  beam_to_column_face: "梁长算至柱侧面",
  beam_height_to_slab_bottom: "整浇梁高算至板底",
};

export function Sidebar(props: {
  drawings: DrawingMeta[];
  selectedId: string;
  onSelect: (id: string) => void;
  params: Record<string, unknown>;
}) {
  return (
    <aside className="col-left">
      <div className="section-label">图纸库</div>
      {props.drawings.map((d) => (
        <div
          key={d.id}
          className={"drawing-item" + (d.id === props.selectedId ? " active" : "")}
          onClick={() => props.onSelect(d.id)}
        >
          <div className="name">{d.name}</div>
          <div className="desc">{d.desc}</div>
          <div className="meta">项目地区: {d.region}</div>
        </div>
      ))}

      <div className="section-label">当前计量口径参数</div>
      <div className="params-box">
        {Object.entries(props.params).map(([k, v]) => (
          <div className="kv" key={k}>
            <span className="k">{PARAM_LABELS[k] ?? k}</span>
            <span>{typeof v === "boolean" ? (v ? "是" : "否") : String(v)}</span>
          </div>
        ))}
        {Object.keys(props.params).length === 0 && (
          <div style={{ color: "var(--c-text-muted)" }}>使用默认参数</div>
        )}
      </div>
    </aside>
  );
}
