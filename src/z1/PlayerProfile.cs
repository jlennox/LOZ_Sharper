﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using z1.Actors;
using z1.IO;
using z1.Render;

namespace z1;

internal sealed class ObjectState
{
    public bool HasInteracted { get; set; }
    public bool ItemGot { get; set; }
}

internal sealed class RoomFlags
{
    public bool ItemState { get; set; } // true if item has been picked up.
    public bool ShortcutState { get; set; } // Overworld
    public bool SecretState { get; set; } // Overworld
    public bool VisitState { get; set; } // Underworld
    public Dictionary<Direction, bool> DoorState { get; set; } = []; // Underworld
    public bool GetDoorState(Direction dir) => DoorState.TryGetValue(dir, out var state) && state;
    public bool SetDoorState(Direction dir) => DoorState[dir] = true;
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

[DebuggerDisplay("{Name} ({Hearts})")]
internal sealed class PlayerProfile
{
    public const int MaxNameLength = 8;
    public const int DefaultHeartCount = 3;
    public const int DefaultMaxBombCount = 8;
    public const int DefaultBombCount = 2;
    public const int DefaultFireCount = 2;
    public const int DefaultBoomerangCount = 2;
    public const int DefaultArrowCount = 2;
    public const int DefaultShotCount = 2;
    public const int DefaultMaxRupees = 2;
    public const int DefaultMaxBombs = 8;

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
    public Dictionary<string, Dictionary<string, RoomFlags>> RoomFlags { get; set; }
    public Dictionary<int, DungeonItems> DungeonItems { get; set; }

    public PlayerProfile()
    {
    }

    public void Initialize()
    {
        if (Hearts < DefaultHeartCount) Hearts = DefaultHeartCount;
        Items ??= new Dictionary<ItemSlot, int>();
        RoomFlags ??= [];
        Statistics ??= new PlayerStatistics();
        DungeonItems ??= new Dictionary<int, DungeonItems>();
        if (Id == default) Id = Guid.NewGuid();

        foreach (var slot in Enum.GetValues<ItemSlot>())
        {
            if (!Items.ContainsKey(slot))
            {
                Items[slot] = slot switch
                {
                    ItemSlot.HeartContainers => DefaultHeartCount,
                    ItemSlot.MaxConcurrentBombs => DefaultBombCount,
                    ItemSlot.MaxConcurrentFire => DefaultFireCount,
                    ItemSlot.MaxConcurrentBoomerangs => DefaultBoomerangCount,
                    ItemSlot.MaxConcurrentArrows => DefaultArrowCount,
                    ItemSlot.MaxConcurrentSwordShots => DefaultShotCount,
                    ItemSlot.MaxConcurrentMagicWaves => DefaultShotCount,
                    ItemSlot.MaxRupees => DefaultMaxRupees,
                    ItemSlot.MaxBombs => DefaultMaxBombs,
                    _ => 0,
                };
            }
        }

        Statistics.Initialize();
    }

    public RoomFlags GetRoomFlags(GameRoom room)
    {
        if (!RoomFlags.TryGetValue(room.World.Id, out var worldFlags))
        {
            worldFlags = new Dictionary<string, RoomFlags>();
            RoomFlags[room.World.Id] = worldFlags;
        }

        if (!worldFlags.TryGetValue(room.Id, out var roomFlags))
        {
            roomFlags = new RoomFlags();
            worldFlags[room.Id] = roomFlags;
        }

        return roomFlags;
    }

    public ObjectState GetObjectFlags(GameRoom room, InteractiveGameObject obj)
    {
        var roomflags = GetRoomFlags(room);
        return roomflags.GetObjectState(obj.Id);
    }

    public DungeonItems GetDungeonItems(int dungeon)
    {
        if (!DungeonItems.TryGetValue(dungeon, out var items))
        {
            items = new DungeonItems();
            items.Initialize();
            DungeonItems[dungeon] = items;
        }

        return items;
    }

    public void SetDungeonItem(int dungeon, ItemId item) => GetDungeonItems(dungeon).Set(item);
    public bool GetDungeonItem(int dungeon, ItemId item)
    {
        if (dungeon == 0) return false; // overworld.
        return GetDungeonItems(dungeon).Get(item);
    }

    public void AddItem(ItemSlot itemSlot, int amount)
    {
        var have = Items[itemSlot];
        var max = GetMax(itemSlot);
        Items[itemSlot] = Math.Clamp(have + amount, 0, max);
    }

    public void SetPlayerColor()
    {
        ReadOnlySpan<byte> palette = [0x29, 0x32, 0x16];

        var value = Items[ItemSlot.Ring];
        Graphics.SetColorIndexed(Palette.Player, 1, palette[value]);
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

    public static PlayerProfile MakeDefault() => new();
    public static List<PlayerProfile> MakeDefaults() => new();

    public int GetItem(ItemSlot slot) => Items[slot];
    public bool HasItem(ItemSlot slot) => GetItem(slot) != 0;
    public bool PreventDarkRooms(Game game) => game.Enhancements.RedCandleLightsDarkRooms && GetItem(ItemSlot.Candle) >= 2;
    public int GetMaxHeartsValue() => GetMaxHeartsValue(Items[ItemSlot.HeartContainers]);
    public static int GetMaxHeartsValue(int heartContainers) => (heartContainers << 8) - 1;
    public bool IsFullHealth() => Hearts >= (Items[ItemSlot.HeartContainers] << 8) - 0x80;
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
