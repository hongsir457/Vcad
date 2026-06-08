# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this project adheres to [SemVer](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Apache-2.0 LICENSE.
- `Vcad.Core` (netstandard2.0): DSL DTOs, validator, result contract,
  error codes, mapping log.
- `Vcad.Plugin.Shared`: AutoCAD .NET plugin source (sidebar with DSL Input
  and Model Settings tabs, DPAPI-encrypted API key storage, Agent Lite
  client, secret redactor).
- `Vcad.Plugin.Acad2017` (net47, AutoCAD 2017–2024) and
  `Vcad.Plugin.Acad2025` (net8.0-windows, AutoCAD 2025+) csprojs.
- `Vcad.AgentLite` (.NET 8 minimal API on `127.0.0.1:8765`) with
  Echo / OpenAI / Anthropic providers and request body / token guards.
- Bundle `PackageContents.xml` for both AutoCAD ranges.
- JSON schemas for `vcad_dsl_v1` and `vcad_result_v1`.
- Sample DSL JSON, install / troubleshooting / SECURITY / CONTRIBUTING /
  CODE_OF_CONDUCT.
- Release check (`tools/check-release.sh` and PowerShell variant) that
  rejects Autodesk managed DLLs and likely real API keys.
- GitHub Actions workflow: build Core + AgentLite, run tests, run the
  release check.

### Security
- All DSL execution runs inside one `LockDocument` + `Transaction` + Undo
  group; failures abort the transaction.
- Parameter ranges enforced in Core: coordinate ≤ 1e9 mm, dimension
  ≤ 1e8 mm, text ≤ 2048 chars, layer name ≤ 255 chars, request ≤ 1 MB.
- Agent Lite binds 127.0.0.1 only, requires `X-VCAD-Agent-Token`, caps
  request bodies at 256 KB and natural-language text at 8000 chars.
