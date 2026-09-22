using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class ResizeReferencedRasterAssets
{
    public static void ResizeDirectory(string sourceDirectory, string destinationDirectory, int maxDimension)
    {
        foreach (var sourcePath in Directory.GetFiles(sourceDirectory, "*.*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
            {
                Resize(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), maxDimension);
            }
        }
    }

    public static void Resize(string sourcePath, string destinationPath, int maxDimension)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        using (var sourceStream = new MemoryStream(sourceBytes))
        using (var loadedSource = new Bitmap(sourceStream))
        using (var source = new Bitmap(loadedSource))
        {
            var scale = Math.Min(1d, maxDimension / (double)Math.Max(source.Width, source.Height));
            var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

            using (var target = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight));

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                target.Save(destinationPath, ImageFormat.Png);
            }
        }
    }
}
