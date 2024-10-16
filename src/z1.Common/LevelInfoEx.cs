using System.Diagnostics;
using System.Text.Json.Serialization;
using z1.Common.Data;

namespace z1.Common;

public record PointXY(int X, int Y)
{
    public PointXY() : this(0, 0) { }
}

public sealed class LevelInfoEx
{
    public required byte[] OWPondColors { get; init; }
    public required CavePaletteSet CavePalette { get; init; }
    public required ShopSpec[] CaveSpec { get; init; }
    public required Dictionary<ObjType, ObjectAttribute> Attributes { get; init; }
    public required int[][] LevelPersonStringIds { get; init; }
    public required PointXY[] SpawnSpot { get; init; }

    public ObjectAttribute GetObjectAttribute(ObjType type)
    {
        if (!Attributes.TryGetValue(type, out var objAttr))
        {
            // throw new ArgumentOutOfRangeException(nameof(type), type, "Unable to locate object attributes.");
            // This is mutable which makes me hate not instancing something new here :)
            return ObjectAttribute.Default;
        }
        return objAttr;
    }
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
[TiledSelectableEnum]
public enum WorldOptions
{
    [TiledIgnore] None = 0,
    AllowWhirlwind = 1 << 0,
}

public sealed class WorldSettings
{
    public GameWorldType WorldType { get; set; }
    public WorldOptions Options { get; set; }
    public byte[][] Palettes { get; set; }
    // Player's position when enter in the starting room.
    // public byte StartRoomId { get; set; }
    // public byte TriforceRoomId { get; set; }
    // public byte BossRoomId { get; set; }
    public SongId SongId { get; set; }
    public int LevelNumber { get; set; }
    // public byte EffectiveLevelNumber { get; set; }
    // public byte DrawnMapOffset { get; set; }
    // public byte[] CellarRoomIds { get; set; }
    // public byte[] ShortcutPosition { get; set; }
    // public byte[] DrawnMap { get; set; }
    // public byte[] Padding { get; set; }
    public byte[][][] OutOfCellarPalette { get; set; }
    public byte[][][] InCellarPalette { get; set; }
    public byte[][][] DarkPalette { get; set; }
    public byte[][][] DeathPalette { get; set; }

    [TiledIgnore, JsonIgnore] public bool AllowWhirlwind => Options.HasFlag(WorldOptions.AllowWhirlwind);
}

[Flags]
public enum SoundFlags
{
    None = 0,
    PlayIfQuietSlot = 1,
}

[DebuggerDisplay("{Filename}")]
public sealed class SongInformation
{
    public int Track { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    public int Slot { get; set; }
    public int Priority { get; set; }
    public SoundFlags Flags { get; set; }
    public string Filename { get; set; }

    [JsonIgnore] public float StartSeconds => Start * (1 / 60f);
    [JsonIgnore] public float EndSeconds => End * (1 / 60f);

    [JsonIgnore] public bool PlayIfQuietSlot => Flags.HasFlag(SoundFlags.PlayIfQuietSlot);
}

public sealed class CavePaletteSet
{
    public byte[] PaletteA { get; set; }
    public byte[] PaletteB { get; set; }

