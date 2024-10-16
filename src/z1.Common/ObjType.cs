using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Text;
using System.Text.Json;
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
    [TiledIgnore] Unknown1__,
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
    [TiledIgnore] Unknown5__,
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
    Princess,
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

    [TiledIgnore] Person1,
    [TiledIgnore] Person2,
    [TiledIgnore] Person3,
    [TiledIgnore] Person4,
    [TiledIgnore] Person5,
    [TiledIgnore] Person6,
    [TiledIgnore] Person7,
    [TiledIgnore] Person8,

    [TiledIgnore] FlyingRock,
    [TiledIgnore] Unknown54__,
    [TiledIgnore] Fireball,
    [TiledIgnore] Fireball2,
    [TiledIgnore] PlayerSwordShot,

    OldMan,
    OldWoman,
    Merchant,
    FriendlyMoblin,

    [TiledIgnore] MagicWave = OldMan,
    [TiledIgnore] MagicWave2 = OldWoman,
    [TiledIgnore] Arrow = FriendlyMoblin,

    [TiledIgnore] Boomerang,
    [TiledIgnore] DeadDummy,
    [TiledIgnore] FluteSecret,
    [TiledIgnore] Ladder,
    [TiledIgnore] Item,

    Dock,
    Rock,
    RockWall,
    Tree,
    Headstone,

    [TiledIgnore] Unknown66__,
    [TiledIgnore] Unknown67__,
    Block,
    [TiledIgnore] Unknown69__,

    [TiledIgnore] Cave1,
    [TiledIgnore] Cave2,
    [TiledIgnore] Cave3WhiteSword,
    [TiledIgnore] Cave4MagicSword,
    [TiledIgnore] Cave5Shortcut,
    [TiledIgnore] Cave6,
    [TiledIgnore] Cave7,
    [TiledIgnore] Cave8,
    [TiledIgnore] Cave9,
    [TiledIgnore] Cave10,
    [TiledIgnore] Cave11MedicineShop,
    [TiledIgnore] Cave12LostHillsHint,
    [TiledIgnore] Cave13LostWoodsHint,
    [TiledIgnore] Cave14,
    [TiledIgnore] Cave15,
    [TiledIgnore] Cave16,
    [TiledIgnore] Cave17,
    [TiledIgnore] Cave18,
    [TiledIgnore] Cave19,
    [TiledIgnore] Cave20,

    [TiledIgnore] Bomb,
    [TiledIgnore] PlayerSword,
    [TiledIgnore] Fire,
    [TiledIgnore] Rod,
    [TiledIgnore] Food,

    [TiledIgnore] Player,

    [TiledIgnore] PersonEnd = Person8 + 1,
    [TiledIgnore] PersonTypes = PersonEnd - Person1,
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
public enum DwellerType
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
    None = -1,
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
    ArgumentItemId,

    [TiledIgnore] MAX = 0x3F,
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
    Text,
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
    [TiledIgnore] Compass, // unused but need to keep for the index values.
    [TiledIgnore] Map, // unused but need to keep for the index values.
    [TiledIgnore] Compass9, // unused but need to keep for the index values.
    [TiledIgnore] Map9, // unused but need to keep for the index values.
    Clock,
    Rupees,
    Keys,
    HeartContainers,
    [TiledIgnore] PartialHeart_Unused,
    [TiledIgnore] TriforcePieces,
    PowerTriforce,
    Boomerang,
    MagicShield,
    MaxBombs,
    RupeesToAdd,
    RupeesToSubtract,

    // Added
    MaxConcurrentProjectiles,
    MaxRupees,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SoundEffect
{
    None = -1,
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

    [TiledIgnore] MAX
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DoorType { Open, Wall, FalseWall, FalseWall2, Bombable, Key, Key2, Shutter, None }

public enum TileAction
{
    None,
    Push,
    Bomb,
    Burn,
    PushHeadstone,
    Ladder,
    Raft,
    Cave,
    Stairs,
    Ghost,
    Armos,
    PushBlock,

    // These ones aren't real :)
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
    Princess,

    [TiledIgnore] MAX
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlockType
{
    Dock = 0x0B,
    Cave = 0x0C,
    Ground = 0x0E,
    Stairs = 0x12,
    Rock = 0x13,
    Headstone = 0x14,

    Block = 0,
    Tile = 1,
    UnderworldStairs = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TileType
{
    Tile = 1,
    Ground = 0x0E,
    Rock = 0xC8,
    Headstone = 0xBC,
    Block = 0xB0,
    WallEdge = 0xF6,
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
    Door,
    Wall,

    FirstWalkable = GenericWalkable,
    FirstSolid = Doorway,
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Direction
{
    None = 0,
    Right = 1,
    Left = 2,
    Down = 4,
    Up = 8,
    [TiledIgnore] DirectionMask = 0x0F,
    [TiledIgnore] ShoveMask = 0x80, // JOE: TODO: Not sure what this is.
    [TiledIgnore] FullMask = 0xFF,
    [TiledIgnore] VerticalMask = Down | Up,
    [TiledIgnore] HorizontalMask = Left | Right,
    [TiledIgnore] OppositeVerticals = VerticalMask,
    [TiledIgnore] OppositeHorizontals = HorizontalMask,
}

public readonly record struct MonsterEntry(ObjType ObjType, bool IsRingleader = false, int Count = 1, Point? Point = null)
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
            int? pointX = null;
            int? pointY = null;
            var isRingleader = false;

            if (parser.TryExpectChar(monsterListSpan, '['))
            {
                while (true)
                {
                    var param = parser.ExpectWord(monsterListSpan); // JOE: TODO: Case sensitive.
                    switch (param)
                    {
                        case nameof(System.Drawing.Point.X):
                            parser.ExpectChar(monsterListSpan, '=');
                            pointX = parser.ExpectInt(monsterListSpan);
                            break;
                        case nameof(System.Drawing.Point.Y):
                            parser.ExpectChar(monsterListSpan, '=');
                            pointY = parser.ExpectInt(monsterListSpan);
                            break;
                        case nameof(IsRingleader):
                            isRingleader = true;
                            break;
                        default: throw new Exception($"Unsupported parameter \"{param}\" in monster list \"{monsterList}\"");
                    }

                    if (!parser.TryExpectChar(monsterListSpan, ',')) break;
                }

                parser.ExpectChar(monsterListSpan, ']');
            }

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
                Point? point = pointX != null && pointY != null ? new Point(pointX.Value, pointY.Value) : null;
                for (var j = 0; j < count; j++) list.Add(new MonsterEntry(type, isRingleader, 1, point));
            }

            if (!parser.TryExpectChar(monsterListSpan, ',')) break;
        }

        return list.ToImmutableArray();
    }

    public override string ToString()
    {
        // Only runs in the extractor, so allocations are not important.
        var sb = new StringBuilder();

        sb.Append(ObjType.ToString());

        if (Point != null || IsRingleader)
        {
            sb.Append('[');
            var hasValue = false;
            if (Point != null)
            {
                sb.Append($"{nameof(System.Drawing.Point.X)}= ");
                sb.Append(Point.Value.X);
                sb.Append($",{nameof(System.Drawing.Point.Y)}=");
                sb.Append(Point.Value.Y);
                hasValue = true;
            }
            if (IsRingleader)
            {
                if (hasValue) sb.Append(',');
                sb.Append(nameof(IsRingleader));
            }
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

public static class CommonOverworldRoomName
{
    public const string Cave = nameof(Cave);
    public const string Shortcut = nameof(Shortcut);
}

public static class CommonUnderworldRoomName
{
    public const string ItemCellar = nameof(ItemCellar);
    public const string Transport = nameof(Transport);
}

public enum DoorState { Open, Locked, Shutter, Wall, Bombed, None }

[JsonConverter(typeof(Converter))]
public readonly record struct DoorTileIndexKey(Direction Direction, DoorState Type)
{
    public class Converter : JsonConverter<DoorTileIndexKey>
    {
        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] DoorTileIndexKey value, JsonSerializerOptions options)
        {
            writer.WritePropertyName($"{value.Direction}/{value.Type}");
        }

        public override DoorTileIndexKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            var parser = new StringParser();
            var direction = parser.ExpectEnum<Direction>(s);
            parser.ExpectChar(s, '/');
            var type = parser.ExpectEnum<DoorState>(s);
            return new DoorTileIndexKey(direction, type);
        }

        public override DoorTileIndexKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return default;
        }

        public override void Write(Utf8JsonWriter writer, DoorTileIndexKey value, JsonSerializerOptions options) { }
    }
}

// Sadly, we can't json serialize T[,]
public readonly record struct Array2D<T>(T[] Entries, int Width, int Height)
{
    public T this[int x, int y] => Entries[y * Width + x];

    public Span<T> Row(int y) => Entries.AsSpan(y * Width, Width);

    public void Blit(Array2D<T> source, int x, int y)
    {
        for (var srcY = 0; srcY < source.Height; srcY++)
        {
            var sourceRow = source.Row(srcY);
            var destRow = Row(y + srcY);
            sourceRow.CopyTo(destRow[x..]);
        }
    }
}

public sealed class DoorTileIndex : Dictionary<DoorTileIndexKey, Array2D<TiledTile>>
{
    public Array2D<TiledTile> Get(Direction direction, DoorState type)
    {
        return this[new DoorTileIndexKey(direction, type)];
    }
}
