using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using z1.Actors;
using z1.IO;
using z1.Render;

namespace z1;

internal sealed class ObjectState
{
    public ItemId? ItemId { get; set; }
    public bool HasInteracted { get; set; }
    public bool ItemGot { get; set; }
}

internal enum PersistedDoorState { Normal, Open }

internal sealed class PersistedRoomState
{
    public bool IsDoorOpen(Direction dir) => DoorState.TryGetValue(dir, out var state) && state == PersistedDoorState.Open;
    public void SetDoorState(Direction dir, PersistedDoorState state) => DoorState[dir] = state;

    public bool VisitState { get; set; } // Used by underworld map.
    public Dictionary<Direction, PersistedDoorState> DoorState { get; set; } = []; // Underworld
    public int ObjectCount { get; set; }
    public Dictionary<string, ObjectState> ObjectState { get; set; } = [];

    public ObjectState GetObjectState(string id)
    {
        if (!ObjectState.TryGetValue(id, out var state))
        {
            state = new ObjectState();
            ObjectState[id] = state;
        }
        return state;
    }
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

internal sealed class ObjectStatistics
{
    public long Kills { get; set; }
    public long Seen { get; set; } // TODO
    public long DamageTaken { get; set; }
    public long DamageDone { get; set; } // TODO
}

internal sealed class PlayerStatistics
{
    public int Version { get; set; }
    public Dictionary<ItemSlot, long> ItemUses { get; set; }
    public Dictionary<DamageType, long> DamageDone { get; set; }
    public Dictionary<ObjType, ObjectStatistics> ObjectStatistics { get; set; }
    public int SaveCount { get; set; } // TODO
    public int CheatCount { get; set; } // TODO
    public int UWWallsBombed { get; set; }
    public int TreesBurned { get; set; }
    public int OWBlocksBombed { get; set; }
    public long RupeesCollected { get; set; }
    public long RupeesSpent { get; set; }

    public void Initialize()
    {
        DamageDone ??= new();
        ItemUses ??= new();
        ObjectStatistics ??= new();
    }

    public ObjectStatistics GetObjectStatistics(ObjType type)
    {
        if (!ObjectStatistics.TryGetValue(type, out var stats))
        {
            stats = new ObjectStatistics();
            ObjectStatistics[type] = stats;
        }

        return stats;
    }

    public ObjectStatistics GetObjectStatistics(Actor actor)
    {
        return GetObjectStatistics(actor.GetRootOwner().ObjType);
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
}

internal sealed class DungeonItems
{
    public Dictionary<ItemId, bool> Items { get; set; }

    public void Initialize()
    {
        Items ??= new();
    }

    public bool Get(ItemId item) => Items.ContainsKey(item);
    public bool Set(ItemId item) => Items[item] = true;
}

[JsonConverter(typeof(Converter))]
internal sealed record RoomDirectory(string WorldName, string RoomName)
{
    public override string ToString() => $"{WorldName}/{RoomName}";

    public class Converter : JsonConverter<RoomDirectory>
    {
        private static string GetJsonString(RoomDirectory directory)
        {
            return directory.ToString();
        }

        private static RoomDirectory ParseJsonString(string json)
        {
            var parser = new StringParser();
            var span = json.AsSpan();
            var world = parser.ReadUntil(span, '/').ToString();
            parser.ExpectChar(span, '/');
            var room = parser.ReadRemaining(span).ToString();
            return new RoomDirectory(world, room);
        }

        public override RoomDirectory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ParseJsonString(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, RoomDirectory value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(GetJsonString(value));
        }

        public override RoomDirectory ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ParseJsonString(reader.GetString());
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, RoomDirectory value, JsonSerializerOptions options)
        {
            writer.WritePropertyName(GetJsonString(value));
        }
    }
}

internal sealed class PersistedItems
{
    public const int DefaultHeartCount = 3;
    public const int DefaultMaxBombCount = 8;
    public const int DefaultMaxConcurrentProjectiles = 2;
    public const int DefaultMaxRupees = 255;
    public const int DefaultMaxBombs = 8;

    private static readonly Dictionary<ItemSlot, int> _defaultItems = new()
    {
        [ItemSlot.HeartContainers] = DefaultHeartCount,
        [ItemSlot.MaxConcurrentProjectiles] = DefaultMaxConcurrentProjectiles,
        [ItemSlot.MaxRupees] = DefaultMaxRupees,
        [ItemSlot.MaxBombs] = DefaultMaxBombs,
    };

    // Sadly, we can't keep this private, otherwise the json serializer won't be able to serialize it.
    [JsonInclude]
    internal Dictionary<ItemSlot, int> Items { get; set; } = [];

