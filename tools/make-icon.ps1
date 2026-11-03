# Рисует иконку приложения через WPF и собирает многоразмерный .ico
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\PCMonitor\Assets\app.ico'
$previewDir = Join-Path $root 'obj'

function C($hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function P($x, $y) { New-Object Windows.Point $x, $y }

function Arc($cx, $cy, $r, $startDeg, $sweepDeg) {
    $a1 = $startDeg * [Math]::PI / 180; $a2 = ($startDeg + $sweepDeg) * [Math]::PI / 180
    $g = New-Object Windows.Media.StreamGeometry
    $ctx = $g.Open()
    $ctx.BeginFigure((P ($cx + $r * [Math]::Cos($a1)) ($cy + $r * [Math]::Sin($a1))), $false, $false)
    $ctx.ArcTo((P ($cx + $r * [Math]::Cos($a2)) ($cy + $r * [Math]::Sin($a2))), (New-Object Windows.Size $r, $r), 0, ($sweepDeg -gt 180), [Windows.Media.SweepDirection]::Clockwise, $true, $false)
    $ctx.Close(); $g
}

function Render([int]$s) {
    $small = $s -le 32
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $k = $s / 256.0

    # Плитка: тёмный градиент + тонкая градиентная рамка
    $pad = if ($small) { 0.5 } else { 8 * $k }
    $rect = New-Object Windows.Rect $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $rad = $s * 0.235
    $bg = New-Object Windows.Media.LinearGradientBrush (C '#232A4D'), (C '#0A0D1A'), (P 0 0), (P 0.6 1)
    $border = New-Object Windows.Media.LinearGradientBrush (C '#7FE7FF'), (C '#9B6BFF'), (P 0 0), (P 1 1)
    $bw = [Math]::Max(1, 5 * $k)
    $dc.DrawRoundedRectangle($bg, (New-Object Windows.Media.Pen $border, $bw), $rect, $rad, $rad)

    # Мягкое свечение в центре
    $glow = New-Object Windows.Media.RadialGradientBrush (C '#5500C8FF'), (C '#0000C8FF')
    $dc.DrawEllipse($glow, $null, (P ($s / 2) ($s * 0.52)), ($s * 0.42), ($s * 0.42))

    # Блик сверху
    if (-not $small) {
        $hl = New-Object Windows.Media.LinearGradientBrush (C '#22FFFFFF'), (C '#00FFFFFF'), 90
        $dc.DrawRoundedRectangle($hl, $null, (New-Object Windows.Rect ($pad + $bw), ($pad + $bw), ($s - 2 * $pad - 2 * $bw), ($s * 0.42)), ($rad * 0.9), ($rad * 0.9))
    }

    # Кольцо-индикатор (270°) с градиентом
    $cx = $s / 2; $cy = $s * 0.53; $r = $s * 0.30
    $th = if ($small) { $s * 0.12 } else { $s * 0.085 }
    $track = New-Object Windows.Media.Pen (New-Object Windows.Media.SolidColorBrush (C '#33FFFFFF')), $th
    $track.StartLineCap = 'Round'; $track.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $track, (Arc $cx $cy $r 135 270))
    $grad = New-Object Windows.Media.LinearGradientBrush
    $grad.StartPoint = P 0 1; $grad.EndPoint = P 1 0
    $grad.GradientStops.Add((New-Object Windows.Media.GradientStop (C '#00D2FF'), 0))
    $grad.GradientStops.Add((New-Object Windows.Media.GradientStop (C '#7C4DFF'), 0.55))
    $grad.GradientStops.Add((New-Object Windows.Media.GradientStop (C '#FF3EA5'), 1))
    $pen = New-Object Windows.Media.Pen $grad, $th
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'
    $sweep = 200
    $dc.DrawGeometry($null, $pen, (Arc $cx $cy $r 135 $sweep))

    # Светящаяся точка на конце дуги
    $ea = (135 + $sweep) * [Math]::PI / 180
    $end = P ($cx + $r * [Math]::Cos($ea)) ($cy + $r * [Math]::Sin($ea))
    if (-not $small) {
        $halo = New-Object Windows.Media.RadialGradientBrush (C '#AAFF3EA5'), (C '#00FF3EA5')
        $dc.DrawEllipse($halo, $null, $end, ($th * 1.5), ($th * 1.5))
        $dc.DrawEllipse([Windows.Media.Brushes]::White, $null, $end, ($th * 0.28), ($th * 0.28))
    }

    # Пульс внутри кольца
    $lw = if ($small) { [Math]::Max(1.6, $s * 0.085) } else { $s * 0.05 }
    $pts = @(@(-0.17, 0), @(-0.07, 0), @(-0.02, -0.13), @(0.05, 0.12), @(0.10, -0.04), @(0.17, -0.04))
    $pl = New-Object Windows.Media.StreamGeometry
    $ctx = $pl.Open()
    $first = $true
    foreach ($p in $pts) {
        $pt = P ($cx + $p[0] * $s) ($cy + $p[1] * $s)
        if ($first) { $ctx.BeginFigure($pt, $false, $false); $first = $false } else { $ctx.LineTo($pt, $true, $true) }
    }
    $ctx.Close()
    if (-not $small) {
        $gp = New-Object Windows.Media.Pen (New-Object Windows.Media.SolidColorBrush (C '#3300E5FF')), ($lw * 2.1)
        $gp.LineJoin = 'Round'; $gp.StartLineCap = 'Round'; $gp.EndLineCap = 'Round'
        $dc.DrawGeometry($null, $gp, $pl)
    }
    $wp = New-Object Windows.Media.Pen ([Windows.Media.Brushes]::White), $lw
    $wp.LineJoin = 'Round'; $wp.StartLineCap = 'Round'; $wp.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $wp, $pl)

    $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $s, $s, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    ,$ms.ToArray()
}

$sizes = 256, 128, 64, 48, 40, 32, 24, 20, 16
$frames = foreach ($s in $sizes) { ,@($s, (Render $s)) }

$o = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $o
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$off = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $d = if ($f[0] -ge 256) { 0 } else { $f[0] }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f[1].Length); $w.Write([uint32]$off)
    $off += $f[1].Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
[IO.File]::WriteAllBytes($out, $o.ToArray())

# Превью: все размеры на одной полосе
foreach ($f in $frames) { if ($f[0] -in 256, 32, 16) { [IO.File]::WriteAllBytes((Join-Path $previewDir "icon$($f[0]).png"), $f[1]) } }
"ok $((Get-Item $out).Length) bytes"
