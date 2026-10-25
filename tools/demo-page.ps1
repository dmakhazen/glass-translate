# Draws a made-up Thai shop page (header, banner, product grid) used as the background of the README screenshot.
# Usage: powershell -File tools\demo-page.ps1 out.png
param([string]$Out = 'demo-page.png')
Add-Type -AssemblyName System.Drawing

$w = 1400; $h = 900
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'ClearTypeGridFit'
function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function B($hex) { New-Object System.Drawing.SolidBrush (C $hex) }
function P($hex, $width) { New-Object System.Drawing.Pen (C $hex), $width }
function F($size, $style = 'Regular') { New-Object System.Drawing.Font 'Tahoma', $size, ([System.Drawing.FontStyle]$style), ([System.Drawing.GraphicsUnit]::Pixel) }
function RoundRect($x, $y, $rw, $rh, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r, $r, 180, 90); $p.AddArc($x + $rw - $r, $y, $r, $r, 270, 90)
    $p.AddArc($x + $rw - $r, $y + $rh - $r, $r, $r, 0, 90); $p.AddArc($x, $y + $rh - $r, $r, $r, 90, 90)
    $p.CloseFigure(); $p
}
function Text($s, $font, $brush, $x, $y) { $g.DrawString($s, $font, $brush, $x, $y) }

$g.Clear((C '#F3F4F6'))

# Header
$g.FillRectangle((B '#F57224'), 0, 0, $w, 72)
Text 'ShopTH' (F 30 'Bold') (B '#FFFFFF') 36 16
$g.FillPath((B '#FFFFFF'), (RoundRect 220 16 760 40 10))
Text 'ค้นหาสินค้า แบรนด์ และร้านค้า' (F 17) (B '#9CA3AF') 238 24
Text 'เข้าสู่ระบบ' (F 16 'Bold') (B '#FFFFFF') 1040 25
Text 'ตะกร้า (2)' (F 16 'Bold') (B '#FFFFFF') 1180 25

# Categories
$g.FillRectangle((B '#FFFFFF'), 0, 72, $w, 46)
$x = 36
foreach ($c in 'หน้าแรก', 'อิเล็กทรอนิกส์', 'แฟชั่น', 'บ้านและสวน', 'ความงาม', 'กีฬาและกิจกรรม', 'ของเล่น') {
    Text $c (F 16) (B '#374151') $x 84
    $x += $g.MeasureString($c, (F 16)).Width + 34
}

# Promo banner
$banner = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Rectangle 20, 134, 1360, 110), (C '#E8336F'), (C '#FF8A3D'), 0
$g.FillPath($banner, (RoundRect 20 134 1360 110 14))
Text 'เมกะเซลล์ 11.11 ลดสูงสุด 90%' (F 38 'Bold') (B '#FFFFFF') 52 150
Text 'ส่งฟรีไม่มีขั้นต่ำ · คูปองส่วนลดทุกชั่วโมง' (F 20) (B '#FFF3A3') 56 204
$g.FillPath((B '#FFFFFF'), (RoundRect 1180 168 170 46 23))
Text 'ช้อปเลย' (F 20 'Bold') (B '#E8336F') 1226 178

Text 'สินค้าแนะนำสำหรับคุณ' (F 22 'Bold') (B '#1F2329') 24 262

