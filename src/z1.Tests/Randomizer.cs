using System;
using System.Collections.Immutable;
using z1.Common;
using z1.GUI;

namespace z1.Tests;
internal class Randomizer
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
}
