﻿using System.Drawing;
using System.Runtime.InteropServices;
#pragma warning disable CA1416

namespace ExtractLoz;

using static World;

internal sealed class World
{
    public const int MobColumns = 16;
    public const int ScreenRows = 22;
    public const int ScreenColumns = 32;
    public const int MobTileWidth = 16;
    public const int MobTileHeight = 16;
    public const int TileWidth = 8;
    public const int TileHeight = 8;
    public const int TileMapWidth = ScreenColumns * TileWidth;
    public const int Rooms = 128;
    public const int UniqueRooms = 124;
    public const int TileMapHeight = ScreenRows * TileHeight;
    public const int TileMapBaseY = 64;

    public const int WorldWidth = 16;
    public const int WorldHeight = 8;

    public const int OWMarginRight = 0xE0;
    public const int OWMarginLeft = 0x10;
    public const int OWMarginTop = 0x4D;
    public const int OWMarginBottom = 0xCD;

    public const int UWMarginRight = 0xD0;
    public const int UWMarginLeft = 0x20;
    public const int UWMarginTop = 0x5D;
    public const int UWMarginBottom = 0xBD;
    public const int TileTypes = 256;
}

internal readonly record struct ColumnRow(byte Desc, bool IsOverworld)
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

internal readonly struct TableResource<T>
    where T : struct
{
    public readonly int Length;
    public readonly short[] Offsets;
    public readonly byte[] Heap;

    public TableResource(int length, short[] offsets, byte[] heap)
    {
        Length = length;
        Offsets = offsets;
        Heap = heap;
    }

    public static TableResource<T> Load(ReadOnlySpan<byte> bytes)
    {
        var length = BitConverter.ToInt16(bytes);
        bytes = bytes[sizeof(short)..];

        var offsetLength = length * sizeof(short);
        var offsets = MemoryMarshal.Cast<byte, short>(bytes[..offsetLength]);

        var heap = bytes[offsetLength..];
        return new TableResource<T>(length, offsets.ToArray(), heap.ToArray());
    }


    public TAs GetItem<TAs>(Extra extra) where TAs : struct => GetItem<TAs>((int)extra);
    public ReadOnlySpan<TAs> GetItems<TAs>(Extra extra) where TAs : struct => GetItems<TAs>((int)extra);
    public TAs GetItem<TAs>(Sparse extra) where TAs : struct => GetItem<TAs>((int)extra);
    public ReadOnlySpan<TAs> GetItems<TAs>(Sparse extra) where TAs : struct => GetItems<TAs>((int)extra);

    public ReadOnlySpan<TAs> GetItems<TAs>(int index) where TAs : struct => MemoryMarshal.Cast<T, TAs>(GetItem(index));
    public TAs GetItem<TAs>(int index) where TAs : struct => GetItems<TAs>(index)[0];

    public ReadOnlySpan<T> GetItem(int index)
    {
        if (index >= Length) return null;
        if (Heap == null || index >= Offsets.Length) throw new Exception();

        var offset = Offsets[index];
        if (Heap.Length <= offset) throw new Exception();

        return MemoryMarshal.Cast<byte, T>(Heap.AsSpan()[offset..]);
    }

    public TSparse? FindSparseAttr<TSparse>(Sparse attrId, int elemId)
        where TSparse : struct
    {
        if (Heap == null) return null;
        var valueArray = GetItems<byte>(attrId);

        var count = valueArray[0];
        var elemSize = valueArray[1];
        var elem = valueArray[2..];

        for (var i = 0; i < count; i++, elem = elem[elemSize..])
        {
            if (elem[0] == elemId)
            {
                return MemoryMarshal.Cast<byte, TSparse>(elem)[0];
            }
        }

        return null;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparsePos
{
    public byte roomId;
    public byte pos;

    public Point GetRoomCoord()
    {
        var row = pos & 0x0F;
        var col = (pos & 0xF0) >> 4;
        row -= 4;
        return new Point(col, row);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparsePos2
{
    public byte roomId;
    public byte x;
    public byte y;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparseRoomItem
{
    public readonly byte roomId;
    public readonly byte x;
    public readonly byte y;
    public readonly byte itemId;

    public readonly ItemId AsItemId => (ItemId)itemId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct SparseMaze
{
    public readonly byte roomId;
    public readonly byte exitDir;
    public fixed byte path[4];

    public readonly Direction ExitDirection => (Direction)exitDir;
    public readonly ReadOnlySpan<Direction> Paths => new[] { (Direction)path[0], (Direction)path[1], (Direction)path[2], (Direction)path[3], };
}

[Flags]
internal enum Direction
{
    None = 0,
    Right = 1,
    Left = 2,
    Down = 4,
    Up = 8,
    DirectionMask = 0x0F,
    ShoveMask = 0x80, // JOE: TODO: Not sure what this is.
    FullMask = 0xFF,
    VerticalMask = Down | Up,
    HorizontalMask = Left | Right,
    OppositeVerticals = VerticalMask,
    OppositeHorizontals = HorizontalMask,
}


internal enum Extra
{
    PondColors,
    SpawnSpots,
    ObjAttrs,
    CavePalettes,
    Caves,
    LevelPersonStringIds,
    HitPoints,
    PlayerDamage,
}

internal enum Sparse
{
    ArmosStairs,
    ArmosItem,
    Dock,
    Item,
    Shortcut,
    Maze,
    SecretScroll,
    Ladder,
    Recorder,
    Fairy,
    RoomReplacement,
}

internal sealed class TileMap
{
    public const int Size = World.ScreenRows * World.ScreenColumns;

    public byte this[int row, int col]
    {
        get => _tileRefs[row * World.ScreenColumns + col];
        set => _tileRefs[row * World.ScreenColumns + col] = (byte)value;
    }

    private readonly byte[] _tileRefs = new byte[Size];
    private readonly byte[] _tileBehaviors = new byte[Size];

    public ref byte Refs(int index) => ref _tileRefs[index];
    public ref byte Refs(int row, int col) => ref _tileRefs[row * World.ScreenColumns + col];
    public ref byte Behaviors(int row, int col) => ref _tileBehaviors[row * World.ScreenColumns + col];
    public ref byte Behaviors(int index) => ref _tileBehaviors[index];
    // public TileBehavior AsBehaviors(int row, int col)
    // {
    //     row = Math.Max(0, Math.Min(row, World.ScreenRows - 1));
    //     col = Math.Max(0, Math.Min(col, World.ScreenColumns - 1));
    //
    //     return (TileBehavior)_tileBehaviors[row * World.ScreenColumns + col];
    // }

}

internal sealed class MapExtractor
{
    private delegate void LoadMobDelegate(ref TileMap map, int row, int col, int squareIndex);

    public record ActionableTiles(
        int X, int Y, int Height, int Width, int QuestId, TileAction Action,
        TiledProperty[]? Properties = null, bool Visible = true, string Name = null)
    {
        public int Right => X + Width * 2;

        public bool CanRepeat(ActionableTiles other)
        {
            if (Action != other.Action) return false;
            var lengthA = Properties?.Length ?? 0;
            var lengthB = other.Properties?.Length ?? 0;
            if (lengthA != lengthB) return false;
            if (lengthA == 0 && lengthB == 0) return true;
            var sortedA = Properties!.OrderBy(p => p.Name).ToArray();
            var sortedB = other.Properties!.OrderBy(p => p.Name).ToArray();
            for (var i = 0; i < sortedA.Length; i++)
            {
                var a = sortedA[i];
                var b = sortedB[i];
                if (a.Name != b.Name || a.Type != b.Type || a.Value != b.Value) return false;
            }
            return true;
        }

        public int HashProperties()
        {
            if (Properties == null) return 0;
            var hasher = new HashCode();
            foreach (var prop in Properties.OrderBy(t => t.Name))
            {
                hasher.Add(prop.Name);
                hasher.Add(prop.Type);
                hasher.Add(prop.Value);
            }
            return hasher.ToHashCode();
        }

        public ActionableTiles ExpandHeight() => this with { Height = Height + 1 };
        public ActionableTiles ExpandWidth() => this with { Width = Width + 1 };
    }

    private readonly TableResource<byte> _sparseTable;
    private readonly RoomAttr[] _roomAttributes;
    private readonly LevelInfoBlock _infoBlock;
    private readonly byte[] _owTable;
    private readonly byte[] _owTable2;
    private readonly byte[][] _roomCols;
    private readonly byte[] _tileAttrs;

    private int _rowCount;
    private int _colCount;
    private int _startRow;
    private int _startCol;
    private int _tileTypeCount;
    private int _marginRight;
    private int _marginLeft;
    private int _marginBottom;
    private int _marginTop;

    public MapExtractor(
        TableResource<byte> sparseTable, RoomAttr[] roomAttributes, LevelInfoBlock infoBlock,
        byte[] owTable, byte[] owTable2,
        byte[][] roomCols, byte[] tileAttributes)
    {
        _sparseTable = sparseTable;
        _roomAttributes = roomAttributes;
        _infoBlock = infoBlock;
        _owTable = owTable;
        _owTable2 = owTable2;
        _roomCols = roomCols;
        _tileAttrs = tileAttributes;
    }

    public unsafe TileMap LoadLayout(int roomId, bool isOverworld, TableResource<byte> colTables, out ActionableTiles[] actions)
    {
        if (isOverworld)
        {
            LoadOpenRoomContext();
        }
        else
        {
            LoadClosedRoomContext();
        }

        var tileactions = new List<ActionableTiles>();

        var roomAttrs = _roomAttributes[roomId];
        var uniqueRoomId = roomAttrs.GetUniqueRoomId();

        var maxColumnStartOffset = (_colCount / 2 - 1) * _rowCount / 2;

        var map = new TileMap();
        var rowEnd = _startRow + _rowCount;

        // if (_roomCols.Length != UniqueRooms) throw new Exception($"Expected {UniqueRooms}, but got {_roomCols.Length}");

        LoadMobDelegate loadMobFunc = isOverworld switch
        {
            true => LoadOWMapSquare,
            _ => LoadUWMapSquare
        };

        var owRoomAttrs = new OWRoomAttr(roomAttrs);

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

        var columns = _roomCols[uniqueRoomId];
        if (columns.Length != MobColumns) throw new Exception();

        bool FindSparseFlag(Sparse attrId) => _sparseTable.FindSparseAttr<SparsePos>(attrId, roomId).HasValue;
        SparsePos? FindSparsePos(Sparse attrId) => _sparseTable.FindSparseAttr<SparsePos>(attrId, roomId);
        SparsePos2? FindSparsePos2(Sparse attrId) => _sparseTable.FindSparseAttr<SparsePos2>(attrId, roomId);
        SparseRoomItem? FindSparseItem(Sparse attrId) => _sparseTable.FindSparseAttr<SparseRoomItem>(attrId, roomId);

        var armosStairs = FindSparsePos2(Sparse.ArmosStairs);
        var armosItem = FindSparseItem(Sparse.ArmosItem);
        var hasDock = FindSparseFlag(Sparse.Dock);
        var roomItem = FindSparseItem(Sparse.Item);
        var hasShortcutStairs = FindSparseFlag(Sparse.Shortcut);
        // var maze = _sparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, roomId);
        // SecretScroll,
        var doesRoomSupportLadder = FindSparseFlag(Sparse.Ladder);
        var recorderPosition = FindSparsePos(Sparse.Recorder);
        // Fairy,
        // If second quest, do this: _roomAttrs[roomId] = sparseAttr[i].attrs;
        var roomReplacements = _sparseTable.GetItems<byte>(Sparse.RoomReplacement);

        var exitPos = owRoomAttrs.GetExitPosition();
        var col = exitPos & 0x0F;
        var row = (exitPos >> 4) + 4;

        var caveId = owRoomAttrs.GetCaveId();
        var questId = owRoomAttrs.QuestNumber();
        var caveName = (int)caveId <= 9 ? $"Level_{caveId}" : $"Cave_{caveId - 0x0F}";
        var caveProps = new[] {
            new TiledProperty(TiledObjectProperties.Enters, caveName),
            new TiledProperty(TiledObjectProperties.ExitPosition, $"{col},{row}"),
        };

        if (recorderPosition != null)
        {
            var rec = recorderPosition.Value.GetRoomCoord();
            tileactions.Add(new ActionableTiles(rec.X * 2, rec.Y * 2, 1, 1, questId, TileAction.Recorder, caveProps, false));
        }

        var shortcutStairsName = $"shortcut_stairs-{roomId}";

        if (hasShortcutStairs)
        {
            var stairsIndex = owRoomAttrs.GetShortcutStairsIndex();
            var stairsPos = _infoBlock.ShortcutPosition[stairsIndex];
            GetRoomCoord(stairsPos, out var stairsRow, out var stairsCol);
            tileactions.Add(new ActionableTiles(stairsCol * 2, stairsRow * 2, 1, 1, questId, TileAction.Stairs, caveProps, false, shortcutStairsName));
        }

        TiledProperty[]? GetCaveId(TileAction action, int x, int y)
        {
            if (action == TileAction.Armos)
            {
                if (armosStairs.HasValue && armosStairs.Value.x / 16 == x && armosStairs.Value.y / 16 == y)
                {
                    return caveProps;
                }
            }

            if (action is TileAction.Bomb or TileAction.Burn
                or TileAction.Headstone or TileAction.Cave
                or TileAction.Stairs)
            {
                return caveProps;
            }

            return null;
        }

        for (var columnDescIndex = 0; columnDescIndex < _colCount / 2; columnDescIndex++)
        {
            var columnDesc = columns[columnDescIndex];

            var tableIndex = (byte)((columnDesc & 0xF0) >> 4);
            var columnIndex = (byte)(columnDesc & 0x0F);

            var columnTable = colTables.GetItem(tableIndex);
            var columnStart = GetColumnStart(columnTable, columnIndex, maxColumnStartOffset);
            var column = _startCol + columnDescIndex * 2;

            for (var rowIndex = _startRow; rowIndex < rowEnd; columnStart++)
            {
                var columnRow = new ColumnRow(columnTable[columnStart], isOverworld);
                var tileRef = columnRow.SquareNumber;

                loadMobFunc(ref map, rowIndex, column, tileRef);

                rowIndex += 2;

                var repeatCount = columnRow.RepeatCount;
                for (var m = 0; m < repeatCount && rowIndex < rowEnd; m++)
                {
                    loadMobFunc(ref map, rowIndex, column, tileRef);
                    rowIndex += 2;
                }
            }

            ActionableTiles? lastAction = null;

            // This gives us vertical strips for repeated actions.
            columnStart = GetColumnStart(columnTable, columnIndex, maxColumnStartOffset);
            for (var rowIndex = _startRow; rowIndex < rowEnd; columnStart++)
            {
                var columnRow = new ColumnRow(columnTable[columnStart], isOverworld);
                var tileRef = columnRow.SquareNumber; var attr = _tileAttrs[tileRef];
                var action = TileAttr.GetAction(attr);

                if (lastAction != null && lastAction.Action != action)
                {
                    if (lastAction.Action != TileAction.None)
                    {
                        tileactions.Add(lastAction);
                    }
                    lastAction = null;
                }

                var props = new List<TiledProperty>();
                var caveProp = GetCaveId(action, column, rowIndex);
                if (caveProp != null) props.AddRange(caveProp);
                if (action == TileAction.Raft) props.Add(new TiledProperty(TiledObjectProperties.Direction, Direction.Up.ToString()));
                if (action == TileAction.Push && isOverworld) props.Add(new TiledProperty(TiledObjectProperties.Reveals, shortcutStairsName));
                var newaction = new ActionableTiles(column, rowIndex, 0, 1, questId, action, props.Count == 0 ? null : props.ToArray());

                if (lastAction != null && !lastAction.CanRepeat(newaction))
                {
                    if (lastAction.Action != TileAction.None)
                    {
                        tileactions.Add(lastAction);
                    }
                    lastAction = newaction;
                }

                lastAction ??= newaction;
                lastAction = lastAction.ExpandHeight();

                rowIndex += 2;

                var repeatCount = columnRow.RepeatCount;
                for (var m = 0; m < repeatCount && rowIndex < rowEnd; m++)
                {
                    loadMobFunc(ref map, rowIndex, column, tileRef);
                    if (action != TileAction.None)
                    {
                        lastAction = lastAction.ExpandHeight();
                    }
                    rowIndex += 2;
                }
            }

            if (lastAction != null && lastAction.Action != TileAction.None)
            {
                tileactions.Add(lastAction);
            }
        }

        // This is then a pretty basic rectification of those repeated strips.
        // The white sword cave screen is an example of a screen it's not the best.
        // It could be 2 rectangles instead of 3.
        var toremove = new HashSet<ActionableTiles>();
        var grouped = tileactions
            .GroupBy(t => new ActionableTileGrouping(t.Y, t.Height, t.Action, t.HashProperties()))
            .ToArray();

        foreach (var actionGroup in grouped)
        {
            var ordered = actionGroup.OrderBy(t => t.X).ToArray();
            var last = ordered[0];
            foreach (var action in ordered.Skip(1))
            {
                if (action.X == last.Right)
                {
                    toremove.Add(last);
                    toremove.Add(action);
                    last = last.ExpandWidth();
                    tileactions.Add(last);
                    continue;
                }

                last = action;
            }
        }

        tileactions.RemoveAll(toremove.Contains);

        if (!isOverworld) // JOE: TODO: && !isCellar
        {
            // var uwRoomAttrs = CurrentUWRoomAttrs;
            // if (uwRoomAttrs.HasBlock())
            // {
            //     for (var c = _startCol; c < _startCol + _colCount; c += 2)
            //     {
            //         var tileRef = _tileMaps[_curTileMapIndex].Refs(UWBlockRow, c);
            //         if (tileRef == (byte)BlockObjType.TileBlock)
            //         {
            //             ActionFuncs[(int)TileAction.Block](UWBlockRow, c, TileInteraction.Load);
            //             break;
            //         }
            //     }
            // }
        }

        // for (var i = 0; i < ScreenRows * ScreenColumns; i++)
        // {
        //     var t = map.Refs(i);
        //     map.Behaviors(i) = _tileBehaviors[t];
        // }

        // PatchTileBehaviors();

        if (isOverworld && !doesRoomSupportLadder)
        {
            tileactions = tileactions.Where(t => t.Action != TileAction.Ladder).ToList();
        }

        if (!isOverworld || !hasDock)
        {
            tileactions = tileactions.Where(t => t.Action != TileAction.Raft).ToList();
        }

        actions = tileactions.ToArray();

        return map;
    }

    readonly record struct ActionableTileGrouping(int Y, int Height, TileAction Action, int PropertyHash);

    public TiledTile[] DrawMap(TileMap map, bool isOverworld, int roomId, int offsetX, int offsetY)
    {
        var outerPalette = _roomAttributes[roomId].GetOuterPalette();
        var innerPalette = _roomAttributes[roomId].GetInnerPalette();

        // if (IsUWCellar(roomId) || IsPlayingCave())
        // {
        //     outerPalette = (Palette)3;
        //     innerPalette = (Palette)2;
        // }

        var firstRow = 0;
        var lastRow = ScreenRows;
        var tileOffsetY = offsetY;

        var firstCol = 0;
        var lastCol = ScreenColumns;
        var tileOffsetX = offsetX;

        if (offsetY < 0)
        {
            firstRow = -offsetY / TileHeight;
            tileOffsetY = -(-offsetY % TileHeight);
        }
        else if (offsetY > 0)
        {
            lastRow = ScreenRows - offsetY / TileHeight;
        }
        else if (offsetX < 0)
        {
            firstCol = -offsetX / TileWidth;
            tileOffsetX = -(-offsetX % TileWidth);
        }
        else if (offsetX > 0)
        {
            lastCol = ScreenColumns - offsetX / TileWidth;
        }

        var endCol = _startCol + _colCount;
        var endRow = _startRow + _rowCount;

        var y = TileMapBaseY + tileOffsetY;

        if (!isOverworld ) // TODO: "&& !isCellar"
        {
            // Graphics.DrawImage(
            //     _wallsBmp,
            //     0, 0,
            //     TileMapWidth, TileMapHeight,
            //     offsetX, TileMapBaseY + offsetY,
            //     outerPalette, 0);
        }

        // var backgroundSheet = isOverworld ? TileSheet.BackgroundOverworld : TileSheet.BackgroundUnderworld;

        var tiles = new List<TiledTile>(lastRow * lastCol);

        for (var row = firstRow; row < lastRow; row++, y += TileHeight)
        {
            if (row < _startRow || row >= endRow) continue;

            var x = tileOffsetX;
            for (var column = firstCol; column < lastCol; column++, x += TileWidth)
            {
                if (column < _startCol || column >= endCol) continue;

                var tileRef = map.Refs(row, column);
                // var srcX = (tileRef & 0x0F) * TileWidth;
                // var srcY = ((tileRef & 0xF0) >> 4) * TileHeight;

                // var palette = (row is < 4 or >= 18 || column is < 4 or >= 28) ? outerPalette : innerPalette;

                // Graphics.DrawTile(backgroundSheet, srcX, srcY, TileWidth, TileHeight, x, y, palette, 0);
                tiles.Add(TiledTile.Create(tileRef + 1));
            }
        }

        if (!isOverworld) // TODO: "&& !isCellar"
        {
            // DrawDoors(roomId, false, offsetX, offsetY);
        }

        return tiles.ToArray();

        // return new TiledLayer(ScreenColumns, ScreenRows, tiles.ToArray())
        // {
        //     Name = $"Room {roomId}",
        //     Encoding = "base64",
        //     Compression = "",
        //     Type = TiledLayerType.TileLayer,
        //     Visible = true,
        //     Opacity = 1.0f,
        // };
    }

    public static void GetRoomCoord(int position, out int row, out int col)
    {
        row = position & 0x0F;
        col = (position & 0xF0) >> 4;
        row -= 4;
    }

    private void LoadOWMapSquare(ref TileMap map, int row, int col, int squareIndex)
    {
        // Square table:
        // - Is > $10, then refers to upper left, bottom left, upper right, bottom right.
        // - Is <= $10, then refers to secondary table, which are the reused squares.

        // Secondary square table:
        // - Each entry is 4 bytes long. 16 entries in total, indexed by values in the primary square table that are less than 16.
        // - Bytes specify tile numbers (in pattern table 1)

        var primary = _owTable[squareIndex];

        if (primary == 0xFF)
        {
            var index = squareIndex * 4;
            var secondaries = _owTable2;
            map[row, col] = secondaries[index + 0];
            map[row, col + 1] = secondaries[index + 2];
            map[row + 1, col] = secondaries[index + 1];
            map[row + 1, col + 1] = secondaries[index + 3];
        }
        else
        {
            map[row, col] = primary;
            map[row, col + 1] = (byte)(primary + 2);
            map[row + 1, col] = (byte)(primary + 1);
            map[row + 1, col + 1] = (byte)(primary + 3);
        }
    }

    private void LoadUWMapSquare(ref TileMap map, int row, int col, int squareIndex)
    {
        var primary = _owTable[squareIndex];

        if (primary is < 0x70 or > 0xF2)
        {
            map[row, col] = primary;
            map[row, col + 1] = primary;
            map[row + 1, col] = primary;
            map[row + 1, col + 1] = primary;
        }
        else
        {
            map[row, col] = primary;
            map[row, col + 1] = (byte)(primary + 2);
            map[row + 1, col] = (byte)(primary + 1);
            map[row + 1, col + 1] = (byte)(primary + 3);
        }
    }

    private void LoadOpenRoomContext()
    {
        _colCount = 32;
        _rowCount = 22;
        _startRow = 0;
        _startCol = 0;
        _tileTypeCount = 56;
        _marginRight = OWMarginRight;
        _marginLeft = OWMarginLeft;
        _marginBottom = OWMarginBottom;
        _marginTop = OWMarginTop;
    }

    private void LoadClosedRoomContext()
    {
        _colCount = 24;
        _rowCount = 14;
        _startRow = 4;
        _startCol = 4;
        _tileTypeCount = 9;
        _marginRight = UWMarginRight;
        _marginLeft = UWMarginLeft;
        _marginBottom = UWMarginBottom;
        _marginTop = UWMarginTop;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RoomAttr
{
    public byte UniqueRoomId;
    public byte PalettesAndMonsterCount;
    public byte MonsterListId;

    public byte A;
    public byte B;
    public byte C;
    public byte D;

    public readonly int GetUniqueRoomId() => UniqueRoomId & 0x7F;
    public readonly Palette GetOuterPalette() => (Palette)(PalettesAndMonsterCount & 0x03);
    public readonly Palette GetInnerPalette() => (Palette)((PalettesAndMonsterCount >> 2) & 0x03);
    public readonly int GetMonsterCount() => (PalettesAndMonsterCount >> 4) & 0x0F;

    public static implicit operator OWRoomAttr(RoomAttr b) => new(b);
    public static implicit operator UWRoomAttr(RoomAttr b) => new(b);
}

internal readonly record struct OWRoomAttr(RoomAttr Attrs)
{
    public byte GetExitPosition() => Attrs.A;
    public CaveId GetCaveId() => (CaveId)(Attrs.B & 0x3F);
    public int GetShortcutStairsIndex() => (Attrs.C & 0x03);
    public bool HasZora() => (Attrs.C & 0x04) != 0;
    public bool DoMonstersEnter() => (Attrs.C & 0x08) != 0;
    public bool HasAmbientSound() => (Attrs.C & 0x10) != 0;
    public int QuestNumber() => Attrs.B >> 6;
    public bool IsInQuest(int quest)
    {
        var questId = Attrs.B >> 6;
        return questId == 0 || questId == quest + 1;
    }

    public int GetUniqueRoomId() => Attrs.GetUniqueRoomId();
    public Palette GetOuterPalette() => Attrs.GetOuterPalette();
    public Palette GetInnerPalette() => Attrs.GetInnerPalette();
    public int GetMonsterCount() => Attrs.GetMonsterCount();
}

internal readonly record struct UWRoomAttr(RoomAttr Attrs)
{
    public DoorType GetDoor(int dirOrd) => (DoorType)(dirOrd switch
    {
        0 => Attrs.B & 7,
        1 => (Attrs.B >> 3) & 7,
        2 => Attrs.A & 7,
        3 => (Attrs.A >> 3) & 7,
        _ => 1,
    });

    public int GetLeftCellarExitRoomId() => Attrs.A;
    public int GetRightCellarExitRoomId() => Attrs.B;

    public ItemId GetItemId()
    {
        var itemId = Attrs.C & 0x1F;
        return (ItemId)(itemId == 3 ? 0x3F : itemId);
    }

    public int GetItemPositionIndex() => (Attrs.C >> 5) & 3;
    public Secret GetSecret() => (Secret)(Attrs.D & 7);
    public bool HasBlock() => (Attrs.D & 0x08) != 0;
    public bool IsDark() => (Attrs.D & 0x10) != 0;
    public int GetAmbientSound() => (Attrs.D >> 5) & 3;

    public int GetUniqueRoomId() => Attrs.GetUniqueRoomId();
    public Palette GetOuterPalette() => Attrs.GetOuterPalette();
    public Palette GetInnerPalette() => Attrs.GetInnerPalette();
    public int GetMonsterCount() => Attrs.GetMonsterCount();
}

internal enum Secret { None, FoesDoor, Ringleader, LastBoss, BlockDoor, BlockStairs, MoneyOrLife, FoesItem }

internal enum DoorType { Open, None, FalseWall, FalseWall2, Bombable, Key, Key2, Shutter }

internal static class TileAttr
{
    public static TileAction GetAction(byte t) => (TileAction)((t & 0xF0) >> 4);

    public static bool IsQuadrantBlocked(byte t, int row, int col)
    {
        byte walkBit = 1;
        walkBit <<= col * 2;
        walkBit <<= row;
        return (t & walkBit) != 0;
    }

    public static bool IsTileBlocked(byte t) => (t & 0x0F) != 0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct LevelInfoBlock
{
    public const int LevelPaletteCount = 8;
    public const int LevelShortcutCount = 4;
    public const int LevelCellarCount = 10;
    public const int FadeLength = 4;
    public const int FadePals = 2;
    public const int MapLength = 16;
    public const int PaletteLength = 4;

    public fixed byte Palettes[LevelPaletteCount * PaletteLength];
    public byte StartY;
    public byte StartRoomId;
    public byte TriforceRoomId;
    public byte BossRoomId;
    public byte Song;
    public byte LevelNumber;
    public byte EffectiveLevelNumber;
    public byte DrawnMapOffset;
    public fixed byte CellarRoomIds[LevelCellarCount];
    public fixed byte ShortcutPosition[LevelShortcutCount];
    public fixed byte DrawnMap[MapLength];
    public fixed byte Padding[2];
    public fixed byte OutOfCellarPaletteSeq[FadeLength * FadePals * PaletteLength];
    public fixed byte InCellarPaletteSeq[FadeLength * FadePals * PaletteLength];
    public fixed byte DarkPaletteSeq[FadeLength * FadePals * PaletteLength];
    public fixed byte DeathPaletteSeq[FadeLength * FadePals * PaletteLength];

    public ReadOnlySpan<byte> GetPalette(int index)
    {
        fixed (byte* p = Palettes)
        {
            return new ReadOnlySpan<byte>(p + index * PaletteLength, PaletteLength);
        }
    }

    public ReadOnlySpan<byte> OutOfCellarPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &OutOfCellarPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength);
        }
    }

    public ReadOnlySpan<byte> InCellarPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &InCellarPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength);
        }
    }

    public ReadOnlySpan<byte> DarkPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &DarkPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength);
        }
    }

    public ReadOnlySpan<byte> DeathPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &DeathPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength);
        }
    }

    public byte[][] DeathPalettes(int index)
    {
        // JOE: I dislike this.
        var result = new byte[FadePals][];
        for (var i = 0; i < FadePals; i++)
        {
            result[i] = DeathPalette(index, i).ToArray();
        }

        return result;
    }
}

internal readonly struct ListResource<T>
    where T : struct
{
    public T this[int i]
    {
        get => Backing[i];
        set => Backing[i] = value;
    }

    public readonly T[] Backing;

    public ListResource(T[] backing)
    {
        Backing = backing;
    }

    public static ListResource<T> Load(byte[] bytes)
    {
        var length = BitConverter.ToInt16(bytes);
        bytes = bytes[sizeof(short)..];
        if (bytes.Length != length) throw new InvalidOperationException();
        return new ListResource<T>(MemoryMarshal.Cast<byte, T>(bytes).ToArray());
    }

    public static ReadOnlySpan<T> LoadList(ReadOnlySpan<byte> bytes, int amount)
    {
        return MemoryMarshal.Cast<byte, T>(bytes)[..amount];
    }

    public static T LoadSingle(ReadOnlySpan<byte> bytes) => LoadList(bytes, 1)[0];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct RoomCols
{
    public fixed byte ColumnDesc[MobColumns];

    public byte[] Get()
    {
        var result = new byte[MobColumns];
        for (var i = 0; i < MobColumns; i++)
        {
            result[i] = ColumnDesc[i];
        }
        return result;
    }
}