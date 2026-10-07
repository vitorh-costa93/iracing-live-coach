# Mede o fps do widget Inputs e a taxa de amostragem das entradas, SO no modo --fake (nao toca o jogo; nunca com o iRacing aberto).
# Uso: powershell -ExecutionPolicy Bypass -File ams2\tools\measure-fps.ps1 [-Widget inputs] [-Seconds 6]
param([string]$Widget = 'inputs', [int]$Seconds = 6, [string]$Config = 'Release')
if (Get-Process -Name iRacingSim64DX11, iRacingSim64 -ErrorAction SilentlyContinue) { Write-Error 'iRacing aberto: nao rode o overlay agora.'; exit 2 }
$exe = Join-Path $PSScriptRoot "..\src\Ams2.OverlayHost\bin\$Config\net9.0-windows\Ams2.OverlayHost.exe"
if (-not (Test-Path $exe)) { dotnet build (Join-Path $PSScriptRoot '..\src\Ams2.OverlayHost') -c $Config -v q | Out-Null }
$tmp = Join-Path $env:TEMP ("ams2-fps-" + [guid]::NewGuid().ToString('N'))
& $exe --fake --widget $Widget --seconds $Seconds --measure --pipe "ams2-fps-$PID" --profiles-dir $tmp --x 40 --y 40 | Select-String '\[FPS\]'
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
