# Enemy palette compliance checker (rules R1 / R3).
# R1: white/light-gray share <= 25% (player baseline 32.8%) AND not the largest category.
# R3: high-saturation yellow / red must not appear on a body (FX / BOSS only).
# Categories: white(neutral bright) / gray(neutral mid) / dark(low value) / warm / green / cool / purple
# Usage: powershell -File CheckPalette.ps1 <png> [<png> ...]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Paths)

Add-Type -AssemblyName System.Drawing

function Get-Hue([int]$r, [int]$g, [int]$b) {
    $mx = [math]::Max($r, [math]::Max($g, $b)); $mn = [math]::Min($r, [math]::Min($g, $b))
    $d = $mx - $mn
    if ($d -eq 0) { return -1 }
    if ($mx -eq $r) { $h = 60.0 * ((($g - $b) / [double]$d) % 6) }
    elseif ($mx -eq $g) { $h = 60.0 * ((($b - $r) / [double]$d) + 2) }
    else { $h = 60.0 * ((($r - $g) / [double]$d) + 4) }
    if ($h -lt 0) { $h += 360 }
    return $h
}

function Check([string]$path, [int]$step = 2) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    $w = $bmp.Width; $h = $bmp.Height
    $white = 0; $gray = 0; $dark = 0; $warm = 0; $green = 0; $cool = 0; $purple = 0
    $warnYellow = 0; $warnRed = 0; $count = 0
    $hueHist = @{}
    for ($y = 0; $y -lt $h; $y += $step) {
        for ($x = 0; $x -lt $w; $x += $step) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.A -lt 128) { continue }
            if ($p.R -gt 200 -and $p.B -gt 200 -and $p.G -lt 80) { continue }  # magenta key
            $count++
            $r = $p.R; $g = $p.G; $b = $p.B
            $mx = [math]::Max($r, [math]::Max($g, $b)); $mn = [math]::Min($r, [math]::Min($g, $b))
            $d = $mx - $mn
            $v = $mx / 255.0
            $s = if ($mx -eq 0) { 0 } else { $d / [double]$mx }
            if ($s -ge 0.8 -and $v -ge 0.8 -and $g -gt 180 -and $b -lt 100) { $warnYellow++ }
            if ($s -ge 0.8 -and $v -ge 0.8 -and $g -lt 90 -and $b -lt 90) { $warnRed++ }

            if ($v -lt 0.25) { $dark++; continue }
            if ($d -le 24) {
                if ($v -ge 0.55) { $white++ } else { $gray++ }
                continue
            }
            $hue = Get-Hue $r $g $b
            $bin = [int]($hue / 15)
            if ($hueHist.ContainsKey($bin)) { $hueHist[$bin]++ } else { $hueHist[$bin] = 1 }
            if ($hue -ge 15 -and $hue -lt 75) { $warm++ }
            elseif ($hue -ge 75 -and $hue -lt 165) { $green++ }
            elseif ($hue -ge 165 -and $hue -lt 260) { $cool++ }
            elseif ($hue -ge 260 -and $hue -lt 345) { $purple++ }
            else { $warm++ }
        }
    }
    $bmp.Dispose()
    if ($count -eq 0) { Write-Output "$(Split-Path $path -Leaf): no opaque pixels"; return }

    $cats = [ordered]@{ 'white' = $white; 'gray' = $gray; 'dark' = $dark; 'warm' = $warm; 'green' = $green; 'cool' = $cool; 'purple' = $purple }
    $maxCat = ($cats.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key
    $whitePct = [math]::Round(100.0 * $white / $count, 1)
    $r1 = ($whitePct -le 25 -and $maxCat -ne 'white')
    $domHue = -1
    if ($hueHist.Count -gt 0) { $domHue = ([int](($hueHist.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key) * 15 + 7) }

    $catStr = ($cats.GetEnumerator() | Sort-Object Value -Descending | Where-Object { $_.Value -gt 0 } | ForEach-Object { "$($_.Key) $([math]::Round(100.0 * $_.Value / $count,1))%" }) -join ' | '

    # body-only composition: exclude near-black (V<0.25) so the black outline does not skew it
    $body = $count - $dark
    $bodyStr = 'n/a'
    if ($body -gt 0) {
        $bodyStr = ($cats.GetEnumerator() | Where-Object { $_.Key -ne 'dark' -and $_.Value -gt 0 } | Sort-Object Value -Descending | ForEach-Object { "$($_.Key) $([math]::Round(100.0 * $_.Value / $body,1))%" }) -join ' | '
    }

    Write-Output "=== $(Split-Path $path -Leaf)  [$w x $h]  pixels=$count"
    Write-Output ("    categories : {0}" -f $catStr)
    Write-Output ("    body(no blk): {0}" -f $bodyStr)
    Write-Output ("    dominant   : {0}" -f $maxCat)
    Write-Output ("    dom hue    : {0}" -f $(if ($domHue -lt 0) { "n/a" } else { "$domHue deg" }))
    Write-Output ("    R1 white   : {0}%  (<=25% and not dominant) -> {1}" -f $whitePct, $(if ($r1) { "PASS" } else { "FAIL" }))
    Write-Output ("    R3 warn yel: {0} px{1}" -f $warnYellow, $(if ($warnYellow -gt 0) { "  <-- body must not use it" } else { "" }))
    Write-Output ("    R3 warn red: {0} px{1}" -f $warnRed, $(if ($warnRed -gt 0) { "  <-- body must not use it" } else { "" }))
}

foreach ($p in $Paths) {
    if (Test-Path $p) { Check (Resolve-Path $p).Path } else { Write-Output "NOT FOUND: $p" }
}
