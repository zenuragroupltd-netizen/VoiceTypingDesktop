# make-flags.ps1
# ============================================================
# Generates simplified country-flag PNGs for the Translator
# language dropdown. Uses pure System.Drawing — no external
# images, no external tools. Designs are stylised but
# unambiguous at the ~22×16 target render size.
#
# Output: Assets\Flags\<code>.png  (44×32, 2× for crisp HiDPI)
# ============================================================

param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\Assets\Flags')
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$W = 44
$H = 32

function New-Flag {
    param([string]$Name, [scriptblock]$Draw)

    $bmp = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    & $Draw $g

    $g.Dispose()
    $bmp.Save((Join-Path $OutDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  + $Name.png"
}

function SolidBrush([int]$r, [int]$g, [int]$b) {
    return New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $r, $g, $b))
}

# ------------------------------------------------------------
# Bangladesh: bottle-green with red disc slightly left of centre.
# ------------------------------------------------------------
New-Flag -Name 'bd' -Draw {
    param($g)
    $green = SolidBrush 0 106 78
    $red   = SolidBrush 244 42 65
    $g.FillRectangle($green, 0, 0, $W, $H)
    $r = 10; $cx = 20; $cy = 16
    $g.FillEllipse($red, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
}

# ------------------------------------------------------------
# India: horizontal tricolour with navy Ashoka ring outline.
# ------------------------------------------------------------
New-Flag -Name 'in' -Draw {
    param($g)
    $orange = SolidBrush 255 153 51
    $green  = SolidBrush 19 136 8
    $h3 = [int]($H / 3)
    $g.FillRectangle($orange, 0, 0, $W, $h3)
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, $h3, $W, ($H - 2 * $h3))
    $g.FillRectangle($green, 0, ($H - $h3), $W, $h3)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 0, 128)), 1.2
    $g.DrawEllipse($pen, ($W / 2 - 5), ($H / 2 - 5), 10, 10)
}

# ------------------------------------------------------------
# UK (GB): blue background with simplified Union cross overlay.
# ------------------------------------------------------------
New-Flag -Name 'gb' -Draw {
    param($g)
    $blue = SolidBrush 0 36 125
    $red  = SolidBrush 207 20 43
    $g.FillRectangle($blue, 0, 0, $W, $H)

    # White saltire (diagonals) at reduced thickness
    $wp = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 6
    $g.DrawLine($wp, 0, 0, $W, $H)
    $g.DrawLine($wp, $W, 0, 0, $H)

    # Red saltire (narrower) on top of white
    $rp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 207, 20, 43)), 2.2
    $g.DrawLine($rp, 0, 0, $W, $H)
    $g.DrawLine($rp, $W, 0, 0, $H)

    # White cross (thicker, over saltire)
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, ($H / 2 - 5), $W, 10)
    $g.FillRectangle([System.Drawing.Brushes]::White, ($W / 2 - 5), 0, 10, $H)

    # Red cross (narrower) over white
    $g.FillRectangle($red, 0, ($H / 2 - 2), $W, 4)
    $g.FillRectangle($red, ($W / 2 - 2), 0, 4, $H)
}

# ------------------------------------------------------------
# USA: 13 red/white stripes + blue canton with star dots.
# ------------------------------------------------------------
New-Flag -Name 'us' -Draw {
    param($g)
    $red  = SolidBrush 191 10 48
    $blue = SolidBrush 0 40 104

    # Stripes (13 alternating)
    $sh = $H / 13.0
    for ($i = 0; $i -lt 13; $i++) {
        $brush = if ($i % 2 -eq 0) { $red } else { [System.Drawing.Brushes]::White }
        $g.FillRectangle($brush, 0, ($i * $sh), $W, ($sh + 0.5))
    }
    # Canton
    $cw = [int]($W * 0.4)
    $ch = [int]($H * 7 / 13)
    $g.FillRectangle($blue, 0, 0, $cw, $ch)
    # Simplified stars → white dots (5×4 grid)
    for ($row = 0; $row -lt 4; $row++) {
        for ($col = 0; $col -lt 5; $col++) {
            $x = 2 + $col * 3.0
            $y = 2 + $row * 3.5
            $g.FillEllipse([System.Drawing.Brushes]::White, $x, $y, 1.5, 1.5)
        }
    }
}

# ------------------------------------------------------------
# Pakistan: white hoist + dark-green field + white crescent disc.
# ------------------------------------------------------------
New-Flag -Name 'pk' -Draw {
    param($g)
    $green = SolidBrush 1 65 28
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, $W, $H)
    $hoist = [int]($W * 0.25)
    $g.FillRectangle($green, $hoist, 0, ($W - $hoist), $H)

    # Crescent (two circles, the second offset subtracts from the first)
    $cx = $hoist + ($W - $hoist) / 2 + 2
    $cy = $H / 2
    $r  = 7
    $g.FillEllipse([System.Drawing.Brushes]::White, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
    $g.FillEllipse($green, ($cx - $r + 3), ($cy - $r), ($r * 2), ($r * 2))
    # Little star
    $g.FillEllipse([System.Drawing.Brushes]::White, ($cx + 4), ($cy - 2), 3, 3)
}

# ------------------------------------------------------------
# Saudi Arabia: solid green with white sword bar.
# ------------------------------------------------------------
New-Flag -Name 'sa' -Draw {
    param($g)
    $green = SolidBrush 0 108 53
    $g.FillRectangle($green, 0, 0, $W, $H)
    # Simplified sword line
    $g.FillRectangle([System.Drawing.Brushes]::White, 8, 22, 28, 2)
    # Stylised shahada line
    $g.FillRectangle([System.Drawing.Brushes]::White, 10, 10, 24, 1.5)
    $g.FillRectangle([System.Drawing.Brushes]::White, 12, 14, 20, 1.5)
}

