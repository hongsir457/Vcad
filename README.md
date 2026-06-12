# VCAD Plugin

[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

VCAD means **VoiceCAD**: an open-source AutoCAD plugin framework for turning
voice or typed natural-language requests into a validated CAD task pipeline.

**Status:** experimental MVP (v0.1.0).

> **First time on Windows + AutoCAD 2017?** Follow
> [`docs/windows-verify.md`](docs/windows-verify.md) — clone → build →
> install bundle → see a rectangle drawn in ~10 minutes.

## What it does

- Loads into AutoCAD 2017+ as a `.NET` plugin (`net47` for AutoCAD 2017–2024,
  `net8.0-windows` for AutoCAD 2025+).
- Provides the `VCAD` command which opens a sidebar.
- The sidebar has three tabs:
  - **Chat** — type or dictate a CAD request, attach local context files
    (text, DXF/LISP/script, images, PDF text/metadata, DWG metadata), review
    Intent / Plan / Preview cards, then confirm before execution.
  - **Model Settings** — configure your own LLM provider and API key. The key
     is encrypted with Windows DPAPI (CurrentUser) and stored at
     `%APPDATA%\VCAD\agent.config.json`. Natural-language parse requests send
     the active profile's provider, model, base URL, and API key to the local
     Agent Lite service on `127.0.0.1`.
  - **Usage** — view local session request and success/failure counters.
- Voice input uses Windows local speech recognition when available; audio is
  not uploaded by the plugin.
- Before model parsing, the plugin captures a read-only in-memory snapshot of
  the open DWG: layers, top-level entities, block references, and entities
  produced by exploding block references in memory. This gives the agent drawing
  state for intent understanding and planning without granting execution
  access.
- Execution goes through the CAD agent pipeline: Intent → Task Plan → CAD-IR →
  Validator/Risk Policy → Preview/Confirm → AdapterCommand → Result/Audit.
- AutoCAD execution still has one controlled route: CAD-IR is adapted into the
  VCAD DSL whitelist, then the local AutoCAD .NET executor runs it after
  Preview/Confirm. The model cannot directly invoke arbitrary AutoCAD commands.
- Each request runs inside one `LockDocument` + one `Transaction` + one Undo
  group, so a single `Ctrl+Z` rolls back the whole batch.
- Returns a `vcad_result_v1` JSON including `dsl_id ↔ Handle/ObjectId`
  mapping for every entity created.

## First goal

Paste the sample JSON and draw a 6000×4000 rectangle plus a text label in
AutoCAD.

## Repository layout

```
src/
  Vcad.Core/                netstandard2.0  DSL DTOs, validation, result contract
  Vcad.Plugin.Shared/       shared plugin source files (linked into both csprojs)
  Vcad.Plugin.Acad2017/     net47            AutoCAD 2017–2024
  Vcad.Plugin.Acad2025/     net8.0-windows   AutoCAD 2025+
  Vcad.AgentLite/           net8.0           local HTTP service (127.0.0.1:8765)
bundle/Acad2017/  bundle/Acad2025/   PackageContents.xml templates
schema/                     vcad_dsl_v1 / vcad_result_v1 JSON schemas
samples/commands/           reference DSL JSON
docs/                       install / troubleshooting / DSL spec / blueprint
tools/                      release packaging checks
.github/workflows/          CI release check
tests/                      unit + integration tests
```

## What this project will never ship in open source

- A built-in API key.
- A login system.
- A paid backend.
- Automatic uploading of your DWG files.
- Telemetry that runs by default.

## Supported AutoCAD versions

| AutoCAD version | Bundle | Target framework |
|---|---|---|
| 2017 | `VCAD-Acad2017.bundle` | `net47` (install .NET 4.7 runtime if missing) |
| 2018–2024 | `VCAD-Acad2017.bundle` | `net47` |
| 2025+ | `VCAD-Acad2025.bundle` | `net8.0-windows` |

AutoCAD LT is not supported (it has no managed plugin host).

## Quick start

1. Install the bundle that matches your AutoCAD version (`bundle/Acad2017` or
   `bundle/Acad2025`) to one of:

   - `%APPDATA%\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle` (current user)
   - `%PROGRAMDATA%\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle` (all users)

   Stop AutoCAD and any bundled Agent Lite process before replacing an
   installed bundle:

   ```powershell
   $dest = "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle"
   Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue |
     Where-Object { $_.Path -like "$dest*" } |
     Stop-Process -Force
   if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
   Copy-Item bundle\Acad2017 $dest -Recurse -Force
   ```

2. Start AutoCAD. Type `VCAD` to open the sidebar.

3. In the **Chat** tab, enter `draw a 6000 by 4000 rectangle with a ROOM
   label`, then submit. Review the generated Intent / Plan / Preview cards and
   click **Confirm Execute**. You should see a 6000×4000 rectangle and a text
   label appear in model space, with two new layers `A-WALL` and `T-TEXT`.

4. Press `Ctrl+Z` once — the whole batch is undone.

## Build (Windows)

You need the AutoCAD managed DLLs (`AcMgd.dll`, `AcDbMgd.dll`,
`AcCoreMgd.dll`) from your installed AutoCAD or the ObjectARX SDK. **They
are never committed to this repo.**

```powershell
$env:AutoCAD2017_Managed = "C:\Program Files\Autodesk\AutoCAD 2017"
$env:AutoCAD2025_Managed = "C:\Program Files\Autodesk\AutoCAD 2025"
dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj -c Release
dotnet build src\Vcad.Plugin.Acad2025\Vcad.Plugin.Acad2025.csproj -c Release
```

See [docs/install.md](docs/install.md) for detailed setup, and
[docs/troubleshooting.md](docs/troubleshooting.md) for the common loading
failures (SECURELOAD, missing .NET 4.7, AutoCAD LT, etc).

## Agent Lite

`Vcad.AgentLite` is a tiny local HTTP service (`127.0.0.1:8765`) that turns
natural-language input into VCAD DSL. The plugin only talks to it on
localhost. The service can call your own provider (OpenAI / DeepSeek /
Anthropic / Gemini / Ollama / custom HTTP). **You bring the key.**

`tools/pack-bundle.ps1` publishes Agent Lite into
`Contents\AgentLite\Vcad.AgentLite.exe`. When the VCAD sidebar opens, the
plugin checks `/health` and automatically starts the bundled service if it is
not already running. Manual startup is still useful for development or
troubleshooting:

```bash
cd src/Vcad.AgentLite
dotnet run
```

By default it runs with the deterministic `echo` provider (no network, no
key required). You can either configure a provider in the plugin's **Model
Settings** tab or set environment variables before starting Agent Lite:

```bash
VCAD_AGENT_PROVIDER=openai \
VCAD_AGENT_API_KEY=sk-... \
VCAD_AGENT_MODEL=gpt-5 \
dotnet run
```

DeepSeek is supported through its OpenAI-compatible API:

```bash
VCAD_AGENT_PROVIDER=deepseek \
VCAD_AGENT_API_KEY=sk-... \
VCAD_AGENT_MODEL=deepseek-v4-flash \
dotnet run
```

### Attachment context

The plugin does not upload whole DWG/PDF/source files by path. Attachments are
converted into a bounded local context payload before they are sent to Agent
Lite:

- Text-like files (`.txt`, `.md`, `.json`, `.csv`, `.xml`, `.dxf`, `.lsp`,
  `.scr`) send a text excerpt, capped at 20,000 characters per file.
- Images (`.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.tif`, `.tiff`) up to
  4 MB per file are sent as inline vision payloads to providers that support
  image input. The plugin keeps the total inline image budget bounded; larger
  or overflow images send metadata only.
- PDFs with selectable text are extracted locally and send a bounded text
  excerpt. Scanned/image-only PDFs send metadata only and need OCR or a model
  file-input pipeline before the model can read the report body.
- Binary CAD files currently send metadata (`name`, type, size, SHA-256) only.
  The project intentionally does not dump a 10+ MB binary into a prompt.

Agent Lite accepts request bodies up to 8 MB, 32,000 characters of direct text,
12 attachments per request, and 6 MB of base64 per inline attachment.

## License

Apache-2.0. See [LICENSE](LICENSE).
