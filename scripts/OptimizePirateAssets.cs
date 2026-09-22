using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class OptimizePirateAssets
{
    private static readonly string[] RootAssetNames =
    {
        "amber-gem.png",
        "compass.png",
        "cutlass.png",
        "doubloon-purse.png",
        "emerald-gem.png",
        "flintlock-pistol.png",
        "gold-doubloon.png",
        "gold-pirate-coin.png",
        "jolly-roger.png",
        "pearl.png",
        "pirates-cove.png",
        "powder-kegs.png",
        "ruby-gem.png",
        "rum-bottle-v2.png",
        "sapphire-gem.png",
        "treasure-map.png",
        "treasure-map-bonus.png",
    };

    public static void BuildDeliveryAssets(string assetRoot)
    {
        var outputRoot = Path.Combine(assetRoot, "optimized");
        foreach (var assetName in RootAssetNames)
        {
            var maximumDimension = assetName == "pirates-cove.png" ? 1024 : 384;
            Resize(Path.Combine(assetRoot, assetName), Path.Combine(outputRoot, assetName), maximumDimension);
        }

        foreach (var gemType in new[] { "emerald", "lapis", "ruby", "topaz" })
        {
            foreach (var level in new[] { "empty", "level-1", "level-2", "level-3", "level-4" })
            {
                var relativePath = Path.Combine("chests", gemType, level + ".png");
                Resize(Path.Combine(assetRoot, relativePath), Path.Combine(outputRoot, relativePath), 384);
            }
        }
    }

    private static void Resize(string sourcePath, string outputPath, int maxDimension)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var sourceStream = new MemoryStream(sourceBytes);
        var loadedSource = new Bitmap(sourceStream);
        var source = new Bitmap(loadedSource);
        loadedSource.Dispose();
        sourceStream.Dispose();
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(source.Width, source.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

        var target = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        var graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight));
        graphics.Dispose();
        source.Dispose();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        target.Save(outputPath, ImageFormat.Png);
        target.Dispose();
    }
}
