# VCAD Plugin

[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

VCAD means **VoiceCAD**: an experimental AutoCAD side panel that lets you use
voice or typed natural language to drive a local CAD agent.

**Status:** experimental MVP (v0.1.0).

## What It Does

- Loads into AutoCAD 2017+ as a `.NET` plugin.
- Provides the `VCAD` command, which opens a docked sidebar.
- Uses three tabs: **对话**, **配置**, **用量**.
- Starts the bundled `Vcad.AgentLite` local service automatically when the panel
  opens.
- Calls your configured model provider through AgentLite. You bring your own API
  key; VCAD does not ship a key or a hosted backend.
- Runs a tool loop:

  ```text
  User message -> AgentLite -> tool_calls -> plugin tool host
  -> AutoCAD .NET API or local context tool -> tool_results -> AgentLite
  -> natural-language result in the panel
  ```

- Reads the active DWG into memory before a turn: layers, entities, block
  references, and exploded block internals.
- Supports local context attachments: text-like files, DXF/LISP/script text,
  image metadata or small inline images, PDF text extraction, and DWG metadata.
- Supports voice input through Windows local speech recognition when available.
- Keeps assistant replies in the sidebar. Text is written into the drawing only
  when the user explicitly asks for a label, annotation, title, dimension, or
  note.
- Supports two execution modes:
  - **确认后执行**: write tools pause for confirmation.
  - **完全授权自动执行**: allowed write tools execute without repeated prompts.

## Repository Layout

```text
src/
  Vcad.Plugin.Shared/       shared plugin source files
  Vcad.Plugin.Acad2017/     net47            AutoCAD 2017-2024
  Vcad.Plugin.Acad2025/     net8.0-windows   AutoCAD 2025+
  Vcad.AgentLite/           net8.0           local HTTP service
bundle/Acad2017/            AutoCAD 2017-2024 bundle template
bundle/Acad2025/            AutoCAD 2025+ bundle template
docs/                       install, verification, architecture notes
tools/                      bundle packaging and release checks
tests/                      AgentLite tests
```

## Quick Start On Your Machine

Your AutoCAD 2017 path is:

```powershell
$env:AutoCAD2017_Managed = "D:\autocad2017\AutoCAD 2017"
```

Build and populate the AutoCAD 2017 bundle:

```powershell
cd F:\Vcad
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-bundle.ps1 -Target acad2017
```

Install to the current user's AutoCAD plugin folder. Close AutoCAD and stop the
bundled AgentLite first, otherwise Windows may keep DLLs locked:

```powershell
$dest = "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle"
Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -like "$dest*" } |
  Stop-Process -Force
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item bundle\Acad2017 $dest -Recurse -Force
```

Start AutoCAD, type `VCAD`, open **配置**, set provider/model/API key, save, and
click **测试连接**. Then use **对话**:

```text
画一个 6000 x 4000 的矩形，图层 A-WALL，颜色 7。
```

In confirm mode the panel asks before writing to the drawing.

## Build

AutoCAD managed DLLs are required for plugin builds. They are never committed or
packaged.

```powershell
$env:AutoCAD2017_Managed = "D:\autocad2017\AutoCAD 2017"
dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj -c Release
dotnet test tests\Vcad.AgentLite.Tests\Vcad.AgentLite.Tests.csproj -c Release
```

For compile-only work without AutoCAD installed, use the stub API:

```powershell
$env:VCAD_STUB_AUTODESK = "true"
dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj -c Release
```

## AgentLite

`Vcad.AgentLite` binds to `127.0.0.1` and uses `X-VCAD-Agent-Token`. The plugin
stores the token under `%APPDATA%\VCAD\agent.token` and auto-starts the bundled
service from `Contents\AgentLite\Vcad.AgentLite.exe`.

Manual development startup:

```powershell
dotnet run --project src\Vcad.AgentLite\Vcad.AgentLite.csproj
```

Provider settings can be saved in the plugin UI or set as environment variables:

```powershell
$env:VCAD_AGENT_PROVIDER = "openai"
$env:VCAD_AGENT_BASE_URL = "https://api.openai.com"
$env:VCAD_AGENT_MODEL    = "gpt-5"
$env:VCAD_AGENT_API_KEY  = "sk-..."
dotnet run --project src\Vcad.AgentLite\Vcad.AgentLite.csproj
```

DeepSeek uses the OpenAI-compatible path:

```powershell
$env:VCAD_AGENT_PROVIDER = "deepseek"
$env:VCAD_AGENT_BASE_URL = "https://api.deepseek.com"
$env:VCAD_AGENT_MODEL    = "deepseek-v4-flash"
$env:VCAD_AGENT_API_KEY  = "sk-..."
dotnet run --project src\Vcad.AgentLite\Vcad.AgentLite.csproj
```

## Tool Surface

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

## Attachment Limits

The plugin does not send whole large source documents blindly into a prompt.
It builds a bounded context payload:

- Text-like files send up to 20,000 characters per file.
- PDFs with selectable text are extracted locally and capped at 20,000
  characters.
- Scanned/image-only PDFs currently send metadata and require OCR or a future
  file-input pipeline.
- Images up to 4 MB per file can be sent inline, with a bounded total image
  budget; larger images send metadata only.
- AgentLite accepts request bodies up to 8 MB, 32,000 direct message
  characters, 12 attachments, and 6 MB base64 per inline attachment.

## More Docs

- [Install guide](docs/install.md)
- [Windows + AutoCAD 2017 verification](docs/windows-verify.md)
- [Agent panel architecture](docs/plugin-agent-panel.md)
- [Claude Code AutoCAD COM development bridge](docs/claude-code-autocad-com.md)
- [Troubleshooting](docs/troubleshooting.md)

## License

Apache-2.0. See [LICENSE](LICENSE).
