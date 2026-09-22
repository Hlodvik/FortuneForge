$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -Path "$PSScriptRoot\ResizeReferencedRasterAssets.cs" -ReferencedAssemblies 'System.Drawing.dll'

$assetRoot = (Resolve-Path "$PSScriptRoot\..\fortuneforge.client\src\assets\slots").Path

[ResizeReferencedRasterAssets]::ResizeDirectory(
  (Join-Path $assetRoot 'games\candy-carnival'),
  (Join-Path $assetRoot 'games\candy-carnival\optimized'),
  384)
[ResizeReferencedRasterAssets]::ResizeDirectory(
  (Join-Path $assetRoot 'games\gods-of-olympus'),
  (Join-Path $assetRoot 'games\gods-of-olympus\optimized'),
  384)
[ResizeReferencedRasterAssets]::Resize(
  (Join-Path $assetRoot 'games\gods-of-olympus\olympus-terrace.png'),
  (Join-Path $assetRoot 'games\gods-of-olympus\optimized\olympus-terrace.png'),
  768)
[ResizeReferencedRasterAssets]::ResizeDirectory(
  (Join-Path $assetRoot 'games\rainbow-realm'),
  (Join-Path $assetRoot 'games\rainbow-realm\optimized'),
  384)
[ResizeReferencedRasterAssets]::ResizeDirectory(
  (Join-Path $assetRoot 'symbols\wukong'),
  (Join-Path $assetRoot 'symbols\wukong\optimized'),
  384)

foreach ($assetName in 'celestial-lightning-bolt.png', 'free-game.png', 'rainbow-realm-power-coin.png') {
  [ResizeReferencedRasterAssets]::Resize(
    (Join-Path $assetRoot "symbols\$assetName"),
    (Join-Path $assetRoot "symbols\optimized\$assetName"),
    384)
}

foreach ($assetName in 'neon-jewel-clouds-gold.png', 'rainbow-realm-prismatic-orchard-v3-base.png') {
  [ResizeReferencedRasterAssets]::Resize(
    (Join-Path $assetRoot "backgrounds\$assetName"),
    (Join-Path $assetRoot "backgrounds\optimized\$assetName"),
    1024)
}

[ResizeReferencedRasterAssets]::Resize(
  (Join-Path $assetRoot 'backgrounds\rainbow-realm-pinwheel-flower.png'),
  (Join-Path $assetRoot 'backgrounds\optimized\rainbow-realm-pinwheel-flower.png'),
  384)
