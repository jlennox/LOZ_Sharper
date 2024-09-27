using System.Collections.Immutable;
using System.Drawing;
using System.Text;
using System.Text.Json.Serialization;
using z1.Common.Data;

namespace z1.Common;

// HEY! LISTEN! These must remain in this order, because they're used by the exporter.
// If we ever want to start dropping the unknowns, then we need to make that
// translate between its own and a modified enums.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObjType
{
    None,

    BlueLynel,
    RedLynel,
    BlueMoblin,
    RedMoblin,
    BlueGoriya,
    RedGoriya,
    RedSlowOctorock,
    RedFastOctorock,
    BlueSlowOctorock,
    BlueFastOctorock,
    RedDarknut,
    BlueDarknut,
    BlueTektite,
    RedTektite,
    BlueLeever,
    RedLeever,
    Zora,
    Vire,
    Zol,
    ChildGel,
    Gel,
    PolsVoice,
    LikeLike,
    LittleDigdogger,
    Unknown1__,
    Peahat,
    BlueKeese,
    RedKeese,
    BlackKeese,
    Armos,
    Boulders,
    Boulder,
    Ghini,
    FlyingGhini,
    BlueWizzrobe,
    RedWizzrobe,
    PatraChild1,
    PatraChild2,
    Wallmaster,
    Rope,
    Unknown5__,
    Stalfos,
    Bubble1,
    Bubble2,
    Bubble3,
    Whirlwind,
    PondFairy,
    Gibdo,
    ThreeDodongos,
    OneDodongo,
    BlueGohma,
    RedGohma,
    RupieStash,
    Grumble,
    Zelda,
    Digdogger1,
    Digdogger2,
    RedLamnola,
    BlueLamnola,
    Manhandla,
    Aquamentus,
    Ganon,
    GuardFire,
    StandingFire,
    Moldorm,
    Gleeok1,
    Gleeok2,
    Gleeok3,
    Gleeok4,
    GleeokHead,
    Patra1,
    Patra2,
    Trap,
    TrapSet4,

    Person1,
    Person2,
    Person3,
    Person4,
    Person5,
    Person6,
    Person7,
    Person8,

    FlyingRock,
    Unknown54__,
    Fireball,
    Fireball2,
    PlayerSwordShot,

    OldMan,
    OldWoman,
    Merchant,
    FriendlyMoblin,

    MagicWave = OldMan,
    MagicWave2 = OldWoman,
    Arrow = FriendlyMoblin,

    Boomerang,
    DeadDummy,
    FluteSecret,
    Ladder,
    Item,

    Dock,
    Rock,
    RockWall,
    Tree,
    Headstone,

    Unknown66__,
    Unknown67__,
    Block,
    Unknown69__,

    Cave1,
    Cave2,
    Cave3WhiteSword,
    Cave4MagicSword,
    Cave5Shortcut,
    Cave6,
    Cave7,
    Cave8,
    Cave9,
    Cave10,
    Cave11MedicineShop,
    Cave12LostHillsHint,
    Cave13LostWoodsHint,
    Cave14,
    Cave15,
    Cave16,
    Cave17,
    Cave18,
    Cave19,
    Cave20,

    Bomb,
    PlayerSword,
    Fire,
    Rod,
    Food,

    Player,

    PersonEnd = Person8 + 1,
    PersonTypes = PersonEnd - Person1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaveId
{
    /// <summary>Wooden sword</summary>
    Cave1 = ObjType.Cave1,
    /// <summary>Pick any</summary>
    Cave2 = ObjType.Cave2,
    /// <summary>White sword</summary>
    Cave3WhiteSword = ObjType.Cave3WhiteSword,
    /// <summary>Magic sword</summary>
    Cave4MagicSword = ObjType.Cave4MagicSword,
    /// <summary>Shortcut</summary>
    Cave5Shortcut = ObjType.Cave5Shortcut,
    Cave6 = ObjType.Cave6,
    /// <summary>Gamble</summary>
    Cave7 = ObjType.Cave7,
    /// <summary>Mugger</summary>
    Cave8 = ObjType.Cave8,
    Cave9 = ObjType.Cave9,
    Cave10 = ObjType.Cave10,
    Cave11MedicineShop = ObjType.Cave11MedicineShop,
    Cave12LostHillsHint = ObjType.Cave12LostHillsHint,
    Cave13LostWoodsHint = ObjType.Cave13LostWoodsHint,
    /// <summary>Shop</summary>
    Cave14 = ObjType.Cave14,
    /// <summary>Shop</summary>
    Cave15 = ObjType.Cave15,
    /// <summary>Shop</summary>
    Cave16 = ObjType.Cave16,
    /// <summary>Shop</summary>
    Cave17 = ObjType.Cave17,
    Cave18 = ObjType.Cave18,
    Cave19 = ObjType.Cave19,
    Cave20 = ObjType.Cave20,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaveDwellerType
{
    None = ObjType.None,
    OldMan = ObjType.OldMan,
    OldWoman = ObjType.OldWoman,
    Merchant = ObjType.Merchant,
    Moblin = ObjType.FriendlyMoblin,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemId
{
    Bomb,
    WoodSword,
    WhiteSword,
    MagicSword,
    Food,
    Recorder,
    BlueCandle,
    RedCandle,
    WoodArrow,
    SilverArrow,
    Bow,
    MagicKey,
    Raft,
    Ladder,
    PowerTriforce,
    FiveRupees,
    Rod,
    Book,
    BlueRing,
    RedRing,
    Bracelet,
    Letter,
    Compass,
    Map,
    Rupee,
    Key,
    HeartContainer,
    // JOE: TODO: Split this out into specific pieces?
    TriforcePiece,
    MagicShield,
    WoodBoomerang,
    MagicBoomerang,
    BluePotion,
    RedPotion,
    Clock,
    Heart,
    Fairy,

    // New
    MaxBombs,

    MAX = 0x3F,
    None = MAX
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PersonType
{
    Shop,
    Grumble,
    MoneyOrLife,
    DoorRepair,
    Gambling,
    EnterLevel9,
    CaveShortcut,
    MoreBombs,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ItemSlot
{
    None = -1,
    Sword,
    Bombs,
    Arrow,
    Bow,
    Candle,
    Recorder,
    Food,
    Potion,
    Rod,
    Raft,
    Book,
    Ring,
    Ladder,
    MagicKey,
    Bracelet,
    Letter,
    Compass, // unused but need to keep for the index values.
    Map, // unused but need to keep for the index values.
    Compass9, // unused but need to keep for the index values.
    Map9, // unused but need to keep for the index values.
    Clock,
    Rupees,
    Keys,
    HeartContainers,
    PartialHeart_Unused,
    TriforcePieces,
    PowerTriforce,
    Boomerang,
    MagicShield,
    MaxBombs,
    RupeesToAdd,
    RupeesToSubtract,

    // Added
    MaxConcurrentBombs,
    MaxConcurrentFire,
    MaxConcurrentBoomerangs,
    MaxConcurrentArrows,
    MaxConcurrentSwordShots,
    MaxConcurrentMagicWaves,
    MaxRupees,

    MaxItems
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SoundEffect
{
    Sea,
    SwordWave,
    BossHit,
    Door,
    PlayerHit,
    BossRoar1,
    BossRoar2,
    BossRoar3,
    Cursor,
    RoomItem,
    Secret,
    Item,
    MonsterDie,
    Sword,
    Boomerang,
    Fire,
    Stairs,
    Bomb,
    Parry,
    MonsterHit,
    MagicWave,
    KeyHeart,
    Character,
    PutBomb,
    LowHp,

    MAX
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TileAction
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
    Recorder,
    RecorderDestination,
    Item,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SongId
{
    Intro,
    Ending,
    Overworld,
    Underworld,
    ItemLift,
    Triforce,
    Ganon,
    Level9,
    GameOver,
    Death,
    Recorder,
    Zelda,

    MAX
}

public enum BlockObjType
{
    Cave = 0x0C,
    Ground = 0x0E,
    Stairs = 0x12,
    Rock = 0x13,
    Headstone = 0x14,

    Block = 0,
    Tile = 1,
    UnderworldStairs = 4,

    TileRock = 0xC8,
    TileHeadstone = 0xBC,
    TileBlock = 0xB0,
    TileWallEdge = 0xF6,
}

public enum TileBehavior
{
    None = -1,

    GenericWalkable,
    Sand,
    SlowStairs,
    Stairs,

    Doorway,
    Water,
    GenericSolid,
    Cave,
    Ghost0,
    Ghost1,
    Ghost2,
    Ghost3,
    Ghost4,
    Ghost5,
    Ghost6,
    Ghost7,
    Ghost8,
    Ghost9,
    GhostA,
    GhostB,
    GhostC,
    GhostD,
    GhostE,
    GhostF,
    Armos0,
    Armos1,
    Armos2,
    Armos3,
    Armos4,
    Armos5,
    Armos6,
    Armos7,
    Armos8,
    Armos9,
    ArmosA,
    ArmosB,
    ArmosC,
    ArmosD,
    ArmosE,
    ArmosF,
    Door,
    Wall,

    Max,

    FirstWalkable = GenericWalkable,
    FirstSolid = Doorway,
}

[Flags]
public enum Direction
{
    None = 0,
    Right = 1,
    Left = 2,
    Down = 4,
    Up = 8,
    DirectionMask = 0x0F,
    ShoveMask = 0x80, // JOE: TODO: Not sure what this is.
    FullMask = 0xFF,
    VerticalMask = Down | Up,
    HorizontalMask = Left | Right,
    OppositeVerticals = VerticalMask,
    OppositeHorizontals = HorizontalMask,
}

public readonly record struct MonsterEntry(ObjType ObjType, int Count = 1, Point? Point = null)
{
    [ThreadStatic]
    private static List<MonsterEntry>? _temporaryList;

    public static ImmutableArray<MonsterEntry> ParseMonsters(string? monsterList, out int zoraCount)
    {
        zoraCount = 0;
        if (string.IsNullOrEmpty(monsterList)) return [];

        var parser = new StringParser();
        var list = _temporaryList ??= [];
        list.Clear();

        var monsterListSpan = monsterList.AsSpan();
        for (; parser.Index < monsterListSpan.Length;)
        {
            parser.SkipOptionalWhiteSpace(monsterListSpan);
            var monsterName = parser.ExpectWord(monsterListSpan);
            var count = parser.TryExpectChar(monsterListSpan, '*')
                ? parser.ExpectInt(monsterListSpan)
                : 1;

            if (!Enum.TryParse<ObjType>(monsterName, true, out var type))
            {
                throw new Exception($"Unknown monster type: {monsterName}");
            }

            if (type == ObjType.Zora)
            {
                zoraCount += count;
            }
            else
            {
                for (var j = 0; j < count; j++) list.Add(new MonsterEntry(type));
            }

            if (!parser.TryExpectChar(monsterListSpan, ',')) break;
        }

        return list.ToImmutableArray();
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.Append(ObjType.ToString());

        if (Point != null)
        {
            sb.Append("[X=");
            sb.Append(Point.Value.X);
            sb.Append(",Y=");
            sb.Append(Point.Value.Y);
            sb.Append(']');
        }

        if (Count > 1)
        {
            sb.Append('*');
            sb.Append(Count);
        }

        return sb.ToString();
    }
}