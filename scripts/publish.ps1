# Builds a self-contained, single-file IracingLiveCoach.App.exe and (optionally) drops a Desktop
# shortcut to it -- so the app launches like SimHub/other overlay tools: double-click an .exe, no
# `dotnet run`, no .NET runtime needing to be separately installed on the machine that runs it
# (self-contained bundles the runtime).
#
# Usage: powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 [-Shortcut]

param(
    [switch]$Shortcut
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\IracingLiveCoach.App\IracingLiveCoach.App.csproj"
$publishDir = Join-Path $env:LOCALAPPDATA "IracingLiveCoach"

Write-Host "Publicando em $publishDir ..."
dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir

$exePath = Join-Path $publishDir "IracingLiveCoach.App.exe"
if (-not (Test-Path $exePath)) {
    throw "Publish concluido mas $exePath nao foi encontrado."
}
Write-Host "Publicado: $exePath"

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktop "iRacing Live Coach.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($shortcutPath)
    $lnk.TargetPath = $exePath
    $lnk.WorkingDirectory = $publishDir
    $lnk.Description = "Overlay de coaching ao vivo para iRacing"
    $lnk.Save()
    Write-Host "Atalho criado: $shortcutPath"
}
