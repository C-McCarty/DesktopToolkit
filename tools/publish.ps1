# Produces a fully self-contained distributable in dist/ — no .NET install needed at all:
#   dist/Toolkit.Runner.exe         self-contained host
#   dist/modules/<id>/              each module published self-contained + its module.json
#
# PublishSingleFile is OFF everywhere: Animated Wallpaper's LibVLC needs its plugin folder
# as real files beside the exe, and single-file gains little here. Each module carries its
# own copy of the .NET runtime, so the dist is large (esp. Animated Wallpaper + LibVLC).
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tfm = "net9.0-windows"
$dist = Join-Path $root "dist"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Publishing host (self-contained, $Runtime) ..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\Host\Toolkit.Runner\Toolkit.Runner.csproj") `
    -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=false `
    -o $dist | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Host publish failed." }

# id -> source project + manifest
$Modules = @(
    @{ Project = "src\Modules\MonitorArrangement\MonitorArrangement.csproj";       Dest = "monitor-arrangement"; Manifest = "src\Modules\MonitorArrangement\module.json" }
    @{ Project = "src\Modules\TaskbarManager\TaskbarManager.csproj";               Dest = "taskbar-manager";     Manifest = "src\Modules\TaskbarManager\module.json" }
    @{ Project = "src\Modules\AnimatedWallpaper\AnimatedDesktopBackground.csproj"; Dest = "animated-wallpaper";  Manifest = "src\Modules\AnimatedWallpaper\module.json" }
)

foreach ($m in $Modules) {
    Write-Host "Publishing module $($m.Dest) (self-contained, $Runtime) ..." -ForegroundColor Cyan
    $proj = Join-Path $root $m.Project
    $destDir = Join-Path $dist "modules\$($m.Dest)"

    dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -o $destDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($m.Project)" }

    Get-ChildItem $destDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $root $m.Manifest) (Join-Path $destDir "module.json") -Force
    Write-Host "  -> dist\modules\$($m.Dest)" -ForegroundColor Green
}

Write-Host "Published to $dist" -ForegroundColor Green
