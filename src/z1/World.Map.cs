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

        var facing = Game.Link?.Facing ?? Direction.Up;

        Game.Link = new Link(Game, facing);

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

    private void LoadMap(GameRoom room)
    {
        // TileScheme tileScheme;
        // var uniqueRoomId = _roomAttrs[roomId].GetUniqueRoomId();
        //
        // if (IsOverworld())
        // {
        //     tileScheme = TileScheme.Overworld;
        // }
        // else if (uniqueRoomId >= 0x3E)
        // {
        //     tileScheme = TileScheme.UnderworldCellar;
        //     uniqueRoomId -= 0x3E;
        // }
        // else
        // {
        //     tileScheme = TileScheme.UnderworldMain;
        // }

        LoadLayout(room);

        if (room.HasDungeonDoors)
        {
            foreach (var direction in TiledObjectProperties.DoorDirectionOrder)
            {
                UpdateDoorTileBehavior(room, direction);
            }
        }
    }

    private void LoadOWMapSquare(ref RoomTileMap map, int row, int col, int mobIndex)
    {
        // Square table:
        // - Is > $10, then refers to upper left, bottom left, upper right, bottom right.
        // - Is <= $10, then refers to secondary table, which are the reused squares.

        // Secondary square table:
        // - Each entry is 4 bytes long. 16 entries in total, indexed by values in the primary square table that are less than 16.
        // - Bytes specify tile numbers (in pattern table 1)

        // var primary = _squareTable[mobIndex];

        // if (primary == 0xFF)
        // {
        //     var index = mobIndex * 4;
        //     var secondaries = _squareTableSecondary;
        //     map[row, col] = secondaries[index + 0];
        //     map[row, col + 1] = secondaries[index + 2];
        //     map[row + 1, col] = secondaries[index + 1];
        //     map[row + 1, col + 1] = secondaries[index + 3];
        // }
        // else
        // {
        //     map[row, col] = primary;
        //     map[row, col + 1] = (byte)(primary + 2);
        //     map[row + 1, col] = (byte)(primary + 1);
        //     map[row + 1, col + 1] = (byte)(primary + 3);
        // }
    }

    private void LoadUWMapSquare(ref RoomTileMap map, int row, int col, int mobIndex)
    {
        // var primary = _squareTable[mobIndex];
        //
        // if (primary is < 0x70 or > 0xF2)
        // {
        //     map[row, col] = primary;
        //     map[row, col + 1] = primary;
        //     map[row + 1, col] = primary;
        //     map[row + 1, col + 1] = primary;
        // }
        // else
        // {
        //     map[row, col] = primary;
        //     map[row, col + 1] = (byte)(primary + 2);
        //     map[row + 1, col] = (byte)(primary + 1);
        //     map[row + 1, col + 1] = (byte)(primary + 3);
        // }
    }

    private readonly record struct ColumnRow(byte Desc, bool IsOverworld)
    {
        // The contents of columnTable for the overworld:
        //  7 6 5 4 3 2 1 0
        //  | | |---------| Square number
        //  | |             Duplicated, takes up two rows.
        //  |               Individual column is starting, first column.
        private const byte OverworldSquareMask = 0x3F;
        private const byte OverworldDuplicatedMask = 0x40;
        private const byte ColumnStartMask = 0x80;

        public static bool IsColumnStart(byte desc) => (desc & ColumnStartMask) != 0;

        private readonly int _squareMask = IsOverworld ? OverworldSquareMask : 0x07;

        public byte SquareNumber => (byte)(Desc & _squareMask);

        public int RepeatCount
        {
            get
            {
                if (IsOverworld)
                {
                    return (Desc & OverworldDuplicatedMask) != 0 ? 1 : 0;
                }

                return (Desc >> 4) & 0x7;
            }
        }
    }

    private void LoadLayout(GameRoom room)
    {
        foreach (var actionObject in room.ActionMapObjects)
        {
            var actionFunc = GetTileActionFunction(actionObject.Action);
            actionObject.GetScreenTileCoordinates(out var tileX, out var tileY);
            actionObject.GetTileSize(out var tileWidth, out var tileHeight);
            for (var y = tileY; y < tileY + tileHeight; y += 2)
            {
                for (var x = tileX; x < tileX + tileWidth; x += 2)
                {
                    actionFunc(tileY, tileX, TileInteraction.Load);
                }
            }
        }

        PatchTileBehaviors();
    }

    private void DrawMap(GameRoom room, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = room.OuterPalette;
        var innerPalette = room.InnerPalette;

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