using System.Diagnostics;
using System.Text.Json.Serialization;
using z1.UI;

namespace z1;

internal enum ItemSlot
{
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
    Compass,
    Map,
    Compass9,
    Map9,
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

    MaxItems
}

internal class OWRoomFlags
{
    private const int ItemState = 0x10;
    private const int ShortcutState = 0x20;
    private const int SecretState = 0x80;
    private const int CountMask = 7;
    private const int CountShift = 0;

    private byte _data;

    // JOE: TODO: Use getters/setters.
    public bool GetItemState() => (_data & ItemState) != 0;
    public void SetItemState() => _data |= ItemState;

    public bool GetShortcutState() => (_data & ShortcutState) != 0;
    public void SetShortcutState() => _data |= ShortcutState;

    public bool GetSecretState() => (_data & SecretState) != 0;
    public void SetSecretState() => _data |= SecretState;

    public int GetObjCount() => (_data & CountMask) >> CountShift;
    public void SetObjCount(int count) => _data = (byte)((_data & ~CountMask) | (count << CountShift));
}

internal class UWRoomFlags
{
    private const byte ItemState = 0x10;
    private const byte VisitState = 0x20;
    private const byte CountMask = 0xC0;
    private const byte CountShift = 6;

    private byte _data;

    // JOE: TODO: Use getters/setters.
    public bool GetItemState() => (_data & ItemState) != 0;
    public void SetItemState() => _data |= ItemState;

    public bool GetVisitState() => (_data & VisitState) != 0;
    public void SetVisitState() => _data |= VisitState;

    public bool GetDoorState(Direction dir) => (_data & (int)dir) != 0;
    public void SetDoorState(Direction dir) => _data |= (byte)dir;

    public int GetObjCount() => (_data & CountMask) >> CountShift;
    public void SetObjCount(byte count) => _data = (byte)((_data & ~CountMask) | (byte)(count << CountShift));
}

[DebuggerDisplay("{Name} ({Hearts})")]
internal sealed class PlayerProfile
{
    public const int MaxNameLength = 8;
    public const int DefaultHearts = 3;
    public const int DefaultBombs = 8;

    public int Version { get; set; }
    public string? Name { get; set; }
    public int Index { get; set; }
    public int Quest { get; set; }
    public int Deaths { get; set; }
    public ItemSlot SelectedItem { get; set; }
    [JsonIgnore]
    public int Hearts { get; set; }
    public Dictionary<ItemSlot, int> Items { get; set; }
    public int UsedCheats { get; set; }
    public OWRoomFlags[] OverworldFlags { get; set; }
    public UWRoomFlags[] LevelFlags1 { get; set; }
    public UWRoomFlags[] LevelFlags2 { get; set; }

    public PlayerProfile()
    {
        if (Hearts == 0) Hearts = DefaultHearts;
        Items ??= new Dictionary<ItemSlot, int>();
        OverworldFlags ??= Enumerable.Range(0, Global.LevelBlockRooms).Select(_ => new OWRoomFlags()).ToArray();
        LevelFlags1 ??= Enumerable.Range(0, Global.LevelBlockRooms).Select(_ => new UWRoomFlags()).ToArray();
        LevelFlags2 ??= Enumerable.Range(0, Global.LevelBlockRooms).Select(_ => new UWRoomFlags()).ToArray();

        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            Items[slot] = 0;
        }
    }

    public static PlayerProfile[] MakeDefaults() => Enumerable.Range(0, SaveFolder.MaxProfiles).Select(_ => new PlayerProfile()).ToArray();

    public int GetItem(ItemSlot slot) => Items[slot];
    public bool PreventDarkRooms(Game game) => game.Enhancements && GetItem(ItemSlot.Candle) >= 2;
    public int GetMaxHeartsValue() => GetMaxHeartsValue(Items[ItemSlot.HeartContainers]);
    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
}

internal static class PlayerProfileExtensions
{
    public static bool IsActive(this PlayerProfile? profile) => profile != null && !string.IsNullOrEmpty(profile.Name);
}
