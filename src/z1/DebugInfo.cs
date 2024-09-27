using z1.Render;
using z1.UI;

namespace z1;

internal sealed class DebugInfo
{
    private readonly Game _game;
    private readonly DebugInfoConfiguration _configuration;
    private readonly List<string> _sb = new();

    public DebugInfo(Game game, DebugInfoConfiguration configuration)
    {
        _game = game;
        _configuration = configuration;
    }

    public void Draw()
    {
        if (!_configuration.Enabled) return;

        _sb.Clear();

        if (_configuration.RoomId)
        {
            var room = _game.World.CurrentRoom;
            // _sb.Add($"r:{room.X},{room.Y} ({_game.World.CurRoomId})");
        }

        if (_configuration.ActiveShots) _sb.Add($"shots:{_game.World.ActiveShots}");

        var y = 2;
        const int x = 80;
        foreach (var line in _sb)
        {
            GlobalFunctions.DrawString(line, x, y - 1, Palette.Red, DrawingFlags.None);
            GlobalFunctions.DrawString(line, x + 1, y, 0, DrawingFlags.None);
            y += 8;
        }
    }
}