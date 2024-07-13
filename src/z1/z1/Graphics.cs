using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace z1;

internal static class Graphics
{
    private static SKSurface _surface;
    private static SKImageInfo _info;

    private static SKBitmap?[] tileSheets = new SKBitmap[(int)TileSheet.Max];
    private static SKBitmap paletteBmp;
    private static SKBitmap tileShader;
    private static byte[] paletteBuf;
    private static int paletteBufSize;
    private static int paletteStride;
    private static byte[] systemPalette = new byte[Global.SysPaletteLength];
    private static byte[] grayscalePalette = new byte[Global.SysPaletteLength];
    private static byte[] activeSystemPalette = systemPalette;
    private static byte[] palettes = new byte[Global.PaletteCount * Global.PaletteLength];

    private static int PaletteBmpWidth = Math.Max(Global.PaletteLength, 16);
    private static int PaletteBmpHeight = Math.Max(Global.PaletteCount, 16);

    public static ref byte GetPalette(Palette paletteIndex, int colorIndex) => ref palettes[(int)paletteIndex * Global.PaletteLength + colorIndex];
    public static Span<byte> GetPalette(Palette paletteIndex) => MemoryMarshal.CreateSpan(ref palettes[(int)paletteIndex * Global.PaletteLength], Global.PaletteLength);

    private static float viewScale;
    private static float viewOffsetX;
    private static float viewOffsetY;

    private static int savedClipX;
    private static int savedClipY;
    private static int savedClipWidth;
    private static int savedClipHeight;

    private static TableResource<SpriteAnimation>[] animSpecs = new TableResource<SpriteAnimation>[(int)TileSheet.Max];

    static Graphics()
    {
        paletteBmp = new SKBitmap(PaletteBmpWidth, PaletteBmpHeight);

        AllocatePaletteBuffer();
    }

    private static void AllocatePaletteBuffer()
    {
        var stride = paletteBmp.RowBytes;
        if (stride < 0)
            stride = -stride;

        paletteBufSize = stride * PaletteBmpHeight;
        paletteStride = stride;

        paletteBuf = new byte[paletteBufSize];
    }

    public static void Begin()
    {
    }

    public static void SetSurface(SKSurface surface, SKImageInfo info)
    {
        _surface = surface;
        _info = info;
    }

    public static void End()
    {
        // Not needed?
        _surface.Canvas.Flush();
    }

    public static void LoadTileSheet(TileSheet sheet, string file) {
        var slot = (int)sheet;

        ref var foundRef = ref tileSheets[slot];
        if (foundRef != null)
        {
            foundRef.Dispose();
            foundRef = null;
        }

        tileSheets[slot] = SKBitmap.Decode(Assets.Root.GetPath("out", file)) ?? throw new Exception(); //new SKBitmap(1, 1);
    }

    public static void LoadTileSheet(TileSheet sheet, string path, string animationFile)
    {
        LoadTileSheet(sheet, path);
        animSpecs[(int)sheet] = TableResource<SpriteAnimation>.Load(animationFile);
    }

    public static SpriteAnimation GetAnimation(TileSheet sheet, AnimationId id)
    {
        return animSpecs[(int)sheet].GetItem<SpriteAnimation>((int)id);
    }

    // Uh, is this stuff right?!
    public static SpriteAnimation GetAnimation(TileSheet sheet, int id)
    {
        return animSpecs[(int)sheet].GetItem<SpriteAnimation>(id);
    }

    public static void LoadSystemPalette(int[] colorsArgb8)
    {
        Buffer.BlockCopy(colorsArgb8, 0, systemPalette, 0, systemPalette.Length);

        for (var i = 0; i < Global.SysPaletteLength; i++)
        {
            grayscalePalette[i] = systemPalette[i & 0x30];
        }
    }

    public static SKColor GetSystemColor(int sysColor)
    {
        int argb8 = activeSystemPalette[sysColor];
        return new SKColor(
            (byte)((argb8 >> 16) & 0xFF),
            (byte)((argb8 >> 8) & 0xFF),
            (byte)((argb8 >> 0) & 0xFF),
            (byte)((argb8 >> 24) & 0xFF)
        );
    }

    public static void SetColor(Palette paletteIndex, int colorIndex, uint colorArgb8)
    {
        var y = (int)paletteIndex;
        var x = colorIndex;

        var line = MemoryMarshal.Cast<byte, uint>(paletteBuf[(y * paletteStride)..]);
        line[x] = colorArgb8;
    }

