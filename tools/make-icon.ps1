# Generates src/Kuroko.App/Assets/app.ico (multi-size, PNG-compressed entries).
# Run: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded square with a blue -> violet gradient
    $pad = [Math]::Max(0.5, $size * 0.04)
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $radius = $size * 0.22
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 59, 130, 246)), ([System.Drawing.Color]::FromArgb(255, 124, 92, 255)), 45
    $g.FillPath($brush, $path)

    # white four-point sparkle
    $cx = $size / 2.0; $cy = $size / 2.0
    $outer = $size * 0.30; $inner = $size * 0.075
    $pts = New-Object 'System.Drawing.PointF[]' 8
    for ($i = 0; $i -lt 8; $i++) {
        $angle = [Math]::PI / 4 * $i - [Math]::PI / 2
        $r = if ($i % 2 -eq 0) { $outer } else { $inner }
        $pts[$i] = New-Object System.Drawing.PointF ([single]($cx + $r * [Math]::Cos($angle))), ([single]($cy + $r * [Math]::Sin($angle)))
    }
    $g.FillPolygon([System.Drawing.Brushes]::White, $pts)

    # small secondary sparkle (only where there is room for it)
    if ($size -ge 32) {
        $sx = $size * 0.72; $sy = $size * 0.28; $so = $size * 0.11; $si = $size * 0.03
        $p2 = New-Object 'System.Drawing.PointF[]' 8
        for ($i = 0; $i -lt 8; $i++) {
            $angle = [Math]::PI / 4 * $i - [Math]::PI / 2
            $r = if ($i % 2 -eq 0) { $so } else { $si }
            $p2[$i] = New-Object System.Drawing.PointF ([single]($sx + $r * [Math]::Cos($angle))), ([single]($sy + $r * [Math]::Sin($angle)))
        }
        $g.FillPolygon([System.Drawing.Brushes]::White, $p2)
    }

    $g.Dispose()
    return $bmp
}

# Sizes below 256 are stored as classic DIB entries (readable by every icon API); 256 is PNG, as Windows expects.
function ConvertTo-IconDib($bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $w, $h), 'ReadOnly', 'Format32bppArgb')
    $stride = $data.Stride
    $raw = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))      # BITMAPINFOHEADER (height includes the AND mask)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($w * $h * 4)); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($raw, $y * $stride, $w * 4) }  # pixels, bottom-up BGRA
    $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $h)))                                # AND mask: all zero (alpha does the work)
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Frame $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
    } else {
        $bytes = [byte[]](ConvertTo-IconDib $bmp)   # a function's byte[] result is unrolled by the pipeline; force it back
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $bytes }
}

$outDir = Join-Path $PSScriptRoot '..\src\Kuroko.App\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null
$path = Join-Path (Resolve-Path $outDir) 'app.ico'

$fs = [System.IO.File]::Create($path)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)   # ICONDIR
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {                                                      # ICONDIRENTRY
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$img.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $bw.Write([byte[]]$img.Bytes) }
$bw.Dispose(); $fs.Dispose()
"Wrote $path ($((Get-Item $path).Length) bytes, $($images.Count) sizes)"
