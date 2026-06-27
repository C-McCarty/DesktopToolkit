# Packages each deployed module in modules/ into dist/packages/<id>.zip and emits a
# dist/packages/catalog.json template. To publish a catalog:
#   1) pwsh tools/deploy-modules.ps1      (or publish.ps1) to populate modules/
#   2) pwsh tools/package-modules.ps1     to produce the zips + catalog.json
#   3) upload each dist/packages/<id>.zip as a GitHub *Release asset*
#   4) put the asset download URLs into catalog.json's "package" fields
#   5) commit catalog.json to your catalog repo; point the host's Catalog source at its
#      raw URL (https://raw.githubusercontent.com/<owner>/<repo>/<branch>/catalog.json)
#
# Usage: pwsh tools/package-modules.ps1 [-Owner you -Repo your-catalog -Tag v1.0.0]
param(
    [string]$Owner = "<owner>",
    [string]$Repo  = "<repo>",
    [string]$Tag   = "v1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$modulesDir = Join-Path $root "modules"
$outDir = Join-Path $root "dist\packages"

if (-not (Test-Path $modulesDir)) { throw "No modules folder - run deploy-modules.ps1 first." }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$entries = @()
foreach ($dir in Get-ChildItem $modulesDir -Directory) {
    $manifestPath = Join-Path $dir.FullName "module.json"
    if (-not (Test-Path $manifestPath)) { continue }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

    $zip = Join-Path $outDir "$($dir.Name).zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "Packaging $($dir.Name) ..." -ForegroundColor Cyan
    # Archive the folder CONTENTS (module.json at zip root).
    Compress-Archive -Path (Join-Path $dir.FullName "*") -DestinationPath $zip -CompressionLevel Optimal

    $entries += [ordered]@{
        id          = $manifest.id
        name        = $manifest.name
        description = $manifest.description
        version     = $manifest.version
        package     = "https://github.com/$Owner/$Repo/releases/download/$Tag/$($dir.Name).zip"
    }
    Write-Host "  -> dist\packages\$($dir.Name).zip" -ForegroundColor Green
}

$catalog = [ordered]@{ modules = $entries }
$catalogPath = Join-Path $outDir "catalog.json"
$catalog | ConvertTo-Json -Depth 6 | Out-File $catalogPath -Encoding utf8
$count = $entries.Count
Write-Host "Wrote $catalogPath with $count modules." -ForegroundColor Green
Write-Host "Edit the package URLs if your release tag or asset names differ." -ForegroundColor Yellow
