# make-icon.ps1
# --------------------------------------------------------------
# Generates a multi-resolution .ico from code using pure .NET.
# No external tools (ImageMagick, Inkscape, etc.) required.
# Produces: Assets/app.ico  (containing 16, 32, 48, 64, 128, 256 px)
# --------------------------------------------------------------

param(
    [string]$OutPath = (Join-Path $PSScriptRoot "..\Assets\app.ico")
)

Add-Type -AssemblyName System.Drawing

function New-AppBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Scale factor vs the 256 design
    $s = $Size / 256.0

    # Rounded rectangle path (radius 52 at 256)
    $r = 52 * $s
    $rectX = 8 * $s; $rectY = 8 * $s
    $rectW = 240 * $s; $rectH = 240 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rectX,          $rectY,          $r*2, $r*2, 180, 90) | Out-Null
    $path.AddArc($rectX+$rectW-$r*2, $rectY,       $r*2, $r*2, 270, 90) | Out-Null
    $path.AddArc($rectX+$rectW-$r*2, $rectY+$rectH-$r*2, $r*2, $r*2, 0, 90) | Out-Null
    $path.AddArc($rectX,          $rectY+$rectH-$r*2, $r*2, $r*2, 90, 90) | Out-Null
    $path.CloseFigure()

    # Background gradient (accent)
    $rect = New-Object System.Drawing.RectangleF $rectX, $rectY, $rectW, $rectH
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 138, 108, 255),   # #8A6CFF
        [System.Drawing.Color]::FromArgb(255,  79,  46, 224),   # #4F2EE0
        [float]45)
    $g.FillPath($grad, $path)
    $grad.Dispose()

    # Clip so subsequent shapes don't spill over the rounded corner
    $g.SetClip($path)

    # Soft top inner highlight
    $hlRect = New-Object System.Drawing.RectangleF ($rectX + 8*$s), ($rectY + 8*$s), ($rectW - 16*$s), (112*$s)
    $hlBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb([int](255*0.08), 255, 255, 255))
    $g.FillRectangle($hlBrush, $hlRect)
    $hlBrush.Dispose()

    # ---------- Microphone ----------
    $white     = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $whitePen  = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([float](10*$s))
    $whitePen.StartCap = 'Round'; $whitePen.EndCap = 'Round'

    # Mic body rounded rect (104,64,48,96) r=24
    $mx = 104*$s; $my = 64*$s; $mw = 48*$s; $mh = 96*$s; $mr = 24*$s
    $mp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mp.AddArc($mx,          $my,          $mr*2, $mr*2, 180, 90) | Out-Null
    $mp.AddArc($mx+$mw-$mr*2, $my,          $mr*2, $mr*2, 270, 90) | Out-Null
    $mp.AddArc($mx+$mw-$mr*2, $my+$mh-$mr*2, $mr*2, $mr*2,   0, 90) | Out-Null
    $mp.AddArc($mx,          $my+$mh-$mr*2, $mr*2, $mr*2,  90, 90) | Out-Null
    $mp.CloseFigure()
    $g.FillPath($white, $mp)
    $mp.Dispose()

    # Mic stand arc: semicircle from (72,128) to (184,128), radius 56
    $arcRect = New-Object System.Drawing.RectangleF (72*$s), (72*$s), (112*$s), (112*$s)
    $g.DrawArc($whitePen, $arcRect, 0, 180)

    # Stem 122,176,12,28
    $g.FillRectangle($white, ($rect = New-Object System.Drawing.RectangleF (122*$s), (176*$s), (12*$s), (28*$s)))
    # Base 92,200,72,12
    $g.FillRectangle($white, (New-Object System.Drawing.RectangleF (92*$s), (200*$s), (72*$s), (12*$s)))

    # ---------- Sound waves ----------
    $wavePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb([int](255*0.85), 255, 255, 255)), ([float](8*$s))
    $wavePen.StartCap = 'Round'; $wavePen.EndCap = 'Round'
    # Left short: (44,120) Q (48,128) (44,136)  — approximate as two lines
    # Use Bezier curves instead for smoothness.
    $g.DrawBezier($wavePen,
        (New-Object System.Drawing.PointF (44*$s),(120*$s)),
        (New-Object System.Drawing.PointF (52*$s),(128*$s)),
        (New-Object System.Drawing.PointF (52*$s),(128*$s)),
        (New-Object System.Drawing.PointF (44*$s),(136*$s)))
    $g.DrawBezier($wavePen,
        (New-Object System.Drawing.PointF (28*$s),(104*$s)),
        (New-Object System.Drawing.PointF (44*$s),(128*$s)),
        (New-Object System.Drawing.PointF (44*$s),(128*$s)),
        (New-Object System.Drawing.PointF (28*$s),(152*$s)))
    $g.DrawBezier($wavePen,
        (New-Object System.Drawing.PointF (212*$s),(120*$s)),
        (New-Object System.Drawing.PointF (204*$s),(128*$s)),
        (New-Object System.Drawing.PointF (204*$s),(128*$s)),
        (New-Object System.Drawing.PointF (212*$s),(136*$s)))
    $g.DrawBezier($wavePen,
        (New-Object System.Drawing.PointF (228*$s),(104*$s)),
        (New-Object System.Drawing.PointF (212*$s),(128*$s)),
        (New-Object System.Drawing.PointF (212*$s),(128*$s)),
        (New-Object System.Drawing.PointF (228*$s),(152*$s)))

    $whitePen.Dispose(); $wavePen.Dispose(); $white.Dispose()
    $g.ResetClip(); $path.Dispose(); $g.Dispose()
    return ,$bmp
}

function Write-Ico {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $pngs = @()
    foreach ($sz in $Sizes) {
        $bmp = New-AppBitmap -Size $sz
        $ms  = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs += ,@{ Size = $sz; Bytes = $ms.ToArray() }
        $ms.Dispose(); $bmp.Dispose()
    }

    # Build ICO header + directory + data
    $outDir = [System.IO.Path]::GetDirectoryName($Path)
    if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    $fs = [System.IO.File]::Create($Path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([UInt16]0)          # reserved
    $bw.Write([UInt16]1)          # type = 1 (icon)
    $bw.Write([UInt16]$pngs.Count)

    $offset = 6 + 16 * $pngs.Count
    foreach ($p in $pngs) {
        $w = if ($p.Size -ge 256) { 0 } else { [byte]$p.Size }
        $h = $w
        $bw.Write([byte]$w)
        $bw.Write([byte]$h)
        $bw.Write([byte]0)        # colors
        $bw.Write([byte]0)        # reserved
        $bw.Write([UInt16]1)      # planes
        $bw.Write([UInt16]32)     # bpp
        $bw.Write([UInt32]$p.Bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $p.Bytes.Length
    }
    foreach ($p in $pngs) { $bw.Write($p.Bytes) }

    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

Write-Ico -Path $OutPath -Sizes @(16, 24, 32, 48, 64, 128, 256)
Write-Host "Wrote $OutPath"
