using System;

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
            if (obj.Interaction.Item != null) yield return obj;
        }
    }

    public static IEnumerable<InteractableBlockObject> GetStairs(this GameRoom room)
    {
        foreach (var obj in room.InteractableBlockObjects)
        {
            if (obj.Interaction.Entrance != null) yield return obj;
        }
    }

    public static void SetFloorItem(this GameRoom room, ItemId itemId)
    {
        foreach (var obj in GetFloorItems(room))
        {
            if (obj.Interaction.Item != null)
            {
                obj.Interaction.Item.Item = itemId;
                return;
            }
        }

        throw new Exception($"Room {room.UniqueId} has no floor item to set.");
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
                var dungeon = store.GetWorld(GameWorldType.Underworld, dungeonName);
                if (!seen.Add(dungeon.Name)) continue;
                yield return dungeon;
            }
        }
    }
}