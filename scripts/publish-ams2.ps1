# Publica os dois executaveis do app AMS2 (OverlayHost + ControlCenter, self-contained, single-file)
# em %LOCALAPPDATA%\Ams2LiveCoach e espelha a mesma saida em Desktop\AMS2 Live Coach (robocopy /MIR),
# como o publish-v3.ps1: a copia do Desktop nunca pode ficar velha. Se algum exe do Desktop estiver
# aberto, o espelho e pulado com aviso (feche o app e rode de novo).
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\publish-ams2.ps1 [-Shortcut]

param(
    [switch]$Shortcut
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$overlayProject = Join-Path $repoRoot "ams2\src\Ams2.OverlayHost\Ams2.OverlayHost.csproj"
$controlCenterProject = Join-Path $repoRoot "ams2\src\Ams2.ControlCenter\Ams2.ControlCenter.csproj"
$fontsDir = Join-Path $repoRoot "ams2\fonts"
$publishDir = Join-Path $env:LOCALAPPDATA "Ams2LiveCoach"
$desktopInstallDir = Join-Path ([Environment]::GetFolderPath("Desktop")) "AMS2 Live Coach"

foreach ($p in @($overlayProject, $controlCenterProject)) {
    Write-Host "Publicando $(Split-Path -Leaf $p) em $publishDir ..."
    dotnet publish $p -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou para $p" }
}

$overlayExe = Join-Path $publishDir "Ams2.OverlayHost.exe"
$controlCenterExe = Join-Path $publishDir "Ams2.ControlCenter.exe"
foreach ($exe in @($overlayExe, $controlCenterExe)) {
    if (-not (Test-Path $exe)) { throw "Publish concluido mas $exe nao foi encontrado." }
}

# O host procura as fontes em fonts\ ao lado do exe; garante a copia mesmo em single-file.
$publishFonts = Join-Path $publishDir "fonts"
New-Item -ItemType Directory -Force $publishFonts | Out-Null
Copy-Item (Join-Path $fontsDir "*.ttf") $publishFonts -Force
Write-Host "Publicado: $overlayExe e $controlCenterExe"

Write-Host "Espelhando build para $desktopInstallDir ..."
$desktopOverlayExe = Join-Path $desktopInstallDir "Ams2.OverlayHost.exe"
$desktopControlCenterExe = Join-Path $desktopInstallDir "Ams2.ControlCenter.exe"
$desktopLocked = Get-Process | Where-Object {
    try { $_.Path -eq $desktopOverlayExe -or $_.Path -eq $desktopControlCenterExe } catch { $false }
}
if ($desktopLocked) {
    Write-Warning "Ams2.OverlayHost.exe ou Ams2.ControlCenter.exe da area de trabalho esta em execucao -- copia para $desktopInstallDir pulada. Feche o app e rode o publish de novo."
} else {
    robocopy $publishDir $desktopInstallDir /MIR /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy falhou ao espelhar para $desktopInstallDir (codigo $LASTEXITCODE)." }
    Write-Host "Area de trabalho atualizada: $desktopInstallDir"
}

if ($Shortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shell = New-Object -ComObject WScript.Shell
    foreach ($s in @(
        @{ Name = "AMS2 Live Coach - Overlay"; Target = $desktopOverlayExe; Desc = "Overlay ao vivo para Automobilista 2" },
        @{ Name = "AMS2 Live Coach - Control Center"; Target = $desktopControlCenterExe; Desc = "Painel de controle do overlay AMS2" })) {
        $lnk = $shell.CreateShortcut((Join-Path $desktop "$($s.Name).lnk"))
        $lnk.TargetPath = $s.Target
        $lnk.WorkingDirectory = $desktopInstallDir
        $lnk.Description = $s.Desc
        $lnk.Save()
    }
    Write-Host "Atalhos criados na area de trabalho."
}
