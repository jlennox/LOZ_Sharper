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
    public Random ItemRandom { get; }
    public Random OverworldMapRandom { get; }

    private readonly int _doorRandomSeed;

    public List<GameRoom> RandomDungeonRoomList { get; } = new();

    private bool _isInitialized = false;
    private readonly List<ImmutableArray<MonsterEntry>> _monstersDungeonsA = new(); // Dungeons 1-4
    private readonly List<ImmutableArray<MonsterEntry>> _monstersDungeonsB = new(); // Dungeons 5-9
    private readonly List<ImmutableArray<MonsterEntry>> _monstersOverworld = new();
    private readonly List<ImmutableArray<MonsterEntry>> _monstersAll = new();

    public List<ItemId> DungeonItems { get; } = [];

    public RandomizerState(int seed, RandomizerFlags flags)
    {
        Seed = seed;
        Flags = flags;

        // To help preserve seed stability, avoid ever re-ordering these. Add new seeds to the end.
        var seedRandom = new Random(seed);
        RoomListRandom = new Random(seedRandom.Next());
        MonsterRandom = new Random(seedRandom.Next());
        _doorRandomSeed = seedRandom.Next();
        RoomRandom = new Random(seedRandom.Next());
        ItemRandom = new Random(seedRandom.Next());
        OverworldMapRandom = new Random(seedRandom.Next());

        RerandomizeItemList();
    }

    public void RerandomizeItemList()
    {
        DungeonItems.Clear();
        DungeonItems.AddRangeRandomly(DungeonStats.AllDungeonItems, ItemRandom);
    }

    public void Initialize(IEnumerable<GameWorld> dungeons)
    {
        if (_isInitialized) throw new Exception();
        if (RandomDungeonRoomList.Count > 0) throw new Exception();
        if (_monstersDungeonsA.Count > 0) throw new Exception();
        if (_monstersDungeonsB.Count > 0) throw new Exception();
        if (_monstersAll.Count > 0) throw new Exception();

        _isInitialized = true;
        foreach (var dungeon in dungeons)
        {
            RandomDungeonRoomList.AddRangeRandomly(dungeon.Rooms, RoomListRandom);

            var monsterList = dungeon.Settings.LevelNumber <= 4 ? _monstersDungeonsA : _monstersDungeonsB;
            foreach (var room in dungeon.Rooms)
            {
                monsterList.AddRandomly(room.Monsters, MonsterRandom);
                _monstersAll.AddRandomly(room.Monsters, MonsterRandom);
            }
        }
    }

    public Random CreateDoorRandom(int levelNumber) => new(_doorRandomSeed + levelNumber);

    private List<ImmutableArray<MonsterEntry>> GetMonsterListForDungeonNumber(int number)
    {
        return number switch
        {
            <= 4 => _monstersDungeonsA,
            <= 9 => _monstersDungeonsB,
            _ => _monstersOverworld
        };
    }

    public ImmutableArray<MonsterEntry> GetRoomMonsters(GameRoom room)
    {
        DemandInitialized();

        var monsterPool = GetMonsterListForDungeonNumber(room.GameWorld.Settings.LevelNumber);
        return monsterPool.Pop();
    }

    private void DemandInitialized()
    {
        if (!_isInitialized) throw new Exception("RandomizerState is not initialized.");
    }
}