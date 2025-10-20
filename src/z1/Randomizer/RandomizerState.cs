using System;
using System.Collections.Immutable;

namespace z1.Randomizer;

// IDEA: Make an attribute that allows each property to specify its bit location, for the flag textualization.
internal sealed class RandomizerDungeonFlags
{
    public bool Rooms { get; set; } = true;
    public bool Shapes { get; set; } = true;
    public int ShapesSizeVariance { get; set; } = 2;
    public bool RandomizeMonsters { get; set; } = true;
    public bool AlwaysHaveCompass { get; set; } = true;
    public bool AlwaysHaveMap { get; set; } = true;
    public bool AllowFalseWalls { get; set; } = false;
}

internal sealed class RandomizerFlags
{
    public RandomizerDungeonFlags Dungeon { get; } = new();

    public void CheckIntegrity()
    {
        if (Dungeon is { Shapes: true, Rooms: false })
        {
            throw new InvalidOperationException("Cannot randomize dungeon shapes without randomizing rooms.");
        }
    }
}

internal sealed class RandomizerState
{
    public int Seed { get; }
    public RandomizerFlags Flags { get; }
    public Random RoomListRandom { get; }
    public Random MonsterRandom { get; }
    public Random RoomRandom { get; }

    public List<GameRoom> RandomDungeonRoomList { get; } = new();

    public List<ImmutableArray<MonsterEntry>> MonstersDungeonsA { get; } = new(); // Dungeons 1-4
    public List<ImmutableArray<MonsterEntry>> MonstersDungeonsB { get; } = new(); // Dungeons 5-9
    public List<ImmutableArray<MonsterEntry>> MonstersOverworld { get; } = new();
    public List<ImmutableArray<MonsterEntry>> MonstersAll { get; } = new();

    public List<ItemId> DungeonItems { get; } = [
        // ItemId.WoodSword,
        // ItemId.WhiteSword,
        // ItemId.MagicSword,
        // ItemId.Food,
        ItemId.Recorder,
        ItemId.BlueCandle,
        ItemId.RedCandle,
        // ItemId.WoodArrow,
        ItemId.SilverArrow,
        ItemId.Bow,
        ItemId.MagicKey,
        ItemId.Raft,
        ItemId.Ladder,
        // ItemId.PowerTriforce,
        ItemId.Rod,
        ItemId.Book,
        // ItemId.BlueRing,
        ItemId.RedRing,
        ItemId.Bracelet,
        // ItemId.Letter,
        ItemId.WoodBoomerang,
        ItemId.MagicBoomerang];

    public RandomizerState(int seed, RandomizerFlags flags)
    {
        Seed = seed;
        Flags = flags;

        var seedRandom = new Random(seed);
        RoomListRandom = new Random(seedRandom.Next());
        MonsterRandom = new Random(seedRandom.Next());
        RoomRandom = new Random(seedRandom.Next());

        DungeonItems.Shuffle(RoomRandom);
    }

    public void Initialize(IEnumerable<GameWorld> dungeons)
    {
        if (RandomDungeonRoomList.Count > 0) throw new Exception();
        if (MonstersDungeonsA.Count > 0) throw new Exception();
        if (MonstersDungeonsB.Count > 0) throw new Exception();
        if (MonstersAll.Count > 0) throw new Exception();

        foreach (var dungeon in dungeons)
        {
            RandomDungeonRoomList.AddRangeRandomly(dungeon.Rooms, RoomListRandom);

            var monsterList = dungeon.Settings.LevelNumber <= 4 ? MonstersDungeonsA : MonstersDungeonsB;
            foreach (var room in dungeon.Rooms)
            {
                monsterList.AddRandomly(room.Monsters, MonsterRandom);
                MonstersAll.AddRandomly(room.Monsters, MonsterRandom);
            }
        }
    }

    // All special rooms have been fit. Now strip all the interactions that make rooms "special" from them from the
    // remaining pool.
    public void NormalizeRemainingRooms()
    {
        var toremove = new List<InteractableBase>();
        foreach (var room in RandomDungeonRoomList)
        {
            toremove.Clear();

            foreach (var obj in room.InteractableBlockObjects)
            {
                if (obj.Interaction.Entrance != null)
                {
                    toremove.Add(obj.Interaction);

                    if (obj.Interaction.Interaction == Interaction.Revealed)
                    {
                        toremove.Add(room.GetRevealer(obj.Interaction));
                    }
                }
            }

            room.InteractableBlockObjects = room.InteractableBlockObjects
                .Where(t => !toremove.Contains(t.Interaction))
                .ToImmutableArray();
        }
    }
}