# Contributing to VCAD

VCAD is an open-source AutoCAD plugin framework licensed under Apache-2.0. By
submitting a contribution, you agree it is licensed the same way.

## What We Want

- Better AutoCAD tool-host behavior.
- Better AgentLite turn handling and provider diagnostics.
- Safer CAD write tools with focused tests.
- Better file/PDF/image context extraction.
- AutoCAD version compatibility reports.
- UI improvements for the **对话 / 配置 / 用量** panel.

## What We Will Say No To For Now

- Login, billing, hosted backend, or bundled API keys.
- Default uploading of DWG files, prompts, logs, or attachments.
- Arbitrary script execution returned by a model.
- CAD write tools without parameter validation and confirmation behavior.

## Developer Setup

```powershell
dotnet build src\Vcad.AgentLite\Vcad.AgentLite.csproj -c Release
dotnet test tests\Vcad.AgentLite.Tests\Vcad.AgentLite.Tests.csproj -c Release
```

For plugin builds on this machine:

```powershell
$env:AutoCAD2017_Managed = "D:\autocad2017\AutoCAD 2017"
dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj -c Release
```

Compile-only fallback:

```powershell
$env:VCAD_STUB_AUTODESK = "true"
dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj -c Release
```

Never commit `AcMgd.dll`, `AcDbMgd.dll`, or `AcCoreMgd.dll`.

## Pull Request Checklist

- [ ] AgentLite tests pass.
- [ ] At least one plugin target builds.
- [ ] `tools/check-release.ps1` passes.
- [ ] No real API keys or internal endpoints.
- [ ] New CAD write tools validate input and respect execution mode.
- [ ] Docs match changed behavior.

## Commit Style

Conventional Commits are preferred but not required:

```text
feat: add cad draw arc tool
fix: keep assistant replies out of drawing text
docs: clarify AutoCAD 2017 install path
```

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
