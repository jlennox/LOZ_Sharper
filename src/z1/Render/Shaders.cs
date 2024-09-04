namespace z1.Render;

internal static class Shaders
{
    public const string GuiRectVertex = """
        #version 330 core

        layout(location = 0) in vec2 in_position;

        out vec2 pass_uv;

        uniform ivec2 u_pos;
        uniform ivec2 u_size;
        uniform vec2 u_uvStart;
        uniform vec2 u_uvEnd;
        uniform ivec2 u_viewportSize;

        uniform vec2 u_tileOffset;
        uniform vec2 u_tileSize;

        void main()
        {
            // First calculate vertex position
            vec2 absPos = in_position * u_size;
            vec2 relPos = (u_pos + absPos) / u_viewportSize;
            float glX = relPos.x * 2 - 1; // (0 => 1) to (-1 => 1)
            float glY = relPos.y * -2 + 1; // (0 => 1) to (1 => -1)
            gl_Position = vec4(glX, glY, 0, 1);

            pass_uv = mix(u_uvStart, u_uvEnd, in_position);
        }
        """;

    public const string GuiRectFragment = """
        #version 330 core

        in vec2 pass_uv;

        out vec4 out_color;

        uniform float u_opacity;
        uniform sampler2DArray u_texture;
        uniform int u_layerIndex;
        uniform vec4 u_palette[4];

        void main()
        {
            out_color = texture(u_texture, vec3(pass_uv.x, pass_uv.y, u_layerIndex));

            float paletteIndexFloat = out_color.r * 255.0 / 16.0;
            int paletteIndex = int(floor(clamp(paletteIndexFloat, 0.0, 3.0)));
            vec4 palette = u_palette[int(paletteIndex)];
            out_color = vec4(palette.rgb, 1.0);
            out_color.a *= u_opacity;
        }
        """;
}