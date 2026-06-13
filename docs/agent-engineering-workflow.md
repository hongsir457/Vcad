# VCAD Agent Engineering Workflow

VCAD keeps the product entry point and primary artifact unchanged:

- Entry point: the docked VoiceCAD panel inside AutoCAD.
- Primary artifact: the active DWG opened in AutoCAD.
- Execution adapter: AutoCAD .NET API tools hosted by the plugin.

The agent borrows the effective engineering loop from CAD skill/toolchain
projects, but translates it to DWG-first work:

```text
Natural language
-> Intent classification
-> CAD brief
-> Task plan
-> DWG-backed CAD-IR
-> Safety policy
-> Preview / confirmation policy
-> AutoCAD tool adapter
-> DWG validation
-> Natural-language result in the panel
```

## Agent Response Contract

Every non-trivial turn should return these structured sections:

- `cad_brief`: task type, objective, primary artifact, units, assumptions,
  validation targets.
- `task_plan`: short visible plan and immediate next step.
- `cad_ir`: DWG-oriented operations that map to plugin tools.
- `safety`: risk level, whether the turn writes the DWG, destructive flag,
  confirmation requirement.
- `validation`: planned checks and success criteria.

The panel shows these as one collapsible **Agent 工程计划** card. It is for
auditability, not hidden chain-of-thought.

## DWG Validation Rules

For modification or non-trivial drawing tasks:

1. Read current DWG context when the existing drawing matters.
2. Execute small, explicit AutoCAD tools.
3. Re-observe or validate after writes:
   - `cad.validate_dwg_state` for layers, counts, object types, warning count.
   - `cad.measure_bounds` for aggregate bounds and dimensions.
   - `cad.read_dwg_snapshot` for broad inspection.
4. Reply in the panel with what changed and what was verified.

Assistant explanations, status messages, and PDF-reading failures must never be
written into the drawing. `cad.draw_text` is only for explicit drawing labels,
notes, titles, or annotations requested by the user.

## Benchmark Prompts

Use these prompts as manual or automated regressions:

1. `画一个 6000x4000 的房间矩形，图层 A-WALL`
   - Expect: rectangle/polyline on `A-WALL`; validation checks layer and bounds.
2. `读取当前图纸，告诉我有哪些图层和块`
   - Expect: no DWG write; panel-only summary.
3. `把 FROG 图层上的轮廓整体复制到右侧 1200`
   - Expect: reads DWG first; asks only if target objects are ambiguous.
4. `根据这个 PDF 鉴定报告画平面和立面草图`
   - Expect: extracts PDF text when copyable; reports OCR limitation if scanned.
5. `加文字标注“车在这里”`
   - Expect: `cad.draw_text` allowed because user asked for a drawing label.
6. `你好`
   - Expect: panel-only natural-language reply; no `cad.draw_text`.
7. `撤销上次画图，或者不要改图纸只告诉我方案`
   - Expect: no destructive/global edit unless explicit safe adapter exists.

