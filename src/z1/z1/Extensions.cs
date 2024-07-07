using System.Reflection.Metadata.Ecma335;
using SkiaSharp;

namespace z1;

internal unsafe static class Extensions
{
    public static SKBitmap Mirror(this SKBitmap bitmap)
    {
        var mirroredBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

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
        var mirroredBitmap = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

        using (var canvas = new SKCanvas(mirroredBitmap))
        {
            var mirrorMatrix = SKMatrix.CreateScale(1, -1);
            mirrorMatrix.TransY = bitmap.Height;
            canvas.SetMatrix(mirrorMatrix);
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        return mirroredBitmap;
    }

    public static SKBitmap[] Mirror(this SKBitmap[] bitmaps)
    {
        var newbitmaps = new SKBitmap[bitmaps.Length];
        for (int i = 0; i < bitmaps.Length; i++) {
            newbitmaps[i] = bitmaps[i].Mirror();
        }
        return newbitmaps;
    }

    public static SKBitmap[] Flip(this SKBitmap[] bitmaps)
    {
        var newbitmaps = new SKBitmap[bitmaps.Length];
        for (int i = 0; i < bitmaps.Length; i++) {
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

    internal readonly unsafe record struct SKBitmapLock(nint Data, int Stride, int Length)
    {
        public SKColor* Pixels => (SKColor*)Data;
        public SKColor* End => (SKColor*)Data + Length;
        public SKColor* PtrFromPoint(int x, int y) => (SKColor*)(Data + y * Stride + x * sizeof(SKColor));
    }

    public static unsafe SKBitmapLock Lock(this SKBitmap bitmap)
    {
        return new SKBitmapLock(bitmap.GetPixels(), bitmap.RowBytes, bitmap.RowBytes * bitmap.Height);
    }

    public static unsafe SKBitmap Extract(this SKBitmap bitmap, int x, int y, int width, int height)
    {
        var dest = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(dest);
        canvas.DrawBitmap(bitmap, new SKRect(x, y, x + width, y + height), new SKRect(0, 0, width, height));
        return dest.Mask(Sprites.TransparentMask);
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

    public static bool IsHorizontal(this Direction direction, Direction? mask = null)
    {
        return (direction & Direction.FullMask) is Direction.Left or Direction.Right;
    }

    public static bool IsVertical(this Direction direction, Direction mask = Direction.FullMask)
    {
        return (direction & Direction.FullMask) is Direction.Up or Direction.Down;
    }

    public static bool IsGrowing(this Direction direction) => direction is Direction.Right or Direction.Down;
    public static int GetOrdinal(this Direction direction)
    {
        // [sic] ORIGINAL: the original game goes in the opposite order.
        var dir = (int)direction;
        for (int i = 0; i < 4; i++)
        {
            if ((dir & 1) != 0)
                return i;
            dir = dir >> 1;
        }

        return 0;
    }

    public static Direction GetOrdDirection(this int ord)
    {
        // [sic] ORIGINAL: the original game goes in the opposite order.
        return (Direction)(1 << ord);
    }

    public static byte GetByte(this Random random) => (byte)random.Next(256);
}
