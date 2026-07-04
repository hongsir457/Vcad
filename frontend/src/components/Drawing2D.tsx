import { useMemo } from "react";
import type { RawDrawing, RawEntity } from "../types";

/** 二维图纸 SVG 渲染: 按图层还原施工图表达(轴线/柱/梁/墙/门窗/板)。 */

const SECTION_RE = /(\d+(?:\.\d+)?)\s*[xX×]\s*(\d+(?:\.\d+)?)/;

function sectionOf(tag?: string): [number, number] {
  const m = tag?.match(SECTION_RE);
  return m ? [parseFloat(m[1]), parseFloat(m[2])] : [300, 300];
}

function doorWinSize(tag?: string): number {
  const m = tag?.match(/^[MC](\d{2})\d{2}$/);
  return m ? parseInt(m[1]) * 100 : 900;
}

/** 门窗插入点所在墙的方向: 水平(0)或竖直(90)。 */
function wallAngleAt(ents: RawEntity[], at: number[]): number {
  let best = 0;
  let bestD = Infinity;
  for (const e of ents) {
    if (e.layer !== "WALL" || !e.p1 || !e.p2) continue;
    const [x1, y1] = e.p1;
    const [x2, y2] = e.p2;
    const dx = x2 - x1, dy = y2 - y1;
    const len2 = dx * dx + dy * dy || 1;
    const t = Math.max(0, Math.min(1, ((at[0] - x1) * dx + (at[1] - y1) * dy) / len2));
    const d = Math.hypot(at[0] - (x1 + t * dx), at[1] - (y1 + t * dy));
    if (d < bestD) {
      bestD = d;
      best = Math.abs(dx) >= Math.abs(dy) ? 0 : 90;
    }
  }
  return best;
}

