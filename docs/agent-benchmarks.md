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

6. Query and describe a target
   - Prompt: `find the objects near x=0,y=0 within 3000 and describe the nearest polyline`
   - Expect: `cad.find_near`, then `cad.describe_entity`; no DWG write.

7. Find closed regions
   - Prompt: `find closed room-like contours on layer A-WALL`
   - Expect: `cad.find_closed_regions` or `cad.find_connected_contours`; no DWG write.

8. Semantic scan
   - Prompt: `scan this drawing and tell me likely walls, rooms, stairs, doors, windows, and notes`
   - Expect: `cad.semantic_scan` plus targeted query/describe calls.

9. Modify selected geometry
   - Prompt: `copy the polyline on layer FROG 1200 units to the right, then verify the count changed`
   - Expect: observe/query first, `cad.copy_entities`, then `cad.layer_diff` or `cad.before_after_diff`.

10. Change properties
   - Prompt: `move all text on layer NOTES to A-ANNO and make it color 4`
   - Expect: `cad.query_entities`, `cad.change_layer` or `cad.set_properties`, then validation.

11. Wall/room drafting
   - Prompt: `draw a 6000 x 4000 room with 200 wall thickness on layer A-WALL`
   - Expect: `cad.draw_room`, followed by bounds and layer validation.

12. Dimension drafting
   - Prompt: `dimension the room width from (0,0) to (6000,0)`
   - Expect: `cad.draw_dimension`; no assistant explanation text in the DWG.

13. PDF-driven drafting
   - Prompt: `draw the plan and elevation from this appraisal PDF`
   - Expect: use extracted PDF text if available; report OCR limitation if the
     PDF is scanned; ask only for missing dimensions or permission to OCR/retry.

14. Explicit annotation
   - Prompt: `add the label "car here" at x=1200,y=800`
   - Expect: `cad.draw_text` is allowed because the user requested drawing text.

15. Before/after validation
   - Prompt: `after drawing, show what changed`
   - Expect: preserve or request a previous snapshot, then use
     `cad.before_after_diff` or `cad.layer_diff`.

16. U-shaped double-run stair
   - Prompt: `画一个U型双跑楼梯，宽度1200，踏步250，踢步150，层高3900`
   - Expect: `cad.draw_stair` with a bounded plan, no assistant explanation text
     written into the DWG, and a post-write snapshot/summary.

## Automated Coverage

Current tests cover:

- `/tools` exposes the expanded DWG memory, geometry, semantic, draw, modify,
  and context tool manifest.
- Echo rectangle requests include `cad.preview_plan` and `cad.draw_rectangle`.
- Stair requests include the high-level `cad.draw_stair` tool.
- Inspect requests call `cad.read_dwg_snapshot`.
- Greeting requests do not call `cad.draw_text`.

Future tests should add plugin-level AutoCAD integration coverage for selector
matching, geometry relationships, semantic scanning, modification tools, layer
diffs, and before/after diffs against a known DWG fixture.
