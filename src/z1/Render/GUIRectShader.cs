using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace z1.Render;

internal sealed class GUIRectShader : Shader2D
{
    public static GUIRectShader Instance { get; private set; } = null!; // Set in RenderManager

    private readonly int _lPos;
    private readonly int _lSize;
    private readonly int _lCornerRadii;
    private readonly int _lLineThickness;
    private readonly int _lOpacity;

    private readonly int _lUseTexture;
    private readonly int _lColor;
    private readonly int _lLineColor;
    private readonly int _lUVStart;
    private readonly int _lUVEnd;

    public GUIRectShader(GL gl)
        : base(gl, Shaders.GuiRectVertex, Shaders.GuiRectFragment)
    {
        Instance = this;

        _lPos = GetUniformLocation(gl, "u_pos");
        _lSize = GetUniformLocation(gl, "u_size");
        _lCornerRadii = GetUniformLocation(gl, "u_cornerRadii");
        _lLineThickness = GetUniformLocation(gl, "u_lineThickness");
        _lOpacity = GetUniformLocation(gl, "u_opacity");

        _lUseTexture = GetUniformLocation(gl, "u_useTexture");
        _lColor = GetUniformLocation(gl, "u_color");
        _lLineColor = GetUniformLocation(gl, "u_lineColor");
        _lUVStart = GetUniformLocation(gl, "u_uvStart");
        _lUVEnd = GetUniformLocation(gl, "u_uvEnd");

        // Set texture unit now
        Use(gl);
        gl.Uniform1(GetUniformLocation(gl, "u_texture"), 0);
    }

    public void SetRect(GL gl, in Rectangle r)
    {
        gl.Uniform2(_lPos, r.X, r.Y);
        gl.Uniform2(_lSize, r.Width, r.Height);
    }
    public void SetCornerRadii(GL gl, in Vector4D<int> v)
    {
        gl.Uniform4(_lCornerRadii, v.X, v.Y, v.Z, v.W);
    }
    public void SetLineThickness(GL gl, int i)
    {
        gl.Uniform1(_lLineThickness, i);
    }
    public void SetOpacity(GL gl, float f)
    {
        gl.Uniform1(_lOpacity, f);
    }

    public void SetUseTexture(GL gl, bool b)
    {
        gl.Uniform1(_lUseTexture, b ? 1 : 0);
    }
    public void SetColor(GL gl, in Vector4 c)
    {
        Colors.PutInShader(gl, _lColor, c);
    }
    public void SetLineColor(GL gl, in Vector4 c)
    {
        Colors.PutInShader(gl, _lLineColor, c);
    }
    public void SetUV(GL gl, in UV uv)
    {
        gl.Uniform2(_lUVStart, uv.Start);
        gl.Uniform2(_lUVEnd, uv.End);
    }
}

internal abstract class Shader2D : GLShader
{
    private readonly int _lViewportSize;

    public Shader2D(GL gl, string vertexShader, string fragmentShader)
        : base(gl, vertexShader, fragmentShader)
    {
        _lViewportSize = GetUniformLocation(gl, "u_viewportSize");
    }

    public void UpdateViewport(GL gl, Size size)
    {
        gl.Uniform2(_lViewportSize, size.Width, size.Height);
    }
}

internal abstract class GLShader
{
    public readonly uint Program;

