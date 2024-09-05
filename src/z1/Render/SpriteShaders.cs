namespace z1.Render;

internal static class SpriteShaders
{
    public const string Vertex = """
        #version 330 core

        layout(location = 0) in vec2 in_position;

        out vec2 pass_uv;

        uniform ivec2 u_pos;
        uniform ivec2 u_size;
        uniform vec2 u_sourcePos;
        uniform vec2 u_uvStart;
        uniform vec2 u_uvEnd;
        uniform ivec2 u_viewportSize;

        void main()
        {
            vec2 absPos = in_position * u_size;
            vec2 offsetPos = in_position * u_size;
            vec2 relPos = (u_pos + absPos - u_sourcePos) / u_viewportSize ; // * (u_sourcePos / u_size);
            float glX = relPos.x * 2 - 1; // (0 => 1) to (-1 => 1)
            float glY = relPos.y * -2 + 1; // (0 => 1) to (1 => -1)
            gl_Position = vec4(glX, glY, 0, 1);

            pass_uv = mix(u_uvStart, u_uvEnd, in_position);
        }
        """;

    public const string Fragment = """
        #version 330 core

        in vec2 pass_uv;

        out vec4 out_color;

        uniform float u_opacity;
        uniform sampler2D u_texture;
        uniform int u_layerIndex;
        uniform uvec4 u_palette;

        void main()
        {
            out_color = texture(u_texture, pass_uv);

            float paletteIndexFloat = out_color.r * 255.0 / 16.0;
            int paletteIndex = int(floor(clamp(paletteIndexFloat, 0.0, 3.0)));
            uint palette = u_palette[paletteIndex];
            out_color = vec4(
                (float((palette >> 16) & uint(0xFF)) / 0xFF),
                (float((palette >> 8) & uint(0xFF)) / 0xFF),
                (float((palette >> 0) & uint(0xFF)) / 0xFF),
                (float((palette >> 24) & uint(0xFF)) / 0xFF)
            );
            out_color.a *= u_opacity;
        }
        """;
}