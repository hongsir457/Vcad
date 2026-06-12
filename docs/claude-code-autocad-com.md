# Claude Code + AutoCAD COM Bridge

This is a development and validation path, not the final product UX. It proves
the same observe -> tool call -> observe loop outside the plugin.

## Loop

```text
Claude/Codex conversation
  -> inspect AutoCAD via COM
  -> choose one small action
  -> execute COM operation
  -> inspect result
  -> continue
```

The product path uses the VCAD sidebar and AgentLite; this COM bridge is useful
for debugging AutoCAD behavior quickly from a coding agent.

## Requirements

- AutoCAD must already be running.
- Open the DWG you want to inspect or edit.
- Run commands from the repo root.
- Use Windows PowerShell.

## Smoke Test

```powershell
cd F:\Vcad
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 ping
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 snapshot -Limit 20
```

## CAD Actions

Create or update a layer:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 add-layer -Layer A-WALL -Color 7
```

Draw a line:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 add-line -Layer A-WALL -X1 0 -Y1 0 -X2 6000 -Y2 0
```

Draw a rectangle:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 add-rectangle -Layer A-WALL -X 0 -Y 0 -Width 6000 -Height 4000
```

Add drawing text:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 add-text -Layer T-TEXT -X 1000 -Y 500 -Text "ROOM" -TextHeight 250
```

Run a native AutoCAD command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\autocad-com-bridge.ps1 send-command -Command "_.ZOOM _E"
```

`send-command` is asynchronous in AutoCAD. Prefer direct COM methods when
deterministic feedback is needed.

## Rule

Do not write natural-language assistant replies into the drawing. Use `add-text`
only when the user explicitly asks for a label, annotation, title, dimension, or
other drawing text.
