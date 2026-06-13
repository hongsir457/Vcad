# VCAD Agent Benchmark Tasks

Use this file for manual QA and automated regression expansion. The expected
artifact is always the active AutoCAD DWG, and conversational explanations stay
inside the VCAD panel.

## Smoke Benchmarks

1. Draw a rectangle
   - Prompt: `draw a rectangle 6000 x 4000 on layer A-WALL`
   - Expect: `cad.preview_plan`, a rectangle/polyline write tool, and validation
     with layer/count/bounds checks.

2. Inspect the current drawing
   - Prompt: `inspect the current drawing and tell me the layers and blocks`
   - Expect: `cad.read_dwg_snapshot`; no DWG write.

3. Greeting only
   - Prompt: `你好`
   - Expect: panel-only natural-language reply or clarification; no
     `cad.draw_text`.

4. Copy a selected outline
   - Prompt: `copy the outline on layer FROG 1200 units to the right`
   - Expect: observe first; use `layer:FROG` or a more specific selector; ask
     only if the target is ambiguous.

5. Measure existing geometry
   - Prompt: `measure the bounds of layer FROG and the distance to layer DOOR`
   - Expect: `cad.measure_bounds` and/or `cad.measure_distance`; no DWG write.

6. PDF-driven drafting
   - Prompt: `draw the plan and elevation from this appraisal PDF`
   - Expect: use extracted PDF text if available; report OCR limitation if the
     PDF is scanned; ask only for missing dimensions or permission to OCR/retry.

7. Explicit annotation
   - Prompt: `add the label "car here" at x=1200,y=800`
   - Expect: `cad.draw_text` is allowed because the user requested drawing text.

8. Before/after validation
   - Prompt: `after drawing, show what changed`
   - Expect: preserve or request a previous snapshot, then use
     `cad.before_after_diff` or `cad.layer_diff`.

9. U-shaped double-run stair
   - Prompt: `画一个U型双跑楼梯，宽度1200，踏步250，踢步150，层高3900`
   - Expect: `cad.draw_stair` with a bounded plan, no assistant explanation text
     written into the DWG, and a post-write snapshot/summary.

## Automated Coverage

Current tests cover:

- `/tools` exposes the expanded CAD and context tool manifest.
- Echo rectangle requests include `cad.preview_plan` and `cad.draw_rectangle`.
- Stair requests include the high-level `cad.draw_stair` tool.
- Inspect requests call `cad.read_dwg_snapshot`.
- Greeting requests do not call `cad.draw_text`.

Future tests should add plugin-level AutoCAD integration coverage for selector
matching, bounds measurement, layer diffs, and before/after diffs against a
known DWG fixture.
