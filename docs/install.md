# Installing VCAD

## 1. Pick The Bundle

| AutoCAD | Bundle |
|---|---|
| AutoCAD 2017-2024 | `VCAD-Acad2017.bundle` |
| AutoCAD 2025+ | `VCAD-Acad2025.bundle` |

AutoCAD LT is not supported because it does not host managed plugins.

## 2. Build The Bundle

For this machine, AutoCAD 2017 is installed at:

```powershell
$env:AutoCAD2017_Managed = "D:\autocad2017\AutoCAD 2017"
```

Build and populate the bundle:

```powershell
cd F:\Vcad
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-bundle.ps1 -Target acad2017
```

Expected layout:

```text
VCAD-Acad2017.bundle\
  PackageContents.xml
  Contents\
    Vcad.Plugin.Acad2017.dll
    Newtonsoft.Json.dll
    UglyToad.PdfPig.dll
    AgentLite\
      Vcad.AgentLite.exe
```

## 3. Copy To AutoCAD's Plugin Folder

Current user install:

```powershell
$dest = "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle"
Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -like "$dest*" } |
  Stop-Process -Force
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item bundle\Acad2017 $dest -Recurse -Force
```

All users install, with admin rights:

```text
%PROGRAMDATA%\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle
```

## 4. Start AutoCAD

Start AutoCAD and type:

```text
VCAD
```

The sidebar opens with **对话 / 配置 / 用量** tabs. The plugin starts bundled
AgentLite automatically from `Contents\AgentLite`.

## 5. Configure A Model

Open **配置**:

1. Pick provider and model.
2. Enter API Base URL and API key.
3. Choose execution mode:
   - `确认后执行`
   - `完全授权自动执行`
4. Click **保存**.
5. Click **测试连接**.

The API key is encrypted with Windows DPAPI and stored under
`%APPDATA%\VCAD\agent.config.json`.

## Developer NETLOAD Workflow

For fast local iterations:

```text
NETLOAD
```

Pick `Vcad.Plugin.Acad2017.dll` from the build output, then type `VCAD`.

If `NETLOAD` produces no output and `VCAD` is unknown, see
[troubleshooting.md](troubleshooting.md).
