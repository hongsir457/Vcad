import { useRef, useState } from "react";
import { api } from "../api";
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
  onChanged: (selectId?: string) => void;
  onAuthError: (e: unknown) => boolean;
  onBalanceChanged: () => void;
  params: Record<string, unknown>;
}) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState("");

  const doUpload = async (file: File) => {
    setUploading(true);
    setError("");
    try {
      const res = await api.upload(file);
      props.onChanged(res.id);
      props.onBalanceChanged();
    } catch (e) {
      if (!props.onAuthError(e)) {
        setError(e instanceof Error ? e.message.slice(0, 300) : String(e));
      }
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  };

  const doDelete = async (id: string) => {
    try {
      await api.deleteDrawing(id);
      props.onChanged();
    } catch (e) {
      if (!props.onAuthError(e)) {
        setError(e instanceof Error ? e.message.slice(0, 200) : String(e));
      }
    }
  };

  const samples = props.drawings.filter((d) => d.source !== "upload");
  const uploads = props.drawings.filter((d) => d.source === "upload");

  const item = (d: DrawingMeta, deletable: boolean) => (
    <div
      key={d.id}
      className={"drawing-item" + (d.id === props.selectedId ? " active" : "")}
      onClick={() => props.onSelect(d.id)}
    >
      <div className="name">
        {d.name}
        {deletable && (
          <button
            className="del-btn"
            title="删除"
            onClick={(e) => {
              e.stopPropagation();
              doDelete(d.id);
            }}
          >
            ×
          </button>
        )}
      </div>
      <div className="desc">{d.desc}</div>
      <div className="meta">项目地区: {d.region || "—"}</div>
    </div>
  );

  return (
    <aside className="col-left">
      <div className="section-label">上传图纸</div>
      <div style={{ padding: "0 14px 6px" }}>
        <button
          className="btn"
          style={{ width: "100%" }}
          disabled={uploading}
          onClick={() => fileRef.current?.click()}
        >
          {uploading ? "解析中…(PDF 含 OCR 需数分钟)" : "上传 DWG / DXF / PDF / JSON"}
        </button>
        <input
          ref={fileRef}
          type="file"
          accept=".dwg,.dxf,.pdf,.json"
          style={{ display: "none" }}
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) void doUpload(f);
          }}
        />
        {error && (
          <div style={{ color: "var(--c-error)", fontSize: 12, marginTop: 6, lineHeight: 1.5 }}>
            {error}
          </div>
        )}
      </div>

      {uploads.length > 0 && (
        <>
          <div className="section-label">已上传</div>
          {uploads.map((d) => item(d, true))}
        </>
      )}

      <div className="section-label">内置样例</div>
      {samples.map((d) => item(d, false))}

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
