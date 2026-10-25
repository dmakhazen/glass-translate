# Draws the 1280x640 social preview image (GitHub → Settings → Social preview).
# Usage: powershell -File tools\social-preview.ps1 <icon-256.png> <docs\screenshot.png> <out.png>
param([string]$Icon, [string]$Screenshot, [string]$Out)
Add-Type -AssemblyName System.Drawing

$w = 1280; $h = 640
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function B($hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function F($size, $style = 'Regular') { New-Object System.Drawing.Font 'Segoe UI', $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }

$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 0, 0, $w, $h), (C '#1B1E26'), (C '#10131A'), 30
$g.FillRectangle($bg, 0, 0, $w, $h)

# Screenshot on the right, running off the edge
$shotImage = [System.Drawing.Image]::FromFile($Screenshot)
$sw = 760; $sh = [int]($shotImage.Height * $sw / $shotImage.Width)
$g.DrawImage($shotImage, 590, [int](($h - $sh) / 2), $sw, $sh)

# Text on the left
$iconImage = [System.Drawing.Image]::FromFile($Icon)
$g.DrawImage($iconImage, 64, 150, 112, 112)
$g.DrawString('Glass Translate', (F 58 'Bold'), (B '#F2F4F7'), 56, 280)
$g.DrawString("Translate any text on your screen,`neven inside images.", (F 28), (B '#B4BCC8'), 62, 362)
$g.DrawString('Free  ·  Portable  ·  No API keys', (F 22 'Bold'), (B '#4C8DFF'), 62, 460)

$bmp.Save($Out)
