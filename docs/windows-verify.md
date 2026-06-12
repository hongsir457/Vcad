# Verifying VCAD on Windows + AutoCAD 2017

This is the shortest path from "I have an empty folder" to "I see VCAD draw
a rectangle in AutoCAD 2017".

## 0. One-time prerequisites

Install on your Windows machine:

1. **Git for Windows** — <https://git-scm.com/download/win>
2. **.NET SDK 8.x (x64)** — <https://dotnet.microsoft.com/download/dotnet/8.0>
   (the SDK, not just the runtime — pick "SDK 8.0.x x64 Installer").
3. **AutoCAD 2017** is already installed (you said so).
4. (Optional) **.NET Framework 4.7 Runtime** — Windows 10/11 with full
   updates already has it. Confirm in PowerShell:
   ```powershell
   (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -ge 460798
   ```
   `True` means you're good. `False` → install .NET Framework 4.7 from the
   Microsoft download center.

You do **not** need Visual Studio. The csproj already references
`Microsoft.NETFramework.ReferenceAssemblies`, so `dotnet build` works
without any targeting pack install.

## 1. Get the code

Open PowerShell:

```powershell
cd C:\path\where\you\want\it
git clone https://github.com/hongsir457/Vcad.git
cd Vcad
git checkout claude/busy-fermat-cs0Gs
```

(If the repo URL differs, replace it. The branch with this MVP is
`claude/busy-fermat-cs0Gs`.)

## 2. Tell the build where AutoCAD 2017 lives

The build needs to find AutoCAD's managed DLLs **without ever copying or
committing them**. Set one environment variable for the current shell:

```powershell
$env:AutoCAD2017_Managed = "C:\Program Files\Autodesk\AutoCAD 2017"
```

Sanity check — these three files must exist:

```powershell
Test-Path "$env:AutoCAD2017_Managed\AcMgd.dll"
Test-Path "$env:AutoCAD2017_Managed\AcDbMgd.dll"
Test-Path "$env:AutoCAD2017_Managed\AcCoreMgd.dll"
```