    public static void SetColor(Palette paletteIndex, int colorIndex, int colorArgb8) => SetColor(paletteIndex, colorIndex, (uint)colorArgb8);

    public static void SetPalette(Palette paletteIndex, ReadOnlySpan<byte> colorsArgb8)
    {
        var y = (int)paletteIndex;
        var line = MemoryMarshal.Cast<byte, int>(paletteBuf[(y * paletteStride)..]);

        for ( var x = 0; x < Global.PaletteLength; x++ )
        {
            line[x] = colorsArgb8[x];
        }
    }

    public static void SetColorIndexed(Palette paletteIndex, int colorIndex, int sysColor)
    {
        var colorArgb8 = 0;
        if (colorIndex != 0)
            colorArgb8 = activeSystemPalette[sysColor];
        SetColor(paletteIndex, colorIndex, colorArgb8);
        GetPalette(paletteIndex, colorIndex) = (byte)sysColor;
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors)
    {
        var colorsArgb8 = new byte[]
        {
            0,
            activeSystemPalette[sysColors[1]],
            activeSystemPalette[sysColors[2]],
            activeSystemPalette[sysColors[3]],
        };

        SetPalette(paletteIndex, colorsArgb8);
        var dest = GetPalette(paletteIndex);
        sysColors[..Global.PaletteLength].CopyTo(dest);
    }

    public static void UpdatePalettes()
    {
        // TODO int format = al_get_bitmap_format( paletteBmp );
        // TODO ALLEGRO_LOCKED_REGION*  region = al_lock_bitmap( paletteBmp, format, ALLEGRO_LOCK_WRITEONLY );
        // TODO assert( region != nullptr );
        // TODO
        // TODO unsigned char* base = (unsigned char*) region->data;
        // TODO if ( region->pitch < 0 )
        // TODO     base += region->pitch * (PaletteBmpHeight - 1);
        // TODO
        // TODO memcpy( base, paletteBuf, paletteBufSize );
        // TODO
        // TODO al_unlock_bitmap( paletteBmp );
    }

    public static void SwitchSystemPalette(Span<byte> newSystemPalette)
    {
        if (newSystemPalette.SequenceEqual(activeSystemPalette))
            return;

        newSystemPalette.CopyTo(activeSystemPalette);

        for (var i = 0; i < Global.PaletteCount; i++)
        {
            var sysColors = GetPalette((Palette)i);
            var colorsArgb8 = new byte[]
            {
                0,
                activeSystemPalette[sysColors[1]],
                activeSystemPalette[sysColors[2]],
                activeSystemPalette[sysColors[3]],
            };
            SetPalette((Palette)i, colorsArgb8);
        }
        UpdatePalettes();
    }

    public static void EnableGrayscale()
    {
        SwitchSystemPalette(grayscalePalette);
    }

    public static void DisableGrayscale()
    {
        SwitchSystemPalette(systemPalette);
    }

