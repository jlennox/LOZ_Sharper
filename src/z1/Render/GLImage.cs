using System.Numerics;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace z1.Render;

internal sealed unsafe class GLImage : IDisposable
{
    private readonly GL _gl;
    private readonly uint _texture;
    private readonly Size _size;

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

    public void Render(
        int srcx, int srcy, int width, int height,
        int destinationx, int destinationy,
        ReadOnlySpan<SKColor> palette, Size viewportSize,
        DrawingFlags flags)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texture);

        var sizeWidthf = (float)_size.Width;
        var sizeHeightf = (float)_size.Height;

        var left = srcx / sizeWidthf;
        var top = srcy / sizeHeightf;
        var right = (srcx + width) / sizeWidthf;
        var bottom = (srcy + height) / sizeHeightf;

        ReadOnlySpan <Vector2> verticies = stackalloc Vector2[4] {
            new Vector2(left, top),
            new Vector2(left, bottom),
            new Vector2(right, top),
            new Vector2(right, bottom),
        };

        var finalPalette = palette;

        if (!flags.HasFlag(DrawingFlags.NoTransparency))
        {
            finalPalette = stackalloc SKColor[4] {
                new SKColor(0),
                palette[1],
                palette[2],
                palette[3],
            };
        }

        _gl.BufferData(
            BufferTargetARB.ArrayBuffer, (nuint)verticies.Length * (nuint)sizeof(Vector2),
            verticies, BufferUsageARB.StreamDraw);

        var shader = GLSpriteShader.Instance;
        shader.Use(_gl);
        shader.SetViewport(viewportSize.Width, viewportSize.Height);
        shader.SetDestination(destinationx, destinationy, _size.Width, _size.Height);
        shader.SetSourcePosition(srcx, srcy);
        shader.SetOpacity(1f);
        shader.SetUV(new UV(false, false)); // flags.HasFlag(DrawingFlags.FlipHorizontal), flags.HasFlag(DrawingFlags.FlipVertical)));
        shader.SetPalette(finalPalette);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, (uint)verticies.Length);
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
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vector2), null);

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