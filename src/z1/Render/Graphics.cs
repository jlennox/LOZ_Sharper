using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Silk.NET.OpenGL;
using SkiaSharp;
using z1.IO;

namespace z1.Render;

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
    private static GL? _gl;
    private static Size? _viewportSize;

    private static readonly int _paletteBmpWidth = Math.Max(Global.PaletteLength, 16);
    private static readonly int _paletteBmpHeight = Math.Max(Global.PaletteCount, 16);

    private static readonly GLImage?[] _tileSheets = new GLImage[(int)TileSheet.Max];
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
    // private readonly record struct TileCache(TileSheet? Slot, SKBitmap? Bitmap, int[] SystemPalette, int X, int Y, Palette Palette, DrawingFlags Flags)
    // {
    //     private static readonly Dictionary<TileCache, GLImage> _tileCache = new(200);
    //     private static readonly Vector256<int> _zeroCheck = Vector256.Create(0);
    //     private static readonly Vector256<int> _oneCheck = Vector256.Create(0x01010101);
    //     private static readonly Vector256<int> _twoCheck = Vector256.Create(0x02020202);
    //     private static readonly Vector256<int> _threeCheck = Vector256.Create(0x03030303);
    //
    //     // JOE: Arg. This makes me hate the tile cache even more.
    //     public static void Clear()
    //     {
    //         foreach (var (_, bitmap) in _tileCache) bitmap.Delete(_gl!);
    //         _tileCache.Clear();
    //     }
    //
    //     public unsafe GLImage GetValue(int width, int height)
    //     {
    //         if (_tileCache.TryGetValue(this, out var image)) return image;
    //
    //         var sheet = Bitmap ?? _tileSheets[(int)Slot] ?? throw new Exception();
    //
    //         var makeTransparent = !Flags.HasFlag(DrawingFlags.NoTransparency);
    //         var paletteY = (int)Palette * _paletteBmpWidth;
    //         var paletteSpan = MemoryMarshal.Cast<byte, SKColor>(_paletteBuf.AsSpan())[paletteY..];
    //
    //         var tile = sheet.Extract(X, Y, width, height, null, Flags);
    //
    //         var color0 = makeTransparent ? SKColors.Transparent : paletteSpan[0];
    //         var color1 = paletteSpan[1];
    //         var color2 = paletteSpan[2];
    //         var color3 = paletteSpan[3];
    //
    //         var locked = tile.Lock();
    //         var px = locked.Pixels;
    //         var nextLineDistance = locked.Stride / sizeof(SKColor) - width;
    //         var eightMultiple = width % 8 == 0;
    //
    //         // Benchmarks of various methods: https://gist.github.com/jlennox/41b2992a78a3d9a6c39fe3f8eadaab8e
    //
    //         if (nextLineDistance == 0 && eightMultiple)
    //         {
    //             var end = locked.End;
    //
    //             if (Avx2.IsSupported)
    //             {
    //                 var zeroCheck = _zeroCheck;
    //                 var oneCheck = _oneCheck;
    //                 var twoCheck = _twoCheck;
    //                 var threeCheck = _threeCheck;
    //
    //                 var zeroColor = Vector256.Create(*(int*)&color0);
    //                 var oneColor = Vector256.Create(*(int*)&color1);
    //                 var twoColor = Vector256.Create(*(int*)&color2);
    //                 var threeColor = Vector256.Create(*(int*)&color3);
    //
    //                 for (; px < end; px += 8)
    //                 {
    //                     var pixelVector = Vector256.Load((int*)px);
    //
    //                     var compareZero = Avx2.CompareEqual(pixelVector, zeroCheck);
    //                     var compareOne = Avx2.CompareEqual(pixelVector, oneCheck);
    //                     var compareTwo = Avx2.CompareEqual(pixelVector, twoCheck);
    //                     var compareThree = Avx2.CompareEqual(pixelVector, threeCheck);
    //
    //                     var blended = Avx2.BlendVariable(pixelVector, zeroColor, compareZero);
    //                     blended = Avx2.BlendVariable(blended, oneColor, compareOne);
    //                     blended = Avx2.BlendVariable(blended, twoColor, compareTwo);
    //                     blended = Avx2.BlendVariable(blended, threeColor, compareThree);
    //                     Avx.Store((int*)px, blended);
    //                 }
    //
    //                 return _tileCache[this] = new GLImage(_gl!, tile);
    //             }
    //
    //             var paletteUnrolled = stackalloc SKColor[] { color0, color1, color2, color3 };
    //             for (; px < end; px += 8)
    //             {
    //                 // Blue is the fastest to access because it does not use shifts.
    //                 px[0] = paletteUnrolled[px[0].Blue];
    //                 px[1] = paletteUnrolled[px[1].Blue];
    //                 px[2] = paletteUnrolled[px[2].Blue];
    //                 px[3] = paletteUnrolled[px[3].Blue];
    //                 px[4] = paletteUnrolled[px[4].Blue];
    //                 px[5] = paletteUnrolled[px[5].Blue];
    //                 px[6] = paletteUnrolled[px[6].Blue];
    //                 px[7] = paletteUnrolled[px[7].Blue];
    //             }
    //
    //             return _tileCache[this] = new GLImage(_gl!, tile);
    //         }
    //
    //         var palette = stackalloc SKColor[] { color0, color1, color2, color3 };
    //         for (var y = 0; y < locked.Height; ++y, px += nextLineDistance)
    //         {
    //             for (var x = 0; x < locked.Width; ++x, ++px)
    //             {
    //                 // Blue is the fastest to access because it does not use shifts.
    //                 *px = palette[px->Blue];
    //             }
    //         }
    //
    //         return _tileCache[this] = new GLImage(_gl!, tile);
    //     }
    // }

    static Graphics()
    {
        var size = _paletteBmpWidth * _paletteStride * _paletteBmpHeight;
        _paletteBuf = new byte[size];
    }

    public static void SetSurface(GL gl, SKSurface surface, int width, int height)
    {
        _gl = gl;
        _surface = surface;
        _viewportSize = new Size(width, height);
    }

    public static void SetViewportSize(int width, int height)
    {
        _viewportSize = new Size(width, height);
    }

    public static void Begin() { }
    public static void End() { }

    public static void LoadTileSheet(TileSheet sheet, Asset file)
    {
        ref var foundRef = ref _tileSheets[(int)sheet];
        if (foundRef != null)
        {
            foundRef.Delete(_gl!);
            foundRef = null;
        }

        var bitmap = file.DecodeSKBitmapTileData();
        foundRef = new GLImage(_gl!, bitmap);
    }

    public static void LoadTileSheet(TileSheet sheet, Asset path, Asset animationFile)
    {
        LoadTileSheet(sheet, path);
        _animSpecs[(int)sheet] = TableResource<SpriteAnimationStruct>.Load(animationFile);
    }

    // public static SKImage PaletteTileSheet(TileSheet sheet, Palette palette)
    // {
    //     var image = _tileSheets[(int)sheet];
    //     var cacheKey = new TileCache(null, image, _activeSystemPalette, 0, 0, palette, DrawingFlags.None);
    //     return cacheKey.GetValue(image.Width, image.Height);
    // }

    // Preprocesses an image to set all color channels to their appropriate color palette index, allowing
    // palette transformations to be done faster at runtime.
    public static unsafe void PreprocessPalette(SKBitmap bitmap)
    {
        return;
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
            (byte)(argb8 >> 16 & 0xFF),
            (byte)(argb8 >> 8 & 0xFF),
            (byte)(argb8 >> 0 & 0xFF),
            (byte)(argb8 >> 24 & 0xFF)
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
        // TileCache.Clear();
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ImmutableArray<byte> sysColors)
    {
        SetPaletteIndexed(paletteIndex, sysColors.AsSpan());
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors)
    {
        ReadOnlySpan<int> colorsArgb8 =
        [
            0,
            _activeSystemPalette[sysColors[1]],
            _activeSystemPalette[sysColors[2]],
            _activeSystemPalette[sysColors[3]],
        ];

        SetPalette(paletteIndex, colorsArgb8);
        var dest = GetPalette(paletteIndex);
        sysColors[..Global.PaletteLength].CopyTo(dest);
        // TileCache.Clear();
    }

    public static void UpdatePalettes()
    {
        // TileCache.Clear();
    }

    public static void SwitchSystemPalette(int[] newSystemPalette)
    {
        if (newSystemPalette == _activeSystemPalette) return;

        _activeSystemPalette = newSystemPalette;

        for (var i = 0; i < Global.PaletteCount; i++)
        {
            var sysColors = GetPalette((Palette)i);
            ReadOnlySpan<int> colorsArgb8 =
            [
                0,
                _activeSystemPalette[sysColors[1]],
                _activeSystemPalette[sysColors[2]],
                _activeSystemPalette[sysColors[3]],
            ];
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

    public static SpriteAnimator GetSpriteAnimator(TileSheet sheet, AnimationId id) => new(sheet, id);
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

        // var cacheKey = new TileCache(null, bitmap, _activeSystemPalette, srcX, srcY, palette, flags);
        // var tile = cacheKey.GetValue(width, height);

        // tile.Render(_gl!, srcX, srcY, width, height, _viewportSize.Value, new Point(destX, destY));
        // _surface.Canvas.DrawImage(tile, destRect);
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

        // var cacheKey = new TileCache(slot, null, _activeSystemPalette, srcX, srcY, palette, flags);
        // var tile = cacheKey.GetValue(width, height);

        var destRect = new SKRect(destX, destY, destX + width, destY + height);
        // _surface.Canvas.DrawImage(tile, destRect);
        var tiles = _tileSheets[(int)slot];
        tiles.Render(_gl!, srcX, srcY, width, height, _viewportSize.Value, new Point(destX, destY));
    }

    public static void DrawStripSprite16X16(TileSheet slot, int firstTile, int destX, int destY, Palette palette)
    {
        ReadOnlySpan<byte> offsetsX = [0, 0, 8, 8];
        ReadOnlySpan<byte> offsetsY = [0, 8, 0, 8];

        var tileRef = firstTile;

        for (var i = 0; i < 4; i++)
        {
            var srcX = (tileRef & 0x0F) * World.TileWidth;
            var srcY = ((tileRef & 0xF0) >> 4) * World.TileHeight;
            tileRef++;

            DrawTile(
                slot, srcX, srcY,
                World.TileWidth, World.TileHeight,
                destX + offsetsX[i], destY + offsetsY[i],
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

    public static void DebugDumpTiles()
    {
        Asset.Initialize();

        // var sysPal = ListResource<int>.LoadList(new Asset("pal.dat"), Global.SysPaletteLength).ToArray();
        // LoadSystemPalette(sysPal);
        //
        // LoadTileSheet(TileSheet.PlayerAndItems, new Asset("overworldTilesDebug.png"));
        // PaletteTileSheet(TileSheet.PlayerAndItems, Palette.LevelFgPalette).SavePng(@"C:\users\joe\desktop\delete\_z1.png");
    }
}
