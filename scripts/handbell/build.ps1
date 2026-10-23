# Headless build of the handbell-spike fork (Windows x64), as measured in handbell-hero S3 Phase B.
# Prereqs: Unity 6000.3.5f2 (changeset 3fa8bc678cb0) with an activated license (Unity Hub sign-in),
# Blender registered for .blend files, .NET SDK, Git LFS (run `git lfs pull` after cloning).
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe",
    [string]$Output,
    [string]$ToolPath = (Join-Path $env:LOCALAPPDATA "yarg-handbell-tools")
)
$ErrorActionPreference = "Stop"
$project = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $Output) { $Output = Join-Path (Split-Path $project -Parent) "YARG-handbell-build\YARG.exe" }
$logs = Join-Path $project "Logs\handbell-build"
New-Item -ItemType Directory -Force $logs | Out-Null

# 1. NuGet restore. NuGetForUnity's restore-on-load does not run in a batch-mode import that
#    stops on the compile errors the missing packages cause, so restore with its CLI first.
if (-not (Test-Path (Join-Path $ToolPath "nugetforunity.exe"))) {
    dotnet tool install NuGetForUnity.Cli --version 4.5.0 --tool-path $ToolPath
}
& (Join-Path $ToolPath "nugetforunity.exe") restore $project

# 2. Import + compile
& $Unity -batchmode -nographics -quit -projectPath $project -logFile (Join-Path $logs "import.log") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Import/compile failed; see $logs\import.log" }

# 3. Player build
& $Unity -batchmode -nographics -quit -projectPath $project -buildTarget Win64 `
    -buildWindows64Player $Output -logFile (Join-Path $logs "build.log") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Player build failed; see $logs\build.log" }
Write-Host "Built $Output"
Write-Host "Run: YARG.exe -hgp [-hgp-keyboard] [-hgp-budget-ms 40] [-persistent-data-path <dir>]"
