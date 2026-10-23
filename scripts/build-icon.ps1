$ErrorActionPreference = 'Stop'
$assetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::FromFile((Join-Path $assetRoot 'shotink-icon.png'))
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap $size, $size
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = New-Object IO.MemoryStream
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $size, $size)
            if ($size -eq 256) {
                $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            } else {
                # .NET Framework decodes small PNG ICO frames incorrectly; use classic 32-bit DIBs.
                $frameWriter = New-Object IO.BinaryWriter $stream
                try {
                    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                    $frameWriter.Write([uint32]40); $frameWriter.Write([int]$size); $frameWriter.Write([int]($size * 2))
                    $frameWriter.Write([uint16]1); $frameWriter.Write([uint16]32); $frameWriter.Write([uint32]0)
                    $frameWriter.Write([uint32]($size * $size * 4)); $frameWriter.Write([int]0); $frameWriter.Write([int]0)
                    $frameWriter.Write([uint32]0); $frameWriter.Write([uint32]0)
                    for ($row = $size - 1; $row -ge 0; $row--) {
                        for ($column = 0; $column -lt $size; $column++) {
                            $color = $bitmap.GetPixel($column, $row)
                            $frameWriter.Write([byte]$color.B); $frameWriter.Write([byte]$color.G)
                            $frameWriter.Write([byte]$color.R); $frameWriter.Write([byte]$color.A)
                        }
                    }
                    for ($row = $size - 1; $row -ge 0; $row--) {
                        $mask = New-Object byte[] $maskStride
                        for ($column = 0; $column -lt $size; $column++) {
                            if ($bitmap.GetPixel($column, $row).A -eq 0) {
                                $position = [int][Math]::Floor($column / 8.0)
                                $mask[$position] = $mask[$position] -bor (128 -shr ($column % 8))
                            }
                        }
                        $frameWriter.Write([byte[]]$mask)
                    }
                    $frameWriter.Flush()
                    $frames += ,$stream.ToArray()
                } finally { $frameWriter.Dispose() }
                continue
            }
            $frames += ,$stream.ToArray()
        } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
    }
} finally { $source.Dispose() }
$output = [IO.File]::Create((Join-Path $assetRoot 'shotink.ico'))
$writer = New-Object IO.BinaryWriter $output
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
    $offset = 6 + 16 * $sizes.Length
    for ($index = 0; $index -lt $sizes.Length; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
