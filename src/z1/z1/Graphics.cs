﻿using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace z1;

[Flags]
internal enum DrawingFlags
{
    None = 0,
    FlipHorizontal = 1 << 0,
    FlipVertical = 1 << 1,
    NoTransparency = 1 << 2,
}

internal enum TileSheet { Background, PlayerAndItems, Npcs, Boss, Font, Max }

internal static class Graphics
{
    private static SKSurface? _surface;

    private static readonly int _paletteBmpWidth = Math.Max(Global.PaletteLength, 16);
    private static readonly int _paletteBmpHeight = Math.Max(Global.PaletteCount, 16);

    private static readonly SKBitmap?[] _tileSheets = new SKBitmap[(int)TileSheet.Max];
    private static readonly byte[] _paletteBuf;
    private static readonly int _paletteStride = _paletteBmpWidth * Unsafe.SizeOf<SKColor>();
    private static readonly int[] _systemPalette = new int[Global.SysPaletteLength];
    private static readonly int[] _grayscalePalette = new int[Global.SysPaletteLength];
    private static int[] _activeSystemPalette = _systemPalette;
    private static readonly byte[] _palettes = new byte[Global.PaletteCount * Global.PaletteLength];

    public static ref byte GetPalette(Palette paletteIndex, int colorIndex) => ref _palettes[(int)paletteIndex * Global.PaletteLength + colorIndex];
    public static Span<byte> GetPalette(Palette paletteIndex) => MemoryMarshal.CreateSpan(ref _palettes[(int)paletteIndex * Global.PaletteLength], Global.PaletteLength);

    private static readonly TableResource<SpriteAnimationStruct>[] _animSpecs = new TableResource<SpriteAnimationStruct>[(int)TileSheet.Max];

