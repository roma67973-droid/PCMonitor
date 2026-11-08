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

    # Плитка: плоская, тёмно-серая, тонкая рамка
    $pad = if ($small) { 0.5 } else { 8 * $k }
    $rect = New-Object Windows.Rect $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $rad = $s * 0.22
    $bg = New-Object Windows.Media.SolidColorBrush (C '#1D1D1D')
    $border = New-Object Windows.Media.SolidColorBrush (C '#2F2F2F')
    $bw = [Math]::Max(1, 4 * $k)
    $dc.DrawRoundedRectangle($bg, (New-Object Windows.Media.Pen $border, $bw), $rect, $rad, $rad)

    # Кольцо-индикатор (270°): серая дорожка и дуга цвета акцента
    $cx = $s / 2; $cy = $s * 0.53; $r = $s * 0.28
    $th = if ($small) { $s * 0.13 } else { $s * 0.10 }
    $track = New-Object Windows.Media.Pen (New-Object Windows.Media.SolidColorBrush (C '#2B2B2B')), $th
    $track.StartLineCap = 'Round'; $track.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $track, (Arc $cx $cy $r 135 270))
    $pen = New-Object Windows.Media.Pen (New-Object Windows.Media.SolidColorBrush (C '#FF8A3D')), $th
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $pen, (Arc $cx $cy $r 135 200))

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
