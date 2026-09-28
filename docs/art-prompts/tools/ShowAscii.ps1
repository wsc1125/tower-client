# ASCII preview of a PNG: one char per cell, by average luminance of opaque pixels.
# '.' = transparent, ' '=dark ... '@'=bright
# Usage: powershell -File ShowAscii.ps1 <png> [cols] [rows]
param([string]$Path, [int]$Cols = 48, [int]$Rows = 24)

Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Path).Path)
$w = $bmp.Width; $h = $bmp.Height
$ramp = ' .:-=+*#%@'
Write-Output "$(Split-Path $Path -Leaf)  [$w x $h]"
for ($ry = 0; $ry -lt $Rows; $ry++) {
    $line = ''
    for ($rx = 0; $rx -lt $Cols; $rx++) {
        $x0 = [int]($rx * $w / $Cols); $x1 = [int](($rx + 1) * $w / $Cols)
        $y0 = [int]($ry * $h / $Rows); $y1 = [int](($ry + 1) * $h / $Rows)
        $sum = 0.0; $n = 0; $opaque = 0; $total = 0
        for ($y = $y0; $y -lt $y1; $y += 2) {
            for ($x = $x0; $x -lt $x1; $x += 2) {
                $total++
                $p = $bmp.GetPixel($x, $y)
                if ($p.A -lt 128) { continue }
                if ($p.R -gt 200 -and $p.B -gt 200 -and $p.G -lt 80) { continue }
                $opaque++
                $sum += (0.299 * $p.R + 0.587 * $p.G + 0.114 * $p.B)
                $n++
            }
        }
        if ($n -eq 0) { $line += '.'; continue }
        $lum = $sum / $n
        $idx = [int]([math]::Round($lum / 255.0 * ($ramp.Length - 1)))
        $line += $ramp[$idx]
    }
    Write-Output $line
}
$bmp.Dispose()
