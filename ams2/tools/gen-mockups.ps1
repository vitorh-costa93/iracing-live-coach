# Gera mockups do overlay/Control Center do AMS2 por época da F1 via OpenAI Images.
# Modelo definido pelo usuário (01/10/2026): gpt-image-2.5-flare, qualidade medium, sem fallback.
# Chave: lida de $env:OPENAI_API_KEY; se ausente, de ams2\.env (OPEN_AI_KEY=...) ou do -EnvFile.
# A chave nunca é impressa nem gravada.
#   powershell -ExecutionPolicy Bypass -File ams2\tools\gen-mockups.ps1 [-EnvFile <caminho>] [-Eras 1990s,2000s,2010s]
param(
    [string]$EnvFile,
    [string[]]$Eras = @('1990s', '2000s', '2010s'),
    [string]$Model = 'gpt-image-2.5-flare',
    [string]$Quality = 'medium'
)
$ErrorActionPreference = 'Stop'

if (-not $EnvFile) { $EnvFile = Join-Path $PSScriptRoot '..\.env' }
$key = $env:OPENAI_API_KEY
if (-not $key -and (Test-Path $EnvFile)) {
    $line = Get-Content $EnvFile | Where-Object { $_ -match '^\s*(OPEN_AI_KEY|OPENAI_API_KEY)\s*=' } | Select-Object -First 1
    if ($line) { $key = ($line -split '=', 2)[1].Trim().Trim('"').Trim("'") }
}
if (-not $key) { throw 'OPENAI_API_KEY não encontrada (variável de ambiente ou -EnvFile).' }

$styles = @{
    '1990s' = 'Formula 1 mid-1990s era: analog TV broadcast graphics, chunky italic sans-serif, sponsor-livery colors (red, white, blue, yellow), thin bevelled borders, slight CRT glow, boxy gradient panels, timing-tower look of 1990s race coverage.'
    '2000s' = 'Formula 1 2000s era (V10 / early digital TV): glossy brushed-metal and carbon-fibre textures, silver and orange accents, rounded translucent panels, early HD broadcast timing graphics, sleek wide sans-serif.'
    '2010s' = 'Formula 1 2010s era (hybrid / modern HD TV): flat clean dark panels, thin neon accent lines, team-colour tags, condensed geometric sans-serif, minimal glassy overlays like official F1 broadcast graphics.'
}
$shots = @{
    'overlay' = 'Screenshot-style mockup of a racing-game HUD overlay on top of a Automobilista 2 race scene (cars on track, no real logos). Show together: a standings tower with positions, driver short names, class colour tags and gaps; a Relative list (cars ahead and behind with time gaps); a fuel widget (fuel left, laps remaining, per-lap use); tyre temperature/wear widget; weather widget (air/track temp, rain). Text in English.'
    'control-center' = 'Desktop app window mockup, a settings dashboard for the overlay: left sidebar with sections (Widgets, Layout, Profiles, Columns, Colors, Sounds), main area showing widget on/off toggles, a live preview thumbnail of the overlay, a profile selector dropdown and column checkboxes for the standings widget. Text in English. Clean, implementation-ready UI.'
}

$headers = @{ Authorization = "Bearer $key"; 'Content-Type' = 'application/json' }
foreach ($era in $Eras) {
    if (-not $styles.ContainsKey($era)) { Write-Warning "Época desconhecida: $era"; continue }
    $dir = Join-Path $PSScriptRoot "..\mockups\$era"
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($name in $shots.Keys) {
        $out = Join-Path $dir "$name.png"
        $body = @{
            model   = $Model
            prompt  = "$($shots[$name]) Visual style: $($styles[$era])"
            size    = '1536x1024'
            quality = $Quality
            n       = 1
        } | ConvertTo-Json
        Write-Host "[$era] gerando $name ..."
        $r = Invoke-RestMethod -Uri 'https://api.openai.com/v1/images/generations' -Method Post -Headers $headers -Body $body -TimeoutSec 300
        [IO.File]::WriteAllBytes($out, [Convert]::FromBase64String($r.data[0].b64_json))
        if ($r.usage) { Write-Host ("  usage: " + ($r.usage | ConvertTo-Json -Compress)) }
        Write-Host "  salvo: $out"
    }
}