    public int Get(ItemSlot slot) => Items.GetValueOrDefault(slot);
    public bool Has(ItemSlot slot) => Get(slot) != 0;
    public void Set(ItemSlot slot, int value) => Items[slot] = value;
    public void Reset()
    {
        Items.Clear();
        Initialize();
    }

    public void Add(ItemSlot itemSlot, int amount)
    {
        var have = Get(itemSlot);
        var max = GetMax(itemSlot);
        Items[itemSlot] = Math.Clamp(have + amount, 0, max);
    }

    public int GetMax(ItemSlot slot)
    {
        return slot switch
        {
            ItemSlot.Bombs => Items[ItemSlot.MaxBombs],
            ItemSlot.Rupees => Items[ItemSlot.MaxRupees],
            _ => 0xFF,
        };
    }

    public void Initialize()
    {
        Items ??= [];

        foreach (var (slot, def) in _defaultItems)
        {
            Items.TryAdd(slot, def);
        }
    }
}

[DebuggerDisplay("{Name} ({Hearts})")]
internal sealed class PlayerProfile
{
    public const int MaxNameLength = 8;

    public int Version { get; set; }
    public string? Name { get; set; }
    public int Index { get; set; }
    // JOE: TODO: The profile does not control the quest. Instead, this should control what "world" the player is on.
    // public int Quest { get; set; }
    public int Deaths { get; set; }
    public ItemSlot SelectedItem { get; set; }
    [JsonIgnore] // This is current HP which is runtime only, not saved. Max heart count is ItemSlot.HeartContainers
    public int Hearts { get; set; }
    public PersistedItems Items { get; set; }
    public PlayerStatistics Statistics { get; set; }
    public Dictionary<string, PersistedRoomState> RoomState { get; set; } // Key: GameRoom.UniqueId
    public Dictionary<string, DungeonItems> DungeonItems { get; set; } // Key: GameWorld.UniqueId
    public TimeSpan Playtime { get; set; } // JOE: TODO

    public PlayerProfile()
    {
    }

    public static PlayerProfile CreateForRecording()
    {
        var profile = new PlayerProfile();
        profile.Initialize();
        return profile;
    }

    public void Initialize()
    {
        if (Hearts < PersistedItems.DefaultHeartCount) Hearts = PersistedItems.DefaultHeartCount;
        Items ??= new PersistedItems();
        RoomState ??= [];
        Statistics ??= new PlayerStatistics();
        DungeonItems ??= [];

        Items.Initialize();
        Statistics.Initialize();
    }

    public void Start()
    {
        Hearts = GetMaxHeartsValue(PersistedItems.DefaultHeartCount);
    }

    public PersistedRoomState GetRoomFlags(GameRoom room)
    {
        if (!RoomState.TryGetValue(room.UniqueId, out var roomFlags))
        {
            roomFlags = new PersistedRoomState();
            RoomState[room.UniqueId] = roomFlags;
        }

        return roomFlags;
    }

    public ObjectState GetObjectFlags(GameRoom room, InteractableBlockObject obj)
    {
        return GetObjectFlags(room, obj.Id);
    }

    public ObjectState GetObjectFlags(GameRoom room, string id)
    {
        var roomflags = GetRoomFlags(room);
        return roomflags.GetObjectState(id);
    }

    public DungeonItems GetDungeonItems(GameWorld dungeon)
    {
        if (!DungeonItems.TryGetValue(dungeon.UniqueId, out var items))
        {
            items = new DungeonItems();
            items.Initialize();
            DungeonItems[dungeon.UniqueId] = items;
        }

        return items;
    }

    public void SetDungeonItem(GameWorld dungeon, ItemId item) => GetDungeonItems(dungeon).Set(item);
    public bool GetDungeonItem(GameWorld dungeon, ItemId item)
    {
        if (dungeon.IsOverworld) return false;

        return GetDungeonItems(dungeon).Get(item);
    }

    public void SetPlayerColor()
    {
        ReadOnlySpan<byte> palette = [0x29, 0x32, 0x16];

        var value = Items.Get(ItemSlot.Ring);
        Graphics.SetColorIndexed(Palette.Player, 1, palette[value]);
    }

    public static PlayerProfile MakeDefault() => new();
    public static List<PlayerProfile> MakeDefaults() => new();

    public bool PreventDarkRooms(Game game) => game.Enhancements.RedCandleLightsDarkRooms && Items.Get(ItemSlot.Candle) >= 2;
    public int GetMaxHeartsValue() => GetMaxHeartsValue(Items.Get(ItemSlot.HeartContainers));
    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
    public bool IsFullHealth() => Hearts >= (Items.Get(ItemSlot.HeartContainers) << 8) - 0x80;
}

internal static class PlayerProfileExtensions
{
    public static bool IsActive([MaybeNullWhen(false)] this PlayerProfile profile) => profile != null && !string.IsNullOrEmpty(profile.Name);

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
