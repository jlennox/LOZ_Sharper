using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using z1.Actors;
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
    public List<PlayerProfile> Profiles { get; set; }

    public void Initialize()
    {
        if (Profiles == null!) Profiles = PlayerProfile.MakeDefaults();

        foreach (var profile in Profiles)
        {
            profile.Initialize();
        }
    }

    public static PlayerProfiles MakeDefault()
    {
        return new PlayerProfiles
        {
            Profiles = PlayerProfile.MakeDefaults(),
        };
    }
}

internal sealed class PlayerStatistics
{
    public int Version { get; set; }
    public Dictionary<ObjType, int> Kills { get; set; }
    public Dictionary<ItemSlot, int> ItemUses { get; set; }
    public Dictionary<DamageType, long> DamageDone { get; set; }
    public Dictionary<ObjType, long> DamageTaken { get; set; }
    public int SaveCount { get; set; } // TODO
    public int UWWallsBombed { get; set; }
    public int TreesBurned { get; set; }
    public int OWBlocksBombed { get; set; }
    public long RupeesCollected { get; set; }
    public long RupeesSpent { get; set; }

    public void Initialize()
    {
        Kills ??= new();
        DamageDone ??= new();
        ItemUses ??= new();
        DamageTaken ??= new();
    }

    public void AddKill(ObjType type)
    {
        var count = Kills.GetValueOrDefault(type) + 1;
        Kills[type] = count;
    }

    public void AddItemUse(ItemSlot slot)
    {
        var count = ItemUses.GetValueOrDefault(slot) + 1;
        ItemUses[slot] = count;
    }

    public void DealDamage(CollisionContext context)
    {
        var count = DamageDone.GetValueOrDefault(context.DamageType) + context.Damage;
        DamageDone[context.DamageType] = count;
    }

    public void TakeDamage(Actor actor, int amount)
    {
        // PersonEnd = FlyingRock
        var count = DamageTaken.GetValueOrDefault(actor.ObjType) + amount;
        DamageTaken[actor.ObjType] = count;
    }
}

[DebuggerDisplay("{Name} ({Hearts})")]
internal sealed class PlayerProfile
{
    public const int MaxNameLength = 8;
    public const int DefaultHearts = 3;
    public const int DefaultBombs = 8;

    public int Version { get; set; }
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public int Index { get; set; }
    public int Quest { get; set; }
    public int Deaths { get; set; }
    public ItemSlot SelectedItem { get; set; }
    [JsonIgnore] // This is current HP which is runtime only, not saved. Max heart count is ItemSlot.HeartContainers
    public int Hearts { get; set; }
    public Dictionary<ItemSlot, int> Items { get; set; }
    public PlayerStatistics Statistics { get; set; }
    public int UsedCheats { get; set; }
    public List<RoomFlags> RoomFlags { get; set; }

    public PlayerProfile()
    {
    }

    public void Initialize()
    {
        if (Hearts < DefaultHearts) Hearts = DefaultHearts;
        Items ??= new Dictionary<ItemSlot, int>();
        RoomFlags ??= [];
        Statistics ??= new PlayerStatistics();
        if (Id == default) Id = Guid.NewGuid();

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

        Statistics.Initialize();
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

    public void SetPlayerColor()
    {
        ReadOnlySpan<byte> palette = [0x29, 0x32, 0x16];

        var value = Items[ItemSlot.Ring];
        Graphics.SetColorIndexed(Palette.Player, 1, palette[value]);
    }

    public static PlayerProfile MakeDefault() => new();
    public static List<PlayerProfile> MakeDefaults() => new();

    public int GetItem(ItemSlot slot) => Items[slot];
    public bool HasItem(ItemSlot slot) => GetItem(slot) != 0;
    public bool PreventDarkRooms(Game game) => game.Enhancements.RedCandleLightsDarkRooms && GetItem(ItemSlot.Candle) >= 2;
    public int GetMaxHeartsValue() => GetMaxHeartsValue(Items[ItemSlot.HeartContainers]);
    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
}

internal static class PlayerProfileExtensions
{
    public static bool IsActive([MaybeNullWhen(false)] this PlayerProfile? profile) => profile != null && !string.IsNullOrEmpty(profile.Name);

    public static int GetIndex(this List<PlayerProfile> profiles, int page, int index)
    {
        return page * SaveFolder.MaxProfiles + index;
    }

    public static PlayerProfile? GetProfile(this List<PlayerProfile> profiles, int page, int index)
    {
        var profileIndex = GetIndex(profiles, page, index);
        return profileIndex >= profiles.Count
            ? null
            : profiles[profileIndex];
    }
}
