namespace z1.Render;

internal static class SpriteShaders
{
    public static string Vertex = """
        #version 330 core

        layout(location = 0) in ivec2 in_tile_position; // tile location inside of texture, in pixels
        layout(location = 1) in ivec2 in_screen_position; // the destination in u_viewportSize coordinates, in pixels

        out vec2 pass_uv;

        uniform ivec2 u_viewportSize; // 256x240
        uniform ivec2 u_size; // full texture size, 128x128

        void main()
        {
            vec2 viewportSize = vec2(u_viewportSize); // Don't want to coerce results to ints.
            // We experience some viewport size specific texture misalignment and this appears to make it less common..?
            vec2 screenPosition = vec2(in_screen_position) + .5;
            // View port space to screen space.
            vec2 viewportRelativePos = (screenPosition * vec2(2, -2) / viewportSize);
            // Translate from 0 => 1 coordinate space, to (-1 => 1) and (1 => -1)
            // y is 0 at the bottom, so it's flipped.
            vec2 pos = viewportRelativePos + vec2(-1, 1);
            gl_Position = vec4(pos, 0, 1);

            pass_uv = vec2(in_tile_position) / vec2(u_size);
        }
        """;

    public static string Fragment = """
        #version 330 core

        in vec2 pass_uv;

        out vec4 out_color;

        uniform float u_opacity;
        uniform sampler2D u_texture;
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