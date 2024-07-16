using SkiaSharp;

namespace z1;

internal static unsafe class SKBitmapExtensions
{
    public static SKBitmap Mirror(this SKBitmap bitmap)
    {
        var mirroredBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        using (var canvas = new SKCanvas(mirroredBitmap))
        {
            var mirrorMatrix = SKMatrix.CreateScale(-1, 1);
            mirrorMatrix.TransX = bitmap.Width;
            canvas.SetMatrix(mirrorMatrix);
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        return mirroredBitmap;
    }

    public static SKBitmap Flip(this SKBitmap bitmap)
    {
        var mirroredBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        using (var canvas = new SKCanvas(mirroredBitmap))
        {
            var mirrorMatrix = SKMatrix.CreateScale(1, -1);
            mirrorMatrix.TransY = bitmap.Height;
            canvas.SetMatrix(mirrorMatrix);
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        return mirroredBitmap;
    }

    // JOE: TODO: This is way too costly to run on each draw.
    public static SKBitmap ApplyFlags(this SKBitmap bitmap, DrawingFlags flags)
    {
        if (flags == DrawingFlags.None) return bitmap;
        var flip = flags.HasFlag(DrawingFlags.FlipVertical);
        var mirror = flags.HasFlag(DrawingFlags.FlipHorizontal);

        var mirroredBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        using (var canvas = new SKCanvas(mirroredBitmap))
        {
            var mirrorMatrix = SKMatrix.CreateScale(mirror ? -1 : 1, flip ? -1 : 1);
            mirrorMatrix.TransY = bitmap.Height;
            canvas.SetMatrix(mirrorMatrix);
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        return mirroredBitmap;
    }

    public static SKBitmap[] Mirror(this SKBitmap[] bitmaps)
    {
        var newbitmaps = new SKBitmap[bitmaps.Length];
        for (int i = 0; i < bitmaps.Length; i++)
        {
            newbitmaps[i] = bitmaps[i].Mirror();
        }
        return newbitmaps;
    }

    public static SKBitmap[] Flip(this SKBitmap[] bitmaps)
    {
        var newbitmaps = new SKBitmap[bitmaps.Length];
        for (int i = 0; i < bitmaps.Length; i++)
        {
            newbitmaps[i] = bitmaps[i].Flip();
        }
        return newbitmaps;
    }

    public static SKBitmap ChangePalette(this SKBitmap bitmap, SKColor[] from, SKColor[] to)
    {
        if (from.Length != to.Length) throw new ArgumentException("from and to must have the same length");

        bitmap = bitmap.Copy();
        var locked = bitmap.Lock();
        var pixels = locked.Pixels;
        var end = locked.End;

        fixed (SKColor* fromPtr = from, toPtr = to)
        {
            var fromEnd = fromPtr + from.Length;
            for (var px = pixels; pixels < end; ++px)
            {
                var fromCurrentPtr = fromPtr;
                for (var j = 0; j < from.Length; ++j, ++fromCurrentPtr)
                {
                    if (*px == *fromCurrentPtr)
                    {
                        *px = toPtr[j];
                        break;
                    }
                }
            }
        }

        return bitmap;
    }

    internal readonly unsafe record struct SKBitmapLock(nint Data, int Stride, int Width, int Height)
    {
        public int Length => Stride * Height;
        public SKColor* Pixels => (SKColor*)Data;
        public SKColor* End => (SKColor*)(Data + Length);
        public Span<byte> Bytes => new((byte*)Data, Length);
        public Span<SKColor> Colors => new(Pixels, Length / sizeof(SKColor));
        public SKColor* PtrFromPoint(int x, int y) => (SKColor*)(Data + y * Stride + x * sizeof(SKColor));
    }

    public static unsafe SKBitmapLock Lock(this SKBitmap bitmap)
    {
        return new SKBitmapLock(bitmap.GetPixels(), bitmap.RowBytes, bitmap.Width, bitmap.Height);
    }

    public static unsafe SKBitmap Extract(this SKBitmap bitmap, int x, int y, int width, int height, SKPaint? paint = null, DrawingFlags flags = DrawingFlags.None)
    {
        var flip = flags.HasFlag(DrawingFlags.FlipVertical);
        var mirror = flags.HasFlag(DrawingFlags.FlipHorizontal);

        var dest = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
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

    public static unsafe SKBitmap Mask(this SKBitmap bitmap, SKColor mask)
    {
        var locked = bitmap.Lock();
        var pixels = locked.Pixels;
        for (var px = pixels; px < locked.End; ++px)
        {
            if (*px == mask)
            {
                *px = SKColors.Transparent;
            }
        }

        return bitmap;
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