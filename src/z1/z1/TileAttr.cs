namespace z1;

internal enum TileAction
{
    None,
    Push,
    Bomb,
    Burn,
    Headstone,
    Ladder,
    Raft,
    Cave,
    Stairs,
    Ghost,
    Armos,
    Block,
}


internal static class TileAttr
{
    public static TileAction GetAction(byte t) => (TileAction)((t & 0xF0) >> 4);

    public static bool IsQuadrantBlocked(byte t, int row, int col)
    {
        byte walkBit = 1;
        walkBit <<= col * 2;
        walkBit <<= row;
        return (t & walkBit) != 0;
    }

    public static bool IsTileBlocked(byte t) => (t & 0x0F) != 0;
}

internal static class Maffs
{
    public const float _2_PI = 6.283185307179586f;
    public const float PI_OVER_16 = 0.196349540849362f;
    public const float PI_OVER_8 = 0.392699081698724f;
    public const float NEG_PI_OVER_8 = -0.392699081698724f;

    public static int GetSector16(float x, float y)
    {
        var sector = 0;

        if (y < 0)
        {
            sector += 8;
            y = -y;
            x = -x;
        }

        if (x < 0)
        {
            sector += 4;
            var temp = x;
            x = y;
            y = -temp;
        }

        if (x < y)
        {
            sector += 2;
            var temp = y - x;
            x = x + y;
            y = temp;
            // Because we're only finding out the sector, only the angle matters, not the point along it.
            // So, we can skip multiplying x and y by 1/(2^.5)
        }

        Rotate(NEG_PI_OVER_8, ref x, ref y);

        if (y > 0)
            sector++;

        sector %= 16;
        return sector;
    }

    public static void Rotate(float angle, ref float x, ref float y )
    {
        var sine = (float)Math.Sin(angle);
        var cosine = (float)Math.Cos(angle);
        var x1 = x;
        var y1 = y;

        x = x1 * cosine - y * sine;
        y = x1 * sine + y * cosine;
    }

    public static void PolarToCart(float angle, float distance, out float x, out float y )
    {
        y = (float)Math.Sin(angle) * distance;
        x = (float)Math.Cos(angle) * distance;
    }
}