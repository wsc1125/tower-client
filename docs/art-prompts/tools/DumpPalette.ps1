# Dump the distinct quantized colors of a PNG with share %, sorted desc.
# Usage: powershell -File DumpPalette.ps1 <png> [topN]
param([string]$Path, [int]$Top = 24)

Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Path).Path)
$w = $bmp.Width; $h = $bmp.Height
$hist = @{}
$count = 0
for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $p = $bmp.GetPixel($x, $y)
        if ($p.A -lt 128) { continue }
        if ($p.R -gt 200 -and $p.B -gt 200 -and $p.G -lt 80) { continue }
        $count++
        $key = "$([int]($p.R/16)),$([int]($p.G/16)),$([int]($p.B/16))"
        if ($hist.ContainsKey($key)) { $hist[$key]++ } else { $hist[$key] = 1 }
    }
}
$bmp.Dispose()
Write-Output "$(Split-Path $Path -Leaf)  [$w x $h]  opaque=$count"
$hist.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First $Top | ForEach-Object {
    $p = $_.Key -split ','
    $r = [int]$p[0]*16+8; $g = [int]$p[1]*16+8; $b = [int]$p[2]*16+8
    $mx = [math]::Max($r, [math]::Max($g, $b)); $mn = [math]::Min($r, [math]::Min($g, $b))
    $d = $mx - $mn; $v = $mx / 255.0
    $tag = if ($v -lt 0.25) { 'dark' } elseif ($d -le 24) { if ($v -ge 0.55) { 'white' } else { 'gray' } } else { 'color' }
    "{0}  {1,5}%   #{2:X2}{3:X2}{4:X2}  ({5})" -f ('{0:X2}{1:X2}{2:X2}' -f $r, $g, $b), [math]::Round(100.0 * $_.Value / $count, 1), $r, $g, $b, $tag
}
