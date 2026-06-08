# Installing VCAD

## 1. Pick the right bundle

| Your AutoCAD | Bundle |
|---|---|
| AutoCAD 2017 – 2024 | `VCAD-Acad2017.bundle` |
| AutoCAD 2025+ | `VCAD-Acad2025.bundle` |

AutoCAD LT is not supported.

## 2. Copy the bundle to AutoCAD's plugin folder

Pick **one** of the following. Both are in AutoCAD's default `TRUSTEDPATHS`:

- Current user (no admin rights needed):
  ```
  %APPDATA%\Autodesk\ApplicationPlugins\VCAD-AcadXXXX.bundle\
  ```
- All users (admin rights):
  ```
  %PROGRAMDATA%\Autodesk\ApplicationPlugins\VCAD-AcadXXXX.bundle\
  ```

After copying, the folder should look like:

```
VCAD-Acad2017.bundle\
  PackageContents.xml
  Contents\
    Vcad.Plugin.Acad2017.dll
    Vcad.Core.dll
    Newtonsoft.Json.dll
```

## 3. Restart AutoCAD

Start AutoCAD. You should see this in the command line on first load:

```
VCAD plugin loaded. Type VCAD to open the sidebar.
```

Type `VCAD` and the sidebar opens.

## 4. AutoCAD 2017 only — install .NET Framework 4.7

AutoCAD 2017 ships with .NET 4.6. The plugin requires .NET 4.7. Almost all
Windows machines already have 4.7 or later via Windows Update; if not,
install the runtime from the Microsoft download center.

You can verify with PowerShell:

```powershell
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -ge 460798
```

`True` means .NET 4.7+ is installed.

## 5. AutoCAD 2025+ only — install .NET 8 Desktop Runtime

AutoCAD 2025 itself ships with the .NET 8 runtime, so this is usually
automatic. If the plugin still fails to load, install the **.NET Desktop
Runtime 8.x (x64)** from Microsoft.

## Loading via NETLOAD (developer workflow)

For development iterations you can bypass the bundle:

```
NETLOAD
```

and pick `Vcad.Plugin.Acad2017.dll` or `Vcad.Plugin.Acad2025.dll` from your
build output. Then type `VCAD`.

If `NETLOAD` produces no output and `VCAD` is "unknown command", see
[troubleshooting.md](troubleshooting.md).
