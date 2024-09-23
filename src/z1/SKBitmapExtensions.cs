using SkiaSharp;
using z1.Render;

namespace z1;

internal static class SKBitmapExtensions
{
    internal readonly unsafe record struct SKBitmapLock(nint Data, int Stride, int Width, int Height)
    {
        public int Length => Stride * Height;
        public SKColor* Pixels => (SKColor*)Data;
        public SKColor* End => (SKColor*)(Data + Length);
        public SKColor* PtrFromPoint(int x, int y) => (SKColor*)(Data + y * Stride + x * sizeof(SKColor));
    }

    public static SKBitmapLock Lock(this SKBitmap bitmap)
    {
        return new SKBitmapLock(bitmap.GetPixels(), bitmap.RowBytes, bitmap.Width, bitmap.Height);
    }

    public static SKBitmap Extract(this SKBitmap bitmap, int x, int y, int width, int height, SKPaint? paint = null, DrawingFlags flags = DrawingFlags.None)
    {
        var flip = flags.HasFlag(DrawingFlags.FlipY);
        var mirror = flags.HasFlag(DrawingFlags.FlipX);

        var dest = new SKBitmap(width, height, bitmap.ColorType, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(dest);
        if (flip || mirror)
        {
            var mirrorMatrix = SKMatrix.CreateScale(mirror ? -1 : 1, flip ? -1 : 1);
            if (flip) mirrorMatrix.TransY = height;
            if (mirror) mirrorMatrix.TransX = width;
            canvas.SetMatrix(mirrorMatrix);
        }
        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height), new SKRect(0, 0, width, height), paint);
        return dest;
    }

    public static void SavePng(this SKBitmap bitmap, string path, int quality = 100)
    {
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, quality))
        using (var outputStream = File.OpenWrite(path))
        {
            data.SaveTo(outputStream);
        }
    }

    public static void SavePng(this SKCanvas canvas, string path, int quality = 100)
    {
        canvas.GetDeviceClipBounds(out var rect);
        using var bitmap = new SKBitmap(rect.Width, rect.Height);
        bitmap.SavePng(path, quality);
    }
}