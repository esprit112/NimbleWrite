# Draws the NimbleWrite mark (same geometry as the web favicon) at several sizes and packs them into nimblewrite.ico.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File desktop/tools/make-icon.ps1
param([string]$Out = (Join-Path $PSScriptRoot '..\nimblewrite.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$px) {
  $bmp = New-Object System.Drawing.Bitmap $px, $px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  $s = $px / 64.0
  $d = 32 * $s
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90)
  $path.AddArc($px - $d, 0, $d, $d, 270, 90)
  $path.AddArc($px - $d, $px - $d, $d, $d, 0, 90)
  $path.AddArc(0, $px - $d, $d, $d, 90, 90)
  $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(11, 107, 58))), $path)
  $coords = @(18, 44, 18, 20, 24, 20, 40, 36, 40, 20, 46, 20, 46, 44, 40, 44, 24, 28, 24, 44)
  $pts = New-Object 'System.Drawing.PointF[]' 10
  for ($i = 0; $i -lt 10; $i++) { $pts[$i] = New-Object System.Drawing.PointF ([single]($coords[2 * $i] * $s)), ([single]($coords[2 * $i + 1] * $s)) }
  $g.FillPolygon([System.Drawing.Brushes]::White, $pts)
  $r = 5 * $s
  $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(118, 185, 0))), [single](50 * $s - $r), [single](46 * $s - $r), [single](2 * $r), [single](2 * $r))
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  return , $ms.ToArray()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($px in $sizes) { $frames += , (New-Frame $px) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out)).Path + '\' + (Split-Path $Out -Leaf))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $px = $sizes[$i]; $data = $frames[$i]
  $dim = if ($px -ge 256) { 0 } else { $px }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
  $offset += $data.Length
}
foreach ($data in $frames) { $w.Write([byte[]]$data) }
$w.Close()
"wrote $Out ($($sizes.Count) sizes, $offset bytes)"
