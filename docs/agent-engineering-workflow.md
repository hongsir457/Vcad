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
     measurement, CAD validation, document context, web context, file context,
     and CAD action tools.
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
   - `cad.read_dwg_snapshot`: read layers, entities, blocks, expanded internals.
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

6. Progressive reference snippets
   - `AgentReferences` loads short task-specific guidance into the model prompt:
     DWG selectors, write/validation loop, attachments, web, and workspace
     files.
   - This keeps the model focused without stuffing the full architecture into
     every turn.

7. Benchmark task set
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
- Ask only for missing, task-specific parameters.
- If a tool call fails, repair the arguments and continue the same task.
- Do not return to generic initial intent options after an execution failure.
- `cad.draw_text` is only for explicit drawing labels, notes, titles,
  dimensions, or annotations requested by the user.
