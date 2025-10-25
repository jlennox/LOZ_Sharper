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
}