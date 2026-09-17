# Publishes V3's two self-contained executables (OverlayHost + ControlCenter) side-by-side with
# V2's own publish output -- V2 lives at %LOCALAPPDATA%\IracingLiveCoach (scripts\publish.ps1);
# V3 goes to %LOCALAPPDATA%\IracingLiveCoachV3 so both can be run and compared without collision,
# per this rearchitecture's standing rule that V2 stays a frozen, untouched baseline.
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\publish-v3.ps1 [-Shortcut]

param(
    [switch]$Shortcut
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $repoRoot "v3\src\IracingLiveCoach.OverlayHost\IracingLiveCoach.OverlayHost.csproj"
$controlCenterProject = Join-Path $repoRoot "v3\src\IracingLiveCoach.ControlCenter\IracingLiveCoach.ControlCenter.csproj"
$publishDir = Join-Path $env:LOCALAPPDATA "IracingLiveCoachV3"

Write-Host "Publicando OverlayHost em $publishDir ..."
dotnet publish $overlayProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

$overlayExe = Join-Path $publishDir "IracingLiveCoach.OverlayHost.exe"
if (-not (Test-Path $overlayExe)) {
    throw "Publish concluido mas $overlayExe nao foi encontrado."
}
Write-Host "Publicado: $overlayExe"

Write-Host "Publicando Control Center em $publishDir ..."
dotnet publish $controlCenterProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

$controlCenterExe = Join-Path $publishDir "IracingLiveCoach.ControlCenter.exe"
if (-not (Test-Path $controlCenterExe)) {
    throw "Publish concluido mas $controlCenterExe nao foi encontrado."
}
Write-Host "Publicado: $controlCenterExe"

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shell = New-Object -ComObject WScript.Shell

    $overlayShortcut = $shell.CreateShortcut((Join-Path $desktop "iRacing Live Coach V3 - Overlay.lnk"))
    $overlayShortcut.TargetPath = $overlayExe
    $overlayShortcut.WorkingDirectory = $publishDir
    $overlayShortcut.Description = "Overlay de coaching ao vivo para iRacing (V3, GPU-composited)"
    $overlayShortcut.Save()

    $controlShortcut = $shell.CreateShortcut((Join-Path $desktop "iRacing Live Coach V3 - Control Center.lnk"))
    $controlShortcut.TargetPath = $controlCenterExe
    $controlShortcut.WorkingDirectory = $publishDir
    $controlShortcut.Description = "Painel de controle do overlay V3"
    $controlShortcut.Save()

    Write-Host "Atalhos criados na area de trabalho."
}
