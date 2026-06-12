# Verifying VCAD On Windows + AutoCAD 2017

This is the shortest local test path for the current Agent panel architecture.

## 0. Prerequisites

- Git for Windows
- .NET SDK 8.x x64
- AutoCAD 2017 installed at `D:\autocad2017\AutoCAD 2017`
- .NET Framework 4.7 or later

Check .NET Framework:

```powershell
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -ge 460798
```

## 1. Build And Pack

```powershell
cd F:\Vcad
$env:AutoCAD2017_Managed = "D:\autocad2017\AutoCAD 2017"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-bundle.ps1 -Target acad2017
```

## 2. Install

Close AutoCAD first.

```powershell
$dest = "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle"
Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -like "$dest*" } |
  Stop-Process -Force
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item bundle\Acad2017 $dest -Recurse -Force
```

## 3. Open The Panel

1. Start AutoCAD 2017.
2. Open or create a DWG.
3. Type `VCAD`.
4. Confirm the sidebar opens with **对话 / 配置 / 用量**.
5. AgentLite should auto-start and the header should move to connected/online.

## 4. Configure And Test Model

Open **配置**:

- Provider: `openai`, `deepseek`, `anthropic`, `gemini`, `ollama`, or `custom`
- Model: pick one your account can access
- API Base URL
- API Key
- Execution mode: start with `确认后执行`

Click **保存**, then **测试连接**.

## 5. Run A Safe CAD Task

In **对话**, send:

```text
画一个 6000 x 4000 的矩形，图层 A-WALL，颜色 7。
```

Expected behavior:

1. The panel shows model reasoning/progress cards.
2. The panel shows a `cad.draw_rectangle` tool call.
3. In confirm mode, click **确认执行**.
4. AutoCAD gets a 6000 x 4000 rectangle.
5. The panel shows the tool result in natural language.

## 6. Verify AgentLite Health

```powershell
Invoke-RestMethod http://127.0.0.1:8765/health -Headers @{
  "X-VCAD-Agent-Token" = (Get-Content "$env:APPDATA\VCAD\agent.token" -Raw)
}
```

Expected result includes `vcad_agent_turn_v1`.

## Common Failures

| Symptom | Cause | Fix |
|---|---|---|
| Pack fails on missing `AcMgd.dll` | `AutoCAD2017_Managed` points at wrong folder | Use the folder containing `AcMgd.dll`, `AcDbMgd.dll`, `AcCoreMgd.dll` |
| AutoCAD does not load plugin | Bundle not in trusted path or `SECURELOAD` blocked it | Use `%APPDATA%\Autodesk\ApplicationPlugins` |
| AgentLite not connected | Port busy or old process locked | Stop `Vcad.AgentLite`, reinstall bundle, reopen `VCAD` |
| Model connection 403/model access error | Provider account cannot use selected model | Pick an accessible model |
| Write tool asks every time | Confirm mode is enabled | Switch to `完全授权自动执行` only when you trust the task |
