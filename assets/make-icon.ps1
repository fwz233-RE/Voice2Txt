# make-icon.ps1 (v2) - Material "mic" FILL glyph (official SVG, parsed to vector)
# on a Material You gradient rounded tile. Multi-size .ico output.
Add-Type -AssemblyName System.Drawing

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$svgPath = Join-Path $dir 'mic_fill1.svg'

# ---------- tiny SVG path parser (MmLlHhVvCcSsQqTtZz) ----------
function Convert-PathToGP([string]$d, [float]$scale, [float]$offX, [float]$offY) {
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.FillMode = 'Alternate'
    $tokens = @()
    foreach ($m in [regex]::Matches($d, '[MmLlHhVvCcSsQqTtZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?')) { $tokens += $m.Value }
    $i = 0
    $cx = 0.0; $cy = 0.0; $sx = 0.0; $sy = 0.0
    $lcx = 0.0; $lcy = 0.0          # last control point (for S/T)
    $lastCmd = ''
    function N([ref]$r) { $v = [double]$tokens[$r.Value]; $r.Value++; return $v }
    function PT([double]$x, [double]$y) {
        return New-Object System.Drawing.PointF(($x * $scale + $offX), (($y + 960.0) * $scale + $offY))
    }
    while ($i -lt $tokens.Count) {
        $t = $tokens[$i]
        if ($t -match '^[A-Za-z]') { $cmd = $t; $i++ } else { $cmd = $(if ($lastCmd -eq '') { 'M' } else { $lastCmd }) }
        switch -Regex ($cmd) {
            '^[Mm]$' {
                $x = N ([ref]$i); $y = N ([ref]$i)
                if ($cmd -ceq 'm') { $x += $cx; $y += $cy }
                $cx = $x; $cy = $y; $sx = $x; $sy = $y
                $gp.StartFigure()
                $lastCmd = $(if ($cmd -eq 'M') { 'L' } else { 'l' })
            }
            '^[Ll]$' {
                $x = N ([ref]$i); $y = N ([ref]$i)
                if ($cmd -ceq 'l') { $x += $cx; $y += $cy }
                $gp.AddLine((PT $cx $cy), (PT $x $y))
                $cx = $x; $cy = $y
                $lastCmd = $cmd
            }
            '^[Hh]$' {
                $x = N ([ref]$i)
                if ($cmd -ceq 'h') { $x += $cx }
                $gp.AddLine((PT $cx $cy), (PT $x $cy))
                $cx = $x
                $lastCmd = $cmd
            }
            '^[Vv]$' {
                $y = N ([ref]$i)
                if ($cmd -ceq 'v') { $y += $cy }
                $gp.AddLine((PT $cx $cy), (PT $cx $y))
                $cy = $y
                $lastCmd = $cmd
            }
            '^[Cc]$' {
                $x1 = N ([ref]$i); $y1 = N ([ref]$i); $x2 = N ([ref]$i); $y2 = N ([ref]$i); $x = N ([ref]$i); $y = N ([ref]$i)
                if ($cmd -ceq 'c') { $x1 += $cx; $y1 += $cy; $x2 += $cx; $y2 += $cy; $x += $cx; $y += $cy }
                $gp.AddBezier((PT $cx $cy), (PT $x1 $y1), (PT $x2 $y2), (PT $x $y))
                $lcx = $x2; $lcy = $y2; $cx = $x; $cy = $y
                $lastCmd = $cmd
            }
            '^[Ss]$' {
                $x2 = N ([ref]$i); $y2 = N ([ref]$i); $x = N ([ref]$i); $y = N ([ref]$i)
                if ($cmd -ceq 's') { $x2 += $cx; $y2 += $cy; $x += $cx; $y += $cy }
                if ($lastCmd -match '^[CcSs]$') { $x1 = 2 * $cx - $lcx; $y1 = 2 * $cy - $lcy } else { $x1 = $cx; $y1 = $cy }
                $gp.AddBezier((PT $cx $cy), (PT $x1 $y1), (PT $x2 $y2), (PT $x $y))
                $lcx = $x2; $lcy = $y2; $cx = $x; $cy = $y
                $lastCmd = $cmd
            }
            '^[Qq]$' {
                $qx = N ([ref]$i); $qy = N ([ref]$i); $x = N ([ref]$i); $y = N ([ref]$i)
                if ($cmd -ceq 'q') { $qx += $cx; $qy += $cy; $x += $cx; $y += $cy }
                $x1 = $cx + 2.0 / 3.0 * ($qx - $cx); $y1 = $cy + 2.0 / 3.0 * ($qy - $cy)
                $x2 = $x + 2.0 / 3.0 * ($qx - $x);   $y2 = $y + 2.0 / 3.0 * ($qy - $y)
                $gp.AddBezier((PT $cx $cy), (PT $x1 $y1), (PT $x2 $y2), (PT $x $y))
                $lcx = $qx; $lcy = $qy; $cx = $x; $cy = $y
                $lastCmd = $cmd
            }
            '^[Tt]$' {
                $x = N ([ref]$i); $y = N ([ref]$i)
                if ($cmd -ceq 't') { $x += $cx; $y += $cy }
                if ($lastCmd -match '^[QqTt]$') { $qx = 2 * $cx - $lcx; $qy = 2 * $cy - $lcy } else { $qx = $cx; $qy = $cy }
                $x1 = $cx + 2.0 / 3.0 * ($qx - $cx); $y1 = $cy + 2.0 / 3.0 * ($qy - $cy)
                $x2 = $x + 2.0 / 3.0 * ($qx - $x);   $y2 = $y + 2.0 / 3.0 * ($qy - $y)
                $gp.AddBezier((PT $cx $cy), (PT $x1 $y1), (PT $x2 $y2), (PT $x $y))
                $lcx = $qx; $lcy = $qy; $cx = $x; $cy = $y
                $lastCmd = $cmd
            }
            '^[Zz]$' {
                $gp.AddLine((PT $cx $cy), (PT $sx $sy))
                $gp.CloseFigure()
                $cx = $sx; $cy = $sy
                $lastCmd = $cmd
            }
        }
    }
    return $gp
}

