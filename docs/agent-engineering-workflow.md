# VCAD Agent Engineering Workflow

VCAD keeps the product entry point and primary artifact unchanged:

- Entry point: the docked VoiceCAD panel inside AutoCAD.
- Primary artifact: the active DWG opened in AutoCAD.
- Execution adapter: AutoCAD .NET API tools hosted by the plugin.

The agent borrows the useful engineering ideas from CAD skill/toolchain
projects, but translates them to a DWG-first workflow:

```text
Natural language
-> Intent
-> CAD brief
-> Task plan
-> DWG Memory / Geometry Index
-> DWG-backed CAD-IR
-> Safety
-> Preview / confirmation
-> AutoCAD adapter tool calls
-> DWG validation
-> Natural-language result in the panel
```

## What Was Adopted

1. Skill/tool layering
   - AgentLite exposes a manifest grouped into CAD context, CAD preview, CAD
     geometry, CAD semantics, CAD validation, CAD modification, document
     context, web context, file context, and CAD action tools.
   - The plugin owns `cad.*` tools because only AutoCAD can safely inspect and
     mutate the active drawing.
   - AgentLite owns guarded external context tools such as `web.search`,
     `web.fetch_url`, `workspace.read_file`, and `workspace.write_file`.

2. CAD brief
   - Every non-trivial agent turn must return `cad_brief`, `task_plan`,
     `cad_ir`, `safety`, and `validation`.
   - The panel shows these as one collapsed engineering card. This gives the
     user auditability without exposing hidden chain-of-thought.

3. DWG selector references
   - Snapshot entities include stable selector references such as
     `handle:1A2F`, `layer:FROG`, `type:Polyline`, and `block:Door`.
   - Read tools accept `selector` / `selectors` plus direct `layer`, `type`,
     `handle`, and `include_exploded` filters.
   - Block internals are included in the memory snapshot for understanding and
     measurement.

4. Deterministic checks
   - `cad.read_dwg_snapshot`: read drawing metadata, layers, linetypes,
     text/dim styles, blocks, entities, properties, bounds, geometry index, and
     expanded block internals.
   - `cad.read_layers`, `cad.read_styles`, `cad.read_blocks`: read focused DWG
     tables without forcing the model to parse a whole snapshot.
   - `cad.query_entities`: query by selector, layer, type, handle, text,
     bounds/window, near point, length, and expanded block inclusion.
   - `cad.describe_entity`, `cad.describe_selection`: inspect selected targets
     before modifying them.
   - `cad.find_near`, `cad.find_intersections`,
     `cad.find_connected_contours`, `cad.find_closed_regions`: reason about
     geometry relationships.
   - `cad.measure_relation`: compare two selectors by bounds, distance,
     intersection, containment, and simple orientation hints.
   - `cad.semantic_scan`: identify likely walls, rooms, stairs, annotations,
     doors, and windows from geometry, layers, text, and blocks.
   - `cad.count_entities`: count selected objects by layer and type.
   - `cad.measure_bounds`: measure selected aggregate bounds.
   - `cad.measure_distance`: measure point-to-point or selector-center distance.
   - `cad.layer_diff`: compare layer counts before and after a step.
   - `cad.before_after_diff`: compare counts, layer/type deltas, and bounds.
   - `cad.validate_dwg_state`: verify expected layers, counts, types, warnings.

5. Preview/validation loop
   - `cad.preview_plan` provides a dry-run style preview over the current DWG
     context before writes.
   - Write tools still go through the panel's confirmation or trusted execution
     mode.
   - After writes, the agent should validate with deterministic read tools and
     summarize the result in the panel. Assistant explanations must never be
     written into the DWG.

6. Third-stage CAD tools
   - Drawing: `cad.draw_arc`, `cad.draw_room`, `cad.draw_wall`,
     `cad.draw_mtext`, `cad.draw_dimension`, and `cad.insert_block` supplement
     the basic line/polyline/circle/rectangle/stair tools.
   - Modification: `cad.move_entities`, `cad.copy_entities`,
     `cad.rotate_entities`, `cad.scale_entities`, `cad.offset_entities`,
     `cad.delete_entities`, `cad.change_layer`, and `cad.set_properties` act on
     top-level editable entities selected by stable DWG selectors.
   - The model must resolve vague references to selectors before modification.

7. Progressive reference snippets
   - `AgentReferences` loads short task-specific guidance into the model prompt:
     DWG selectors, write/validation loop, attachments, web, and workspace
     files.
   - This keeps the model focused without stuffing the full architecture into
     every turn.

8. Benchmark task set
   - Manual and automated regression prompts live in
     [agent-benchmarks.md](agent-benchmarks.md).
   - The test suite covers the tool manifest, simple rectangle planning,
     snapshot inspection, and the rule that conversational replies stay in the
     panel instead of becoming `cad.draw_text`.

## Product Rules

- The current DWG is the source of truth.
- Do not emit AutoLISP or script text as the main control path.
- Use tool calls for CAD work.
- Use selectors after observation instead of vague object references.
- For existing drawings, query and describe the target set before modifying it.
- Use semantic and geometry tools when the user's instruction depends on
  relationships such as nearby, inside, connected, intersecting, wall, room,
  stair, door, or window.
- Ask only for missing, task-specific parameters.
- If a tool call fails, repair the arguments and continue the same task.
- Do not return to generic initial intent options after an execution failure.
- `cad.draw_text` is only for explicit drawing labels, notes, titles,
  dimensions, or annotations requested by the user.
