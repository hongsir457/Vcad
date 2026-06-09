#requires -version 5
<#
.SYNOPSIS
  Build the VCAD plugin for AutoCAD 2017+ (net47) and/or 2025+ (net8.0-windows),
  copy the binaries into the matching bundle\Contents folder, and (optionally)
  zip the bundle.

.PARAMETER Target
  acad2017  - build only the AutoCAD 2017-2024 bundle.
  acad2025  - build only the AutoCAD 2025+ bundle.
  all       - build both. (default)

.PARAMETER Configuration
  Release (default) | Debug

.PARAMETER Zip
  If set, produce VCAD-<Target>-<version>.zip next to the bundle.

.EXAMPLE
  # Build both, then install the 2017 bundle for the current user
  pwsh tools/pack-bundle.ps1 -Target all
  Copy-Item bundle\Acad2017 "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle" -Recurse -Force

.EXAMPLE
  pwsh tools/pack-bundle.ps1 -Target acad2017 -Zip

.NOTES
  Autodesk DLLs (AcMgd, AcDbMgd, AcCoreMgd) are NEVER copied into Contents.
  csproj references mark them Private=False; release-check.ps1 will fail the
  build if they sneak in.
#>

param(
  [ValidateSet('acad2017','acad2025','all')]
  [string]$Target = 'all',
  [ValidateSet('Release','Debug')]
  [string]$Configuration = 'Release',
  [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

function Build-One {
  param(
    [string]$Name,         # "Acad2017" or "Acad2025"
    [string]$Tfm           # "net47" or "net8.0-windows"
  )

  $projDir  = "src\Vcad.Plugin.$Name"
  $csproj   = "$projDir\Vcad.Plugin.$Name.csproj"
  $outDir   = "$projDir\bin\$Configuration\$Tfm"
  $bundle   = "bundle\$Name"
  $contents = "$bundle\Contents"

  if (-not (Test-Path $csproj)) {
    throw "Missing project file: $csproj"
  }

  Write-Host "[pack-bundle] dotnet build $csproj ($Configuration / $Tfm)" -ForegroundColor Cyan
  dotnet build $csproj -c $Configuration | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Build failed for $csproj" }

  if (-not (Test-Path $outDir)) {
    throw "Expected build output not found: $outDir"
  }

  # Clean Contents/ but keep the .gitkeep so the folder stays tracked.
  if (Test-Path $contents) {
    Get-ChildItem $contents -File | Where-Object { $_.Name -ne '.gitkeep' } |
      Remove-Item -Force
  } else {
    New-Item -ItemType Directory -Path $contents -Force | Out-Null
  }

  # ALLOWLIST: only ship the plugin's own DLLs plus its direct third-party
  # dependency. AutoCAD already provides the .NET BCL, so we must NOT
  # bundle the System.*/netstandard reference assemblies the SDK copies
  # into bin\, and we must NEVER bundle Autodesk managed DLLs or the
  # compile-only stub.
  $allowedNames = @(
    "Vcad.Plugin.$Name.dll",
    "Vcad.Plugin.$Name.pdb",
    'Vcad.Core.dll',
    'Vcad.Core.pdb',
    'Newtonsoft.Json.dll'
  )
  $banned = @(
    'AcMgd.dll','AcDbMgd.dll','AcCoreMgd.dll',
    'acmgd.dll','acdbmgd.dll','accoremgd.dll',
    'Autodesk.AutoCAD.Stubs.dll'
  )
  foreach ($name in $allowedNames) {
    $src = Join-Path $outDir $name
    if (-not (Test-Path $src -PathType Leaf)) {
      if ($name -match '\.pdb$') { continue } # pdb is optional
      throw "Expected build output missing: $name (under $outDir)"
    }
    if ($banned -contains $name) {
      throw "Refusing to ship banned file: $name"
    }
    Copy-Item $src (Join-Path $contents $name) -Force
  }

  Write-Host "[pack-bundle] populated $contents" -ForegroundColor Green
  Get-ChildItem $contents -File | Select-Object Name, Length | Format-Table | Out-Host

  if ($Zip) {
    $version = (Select-String -Path 'Directory.Build.props' -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
    $zipName = "VCAD-$Name-v$version.zip"
    if (Test-Path $zipName) { Remove-Item $zipName -Force }
    Compress-Archive -Path $bundle -DestinationPath $zipName
    Write-Host "[pack-bundle] wrote $zipName" -ForegroundColor Green
  }
}

# Final release-check before we hand off the bundle.
Write-Host "[pack-bundle] running release-check.ps1" -ForegroundColor Cyan
& "$PSScriptRoot\check-release.ps1" -Root $root
if ($LASTEXITCODE -ne 0) { throw "release-check failed" }

switch ($Target) {
  'acad2017' { Build-One -Name 'Acad2017' -Tfm 'net47' }
  'acad2025' { Build-One -Name 'Acad2025' -Tfm 'net8.0-windows' }
  'all'      {
    Build-One -Name 'Acad2017' -Tfm 'net47'
    Build-One -Name 'Acad2025' -Tfm 'net8.0-windows'
  }
}

Write-Host "[pack-bundle] DONE" -ForegroundColor Green
