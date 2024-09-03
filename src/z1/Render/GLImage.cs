using System.Numerics;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace z1.Render;

internal sealed unsafe class GLImage
{
    public uint Texture { get; }
    public Size Size { get; }

    public GLImage(GL gl, SKBitmap bitmap)
    {
        Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, Texture);
        LoadTextureData(gl, bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height);
        Size = new Size(bitmap.Width, bitmap.Height);
    }

    public static void LoadTextureData(GL gl, ReadOnlySpan<byte> data, int width, int height)
    {
        fixed (byte* dataPtr = data)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, dataPtr);

            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }
    }

    public void Render(GL gl, int srcx, int srcy, int width, int height, Size viewportSize, Point destination, bool xFlip = false, bool yFlip = false)
    {
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, Texture);

        // gl.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, srcx, srcy, (uint)width, (uint)height);
        // gl.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 2, 2, (uint)Size.Width, (uint)Size.Height);

        // var destRect = new Rectangle(destination, Size);
        var destRect = new Rectangle(destination.X, destination.Y, width, height);

        var shader = GUIRectShader.Instance;
        shader.Use(gl);
        shader.UpdateViewport(gl, new Size(256, 240));
        shader.SetRect(gl, destRect);
        shader.SetCornerRadii(gl, default);
        shader.SetLineThickness(gl, 0);
        shader.SetOpacity(gl, 1f);
        shader.SetUseTexture(gl, true);
        shader.SetUV(gl, new UV(xFlip, yFlip));
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