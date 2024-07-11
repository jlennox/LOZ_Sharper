using SkiaSharp;

namespace z1;

internal class Graphics
{
    public static void SetColorIndexed(Palette paletteIndex, int colorIndex, int sysColor) { }
    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors) { }
    public static void UpdatePalettes() { }

    public static void EnableGrayscale() { }
    public static void DisableGrayscale() { }

    public static void SetViewParams(float scale, float x, float y) { }
    public static void SetClip(int x, int y, int width, int height) { }
    public static void ResetClip() { }
    public static void Begin() { }
    public static void End() { }

    public static void LoadTileSheet(TileSheet sheet, string file) { }
    public static void LoadTileSheet(TileSheet sheet, string file, string animationFile) { }
    public static void LoadSystemPalette(int[] colorsArgb8) { }

    public static SpriteAnimation GetAnimation(TileSheet sheet, AnimationId id) => throw new Exception();
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
    { }

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
    { }
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

