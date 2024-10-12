using System.Collections.Immutable;
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
    FlipX = 1 << 0,
    FlipY = 1 << 1,
    NoTransparency = 1 << 2,
}

internal enum TileSheet
{
    BackgroundOverworld,
    BackgroundUnderworld,
    PlayerAndItems,
    NpcsOverworld,
    NpcsUnderworld,
    Boss1257,
    Boss3468,
    Boss9,
    Font,
}

internal sealed class ImageSheet
{
    public TileSheet Sheet { get; }
    public GLImage Image { get; }
    public TableResource<SpriteAnimationStruct>? Animation { get; }

    public ImageSheet(TileSheet sheet, Asset imageFile, Asset animationFile)
        : this(sheet, imageFile)
    {
        Animation = animationFile.IsEmpty ? null : TableResource<SpriteAnimationStruct>.Load(animationFile);
    }

    public ImageSheet(TileSheet sheet, Asset imageFile)
    {
        Sheet = sheet;
        Image = Graphics.CreateImage(imageFile);
    }
}

internal sealed class GraphicSheets
{
    // TODO: Make this immutable.
    public List<ImageSheet> Sheets { get; } = new();

    public void AddSheets(ReadOnlySpan<ImageSheet> sheets)
    {
        Sheets.AddRange(sheets);
    }

    public GLImage GetImage(TileSheet sheet)
    {
        var imagesheet = Sheets.First(t => t.Sheet == sheet);
        return imagesheet.Image;
    }

    public TableResource<SpriteAnimationStruct> GetAnimation(TileSheet sheet)
    {
        var imagesheet = Sheets.First(t => t.Sheet == sheet);
        return imagesheet.Animation ?? throw new Exception();
    }
}

internal static class Graphics
{
    private static GL? _gl;
    private static Size? _windowSize;
    private static readonly Size _viewportSize = new(256, 240);

    private static readonly int _paletteBmpWidth = Math.Max(Global.PaletteLength, 16);
    private static readonly int _paletteBmpHeight = Math.Max(Global.PaletteCount, 16);

    private static readonly byte[] _paletteBuf;
    private static readonly int _paletteStride = _paletteBmpWidth * Unsafe.SizeOf<SKColor>();
    private static readonly uint[] _systemPalette = new uint[Global.SysPaletteLength];
    private static readonly uint[] _grayscalePalette = new uint[Global.SysPaletteLength];
    private static uint[] _activeSystemPalette = _systemPalette;
    private static readonly byte[] _palettes = new byte[Global.PaletteCount * Global.PaletteLength];

    public static ref byte GetPalette(Palette paletteIndex, int colorIndex) => ref _palettes[(int)paletteIndex * Global.PaletteLength + colorIndex];
    public static Span<byte> GetPalette(Palette paletteIndex) => MemoryMarshal.CreateSpan(ref _palettes[(int)paletteIndex * Global.PaletteLength], Global.PaletteLength);
    private static ReadOnlySpan<SKColor> GetPaletteColors(Palette palette)
    {
        var paletteY = (int)palette * _paletteBmpWidth;
        return MemoryMarshal.Cast<byte, SKColor>(_paletteBuf.AsSpan())[paletteY..(paletteY + 4)];
    }

    public static GraphicSheets GraphicSheets { get; } = new();

    static Graphics()
    {
        var size = _paletteBmpWidth * _paletteStride * _paletteBmpHeight;
        _paletteBuf = new byte[size];
    }

    public static void Initialize(GL gl)
    {
        _gl = gl;
        GLSpriteShader.Initialize(gl);
        GLVertexArray.Initialize(gl);

        var sysPal = new Asset("Palette.json").ReadJson<uint[]>();
        LoadSystemPalette(sysPal);

        GraphicSheets.AddSheets([
            new ImageSheet(TileSheet.Font, new Asset("font.png")),
            new ImageSheet(TileSheet.BackgroundUnderworld, new Asset("underworldTiles.png")),
            new ImageSheet(TileSheet.BackgroundOverworld, new Asset("overworldTiles.png")),
            new ImageSheet(TileSheet.PlayerAndItems, new Asset("playerItem.png"), new Asset("playerItemsSheet.tab")),
            new ImageSheet(TileSheet.Boss1257, new Asset("uwBoss1257.png"), new Asset("uwBossSheet1257.tab")),
            new ImageSheet(TileSheet.Boss3468, new Asset("uwBoss3468.png"), new Asset("uwBossSheet3468.tab")),
            new ImageSheet(TileSheet.Boss9, new Asset("uwBoss9.png"), new Asset("uwBossSheet9.tab")),
            new ImageSheet(TileSheet.NpcsOverworld, new Asset("owNpcs.png"), new Asset("owNpcsSheet.tab")),
            new ImageSheet(TileSheet.NpcsUnderworld, new Asset("uwNpcs.png"), new Asset("uwNpcsSheet.tab")),
        ]);
    }

