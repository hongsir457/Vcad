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
  - **Chat** — type or dictate a CAD request, attach local context files, review
    Intent / Plan / Preview cards, then confirm before execution.
  - **Model Settings** — configure your own LLM provider and API key. The key
     is encrypted with Windows DPAPI (CurrentUser) and stored at
     `%APPDATA%\VCAD\agent.config.json`. Natural-language parse requests send
     the active profile's provider, model, base URL, and API key to the local
     Agent Lite service on `127.0.0.1`.
  - **Usage** — view local session request and success/failure counters.
- Voice input uses Windows local speech recognition when available; audio is
  not uploaded by the plugin.
- Execution goes through the CAD agent pipeline: Intent → Task Plan → CAD-IR →
  Validator/Risk Policy → Preview/Confirm → AdapterCommand → Result/Audit.
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

   - `%APPDATA%\Autodesk\ApplicationPlugins\VCAD.bundle` (current user)
   - `%PROGRAMDATA%\Autodesk\ApplicationPlugins\VCAD.bundle` (all users)

2. Start AutoCAD. Type `VCAD` to open the sidebar.

3. Switch to the **DSL Input** tab, click **Load Sample**, then **Run DSL**.
   You should see a 6000×4000 rectangle and a text label appear in model
   space, with two new layers `A-WALL` and `T-TEXT`.

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

## Agent Lite (optional)

`Vcad.AgentLite` is a tiny local HTTP service (`127.0.0.1:8765`) that turns
natural-language input into VCAD DSL. The plugin only talks to it on
localhost. The service can call your own provider (OpenAI / Anthropic /
Ollama / custom HTTP). **You bring the key.**

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
VCAD_AGENT_MODEL=gpt-4o-mini \
dotnet run
```

DeepSeek is supported through its OpenAI-compatible API:

```bash
VCAD_AGENT_PROVIDER=deepseek \
VCAD_AGENT_API_KEY=sk-... \
VCAD_AGENT_MODEL=deepseek-v4-flash \
dotnet run
```

## License

Apache-2.0. See [LICENSE](LICENSE).
