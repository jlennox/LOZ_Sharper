namespace z1.Render;

internal static class Shaders
{
    public const string GuiRectVertex = """
        #version 330 core

        layout(location = 0) in vec2 in_position;

        out vec2 pass_absPos;
        out vec2 pass_uv;

        uniform ivec2 u_pos;
        uniform ivec2 u_size;
        uniform int u_useTexture;
        uniform vec2 u_uvStart;
        uniform vec2 u_uvEnd;
        uniform ivec2 u_viewportSize;


        void main()
        {
            // First calculate vertex position
            pass_absPos = in_position * u_size;
            vec2 relPos = (u_pos + pass_absPos) / u_viewportSize;
            float glX = relPos.x * 2 - 1; // (0 => 1) to (-1 => 1)
            float glY = relPos.y * -2 + 1; // (0 => 1) to (1 => -1)
            gl_Position = vec4(glX, glY, 0, 1);

            if (u_useTexture != 0)
            {
                pass_uv = mix(u_uvStart, u_uvEnd, in_position);
            }
        }
        """;

    public const string GuiRectFragment = """
        #version 330 core

        in vec2 pass_absPos;
        in vec2 pass_uv;

        out vec4 out_color;

        uniform ivec2 u_size;
        uniform ivec4 u_cornerRadii; // TopLeft,BottomLeft,TopRight,BottomRight
        uniform int u_lineThickness;
        uniform int u_useTexture;
        uniform vec4 u_lineColor;
        uniform vec4 u_color;
        uniform float u_opacity;
        uniform sampler2D u_texture;


        float getDist(vec2 halfSize, float r)
        {
            return 1 - (length(max(abs(pass_absPos - halfSize) - halfSize + r, 0)) - r);
        }
        bool discardPixels()
        {
            vec2 halfSize = u_size * 0.5;
            ivec2 xSide = pass_absPos.x < halfSize.x ? u_cornerRadii.xy : u_cornerRadii.zw;
            int cornerRadius = pass_absPos.y < halfSize.y ? xSide.x : xSide.y;

            // Discard pixels for corner radius
            if (cornerRadius != 0 && getDist(halfSize, cornerRadius) < 1)
            {
                discard;
            }
            // Discard pixels for line thickness
            if (u_lineThickness != 0)
            {
                if (getDist(halfSize, cornerRadius + u_lineThickness) < 1 + u_lineThickness)
                {
                    return true; // Returns true if we're drawing the line
                }
                if (u_color.a == 0)
                {
                    discard; // Discard inner pixels if we're not filling the rect
                }
            }
            return false;
        }

        void main()
        {
            if (discardPixels())
            {
                out_color = u_lineColor;
            }
            else if (u_useTexture != 0)
            {
                out_color = texture(u_texture, pass_uv);
            }
            else
            {
                out_color = u_color;
            }
            out_color.a *= u_opacity;
        }
        """;
}