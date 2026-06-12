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
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/pack-bundle.ps1 -Target all
  Copy-Item bundle\Acad2017 "$env:APPDATA\Autodesk\ApplicationPlugins\VCAD-Acad2017.bundle" -Recurse -Force

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/pack-bundle.ps1 -Target acad2017 -Zip

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
  [switch]$Zip,
  [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

function Stop-AgentLiteUnderPath {
  param(
    [string]$PathPrefix,
    [string]$Reason
  )

  if ([string]::IsNullOrWhiteSpace($PathPrefix) -or -not (Test-Path $PathPrefix)) {
    return
  }

  $prefix = [System.IO.Path]::GetFullPath($PathPrefix).TrimEnd('\')
  $agents = Get-Process -Name Vcad.AgentLite -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and [System.IO.Path]::GetFullPath($_.Path).StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
  }
  if ($agents) {
    Write-Host "[deploy] stopping $($agents.Count) Vcad.AgentLite process(es) for $Reason" -ForegroundColor Yellow
    $agents | Stop-Process -Force
    Start-Sleep -Seconds 1
  }
}

function Stop-InstalledAgentLite {
  foreach ($pluginRoot in @("$env:APPDATA\Autodesk\ApplicationPlugins",
                           "$env:PROGRAMDATA\Autodesk\ApplicationPlugins")) {
    Stop-AgentLiteUnderPath -PathPrefix $pluginRoot -Reason $pluginRoot
  }
}

function Publish-AgentLite {
  param([string]$Contents)

  $agentProj = "src\Vcad.AgentLite\Vcad.AgentLite.csproj"
  $agentOut = Join-Path $Contents "AgentLite"
  Stop-AgentLiteUnderPath -PathPrefix $agentOut -Reason $agentOut
  if (Test-Path $agentOut) {
    Remove-Item $agentOut -Recurse -Force
  }
  New-Item -ItemType Directory -Path $agentOut -Force | Out-Null

  Write-Host "[pack-bundle] dotnet publish $agentProj -> $agentOut" -ForegroundColor Cyan
  dotnet publish $agentProj -c $Configuration -r win-x64 --self-contained true -o $agentOut /p:PublishSingleFile=false | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "AgentLite publish failed" }

  foreach ($name in @('AcMgd.dll','AcDbMgd.dll','AcCoreMgd.dll','Autodesk.AutoCAD.Stubs.dll')) {
    if (Test-Path (Join-Path $agentOut $name)) {
      throw "Refusing to ship banned file in AgentLite: $name"
    }
  }

  if (-not (Test-Path (Join-Path $agentOut 'Vcad.AgentLite.exe'))) {
    throw "Expected AgentLite executable missing under $agentOut"
  }
}

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

  # ALLOWLIST: ship the plugin's own DLLs plus explicit third-party runtime
  # dependencies. AutoCAD already provides most .NET BCL assemblies, so do not
  # blindly copy bin\*.dll; only include the NuGet assemblies this plugin uses.
  # We must NEVER bundle Autodesk managed DLLs or the compile-only stub.
  $requiredNames = @(
    "Vcad.Plugin.$Name.dll",
    "Vcad.Plugin.$Name.pdb",
    'Vcad.Core.dll',
    'Vcad.Core.pdb',
    'Newtonsoft.Json.dll',
    'UglyToad.PdfPig.dll',
    'UglyToad.PdfPig.Core.dll',
    'UglyToad.PdfPig.DocumentLayoutAnalysis.dll',
    'UglyToad.PdfPig.Fonts.dll',
    'UglyToad.PdfPig.Package.dll',
    'UglyToad.PdfPig.Tokenization.dll',
    'UglyToad.PdfPig.Tokens.dll'
  )
  $optionalNames = @(
    'Microsoft.Bcl.HashCode.dll',
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.ValueTuple.dll'
  )
  $banned = @(
    'AcMgd.dll','AcDbMgd.dll','AcCoreMgd.dll',
    'acmgd.dll','acdbmgd.dll','accoremgd.dll',
    'Autodesk.AutoCAD.Stubs.dll'
  )
  foreach ($name in $requiredNames) {
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
  foreach ($name in $optionalNames) {
    $src = Join-Path $outDir $name
    if (-not (Test-Path $src -PathType Leaf)) { continue }
    if ($banned -contains $name) {
      throw "Refusing to ship banned file: $name"
    }
    Copy-Item $src (Join-Path $contents $name) -Force
  }

  Publish-AgentLite -Contents $contents

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

function Deploy-One {
  param([string]$Name)

  $bundle  = "bundle\$Name"
  $dest    = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\VCAD-$Name.bundle"

  # Agent Lite is started from inside Contents\AgentLite and keeps those
  # binaries locked even after AutoCAD exits.
  Stop-InstalledAgentLite

  # Kill any running AutoCAD so its loaded DLL handle is released.
  $acad = Get-Process -Name acad -ErrorAction SilentlyContinue
  if ($acad) {
    Write-Host "[deploy] killing $($acad.Count) acad.exe process(es)" -ForegroundColor Yellow
    $acad | Stop-Process -Force
    Start-Sleep -Seconds 2
  }

  # Remove EVERY VCAD* bundle in BOTH plugin paths so an old one can't win
  # the load race. We do not own anything else under ApplicationPlugins.
  foreach ($root in @("$env:APPDATA\Autodesk\ApplicationPlugins",
                      "$env:PROGRAMDATA\Autodesk\ApplicationPlugins")) {
    if (Test-Path $root) {
      Get-ChildItem $root -Directory -Filter "VCAD*" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "[deploy] removing existing $($_.FullName)" -ForegroundColor Yellow
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Copy-Item $bundle $dest -Recurse -Force
  Write-Host "[deploy] installed -> $dest" -ForegroundColor Green
  Get-ChildItem "$dest\Contents" | Select-Object Name, Length | Format-Table | Out-Host
}

# Final release-check before we hand off the bundle.
Write-Host "[pack-bundle] running release-check.ps1" -ForegroundColor Cyan
& "$PSScriptRoot\check-release.ps1" -Root $root
if ($LASTEXITCODE -ne 0) { throw "release-check failed" }

$built = @()
switch ($Target) {
  'acad2017' { Build-One -Name 'Acad2017' -Tfm 'net47'; $built += 'Acad2017' }
  'acad2025' { Build-One -Name 'Acad2025' -Tfm 'net8.0-windows'; $built += 'Acad2025' }
  'all'      {
    Build-One -Name 'Acad2017' -Tfm 'net47'; $built += 'Acad2017'
    Build-One -Name 'Acad2025' -Tfm 'net8.0-windows'; $built += 'Acad2025'
  }
}

if ($Deploy) {
  foreach ($name in $built) { Deploy-One -Name $name }
  Write-Host "[pack-bundle] DEPLOY DONE — restart AutoCAD now" -ForegroundColor Green
} else {
  Write-Host "[pack-bundle] DONE" -ForegroundColor Green
}