export function Drawing2D({ drawing }: { drawing: RawDrawing }) {
  const { ents, viewBox, texts } = useMemo(() => {
    const ents = drawing.entities;
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    const feed = (x: number, y: number) => {
      minX = Math.min(minX, x); maxX = Math.max(maxX, x);
      minY = Math.min(minY, y); maxY = Math.max(maxY, y);
    };
    for (const e of ents) {
      for (const p of [e.p1, e.p2, e.at, ...(e.pts ?? [])]) {
        if (p) feed(p[0], p[1]);
      }
    }
    const pad = 1200;
    const vb = `${minX - pad} ${-(maxY + pad)} ${maxX - minX + 2 * pad} ${maxY - minY + 2 * pad}`;

    // 文本(轴号/构件标注)统一在未翻转坐标系里排布
    const texts: { x: number; y: number; s: string; cls: string; circle?: boolean }[] = [];
    for (const e of ents) {
      if (e.layer === "AXIS" && e.p1 && e.p2 && e.label) {
        texts.push({ x: e.p1[0], y: e.p1[1], s: e.label, cls: "axis", circle: true });
      }
      if (e.layer === "COL" && e.at && e.tag) {
        texts.push({ x: e.at[0] + 260, y: e.at[1] + 320, s: e.tag.split(" ")[0], cls: "tag" });
      }
      if (e.layer === "SCOL" && e.at && e.tag) {
        texts.push({ x: e.at[0] + 300, y: e.at[1] + 380, s: e.tag, cls: "tag" });
      }
      if (e.layer === "SBEAM" && e.p1 && e.p2 && e.tag) {
        texts.push({
          x: (e.p1[0] + e.p2[0]) / 2, y: (e.p1[1] + e.p2[1]) / 2 + 160,
          s: e.tag, cls: "beam",
        });
      }
      if ((e.layer === "DOOR" || e.layer === "WIN") && e.at && e.tag) {
        texts.push({ x: e.at[0], y: e.at[1] + 330, s: e.tag, cls: "opening" });
      }
      if (e.layer === "BEAM" && e.p1 && e.p2 && e.tag) {
        texts.push({
          x: (e.p1[0] + e.p2[0]) / 2, y: (e.p1[1] + e.p2[1]) / 2 + 140,
          s: e.tag, cls: "beam",
        });
      }
    }
    return { ents, viewBox: vb, texts };
  }, [drawing]);

  return (
    <svg viewBox={viewBox} style={{ width: "100%", height: "100%", display: "block" }}>
      {/* y 翻转组: 图纸坐标系 y 向上 */}
      <g transform="scale(1,-1)">
        {/* 板 */}
        {ents.map((e, i) =>
          e.layer === "SLAB" && e.pts ? (
            <polygon
              key={"s" + i}
              points={e.pts.map((p) => p.join(",")).join(" ")}
              fill="rgba(46,125,167,0.07)"
              stroke="#2e7da7" strokeWidth={16} strokeDasharray="120 80"
            />
          ) : null,
        )}
        {/* 轴线 */}
        {ents.map((e, i) =>
          e.layer === "AXIS" && e.p1 && e.p2 ? (
            <line
              key={"a" + i}
              x1={e.p1[0]} y1={e.p1[1]} x2={e.p2[0]} y2={e.p2[1]}
              stroke="#b33d34" strokeWidth={14} strokeDasharray="500 140 60 140"
            />
          ) : null,
        )}
        {/* 墙 */}
        {ents.map((e, i) =>
          e.layer === "WALL" && e.p1 && e.p2 ? (
            <line
              key={"w" + i}
              x1={e.p1[0]} y1={e.p1[1]} x2={e.p2[0]} y2={e.p2[1]}
              stroke="#5b6b78" strokeWidth={e.width ?? 200} strokeLinecap="butt"
              opacity={0.85}
            />
          ) : null,
        )}
        {/* 梁(虚线双线简化为粗虚线) */}
        {ents.map((e, i) =>
          e.layer === "BEAM" && e.p1 && e.p2 ? (
            <line
              key={"b" + i}
              x1={e.p1[0]} y1={e.p1[1]} x2={e.p2[0]} y2={e.p2[1]}
              stroke="#24313d" strokeWidth={sectionOf(e.tag)[0]}
              strokeDasharray="300 180" opacity={0.5}
            />
          ) : null,
        )}
        {/* 柱 */}
        {ents.map((e, i) => {
          if (e.layer !== "COL" || !e.at) return null;
          const [b, h] = sectionOf(e.tag);
          return (
            <rect
              key={"c" + i}
              x={e.at[0] - b / 2} y={e.at[1] - h / 2} width={b} height={h}
              fill="#101a22"
            />
          );
        })}
        {/* 钢梁(型钢中心线) */}
        {ents.map((e, i) =>
          e.layer === "SBEAM" && e.p1 && e.p2 ? (
            <line
              key={"sb" + i}
              x1={e.p1[0]} y1={e.p1[1]} x2={e.p2[0]} y2={e.p2[1]}
              stroke="#2e5d7d" strokeWidth={160} opacity={0.8}
            />
          ) : null,
        )}
        {/* 钢柱(H 型钢符号: 按截面外轮廓画矩形) */}
        {ents.map((e, i) => {
          if (e.layer !== "SCOL" || !e.at) return null;
          const [h, b] = sectionOf(e.section ?? "300x300");
          return (
            <rect
              key={"sc" + i}
              x={e.at[0] - b / 2} y={e.at[1] - h / 2} width={b} height={h}
              fill="#163a59" stroke="#0f2740" strokeWidth={30}
            />
          );
        })}
        {/* 门窗 */}
        {ents.map((e, i) => {
          if ((e.layer !== "DOOR" && e.layer !== "WIN") || !e.at) return null;
          const w = doorWinSize(e.tag);
          const ang = wallAngleAt(ents, e.at);
          const color = e.layer === "DOOR" ? "#b56b2f" : "#2e7da7";
          return (
            <rect
              key={"o" + i}
              x={-w / 2} y={-150} width={w} height={300}
              fill="#fff" stroke={color} strokeWidth={40}
              transform={`translate(${e.at[0]},${e.at[1]}) rotate(${ang})`}
            />
          );
        })}
      </g>
      {/* 文本(不翻转) */}
      {texts.map((t, i) =>
        t.circle ? (
          <g key={"t" + i}>
            <circle cx={t.x} cy={-t.y} r={280} fill="#fff" stroke="#b33d34" strokeWidth={16} />
            <text x={t.x} y={-t.y + 105} textAnchor="middle" fontSize={330}
              fill="#b33d34" fontWeight={600}>{t.s}</text>
          </g>
        ) : (
          <text key={"t" + i} x={t.x} y={-t.y} textAnchor="middle" fontSize={240}
            fill={t.cls === "opening" ? "#b56b2f" : "#24313d"}
            fontFamily="IBM Plex Mono, monospace">{t.s}</text>
        ),
      )}
    </svg>
  );
}
