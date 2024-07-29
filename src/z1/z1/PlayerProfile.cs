using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct OWRoomFlags
{
    private const int ItemState = 0x10;
    private const int ShortcutState = 0x20;
    private const int SecretState = 0x80;
    private const int CountMask = 7;
    private const int CountShift = 0;

    private byte Data;

    public readonly bool GetItemState() => (Data & ItemState) != 0;
    public void SetItemState() => Data |= ItemState;
    public readonly bool GetShortcutState() => (Data & ShortcutState) != 0;
    public void SetShortcutState() => Data |= ShortcutState;
    public readonly bool GetSecretState() => (Data & SecretState) != 0;
    public void SetSecretState() => Data |= SecretState;
    public readonly int GetObjCount() => (Data & CountMask) >> CountShift;
    public void SetObjCount(int count) => Data = (byte)((Data & ~CountMask) | (count << CountShift));
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UWRoomFlags
{
    private const byte ItemState = 0x10;
    private const byte VisitState = 0x20;
    private const byte CountMask = 0xC0;
    private const byte CountShift = 6;

    private byte Data;

    // JOE: TODO: Use getters/setters.
    public readonly bool GetItemState() => (Data & ItemState) != 0;
    public void SetItemState() => Data |= ItemState;

    public readonly bool GetVisitState() => (Data & VisitState) != 0;
    public void SetVisitState() => Data |= VisitState;

    public readonly bool GetDoorState(Direction dir) => (Data & (int)dir) != 0;
    public void SetDoorState(Direction dir) => Data |= (byte)dir;

    public readonly int GetObjCount() => (Data & CountMask) >> CountShift;
    public void SetObjCount(byte count) => Data = (byte)((Data & ~CountMask) | (byte)(count << CountShift));
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
        OverworldFlags ??= new OWRoomFlags[Global.LevelBlockRooms];
        LevelFlags1 ??= new UWRoomFlags[Global.LevelBlockRooms];
        LevelFlags2 ??= new UWRoomFlags[Global.LevelBlockRooms];

        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            Items[slot] = 0;
        }
    }

    public int GetItem(ItemSlot slot) => Items[slot];

    public bool PreventDarkRooms() => Game.Enhancements && GetItem(ItemSlot.Candle) >= 2;

    public int GetMaxHeartsValue() => GetMaxHeartsValue(Items[ItemSlot.HeartContainers]);
    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
}

internal static class PlayerProfileExtensions
{
    public static bool IsActive(this PlayerProfile? profile) => profile != null && !string.IsNullOrEmpty(profile.Name);
}
