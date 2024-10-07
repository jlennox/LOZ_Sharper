using System.Diagnostics.CodeAnalysis;
using z1.Actors;
using z1.IO;
using z1.Render;

namespace z1;

internal partial class World
{
    private void LoadLevel(int level)
    {
        var levelDirName = level == 0
            ? "Overworld.world"
            : $"Level{Profile.Quest:D2}_{level:D2}.world"; // JOE: TODO: Make this logic generic.

        CurrentWorld = new GameWorld(Game, new Asset($"Maps/{levelDirName}").ReadJson<TiledWorld>(), $"Maps/{levelDirName}", Profile.Quest);
        // _directory = new Asset($"levelDir_{Profile.Quest}_{level}.json").ReadJson<LevelDirectory>();
        // _infoBlock = ListResource<LevelInfoBlock>.LoadSingle(_directory.LevelInfoBlock);

        _tempShutterRoom = null;
        _tempShutterDoorDir = 0;
        _tempShutters = false;
        _darkRoomFadeStep = 0;
        CurrentWorld.ResetLevelKillCounts();
        _roomHistory.Clear();
        WhirlwindTeleporting = 0;

        if (level == 0)
        {
            LoadOverworldContext();
            CurrentRoom = CurrentWorld.EntryRoom;
        }
        else
        {
            LoadUnderworldContext();

            // foreach (var tileMap in _tileMaps)
            // {
            //     for (var x = 0; x < TileMap.Size; x++)
            //     {
            //         tileMap.Tile(x) = (byte)BlockObjType.TileWallEdge;
            //     }
            // }
        }

        // _roomAttrs = ListResource<RoomAttrs>.LoadList(new Asset(_directory.RoomAttrs), Rooms).ToArray();
        // _sparseRoomAttrs = TableResource<byte>.Load(new Asset(_directory.Extra1));

        var facing = Game.Player?.Facing ?? Direction.Up;

        Game.Player = new Player(Game, facing);

        // Replace room attributes, if in second quest.

        // JOE: TODO: MAP REWRITE if (level == 0 && Profile.Quest == 1)
        // JOE: TODO: MAP REWRITE {
        // JOE: TODO: MAP REWRITE     var pReplacement = _sparseRoomAttrs.GetItems<byte>(Sparse.RoomReplacement);
        // JOE: TODO: MAP REWRITE     int replacementCount = pReplacement[0];
        // JOE: TODO: MAP REWRITE     var sparseAttr = MemoryMarshal.Cast<byte, SparseRoomAttr>(pReplacement[2..]); // JOE: Go until replacementCount * sizeof(SparseRoomAttr) ??
        // JOE: TODO: MAP REWRITE
        // JOE: TODO: MAP REWRITE     for (var i = 0; i < replacementCount; i++)
        // JOE: TODO: MAP REWRITE     {
        // JOE: TODO: MAP REWRITE         int roomId = sparseAttr[i].roomId;
        // JOE: TODO: MAP REWRITE         _roomAttrs[roomId] = sparseAttr[i].attrs;
        // JOE: TODO: MAP REWRITE     }
        // JOE: TODO: MAP REWRITE }
    }

    public readonly record struct RoomHistoryEntry(GameRoom Room, GameWorld World, Entrance FromEntrance);
    private readonly Stack<RoomHistoryEntry> _previousRooms = new();

    public RoomHistoryEntry? GetPreviousEntrance()
    {
        if (_previousRooms.Count == 0) return null;
        return _previousRooms.Peek();
    }

    public bool TryTakePreviousEntrance([MaybeNullWhen(false)] out RoomHistoryEntry entry)
    {
        return _previousRooms.TryPop(out entry);
    }

    private void LoadMap(GameRoom room, Entrance? fromEntrance = null)
    {
        if (fromEntrance != null) _previousRooms.Push(new RoomHistoryEntry(CurrentRoom, CurrentWorld, fromEntrance));

        CurrentRoom = room;
        CurrentWorld = room.World;

        room.Reset();
        LoadLayout(room);

        if (room.HasDungeonDoors)
        {
            foreach (var direction in TiledObjectProperties.DoorDirectionOrder)
            {
                UpdateDoorTileBehavior(room, direction);
            }
        }
    }

    private void LoadLayout(GameRoom room)
    {
        foreach (var actionObject in room.InteractiveGameObjects)
        {
            _objects.Add(new InteractiveGameObjectActor(Game, actionObject));
            // JOE: TODO: OBJECT REWRITE var actionFunc = GetTileActionFunction(actionObject.Action);
            // JOE: TODO: OBJECT REWRITE actionObject.GetScreenTileCoordinates(out var tileX, out var tileY);
            // JOE: TODO: OBJECT REWRITE actionObject.GetTileSize(out var tileWidth, out var tileHeight);
            // JOE: TODO: OBJECT REWRITE for (var y = tileY; y < tileY + tileHeight; y += 2)
            // JOE: TODO: OBJECT REWRITE {
            // JOE: TODO: OBJECT REWRITE     for (var x = tileX; x < tileX + tileWidth; x += 2)
            // JOE: TODO: OBJECT REWRITE     {
            // JOE: TODO: OBJECT REWRITE         actionFunc(tileY, tileX, TileInteraction.Load);
            // JOE: TODO: OBJECT REWRITE     }
            // JOE: TODO: OBJECT REWRITE }
        }

        PatchTileBehaviors();
    }

    private void DrawMap(GameRoom room, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = room.RoomInformation.OuterPalette;
        var innerPalette = room.RoomInformation.InnerPalette;

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

        if (room.HasDungeonDoors)
        {
            Graphics.DrawImage(
                _wallsBmp,
                0, 0,
                TileMapWidth, TileMapHeight,
                offsetX, TileMapBaseY + offsetY,
                outerPalette, 0);
        }

        for (var ytile = firstRow; ytile < lastRow; ytile++, y += TileHeight)
        {
            if (ytile < _startRow || ytile >= endRow) continue;

            var x = tileOffsetX;
            for (var xtile = firstCol; xtile < lastCol; xtile++, x += TileWidth)
            {
                if (xtile < _startCol || xtile >= endCol) continue;

                var tileRef = room.RoomMap.Tile(xtile, ytile);
                var palette = (ytile is < 4 or >= 18 || xtile is < 4 or >= 28) ? outerPalette : innerPalette;
                room.DrawTile(tileRef, x, y, palette);
            }
        }

        if (room.HasDungeonDoors)
        {
            DrawDoors(room, false, offsetX, offsetY);
        }

        Graphics.End();
    }
}