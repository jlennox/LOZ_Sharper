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

// This is very large for a struct, which is very questionable. Profiling is needed.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct DrawRequest
{
    public required int SrcX { get; init; }
    public required int SrcY { get; init; }
    public required int SrcRight { get; init; }
    public required int SrcBottom { get; init; }
    public required int DestX { get; init; }
    public required int DestY { get; init; }
    public required SKColor PaletteA { get; init; }
    public required SKColor PaletteB { get; init; }
    public required SKColor PaletteC { get; init; }
    public required SKColor PaletteD { get; init; }
    public required GLImage Image { get; init; }
}

internal sealed class ImageSheet
{
    public TileSheet Sheet { get; }
    public TableResource<SpriteAnimationStruct>? Animation { get; }

    private readonly Asset _imageFile;
    private GraphicsImage? _image;

    public ImageSheet(TileSheet sheet, Asset imageFile, Asset animationFile)
        : this(sheet, imageFile)
    {
        Animation = animationFile.IsEmpty ? null : TableResource<SpriteAnimationStruct>.Load(animationFile);
    }

    public ImageSheet(TileSheet sheet, Asset imageFile)
    {
        _imageFile = imageFile;
        Sheet = sheet;
    }

    public GraphicsImage GetImage(Graphics graphics)
    {
        _image ??= graphics.CreateImage(_imageFile);
        return _image;
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

    public GraphicsImage GetImage(Graphics graphics, TileSheet sheet)
    {
        var imagesheet = Sheets.First(t => t.Sheet == sheet);
        return imagesheet.GetImage(graphics);
    }

    public TableResource<SpriteAnimationStruct> GetAnimation(TileSheet sheet)
    {
        var imagesheet = Sheets.First(t => t.Sheet == sheet);
        return imagesheet.Animation ?? throw new Exception();
    }
}

internal readonly struct UnclipScope(Graphics graphics) : IDisposable
{
    public void Dispose() => graphics.ResetClip();
}

internal abstract class Graphics
{
    public static GraphicSheets GraphicSheets { get; } = new();
    public static readonly PriorityQueue<DrawRequest, DrawOrder> DrawRequests = new();
    public static bool ImmediateRenderer { get; set; } // To make using RenderDoc easier.

    public abstract void StartRender();
    public abstract void SetWindowSize(int width, int height);
    public abstract GraphicsImage CreateImage(Asset asset);
    public abstract void Begin();
    public abstract void End();
    public abstract void FinishRender();
    public abstract void UpdatePalettes();
    public abstract void DrawImage(GraphicsImage? bitmap, int srcX, int srcY, int width, int height, int destX, int destY, Palette palette, DrawingFlags flags, DrawOrder order);
    public abstract void DrawSpriteTile(TileSheet sheet, int srcX, int srcY, int width, int height, int destX, int destY, Palette palette, DrawingFlags flags, DrawOrder order);
    public abstract void DrawTile(TileSheet sheet, int srcX, int srcY, int width, int height, int destX, int destY, Palette palette, DrawingFlags flags, DrawOrder order);
    public abstract void DrawTile(TileSheet sheet, int srcX, int srcY, int width, int height, int destX, int destY, ReadOnlySpan<SKColor> palette, DrawingFlags flags, DrawOrder order);
    public abstract void DrawStripSprite16X16(TileSheet sheet, BlockType firstTile, int destX, int destY, Palette palette, DrawOrder order);
    public abstract void DrawStripSprite16X16(TileSheet sheet, TileType firstTile, int destX, int destY, Palette palette, DrawOrder order);
    public abstract void DrawStripSprite16X16(TileSheet sheet, int firstTile, int destX, int destY, Palette palette, DrawOrder order);
    public abstract void Clear(SKColor color);
    public abstract UnclipScope SetClip(int x, int y, int width, int height);
    public abstract void ResetClip();

    public static SpriteAnimation GetAnimation(TileSheet sheet, AnimationId id)
    {
        var animation = GraphicSheets.GetAnimation(sheet);
        return animation.LoadVariableLengthData<SpriteAnimation>((int)id);
    }

    public static SpriteAnimator GetSpriteAnimator(TileSheet sheet, AnimationId id) => new(sheet, id);
    public static SpriteImage GetSpriteImage(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));

