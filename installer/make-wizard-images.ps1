# Draws the installer's wizard images (24-bit BMP, several DPI sizes) from the NimbleWrite mark: green rounded square,
# white N, NVIDIA-green dot. Run once; the build script uses the files in installer\assets.
#   powershell -NoProfile -ExecutionPolicy Bypass -File installer\make-wizard-images.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Hex([string]$h, [int]$a = 255) {
  $c = [System.Drawing.ColorTranslator]::FromHtml($h)
  [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B)
}

function RoundedRect([single]$x, [single]$y, [single]$size, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $size - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $size - $d, $y + $size - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $size - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  $p
}

# The mark on a 64-unit grid (same geometry as the tray icon and favicon).
function DrawMark($g, [single]$x, [single]$y, [single]$size, $square, $letter) {
  $s = $size / 64
  $path = RoundedRect $x $y $size (16 * $s)
  $g.FillPath((New-Object System.Drawing.SolidBrush $square), $path)
  $pts = @(@(18,44),@(18,20),@(24,20),@(40,36),@(40,20),@(46,20),@(46,44),@(40,44),@(24,28),@(24,44)) | ForEach-Object {
    New-Object System.Drawing.PointF ($x + $_[0] * $s), ($y + $_[1] * $s)
  }
  $g.FillPolygon((New-Object System.Drawing.SolidBrush $letter), [System.Drawing.PointF[]]$pts)
  $g.FillEllipse((New-Object System.Drawing.SolidBrush (Hex '#76B900')), ($x + 45 * $s), ($y + 41 * $s), (10 * $s), (10 * $s))
}

function NewCanvas([int]$w, [int]$h) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.PixelOffsetMode = 'HighQuality'
  $g.InterpolationMode = 'HighQualityBicubic'
  $g.TextRenderingHint = 'AntiAliasGridFit'
  @($bmp, $g)
}

function CenteredText($g, [string]$text, [string]$family, [single]$px, $style, $color, [int]$w, [single]$y) {
  $font = New-Object System.Drawing.Font $family, $px, $style, ([System.Drawing.GraphicsUnit]::Pixel)
  $size = $g.MeasureString($text, $font)
  $g.DrawString($text, $font, (New-Object System.Drawing.SolidBrush $color), (($w - $size.Width) / 2), $y)
  $font.Dispose()
  $size.Height
}

# Welcome/finish sidebar (aspect 164:314). Sizes are Inno Setup 6.6+'s image areas at 100-250% DPI; Setup picks
# the closest one for the screen.
foreach ($size in @(@(202,386,'100'), @(269,515,'125'), @(336,643,'150'), @(403,772,'175'), @(430,824,'200'), @(498,953,'225'), @(534,1022,'250'))) {
  $w = $size[0]; $h = $size[1]
  $bmp, $g = NewCanvas $w $h
  $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Hex '#0E7D45'), (Hex '#063F22'), 90
  $g.FillRectangle($grad, $rect)
  # A soft glow behind the mark.
  $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
  $glow.AddEllipse([single](-0.35 * $w), [single](0.02 * $h), [single](1.7 * $w), [single](0.62 * $h))
  $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
  $pgb.CenterColor = Hex '#FFFFFF' 34
  $pgb.SurroundColors = @(Hex '#FFFFFF' 0)
  $g.FillPath($pgb, $glow)
  $mark = [single](0.44 * $w)
  $top = [single](0.2 * $h)
  DrawMark $g (($w - $mark) / 2) $top $mark (Hex '#FFFFFF') (Hex '#0B6B3A')
  $y = $top + $mark + 0.06 * $h
  $th = CenteredText $g 'NimbleWrite' 'Segoe UI Semibold' ([single](0.135 * $w)) ([System.Drawing.FontStyle]::Regular) (Hex '#FFFFFF') $w $y
  $null = CenteredText $g 'Write clearly, everywhere.' 'Segoe UI' ([single](0.073 * $w)) ([System.Drawing.FontStyle]::Regular) (Hex '#FFFFFF' 205) $w ($y + $th + 0.004 * $h)
  $file = Join-Path $out "wizard-large-$($size[2]).bmp"
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
  $g.Dispose(); $bmp.Dispose()
  "wrote $file ($w x $h)"
}

# Header icon on the inner pages (square), on the wizard's white header.
foreach ($size in @(@(58,'100'), @(77,'125'), @(97,'150'), @(116,'175'), @(124,'200'), @(143,'225'), @(159,'250'))) {
  $s = $size[0]
  $bmp, $g = NewCanvas $s $s
  $g.Clear((Hex '#FFFFFF'))
  $m = [single]($s * 0.86)
  DrawMark $g (($s - $m) / 2) (($s - $m) / 2) $m (Hex '#0B6B3A') (Hex '#FFFFFF')
  $file = Join-Path $out "wizard-small-$($size[1]).bmp"
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
  $g.Dispose(); $bmp.Dispose()
  "wrote $file ($s x $s)"
}
