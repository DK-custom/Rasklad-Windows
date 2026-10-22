param([switch]$Tray)
$ErrorActionPreference = 'Stop'
$targetName = if ($Tray) { 'Rasklad-tray.ico' } else { 'Rasklad.ico' }
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'assets/Rasklad-icon.png'))
try {
    if (-not ('RaskladIconBounds' -as [type])) {
        Add-Type -TypeDefinition @'
public static class RaskladIconBounds {
    public static int[] Find(byte[] pixels, int width, int height) {
        int left=width, top=height, right=-1, bottom=-1;
        for (int y=0; y<height; y++) for (int x=0; x<width; x++) {
            if (pixels[(y*width+x)*4+3] == 0) continue;
            if (x<left) left=x; if (x>right) right=x;
            if (y<top) top=y; if (y>bottom) bottom=y;
        }
        return new[] {left,top,right-left+1,bottom-top+1};
    }
}
'@
    }
    $pixels = [byte[]]::new($source.Width * $source.Height * 4)
    $locked = $source.LockBits([Drawing.Rectangle]::new(0,0,$source.Width,$source.Height),
        [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        for ($row=0; $row -lt $source.Height; $row++) {
            [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($locked.Scan0,$row*$locked.Stride),
                $pixels,$row*$source.Width*4,$source.Width*4)
        }
    } finally { $source.UnlockBits($locked) }
    $bounds = [RaskladIconBounds]::Find($pixels,$source.Width,$source.Height)
    if ($bounds[2] -le 0 -or $bounds[3] -le 0) { throw 'Значок полностью прозрачен.' }
    $sourceRect = [Drawing.Rectangle]::new($bounds[0],$bounds[1],$bounds[2],$bounds[3])
    $sizes = if ($Tray) { @(16, 20, 24, 32, 40, 48, 64, 256) } else { @(16, 20, 24, 32, 40, 48, 64, 128, 256) }
    $frames = foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size), $sourceRect, [Drawing.GraphicsUnit]::Pixel)
            if ($size -eq 256 -and -not $Tray) {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            } else {
                # Small ICO frames use the conventional DIB encoding for .NET Framework compatibility.
                $dib = [IO.BinaryWriter]::new($stream, [Text.Encoding]::UTF8, $true)
                try {
                    $dib.Write([uint32]40); $dib.Write([int32]$size); $dib.Write([int32]($size * 2))
                    $dib.Write([uint16]1); $dib.Write([uint16]32)
                    $dib.Write([uint32]0); $dib.Write([uint32]($size * $size * 4))
                    $dib.Write([int32]0); $dib.Write([int32]0); $dib.Write([uint32]0); $dib.Write([uint32]0)
                    $stride = [int]([Math]::Ceiling($size / 32.0) * 4)
                    $mask = [byte[]]::new($stride * $size)
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        for ($x = 0; $x -lt $size; $x++) {
                            $pixel = $bitmap.GetPixel($x, $y)
                            $dib.Write([byte]$pixel.B); $dib.Write([byte]$pixel.G)
                            $dib.Write([byte]$pixel.R); $dib.Write([byte]$pixel.A)
                            if ($pixel.A -eq 0) {
                                $index = ($size - 1 - $y) * $stride + [int][Math]::Floor($x / 8.0)
                                $mask[$index] = $mask[$index] -bor (0x80 -shr ($x % 8))
                            }
                        }
                    }
                    $dib.Write($mask); $dib.Flush()
                } finally { $dib.Dispose() }
            }
            [pscustomobject]@{Size=$size; Bytes=$stream.ToArray()}
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $PSScriptRoot "assets/$targetName"))
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        [uint32]$offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) {0} else {$frame.Size}
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write($offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
} finally { $source.Dispose() }
Write-Output (Join-Path $PSScriptRoot "assets/$targetName")
