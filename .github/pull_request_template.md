## Summary

<!-- What problem does this solve? Link the issue if there is one. -->

## Changes

- [ ] Code change
- [ ] Documentation change
- [ ] Schema / sample change
- [ ] CI / tooling change

## Checklist

- [ ] `dotnet build src/Vcad.Core/Vcad.Core.csproj` succeeds.
- [ ] `dotnet test tests/Vcad.Core.Tests/Vcad.Core.Tests.csproj` is green.
- [ ] `dotnet test tests/Vcad.AgentLite.Tests/Vcad.AgentLite.Tests.csproj` is green.
- [ ] `bash tools/check-release.sh .` passes (no Autodesk DLL, no real keys).
- [ ] If a new DSL command was added, schema + sample + test + docs updated.
- [ ] No third-party NuGet added without justification.

## Risk

<!-- What could break? Backwards compatibility? Performance? AutoCAD versions? -->
