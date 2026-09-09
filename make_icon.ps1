# Genera clock.ico (multi-resolución, PNG dentro de ICO) con un reloj estilo BetaClock.
Add-Type -AssemblyName System.Drawing
$dir = Split-Path -Parent $MyInvocation.MyCommand.Definition

function New-ClockPng([int]$sz) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Cuerpo: tile redondeado oscuro
    $m = [Math]::Max(1, [int]($sz * 0.045))
    $rad = [int]($sz * 0.22)
    $d = $rad * 2
    $w = $sz - 2 * $m
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $d, $d, 180, 90)
    $path.AddArc($sz - $m - $d, $m, $d, $d, 270, 90)
    $path.AddArc($sz - $m - $d, $sz - $m - $d, $d, $d, 0, 90)
    $path.AddArc($m, $sz - $m - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 18, 24))
    $g.FillPath($bg, $path)
    if ($sz -ge 32) {
        $bpen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 60, 60, 74), [float]([Math]::Max(1, $sz * 0.012)))
        $g.DrawPath($bpen, $path)
    }

    $cx = $sz / 2.0; $cy = $sz / 2.0
    $cr = $sz * 0.33

    # Anillo del reloj (cian neón)
    $ringW = [float]([Math]::Max(2, $sz * 0.05))
    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 230, 230), $ringW)
    $g.DrawEllipse($ring, [float]($cx - $cr), [float]($cy - $cr), [float]($cr * 2), [float]($cr * 2))

    # Marcas 12/3/6/9 (RGB sutil)
    if ($sz -ge 24) {
        $tickCols = @(
            [System.Drawing.Color]::FromArgb(255, 255, 80, 80),
            [System.Drawing.Color]::FromArgb(255, 80, 200, 255),
            [System.Drawing.Color]::FromArgb(255, 120, 255, 120),
            [System.Drawing.Color]::FromArgb(255, 255, 210, 60))
        $angs = @(0, 90, 180, 270)
        for ($k = 0; $k -lt 4; $k++) {
            $a = $angs[$k] * [Math]::PI / 180.0
            $sx = $cx + [Math]::Sin($a) * $cr * 0.82
            $sy = $cy - [Math]::Cos($a) * $cr * 0.82
            $ex = $cx + [Math]::Sin($a) * $cr * 0.97
            $ey = $cy - [Math]::Cos($a) * $cr * 0.97
            $tp = New-Object System.Drawing.Pen ($tickCols[$k], [float]([Math]::Max(1.5, $sz * 0.03)))
            $tp.StartCap = 'Round'; $tp.EndCap = 'Round'
            $g.DrawLine($tp, [float]$sx, [float]$sy, [float]$ex, [float]$ey)
        }
    }

    # Manecillas (10:10)
    function Hand($angDeg, $len, $width, $color) {
        $a = $angDeg * [Math]::PI / 180.0
        $ex = $cx + [Math]::Sin($a) * $cr * $len
        $ey = $cy - [Math]::Cos($a) * $cr * $len
        $p = New-Object System.Drawing.Pen ($color, [float]$width)
        $p.StartCap = 'Round'; $p.EndCap = 'Round'
        $g.DrawLine($p, [float]$cx, [float]$cy, [float]$ex, [float]$ey)
    }
    Hand 300 0.50 ([Math]::Max(2, $sz * 0.055)) ([System.Drawing.Color]::FromArgb(255, 255, 150, 40))  # hora -> ~10
    Hand 60  0.78 ([Math]::Max(1.5, $sz * 0.04)) ([System.Drawing.Color]::FromArgb(255, 240, 240, 245)) # minutos -> ~2

    # Punto central
    $cdr = [Math]::Max(1.5, $sz * 0.05)
    $cdot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 240, 245))
    $g.FillEllipse($cdot, [float]($cx - $cdr), [float]($cy - $cdr), [float]($cdr * 2), [float]($cdr * 2))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = @(256, 64, 48, 32, 16)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = New-ClockPng $s }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $wh = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$wh); $bw.Write([Byte]$wh)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $dir "clock.ico"), $out.ToArray())
"clock.ico generado ($([math]::Round((Get-Item (Join-Path $dir 'clock.ico')).Length/1KB,1)) KB)"
