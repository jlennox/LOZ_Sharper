using Silk.NET.OpenGL;

namespace z1.Render;

internal sealed class GLFramebuffer : IDisposable
{
    public Size Size { get; }

    private readonly GL _gl;
    private readonly uint _framebuffer;
    private readonly uint _texture;

    public unsafe GLFramebuffer(GL gl, Size size)
    {
        _gl = gl;
        Size = size;

        _texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)size.Width, (uint)size.Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texture, 0);

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete) throw new Exception($"Framebuffer is incomplete: {status}");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)Size.Width, (uint)Size.Height);
    }

    public void Unbind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    // Scaling happens here as a single operation so that every source pixel maps to a consistent run of
    // destination pixels, instead of each sprite quad rounding its own texel boundaries independently.
    public void BlitToScreen(Rectangle destination)
    {
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        _gl.BlitFramebuffer(
            0, 0, Size.Width, Size.Height,
            destination.X, destination.Y, destination.X + destination.Width, destination.Y + destination.Height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_framebuffer);
        _gl.DeleteTexture(_texture);
    }
}