    protected GLShader(GL gl, string vertexShader, string fragmentShader)
    {
        Program = gl.CreateProgram();
        var vertex = LoadShader(gl, ShaderType.VertexShader, vertexShader);
        var fragment = LoadShader(gl, ShaderType.FragmentShader, fragmentShader);
        gl.AttachShader(Program, vertex);
        gl.AttachShader(Program, fragment);
        gl.LinkProgram(Program);
        gl.GetProgram(Program, ProgramPropertyARB.LinkStatus, out var status);
        if (status != 1)
        {
            throw new Exception("Program failed to link: " + gl.GetProgramInfoLog(Program));
        }
        gl.DetachShader(Program, vertex);
        gl.DetachShader(Program, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
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

    public int GetUniformLocation(GL gl, string name, bool throwIfNotExists = true)
    {
        var location = gl.GetUniformLocation(Program, name);
        if (throwIfNotExists && location == -1)
        {
            throw new Exception($"\"{name}\" uniform was not found on the shader");
        }
        return location;
    }

    protected static unsafe void Matrix4(GL gl, int loc, Matrix4x4 value)
    {
        gl.UniformMatrix4(loc, 1, false, (float*)&value);
    }

    public void Use(GL gl)
    {
        gl.UseProgram(Program);
    }

    public void Delete(GL gl)
    {
        gl.DeleteProgram(Program);
    }
}

internal static class Colors
{
    public static Vector4 Transparent { get; } = new Vector4(0, 0, 0, 0);
    public static Vector3 Black3 { get; } = new Vector3(0, 0, 0);
    public static Vector4 Black4 { get; } = new Vector4(0, 0, 0, 1);
    public static Vector3 White3 { get; } = new Vector3(1, 1, 1);
    public static Vector4 White4 { get; } = new Vector4(1, 1, 1, 1);
    public static Vector4 Red4 { get; } = new Vector4(1, 0, 0, 1);
    public static Vector4 Green4 { get; } = new Vector4(0, 1, 0, 1);
    public static Vector4 Blue4 { get; } = new Vector4(0, 0, 1, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 FromRGB(uint r, uint g, uint b)
    {
        return new Vector3(r / 255f, g / 255f, b / 255f);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 V4FromRGB(uint r, uint g, uint b)
    {
        return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 FromRGBA(in Vector3 rgb, uint a)
    {
        return new Vector4(rgb, a / 255f);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 FromRGBA(uint r, uint g, uint b, uint a)
    {
        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void PutInShader(GL gl, int loc, Vector3 c)
    {
        gl.Uniform3(loc, 1, (float*)&c);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void PutInShader(GL gl, int loc, Vector4 c)
    {
        gl.Uniform4(loc, 1, (float*)&c);
    }
}

internal readonly struct UV
{
    public readonly Vector2 Start;
    public readonly Vector2 End;

    public UV(bool xFlip, bool yFlip)
    {
        Start.X = xFlip ? 1f : 0f;
        Start.Y = yFlip ? 1f : 0f;
        End.X = xFlip ? 0f : 1f;
        End.Y = yFlip ? 0f : 1f;
    }

    public UV(in Rectangle rect, Point atlasSize, bool xFlip = false, bool yFlip = false)
    {
        Start.X = (float)(xFlip ? rect.Right + 1 : rect.X) / atlasSize.X;
        Start.Y = (float)(yFlip ? rect.Bottom + 1 : rect.Y) / atlasSize.Y;
        End.X = (float)(xFlip ? rect.X : rect.Right + 1) / atlasSize.X;
        End.Y = (float)(yFlip ? rect.Y : rect.Bottom + 1) / atlasSize.Y;
    }

    public static UV FromAtlas(int imgIndex, Point imgSize, Point atlasSize, bool xFlip = false, bool yFlip = false)
    {
        var i = imgIndex * imgSize.X;
        Point topLeft;
        topLeft.X = i % atlasSize.X;
        topLeft.Y = i / atlasSize.X * imgSize.Y;

        var rect = new Rectangle(topLeft.X, topLeft.Y, imgSize.X, imgSize.Y);
        return new UV(rect, atlasSize, xFlip: xFlip, yFlip: yFlip);
    }

    public Vector2 GetBottomLeft()
    {
        return new Vector2(Start.X, End.Y);
    }
    public Vector2 GetTopRight()
    {
        return new Vector2(End.X, Start.Y);
    }

#if DEBUG
    public override string ToString()
    {
        return string.Format("[Start: {0}, End: {1}]", Start, End);
    }
#endif
}