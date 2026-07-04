#!/usr/bin/env node
/**
 * DWG/DXF -> 通用实体 JSON 提取器 (基于 @mlightcad/libredwg-web 的 WASM LibreDWG)
 *
 * 用法: node dwg2json.mjs <input.dwg|input.dxf> <output.json>
 *
 * 从模型空间出发递归炸开所有块引用(INSERT), 把几何变换到世界坐标,
 * 输出扁平实体流。INSERT 本身也保留一条记录(块名对识别有语义价值,
 * 如钢柱符号块 "C1")。
 *
 * 输出实体:
 *   {"type":"LINE","layer":"...","p1":[x,y],"p2":[x,y],"block":"路径"}
 *   {"type":"TEXT"|"MTEXT"|"ATTRIB","layer":"...","at":[x,y],"text":"...","height":h}
 *   {"type":"INSERT","layer":"...","at":[x,y],"name":"C1","rotation":rad}
 *   {"type":"LWPOLYLINE","layer":"...","pts":[[x,y]...],"closed":bool,"width":w}
 *   {"type":"ARC"|"CIRCLE","layer":"...","at":[x,y],"r":r}
 *   {"type":"HATCH","layer":"...","at":[x,y]}
 * 坐标单位与模型空间一致(通常 mm)。
 */

import fs from "fs";
import { LibreDwg, Dwg_File_Type } from "@mlightcad/libredwg-web";

const [, , input, output] = process.argv;
if (!input || !output) {
  console.error("用法: node dwg2json.mjs <input.dwg|input.dxf> <output.json>");
  process.exit(2);
}

const lib = await LibreDwg.create();
const buf = fs.readFileSync(input);
const fileType = input.toLowerCase().endsWith(".dxf")
  ? Dwg_File_Type.DXF
  : Dwg_File_Type.DWG;
const dwg = lib.dwg_read_data(buf.buffer, fileType);
if (!dwg) {
  console.error("无法读取图纸文件");
  process.exit(1);
}
const db = lib.convert(dwg);

const records = db.tables?.BLOCK_RECORD?.entries ?? [];
const blockByName = new Map(records.map((r) => [r.name, r]));
const modelSpace = records.find((r) => r.name === "*MODEL_SPACE");
if (!modelSpace) {
  console.error("未找到模型空间");
  process.exit(1);
}

const MAX_ENTITIES = 300000;
const out = [];

/** 仿射变换: 平移 tx,ty + 旋转 rot(弧度) + 缩放 sx,sy */
function makeXform(parent, ins) {
  const rot = ins.rotation ?? 0;
  const sx = ins.xScale || 1;
  const sy = ins.yScale || 1;
  const cos = Math.cos(rot), sin = Math.sin(rot);
  const base = ins.__block?.basePoint ?? { x: 0, y: 0 };
  return (x, y) => {
    // 块内局部坐标 -> 相对基点 -> 缩放 -> 旋转 -> 平移到插入点 -> 父变换
    const lx = (x - (base.x ?? 0)) * sx;
    const ly = (y - (base.y ?? 0)) * sy;
    const wx = ins.insertionPoint.x + lx * cos - ly * sin;
    const wy = ins.insertionPoint.y + lx * sin + ly * cos;
    return parent ? parent(wx, wy) : [wx, wy];
  };
}

const num = (v) => (typeof v === "number" && isFinite(v) ? +v.toFixed(2) : 0);

function emit(e, xf, blockPath) {
  if (out.length >= MAX_ENTITIES) return;
  const P = (p) => {
    if (!p) return [0, 0];
    const [x, y] = xf ? xf(p.x ?? 0, p.y ?? 0) : [p.x ?? 0, p.y ?? 0];
    return [num(x), num(y)];
  };
  const base = { type: e.type, layer: e.layer ?? "0" };
  if (blockPath) base.block = blockPath;

  switch (e.type) {
    case "LINE":
      out.push({ ...base, p1: P(e.startPoint), p2: P(e.endPoint) });
      break;
    case "TEXT":
    case "ATTRIB":
      out.push({
        ...base,
        at: P(e.startPoint ?? e.insertionPoint ?? e.position),
        text: e.text ?? "",
        height: num(e.textHeight ?? 0),
      });
      break;
    case "MTEXT":
      out.push({
        ...base,
        at: P(e.insertionPoint ?? e.startPoint ?? e.position),
        text: (e.text ?? "").replace(/\\[A-Za-z][^;]*;|[{}]/g, ""),
        height: num(e.textHeight ?? 0),
      });
      break;
    case "LWPOLYLINE":
    case "POLYLINE_2D":
      out.push({
        ...base,
        pts: (e.vertices ?? []).map((v) => P(v)),
        closed: !!((e.flag ?? 0) & 1 || e.closed),
        width: num(e.constantWidth ?? 0),
      });
      break;
    case "ARC":
    case "CIRCLE":
      out.push({ ...base, at: P(e.center), r: num(e.radius) });
      break;
    case "HATCH": {
      const path = e.boundaryPaths?.[0];
      const verts = (path?.edges ?? path?.vertices ?? [])
        .map((v) => v?.start ?? v)
        .filter((v) => v && typeof v.x === "number");
      if (verts.length) {
        const cx = verts.reduce((s, v) => s + (v.x ?? 0), 0) / verts.length;
        const cy = verts.reduce((s, v) => s + (v.y ?? 0), 0) / verts.length;
        out.push({ ...base, at: P({ x: cx, y: cy }) });
      }
      break;
    }
    default:
      break;
  }
}

function walk(entities, xf, blockPath, depth) {
  for (const e of entities ?? []) {
    if (out.length >= MAX_ENTITIES) return;
    if (e.type === "INSERT") {
      const rec = blockByName.get(e.name);
      // 记录 INSERT 本身(块名承载语义, 如柱符号块)
      const at = e.insertionPoint ?? { x: 0, y: 0 };
      const [wx, wy] = xf ? xf(at.x, at.y) : [at.x, at.y];
      out.push({
        type: "INSERT", layer: e.layer ?? "0",
        at: [num(wx), num(wy)], name: e.name ?? "",
        rotation: num(e.rotation ?? 0),
        ...(blockPath ? { block: blockPath } : {}),
      });
      if (rec && depth < 6) {
        const childXf = makeXform(xf, { ...e, __block: rec });
        walk(rec.entities, childXf, blockPath ? `${blockPath}/${e.name}` : e.name, depth + 1);
      }
      // INSERT 挂接的属性文字(已是世界坐标系下的父空间坐标)
      for (const a of e.attribs ?? []) emit(a, xf, blockPath);
    } else {
      emit(e, xf, blockPath);
    }
  }
}

walk(modelSpace.entities, null, "", 0);

fs.writeFileSync(
  output,
  JSON.stringify({ source: input.split("/").pop(), entities: out }),
);
console.error(`模型空间递归展开 ${out.length} 个实体 -> ${output}`);
