# Security Policy

VCAD is designed so that an open-source install can be used safely with
arbitrary LLM-generated input. The plugin and the local Agent are the
final security boundary — never the prompt.

## What VCAD does not do

- It does **not** execute arbitrary code returned by an LLM.
- It does **not** execute arbitrary AutoLISP strings.
- It does **not** shell out to the operating system.
- It does **not** read or write arbitrary files on your disk.
- It does **not** upload your DWG, prompts, or logs anywhere.
- It does **not** ship with any built-in API key or pre-configured remote
  endpoint.

## Defenses in depth

1. **JSON schema and whitelist.** Every command is validated against
   `vcad_dsl_v1`. Unknown command types fail with `E_COMMAND_NOT_ALLOWED`.
2. **Parameter range checks.** See `ParameterLimits` in `Vcad.Core`.
   Coordinates capped at 1e9 mm; dimensions in (0, 1e8 mm]; text length
   ≤ 2048; layer name ≤ 255; request body ≤ 1 MB.
3. **One transaction per request.** All commands in a request execute
   inside a single `LockDocument` + `Transaction` + Undo group. If any
   command fails, the transaction is aborted and **nothing is committed**.
4. **Loopback-only Agent.** `Vcad.AgentLite` binds `127.0.0.1` only. A
   per-user token (`X-VCAD-Agent-Token`) is required.
5. **DPAPI-encrypted secrets.** API keys saved in the UI are encrypted
   with Windows DPAPI (CurrentUser scope) and stored under `%APPDATA%`.
6. **Log redaction.** Error messages, exception text, and copy/diagnostic
   exports run through `SecretRedactor` so `Authorization`, `Bearer`,
   `api_key=`, `sk-...`, and `sk-ant-...` are replaced with `***`.

## Reporting a vulnerability

Please open a **private** security advisory on GitHub (Security tab →
"Report a vulnerability") rather than a public issue. Include:

- Affected version (e.g. `v0.1.0`).
- AutoCAD version and OS.
- A minimal reproducer if possible (a small DSL JSON, or a sequence of
  steps in the sidebar).
- Whether the issue lets a crafted DSL or LLM response break the
  invariants above (e.g. run shell commands, exfiltrate files, escape the
  whitelist).

We aim to acknowledge within 5 business days. There is no bug bounty.

## Out of scope

- Reports that require an attacker to already have local code execution
  on the user's machine.
- Findings that depend on the user disabling `SECURELOAD` or copying the
  plugin into a non-standard, untrusted directory.
- Vulnerabilities in third-party libraries that VCAD does not actually
  expose (please report those upstream).
