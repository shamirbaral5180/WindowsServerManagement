param(
    [string]$PngPath = "src/WindowsServerManagement.App/Assets/AppIcon.png",
    [string]$IcoPath = "src/WindowsServerManagement.App/Assets/AppIcon.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.Clear([System.Drawing.Color]::Transparent)

$backgroundPath = New-RoundedPath 6 6 244 244 44
$backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, 256, 256)),
    [System.Drawing.Color]::FromArgb(255, 0, 120, 215),
    [System.Drawing.Color]::FromArgb(255, 0, 75, 135),
    45)
$graphics.FillPath($backgroundBrush, $backgroundPath)

$highlightBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(52, 255, 255, 255))
$graphics.FillEllipse($highlightBrush, -50, -70, 320, 230)

$rackShadow = New-RoundedPath 45 53 106 150 16
$shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(55, 0, 0, 0))
$graphics.FillPath($shadowBrush, $rackShadow)

$rackPath = New-RoundedPath 38 46 106 150 16
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 248, 252, 255))
$graphics.FillPath($whiteBrush, $rackPath)

$slotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 91, 163))
$slotHighlight = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 255, 255, 255))
foreach ($y in @(72, 112, 152)) {
    $slot = New-RoundedPath 54 $y 74 20 6
    $graphics.FillPath($slotBrush, $slot)
    $graphics.FillRectangle($slotHighlight, 59, ($y + 4), 34, 4)
    $slot.Dispose()
}

$greenBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 104, 231, 154))
$cyanBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 94, 218, 239))
$graphics.FillEllipse($greenBrush, 115, 78, 8, 8)
$graphics.FillEllipse($cyanBrush, 115, 118, 8, 8)
$graphics.FillEllipse($greenBrush, 115, 158, 8, 8)

$globeRingBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 78, 140))
$graphics.FillEllipse($globeRingBrush, 111, 106, 112, 112)
$globePath = New-Object System.Drawing.Drawing2D.GraphicsPath
$globePath.AddEllipse(118, 113, 98, 98)
$globeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 248, 252, 255))
$graphics.FillPath($globeBrush, $globePath)

$globePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 0, 91, 163), 4.5)
$globePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$globePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.DrawEllipse($globePen, 118, 113, 98, 98)
$graphics.SetClip($globePath)
$graphics.DrawEllipse($globePen, 145, 113, 44, 98)
$graphics.DrawEllipse($globePen, 128, 113, 78, 98)
$graphics.DrawLine($globePen, 119, 162, 215, 162)
$graphics.DrawLine($globePen, 122, 143, 212, 143)
$graphics.DrawLine($globePen, 122, 181, 212, 181)
$graphics.ResetClip()

$smallDot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 94, 218, 239))
$graphics.FillEllipse($smallDot, 198, 118, 12, 12)

$bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($targetSize in $sizes) {
    $frame = New-Object System.Drawing.Bitmap($targetSize, $targetSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $frameGraphics = [System.Drawing.Graphics]::FromImage($frame)
    $frameGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $frameGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $frameGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $frameGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $frameGraphics.Clear([System.Drawing.Color]::Transparent)
    $frameGraphics.DrawImage($bitmap, 0, 0, $targetSize, $targetSize)
    $stream = New-Object System.IO.MemoryStream
    $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{ Size = $targetSize; Data = $stream.ToArray() }
    $frameGraphics.Dispose()
    $frame.Dispose()
    $stream.Dispose()
}

$fileStream = [System.IO.File]::Create($IcoPath)
$writer = New-Object System.IO.BinaryWriter($fileStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) {
    $writer.Write($frame.Data)
}
$writer.Dispose()
$fileStream.Dispose()

$backgroundBrush.Dispose()
$highlightBrush.Dispose()
$shadowBrush.Dispose()
$whiteBrush.Dispose()
$slotBrush.Dispose()
$slotHighlight.Dispose()
$greenBrush.Dispose()
$cyanBrush.Dispose()
$globeRingBrush.Dispose()
$globeBrush.Dispose()
$globePen.Dispose()
$smallDot.Dispose()
$backgroundPath.Dispose()
$rackShadow.Dispose()
$rackPath.Dispose()
$globePath.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Get-Item $PngPath, $IcoPath | Select-Object FullName, Length
