using System.Text.Json.Serialization;
using z1.Common.Data;

namespace z1.Common;

public record PointXY(int X, int Y)
{
    public PointXY() : this(0, 0) { }
}

public sealed class LevelInfoEx
{
    public byte[] OWPondColors { get; set; }
    public CavePaletteSet CavePalette { get; set; }
    public CaveSpec[] CaveSpec { get; set; }
    public Dictionary<ObjType, ObjectAttribute> ObjectAttribute { get; set; }
    public int[][] LevelPersonStringIds { get; set; }
    public PointXY[] SpawnSpot { get; set; }
}

public sealed class WorldInfo
{
    public byte[][] Palettes { get; set; }
    public byte StartY { get; set; }
    public byte StartRoomId { get; set; }
    public byte TriforceRoomId { get; set; }
    public byte BossRoomId { get; set; }
    public SongId SongId { get; set; }
    public byte LevelNumber { get; set; }
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
    ShowNegative = 1 << 0,
    ShowNumbers = 1 << 1,
    ShowItems = 1 << 2,
    Special = 1 << 3,
    Pay = 1 << 4,
    PickUp = 1 << 5,
    ControlsShutters = 1 << 6,
    ControlsBlockingWall = 1 << 7,
    // Check they have the item when they enter (level 9 entrance)
    EntranceCheck = 1 << 8,
    // Charge them when they enter (overworld mugger rooms)
    EntranceCost = 1 << 9,
    // Determines if you can only get one item from here per play through. IE, a take any cave.
    Persisted = 1 << 10,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaveType { None = 0, Items = 0x79, Shortcut = 0x7A, }

public sealed class CaveSpec
{
    public CaveDwellerType DwellerType { get; set; }
    [TiledIgnore]
    public CaveId? CaveId { get; set; }
    public CaveType CaveType { get; set; }
    public PersonType PersonType { get; set; }
    [JsonConverter(typeof(TiledJsonSelectableEnumConverter<CaveSpecOptions>))]
    public CaveSpecOptions Options { get; set; }
    public string Text { get; set; }
    public ItemId? RequiresItem { get; set; }
    [TiledIgnore, JsonIgnore]
    public CaveShopItem[]? Items { get; set; }
    public ItemSlot? EntranceCheckItem { get; set; }
    public int? EntranceCheckAmount { get; set; }

    [TiledIgnore, JsonIgnore] public bool ShowNegative => HasOption(CaveSpecOptions.ShowNegative);
    [TiledIgnore, JsonIgnore] public bool ShowNumbers => HasOption(CaveSpecOptions.ShowNumbers);
    [TiledIgnore, JsonIgnore] public bool ShowItems => HasOption(CaveSpecOptions.ShowItems);
    [TiledIgnore, JsonIgnore] public bool IsSpecial => HasOption(CaveSpecOptions.Special);
    [TiledIgnore, JsonIgnore] public bool IsPay => HasOption(CaveSpecOptions.Pay);
    [TiledIgnore, JsonIgnore] public bool IsPickUp => HasOption(CaveSpecOptions.PickUp);
    [TiledIgnore, JsonIgnore] public bool DoesControlsShutters => HasOption(CaveSpecOptions.ControlsShutters);
    [TiledIgnore, JsonIgnore] public bool DoesControlsBlockingWall => HasOption(CaveSpecOptions.ControlsBlockingWall);
    [TiledIgnore, JsonIgnore] public bool HasEntranceCheck => HasOption(CaveSpecOptions.EntranceCheck);
    [TiledIgnore, JsonIgnore] public bool HasEntranceCost => HasOption(CaveSpecOptions.EntranceCost);
    [TiledIgnore, JsonIgnore] public bool IsPersisted => HasOption(CaveSpecOptions.Persisted);

    public CaveSpec Clone()
    {
        var clone = (CaveSpec)MemberwiseClone();
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
public sealed class CaveShopItem
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

    public CaveShopItem Clone()
    {
        return (CaveShopItem)MemberwiseClone();
    }

    public bool HasOption(CaveShopItemOptions option) => Options.HasFlag(option);
}