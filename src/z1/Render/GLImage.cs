using System.Numerics;
using Silk.NET.OpenGL;
using SkiaSharp;
using z1.IO;

namespace z1.Render;

internal enum DrawOrder
{
    BehindBackground,
    Background,
    Sprites,
    Player,
    Foreground,
    Overlay,
    OverlayForeground,
}

internal sealed unsafe class GLImage : IDisposable
{
    public int Width => _size.Width;
    public int Height => _size.Height;

    private readonly GL _gl;
    private readonly uint _texture;
    private readonly Size _size;

    public GLImage(GL gl, Asset bitmap) : this(gl, bitmap.DecodeSKBitmapTileData())
    {
    }

    public GLImage(GL gl, SKBitmap bitmap)
    {
        _gl = gl;
        _texture = gl.GenTexture();
        _size = new Size(bitmap.Width, bitmap.Height);
        LoadTextureData(bitmap);
    }

    private void LoadTextureData(SKBitmap bitmap)
    {
        if (bitmap.BytesPerPixel != 4) throw new ArgumentException("Bitmap must be 4 bytes per pixel.", nameof(bitmap));

        var bitmapLock = bitmap.Lock();

        _gl.BindTexture(TextureTarget.Texture2D, _texture);

        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)bitmap.Width, (uint)bitmap.Height, 0,
            PixelFormat.Bgra, PixelType.UnsignedByte, (void*)bitmapLock.Data);

        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
    }

    public void Draw(
        int srcX, int srcY, int width, int height,
        int destX, int destY,
        ReadOnlySpan<SKColor> palette, DrawingFlags flags,
        DrawOrder layer)
    {
        var right = srcX + width;
        var bottom = srcY + height;

        // Branchless conditional swaps:
        // https://github.com/jlennox/Benchmarks/blob/main/BranchlessSwap.cs
        var flipHor = -BitOperations.PopCount((uint)(flags & DrawingFlags.FlipX));
        var flipVert = -BitOperations.PopCount((uint)(flags & DrawingFlags.FlipY));

        var tempSrcX = (flipHor & right) | (~flipHor & srcX);
        var tempRight = (flipHor & srcX) | (~flipHor & right);
        srcX = tempSrcX;
        right = tempRight;

        var tempSrcY = (flipVert & bottom) | (~flipVert & srcY);
        var tempBottom = (flipVert & srcY) | (~flipVert & bottom);
        srcY = tempSrcY;
        bottom = tempBottom;

        var finalPalette = palette;

        // if (!flags.HasFlag(DrawingFlags.NoTransparency))
        {
            // JOE: TODO: Arg. I do not know why this broke? It behaves the same in the original code, down
            // to the exact same palette which has 00 for the alpha, but it works there.
            var pal0 = palette[0];
            finalPalette = stackalloc SKColor[] {
                flags.HasFlag(DrawingFlags.NoTransparency)
                    ? new SKColor(pal0.Red, pal0.Green, pal0.Blue, 0xFF)
                    : new SKColor(0),
                palette[1],
                palette[2],
                palette[3],
            };
        }

        var request = new DrawRequest
        {
            SrcX = srcX,
            SrcY = srcY,
            SrcRight = right,
            SrcBottom = bottom,
            DestX = destX,
            DestY = destY,
            PaletteA = finalPalette[0],
            PaletteB = finalPalette[1],
            PaletteC = finalPalette[2],
            PaletteD = finalPalette[3],
            Image = this,
        };


        if (Graphics.ImmediateRenderer)
        {
            Draw(ref request);
        }
        else
        {
            Graphics.DrawRequests.Enqueue(request, layer);
        }
    }

    // Ref to avoid big struct copy. TODO: Actually profile this.
    public void Draw(ref DrawRequest request)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texture);

        // TODO: These abs aint great.
        var width = Math.Abs(request.SrcRight - request.SrcX);
        var height = Math.Abs(request.SrcBottom - request.SrcY);

        ReadOnlySpan<Point> verticies = stackalloc Point[] {
            new Point(request.SrcX, request.SrcY), new Point(request.DestX, request.DestY),
            new Point(request.SrcX, request.SrcBottom), new Point(request.DestX, request.DestY + height),
            new Point(request.SrcRight, request.SrcY), new Point(request.DestX + width, request.DestY),
            new Point(request.SrcRight, request.SrcBottom), new Point(request.DestX + width, request.DestY + height),
        };

        ReadOnlySpan<SKColor> palette = stackalloc SKColor[] {
            request.PaletteA,
            request.PaletteB,
            request.PaletteC,
            request.PaletteD,
        };

        _gl.BufferData(
            BufferTargetARB.ArrayBuffer, (nuint)verticies.Length * (nuint)sizeof(Point),
            verticies, BufferUsageARB.StreamDraw);

        var shader = GLSpriteShader.Instance;
        shader.Use();
        shader.SetTextureSize(_size.Width, _size.Height);
        shader.SetOpacity(1f);
        shader.SetPalette(palette);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, (uint)verticies.Length / 2);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_texture);
    }
}

internal sealed class GLVertexArray : IDisposable
{
    public static GLVertexArray Instance { get; private set; } = null!;

    private readonly GL _gl;
    private readonly uint _vertexArrayObject;
    private readonly uint _vertextBufferObject;

    private unsafe GLVertexArray(GL gl)
    {
        _gl = gl;
        _vertexArrayObject = gl.GenVertexArray();
        gl.BindVertexArray(_vertexArrayObject);

        _vertextBufferObject = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertextBufferObject);

        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribIPointer(0, 2, VertexAttribIType.Int, (uint)sizeof(Point) * 2, null);
        gl.VertexAttribIPointer(1, 2, VertexAttribIType.Int, (uint)sizeof(Point) * 2, sizeof(Point));

        gl.Disable(EnableCap.DepthTest);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public static void Initialize(GL gl)
    {
        Instance = new GLVertexArray(gl);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vertexArrayObject);
        _gl.DeleteBuffer(_vertextBufferObject);
    }
}