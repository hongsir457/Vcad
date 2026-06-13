# Troubleshooting

## `NETLOAD` Returned Nothing And `VCAD` Is Unknown

AutoCAD likely rejected the DLL because of `SECURELOAD`.

```text
SECURELOAD
```

If it is `1` or `2`, load the plugin from a trusted path:

- `%APPDATA%\Autodesk\ApplicationPlugins\`
- `%PROGRAMDATA%\Autodesk\ApplicationPlugins\`

Do not set `SECURELOAD=0` just to make it load.

## AutoCAD Shows An Unsigned Executable Warning

Local development builds are not code-signed. If AutoCAD prompts for
`Vcad.Plugin.Acad2017.dll`, verify the path is the installed VCAD bundle, then
choose **Load Once** for testing or **Always Load** for this trusted local build.
If you choose **Do Not Load**, the `VCAD` command and agent panel will not be
available in that AutoCAD session.

## Could Not Load File Or Assembly

For AutoCAD 2017, make sure .NET Framework 4.7 or later is installed:

```powershell
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -ge 460798
```

## Bundle Cannot Be Replaced

AutoCAD or bundled AgentLite is still locking files. Close AutoCAD and run:

```powershell
$dest = "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle"
Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -like "$dest*" } |
  Stop-Process -Force
```

Then delete/copy the bundle again.

## AgentLite 未连接

- The plugin should auto-start `Contents\AgentLite\Vcad.AgentLite.exe` when the
  panel opens.
- Check `%APPDATA%\VCAD\logs`.
- Check health:

  ```powershell
  Invoke-RestMethod http://127.0.0.1:8765/health -Headers @{
    "X-VCAD-Agent-Token" = (Get-Content "$env:APPDATA\VCAD\agent.token" -Raw)
  }
  ```

- Make sure another process is not occupying the configured port.

## Model Test Fails

- The provider may reject the configured model or API key.
- Save the profile before testing.
- If the provider says the project has no model access, pick a model available
  to that provider account.
- The sidebar redacts secrets in errors; provider status codes and redacted
  response text are shown in the config page.

## Replies Are Drawn Into The DWG

That should not happen in the current architecture. Assistant text belongs in
the panel. `cad.draw_text` is only for explicit drawing labels, annotations,
titles, dimensions, and notes. If you can reproduce panel text being inserted
into the drawing, open an issue with the prompt and the visible tool call.

## Tool Call Failed

Read the tool result card in **对话**. Common causes:

- Missing active DWG.
- Invalid layer name.
- Bad coordinate or dimension.
- Trying to write text that looks like an assistant/status reply.
- AutoCAD API threw during the transaction.

## Reset VCAD Local State

```cmd
del "%APPDATA%\VCAD\agent.config.json"
del "%APPDATA%\VCAD\agent.token"
rmdir /s /q "%APPDATA%\VCAD\logs"
```
