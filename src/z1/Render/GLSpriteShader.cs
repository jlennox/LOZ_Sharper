using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace z1.Render;

internal sealed class GLSpriteShader : IDisposable
{
    public static GLSpriteShader Instance { get; private set; } = null!;

    private readonly GL _gl;
    private readonly uint _program;

    private readonly int _lViewportSize;
    private readonly int _lTextureSize;
    private readonly int _lOpacity;
    private readonly int _lPalette;

    private GLSpriteShader(GL gl)
    {
        _gl = gl;
        Instance = this;

        _program = gl.CreateProgram();
        var vertex = LoadShader(gl, ShaderType.VertexShader, SpriteShaders.Vertex);
        var fragment = LoadShader(gl, ShaderType.FragmentShader, SpriteShaders.Fragment);
        gl.AttachShader(_program, vertex);
        gl.AttachShader(_program, fragment);
        gl.LinkProgram(_program);
        gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var status);
        if (status != 1) throw new Exception("Program failed to link: " + gl.GetProgramInfoLog(_program));
        gl.DetachShader(_program, vertex);
        gl.DetachShader(_program, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        _lViewportSize = GetUniformLocation("u_viewportSize");
        _lTextureSize = GetUniformLocation("u_size");
        _lOpacity = GetUniformLocation("u_opacity");
        _lPalette = GetUniformLocation("u_palette");

        Use(gl);
        gl.Uniform1(GetUniformLocation("u_texture"), 0);
    }

    public static void Initialize(GL gL)
    {
        Instance = new GLSpriteShader(gL);
    }

    private static uint LoadShader(GL gl, ShaderType type, string src)
    {
        var handle = gl.CreateShader(type);
        gl.ShaderSource(handle, src);
        gl.CompileShader(handle);

        var error = gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrEmpty(error))
        {
            throw new Exception($"Error compiling \"{type}\" shader: {error}");
        }

        return handle;
    }

    private int GetUniformLocation(string name)
    {
        var location = _gl.GetUniformLocation(_program, name);
        // if (location == -1) throw new Exception($"\"{name}\" uniform was not found on the shader");
        return location;
    }

    public void Use(GL gl)
    {
        _gl.UseProgram(_program);
    }

    public void SetViewport(int width, int height)
    {
        _gl.Uniform2(_lViewportSize, width, height);
    }

    public void SetPalette(ReadOnlySpan<SKColor> colors)
    {
        _gl.Uniform4(_lPalette, MemoryMarshal.Cast<SKColor, uint>(colors));
    }

    public void SetOpacity(float f)
    {
        _gl.Uniform1(_lOpacity, f);
    }

    public void SetTextureSize(int width, int height)
    {
        _gl.Uniform2(_lTextureSize, width, height);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_program);
    }
}
