# Mockups v2 do overlay/Control Center do AMS2, usando capturas de referência (ams2\reference).
# Modelo definido pelo usuário: gpt-image-2.5-flare, qualidade medium. Endpoint: images/edits (com referências) ou generations.
# Chave: ams2\.env (OPEN_AI_KEY=...) ou $env:OPENAI_API_KEY. Nunca é impressa; passa ao curl por stdin.
param(
    [string[]]$Eras = @('1990s', '2000s', '2010s'),
    [string[]]$Shots = @(),
    [string]$Model = 'gpt-image-2.5-flare',
    [string]$Quality = 'medium'
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$ref = Join-Path $root 'reference'

$key = $env:OPENAI_API_KEY
$envFile = Join-Path $root '.env'
if (-not $key -and (Test-Path $envFile)) {
    $line = Get-Content $envFile | Where-Object { $_ -match '^\s*(OPEN_AI_KEY|OPENAI_API_KEY)\s*=' } | Select-Object -First 1
    if ($line) { $key = ($line -split '=', 2)[1].Trim().Trim('"').Trim("'") }
}
if (-not $key) { throw 'Chave da OpenAI não encontrada.' }

$inputGraph = 'Also include an "Inputs" widget like the iRacing input graph: a rolling time-trace of the last ~10 seconds with throttle (green line/area), brake (red line/area) and steering (thin white line), plus two vertical pedal bars for throttle and brake and a small gear and speed readout, restyled to match this era.'

$eraDefs = @{
    '1990s' = @{
        label = 'late 1990s to early 2000s (FOM world feed, 1998-2001)'
        refs  = @('f1-1998-fastest-lap.webp', 'f1-1998-battle-gap.webp', 'f1-1998-lap-times.webp', 'f1-1998-driver-caption.png')
        style = 'Match the attached reference screenshots of the broadcast graphics of that era: a translucent dark grey-green bar across the lower part of the screen, yellow square position boxes with a black bold number, white bold wide sans-serif driver names with letter spacing, teal/cyan team and info text, round teal number badges, yellow LAP labels, gold gradient gap bars, soft 4:3 analog TV look. Reuse these exact graphic elements for the standings, relative and info widgets.'
    }
    '2000s' = @{
        label = '2004-2008 (FOM world feed graphics set 2004-2009)'
        refs  = @('typo-2005-standings.png', 'typo-2005-laptimes.png', 'typo-2005-captions.png', 'f1-2005-china-tower-bottom.png', 'f1-2000s-speedo-dial.png', 'f1-2007-tower-left.webp')
        style = 'TYPOGRAPHY IS CRITICAL. The first three attached images are 3x enlarged crops of the real text; copy their lettering exactly: a single neutral grotesque / humanist sans-serif in medium to semi-bold weight, NOT condensed, NOT italic, NOT all caps. Driver names are in mixed case with a first-initial prefix (for example "M Schumacher", "de la Rosa", "Fisichella") in dark navy on the white plate, left-aligned with a small left padding. Gaps and times are in bold white tabular figures on black (for example "+6.195", "1:47.143"), right-aligned, with a plus sign. Column headers such as "Lap 27" are medium weight white on dark purple with light letter-spacing and centred. Position numbers are bold white in small boxes. The lap counter "29/56" is dark bold on a white box. Reproduce the broadcast graphics of the attached high-definition 2005 screenshots exactly, with faithful proportions, colours, fonts and spacing (the first three images): the standings in two side-by-side blocks at the bottom, each row a white name plate with dark navy bold text beside a black cell with white numbers, a small red position box with the number 1, dark navy position boxes for the others, the lap-times table with a dark purple header (Lap 27, Lap 26, Lap 25), black time cells and green gap cells, a small white lap counter box at top centre, and the driver caption plates with a flag and a tyre-supplier letter. Also take the look and feel from the other attached 2007 screenshots:white name plates with dark navy text next to dark navy cells with white numbers, a small red position box, orange and green gap cells, a small white lap counter box at top centre, a narrow vertical timing tower of three-letter codes on the left, straight horizontal and vertical lines, no slanted shapes. For the Inputs widget use the round analog speedometer from the fourth attached image (white dial with numbers, black needle, gear box, a green THROTTLE label and a red BRAKE label, a segmented speed bar) as the base, and embed the input graphs in it: a rolling throttle (green), brake (red) and steering (white) trace in a panel attached under the dial, with throttle and brake bars. Keep everything inside a 4:3 safe area. Do not draw any logo.'
    }
    '2010s' = @{
        label = 'modern 2010s (2017 F1 broadcast graphics)'
        refs  = @('v1-2010s-overlay.png')
        style = 'Keep the look of the attached image exactly: flat dark panels with rounded corners, thin cyan accent lines, red slash markers on panel titles, small tyre compound tags (HYB, MED, SOFT), numbers in small yellow squares, a track minimap, white clean sans-serif. Do not use any yellow-and-black bold broadcast style. Clean modern HD broadcast look. Add the Inputs widget in this same style.'
    }
}
$shotDefs = @{
    'overlay' = "Mockup of a racing-game HUD overlay over an Automobilista 2 Formula race scene (generic cars, no real logos). Widgets: standings tower (position, 3-letter name, class tag, gap), Relative list (cars ahead and behind with time gaps), fuel widget (fuel left, laps remaining, use per lap), tyre temperature and wear widget, weather widget. $inputGraph Text in English."
    'control-center' = "Desktop app window mockup, the settings dashboard of the overlay: left sidebar (Widgets, Layout, Profiles, Columns, Colors, Sounds), main area with widget on/off toggles including Standings, Relative, Fuel, Tyres, Weather and Inputs, a live preview thumbnail of the overlay showing the Inputs trace graph, a profile dropdown, and column checkboxes for the standings widget. Show full customisation: a widget list with drag handles to reorder, per-widget sliders for font size and widget scale, a stepper for number of visible rows, opacity, and a layout editor canvas where widgets can be dragged and resized. Text in English. Implementation-ready UI."
}

$cfg = Join-Path $env:TEMP "curl-$([guid]::NewGuid().ToString('N')).cfg"
try {
    foreach ($era in $Eras) {
        $e = $eraDefs[$era]
        $dir = Join-Path $root "mockups\$era"
        New-Item -ItemType Directory -Force $dir | Out-Null
        foreach ($name in $shotDefs.Keys) {
            if ($Shots.Count -gt 0 -and $Shots -notcontains $name) { continue }
            $out = Join-Path $dir "$name.png"
            $prompt = "$($shotDefs[$name]) Era: $($e.label). Visual style: $($e.style)"
            $prompt = $prompt.Replace('"', "'")
            Write-Host "[$era] gerando $name ..."
            $resp = Join-Path $env:TEMP "resp-$([guid]::NewGuid().ToString('N')).json"
            if ($e.refs.Count -gt 0) {
                $pf = Join-Path $env:TEMP "prompt-$([guid]::NewGuid().ToString('N')).txt"
                [IO.File]::WriteAllText($pf, $prompt, (New-Object Text.UTF8Encoding($false)))
                $curlArgs = @('-sS', '-X', 'POST', 'https://api.openai.com/v1/images/edits', '-K', $cfg,
                          '-F', "model=$Model", '-F', "quality=$Quality", '-F', 'size=1536x1024', '-F', "prompt=<$pf", '-o', $resp)
                foreach ($r in $e.refs) {
                    $mime = if ($r -like '*.webp') { 'image/webp' } elseif ($r -like '*.png') { 'image/png' } else { 'image/jpeg' }
                    $curlArgs += @('-F', "image[]=@$(Join-Path $ref $r);type=$mime")
                }
            } else {
                $bodyFile = Join-Path $env:TEMP "body-$([guid]::NewGuid().ToString('N')).json"
                $json = @{ model = $Model; prompt = $prompt; size = '1536x1024'; quality = $Quality; n = 1 } | ConvertTo-Json
                [IO.File]::WriteAllText($bodyFile, $json, (New-Object Text.UTF8Encoding($false)))
                $curlArgs = @('-sS', '-X', 'POST', 'https://api.openai.com/v1/images/generations', '-K', $cfg,
                          '-H', 'Content-Type: application/json', '--data-binary', "@$bodyFile", '-o', $resp)
            }
            [IO.File]::WriteAllText($cfg, "header = `"Authorization: Bearer $key`"")
            & curl.exe @curlArgs
            $j = Get-Content $resp -Raw | ConvertFrom-Json
            Remove-Item $resp -Force
            if (-not $j.data) { Write-Warning ("Falhou: " + ($j.error.message)); continue }
            [IO.File]::WriteAllBytes($out, [Convert]::FromBase64String($j.data[0].b64_json))
            if ($j.usage) { Write-Host ("  usage: " + ($j.usage | ConvertTo-Json -Compress)) }
            Write-Host "  salvo: $out"
        }
    }
} finally { if (Test-Path $cfg) { Remove-Item $cfg -Force } }