    public static SpriteAnimator GetSpriteAnimator(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));
    public static SpriteImage GetSpriteImage(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));

    public static void DrawBitmap(
        SKBitmap? bitmap,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        int flags
    )
    {
        var palRed = (int)palette / (float)bitmap.Height;
        var tint = new SKColor((byte)(palRed * 255), 0, 0, 255);

        var sourceRect = new SKRect(srcX, srcY, srcX + width, srcY + height);
        var destRect = new SKRect(destX, destY, destX + width, destY + height);

        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn) };

        _surface.Canvas.DrawBitmap(bitmap, sourceRect, destRect, paint);
    }

    public static void DrawSpriteTile(
        TileSheet slot,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        int flags
    )
    {
        DrawTile( slot, srcX, srcY, width, height, destX, destY + 1, palette, flags);
    }

    public static void DrawTile(
        TileSheet slot,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        int flags
    )
    {
        Debug.Assert(slot < TileSheet.Max);

        var sheet = tileSheets[(int)slot];
        var palRed = (int)palette / (float)sheet.Height;
        var tint = new SKColor((byte)(palRed * 255), 0, 0, 255);

        var sourceRect = new SKRect(srcX, srcY, srcX + width, srcY + height);
        var destRect = new SKRect(destX, destY, destX + width, destY + height);

        // TODO using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.SrcIn) };

        _surface.Canvas.DrawBitmap(sheet, sourceRect, destRect); //, paint);

        _surface.Canvas.Flush();
        // sheet.SavePng(@"C:\users\joe\desktop\delete\_sheet.png");
        // _surface.Canvas.SavePng(@"C:\users\joe\desktop\delete\_canvas.png");
    }

    public static void DrawStripSprite16x16(TileSheet slot,int firstTile,int destX,int destY,Palette palette)
    {
        static ReadOnlySpan<byte> offsetsX() => new byte[] { 0, 0, 8, 8 };
        static ReadOnlySpan<byte> offsetsY() => new byte[] { 0, 8, 0, 8 };

        var tileRef = firstTile;

        for (var i = 0; i < 4; i++)
        {
            var srcX = (tileRef & 0x0F) * World.TileWidth;
            var srcY = ((tileRef & 0xF0) >> 4) * World.TileHeight;
            tileRef++;

            DrawTile(
                slot,
                srcX,
                srcY,
                World.TileWidth,
                World.TileHeight,
                destX + offsetsX()[i],
                destY + offsetsY()[i],
                palette,
                0);
        }
    }

    public static void SetViewParams(float scale, float x, float y)
    {
        viewScale = scale;
        viewOffsetX = x;
        viewOffsetY = y;
    }

    public static void Clear(SKColor color)
    {
        _surface.Canvas.Clear(color);
    }

    public static void Clear(SKColor color, int x, int y, int width, int height)
    {
        _surface.Canvas.Clear(color);
    }

    public static void SetClip(int x, int y, int width, int height)
    {
        var savedClipRect = _surface.Canvas.DeviceClipBounds;
        savedClipX = savedClipRect.Left;
        savedClipY = savedClipRect.Top;
        savedClipWidth = savedClipRect.Width;
        savedClipHeight = savedClipRect.Height;

        var y2 = y + height;

        if (y2 < 0)
        {
            height = 0;
            y = 0;
        }
        else if (y > Global.StdViewHeight)
        {
            height = 0;
            y = Global.StdViewHeight;
        }
        else
        {
            if (y < 0)
            {
                height += y;
                y = 0;
            }

            if (y2 > Global.StdViewHeight)
            {
                height = Global.StdViewHeight - y;
            }
        }

        var clipX = (int)(viewOffsetX + x * viewScale);
        var clipY = (int)(viewOffsetY + y * viewScale);
        var clipWidth = (int)(width * viewScale);
        var clipHeight = (int)(height * viewScale);

        // _surface.Canvas.ClipRect(new SKRect(clipX, clipY, clipX + clipWidth, clipY + clipHeight));
    }

    public static void ResetClip()
    {
        _surface.Canvas.ClipRect(new SKRect(savedClipX, savedClipY, savedClipX + savedClipWidth, savedClipY + savedClipHeight));
    }
}

enum BossAnimationIds
{
    B1_Aquamentus,
    B1_Aquamentus_Mouth_Open,
    B1_Aquamentus_Mouth_Closed,
    B1_Digdogger_Big,
    B1_Digdogger_Little,
    B1_Dodongo_R,
    B1_Dodongo_L,
    B1_Dodongo_D,
    B1_Dodongo_U,
    B1_Dodongo_Bloated_R,
    B1_Dodongo_Bloated_L,
    B1_Dodongo_Bloated_D,
    B1_Dodongo_Bloated_U,

    B2_Gleeok_Body = 0,
    B2_Gleeok_Neck,
    B2_Gleeok_Head,
    B2_Gleeok_Head2,
    B2_Manhandla_Hand_L,
    B2_Manhandla_Hand_R,
    B2_Manhandla_Hand_U,
    B2_Manhandla_Hand_D,
    B2_Manhandla_Body,
    B2_Gohma_Legs_L,
    B2_Gohma_Legs_R,
    B2_Gohma_Eye_Closed,
    B2_Gohma_Eye_Mid,
    B2_Gohma_Eye_Open,
    B2_Gohma_Eye_All,

    B3_Ganon = 0,
    B3_Slash_U,
    B3_Slash_D,
    B3_Slash_L,
    B3_Slash_R,
    B3_Pile,
    B3_Zelda_Lift,
    B3_Zelda_Stand,
    B3_Triforce,
    B3_Patra,
    B3_PatraChild,
}

