using System;

namespace z1.Randomizer.Lazy;

// `GameObject`s reference a point or object in a specific world and location. These are used for path finding.
internal abstract record GameObject
{
    public abstract bool CanWalkTo<T>(ContextSource ctxSource, T destination) where T : GameObject;
}

internal record DungeonPointGameObject(int DungeonNumber, Point Room, Point Location) : GameObject
{
    public override bool CanWalkTo<T>(ContextSource ctxSource, T destination)
    {
        var dungeon = DungeonGameObject.Get(ctxSource, DungeonNumber);

        if (destination is OverworldPointGameObject overworldPoint)
        {
            var dungeonEntrance = dungeon.GetInteriorEntranceLocation();

            // First, navigate to the dungeon's entrance.
            if (!CanWalkTo(ctxSource, dungeonEntrance)) return false;

            // Now there, navigate from the dungeon's exit position onward.
            var dungeonExitLocation = dungeon.GetExteriorExitLocation();
            return dungeonExitLocation.CanWalkTo(ctxSource, destination);
        }

        if (destination is not DungeonPointGameObject dungeonPoint) throw new Exception();

        if (dungeonPoint.DungeonNumber != DungeonNumber)
        {
            // Walk out of this dungeon...
            var dungeonExitLocation = dungeon.GetExteriorExitLocation();
            if (!CanWalkTo(ctxSource, dungeonExitLocation)) return false;

            // ...and into the destination dungeon.
            var dungeonTo = DungeonGameObject.Get(ctxSource, dungeonPoint.DungeonNumber);
            var dungeonToEntrance = dungeonTo.GetExteriorEntranceLocation();
            if (!dungeonExitLocation.CanWalkTo(ctxSource, dungeonToEntrance)) return false;

            // Then finally to the destination.
            var dungeonToInteriorEntrance = dungeonTo.GetInteriorEntranceLocation();
            return dungeonToInteriorEntrance.CanWalkTo(ctxSource, destination);
        }

        return dungeon.CanWalkTo(Room, Location, dungeonPoint.Room, dungeonPoint.Location);
    }
}

internal record DungeonItemGameObject(int dungeonNumber, Point room, Point itemLocation, ItemId ItemId)
    : DungeonPointGameObject(dungeonNumber, room, itemLocation)
{
}

internal record OverworldPointGameObject(Point Room, Point Location) : GameObject
{
    public override bool CanWalkTo<T>(ContextSource ctxSource, T destination)
    {
        if (destination is DungeonPointGameObject dungeonObject)
        {
            var dungeon = DungeonGameObject.Get(ctxSource, dungeonObject.DungeonNumber);
            var dungeonEntrance = dungeon.GetExteriorEntranceLocation();

            if (!CanWalkTo(ctxSource, dungeonEntrance)) return false;

            var dungeonInteriorEntrance = dungeon.GetInteriorEntranceLocation();
            return dungeonInteriorEntrance.CanWalkTo(ctxSource, destination);
        }

        if (destination is not OverworldPointGameObject overworldPoint) throw new Exception();

        var overworld = OverworldGameObject.Get(ctxSource);
        return overworld.CanWalkTo(Room, Location, overworldPoint.Room, overworldPoint.Location);
    }
}

internal record OverworldItemGameObject(Point room, Point itemLocation, ItemId ItemId)
    : OverworldPointGameObject(room, itemLocation)
{
}