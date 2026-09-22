using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class GenerateNauticalStarEmblem
{
    public static void Create(string largePath, string smallPath, string tagPath)
    {
        var large = Draw(512);
        large.Save(largePath, ImageFormat.Png);
        var small = Draw(64);
        small.Save(smallPath, ImageFormat.Png);
        var tag = Draw(32);
        tag.Save(tagPath, ImageFormat.Png);
    }

    private static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.Transparent);

        float scale = size / 512f;
        var center = new PointF(size / 2f, size / 2f);
        var star = MakeStar(center, 213f * scale, 98f * scale);
        var starPath = MakePath(star);

        var fill = new SolidBrush(Color.FromArgb(18, 18, 20));
        var outerInk = new Pen(Color.FromArgb(10, 10, 12), 40f * scale) { LineJoin = LineJoin.Round };
        var goldEdge = new Pen(Color.FromArgb(236, 183, 57), 25f * scale) { LineJoin = LineJoin.Round };
        var innerInk = new Pen(Color.FromArgb(18, 18, 20), 5f * scale) { LineJoin = LineJoin.Round };

        graphics.FillPath(fill, starPath);
        graphics.DrawPath(outerInk, starPath);
        graphics.DrawPath(goldEdge, starPath);
        graphics.DrawPath(innerInk, starPath);

        graphics.SetClip(starPath);
        var ivory = new SolidBrush(Color.FromArgb(255, 251, 235));
        var inkFacet = new SolidBrush(Color.FromArgb(24, 21, 18));
        var points = MakeStar(center, 202f * scale, 90f * scale);

        for (var point = 0; point < 5; point++)
        {
            var outer = points[point * 2];
            var trailingInner = points[(point * 2 + 1) % points.Length];
            var leadingInner = points[(point * 2 - 1 + points.Length) % points.Length];
            graphics.FillPolygon(ivory, new[] { center, outer, trailingInner });
            graphics.FillPolygon(inkFacet, new[] { center, leadingInner, outer });
        }

        graphics.ResetClip();
        var hubInk = new SolidBrush(Color.FromArgb(12, 12, 14));
        var hubGold = new SolidBrush(Color.FromArgb(239, 189, 62));
        var hubLight = new SolidBrush(Color.FromArgb(255, 247, 215));
        float hubRadius = 30f * scale;
        graphics.FillEllipse(hubInk, center.X - hubRadius - 8f * scale, center.Y - hubRadius - 8f * scale, (hubRadius + 8f * scale) * 2, (hubRadius + 8f * scale) * 2);
        graphics.FillEllipse(hubGold, center.X - hubRadius, center.Y - hubRadius, hubRadius * 2, hubRadius * 2);
        graphics.FillEllipse(hubLight, center.X - hubRadius * .42f, center.Y - hubRadius * .48f, hubRadius * .7f, hubRadius * .7f);

        return bitmap;
    }

    private static PointF[] MakeStar(PointF center, float outerRadius, float innerRadius)
    {
        var points = new PointF[10];
        for (var index = 0; index < points.Length; index++)
        {
            double radians = (-90d + index * 36d) * Math.PI / 180d;
            float radius = index % 2 == 0 ? outerRadius : innerRadius;
            points[index] = new PointF(
                center.X + radius * (float)Math.Cos(radians),
                center.Y + radius * (float)Math.Sin(radians));
        }
        return points;
    }

    private static GraphicsPath MakePath(PointF[] points)
    {
        var path = new GraphicsPath();
        path.AddPolygon(points);
        return path;
    }
}