# ------------------------------------------------------------
# Spain: red-yellow-red horizontal (middle band is taller).
# ------------------------------------------------------------
New-Flag -Name 'es' -Draw {
    param($g)
    $red    = SolidBrush 198 11 30
    $yellow = SolidBrush 255 199 44
    $q = $H / 4.0
    $g.FillRectangle($red,    0, 0,        $W, $q)
    $g.FillRectangle($yellow, 0, $q,       $W, ($q * 2))
    $g.FillRectangle($red,    0, ($q * 3), $W, $q)
}

# ------------------------------------------------------------
# France: blue / white / red vertical thirds.
# ------------------------------------------------------------
New-Flag -Name 'fr' -Draw {
    param($g)
    $blue = SolidBrush 0 35 149
    $red  = SolidBrush 237 41 57
    $w3 = $W / 3.0
    $g.FillRectangle($blue, 0,         0, $w3, $H)
    $g.FillRectangle([System.Drawing.Brushes]::White, $w3, 0, $w3, $H)
    $g.FillRectangle($red,  ($w3 * 2), 0, ($W - $w3 * 2), $H)
}

# ------------------------------------------------------------
# Germany: black / red / gold horizontal thirds.
# ------------------------------------------------------------
New-Flag -Name 'de' -Draw {
    param($g)
    $red  = SolidBrush 221 0 0
    $gold = SolidBrush 255 206 0
    $h3 = $H / 3.0
    $g.FillRectangle([System.Drawing.Brushes]::Black, 0, 0, $W, $h3)
    $g.FillRectangle($red,  0, $h3,       $W, $h3)
    $g.FillRectangle($gold, 0, ($h3 * 2), $W, ($H - $h3 * 2))
}

# ------------------------------------------------------------
# China: red field + one large + four small yellow stars.
# ------------------------------------------------------------
New-Flag -Name 'cn' -Draw {
    param($g)
    $red    = SolidBrush 238 28 37
    $yellow = SolidBrush 255 222 0
    $g.FillRectangle($red, 0, 0, $W, $H)
    # Big star (simplified to diamond)
    $big = [int]6
    $bx = 9; $by = 10
    $g.FillEllipse($yellow, ($bx - $big / 2), ($by - $big / 2), $big, $big)
    # Four small stars
    foreach ($p in @(@(18, 4), @(20, 10), @(18, 16), @(14, 18))) {
        $g.FillEllipse($yellow, ($p[0] - 1.5), ($p[1] - 1.5), 3, 3)
    }
}

# ------------------------------------------------------------
# Japan: white field with red disc centred.
# ------------------------------------------------------------
New-Flag -Name 'jp' -Draw {
    param($g)
    $red = SolidBrush 188 0 45
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, $W, $H)
    $r = 8
    $g.FillEllipse($red, (($W / 2) - $r), (($H / 2) - $r), ($r * 2), ($r * 2))
}

# ------------------------------------------------------------
# South Korea: white field + simplified taeguk circle + trigrams
# ------------------------------------------------------------
New-Flag -Name 'kr' -Draw {
    param($g)
    $red  = SolidBrush 205 38 58
    $blue = SolidBrush 0 71 160
    $g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, $W, $H)
    $r = 8
    $cx = $W / 2; $cy = $H / 2
    # Top-half red, bottom-half blue (simplified taeguk)
    $rx = [float]($cx - $r); $ry = [float]($cy - $r); $rw = [float]($r * 2); $rh = [float]($r * 2)
    $g.FillPie($red,  $rx, $ry, $rw, $rh, 180, 180)
    $g.FillPie($blue, $rx, $ry, $rw, $rh, 0,   180)
    # Trigram dots at corners
    foreach ($pt in @(@(4, 4), @(39, 4), @(4, 27), @(39, 27))) {
        $g.FillRectangle([System.Drawing.Brushes]::Black, $pt[0], $pt[1], 2, 2)
    }
}

# ------------------------------------------------------------
# UAE / Arabic fallback: red + green/white/black horizontal bars.
# ------------------------------------------------------------
New-Flag -Name 'ae' -Draw {
    param($g)
    $red   = SolidBrush 239 52 65
    $green = SolidBrush 0 115 47
    $g.FillRectangle($red, 0, 0, $W, $H)
    $hoist = [int]($W * 0.25)
    $bandH = $H / 3.0
    $g.FillRectangle($green, $hoist, 0,           ($W - $hoist), $bandH)
    $g.FillRectangle([System.Drawing.Brushes]::White, $hoist, $bandH, ($W - $hoist), $bandH)
    $g.FillRectangle([System.Drawing.Brushes]::Black, $hoist, ($bandH * 2), ($W - $hoist), ($H - $bandH * 2))
}

# ------------------------------------------------------------
# Globe icon for "Auto detect".
# ------------------------------------------------------------
New-Flag -Name 'auto' -Draw {
    param($g)
    $bg = SolidBrush 124 92 255           # accent purple
    $g.FillRectangle($bg, 0, 0, $W, $H)
    $cx = $W / 2; $cy = $H / 2; $r = 10
    $white = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 1.4
    $g.DrawEllipse($white, ($cx - $r), ($cy - $r), ($r * 2), ($r * 2))
    $g.DrawLine($white, ($cx - $r), $cy, ($cx + $r), $cy)
    $g.DrawLine($white, $cx, ($cy - $r), $cx, ($cy + $r))
    $g.DrawArc($white, ($cx - $r / 2), ($cy - $r), $r, ($r * 2), 90, 180)
    $g.DrawArc($white, ($cx - $r / 2), ($cy - $r), $r, ($r * 2), 270, 180)
}

Write-Host "`nDone. Flags written to: $OutDir"