    public static void StartRender()
    {
        ArgumentNullException.ThrowIfNull(_gl);

        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));

        GLSpriteShader.Instance.SetViewport(_viewportSize.Width, _viewportSize.Height);
    }

    public static void SetWindowSize(int width, int height)
    {
        _windowSize = new Size(width, height);
    }

    public static GLImage CreateImage(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(_gl);
        return new GLImage(_gl, asset.DecodeSKBitmapTileData());
    }

    public static void Begin() { }
    public static void End() { }

    public static SpriteAnimation GetAnimation(TileSheet sheet, AnimationId id)
    {
        var animation = GraphicSheets.GetAnimation(sheet);
        return animation.LoadVariableLengthData<SpriteAnimation>((int)id);
    }

    public static void LoadSystemPalette(uint[] colorsArgb8)
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

    public static void SetPalette(Palette paletteIndex, ReadOnlySpan<uint> colorsArgb8)
    {
        var y = (int)paletteIndex;
        var line = MemoryMarshal.Cast<byte, uint>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);

        for (var x = 0; x < Global.PaletteLength; x++)
        {
            line[x] = colorsArgb8[x];
        }
    }

    public static void SetColorIndexed(Palette paletteIndex, int colorIndex, int sysColor)
    {
        uint colorArgb8 = 0;
        if (colorIndex != 0)
        {
            colorArgb8 = _activeSystemPalette[sysColor];
        }
        SetColor(paletteIndex, colorIndex, colorArgb8);
        GetPalette(paletteIndex, colorIndex) = (byte)sysColor;
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ImmutableArray<byte> sysColors)
    {
        SetPaletteIndexed(paletteIndex, sysColors.AsSpan());
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors)
    {
        ReadOnlySpan<uint> colorsArgb8 =
        [
            0,
            _activeSystemPalette[sysColors[1]],
            _activeSystemPalette[sysColors[2]],
            _activeSystemPalette[sysColors[3]],
        ];

        SetPalette(paletteIndex, colorsArgb8);
        var dest = GetPalette(paletteIndex);
        sysColors[..Global.PaletteLength].CopyTo(dest);
    }

    public static void UpdatePalettes()
    {
    }

    public static void SwitchSystemPalette(uint[] newSystemPalette)
    {
        if (newSystemPalette == _activeSystemPalette) return;

        _activeSystemPalette = newSystemPalette;

        for (var i = 0; i < Global.PaletteCount; i++)
        {
            var sysColors = GetPalette((Palette)i);
            ReadOnlySpan<uint> colorsArgb8 =
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

    public static void DrawImage(
        GLImage? bitmap,
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

        bitmap.Draw(srcX, srcY, width, height, destX, destY, GetPaletteColors(palette), flags);
    }

    public static void DrawSpriteTile(
        TileSheet sheet,
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
        DrawTile(sheet, srcX, srcY, width, height, destX, destY + 1, palette, flags);
    }

    public static void DrawTile(
        TileSheet sheet,
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
        var tiles = GraphicSheets.GetImage(sheet);
        tiles.Draw(srcX, srcY, width, height, destX, destY, GetPaletteColors(palette), flags);
    }

    public static void DrawTile(
        TileSheet sheet,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        ReadOnlySpan<SKColor> palette,
        DrawingFlags flags
    )
    {
        var tiles = GraphicSheets.GetImage(sheet);
        tiles.Draw(srcX, srcY, width, height, destX, destY, palette, flags);
    }

    public static void DrawStripSprite16X16(TileSheet sheet, BlockType firstTile, int destX, int destY, Palette palette)
    {
        DrawStripSprite16X16(sheet, (int)firstTile, destX, destY, palette);
    }

    public static void DrawStripSprite16X16(TileSheet sheet, TileType firstTile, int destX, int destY, Palette palette)
    {
        DrawStripSprite16X16(sheet, (int)firstTile, destX, destY, palette);
    }

    public static void DrawStripSprite16X16(TileSheet sheet, int firstTile, int destX, int destY, Palette palette)
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
                sheet, srcX, srcY,
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

    private static bool _clipped = false;

    public static UnclipScope SetClip(int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(_gl);
        ArgumentNullException.ThrowIfNull(_windowSize);

        if (_clipped) throw new Exception();
        _clipped = true;

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

        var xratio = _windowSize.Value.Width / (float)_viewportSize.Width;
        var yratio = _windowSize.Value.Height / (float)_viewportSize.Height;
        var finalWidth = width * xratio;
        var finalHeight = height * yratio;
        var finalX = x * xratio;
        var finalY = _windowSize.Value.Height - finalHeight - y * yratio;
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor((int)finalX, (int)finalY, (uint)finalWidth, (uint)finalHeight);
        return new UnclipScope();
    }

    public static void ResetClip()
    {
        ArgumentNullException.ThrowIfNull(_gl);
        ArgumentNullException.ThrowIfNull(_windowSize);

        _gl.Disable(EnableCap.ScissorTest);
        _clipped = false;
    }
}
