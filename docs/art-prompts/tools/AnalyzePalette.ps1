# Analyze dominant colors of a PNG (alpha>128, magenta background skipped).
# Usage: powershell -File AnalyzePalette.ps1 <png> [...]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Paths)

Add-Type -AssemblyName System.Drawing

function Get-ColorProfile([string]$path, [int]$step = 2) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    $w = $bmp.Width; $h = $bmp.Height
    $hist = @{}
    $count = 0; $sumR = 0; $sumG = 0; $sumB = 0
    for ($y = 0; $y -lt $h; $y += $step) {
        for ($x = 0; $x -lt $w; $x += $step) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.A -lt 128) { continue }
            if ($p.R -gt 200 -and $p.B -gt 200 -and $p.G -lt 80) { continue }  # magenta key
            $count++
            $sumR += $p.R; $sumG += $p.G; $sumB += $p.B
            $key = "$([math]::Floor($p.R/32)),$([math]::Floor($p.G/32)),$([math]::Floor($p.B/32))"
            if ($hist.ContainsKey($key)) { $hist[$key]++ } else { $hist[$key] = 1 }
        }
    }
    $bmp.Dispose()
    if ($count -eq 0) { Write-Output "$(Split-Path $path -Leaf): no opaque pixels"; return }

    $avg = "#{0:X2}{1:X2}{2:X2}" -f [int]($sumR/$count), [int]($sumG/$count), [int]($sumB/$count)

    # NOTE: rough categories. Use CheckPalette.ps1 for compliance checks.
    $cat = @{ 'white' = 0; 'gray' = 0; 'dark' = 0; 'warm' = 0; 'cool' = 0; 'purple' = 0; 'green' = 0 }
    foreach ($kv in $hist.GetEnumerator()) {
        $parts = $kv.Key -split ','
        $r = [int]$parts[0]; $g = [int]$parts[1]; $b = [int]$parts[2]
        $mx = [math]::Max($r, [math]::Max($g, $b)); $mn = [math]::Min($r, [math]::Min($g, $b))
        $d = $mx - $mn
        $v = ($mx + 0.5) / 8.0
        if ($v -lt 0.25) { $cat['dark'] += $kv.Value; continue }
        if ($d -le 0) {
            if ($v -ge 0.55) { $cat['white'] += $kv.Value } else { $cat['gray'] += $kv.Value }
            continue
        }
        if ($r -ge $g -and $g -ge $b) { $cat['warm'] += $kv.Value }
        elseif ($b -gt $r) {
            if ($r -gt $g) { $cat['purple'] += $kv.Value } else { $cat['cool'] += $kv.Value }
        }
        elseif ($g -gt $r) { $cat['green'] += $kv.Value }
        else { $cat['warm'] += $kv.Value }
    }
    $top = $hist.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 5 | ForEach-Object {
        $p = $_.Key -split ','
        $rr = [int]$p[0]*32+16; $gg = [int]$p[1]*32+16; $bb = [int]$p[2]*32+16
        "#{0:X2}{1:X2}{2:X2}({3}%)" -f $rr, $gg, $bb, [math]::Round(100*$_.Value/$count,1)
    }
    $catStr = ($cat.GetEnumerator() | Sort-Object Value -Descending | Where-Object { $_.Value -gt 0 } | ForEach-Object {
        "$($_.Key) $([math]::Round(100*$_.Value/$count,1))%"
    }) -join ' | '

    Write-Output "=== $(Split-Path $path -Leaf)  [$w x $h]"
    Write-Output "    avg : $avg"
    Write-Output "    cats: $catStr"
    Write-Output "    top : $($top -join '  ')"
}

foreach ($p in $Paths) {
    if (Test-Path $p) { Get-ColorProfile (Resolve-Path $p).Path } else { Write-Output "NOT FOUND: $p" }
}