internal enum OverworldAnimationIds
{
    OW_Boulder,
    OW_Whirlwind,
    OW_OldMan,
    OW_OldWoman,
    OW_Merchant,
    OW_FlyingRock,
    OW_Armos_Down,
    OW_Armos_Up,
    OW_Armos_Right,
    OW_Armos_Left,
    OW_Octorock_Down,
    OW_Octorock_Left,
    OW_Octorock_Up,
    OW_Octorock_Right,
    OW_Mound,
    OW_LeeverHalf,
    OW_Leever,
    OW_Peahat,
    OW_Tektite,
    OW_Lynel_Right,
    OW_Lynel_Left,
    OW_Lynel_Down,
    OW_Lynel_Up,
    OW_Ghini_Left,
    OW_Ghini_Right,
    OW_Ghini_UpLeft,
    OW_Ghini_UpRight,
    OW_Zora_Down,
    OW_Zora_Up,
    OW_Moblin_Right,
    OW_Moblin_Left,
    OW_Moblin_Down,
    OW_Moblin_Up,
}

internal enum AnimationId
{
    LinkWalk_NoShield_Right,
    LinkWalk_NoShield_Left,
    LinkWalk_NoShield_Down,
    LinkWalk_NoShield_Up,
    LinkWalk_LittleShield_Right,
    LinkWalk_LittleShield_Left,
    LinkWalk_LittleShield_Down,
    LinkWalk_LittleShield_Up,
    LinkWalk_BigShield_Right,
    LinkWalk_BigShield_Left,
    LinkWalk_BigShield_Down,
    LinkWalk_BigShield_Up,
    LinkThrust_Right,
    LinkThrust_Left,
    LinkThrust_Down,
    LinkThrust_Up,
    LinkLiftLight,
    LinkLiftHeavy,
    SwordItem,
    FleshItem,
    RecorderItem,
    CandleItem,
    ArrowItem,
    BowItem,
    MKeyItem,
    KeyItem,
    RuppeeItem,
    BombItem,
    BoomerangItem,
    Spark,
    Slash,
    BottleItem,
    BookItem,
    Fireball,
    RingItem,
    MSwordItem,
    WandItem,
    MapItem,
    BraceletItem,
    Fairy,
    MShieldItem,
    Heart,
    Fire,
    Sparkle,
    Clock,
    HeartContainer,
    Compass,
    Raft,
    TriforcePiece,
    Cloud,
    Ladder,
    Wave_Right,
    Wave_Left,
    Wave_Down,
    Wave_Up,
    Sword_Right,
    Sword_Left,
    Sword_Down,
    Sword_Up,
    Arrow_Right,
    Arrow_Left,
    Arrow_Down,
    Arrow_Up,
    Wand_Right,
    Wand_Left,
    Wand_Down,
    Wand_Up,
    Boomerang,
    OldMan,
    OldWoman,
    Merchant,
    Moblin,
    PowerTriforce,
    Cursor,

    B1_Aquamentus = BossAnimationIds.B1_Aquamentus,
    B1_Aquamentus_Mouth_Open = BossAnimationIds.B1_Aquamentus_Mouth_Open,
    B1_Aquamentus_Mouth_Closed = BossAnimationIds.B1_Aquamentus_Mouth_Closed,
    B1_Digdogger_Big = BossAnimationIds.B1_Digdogger_Big,
    B1_Digdogger_Little = BossAnimationIds.B1_Digdogger_Little,
    B1_Dodongo_R = BossAnimationIds.B1_Dodongo_R,
    B1_Dodongo_L = BossAnimationIds.B1_Dodongo_L,
    B1_Dodongo_D = BossAnimationIds.B1_Dodongo_D,
    B1_Dodongo_U = BossAnimationIds.B1_Dodongo_U,
    B1_Dodongo_Bloated_R = BossAnimationIds.B1_Dodongo_Bloated_R,
    B1_Dodongo_Bloated_L = BossAnimationIds.B1_Dodongo_Bloated_L,
    B1_Dodongo_Bloated_D = BossAnimationIds.B1_Dodongo_Bloated_D,
    B1_Dodongo_Bloated_U = BossAnimationIds.B1_Dodongo_Bloated_U,


