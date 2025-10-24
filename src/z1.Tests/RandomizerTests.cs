using System;
using z1.Actors;
using z1.Common;
using z1.IO;
using z1.Randomizer;
using z1.Render;

namespace z1.Tests;

internal abstract class RandomizerTestBase
{
    private static readonly Lazy<Game> _lazyGame = new(GetNormalGame);
    protected Game Game => _lazyGame.Value;

    [OneTimeSetUp]
    public void SetUp()
    {
        Asset.Initialize();
        Graphics.HeadlessInitialize();
    }

    [OneTimeTearDown]
    public void TearDown()
    {

    }

    protected static Game GetRandomizedGame()
    {
        return new Game(new GameIO(), PlayerProfile.CreateForRecording(12345)) { Headless = true };
    }

    protected static Game GetNormalGame()
    {
        return new Game(new GameIO(), PlayerProfile.CreateForRecording()) { Headless = true };
    }

    protected GameWorld GetDungeon(int quest, int dungeon)
    {
        var worldId = $"{quest:00}_{dungeon:00}";
        var world = Game.World.GetWorld(GameWorldType.Underworld, worldId);
        return world;
    }

    protected GameRoom GetUnderworldRoom(int quest, int dungeon, int x, int y)
    {
        var worldId = $"{quest:00}_{dungeon:00}";
        var roomId = $"Level{worldId}/{x},{y}";

        var world = Game.World.GetWorld(GameWorldType.Underworld, worldId);
        var room = world.GetRoomById(roomId);
        Game.World.LoadRoom(room);

        return room;
    }

    protected GameRoom GetOverworldRoom(int x, int y)
    {
        var worldId = "Overworld";
        var roomId = $"{worldId}/{x},{y}";
        var world = Game.World.GetWorld(GameWorldType.Overworld, worldId);
        var room = world.GetRoomById(roomId);
        Game.World.LoadRoom(room);

        return room;
    }
}

[TestFixture]
internal class RandomizerTests : RandomizerTestBase
{
    [Test]
    public void ShapeTest()
    {
        var dungeon = GetDungeon(0, 1);
        var state = new RandomizerState(0, new());
        DungeonState.Create(dungeon, state);
    }

    [Test]
    [TestCase(0, 1, 1, 1)]
    [TestCase(0, 2, 0, 1)]
    [TestCase(0, 3, 1, 0)]
    [TestCase(0, 8, 2, 0)]
    public void DungeonStatsTest(int quest, int level, int staircaseItemCount, int floorItemCount)
    {
        var dungeon = GetDungeon(quest, level);
        var actual = DungeonStats.Create(dungeon);
        Assert.That(actual.StaircaseItemCount, Is.EqualTo(staircaseItemCount));
        Assert.That(actual.FloorItemCount, Is.EqualTo(floorItemCount));
    }

    [Test]
    public void Create()
    {
        var game = new Game(new GameIO(), PlayerProfile.CreateForRecording(12345)) { Headless = true };
    }
}

internal readonly record struct PathRequirmentCase(RoomEntrances From, RoomEntrances To, PathRequirements Requirements)
{
    public KeyValuePair<DoorPair, PathRequirements> ToKeyValuePair()
    {
        return new KeyValuePair<DoorPair, PathRequirements>(DoorPair.Create(From, To), Requirements);
    }
}

[TestFixture]
internal class RoomRequirementsTests : RandomizerTestBase
{

    private void EnsureOverworld(int x, int y, params PathRequirmentCase[] expectedCases)
    {
        var room = GetOverworldRoom(x, y);
        var actual = RoomRequirements.Get(room).Paths.ToArray();
        var expected = expectedCases
            .Select(static t => t.ToKeyValuePair())
            .ToArray();
        Assert.That(actual, Is.EquivalentTo(expected));
    }


