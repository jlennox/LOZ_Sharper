using System.Runtime.InteropServices;
using z1.Actors;
using z1.Common.IO;
using z1.IO;
using z1.Render;

namespace z1;

internal partial class World
{
    private RoomAttrs[] _roomAttrs = new RoomAttrs[Rooms];

    private void LoadLevel(int level)
    {
        var levelDirName = $"levelDir_{Profile.Quest}_{level}.json";

        _directory = new Asset(levelDirName).ReadJson<LevelDirectory>();
        _infoBlock = ListResource<LevelInfoBlock>.LoadSingle(_directory.LevelInfoBlock);

        _tempShutterRoomId = 0;
        _tempShutterDoorDir = 0;
        _tempShutters = false;
        _darkRoomFadeStep = 0;
        Array.Clear(_levelKillCounts);
        _roomHistory.Clear();
        WhirlwindTeleporting = 0;

        if (level == 0)
        {
            LoadOverworldContext();
            _currentRoomMap = RoomMap.Overworld;
        }
        else
        {
            LoadUnderworldContext();
            _currentRoomMap = level < 7 ? RoomMap.UnderworldA : RoomMap.UnderworldB;

            // foreach (var tileMap in _tileMaps)
            // {
            //     for (var x = 0; x < TileMap.Size; x++)
            //     {
            //         tileMap.Tile(x) = (byte)BlockObjType.TileWallEdge;
            //     }
            // }
        }

        _roomAttrs = ListResource<RoomAttrs>.LoadList(new Asset(_directory.RoomAttrs), Rooms).ToArray();
        _sparseRoomAttrs = TableResource<byte>.Load(new Asset(_directory.Extra1));

        var facing = Game.Link?.Facing ?? Direction.Up;

        Game.Link = new Link(Game, facing);

        // Replace room attributes, if in second quest.

        if (level == 0 && Profile.Quest == 1)
        {
            var pReplacement = _sparseRoomAttrs.GetItems<byte>(Sparse.RoomReplacement);
            int replacementCount = pReplacement[0];
            var sparseAttr = MemoryMarshal.Cast<byte, SparseRoomAttr>(pReplacement[2..]); // JOE: Go until replacementCount * sizeof(SparseRoomAttr) ??

            for (var i = 0; i < replacementCount; i++)
            {
                int roomId = sparseAttr[i].roomId;
                _roomAttrs[roomId] = sparseAttr[i].attrs;
            }
        }
    }

    private void LoadMap(int roomId)
    {
        TileScheme tileScheme;
        var uniqueRoomId = _roomAttrs[roomId].GetUniqueRoomId();

        if (IsOverworld())
        {
            tileScheme = TileScheme.Overworld;
        }
        else if (uniqueRoomId >= 0x3E)
        {
            tileScheme = TileScheme.UnderworldCellar;
            uniqueRoomId -= 0x3E;
        }
        else
        {
            tileScheme = TileScheme.UnderworldMain;
        }

        LoadLayout(roomId);

        if (tileScheme == TileScheme.UnderworldMain)
        {
            for (var i = 0; i < Doors; i++)
            {
                UpdateDoorTileBehavior(roomId, i);
            }
        }
    }

    private void LoadOWMapSquare(ref GameScreenMap map, int row, int col, int mobIndex)
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

    private void LoadUWMapSquare(ref GameScreenMap map, int row, int col, int mobIndex)
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

