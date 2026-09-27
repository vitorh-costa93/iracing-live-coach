# Publishes V3's two self-contained executables (OverlayHost + ControlCenter) side-by-side with
# V2's own publish output -- V2 lives at %LOCALAPPDATA%\IracingLiveCoach (scripts\publish.ps1);
# V3 goes to %LOCALAPPDATA%\IracingLiveCoachV3 so both can be run and compared without collision,
# per this rearchitecture's standing rule that V2 stays a frozen, untouched baseline.
#
# The user runs V3 from Desktop shortcuts that point at a full copy of the build living directly
# under the Desktop (not at %LOCALAPPDATA%), so every publish also mirrors the fresh output there
# (robocopy /MIR) -- standing rule as of 27/09/2026: the Desktop copy must never go stale. Skips
# the mirror (with a warning, not a failure) if either exe is currently running there, since an
# open handle can't be overwritten.
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
$desktopInstallDir = Join-Path ([Environment]::GetFolderPath("Desktop")) "iRacing Live Coach V3"

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

Write-Host "Espelhando build para $desktopInstallDir ..."
$desktopOverlayExe = Join-Path $desktopInstallDir "IracingLiveCoach.OverlayHost.exe"
$desktopControlCenterExe = Join-Path $desktopInstallDir "IracingLiveCoach.ControlCenter.exe"
$desktopLocked = (Test-Path $desktopOverlayExe) -and (Get-Process | Where-Object {
    try { $_.Path -eq $desktopOverlayExe -or $_.Path -eq $desktopControlCenterExe } catch { $false }
})
if ($desktopLocked) {
    Write-Warning "iRacingLiveCoach.OverlayHost.exe ou ControlCenter.exe da area de trabalho esta em execucao -- copia para $desktopInstallDir pulada. Feche o app e rode o publish de novo."
} else {
    robocopy $publishDir $desktopInstallDir /MIR /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy falhou ao espelhar para $desktopInstallDir (codigo $LASTEXITCODE)."
    }
    Write-Host "Area de trabalho atualizada: $desktopInstallDir"
}

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shell = New-Object -ComObject WScript.Shell

    $overlayShortcut = $shell.CreateShortcut((Join-Path $desktop "iRacing Live Coach V3 - Overlay.lnk"))
    $overlayShortcut.TargetPath = $desktopOverlayExe
    $overlayShortcut.WorkingDirectory = $desktopInstallDir
    $overlayShortcut.Description = "Overlay de coaching ao vivo para iRacing (V3, GPU-composited)"
    $overlayShortcut.Save()

    $controlShortcut = $shell.CreateShortcut((Join-Path $desktop "iRacing Live Coach V3 - Control Center.lnk"))
    $controlShortcut.TargetPath = $desktopControlCenterExe
    $controlShortcut.WorkingDirectory = $desktopInstallDir
    $controlShortcut.Description = "Painel de controle do overlay V3"
    $controlShortcut.Save()

    Write-Host "Atalhos criados na area de trabalho."
}
