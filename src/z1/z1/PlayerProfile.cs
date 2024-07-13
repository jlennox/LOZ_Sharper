using System.Runtime.InteropServices;

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

    public bool GetItemState() => (Data & ItemState) != 0;
    public void SetItemState() => Data |= ItemState;
    public bool GetShortcutState() => (Data & ShortcutState) != 0;
    public void SetShortcutState() => Data |= ShortcutState;
    public bool GetSecretState() => (Data & SecretState) != 0;
    public void SetSecretState() => Data |= SecretState;
    public int GetObjCount() => (Data & CountMask) >> CountShift;
    public void SetObjCount(int count) => Data = (byte)((Data & ~CountMask) | (count << CountShift));
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal sealed class UWRoomFlags
{
    public const byte ItemState = 0x10;
    public const byte VisitState = 0x20;
    public const byte CountMask = 0xC0;
    public const byte CountShift = 6;

    private byte Data;

    public bool GetItemState() => (Data & ItemState) != 0;
    public void SetItemState() => Data |= ItemState;
    public bool GetVisitState() => (Data & VisitState) != 0;
    public void SetVisitState() => Data |= VisitState;
    public bool GetDoorState(Direction dir) => (Data & (int)dir) != 0;
    public void SetDoorState(Direction dir) => Data |= (byte)dir;
    public int GetObjCount() => (Data & CountMask) >> CountShift;
    public void SetObjCount(byte count) => Data = (byte)((Data & ~CountMask) | (byte)(count << CountShift));
}

internal sealed class PlayerProfile
{
    public const int MaxNameLength = 8;
    public const int DefaultHearts = 3;
    public const int DefaultBombs  = 8;

    public byte[] Name = new byte[MaxNameLength];
    public int NameLength;
    public int Quest;
    public int Deaths;
    public ItemSlot SelectedItem;
    public int Hearts;
    public Dictionary<ItemSlot, int> Items = new();
    public OWRoomFlags[] OverworldFlags = new OWRoomFlags[Global.LevelBlockRooms];
    public UWRoomFlags[] LevelFlags1 = new UWRoomFlags[Global.LevelBlockRooms];
    public UWRoomFlags[] LevelFlags2 = new UWRoomFlags[Global.LevelBlockRooms];

    public PlayerProfile()
    {
        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            Items[slot] = 0;
        }
    }

    public int GetItem(ItemSlot slot) => Items[slot];

    public int GetMaxHeartsValue()
    {
        return GetMaxHeartsValue(Items[ItemSlot.HeartContainers]);
    }

    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
};
