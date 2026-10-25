# Draws a made-up Thai shop page used as the background of the README screenshot.
# Usage: powershell -File tools\demo-page.ps1 out.png
param([string]$Out = 'demo-page.png')
Add-Type -AssemblyName System.Drawing

$w = 1000; $h = 440
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'ClearTypeGridFit'
function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function B($hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function F($size, $style = 'Regular') { New-Object System.Drawing.Font 'Tahoma', $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }
function RoundRect($x, $y, $rw, $rh, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90); $p.AddArc($x + $rw - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $rw - $r, $y + $rh - $r, $r, $r, 0, 90); $p.AddArc($x, $y + $rh - $r, $r, $r, 90, 90)
    $p.CloseFigure(); $p
}

$g.Clear((C '#F3F4F6'))

# Header: shop name and search box
$g.FillRectangle((B '#FFFFFF'), 0, 0, $w, 52)
$g.DrawString('ShopTH', (F 22 'Bold'), (B '#F57224'), 24, 12)
$g.FillPath((B '#F1F2F4'), (RoundRect 170 11 560 30 8))
$g.DrawString('ค้นหาสินค้าและร้านค้า', (F 14), (B '#8A8F98'), 184, 16)

# Product card
$g.FillPath((B '#FFFFFF'), (RoundRect 20 66 960 360 14))

# Product picture: banner with sale text
$img = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 40, 86, 300, 320), (C '#FF5C8A'), (C '#FF8A3D'), 45
$g.FillPath($img, (RoundRect 40 86 300 320 12))
# Headphones: headband arc and two ear cups
$band = New-Object System.Drawing.Pen (C '#FFFFFF'), 16
$g.DrawArc($band, 118, 185, 144, 140, 180, 180)
$g.FillPath((B '#2B2D33'), (RoundRect 104 245 50 80 18)); $g.FillPath((B '#2B2D33'), (RoundRect 226 245 50 80 18))
$g.FillPath((B '#FFFFFF'), (RoundRect 112 256 34 58 12)); $g.FillPath((B '#FFFFFF'), (RoundRect 234 256 34 58 12))
$g.DrawString('ลดราคาพิเศษ 50%', (F 26 'Bold'), (B '#FFFFFF'), 58, 104)
$g.DrawString('วันนี้เท่านั้น!', (F 17 'Bold'), (B '#FFF3A3'), 60, 142)
$g.FillPath((B '#FFFFFF'), (RoundRect 56 346 268 40 10))
$g.DrawString('ส่งฟรีทั่วประเทศ', (F 17 'Bold'), (B '#F5465C'), 112, 355)

# Details
$g.DrawString('หูฟังบลูทูธไร้สาย TWS เสียงดี แบตอึด', (F 21 'Bold'), (B '#1F2329'), 360, 92)
$g.DrawString('กันน้ำ IPX5 รองรับ iPhone และ Android', (F 21 'Bold'), (B '#1F2329'), 360, 122)
$g.DrawString('★★★★★', (F 15), (B '#FFB400'), 362, 162)
$g.DrawString('4.9 · ขายแล้ว 1,200 ชิ้น', (F 15), (B '#6B7280'), 440, 163)
$g.FillPath((B '#FFF4EC'), (RoundRect 360 196 600 62 10))
$g.DrawString('฿299', (F 34 'Bold'), (B '#F57224'), 374, 205)
$g.DrawString('฿599', (F 17), (B '#9CA3AF'), 480, 220)
$g.DrawLine((New-Object System.Drawing.Pen (C '#9CA3AF'), 1.5), 480, 232, 522, 232)
$g.DrawString('ตัวเลือกสี: ดำ ขาว ชมพู', (F 16), (B '#374151'), 362, 278)
$g.DrawString('ส่งจากกรุงเทพฯ ได้รับสินค้าภายใน 2-3 วัน', (F 16), (B '#374151'), 362, 308)
$g.FillPath((B '#FFFFFF'), (RoundRect 362 352 290 50 10))
$g.DrawPath((New-Object System.Drawing.Pen (C '#F57224'), 2), (RoundRect 362 352 290 50 10))
$g.DrawString('ซื้อเลย', (F 18 'Bold'), (B '#F57224'), 474, 364)
$g.FillPath((B '#F57224'), (RoundRect 666 352 294 50 10))
$g.DrawString('เพิ่มลงรถเข็น', (F 18 'Bold'), (B '#FFFFFF'), 760, 364)

$bmp.Save($Out)
