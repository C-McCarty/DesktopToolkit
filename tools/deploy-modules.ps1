# Builds each module in Release and stages it into modules/<id>/ (exe + deps + manifest),
# which is what Toolkit.Runner discovers at runtime. Add new modules to $Modules.
#
# Usage:  pwsh tools/deploy-modules.ps1 [-Configuration Release]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tfm = "net9.0-windows"

# projectPath (relative to repo root) -> destination module folder name
$Modules = @(
    @{ Project = "src\Modules\MonitorArrangement\MonitorArrangement.csproj"; Dest = "monitor-arrangement"; Manifest = "src\Modules\MonitorArrangement\module.json" }
    @{ Project = "src\Modules\TaskbarManager\TaskbarManager.csproj";         Dest = "taskbar-manager";     Manifest = "src\Modules\TaskbarManager\module.json" }
    @{ Project = "src\Modules\AnimatedWallpaper\AnimatedDesktopBackground.csproj"; Dest = "animated-wallpaper"; Manifest = "src\Modules\AnimatedWallpaper\module.json" }
)

foreach ($m in $Modules) {
    $proj = Join-Path $root $m.Project
    Write-Host "Building $($m.Project) ..." -ForegroundColor Cyan
    dotnet build $proj -c $Configuration -v quiet --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($m.Project)" }

    $outDir = Join-Path (Split-Path $proj -Parent) "bin\$Configuration\$tfm"
    $destDir = Join-Path $root "modules\$($m.Dest)"
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    # Copy build output (exe, dlls, runtimeconfig, deps). Skip the per-module pdb noise.
    Copy-Item "$outDir\*" $destDir -Recurse -Force -Exclude *.pdb

    # Stage the manifest from source (so module.json lives with the project, not only the deploy).
    if ($m.Manifest) {
        Copy-Item (Join-Path $root $m.Manifest) (Join-Path $destDir "module.json") -Force
    }

    Write-Host "  -> deployed to modules\$($m.Dest)" -ForegroundColor Green
}

Write-Host "All modules deployed." -ForegroundColor Green