# Product grid: 2 rows x 4 cards
$products = @(
    @('หูฟังบลูทูธไร้สาย TWS', 'เสียงดี แบตอึด กันน้ำ', '฿299', '#6C8CFF', 'ลด 50%', 'headphones'),
    @('พัดลมพกพา ชาร์จ USB', 'เสียงเงียบ ลมแรง 3 ระดับ', '฿159', '#2EC4B6', 'ส่งฟรี', 'fan'),
    @('กระเป๋าเป้กันน้ำ', 'ใส่โน้ตบุ๊ก 15.6 นิ้วได้', '฿450', '#F4A259', 'ขายดี', 'bag'),
    @('แก้วเก็บความเย็น', 'เย็นนาน 24 ชั่วโมง 900 มล.', '฿189', '#9B5DE5', 'ลด 30%', 'cup'),
    @('ครีมกันแดด SPF50', 'กันน้ำ ไม่เหนียวเหนอะหนะ', '฿249', '#F15BB5', 'ของแท้', 'tube'),
    @('รองเท้าวิ่ง น้ำหนักเบา', 'ระบายอากาศได้ดี', '฿690', '#00BBF9', 'ลด 40%', 'shoe'),
    @('สายชาร์จเร็ว Type-C', 'ยาว 2 เมตร ทนทาน', '฿89', '#43AA8B', 'ส่งฟรี', 'cable'),
    @('หม้อทอดไร้น้ำมัน', 'ความจุ 5 ลิตร ตั้งเวลาได้', '฿1,290', '#F94144', 'ขายดี', 'pot')
)
for ($i = 0; $i -lt 8; $i++) {
    $p = $products[$i]
    $cx = 20 + ($i % 4) * 345; $cy = 304 + [math]::Floor($i / 4) * 296
    $g.FillPath((B '#FFFFFF'), (RoundRect $cx $cy 330 282 12))
    # Picture area with a simple drawing of the product
    $g.FillPath((B $p[3]), (RoundRect ($cx + 10) ($cy + 10) 310 140 10))
    $mx = $cx + 165; $my = $cy + 80
    $white = B '#FFFFFF'; $dark = B '#2B2D33'
    switch ($p[5]) {
        'headphones' { $g.DrawArc((P '#FFFFFF' 9), $mx - 40, $my - 45, 80, 80, 180, 180); $g.FillPath($dark, (RoundRect ($mx - 50) ($my - 8) 26 46 10)); $g.FillPath($dark, (RoundRect ($mx + 24) ($my - 8) 26 46 10)) }
        'fan' { $g.FillEllipse($white, $mx - 42, $my - 52, 84, 84); $g.FillEllipse($dark, $mx - 8, $my - 18, 16, 16); $g.FillRectangle($white, $mx - 6, $my + 30, 12, 26) }
        'bag' { $g.FillPath($white, (RoundRect ($mx - 40) ($my - 44) 80 96 18)); $g.FillPath($dark, (RoundRect ($mx - 26) ($my + 6) 52 30 8)) }
        'cup' { $g.FillPath($white, (RoundRect ($mx - 26) ($my - 50) 52 104 14)); $g.FillRectangle($dark, $mx - 26, $my - 34, 52, 8) }
        'tube' { $g.FillPath($white, (RoundRect ($mx - 22) ($my - 50) 44 96 10)); $g.FillRectangle($dark, $mx - 14, $my + 46, 28, 10) }
        'shoe' { $g.FillPath($white, (RoundRect ($mx - 60) ($my - 10) 120 40 18)); $g.FillPath($white, (RoundRect ($mx - 40) ($my - 34) 50 40 14)); $g.FillRectangle($dark, $mx - 60, $my + 24, 120, 8) }
        'cable' { $g.DrawArc((P '#FFFFFF' 8), $mx - 45, $my - 45, 90, 90, 30, 300); $g.FillPath($dark, (RoundRect ($mx + 30) ($my - 14) 34 22 6)) }
        'pot' { $g.FillPath($white, (RoundRect ($mx - 46) ($my - 46) 92 100 22)); $g.FillPath($dark, (RoundRect ($mx - 28) ($my - 20) 56 40 10)) }
    }
    $g.FillPath((B '#FFFFFF'), (RoundRect ($cx + 18) ($cy + 18) 74 28 8))
    Text $p[4] (F 15 'Bold') (B '#E8336F') ($cx + 26) ($cy + 23)
    Text $p[0] (F 18 'Bold') (B '#1F2329') ($cx + 14) ($cy + 160)
    Text $p[1] (F 16) (B '#4B5563') ($cx + 14) ($cy + 188)
    Text $p[2] (F 24 'Bold') (B '#F57224') ($cx + 14) ($cy + 220)
    Text 'ขายแล้ว 1.2พัน' (F 14) (B '#9CA3AF') ($cx + 214) ($cy + 230)
}

$bmp.Save($Out)