    // SystemPalette is intentionally a referenced based compare for speed.
    private readonly record struct TileCache(TileSheet? Slot, SKBitmap? Bitmap, int[] SystemPalette, int X, int Y, Palette Palette, DrawingFlags Flags)
    {
        private static readonly Dictionary<TileCache, SKBitmap> _tileCache = new(200);
        private static readonly byte[] _alphaTransform = new byte[256];
        private static readonly byte[] _redTransform = new byte[256];
        private static readonly byte[] _greenTransform = new byte[256];
        private static readonly byte[] _blueTransform = new byte[256];

        // JOE: Arg. This makes me hate the tile cache even more.
        public static void Clear()
        {
            foreach (var (_, bitmap) in _tileCache) bitmap.Dispose();
            _tileCache.Clear();
        }

        public SKBitmap GetValue(int width, int height)
        {
            if (_tileCache.TryGetValue(this, out var tile)) return tile;

            var sheet = Bitmap ?? _tileSheets[(int)Slot] ?? throw new Exception();

            var makeTransparent = !Flags.HasFlag(DrawingFlags.NoTransparency);
            var paletteY = (int)Palette * _paletteBmpWidth;
            var paletteSpan = MemoryMarshal.Cast<byte, SKColor>(_paletteBuf.AsSpan())[paletteY..];

            var color0 = paletteSpan[0];
            var color1 = paletteSpan[1];
            var color2 = paletteSpan[2];
            var color3 = paletteSpan[3];

            _redTransform[0] = color0.Red;
            _redTransform[1] = color1.Red;
            _redTransform[2] = color2.Red;
            _redTransform[3] = color3.Red;

            _greenTransform[0] = color0.Green;
            _greenTransform[1] = color1.Green;
            _greenTransform[2] = color2.Green;
            _greenTransform[3] = color3.Green;

            _blueTransform[0] = color0.Blue;
            _blueTransform[1] = color1.Blue;
            _blueTransform[2] = color2.Blue;
            _blueTransform[3] = color3.Blue;

            if (makeTransparent)
            {
                _alphaTransform[0] = color0.Alpha;
                _alphaTransform[1] = color1.Alpha;
                _alphaTransform[2] = color2.Alpha;
                _alphaTransform[3] = color3.Alpha;
            }
            else
            {
                Array.Fill(_alphaTransform, (byte)255, 0, 4);
            }

            using var paint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateTable(_alphaTransform, _redTransform, _greenTransform, _blueTransform),
            };

            tile = sheet.Extract(X, Y, width, height, paint, Flags);
            return _tileCache[this] = tile;
        }
    }

    static Graphics()
    {
        var size = _paletteBmpWidth * _paletteStride * _paletteBmpHeight;
        _paletteBuf = new byte[size];
    }

    public static void SetSurface(SKSurface surface)
    {
        _surface = surface;
    }

    public static void Begin() { }
    public static void End() { }

    public static void LoadTileSheet(TileSheet sheet, Asset file)
    {
        var slot = (int)sheet;
        ref var foundRef = ref _tileSheets[slot];
        if (foundRef != null)
        {
            foundRef.Dispose();
            foundRef = null;
        }

        var bitmap = file.DecodeSKBitmap(SKAlphaType.Unpremul);

        PreprocessPalette(bitmap);
        _tileSheets[slot] = bitmap;
    }

    public static void LoadTileSheet(TileSheet sheet, Asset path, Asset animationFile)
    {
        LoadTileSheet(sheet, path);
        _animSpecs[(int)sheet] = TableResource<SpriteAnimationStruct>.Load(animationFile);
    }

    // Preprocesses an image to set all color channels to their appropriate color palette index, allowing
    // a transform to be done by SKColorFilter.
    private static unsafe void PreprocessPalette(SKBitmap bitmap)
    {
        // Unpremul is important here otherwise setting the alpha channel to non-255 causes the colors to transform
        if (bitmap.AlphaType != SKAlphaType.Unpremul) throw new ArgumentOutOfRangeException();

        var locked = bitmap.Lock();
        for (var y = 0; y < locked.Height; ++y)
        {
            var px = locked.PtrFromPoint(0, y);
            for (var x = 0; x < locked.Width; ++x, ++px)
            {
                var val = (byte)(px->Red / 16);
                var color = new SKColor(val, val, val, val);
                *px = color;
            }
        }
    }

    public static SpriteAnimation GetAnimation(TileSheet sheet, AnimationId id)
    {
        return _animSpecs[(int)sheet].LoadVariableLengthData<SpriteAnimation>((int)id);
    }

    public static void LoadSystemPalette(int[] colorsArgb8)
    {
        colorsArgb8.CopyTo(_systemPalette.AsSpan());

        for (var i = 0; i < Global.SysPaletteLength; i++)
        {
            _grayscalePalette[i] = _systemPalette[i & 0x30];
        }
    }

    public static SKColor GetSystemColor(int sysColor)
    {
        var argb8 = _activeSystemPalette[sysColor];
        return new SKColor(
            (byte)((argb8 >> 16) & 0xFF),
            (byte)((argb8 >> 8) & 0xFF),
            (byte)((argb8 >> 0) & 0xFF),
            (byte)((argb8 >> 24) & 0xFF)
        );
    }

    // TODO: this method has to consider the picture format
    public static void SetColor(Palette paletteIndex, int colorIndex, uint colorArgb8)
    {
        var y = (int)paletteIndex;
        var x = colorIndex;

        var line = MemoryMarshal.Cast<byte, uint>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);
        line[x] = colorArgb8;
    }

    public static void SetColor(Palette paletteIndex, int colorIndex, int colorArgb8) => SetColor(paletteIndex, colorIndex, (uint)colorArgb8);

    public static void SetPalette(Palette paletteIndex, ReadOnlySpan<int> colorsArgb8)
    {
        var y = (int)paletteIndex;
        var line = MemoryMarshal.Cast<byte, int>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);

        for (var x = 0; x < Global.PaletteLength; x++)
        {
            line[x] = colorsArgb8[x];
        }
    }

    public static void SetColorIndexed(Palette paletteIndex, int colorIndex, int sysColor)
    {
        var colorArgb8 = 0;
        if (colorIndex != 0)
        {
            colorArgb8 = _activeSystemPalette[sysColor];
        }
        SetColor(paletteIndex, colorIndex, colorArgb8);
        GetPalette(paletteIndex, colorIndex) = (byte)sysColor;
        TileCache.Clear();
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors)
    {
        var colorsArgb8 = new[]
        {
            0,
            _activeSystemPalette[sysColors[1]],
            _activeSystemPalette[sysColors[2]],
            _activeSystemPalette[sysColors[3]],
        };

        SetPalette(paletteIndex, colorsArgb8);
        var dest = GetPalette(paletteIndex);
        sysColors[..Global.PaletteLength].CopyTo(dest);
        TileCache.Clear();
    }

    public static void UpdatePalettes()
    {
        TileCache.Clear();
    }

    public static void SwitchSystemPalette(int[] newSystemPalette)
    {
        if (newSystemPalette == _activeSystemPalette) return;

        _activeSystemPalette = newSystemPalette;

        for (var i = 0; i < Global.PaletteCount; i++)
        {
            var sysColors = GetPalette((Palette)i);
            var colorsArgb8 = new[]
            {
                0,
                _activeSystemPalette[sysColors[1]],
                _activeSystemPalette[sysColors[2]],
                _activeSystemPalette[sysColors[3]],
            };
            SetPalette((Palette)i, colorsArgb8);
        }
        UpdatePalettes();
    }

    public static void EnableGrayscale()
    {
        SwitchSystemPalette(_grayscalePalette);
    }

    public static void DisableGrayscale()
    {
        SwitchSystemPalette(_systemPalette);
    }

    public static SpriteAnimator GetSpriteAnimator(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));
    public static SpriteImage GetSpriteImage(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));

    public static void DrawBitmap(
        SKBitmap? bitmap,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags
    )
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(_surface);

        var destRect = new SKRect(destX, destY, destX + width, destY + height);

        var cacheKey = new TileCache(null, bitmap, _activeSystemPalette, srcX, srcY, palette, flags);
        var tile = cacheKey.GetValue(width, height);
        _surface.Canvas.DrawBitmap(tile, destRect);
    }

    public static void DrawSpriteTile(
        TileSheet slot,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags
    )
    {
        DrawTile(slot, srcX, srcY, width, height, destX, destY + 1, palette, flags);
    }

    public static void DrawTile(
        TileSheet slot,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags
    )
    {
        ArgumentNullException.ThrowIfNull(_surface);
        Debug.Assert(slot < TileSheet.Max);

        var cacheKey = new TileCache(slot, null, _activeSystemPalette, srcX, srcY, palette, flags);
        var tile = cacheKey.GetValue(width, height);

        var destRect = new SKRect(destX, destY, destX + width, destY + height);
        _surface.Canvas.DrawBitmap(tile, destRect);
    }

    public static void DrawStripSprite16X16(TileSheet slot, int firstTile, int destX, int destY, Palette palette)
    {
        ReadOnlySpan<byte> OffsetsX = [0, 0, 8, 8];
        ReadOnlySpan<byte> OffsetsY = [0, 8, 0, 8];

        var tileRef = firstTile;

        for (var i = 0; i < 4; i++)
        {
            var srcX = (tileRef & 0x0F) * World.TileWidth;
            var srcY = ((tileRef & 0xF0) >> 4) * World.TileHeight;
            tileRef++;

            DrawTile(
                slot, srcX, srcY,
                World.TileWidth, World.TileHeight,
                destX + OffsetsX[i], destY + OffsetsY[i],
                palette, 0);
        }
    }

    public static void Clear(SKColor color)
    {
        ArgumentNullException.ThrowIfNull(_surface);
        _surface.Canvas.Clear(color);
    }

    public static void Clear(SKColor color, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(_surface);
        _surface.Canvas.Clear(color);
    }

    public readonly struct UnclipScope : IDisposable
    {
        public void Dispose() => ResetClip();
    }

    public static UnclipScope SetClip(int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(_surface);
        var y2 = y + height;

        if (y2 < 0)
        {
            height = 0;
            y = 0;
        }
        else if (y > Global.StdViewHeight)
        {
            height = 0;
            y = Global.StdViewHeight;
        }
        else
        {
            if (y < 0)
            {
                height += y;
                y = 0;
            }

            if (y2 > Global.StdViewHeight)
            {
                height = Global.StdViewHeight - y;
            }
        }

        _surface.Canvas.Save();
        _surface.Canvas.ClipRect(new SKRect(x, y, x + width, y + height));
        return new UnclipScope();
    }

    public static void ResetClip()
    {
        ArgumentNullException.ThrowIfNull(_surface);
        _surface.Canvas.Restore();
    }
}
