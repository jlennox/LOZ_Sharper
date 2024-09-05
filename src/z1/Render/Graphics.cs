using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
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
    private static GL? _gl;
    private static Size? _windowSize;
    private static readonly Size _viewportSize = new(256, 240);

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

    static Graphics()
    {
        var size = _paletteBmpWidth * _paletteStride * _paletteBmpHeight;
        _paletteBuf = new byte[size];
    }

    public static void SetSurface(GL gl, int width, int height)
    {
        _gl = gl;
        _windowSize = new Size(width, height);
    }

    public static void SetViewportSize(int width, int height)
    {
        _windowSize = new Size(width, height);
    }

    public static void Begin() { }
    public static void End() { }

    public static void LoadTileSheet(TileSheet sheet, Asset file)
    {
        ArgumentNullException.ThrowIfNull(_gl);

        ref var foundRef = ref _tileSheets[(int)sheet];
        if (foundRef != null)
        {
            foundRef.Dispose();
            foundRef = null;
        }

        var bitmap = file.DecodeSKBitmapTileData();
        foundRef = new GLImage(_gl, bitmap);
    }

    public static void LoadTileSheet(TileSheet sheet, Asset path, Asset animationFile)
    {
        LoadTileSheet(sheet, path);
        _animSpecs[(int)sheet] = TableResource<SpriteAnimationStruct>.Load(animationFile);
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
        ArgumentNullException.ThrowIfNull(_gl);
        ArgumentNullException.ThrowIfNull(bitmap);

        var destRect = new SKRect(destX, destY, destX + width, destY + height);

        // var cacheKey = new TileCache(null, bitmap, _activeSystemPalette, srcX, srcY, palette, flags);
        // var tile = cacheKey.GetValue(width, height);

        // tile.Render(_gl, srcX, srcY, width, height, _windowSize.Value, new Point(destX, destY));
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
        Debug.Assert(slot < TileSheet.Max);

        var tiles = _tileSheets[(int)slot]
            ?? throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown or unloaded tile.");
        var paletteY = (int)palette * _paletteBmpWidth;
        var paletteSpan = MemoryMarshal.Cast<byte, SKColor>(_paletteBuf.AsSpan())[paletteY..(paletteY + 4)];
        tiles.Render(srcX, srcY, width, height, destX, destY, paletteSpan, _viewportSize, flags);
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
        ArgumentNullException.ThrowIfNull(_gl);
        _gl.ClearColor(color.ToDrawingColor());
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));
    }

    public readonly struct UnclipScope : IDisposable
    {
        public void Dispose() => ResetClip();
    }

    public static UnclipScope SetClip(int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(_gl);

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

        // _surface.Canvas.Save();
        // _surface.Canvas.ClipRect(new SKRect(x, y, x + width, y + height));
        // var xratio = _windowSize.Value.Width / Global.StdViewWidth;
        // var yratio = _windowSize.Value.Height / Global.StdViewHeight;
        // _gl.Enable(EnableCap.ScissorTest);
        // _gl.Scissor(x * xratio, y * yratio, (uint)width * (uint)xratio, (uint)height * (uint)yratio);
        return new UnclipScope();
    }

    public static void ResetClip()
    {
        ArgumentNullException.ThrowIfNull(_gl);
        _gl.Disable(EnableCap.ScissorTest);
        // _surface.Canvas.Restore();
    }
}