function New-RoundedRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# ---------- design: gradient tile + solid white mic ----------
function New-DesignBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # Material tile: solid color, generous corner radius
    $tile = New-RoundedRect (4 * $s) (4 * $s) (248 * $s) (248 * $s) (58 * $s)
    $solid = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#1E88E5'))
    $g.FillPath($solid, $tile)

    # glyph from official fill SVG, scaled to its INK bounds (as large as possible)
    $svg = [xml](Get-Content -Raw $svgPath)
    $d = $svg.svg.path.d
    $probe = Convert-PathToGP $d 1.0 0.0 0.0
    $b = $probe.GetBounds()
    $target = 207.4 * $s   # ink height = 81% of canvas
    $scale = $target / $b.Height
    if ($b.Width * $scale -gt 224.0 * $s) { $scale = 224.0 * $s / $b.Width }
    $offX = (256.0 * $s - $b.Width * $scale) / 2.0 - $b.X * $scale
    $offY = (256.0 * $s - $b.Height * $scale) / 2.0 - $b.Y * $scale
    $gp = Convert-PathToGP $d $scale $offX $offY
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $gp)

    $g.Dispose()
    return $bmp
}

# ---------- render sizes ----------
$master = New-DesignBitmap 256
$master.Save((Join-Path $dir 'icon_256.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$bmpSizes = @(64, 48, 32, 16)
$images = @{}
$ms = New-Object System.IO.MemoryStream
$master.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$images[256] = $ms.ToArray()
foreach ($sz in $bmpSizes) {
    $small = New-Object System.Drawing.Bitmap($sz, $sz)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'HighQuality'
    $g.PixelOffsetMode = 'HighQuality'
    $g.DrawImage($master, 0, 0, $sz, $sz)
    $g.Dispose()
    $images[$sz] = $small
}

# ---------- ICO packer ----------
function Get-BmpEntry($bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $rowBytes = [int][Math]::Ceiling($w / 32.0) * 4
    $zero = New-Object byte[] $rowBytes
    for ($y = 0; $y -lt $h; $y++) { $bw.Write($zero) }
    $bw.Flush()
    return ,$ms.ToArray()
}

$allSizes = @(256, 64, 48, 32, 16)
$payloads = @{}
foreach ($sz in $allSizes) {
    if ($sz -eq 256) { $payloads[$sz] = $images[256] }
    else { $payloads[$sz] = Get-BmpEntry $images[$sz] }
}

$icoPath = Join-Path $dir 'icon.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$allSizes.Count)
$offset = 6 + 16 * $allSizes.Count
foreach ($sz in $allSizes) {
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$payloads[$sz].Length)
    $bw.Write([int]$offset)
    $offset += $payloads[$sz].Length
}
foreach ($sz in $allSizes) { $bw.Write($payloads[$sz]) }
$bw.Close(); $fs.Dispose()
Write-Host "wrote $icoPath ($([Math]::Round((Get-Item $icoPath).Length / 1KB)) KB)"
