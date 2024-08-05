﻿namespace z1;

internal enum UWNpcsAnimIds
{
    UW_Bubble,
    UW_Gel,
    UW_Trap,
    UW_OldMan,
    UW_Keese,
    UW_Moldorm,
    UW_Wallmaster,
    UW_Rope_Right,
    UW_Rope_Left,
    UW_Stalfos,
    UW_Goriya_Down,
    UW_Goriya_Up,
    UW_Goriya_Right,
    UW_Goriya_Left,
    UW_PolsVoice,
    UW_Gibdo,
    UW_Zol,
    UW_Darknut_Down,
    UW_Darknut_Up,
    UW_Darknut_Right,
    UW_Darknut_Left,
    UW_LanmolaHead,
    UW_LanmolaBody,
    UW_LikeLike,
    UW_Vire_Down,
    UW_Vire_Up,
    UW_Wizzrobe_Right,
    UW_Wizzrobe_Left,
    UW_Wizzrobe_Up,
}

internal enum BossAnimationIds
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

    UW_Bubble = UWNpcsAnimIds.UW_Bubble,
    UW_Gel = UWNpcsAnimIds.UW_Gel,
    UW_Trap = UWNpcsAnimIds.UW_Trap,
    UW_OldMan = UWNpcsAnimIds.UW_OldMan,
    UW_Keese = UWNpcsAnimIds.UW_Keese,
    UW_Moldorm = UWNpcsAnimIds.UW_Moldorm,
    UW_Wallmaster = UWNpcsAnimIds.UW_Wallmaster,
    UW_Rope_Right = UWNpcsAnimIds.UW_Rope_Right,
    UW_Rope_Left = UWNpcsAnimIds.UW_Rope_Left,
    UW_Stalfos = UWNpcsAnimIds.UW_Stalfos,
    UW_Goriya_Down = UWNpcsAnimIds.UW_Goriya_Down,
    UW_Goriya_Up = UWNpcsAnimIds.UW_Goriya_Up,
    UW_Goriya_Right = UWNpcsAnimIds.UW_Goriya_Right,
    UW_Goriya_Left = UWNpcsAnimIds.UW_Goriya_Left,
    UW_PolsVoice = UWNpcsAnimIds.UW_PolsVoice,
    UW_Gibdo = UWNpcsAnimIds.UW_Gibdo,
    UW_Zol = UWNpcsAnimIds.UW_Zol,
    UW_Darknut_Down = UWNpcsAnimIds.UW_Darknut_Down,
    UW_Darknut_Up = UWNpcsAnimIds.UW_Darknut_Up,
    UW_Darknut_Right = UWNpcsAnimIds.UW_Darknut_Right,
    UW_Darknut_Left = UWNpcsAnimIds.UW_Darknut_Left,
    UW_LanmolaHead = UWNpcsAnimIds.UW_LanmolaHead,
    UW_LanmolaBody = UWNpcsAnimIds.UW_LanmolaBody,
    UW_LikeLike = UWNpcsAnimIds.UW_LikeLike,
    UW_Vire_Down = UWNpcsAnimIds.UW_Vire_Down,
    UW_Vire_Up = UWNpcsAnimIds.UW_Vire_Up,
    UW_Wizzrobe_Right = UWNpcsAnimIds.UW_Wizzrobe_Right,
    UW_Wizzrobe_Left = UWNpcsAnimIds.UW_Wizzrobe_Left,
    UW_Wizzrobe_Up = UWNpcsAnimIds.UW_Wizzrobe_Up,
}