    private void EnsureUnderworld(int quest, int dungeon, int x, int y, params PathRequirmentCase[] expectedCases)
    {
        var room = GetUnderworldRoom(quest, dungeon, x, y);
        var actual = RoomRequirements.Get(room).Paths.ToArray();
        var expected = expectedCases
            .Select(static t => t.ToKeyValuePair())
            .ToArray();
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public void HorizontalRiverRoomUnderworld()
    {
        EnsureUnderworld(0, 9, 5, 6,
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Right, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Bottom, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Right, RoomEntrances.Bottom, PathRequirements.None),

            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Right, PathRequirements.Ladder),
            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Left, PathRequirements.Ladder),
            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Bottom, PathRequirements.Ladder)
        );
    }

    [Test]
    // Has staircase against right wall, can only top the others.
    [TestCase(0, 9, 3, 6, RoomEntrances.Top | RoomEntrances.Left | RoomEntrances.Bottom | RoomEntrances.Stairs, TestName = "Stairs Right")]
    // The Princess's room.
    [TestCase(0, 9, 2, 3, RoomEntrances.Bottom, TestName = "Princess")]
    // Old man room in level 1 -- old man blocks passage up.
    [TestCase(0, 1, 1, 4, RoomEntrances.Right | RoomEntrances.Left | RoomEntrances.Bottom, TestName = "Oldman")]
    public void ValidWallsUnderworld(int quest, int level, int x, int y, RoomEntrances expected)
    {
        var room = GetUnderworldRoom(quest, level, x, y);
        var directions = RoomRequirements.Get(room).ConnectableEntrances;
        Assert.That(directions, Is.EqualTo(expected));
    }

    [Test]
    // Ensure raft spot is detected as valid "Top" entrance.
    [TestCase(5, 5, RoomEntrances.Top | RoomEntrances.Left | RoomEntrances.Right | RoomEntrances.Bottom, TestName = "Raft")]
    public void ValidWallsOverworld(int x, int y, RoomEntrances expected)
    {
        var room = GetOverworldRoom(x, y);
        var directions = RoomRequirements.Get(room).ConnectableEntrances;
        Assert.That(directions, Is.EqualTo(expected));
    }

    [Test]
    public void RaftOverworld()
    {
        //    01234567890123456789012345678901
        //  0:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  1:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  2:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  3:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  4:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  5:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  6:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  7:~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        //  8:~~~~~~~~~~~~~~~~__~~~~~~~~~~~~~~
        //  9:~~~~~~~~~~~~~~~~__~~~~~~~~~~~~~~
        // 10:__________~~~~__________________
        // 11:__________~~~~__________________
        // 12:XXXX______~~~~______________XXXX
        // 13:XXXX______~~~~______________XXXX
        // 14:XXXX______~~~~______________XXXX
        // 15:XXXX______~~~~______________XXXX
        // 16:XXXX______~~~~______________XXXX
        // 17:XXXX______~~~~______________XXXX
        // 18:XXXX______~~~~______XX__XX__XXXX
        // 19:XXXX______~~~~______XX__XX__XXXX
        // 20:XXXX______~~~~______XX__XX__XXXX
        // 21:XXXX______~~~~______XX__XX__XXXX

        EnsureOverworld(5, 5,
            new PathRequirmentCase(RoomEntrances.Bottom, RoomEntrances.Left, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Bottom, RoomEntrances.Right, PathRequirements.None),

            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Right, PathRequirements.Raft),
            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Bottom, PathRequirements.Raft)
        );
    }

    [Test]
    public void BraceletOverworld()
    {
        // Map to the right of vanilla start.
        EnsureOverworld(9, 7,
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Right, PathRequirements.None),

            new PathRequirmentCase(RoomEntrances.Stairs, RoomEntrances.Left, PathRequirements.Bracelet),
            new PathRequirmentCase(RoomEntrances.Stairs, RoomEntrances.Right, PathRequirements.Bracelet)
        );
    }

    [Test]
    public void RecorderOverworld()
    {
        EnsureOverworld(2, 4,
            new PathRequirmentCase(RoomEntrances.Bottom, RoomEntrances.Stairs, PathRequirements.Recorder)
        );
    }

    [Test]
    public void BombableRiverOverworld()
    {
        EnsureOverworld(9, 1,
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Right, PathRequirements.None),

            new PathRequirmentCase(RoomEntrances.Stairs, RoomEntrances.Left, PathRequirements.Ladder),
            new PathRequirmentCase(RoomEntrances.Stairs, RoomEntrances.Right, PathRequirements.Ladder)
        );
    }

    [Test]
    public void StartingRoomOverworld()
    {
        EnsureOverworld(7, 7,
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Right, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Top, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Right, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Left, RoomEntrances.Stairs, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Top, RoomEntrances.Stairs, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Right, RoomEntrances.Stairs, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Entry, RoomEntrances.Left, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Entry, RoomEntrances.Top, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Entry, RoomEntrances.Right, PathRequirements.None),
            new PathRequirmentCase(RoomEntrances.Entry, RoomEntrances.Stairs, PathRequirements.None)
        );
    }

    [Test]
    // Has staircase against right wall, can only top the others.
    [TestCase(1, 7, 12, 5, RoomRequirementFlags.HasStaircase | RoomRequirementFlags.HasPushBlock)]
    public void CheckFlags(int quest, int level, int x, int y, RoomRequirementFlags expected)
    {
        var room = GetUnderworldRoom(quest, level, x, y);
        var flags = RoomRequirements.Get(room).Flags;
        Assert.That(flags, Is.EqualTo(expected));
    }
}