    public byte[] GetByIndex(int index) => index switch
    {
        0 => PaletteA,
        1 => PaletteB,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

public sealed class ObjectAttribute
{
    public static readonly ObjectAttribute Default = new();

    public int HitPoints { get; set; }
    public int Damage { get; set; }
    public int ItemDropClass { get; set; }
    public bool HasCustomCollision { get; set; }
    public bool IsInvincibleToWeapons { get; set; }
    public bool IsHalfWidth { get; set; }
    public bool HasWorldCollision { get; set; }
    public bool Unknown10 { get; set; }
    public bool Unknown80 { get; set; }
}

[Flags]
// [JsonConverter(typeof(JsonStringEnumConverter))]
[JsonConverter(typeof(TiledJsonSelectableEnumConverter<CaveSpecOptions>))]
[TiledSelectableEnum]
public enum CaveSpecOptions
{
    None = 0,
    // If the prices listed have a "-" prefix.
    ShowNegative = 1 << 0,
    // If prices are shown.
    ShowNumbers = 1 << 1,
    ShowItems = 1 << 2,
    Pay = 1 << 3,
    PickUp = 1 << 4,
    ControlsShutterDoors = 1 << 5,
    ControlsBlockingWall = 1 << 6,
    // Check they have the item when they enter (level 9 entrance)
    EntranceCheck = 1 << 7,
    // Charge them when they enter (overworld mugger rooms)
    EntranceCost = 1 << 8,
    // Determines if you can only get one item from here per play through. IE, a take any cave.
    Persisted = 1 << 9,
}

public enum PersonItemRequirementEffect { RemovePerson, UpgradeItem }
public enum PersonItemRequirementType { Check, Consumes }

public sealed class PersonItemRequirement
{
    public PersonItemRequirementType RequirementType { get; set; }
    public PersonItemRequirementEffect Effect { get; set; }
    public ItemSlot Item { get; set; }
    public int RequiredLevel { get; set; }

    public int? UpgradeLevel { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaveType { None = 0, Items = 0x79, Shortcut = 0x7A, }

public sealed class ShopSpec
{
    public DwellerType DwellerType { get; set; }
    [TiledIgnore]
    public CaveId? CaveId { get; set; }
    public CaveType CaveType { get; set; }
    public PersonType PersonType { get; set; }
    [JsonConverter(typeof(TiledJsonSelectableEnumConverter<CaveSpecOptions>))]
    public CaveSpecOptions Options { get; set; }
    public string? Text { get; set; }
    public PersonItemRequirement? RequiredItem { get; set; }
    [TiledIgnore, JsonIgnore]
    public ShopItem[]? Items { get; set; }
    public ItemSlot? EntranceCheckItem { get; set; }
    public int? EntranceCheckAmount { get; set; }

    [TiledIgnore, JsonIgnore] public bool ShowNegative => HasOption(CaveSpecOptions.ShowNegative);
    [TiledIgnore, JsonIgnore] public bool ShowNumbers => HasOption(CaveSpecOptions.ShowNumbers);
    [TiledIgnore, JsonIgnore] public bool ShowItems => HasOption(CaveSpecOptions.ShowItems);
    [TiledIgnore, JsonIgnore] public bool IsPay => HasOption(CaveSpecOptions.Pay);
    [TiledIgnore, JsonIgnore] public bool IsPickUp => HasOption(CaveSpecOptions.PickUp);
    [TiledIgnore, JsonIgnore] public bool DoesControlsShutters => HasOption(CaveSpecOptions.ControlsShutterDoors);
    [TiledIgnore, JsonIgnore] public bool DoesControlsBlockingWall => HasOption(CaveSpecOptions.ControlsBlockingWall);
    [TiledIgnore, JsonIgnore] public bool HasEntranceCheck => HasOption(CaveSpecOptions.EntranceCheck);
    [TiledIgnore, JsonIgnore] public bool HasEntranceCost => HasOption(CaveSpecOptions.EntranceCost);
    [TiledIgnore, JsonIgnore] public bool IsPersisted => HasOption(CaveSpecOptions.Persisted);

    public ShopSpec Clone()
    {
        var clone = (ShopSpec)MemberwiseClone();
        if (Items != null && clone.Items != null)
        {
            for (var i = 0; i < clone.Items.Length; i++) clone.Items[i] = Items[i].Clone();
        }
        return clone;
    }

    public bool HasOption(CaveSpecOptions option) => Options.HasFlag(option);
    public void ClearOptions(CaveSpecOptions option) => Options &= ~option;
}

[Flags]
// [JsonConverter(typeof(JsonStringEnumConverter))]
[TiledSelectableEnum]
[JsonConverter(typeof(TiledJsonSelectableEnumConverter<CaveShopItemOptions>))]
public enum CaveShopItemOptions
{
    None = 0,
    ShowNegative = 1 << 0,
    // It doesn't cost anything, it just checks if you have it.
    CheckCost = 1 << 1,
    ShowCostingItem = 1 << 2,
    // Sets the value of the item instead of adding it. IE,
    // 4 bombs with "SetItem" will set the bomb count to 4, without it, it'll add 4 bombs.
    SetItem = 1 << 3,
    Gambling = 1 << 4,
}

[TiledClass]
public sealed class ShopItem
{
    [JsonConverter(typeof(TiledJsonSelectableEnumConverter<CaveShopItemOptions>))]
    public CaveShopItemOptions Options { get; set; }
    public ItemId ItemId { get; set; }
    public int ItemAmount { get; set; }
    public int Cost { get; set; }
    public ItemSlot Costing { get; set; }
    public string? Hint { get; set; }
    // Used by MaxBombs to fill the bomb count to the max.
    public ItemSlot? FillItem { get; set; }

    [JsonIgnore] public bool ShowNegative => HasOption(CaveShopItemOptions.ShowNegative);

    public ShopItem Clone()
    {
        return (ShopItem)MemberwiseClone();
    }

    public bool HasOption(CaveShopItemOptions option) => Options.HasFlag(option);
}