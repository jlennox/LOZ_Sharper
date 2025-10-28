using System;
using System.Diagnostics.CodeAnalysis;

namespace z1.Randomizer;

internal static class RandomizerRoomExtensions
{
    public static bool HasFloorItem(this GameRoom room)
    {
        foreach (var _ in GetFloorItems(room)) return true;
        return false;
    }

    public static bool HasStairs(this GameRoom room)
    {
        foreach (var _ in GetStairs(room)) return true;
        return false;
    }

    public static IEnumerable<InteractableBlockObject> GetFloorItems(this GameRoom room)
    {
        foreach (var obj in room.InteractableBlockObjects)
        {
            if (obj.IsFloorItem()) yield return obj;
        }
    }

    public static IEnumerable<InteractableBlockObject> GetStairs(this GameRoom room)
    {
        foreach (var obj in room.InteractableBlockObjects)
        {
            if (obj.IsEntrance()) yield return obj;
        }
    }

    public static bool TryGetFirstStairs(this GameRoom room, [MaybeNullWhen(false)] out InteractableBlockObject stairs)
    {
        foreach (var obj in GetStairs(room))
        {
            stairs = obj;
            return true;
        }

        stairs = null;
        return false;
    }

    public static void SetFloorItem(this GameRoom room, ItemId itemId, ItemObjectOptions options = ItemObjectOptions.None)
    {
        foreach (var obj in GetFloorItems(room))
        {
            if (obj.Interaction.Item != null)
            {
                obj.Interaction.Item.Item = itemId;
                obj.Interaction.Item.Options = options;
                return;
            }
        }

        throw new Exception($"Room {room.UniqueId} has no floor item to set.");
    }

    public static void SetDungeonFloorItem(this GameRoom room, ItemId itemId)
    {
        SetFloorItem(room, itemId, ItemObjectOptions.IsRoomItem | ItemObjectOptions.MakeItemSound);
    }
}

internal static class RandomizerInteractableBlockObjectExtensions
{
    public static bool IsFloorItem(this InteractableBlockObject obj)
    {
        return obj.Interaction is { Item: not null };
    }

    public static bool IsEntrance(this InteractableBlockObject obj)
    {
        return obj.Interaction is { Entrance: not null };
    }

    public static bool IsPushBlock(this InteractableBlockObject obj)
    {
        return obj.Interaction is { Interaction: Interaction.Push or Interaction.PushVertical };
    }

    public static ItemId GetItem(this InteractableBlockObject obj)
    {
        var item = obj.Interaction.Item ?? throw new Exception();
        return item.Item;
    }
}

internal static class RandomizerWorldExtensions
{
    public static IEnumerable<GameWorld> GetAllDungeons(this GameWorld overworld, WorldStore store)
    {
        static IEnumerable<string> GetDungeonNames(GameRoom room)
        {
            foreach (var interactable in room.InteractableBlockObjects)
            {
                var entrance = interactable.Interaction.Entrance;
                if (entrance == null) continue;
                if (entrance.DestinationType != GameWorldType.Underworld) continue;

                yield return entrance.Destination;
            }
        }

        // Some dungeons, notably 6, have multiple entrances on a single map.
        var seen = new HashSet<string>();
        foreach (var room in overworld.Rooms)
        {
            foreach (var dungeonName in GetDungeonNames(room))
            {
                var dungeon = store.GetWorld(GameWorldType.Underworld, dungeonName, 1);
                if (!seen.Add(dungeon.Name)) continue;
                yield return dungeon;
            }
        }
    }
}