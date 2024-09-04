using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace z1.Render;

internal sealed unsafe class GLImage
{
    private readonly Size _tileSize;
    public uint Texture { get; }
    public Size Size { get; }

    public GLImage(GL gl, SKBitmap bitmap, Size tileSize)
    {
        _tileSize = tileSize;
        Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, Texture);
        LoadTextureData(gl, bitmap, bitmap.Width, bitmap.Height);
        Size = new Size(bitmap.Width, bitmap.Height);
    }

    private void LoadTextureData(GL gl, SKBitmap bitmap, int width, int height)
    {
        if (bitmap.BytesPerPixel != 4) throw new ArgumentException("Bitmap must be 4 bytes per pixel.", nameof(bitmap));

        var bitmapLock = bitmap.Lock();

        const int bytesPerPixel = 4;
        var tileWidth = _tileSize.Width;
        var tileHeight = _tileSize.Height;
        var tilesX = width / _tileSize.Width;
        var tilesY = height / _tileSize.Height;
        var tileCount = tilesX * tilesY;
        var tileWidthByteCount = tileWidth * bytesPerPixel;
        var bytesPerStride = bitmapLock.Stride;
        var tileByteCount = tileWidthByteCount * tileHeight;

        gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.Rgba8,
            (uint)tileWidth, (uint)tileHeight, (uint)tileCount, 0,
            PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);

        var singleTilePtrBase = stackalloc byte[tileByteCount];
        var dataPtrBase = (byte*)bitmapLock.Data;

        var tileIndex = 0;
        for (var y = 0; y < tilesY; y++)
        {
            var dataPtrRow = dataPtrBase + y * bytesPerStride * tileHeight;
            for (var x = 0; x < tilesX; x++)
            {
                var singleTilePtr = singleTilePtrBase;
                var dataPtr = dataPtrRow;
                for (var yx = 0; yx < tileHeight; yx++, singleTilePtr += tileWidthByteCount, dataPtr += bytesPerStride)
                {
                    Unsafe.CopyBlock(singleTilePtr, dataPtr, (uint)tileWidthByteCount);
                }

                gl.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0,
                    tileIndex, (uint)tileWidth, (uint)tileHeight, 1,
                    PixelFormat.Bgra, PixelType.UnsignedByte, singleTilePtrBase);

                dataPtrRow += tileWidthByteCount;
                tileIndex++;
            }
        }

        gl.TexParameterI(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorderNV);
        gl.TexParameterI(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorderNV);
        gl.TexParameterI(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameterI(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
    }

    public void Render(GL gl, int srcx, int srcy, int width, int height, ReadOnlySpan<SKColor> palette, Size viewportSize, Point destination, DrawingFlags flags)
    {
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2DArray, Texture);

        var x = srcx / width;
        var y = srcy / height;
        var tileindex = y * (Size.Width / width) + x;

        var destRect = new Rectangle(destination.X, destination.Y, width, height);

        var shader = GUIRectShader.Instance;
        shader.Use(gl);
        shader.UpdateViewport(gl, new Size(256, 240));
        shader.SetRect(gl, destRect);
        shader.SetCornerRadii(gl, default);
        shader.SetLineThickness(gl, 0);
        shader.SetOpacity(gl, 1f);
        shader.SetUseTexture(gl, true);
        shader.SetUV(gl, new UV(flags.HasFlag(DrawingFlags.FlipHorizontal), flags.HasFlag(DrawingFlags.FlipVertical)));
        shader.SetTextureLayer(gl, tileindex);
        shader.SetPalette(gl, palette, flags.HasFlag(DrawingFlags.NoTransparency));
        RectMesh.Instance.Render(gl);
    }

    public void Delete(GL gl)
    {
        gl.DeleteTexture(Texture);
    }
}

internal sealed class RectMesh
{
    private const int NUM_VERTICES = 4;

    public static RectMesh Instance { get; set; } = null!; // Set in RenderManager

    private readonly uint _vao;
    private readonly uint _vbo;

    public unsafe RectMesh(GL gl)
    {
        // Create vao
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        // Create vbo
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (void* vertices = CreateVertices())
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, 2 * sizeof(float) * NUM_VERTICES, vertices, BufferUsageARB.StaticDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), null);
    }
    private static Vector2[] CreateVertices()
    {
        return [
            new Vector2(0, 0), // Top Left
            new Vector2(0, 1), // Bottom Left
            new Vector2(1, 0), // Top Right
            new Vector2(1, 1)  // Bottom Right
        ];
    }

    public void Render(GL gl)
    {
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, NUM_VERTICES);
    }
    public void Render(GL gl, int x, int y)
    {
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, NUM_VERTICES);
    }

    public void RenderInstanced(GL gl, uint instanceCount)
    {
        gl.BindVertexArray(_vao);
        gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, NUM_VERTICES, instanceCount);
    }
    public void RenderInstancedBaseInstance(GL gl, uint first, uint instanceCount)
    {
        gl.BindVertexArray(_vao);
        gl.DrawArraysInstancedBaseInstance(PrimitiveType.TriangleStrip, 0, NUM_VERTICES, instanceCount, first); // Requires OpenGL 4.2 or above. Not available in OpenGL ES
    }

    public void Delete(GL gl)
    {
        gl.DeleteVertexArray(_vao);
        gl.DeleteBuffer(_vbo);
    }
}