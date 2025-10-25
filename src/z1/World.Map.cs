using System.Diagnostics.CodeAnalysis;
using z1.Render;

namespace z1;

internal partial class World
{
    internal GameWorld GetWorld(GameWorldType type, string destination) => _worldProvider.GetWorld(type, destination);

    private void LoadOverworld() => LoadWorld(GameWorldType.Overworld, "Overworld");

    private void LoadWorld(GameWorldType type, string destination)
    {
        var world = GetWorld(type, destination);
        LoadWorld(world);
    }

    private void LoadWorld(GameWorld world, EntranceHistoryEntry? entranceEntry = null)
    {
        if (entranceEntry == null)
        {
            LoadRoom(world.EntranceRoom);
            var playerX = world.EntranceRoom.EntryPosition?.X;
            var playerY = world.EntranceRoom.EntryPosition?.Y;
            if (playerX != null && playerY != null)
            {
                Game.Player.X = playerX.Value;
                Game.Player.Y = playerY.Value;
            }
        }
        else
        {
            LoadRoom(entranceEntry.Value.Room);
            var playerX = entranceEntry.Value.FromEntrance.ExitPosition?.X;
            var playerY = entranceEntry.Value.FromEntrance.ExitPosition?.Y;
            if (playerX != null && playerY != null)
            {
                Game.Player.X = playerX.Value;
                Game.Player.Y = playerY.Value;
            }
        }

        if (world.IsOverworld)
        {
            _entranceHistory.Clear();
            Player.FromUnderground = true;
        }

        _tempShutterRoom = null;
        _tempShutterDoorDir = 0;
        _tempShutters = false;
        _darkRoomFadeStep = 0;
        world.ResetLevelKillCounts();
        _roomHistory.Clear();
        WhirlwindTeleporting = 0;
    }

    public readonly record struct EntranceHistoryEntry(GameRoom Room, Entrance FromEntrance);

    public sealed class EntranceHistory
    {
        private readonly World _world;
        private readonly EntranceHistoryEntry _default;
        private readonly Stack<EntranceHistoryEntry> _history = new();

        public EntranceHistory(World world)
        {
            _world = world;
            var overworld = world.GetWorld(GameWorldType.Overworld, "Overworld");
            var pos = overworld.EntranceRoom.EntryPosition;
            _default = new EntranceHistoryEntry(overworld.EntranceRoom, new Entrance
            {
                ExitPosition = pos == null ? new PointXY(120, 141) : new PointXY(pos.X, pos.Y)
            });
        }

        public void Push(GameRoom room, Entrance entrance)
        {
            _history.Push(new EntranceHistoryEntry(room, entrance));
        }

        public EntranceHistoryEntry? GetPreviousEntrance()
        {
            if (_history.Count == 0) return null;
            return _history.Peek();
        }

        public EntranceHistoryEntry GetPreviousEntranceOrDefault()
        {
            if (_history.Count == 0) return _default;
            return _history.Peek();
        }

        public bool TryTakePreviousEntrance([MaybeNullWhen(false)] out EntranceHistoryEntry entry)
        {
            if (_history.TryPop(out entry))
            {
                // CurrentWorldMap = _history.Wh
                return true;
            }

            return false;
        }

        public EntranceHistoryEntry TakePreviousEntranceOrDefault()
        {
            return TryTakePreviousEntrance(out var entry) ? entry : _default;
        }

        public void Clear() => _history.Clear();
    }

    private readonly EntranceHistory _entranceHistory;

    private void LoadMap(GameRoom room)
    {
        CurrentRoom = room;
        CurrentWorld = room.GameWorld;

        room.Reset();
        LoadLayout(room);

        var roomState = GetRoomState(room);

        if (room.HasUnderworldDoors)
        {
            foreach (var direction in TiledRoomProperties.DoorDirectionOrder)
            {
                UpdateDoorTileBehavior(room, direction);
                UpdateDoorTiles(room, direction, roomState);
            }
        }
    }

    private void LoadLayout(GameRoom room)
    {
        foreach (var block in room.InteractableBlockObjects)
        {
            _objects.Add(InteractableBlockActor.Make(this, block));
        }

        foreach (var roomInteraction in room.RoomInteractions)
        {
            _objects.Add(new RoomInteractionActor(this, roomInteraction));
        }
    }

    private void DrawMap(GameRoom room, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = room.Settings.OuterPalette;
        var innerPalette = room.Settings.InnerPalette;

        var firstRow = 0;
        var lastRow = ScreenTileHeight;
        var tileOffsetY = offsetY;

        var firstCol = 0;
        var lastCol = ScreenTileWidth;
        var tileOffsetX = offsetX;

        if (offsetY < 0)
        {
            firstRow = -offsetY / TileHeight;
            tileOffsetY = -(-offsetY % TileHeight);
        }
        else if (offsetY > 0)
        {
            lastRow = ScreenTileHeight - offsetY / TileHeight;
        }
        else if (offsetX < 0)
        {
            firstCol = -offsetX / TileWidth;
            tileOffsetX = -(-offsetX % TileWidth);
        }
        else if (offsetX > 0)
        {
            lastCol = ScreenTileWidth - offsetX / TileWidth;
        }

        var endCol = _startCol + _colCount;
        var endRow = _startRow + _rowCount;

        var y = TileMapBaseY + tileOffsetY;

        for (var ytile = firstRow; ytile < lastRow; ytile++, y += TileHeight)
        {
            if (ytile < _startRow || ytile >= endRow) continue;

            var x = tileOffsetX;
            for (var xtile = firstCol; xtile < lastCol; xtile++, x += TileWidth)
            {
                if (xtile < _startCol || xtile >= endCol) continue;

                var tileRef = DrawHitDetection
                    ? TiledTile.Create((int)room.RoomMap.Behavior(xtile, ytile) + 1)
                    : room.RoomMap.Tile(xtile, ytile);
                var palette = (ytile is < 4 or >= 18 || xtile is < 4 or >= 28) ? outerPalette : innerPalette;
                room.DrawTile(tileRef, x, y, palette);
            }
        }

        Graphics.End();
    }
}