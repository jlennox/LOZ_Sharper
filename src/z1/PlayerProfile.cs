using System.Diagnostics;
using System.Text.Json.Serialization;
using z1.IO;

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

internal enum RoomMap
{
    Overworld,
    UnderworldA,
    UnderworldB,
}

internal sealed class RoomFlags
{
    public RoomMap RoomMap { get; set; }
    public int RoomX { get; set; }
    public int RoomY { get; set; }
    public bool ItemState { get; set; }
    public bool ShortcutState { get; set; } // Overworld
    public bool SecretState { get; set; } // Overworld
    public bool VisitState { get; set; } // Underworld
    public Dictionary<Direction, bool> DoorState { get; set; } = []; // Underworld
    public bool GetDoorState(Direction dir) => DoorState.TryGetValue(dir, out var state) && state;
    public bool SetDoorState(Direction dir) => DoorState[dir] = true;
    public int ObjectCount { get; set; }
}

internal sealed class PlayerProfiles : IInitializable
{
    public int Version { get; set; }
    public PlayerProfile[] Profiles { get; set; }

    public void Initialize()
    {
        if (Profiles == null!) Profiles = PlayerProfile.MakeDefaults();
    }

    public static PlayerProfiles MakeDefault()
    {
        return new PlayerProfiles
        {
            Profiles = PlayerProfile.MakeDefaults(),
        };
    }
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
    [JsonIgnore] // This is current HP which is runtime only, not saved. Max heart count is ItemSlot.HeartContainers
    public int Hearts { get; set; }
    public Dictionary<ItemSlot, int> Items { get; set; }
    public int UsedCheats { get; set; }
    public List<RoomFlags> RoomFlags { get; set; }

    [JsonConstructor]
    internal PlayerProfile()
    {
        if (Hearts < DefaultHearts) Hearts = DefaultHearts;
        Items ??= new Dictionary<ItemSlot, int>();
        RoomFlags ??= [];

        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            if (!Items.ContainsKey(slot))
            {
                Items[slot] = slot switch
                {
                    ItemSlot.HeartContainers => DefaultHearts,
                    ItemSlot.MaxBombs => DefaultBombs,
                    _ => 0,
                };
            }
        }
    }

    public RoomFlags GetRoomFlags(RoomMap map, int roomId)
    {
        var x = roomId % World.WorldWidth;
        var y = roomId / World.WorldWidth;
        return GetRoomFlags(map, x, y);
    }

    public RoomFlags GetRoomFlags(RoomMap map, int x, int y)
    {
        // Allow screen wrapping.
        x %= World.WorldWidth;
        y %= World.WorldHeight;
        var room = RoomFlags.FirstOrDefault(rf => rf.RoomMap == map && rf.RoomX == x && rf.RoomY == y);
        if (room == null)
        {
            room = new RoomFlags { RoomMap = map, RoomX = x, RoomY = y };
            RoomFlags.Add(room);
        }

        return room;
    }

    public static PlayerProfile MakeDefault() => new();
    public static PlayerProfile[] MakeDefaults() => Enumerable.Range(0, SaveFolder.MaxProfiles).Select(_ => MakeDefault()).ToArray();

    public int GetItem(ItemSlot slot) => Items[slot];
    public bool HasItem(ItemSlot slot) => GetItem(slot) != 0;
    public bool PreventDarkRooms(Game game) => game.Enhancements.RedCandleLightsDarkRooms && GetItem(ItemSlot.Candle) >= 2;
    public int GetMaxHeartsValue() => GetMaxHeartsValue(Items[ItemSlot.HeartContainers]);
    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
}

internal static class PlayerProfileExtensions
{
    public static bool IsActive(this PlayerProfile? profile) => profile != null && !string.IsNullOrEmpty(profile.Name);
}
