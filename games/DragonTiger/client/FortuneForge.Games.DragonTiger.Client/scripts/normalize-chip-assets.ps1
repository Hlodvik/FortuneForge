param([switch]$Check, [switch]$RebuildApproved)

$ErrorActionPreference = 'Stop'
if ($Check -and $RebuildApproved) { throw 'Check mode cannot rebuild assets.' }
Add-Type -AssemblyName System.Drawing
$chipAssetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/assets'))
$pristineAssetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../Baccarat/client/FortuneForge.Games.Baccarat.Client/src'))
$canvasSize = 64
$bodySize = 60
$bodyMargin = 2
$pristineHashes = @{
    'chip-050.png' = 'B8E4AAF15B4B1499C22AC642BE2C66A3AFB401E688BCAF70853A7AA3F2ACFCCD'
    'chip-50.png' = 'A7F2B11529F2303BF334EF253EE1E3044016BBD9EEAF74C9EB39D9D60C99A36F'
}

function Get-ChipBodyBounds([System.Drawing.Bitmap]$Bitmap) {
    $left = $Bitmap.Width
    $top = $Bitmap.Height
    $right = -1
    $bottom = -1
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            # Ignore the translucent shadow when measuring the actual chip face.
            if ($Bitmap.GetPixel($x, $y).A -ge 160) {
                $left = [Math]::Min($left, $x)
                $top = [Math]::Min($top, $y)
                $right = [Math]::Max($right, $x)
                $bottom = [Math]::Max($bottom, $y)
            }
        }
    }
    if ($right -lt 0) { throw 'Chip contains no opaque artwork.' }
    return [System.Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
}

function Test-NormalizedChip([System.Drawing.Bitmap]$Bitmap) {
    $bounds = Get-ChipBodyBounds $Bitmap
    return $Bitmap.Width -eq $canvasSize -and $Bitmap.Height -eq $canvasSize -and
        $bounds.X -eq $bodyMargin -and $bounds.Y -eq $bodyMargin -and
        $bounds.Width -eq $bodySize -and $bounds.Height -eq $bodySize
}

function New-NormalizedChip([System.Drawing.Bitmap]$Source) {
    $bounds = Get-ChipBodyBounds $Source
    # Bicubic interpolation can shift a sampled alpha boundary by one pixel.
    # Calibrate only a small affine range, always rendering the pristine image.
    # Never resize a previous candidate or a previously normalized output.
    foreach ($extentCorrection in @(0, -.25, .25, -.5, .5)) {
        foreach ($offsetX in @(0, .25, -.25, .5, -.5)) {
            foreach ($offsetY in @(0, .25, -.25, .5, -.5)) {
                $candidate = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                $graphics = [System.Drawing.Graphics]::FromImage($candidate)
                try {
                    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $scaleX = ($bodySize + $extentCorrection) / $bounds.Width
                    $scaleY = ($bodySize + $extentCorrection) / $bounds.Height
                    $destination = [System.Drawing.RectangleF]::new(
                        $bodyMargin - $bounds.X * $scaleX + $offsetX,
                        $bodyMargin - $bounds.Y * $scaleY + $offsetY,
                        $Source.Width * $scaleX,
                        $Source.Height * $scaleY)
                    # Transform the full RGBA image, keeping its antialiased fringe.
                    $graphics.DrawImage($Source, $destination, [System.Drawing.RectangleF]::new(0, 0, $Source.Width, $Source.Height), [System.Drawing.GraphicsUnit]::Pixel)
                }
                finally { $graphics.Dispose() }

                if (Test-NormalizedChip $candidate) { return $candidate }
                $candidate.Dispose()
            }
        }
    }
    throw 'No centered 60x60 chip was found within the bounded affine calibration.'
}

foreach ($chipFile in Get-ChildItem -LiteralPath $chipAssetDirectory -Filter 'chip-*.png' | Sort-Object Name) {
    $current = [System.Drawing.Bitmap]::FromFile($chipFile.FullName)
    try {
        if ((Test-NormalizedChip $current) -and -not ($RebuildApproved -and $pristineHashes.ContainsKey($chipFile.Name))) {
            Write-Output "$($chipFile.Name): already centered, 60x60 chip in 64x64 canvas"
            continue
        }
        if ($Check) { throw "$($chipFile.Name): inconsistent chip face: $(Get-ChipBodyBounds $current)" }
    }
    finally { $current.Dispose() }

    if (-not $pristineHashes.ContainsKey($chipFile.Name)) {
        throw "$($chipFile.Name): no approved pristine source; leave this artwork unchanged."
    }
    $pristinePath = Join-Path $pristineAssetDirectory $chipFile.Name
    if ((Get-FileHash -LiteralPath $pristinePath -Algorithm SHA256).Hash -ne $pristineHashes[$chipFile.Name]) {
        throw "$($chipFile.Name): the approved pristine artwork changed; normalization was not performed."
    }
    $source = [System.Drawing.Bitmap]::FromFile($pristinePath)
    try {
        $normalized = New-NormalizedChip $source
        try {
            # Only the successful candidate is saved, once. Matching files are no-ops.
            $normalized.Save($chipFile.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
            Write-Output "$($chipFile.Name): normalized source PNG to 60x60 chip in 64x64 canvas"
        }
        finally { $normalized.Dispose() }
    }
    finally { $source.Dispose() }
}
