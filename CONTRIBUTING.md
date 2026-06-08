# Contributing to VCAD

Thanks for taking the time to contribute. VCAD is an open-source AutoCAD
plugin framework licensed under Apache-2.0; by submitting a contribution
you agree it is licensed the same way.

## What we want

- Bug fixes for existing whitelisted commands.
- Better error messages and diagnostics.
- New AutoCAD version compatibility reports (we keep a matrix; see
  `docs/troubleshooting.md`).
- Additional sample DSL JSON in `samples/commands/`.
- New language pairs for the model-settings UI labels.

## What we will say no to (for now)

- Login / billing / cloud calls — see [the v0.5 blueprint](docs/vcad_open_blueprint_v0_5.html).
- Anything that uploads DWG, prompts, or logs by default.
- Anything that adds a built-in default API key or default remote endpoint.
- New whitelisted commands without an accompanying RFC (file an issue
  using the **Command Request** template first).

## Developer setup

1. Install the .NET SDK 8.x.
2. Clone the repo.
3. Restore and build the parts that don't need AutoCAD:

   ```bash
   dotnet build src/Vcad.Core/Vcad.Core.csproj
   dotnet build src/Vcad.AgentLite/Vcad.AgentLite.csproj
   dotnet test  tests/Vcad.Core.Tests/Vcad.Core.Tests.csproj
   dotnet test  tests/Vcad.AgentLite.Tests/Vcad.AgentLite.Tests.csproj
   ```

4. On Windows, with AutoCAD installed, set the AutoCAD managed-DLL paths
   in your environment, then build the plugin csprojs:

   ```powershell
   $env:AutoCAD2017_Managed = "C:\Program Files\Autodesk\AutoCAD 2017"
   $env:AutoCAD2025_Managed = "C:\Program Files\Autodesk\AutoCAD 2025"
   dotnet build src\Vcad.Plugin.Acad2017\Vcad.Plugin.Acad2017.csproj
   dotnet build src\Vcad.Plugin.Acad2025\Vcad.Plugin.Acad2025.csproj
   ```

> Never commit `AcMgd.dll`, `AcDbMgd.dll`, `AcCoreMgd.dll`. CI rejects PRs
> that include them.

## Pull request checklist

- [ ] Code builds on `Vcad.Core` and on at least one of the plugin csprojs.
- [ ] `dotnet test` is green for both test projects.
- [ ] The release-check script (`tools/check-release.sh`) passes.
- [ ] No new third-party NuGet without a note on why.
- [ ] No secrets, no real API keys, no internal endpoints anywhere in the
      diff (CI also scans for these).
- [ ] If you added a new command, you also added a JSON schema entry,
      a sample, and a test.

## Commit style

Conventional Commits is preferred but not enforced:

```
feat: add draw_polyline command
fix: handle empty layer name correctly
docs: clarify SECURELOAD instructions
```

## Reviews

Maintainers will look at PRs as time allows. Drive-by reviews from other
contributors are welcome — please keep them technical and respectful.

See [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md).