    static Graphics()
    {
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
}

internal sealed class NullGraphics : Graphics
{
    public override void StartRender() { }
    public override void SetWindowSize(int width, int height) { }
    public override GraphicsImage CreateImage(Asset asset) => new NullImage();
    public override void Begin() { }
    public override void End() { }
    public override void FinishRender() { }
    public override void UpdatePalettes() { }
    public override void DrawImage(GraphicsImage? bitmap, int srcX, int srcY, int width, int height, int destX, int destY, Palette palette, DrawingFlags flags, DrawOrder order) { }
    public override void DrawSpriteTile(TileSheet sheet, int srcX, int srcY, int width, int height, int destX, int destY, Palette palette, DrawingFlags flags, DrawOrder order) { }
    public override void DrawTile(TileSheet sheet, int srcX, int srcY, int width, int height, int destX, int destY, Palette palette, DrawingFlags flags, DrawOrder order) { }
    public override void DrawTile(TileSheet sheet, int srcX, int srcY, int width, int height, int destX, int destY, ReadOnlySpan<SKColor> palette, DrawingFlags flags, DrawOrder order) { }
    public override void DrawStripSprite16X16(TileSheet sheet, BlockType firstTile, int destX, int destY, Palette palette, DrawOrder order) { }
    public override void DrawStripSprite16X16(TileSheet sheet, TileType firstTile, int destX, int destY, Palette palette, DrawOrder order) { }
    public override void DrawStripSprite16X16(TileSheet sheet, int firstTile, int destX, int destY, Palette palette, DrawOrder order) { }
    public override void Clear(SKColor color) { }
    public override UnclipScope SetClip(int x, int y, int width, int height) => new(this);
    public override void ResetClip() { }
}

internal sealed class GLGraphics : Graphics
{
    private readonly GL _gl;
    private Size? _windowSize;
    private readonly Size _viewportSize = new(256, 240);

    public GLGraphics(GL gl)
    {
        _gl = gl;
        GLSpriteShader.Initialize(gl);
        GLVertexArray.Initialize(gl);

        var sysPal = new Asset("Palette.json").ReadJson<uint[]>();
        GraphicPalettes.LoadSystemPalette(sysPal);
    }

    public override void StartRender()
    {
        ArgumentNullException.ThrowIfNull(_gl);

        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));

        GLSpriteShader.Instance.SetViewport(_viewportSize.Width, _viewportSize.Height);
    }

    public override void SetWindowSize(int width, int height)
    {
        _windowSize = new Size(width, height);
    }

    public override GraphicsImage CreateImage(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(_gl);
        return new GLImage(_gl, asset.DecodeSKBitmapTileData());
    }

    public override void Begin() { }
    public override void End() { }

    public override void FinishRender()
    {
        // These should ultimately batch all the vertex and drawing together.
        while (DrawRequests.Count > 0)
        {
            var request = DrawRequests.Dequeue();
            request.Image.Draw(ref request);
        }
    }

    public override void UpdatePalettes()
    {
    }

    public override void DrawImage(
        GraphicsImage? bitmap,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags,
        DrawOrder order
    )
    {
        ArgumentNullException.ThrowIfNull(_gl);
        ArgumentNullException.ThrowIfNull(bitmap);

        bitmap.Draw(srcX, srcY, width, height, destX, destY, GraphicPalettes.GetPaletteColors(palette), flags, order);
    }

    public override void DrawSpriteTile(
        TileSheet sheet,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags,
        DrawOrder order
    )
    {
        DrawTile(sheet, srcX, srcY, width, height, destX, destY + 1, palette, flags, order);
    }

    public override void DrawTile(
        TileSheet sheet,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags,
        DrawOrder order
    )
    {
        var tiles = GraphicSheets.GetImage(this, sheet);
        tiles.Draw(srcX, srcY, width, height, destX, destY, GraphicPalettes.GetPaletteColors(palette), flags, order);
    }

    public override void DrawTile(
        TileSheet sheet,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        ReadOnlySpan<SKColor> palette,
        DrawingFlags flags,
        DrawOrder order
    )
    {
        var tiles = GraphicSheets.GetImage(this, sheet);
        tiles.Draw(srcX, srcY, width, height, destX, destY, palette, flags, order);
    }

    public override void DrawStripSprite16X16(TileSheet sheet, BlockType firstTile, int destX, int destY, Palette palette, DrawOrder order)
    {
        DrawStripSprite16X16(sheet, (int)firstTile, destX, destY, palette, order);
    }

    public override void DrawStripSprite16X16(TileSheet sheet, TileType firstTile, int destX, int destY, Palette palette, DrawOrder order)
    {
        DrawStripSprite16X16(sheet, (int)firstTile, destX, destY, palette, order);
    }

    public override void DrawStripSprite16X16(TileSheet sheet, int firstTile, int destX, int destY, Palette palette, DrawOrder order)
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
                palette, 0, order);
        }
    }

    public override void Clear(SKColor color)
    {
        ArgumentNullException.ThrowIfNull(_gl);
        _gl.ClearColor(color.ToDrawingColor());
        _gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));
    }

    private bool _clipped = false;

    public override UnclipScope SetClip(int x, int y, int width, int height)
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
        return new UnclipScope(this);
    }

    public override void ResetClip()
    {
        ArgumentNullException.ThrowIfNull(_gl);
        ArgumentNullException.ThrowIfNull(_windowSize);

        // This is really not my favorite way to do this. This _should_ be handled by the shader, but we'd need to
        // manage what the clipping for each render entry is.
        // If we do need to batch these, a good pattern might be:
        // List<(ClippingRect? Rect, PriorityQueue<....>>
        FinishRender();
        _gl.Disable(EnableCap.ScissorTest);
        _clipped = false;
    }
}
