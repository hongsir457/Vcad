#!/usr/bin/env bash
# Rejects:
#   1) Autodesk managed DLLs that must never be redistributed.
#   2) Likely real API keys, bearer tokens, or private endpoints.
# Usage:
#   tools/check-release.sh [path-to-scan]   (default: repo root)
set -u

ROOT="${1:-.}"
fail=0

echo "[release-check] scanning $ROOT"

# 1. Autodesk managed DLLs anywhere in the tree.
banned_dlls=(
  "AcMgd.dll" "AcDbMgd.dll" "AcCoreMgd.dll"
  "acmgd.dll" "acdbmgd.dll" "accoremgd.dll"
)
for name in "${banned_dlls[@]}"; do
  while IFS= read -r path; do
    # ignore matches inside .gitignore patterns (find still walks them)
    case "$path" in
      *"/bin/"*|*"/obj/"*) continue ;;
    esac
    echo "[release-check] FAIL: Autodesk DLL present: $path"
    fail=1
  done < <(find "$ROOT" -type f -name "$name" 2>/dev/null)
done

# 2. Suspicious secret strings in tracked files only.
# Use git ls-files when available so we don't scan bin/, obj/, packages/, etc.
if command -v git >/dev/null 2>&1 && git -C "$ROOT" rev-parse >/dev/null 2>&1; then
  files=$(git -C "$ROOT" ls-files)
else
  files=$(find "$ROOT" \
    -type d \( -name bin -o -name obj -o -name node_modules -o -name .git \) -prune -o \
    -type f -print)
fi

patterns=(
  '\bsk-[A-Za-z0-9]{20,}'
  '\bsk-ant-[A-Za-z0-9\-]{20,}'
  '\bAKIA[0-9A-Z]{16}\b'
  '\bxox[abprs]-[A-Za-z0-9-]{10,}'
  'Authorization:\s*Bearer\s+[A-Za-z0-9._-]+'
  'api[_-]?key\s*[=:]\s*["\x27]?[A-Za-z0-9_\-]{20,}["\x27]?'
  'https://api\.openai\.com'
  'https://api\.anthropic\.com'
)

# Whitelist files that legitimately mention the patterns above (docs, redactor, prompt).
allowlist=(
  "SECURITY.md"
  "tools/check-release.sh"
  "tools/check-release.ps1"
  "src/Vcad.AgentLite/SecretRedactor.cs"
  "src/Vcad.AgentLite/Providers/OpenAiProvider.cs"
  "src/Vcad.AgentLite/Providers/AnthropicProvider.cs"
  "src/Vcad.Plugin.Shared/Net/SecretRedactor.cs"
  "docs/vcad_open_blueprint_v0_5.html"
  "docs/troubleshooting.md"
  "docs/windows-verify.md"
  "README.md"
  "tests/Vcad.AgentLite.Tests/SecretRedactorTests.cs"
)
is_allowed() {
  local p="$1"
  for a in "${allowlist[@]}"; do
    [ "$p" = "$a" ] && return 0
  done
  return 1
}

for f in $files; do
  [ -f "$ROOT/$f" ] || continue
  is_allowed "$f" && continue
  for pat in "${patterns[@]}"; do
    if grep -E -nH -- "$pat" "$ROOT/$f" >/dev/null 2>&1; then
      echo "[release-check] FAIL: suspicious secret pattern in $f (/$pat/)"
      grep -E -nH -- "$pat" "$ROOT/$f" | head -5
      fail=1
    fi
  done
done

if [ "$fail" -ne 0 ]; then
  echo "[release-check] FAILED"
  exit 1
fi

echo "[release-check] OK"
