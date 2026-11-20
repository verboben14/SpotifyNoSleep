[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-IconPngBytes([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $scale = $Size / 32.0

        # Outer Spotify Green circle
        $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(29, 185, 84))
        $g.FillEllipse($bgBrush, [float](1.0 * $scale), [float](1.0 * $scale), [float](30.0 * $scale), [float](30.0 * $scale))
        $bgBrush.Dispose()

        # Subtle dark rim
        $rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(75, 0, 0, 0), [float]([Math]::Max(1.0, 1.1 * $scale)))
        $g.DrawEllipse($rimPen, [float](1.5 * $scale), [float](1.5 * $scale), [float](29.0 * $scale), [float](29.0 * $scale))
        $rimPen.Dispose()

        # Three curved white sound-wave arcs
        $wavePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float]([Math]::Max(1.4, 2.6 * $scale)))
        $wavePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $wavePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($wavePen, [float](5.5 * $scale), [float](8.5 * $scale), [float](21.0 * $scale), [float](14.0 * $scale), 212.0, 116.0)
        $g.DrawArc($wavePen, [float](7.5 * $scale), [float](13.5 * $scale), [float](17.0 * $scale), [float](11.0 * $scale), 212.0, 116.0)
        $g.DrawArc($wavePen, [float](9.5 * $scale), [float](18.2 * $scale), [float](13.0 * $scale), [float](8.0 * $scale), 212.0, 116.0)
        $wavePen.Dispose()

        # Awake sun/dot badge in top-right corner
        $haloBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(24, 24, 24))
        $g.FillEllipse($haloBrush, [float](21.0 * $scale), [float](1.0 * $scale), [float](10.0 * $scale), [float](10.0 * $scale))
        $haloBrush.Dispose()

        $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 102))
        $g.FillEllipse($dotBrush, [float](22.5 * $scale), [float](2.5 * $scale), [float](7.0 * $scale), [float](7.0 * $scale))
        $dotBrush.Dispose()

        $ms = New-Object System.IO.MemoryStream
        try {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            return $ms.ToArray()
        }
        finally {
            $ms.Dispose()
        }
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

$sizes = @(16, 32, 48, 256)
$images = @()
foreach ($s in $sizes) {
    $images += ,@($s, (New-IconPngBytes -Size $s))
}

$dir = Split-Path -Parent $OutputPath
if ($dir -and -not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$fs = [System.IO.File]::Create($OutputPath)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR header
    $bw.Write([UInt16]0)               # Reserved
    $bw.Write([UInt16]1)               # Type = 1 (ICO)
    $bw.Write([UInt16]$images.Count)   # Count

    $offset = 6 + (16 * $images.Count)
    foreach ($entry in $images) {
        $s = [int]$entry[0]
        $png = [byte[]]$entry[1]
        $dim = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $bw.Write($dim)                # Width
        $bw.Write($dim)                # Height
        $bw.Write([byte]0)             # ColorCount
        $bw.Write([byte]0)             # Reserved
        $bw.Write([UInt16]1)           # Planes
        $bw.Write([UInt16]32)          # BitCount
        $bw.Write([UInt32]$png.Length) # BytesInRes
        $bw.Write([UInt32]$offset)     # ImageOffset
        $offset += $png.Length
    }

    foreach ($entry in $images) {
        $bw.Write([byte[]]$entry[1])
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}
