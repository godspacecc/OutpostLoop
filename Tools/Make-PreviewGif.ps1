param([string]$FrameDirectory, [string]$OutputPath)
Add-Type -AssemblyName System.Drawing
$stream = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $first = $true
    foreach ($file in (Get-ChildItem -LiteralPath $FrameDirectory -Filter '*.png' | Sort-Object Name)) {
        $image = [System.Drawing.Image]::FromFile($file.FullName)
        $memory = [System.IO.MemoryStream]::new()
        try { $image.Save($memory, [System.Drawing.Imaging.ImageFormat]::Gif); $bytes = $memory.ToArray() }
        finally { $image.Dispose(); $memory.Dispose() }
        $paletteSize = 3 * [Math]::Pow(2, (($bytes[10] -band 7) + 1))
        $palette = [byte[]]$bytes[13..(12 + $paletteSize)]
        $position = 13 + [int]$paletteSize
        if ($first) {
            $writer.Write([byte[]]$bytes[0..($position - 1)])
            # 无限循环扩展；每帧独立调色板，避免不同截图颜色错位。
            $writer.Write([byte[]]@(0x21,0xff,11,78,69,84,83,67,65,80,69,50,46,48,3,1,0,0,0))
            $first = $false
        }
        while ($bytes[$position] -eq 0x21) {
            $position += 2
            while ($bytes[$position] -ne 0) { $position += 1 + [int]$bytes[$position] }
            $position++
        }
        if ($bytes[$position] -ne 0x2c) { throw 'Unexpected GIF image block' }
        $descriptor = [byte[]]$bytes[$position..($position+9)]
        $position += 10
        if (($descriptor[9] -band 0x80) -ne 0) {
            $localLength = 3 * [Math]::Pow(2,(($descriptor[9] -band 7)+1))
            $palette = [byte[]]$bytes[$position..($position+$localLength-1)]
            $position += [int]$localLength
        } else { $descriptor[9] = 0x80 -bor ($bytes[10] -band 7) }
        $start = $position
        $position++
        while ($bytes[$position] -ne 0) { $position += 1 + [int]$bytes[$position] }
        $writer.Write([byte[]]@(0x21,0xf9,4,4,12,0,0,0))
        $writer.Write($descriptor); $writer.Write($palette)
        $writer.Write([byte[]]$bytes[$start..$position])
    }
    $writer.Write([byte]0x3b)
} finally { $writer.Dispose(); $stream.Dispose() }