    private void LoadLayout(int roomId)
    {
        // Needs to be roomId! Move over to roomId object.
        var screen = _currentMap.Screens[roomId];

        foreach (var actionObject in screen.ActionMapObjects)
        {
            var actionFunc = GetTileActionFunction(actionObject.Action);
            actionObject.GetScreenTileCoordinates(_currentMap, out var tileX, out var tileY);
            actionObject.GetTileSize(_currentMap, out var tileWidth, out var tileHeight);
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

    private void LoadLayoutOld(int uniqueRoomId, int tileMapIndex, TileScheme tileScheme)
    {
        var logfn = _traceLog.CreateFunctionLog();
        logfn.Write($"({uniqueRoomId}, {tileMapIndex}, {tileScheme})");

        var maxColumnStartOffset = (_colCount / 2 - 1) * _rowCount / 2;

        // var map = _tileMaps[tileMapIndex];
        var rowEnd = _startRow + _rowCount;

        var owLayoutFormat = tileScheme is TileScheme.Overworld or TileScheme.UnderworldCellar;

        _loadMapObjectFunc = owLayoutFormat switch {
            true => LoadOWMapSquare,
            _ => LoadUWMapSquare
        };

        var owRoomAttrs = CurrentOWRoomAttrs;
        var roomAttrs = CurrentOWRoomAttrs.Attrs;
        logfn.Write($"owRoomAttrs:{roomAttrs.A:X2},{roomAttrs.D:X2},{roomAttrs.C:X2},{roomAttrs.D:X2}");

        static int GetColumnStart(ReadOnlySpan<byte> columnTable, int columnIndex, int maxColumnStartOffset)
        {
            var currentColumnIndex = 0;
            var columnStart = 0;

            for (; columnStart <= maxColumnStartOffset; columnStart++)
            {
                var cell = columnTable[columnStart];
                if (ColumnRow.IsColumnStart(cell))
                {
                    if (currentColumnIndex == columnIndex) return columnStart;
                    currentColumnIndex++;
                }
            }

            throw new Exception();
        }

        // var columns = _roomCols[uniqueRoomId];

        for (var columnDescIndex = 0; columnDescIndex < _colCount / 2; columnDescIndex++)
        {
            // var columnDesc = columns.ColumnDesc[columnDescIndex];

            var tableIndex =  0; //byte)((columnDesc & 0xF0) >> 4);
            var columnIndex = 0; //(byte)(columnDesc & 0x0F);

            ReadOnlySpan<byte> columnTable = []; // _colTables.GetItem(tableIndex);
            var columnStart = GetColumnStart(columnTable, columnIndex, maxColumnStartOffset);
            var column = _startCol + columnDescIndex * 2;

            for (var rowIndex = _startRow; rowIndex < rowEnd; columnStart++)
            {
                var columnRow = new ColumnRow(columnTable[columnStart], owLayoutFormat);
                var squareNumber = columnRow.SquareNumber;

                // _loadMapObjectFunc(ref map, rowIndex, column, squareNumber);

                var attr = (byte)0; // _tileAttrs[squareNumber];
                var action = owRoomAttrs.IsInQuest(Profile.Quest) ? TileAttr.GetAction(attr) : TileAction.None;
                TileActionDel? actionFunc = null;

                if (action != TileAction.None)
                {
                    logfn.Write(
                        $"tileRef:{squareNumber}, attr:{attr:X2}, action:{action}, pos:{rowIndex:X2},{column:X2}");
                    actionFunc = GetTileActionFunction(action);
                    actionFunc(rowIndex, column, TileInteraction.Load);
                }

                rowIndex += 2;

                var repeatCount = columnRow.RepeatCount;
                for (var m = 0; m < repeatCount && rowIndex < rowEnd; m++)
                {
                    // _loadMapObjectFunc(ref map, rowIndex, column, squareNumber);
                    actionFunc?.Invoke(rowIndex, column, TileInteraction.Load);
                    rowIndex += 2;
                }
            }
        }

        if (IsUWMain(CurRoomId))
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            if (uwRoomAttrs.HasBlock())
            {
                for (var c = _startCol; c < _startCol + _colCount; c += 2)
                {
                    // var tileRef = _tileMaps[_curTileMapIndex].Tile(UWBlockRow, c);
                    // if (tileRef == (byte)BlockObjType.TileBlock)
                    // {
                    //     ActionFuncs[(int)TileAction.Block](UWBlockRow, c, TileInteraction.Load);
                    //     break;
                    // }
                }
            }
        }

        for (var i = 0; i < ScreenTileHeight * ScreenTileWidth; i++)
        {
            //  var t = map.Tile(i);
            //  map.Behaviors(i) = _tileBehaviors[t];
        }

        PatchTileBehaviors();
    }

    // JOE: TODO: Don't keep doing the array lookup.
    public GameScreen CurrentScreen => _currentMap.Screens[CurRoomId];

    private void DrawMap(int roomId, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var screen = _currentMap.Screens[roomId];
        var outerPalette = screen.OuterPalette;
        var innerPalette = screen.InnerPalette;

        if (IsUWCellar(roomId) || IsPlayingCave())
        {
            outerPalette = (Palette)3;
            innerPalette = (Palette)2;
        }

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

        if (IsUWMain(roomId))
        {
            Graphics.DrawImage(
                _wallsBmp,
                0, 0,
                TileMapWidth, TileMapHeight,
                offsetX, TileMapBaseY + offsetY,
                outerPalette, 0);
        }

        var backgroundSheet = IsOverworld() ? TileSheet.BackgroundOverworld : TileSheet.BackgroundUnderworld;

        for (var ytile = firstRow; ytile < lastRow; ytile++, y += TileHeight)
        {
            if (ytile < _startRow || ytile >= endRow) continue;

            var x = tileOffsetX;
            for (var xtile = firstCol; xtile < lastCol; xtile++, x += TileWidth)
            {
                if (xtile < _startCol || xtile >= endCol) continue;

                var tileRef = screen.Map.Tile(xtile, ytile);
                var palette = (ytile is < 4 or >= 18 || xtile is < 4 or >= 28) ? outerPalette : innerPalette;
                _currentMap.DrawTile(tileRef, x, y, palette);
            }
        }

        if (IsUWMain(roomId))
        {
            DrawDoors(roomId, false, offsetX, offsetY);
        }

        Graphics.End();
    }
}