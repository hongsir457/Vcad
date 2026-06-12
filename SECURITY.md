# Security Policy

VCAD treats the plugin and the local AgentLite service as the security boundary,
not the model prompt.

## What VCAD Does Not Do

- It does not ship with an API key.
- It does not call a VCAD-owned hosted backend.
- It does not execute arbitrary model-returned scripts.
- It does not execute arbitrary AutoLISP strings.
- It does not upload your DWG, prompts, logs, or attachments by default.
- It does not read or write arbitrary disk paths; workspace tools are rooted.

## Defenses

1. **Tool allowlist.** Agent output is only acted on when it names a registered
   tool.
2. **Plugin-owned CAD writes.** AutoCAD writes happen inside the plugin process,
   through explicit `cad.*` tools.
3. **Input validation.** CAD tool arguments validate layer names, coordinates,
   dimensions, text length, and color values.
4. **Assistant text guard.** Panel replies are not written into the drawing.
   Drawing text is only for explicit labels, annotations, titles, dimensions,
   and notes.
5. **Confirm/trusted mode.** Write tools require confirmation unless the user
   selects trusted execution mode.
6. **Loopback AgentLite.** AgentLite binds `127.0.0.1` and uses
   `X-VCAD-Agent-Token`.
7. **DPAPI secrets.** Saved API keys use Windows DPAPI CurrentUser encryption.
8. **Redaction.** Error reporting redacts common API key and bearer-token
   patterns.

## Reporting A Vulnerability

Open a private GitHub security advisory. Include:

- Affected version.
- AutoCAD version and OS.
- Minimal reproduction steps.
- Whether the issue lets a model response escape the registered tool surface,
  write unintended drawing content, read unintended files, or exfiltrate data.

There is no bug bounty.
