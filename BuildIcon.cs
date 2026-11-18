using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class BuildIcon
{
    // The cloud follows the Codex silhouette. Rendering it here keeps the build self-contained.
    private static readonly PointF[] CloudOutline =
    {
        new PointF(95.1F, 52F), new PointF(95.5F, 57.7F), new PointF(94.6F, 63.4F), new PointF(92.4F, 68.7F), new PointF(88.7F, 73.2F), new PointF(84.1F, 76.6F),
        new PointF(79F, 79F), new PointF(76.6F, 84.1F), new PointF(73.2F, 88.7F), new PointF(68.7F, 92.4F), new PointF(63.4F, 94.6F), new PointF(57.7F, 95.5F),
        new PointF(52F, 95.2F), new PointF(46.6F, 93.1F), new PointF(41.9F, 89.9F), new PointF(36.4F, 89.7F), new PointF(30.5F, 89.3F), new PointF(24.8F, 87.4F),
        new PointF(20F, 84F), new PointF(16.2F, 79.5F), new PointF(13.6F, 74.1F), new PointF(12.6F, 68.3F), new PointF(13.3F, 62.4F), new PointF(10.4F, 57.5F),
        new PointF(7.6F, 52F), new PointF(6.7F, 46F), new PointF(7.5F, 40.1F), new PointF(9.6F, 34.4F), new PointF(13.1F, 29.6F), new PointF(17.9F, 25.8F),
        new PointF(23.4F, 23.4F), new PointF(25.8F, 17.8F), new PointF(29.5F, 13.1F), new PointF(34.4F, 9.6F), new PointF(40.1F, 7.5F), new PointF(46F, 6.6F),
        new PointF(52F, 7.7F), new PointF(57.5F, 10.5F), new PointF(62.4F, 13.4F), new PointF(68.3F, 12.6F), new PointF(74.1F, 13.6F), new PointF(79.4F, 16.3F),
        new PointF(83.8F, 20.2F), new PointF(87.3F, 24.9F), new PointF(89.3F, 30.5F), new PointF(89.6F, 36.4F), new PointF(89.6F, 41.9F), new PointF(93F, 46.6F)
    };

    private static void Main(string[] args)
    {
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var frames = new List<byte[]>(sizes.Length);
        foreach (int size in sizes)
        using (var frame = Render(size))
            frames.Add(EncodeDib(frame));

        using (var ico = new BinaryWriter(File.Create(args[0])))
        {
            ico.Write((ushort)0);
            ico.Write((ushort)1);
            ico.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                ico.Write((byte)(size == 256 ? 0 : size));
                ico.Write((byte)(size == 256 ? 0 : size));
                ico.Write((byte)0);
                ico.Write((byte)0);
                ico.Write((ushort)1);
                ico.Write((ushort)32);
                ico.Write(frames[i].Length);
                ico.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames)
                ico.Write(frame);
        }
    }

    private static Bitmap Render(int size)
    {
        var icon = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(icon))
        using (var cloud = new GraphicsPath())
        using (var gradient = new LinearGradientBrush(new PointF(41, 6), new PointF(53, 96),
            Color.FromArgb(172, 159, 255), Color.FromArgb(54, 56, 255)))
        using (var rim = new Pen(Color.FromArgb(165, 205, 215, 255), 1.05F))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = size / 94F;
            graphics.ScaleTransform(scale, scale);
            graphics.TranslateTransform(-4, -4);
            cloud.AddClosedCurve(CloudOutline, 0.36F);
            gradient.InterpolationColors = new ColorBlend
            {
                Positions = new[] { 0F, .25F, .52F, .77F, 1F },
                Colors = new[]
                {
                    Color.FromArgb(172, 159, 255), Color.FromArgb(155, 154, 255),
                    size <= 24 ? Color.FromArgb(98, 130, 250) : Color.FromArgb(116, 151, 255),
                    Color.FromArgb(75, 92, 255), Color.FromArgb(54, 56, 255)
                }
            };
            graphics.FillPath(gradient, cloud);
            graphics.DrawPath(rim, cloud);

            var hands = new[] { new PointF(51, 28), new PointF(51, 54), new PointF(73, 43) };
            float width = 6.7F;
            float edgeWidth = 2F;
            if (size <= 24)
            {
                // Align the minute hand to a pixel center so both hands stay readable on the title bar.
                graphics.ResetTransform();
                float centerX = size / 2F - 0.5F;
                hands = new[]
                {
                    new PointF(centerX, size * 0.24F),
                    new PointF(centerX, size * 0.56F),
                    new PointF(size * 0.74F, size * 0.42F)
                };
                width = Math.Max(1.9F, size * 0.095F);
                edgeWidth = 0.55F;
            }
            using (var edge = Stroke(Color.FromArgb(145, 44, 62, 177), width + edgeWidth))
            using (var face = Stroke(Color.FromArgb(249, 250, 255), width))
            {
                graphics.DrawLines(edge, hands);
                graphics.DrawLines(face, hands);
            }
        }
        return icon;
    }

    private static Pen Stroke(Color color, float width)
    {
        return new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    }

    private static byte[] EncodeDib(Bitmap bitmap)
    {
        int size = bitmap.Width;
        int maskStride = ((size + 31) / 32) * 4;
        var mask = new byte[maskStride * size];
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            for (int field = 0; field < 6; field++)
                writer.Write(0);

            for (int row = 0; row < size; row++)
            for (int column = 0; column < size; column++)
            {
                Color pixel = bitmap.GetPixel(column, size - 1 - row);
                writer.Write(pixel.B);
                writer.Write(pixel.G);
                writer.Write(pixel.R);
                writer.Write(pixel.A);
                if (pixel.A == 0)
                    mask[row * maskStride + column / 8] |= (byte)(128 >> (column % 8));
            }
            writer.Write(mask);
            writer.Flush();
            return stream.ToArray();
        }
    }

}
