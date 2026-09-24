# make-candidates.ps1 - render Material glyph icon candidates on a Material tile
# Run: powershell -File make-candidates.ps1   (or pwsh)
Add-Type -AssemblyName System.Drawing

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$fontFile = Join-Path $dir 'MaterialSymbolsRounded.ttf'

$fc = New-Object System.Drawing.Text.PrivateFontCollection
$fc.AddFontFile($fontFile)
$family = New-Object System.Drawing.FontFamily('Material Symbols Rounded', $fc)

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

function New-IconPng([int]$codepoint, [double]$stroke, [string]$bgColor, [string]$outPath) {
    $size = 256
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Material tile: rounded square, Blue 600
    $tile = New-RoundedRect 8 8 ($size - 16) ($size - 16) 56
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($bgColor))
    $g.FillPath($brush, $tile)

    # glyph as GraphicsPath so we can fatten the outline to look like FILL
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $text = [string][char]$codepoint
    $emSize = 132.0
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $layout = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $gp.AddString($text, $family, 0, $emSize, $layout, $fmt)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, $gp)
    if ($stroke -gt 0) {
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
        $pen.LineJoin = 'Round'
        $g.DrawPath($pen, $gp)
    }
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "wrote $outPath"
}

$blue = '#1E88E5'
New-IconPng 0xE31D 0    $blue (Join-Path $dir 'cand_mic_outline.png')
New-IconPng 0xE31D 9    $blue (Join-Path $dir 'cand_mic_fill.png')
New-IconPng 0xF8EC 0    $blue (Join-Path $dir 'cand_transcribe_outline.png')
New-IconPng 0xF8EC 9    $blue (Join-Path $dir 'cand_transcribe_fill.png')
New-IconPng 0xE1B8 9    $blue (Join-Path $dir 'cand_graphiceq_fill.png')