If one is missing, point the variable at the folder that actually contains
them (sometimes it's an `inc-x64\` subfolder of the AutoCAD install).

## 3. Build + assemble the bundle

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-bundle.ps1 -Target acad2017
```

What this does:

1. Runs `release-check.ps1` (must pass — no Autodesk DLL, no real keys
   committed).
2. Runs `dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj
   -c Release`.
3. Copies the built `Vcad.Plugin.Acad2017.dll`, `Vcad.Core.dll`,
   `Newtonsoft.Json.dll`, PdfPig DLLs, and required NuGet support DLLs into
   `bundle\Acad2017\Contents\`.
4. Publishes bundled Agent Lite into `bundle\Acad2017\Contents\AgentLite\`.
5. **Strips out** any Autodesk managed DLL by name as a paranoid safety net.

After it finishes you should see:

```
bundle\Acad2017\
  PackageContents.xml
  Contents\
    Vcad.Plugin.Acad2017.dll
    Vcad.Core.dll
    Newtonsoft.Json.dll
    AgentLite\
      Vcad.AgentLite.exe
    UglyToad.PdfPig.dll
    System.Memory.dll
```

## 4. Install the bundle into AutoCAD's plugin folder

Easiest — current user, no admin rights:

```powershell
$dest = "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle"
Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -like "$dest*" } |
  Stop-Process -Force
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item bundle\Acad2017 $dest -Recurse -Force
```

This path is in AutoCAD's default `TRUSTEDPATHS`, so `SECURELOAD` won't
silently reject the plugin.

## 5. Verify in AutoCAD 2017

1. Start AutoCAD 2017.
2. Open any drawing (or start with an empty one).
3. On the command line you should see:
   ```
   VCAD plugin loaded. Type VCAD to open the sidebar.
   ```
   If you don't, see `docs/troubleshooting.md`.
4. Type `VCAD`. The sidebar opens with three tabs: **Chat**, **Config**, and
   **Usage**. Agent Lite should be started automatically from the bundle; the
   status should move to connected/online if port `8765` is free.
5. Configure a working model profile in **Config**, then use **Chat** to enter
   `draw a 6000 by 4000 rectangle with a VCAD DEMO label`. Review the
   Intent / Plan / Preview cards and confirm execution.
6. Result, in this order:
   - Two new layers `A-WALL` (color 7 / white) and `T-TEXT` (color 2 / yellow).
   - A 6000 × 4000 polyline rectangle on `A-WALL`.
   - The text `VCAD DEMO` at (1000, 500) on `T-TEXT`.
   - The log panel shows a full `vcad_result_v1` JSON, including
     `handle`, `object_id`, `dsl_id` for each entity.
7. Press `Ctrl+Z` **once**. All of the above disappears in a single undo.
   That confirms the "one DSL request = one LockDocument + Transaction +
   Undo group" invariant from the v0.5 blueprint.

If steps 1–7 all pass, the plugin is verified.

## 6. (Optional) Verify Agent Lite auto-start

Open the VCAD sidebar and then check health locally:

```powershell
Invoke-RestMethod http://127.0.0.1:8765/health -Headers @{
  "X-VCAD-Agent-Token" = (Get-Content "$env:APPDATA\VCAD\agent.token" -Raw)
}
```

For development or troubleshooting, you can still run Agent Lite manually:

```powershell
dotnet run --project src\Vcad.AgentLite\Vcad.AgentLite.csproj -c Release
```

## 7. (Optional) Use your own LLM

You can run Agent Lite manually with your key in environment variables for
development, but the installed plugin normally starts the bundled service
automatically:

```powershell
$env:VCAD_AGENT_PROVIDER = "openai"
$env:VCAD_AGENT_BASE_URL = "https://api.openai.com"
$env:VCAD_AGENT_MODEL    = "gpt-5"
$env:VCAD_AGENT_API_KEY  = "sk-..."   # your key, this shell only
dotnet run --project src\Vcad.AgentLite\Vcad.AgentLite.csproj -c Release
```

Or save the same settings in the **Model Settings** tab (the key is
encrypted with Windows DPAPI under `%APPDATA%\VCAD\agent.config.json`).

For DeepSeek, choose `Provider = deepseek` in **Model Settings** or start
Agent Lite with:

```powershell
$env:VCAD_AGENT_PROVIDER = "deepseek"
$env:VCAD_AGENT_BASE_URL = "https://api.deepseek.com"
$env:VCAD_AGENT_MODEL    = "deepseek-v4-flash"
$env:VCAD_AGENT_API_KEY  = "sk-..."   # your DeepSeek key
dotnet run --project src\Vcad.AgentLite\Vcad.AgentLite.csproj -c Release
```

## Common failures and fixes

| Symptom | Cause | Fix |
|---|---|---|
| `pack-bundle.ps1` fails on "Missing AcMgd.dll" | `AutoCAD2017_Managed` points at wrong folder | Use the folder that actually contains `AcMgd.dll` |
| AutoCAD doesn't print "VCAD plugin loaded" | `SECURELOAD` blocked it, or bundle isn't in a trusted path | Use the `%APPDATA%\Autodesk\ApplicationPlugins\` path from step 4 |
| `Unknown command "VCAD"` | A different VCAD plugin / macro already registers the name | Type `_VCAD` (forces VCAD's command name globally) |
| Run DSL prints `E_SCHEMA_INVALID` | Hand-edited JSON has a syntax error | Click **Load Sample** to reset |
| Run DSL prints `E_AUTOCAD_TRANSACTION` | AutoCAD threw mid-execution | Read the message in the log; nothing was drawn (the whole batch was aborted) |

Full troubleshooting list is in `docs/troubleshooting.md`.
