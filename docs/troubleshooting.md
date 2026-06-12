# Troubleshooting

## `NETLOAD` returned nothing and `VCAD` is "unknown command"

AutoCAD silently rejected the DLL. Most common cause: `SECURELOAD`.

```
SECURELOAD
```

If it is `1` or `2`, the DLL must live in a trusted path. The default
trusted paths include:

- `%APPDATA%\Autodesk\ApplicationPlugins\`
- `%PROGRAMDATA%\Autodesk\ApplicationPlugins\`

Either move the bundle there, or add your build output directory to
`TRUSTEDPATHS`. Do **not** set `SECURELOAD=0` just to make it load — that
disables an important security check.

## "Could not load file or assembly ..."

You are on AutoCAD 2017 with only .NET 4.6 installed. Install .NET
Framework 4.7 (or later) runtime. See [install.md](install.md) step 4.

## AutoCAD 2025+ shows the plugin as grayed-out / "load failed"

- Confirm the file is `Vcad.Plugin.Acad2025.dll` (the `Acad2017` build will
  **not** load in 2025).
- Confirm .NET 8 Desktop Runtime is installed.
- Confirm `<UseWindowsForms>true</UseWindowsForms>` is in the csproj.

## AutoCAD startup logs say "VCAD" is a duplicate command

Another plugin or macro on the machine already registers `VCAD`. Options:

- Use `_VCAD` (AutoCAD's "force global" alias).
- Rename the registration in your bundle to `VCAD_OPEN` and rebuild.

## Sidebar opens but execution does nothing

Use the **Chat** tab, submit a request, review the generated Intent / Plan /
Preview cards, then confirm execution. If execution fails, read the Result or
Error card in the chat panel.

- `E_SCHEMA_INVALID` — JSON syntax is broken.
- `E_COMMAND_NOT_ALLOWED` — command type is not in the v0.1 whitelist:
  `create_layer`, `draw_line`, `draw_rectangle`, `draw_text`.
- `E_PARAM_RANGE` — width / height / coordinate is zero, negative, or out
  of range. Limits: coordinate ≤ 1e9 mm, dimensions ≤ 1e8 mm.
- `E_LAYER_INVALID` — layer name uses forbidden characters
  (`< > / \ " : ; ? * | , = \``) or is longer than 255 chars.
- `E_AUTOCAD_TRANSACTION` — AutoCAD itself threw; the transaction is
  aborted and nothing was drawn.

## AutoCAD crashes mid-execution

Likely cause: the plugin or a third-party DLL is talking to AutoCAD from a
background thread. All AutoCAD API calls must happen on the document
thread. If you reproduce this with a stock VCAD install, please open a bug
report with the full crash log (Drwatson / Windows Event Viewer).

## "Test Connection" in Model Settings always fails

- Is `Vcad.AgentLite` actually running on the configured port? The plugin
  should auto-start the bundled service when the sidebar opens. Check health:
  `http://127.0.0.1:8765/health` with the token from
  `%APPDATA%\VCAD\agent.token`.
- If Agent Lite is connected but the model test fails, the provider rejected
  the configured model or key. Pick a model your provider project can access
  and click **Save** before testing again.
- Did you set `VCAD_AGENT_TOKEN` on the Agent? Then the plugin needs the
  same token. The plugin writes its token to `%APPDATA%\VCAD\agent.token`.
- Is a local antivirus / firewall blocking loopback HTTP?
- The error message shown in the sidebar is automatically redacted, but
  the AutoCAD command window may show the original. Inspect there for the
  real cause.

## Bundle copied but AutoCAD doesn't see it

- `PackageContents.xml` must be UTF-8 (BOM is OK, but not UTF-16).
- File names are case-sensitive in some configurations; match the casing
  used in the repo.
- AutoCAD scans the plugin folders at startup. Restart AutoCAD fully.

## Resetting the plugin

```
del "%APPDATA%\VCAD\agent.config.json"
del "%APPDATA%\VCAD\agent.token"
rmdir /s /q "%APPDATA%\VCAD\logs"
```

This removes saved profiles, the local Agent token, and mapping logs.
Nothing else is stored.