    B2_Gleeok_Body = BossAnimationIds.B2_Gleeok_Body,
    B2_Gleeok_Neck = BossAnimationIds.B2_Gleeok_Neck,
    B2_Gleeok_Head = BossAnimationIds.B2_Gleeok_Head,
    B2_Gleeok_Head2 = BossAnimationIds.B2_Gleeok_Head2,
    B2_Manhandla_Hand_L = BossAnimationIds.B2_Manhandla_Hand_L,
    B2_Manhandla_Hand_R = BossAnimationIds.B2_Manhandla_Hand_R,
    B2_Manhandla_Hand_U = BossAnimationIds.B2_Manhandla_Hand_U,
    B2_Manhandla_Hand_D = BossAnimationIds.B2_Manhandla_Hand_D,
    B2_Manhandla_Body = BossAnimationIds.B2_Manhandla_Body,
    B2_Gohma_Legs_L = BossAnimationIds.B2_Gohma_Legs_L,
    B2_Gohma_Legs_R = BossAnimationIds.B2_Gohma_Legs_R,
    B2_Gohma_Eye_Closed = BossAnimationIds.B2_Gohma_Eye_Closed,
    B2_Gohma_Eye_Mid = BossAnimationIds.B2_Gohma_Eye_Mid,
    B2_Gohma_Eye_Open = BossAnimationIds.B2_Gohma_Eye_Open,
    B2_Gohma_Eye_All = BossAnimationIds.B2_Gohma_Eye_All,

    B3_Ganon = BossAnimationIds.B3_Ganon,
    B3_Slash_U = BossAnimationIds.B3_Slash_U,
    B3_Slash_D = BossAnimationIds.B3_Slash_D,
    B3_Slash_L = BossAnimationIds.B3_Slash_L,
    B3_Slash_R = BossAnimationIds.B3_Slash_R,
    B3_Pile = BossAnimationIds.B3_Pile,
    B3_Zelda_Lift = BossAnimationIds.B3_Zelda_Lift,
    B3_Zelda_Stand = BossAnimationIds.B3_Zelda_Stand,
    B3_Triforce = BossAnimationIds.B3_Triforce,
    B3_Patra = BossAnimationIds.B3_Patra,
    B3_PatraChild = BossAnimationIds.B3_PatraChild,

    OW_Boulder = OverworldAnimationIds.OW_Boulder,
    OW_Whirlwind = OverworldAnimationIds.OW_Whirlwind,
    OW_OldMan = OverworldAnimationIds.OW_OldMan,
    OW_OldWoman = OverworldAnimationIds.OW_OldWoman,
    OW_Merchant = OverworldAnimationIds.OW_Merchant,
    OW_FlyingRock = OverworldAnimationIds.OW_FlyingRock,
    OW_Armos_Down = OverworldAnimationIds.OW_Armos_Down,
    OW_Armos_Up = OverworldAnimationIds.OW_Armos_Up,
    OW_Armos_Right = OverworldAnimationIds.OW_Armos_Right,
    OW_Armos_Left = OverworldAnimationIds.OW_Armos_Left,
    OW_Octorock_Down = OverworldAnimationIds.OW_Octorock_Down,
    OW_Octorock_Left = OverworldAnimationIds.OW_Octorock_Left,
    OW_Octorock_Up = OverworldAnimationIds.OW_Octorock_Up,
    OW_Octorock_Right = OverworldAnimationIds.OW_Octorock_Right,
    OW_Mound = OverworldAnimationIds.OW_Mound,
    OW_LeeverHalf = OverworldAnimationIds.OW_LeeverHalf,
    OW_Leever = OverworldAnimationIds.OW_Leever,
    OW_Peahat = OverworldAnimationIds.OW_Peahat,
    OW_Tektite = OverworldAnimationIds.OW_Tektite,
    OW_Lynel_Right = OverworldAnimationIds.OW_Lynel_Right,
    OW_Lynel_Left = OverworldAnimationIds.OW_Lynel_Left,
    OW_Lynel_Down = OverworldAnimationIds.OW_Lynel_Down,
    OW_Lynel_Up = OverworldAnimationIds.OW_Lynel_Up,
    OW_Ghini_Left = OverworldAnimationIds.OW_Ghini_Left,
    OW_Ghini_Right = OverworldAnimationIds.OW_Ghini_Right,
    OW_Ghini_UpLeft = OverworldAnimationIds.OW_Ghini_UpLeft,
    OW_Ghini_UpRight = OverworldAnimationIds.OW_Ghini_UpRight,
    OW_Zora_Down = OverworldAnimationIds.OW_Zora_Down,
    OW_Zora_Up = OverworldAnimationIds.OW_Zora_Up,
    OW_Moblin_Right = OverworldAnimationIds.OW_Moblin_Right,
    OW_Moblin_Left = OverworldAnimationIds.OW_Moblin_Left,
    OW_Moblin_Down = OverworldAnimationIds.OW_Moblin_Down,
    OW_Moblin_Up = OverworldAnimationIds.OW_Moblin_Up,
}

