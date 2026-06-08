#requires -version 5
<#
.SYNOPSIS
  Reject Autodesk managed DLLs and likely real API keys.
.EXAMPLE
  pwsh tools/check-release.ps1
#>
param([string]$Root = ".")

$ErrorActionPreference = 'Stop'
$fail = 0

Write-Host "[release-check] scanning $Root"

$bannedDlls = @(
  'AcMgd.dll','AcDbMgd.dll','AcCoreMgd.dll',
  'acmgd.dll','acdbmgd.dll','accoremgd.dll'
)
foreach ($name in $bannedDlls) {
  Get-ChildItem -Path $Root -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
      Write-Host "[release-check] FAIL: Autodesk DLL present: $($_.FullName)"
      $fail = 1
    }
}

$patterns = @(
  'sk-[A-Za-z0-9]{20,}',
  'sk-ant-[A-Za-z0-9\-]{20,}',
  'AKIA[0-9A-Z]{16}',
  'xox[abprs]-[A-Za-z0-9\-]{10,}',
  'Authorization:\s*Bearer\s+[A-Za-z0-9._\-]+',
  'api[_-]?key\s*[=:]\s*["'']?[A-Za-z0-9_\-]{20,}["'']?',
  'https://api\.openai\.com',
  'https://api\.anthropic\.com'
)

$allowlist = @(
  'SECURITY.md',
  'tools/check-release.sh',
  'tools/check-release.ps1',
  'src/Vcad.AgentLite/SecretRedactor.cs',
  'src/Vcad.AgentLite/Providers/OpenAiProvider.cs',
  'src/Vcad.AgentLite/Providers/AnthropicProvider.cs',
  'src/Vcad.Plugin.Shared/Net/SecretRedactor.cs',
  'docs/vcad_open_blueprint_v0_5.html',
  'docs/troubleshooting.md',
  'README.md'
)

# Prefer tracked files only.
$files = $null
try {
  $files = git -C $Root ls-files 2>$null
} catch {}
if (-not $files) {
  $files = Get-ChildItem -Path $Root -Recurse -File `
    | Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules|\.git)\\' } `
    | ForEach-Object { $_.FullName.Substring((Resolve-Path $Root).Path.Length).TrimStart('\','/') -replace '\\','/' }
}

foreach ($rel in $files) {
  $full = Join-Path $Root $rel
  if (-not (Test-Path $full -PathType Leaf)) { continue }
  if ($allowlist -contains $rel) { continue }

  foreach ($pat in $patterns) {
    $hits = Select-String -Path $full -Pattern $pat -ErrorAction SilentlyContinue
    if ($hits) {
      Write-Host "[release-check] FAIL: suspicious secret pattern in $rel (/$pat/)"
      $hits | Select-Object -First 5 | ForEach-Object { Write-Host "  $($_.LineNumber): $($_.Line)" }
      $fail = 1
    }
  }
}

if ($fail -ne 0) {
  Write-Host "[release-check] FAILED"
  exit 1
}
Write-Host "[release-check] OK"
