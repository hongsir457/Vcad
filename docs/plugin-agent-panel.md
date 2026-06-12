# VCAD Plugin As Agent Panel

VCAD is a docked AutoCAD agent panel backed by a local agent runtime. The plugin
is not a command-text box; it is the AutoCAD-side tool host, permission shell,
and visible conversation surface.

## Current Architecture

```text
User
  -> VCAD docked panel
  -> AgentLite local runtime
  -> model provider
  -> tool_calls
  -> VCAD plugin tool host / AgentLite context tools
  -> tool_results
  -> AgentLite continues
  -> VCAD panel renders reply, progress, confirmation, result
```

## Responsibilities

The plugin owns:

- Chat, voice input, file attachment UI, and progress cards.
- Reading the active DWG through AutoCAD APIs.
- Running CAD write tools inside AutoCAD.
- Confirm vs trusted execution mode.
- Keeping assistant replies in the panel.

AgentLite owns:

- Provider calls.
- Conversation turn protocol.
- Web and workspace context tools.
- Token usage extraction and model-facing prompts.

## Agent Turn Protocol

```http
POST /agent/turn
```

Request:

```json
{
  "session_id": "cad-session-id",
  "message": "draw a window schedule from this PDF",
  "cad_observation": {},
  "tool_results": [],
  "attachments": [],
  "provider": {}
}
```

Response:

```json
{
  "session_id": "cad-session-id",
  "assistant_message": "I need to inspect the current drawing layers first.",
  "trace": [
    { "title": "Read drawing", "summary": "Inspecting active DWG before writing." }
  ],
  "tool_calls": [
    { "id": "call-1", "name": "cad.read_dwg_snapshot", "args": { "limit": 500 } }
  ],
  "requires_user_input": false,
  "done": false,
  "usage": {}
}
```

The plugin executes `cad.*` calls because only the AutoCAD process has safe
access to the drawing. It calls `/agent/turn` again with the tool results.

## Tool Ownership

Plugin-hosted CAD tools:

- `cad.read_dwg_snapshot`
- `cad.create_layer`
- `cad.draw_line`
- `cad.draw_rectangle`
- `cad.draw_text`

AgentLite-hosted context tools:

- `web.search`
- `web.fetch_url`
- `workspace.read_file`
- `workspace.write_file`

Write tools require explicit confirmation unless the user chooses trusted mode.

## Product Rule

Natural-language assistant text belongs in the VCAD panel. It must never be
written into the DWG unless the user explicitly asks for drawing text such as a
label, annotation, title, dimension, or note.
