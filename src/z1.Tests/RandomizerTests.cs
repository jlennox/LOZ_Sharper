using System;
using System.Drawing.Text;
using z1.Common;
using z1.IO;
using z1.Randomizer;
using z1.Render;

namespace z1.Tests;

[TestFixture]
internal class RandomizerTests
{
    private static readonly Lazy<Game> _lazyGame = new(GetNormalGame);

    private Game Game => _lazyGame.Value;

    private static Game GetRandomizedGame()
    {
        return new Game(new GameIO(), PlayerProfile.CreateForRecording(12345)) { Headless = true };
    }

    private static Game GetNormalGame()
    {
        return new Game(new GameIO(), PlayerProfile.CreateForRecording()) { Headless = true };
    }

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

    private static void Ensure(
        IReadOnlyDictionary<DoorPair, PathRequirements> requirements,
        RoomEntrances start,
        RoomEntrances end,
        PathRequirements expectedRequirements)
    {
        var key = DoorPair.Create(start, end);
        var actual = requirements[key];
        Assert.That(actual, Is.EqualTo(expectedRequirements));
    }

    private GameWorld GetDungeon(int quest, int dungeon)
    {
        var worldId = $"{quest:00}_{dungeon:00}";
        var world = Game.World.GetWorld(GameWorldType.Underworld, worldId);
        return world;
    }

    private GameRoom GetUnderworldRoom(int quest, int dungeon, int x, int y)
    {
        var worldId = $"{quest:00}_{dungeon:00}";
        var roomId = $"Level{worldId}/{x},{y}";

        var world = Game.World.GetWorld(GameWorldType.Underworld, worldId);
        var room = world.GetRoomById(roomId);
        Game.World.LoadRoom(room);

        return room;
    }

    private GameRoom GetOverworldRoom(int x, int y)
    {
        var worldId = "Overworld";
        var roomId = $"{worldId}/{x},{y}";
        var world = Game.World.GetWorld(GameWorldType.Overworld, worldId);
        var room = world.GetRoomById(roomId);
        Game.World.LoadRoom(room);
        return room;
    }

    [Test]
    public void RoomPathTests()
    {
        // Horizontal river room.
        // Left = open, top = bomb, right = door, down = blocked;
        var room = GetUnderworldRoom(0, 9, 5, 6);
        var requirements = RoomRequirements.Get(room).Paths;
        Ensure(requirements, RoomEntrances.Right, RoomEntrances.Top, PathRequirements.Ladder);
        Ensure(requirements, RoomEntrances.Left, RoomEntrances.Top, PathRequirements.Ladder);
        Ensure(requirements, RoomEntrances.Left, RoomEntrances.Right, PathRequirements.None);
    }

    [Test]
    public void BadGuyTests()
    {
        // Digdogger boss room requires recorder.

        var room = GetUnderworldRoom(0, 5, 4, 2);
        var requirements = RoomRequirements.Get(room).Paths;
        Ensure(requirements, RoomEntrances.Right, RoomEntrances.Top, PathRequirements.Recorder);
        Ensure(requirements, RoomEntrances.Bottom, RoomEntrances.Top, PathRequirements.Recorder);
        Ensure(requirements, RoomEntrances.Right, RoomEntrances.Bottom, PathRequirements.None);
    }

    [Test]
    // Has staircase against right wall, can only top the others.
    [TestCase(0, 9, 3, 6, RoomEntrances.Top | RoomEntrances.Left | RoomEntrances.Bottom, TestName = "Stairs Right")]
    // The Princess's room.
    [TestCase(0, 9, 2, 3, RoomEntrances.Bottom, TestName = "Princess")]
    // Old man room in level 1 -- old man blocks passage up.
    [TestCase(0, 1, 1, 4, RoomEntrances.Right | RoomEntrances.Left | RoomEntrances.Bottom | RoomEntrances.Stairs, TestName = "Oldman")]
    public void ValidUnderworldWallsTest(int quest, int level, int x, int y, RoomEntrances expected)
    {
        var room = GetUnderworldRoom(quest, level, x, y);
        var directions = RoomRequirements.Get(room).ConnectableEntrances;
        Assert.That(directions, Is.EqualTo(expected));
    }

    [Test]
    // Has staircase against right wall, can only top the others.
    [TestCase(5, 5, RoomEntrances.Top | RoomEntrances.Left | RoomEntrances.Right | RoomEntrances.Bottom, TestName = "Raft")]
    public void ValidOverworldWallsTest(int x, int y, RoomEntrances expected)
    {
        var room = GetOverworldRoom(x, y);
        var directions = RoomRequirements.Get(room).ConnectableEntrances;
        Assert.That(directions, Is.EqualTo(expected));
    }

    [Test]
    public void RaftTest()
    {
        var room = GetOverworldRoom(5, 5);
        var paths = RoomRequirements.Get(room).Paths.ToList();

        PathRequirements ConsumeRequirements(RoomEntrances a, RoomEntrances b)
        {
            var key = DoorPair.Create(a, b);
            var path = paths.Single(p => p.Key == key);
            paths.RemoveAll(p => p.Key == key);
            return path.Value;
        }

        void AssertPath(RoomEntrances a, RoomEntrances b, PathRequirements expected)
        {
            var actual = ConsumeRequirements(a, b);
            Assert.That(actual, Is.EqualTo(expected));
        }

        // WWWWWWWWWWWWWW
        // WWWWWWWWWRWWWW
        //      WW
        // B    WW     BB
        // B    WW     BB

        AssertPath(RoomEntrances.Left, RoomEntrances.Bottom, PathRequirements.None);
        AssertPath(RoomEntrances.Right, RoomEntrances.Bottom, PathRequirements.None);

        AssertPath(RoomEntrances.Top, RoomEntrances.Left, PathRequirements.Raft);
        AssertPath(RoomEntrances.Top, RoomEntrances.Left, PathRequirements.Raft);
        AssertPath(RoomEntrances.Top, RoomEntrances.Bottom, PathRequirements.Raft);

        Assert.That(paths, Is.Empty);
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
