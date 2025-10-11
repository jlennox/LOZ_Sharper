using System;
using System.Collections.Immutable;
using z1.Common;
using z1.GUI;
using z1.Randomizer;

namespace z1.Tests;

[TestFixture]
internal class RandomizerTests
{
    private static readonly Lazy<GLWindow> _lazyWindow = new(GetWindow);

    private static GLWindow GetWindow()
    {
        var window = new GLWindow(true);
        window.Game = new Game(new GameIO()) { Headless = true };
        window.Game.Menu.StartWorld(PlayerProfile.CreateForRecording());
        window.Game.Sound.SetMute(true);
        return window;
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (_lazyWindow.IsValueCreated)
        {
            try
            {
                _lazyWindow.Value.Dispose();
            }
            catch { }
        }
    }

    private static void Ensure(IEnumerable<RoomPathRequirement> requirements, Direction start, Direction end, params ItemId[] items)
    {
        var actual = requirements.Where(r => r.StartingDoor == start && r.ExitDoor == end).First();
        var expected = new RoomPathRequirement(start, end, items.ToImmutableArray());
        Assert.That(actual.Requirements.Order().ToArray(), Is.EqualTo(expected.Requirements.Order().ToArray()));
    }

    private static GameWorld GetDungeon(int quest, int dungeon)
    {
        var worldId = $"{quest:00}_{dungeon:00}";
        var window = _lazyWindow.Value;
        var world = window.Game.World.GetWorld(GameWorldType.Underworld, worldId);
        return world;
    }

    private static GameRoom GetUnderworldRoom(int quest, int dungeon, int x, int y)
    {
        var worldId = $"{quest:00}_{dungeon:00}";
        var roomId = $"Level{worldId}/{x},{y}";
        var window = _lazyWindow.Value;

        var world = window.Game.World.GetWorld(GameWorldType.Underworld, worldId);
        var room = world.GetRoomById(roomId);
        window.Game.World.LoadRoom(room);

        return room;
    }

    [Test]
    public void RoomPathTests()
    {
        // Horizontal river room.
        // Left = open, top = bomb, right = door, down = blocked;
        var room = GetUnderworldRoom(0, 9, 5, 6);
        var requirements = room.PathRequirements.Paths;
        Ensure(requirements, Direction.Right, Direction.Up, ItemId.Ladder);
        Ensure(requirements, Direction.Left, Direction.Up, ItemId.Ladder);
        Ensure(requirements, Direction.Left, Direction.Right);
    }

    [Test]
    public void BadGuyTests()
    {
        // Digdogger boss room requires recorder.

        var room = GetUnderworldRoom(0, 5, 4, 2);
        var requirements = room.PathRequirements.Paths;
        Ensure(requirements, Direction.Right, Direction.Up, ItemId.Recorder);
        Ensure(requirements, Direction.Down, Direction.Up, ItemId.Recorder);
        Ensure(requirements, Direction.Right, Direction.Down);
    }

    [Test]
    // Has staircase against right wall, can only top the others.
    [TestCase(0, 9, 3, 6, Direction.Up | Direction.Left | Direction.Down, TestName = "Stairs Right")]
    // The kidnapped's room.
    [TestCase(0, 9, 2, 3, Direction.Down, TestName = "Kidnapped")]
    // Old man room in level 1 -- old man blocks passage up.
    [TestCase(0, 1, 1, 4, Direction.Right | Direction.Left | Direction.Down, TestName = "Oldman")]
    public void ValidWallsTest(int quest, int level, int x, int y, Direction expected)
    {
        var room = GetUnderworldRoom(quest, level, x, y);
        var directions = room.PathRequirements.ConnectableDirections;
        Assert.That(directions, Is.EqualTo(expected));
    }

    [Test]
    // Has staircase against right wall, can only top the others.
    [TestCase(1, 7, 12, 5, RoomRequirementFlags.HasStaircase | RoomRequirementFlags.HasPushBlock)]
    public void CheckFlags(int quest, int level, int x, int y, RoomRequirementFlags expected)
    {
        var room = GetUnderworldRoom(quest, level, x, y);
        var flags = room.PathRequirements.Flags;
        Assert.That(flags, Is.EqualTo(expected));
    }

    [Test]
    public void ShapeTest()
    {
        var dungeon = GetDungeon(0, 1);
        var state = new RandomizerState(0, new());
        Randomizer.Randomizer.CreateDungeonShape(dungeon, state);
    }

    [Test]
    [TestCase(0, 1, 1, 1)]
    [TestCase(0, 2, 0, 1)]
    [TestCase(0, 3, 1, 0)]
    [TestCase(0, 8, 2, 0)]
    public void DungeonStatsTest(int quest, int level, int staircaseItemCount, int floorItemCount)
    {
        var dungeon = GetDungeon(quest, level);
        var actual = DungeonStats.Get(dungeon);
        Assert.That(actual.StaircaseItemCount, Is.EqualTo(staircaseItemCount));
        Assert.That(actual.FloorItemCount, Is.EqualTo(floorItemCount));
    }

    [Test]
    public void Create()
    {
        var window = _lazyWindow.Value;
        var game = window.Game;
        var state = new RandomizerState(0, new());
        Randomizer.Randomizer.Randomize(game.World.CurrentWorld, state);
    }
}
