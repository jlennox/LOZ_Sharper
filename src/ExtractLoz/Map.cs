using System.Drawing;
using System.Runtime.InteropServices;
#pragma warning disable CA1416

namespace ExtractLoz;

using static World;

internal sealed class World
{
    public const int MobColumns = 16;
    public const int RoomTileHeight = 22;
    public const int RoomTileWidth = 32;
    public const int BlockWidth = 16;
    public const int BlockHeight = 16;
    public const int TileWidth = 8;
    public const int TileHeight = 8;
    public const int TileMapWidth = RoomTileWidth * TileWidth;
    public const int Rooms = 128;
    public const int UniqueRooms = 124;
    public const int TileMapHeight = RoomTileHeight * TileHeight;
    public const int TileMapBaseY = 64;
    public const int StartX = 0x78;

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

    public const int UWBlockRow = 10;
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


    // public TAs GetItem<TAs>(Extra extra) where TAs : struct => GetItem<TAs>((int)extra);
    // public ReadOnlySpan<TAs> GetItems<TAs>(Extra extra) where TAs : struct => GetItems<TAs>((int)extra);
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
    public byte RoomId;
    public byte Pos;

    public Point GetRoomCoord()
    {
        var row = Pos & 0x0F;
        var col = (Pos & 0xF0) >> 4;
        row -= 4;
        return new Point(col, row);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparsePos2
{
    public byte RoomId;
    public byte X;
    public byte Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparseRoomItem
{
    public byte RoomId;
    public byte X;
    public byte Y;
    public byte ItemId;

    public readonly ItemId AsItemId => (ItemId)ItemId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct SparseMaze
{
    public readonly byte RoomId;
    public readonly byte ExitDir;
    public fixed byte Path[4];

    public readonly Direction ExitDirection => (Direction)ExitDir;
    public readonly ReadOnlySpan<Direction> Paths => new[] { (Direction)Path[0], (Direction)Path[1], (Direction)Path[2], (Direction)Path[3], };
}

// internal enum Extra
// {
//     PondColors,
//     SpawnSpots,
//     ObjAttrs,
//     CavePalettes,
//     Caves,
//     LevelPersonStringIds,
//     HitPoints,
//     PlayerDamage,
// }

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
    public const int Size = World.RoomTileHeight * World.RoomTileWidth;

    public byte this[int row, int col]
    {
        get => _tileRefs[row * World.RoomTileWidth + col];
        set => _tileRefs[row * World.RoomTileWidth + col] = (byte)value;
    }

    private readonly byte[] _tileRefs = new byte[Size];
    private readonly byte[] _tileBehaviors = new byte[Size];

    public ref byte Refs(int index) => ref _tileRefs[index];
    public ref byte Refs(int row, int col) => ref _tileRefs[row * World.RoomTileWidth + col];
    public ref byte Behaviors(int row, int col) => ref _tileBehaviors[row * World.RoomTileWidth + col];
    public ref byte Behaviors(int index) => ref _tileBehaviors[index];
}

internal sealed class MapExtractor
{
    private readonly MapResources _resources;
    private readonly byte[,] _wallTileMap;

    private delegate void LoadMobDelegate(ref TileMap map, MapResources resources, int row, int col, int squareIndex);

    public record ActionableTiles(
        int TileX, int TileY, int Height, int Width, int QuestId, TileAction Action,
        TiledProperty[]? Properties = null)
    {
        // public int BlockRight => TileX + Width * 2;
        //
        // public bool CanRepeat(ActionableTiles other)
        // {
        //     if (Action != other.Action) return false;
        //     var lengthA = Properties?.Length ?? 0;
        //     var lengthB = other.Properties?.Length ?? 0;
        //     if (lengthA != lengthB) return false;
        //     if (lengthA == 0 && lengthB == 0) return true;
        //     var sortedA = Properties!.OrderBy(p => p.Name).ToArray();
        //     var sortedB = other.Properties!.OrderBy(p => p.Name).ToArray();
        //     for (var i = 0; i < sortedA.Length; i++)
        //     {
        //         var a = sortedA[i];
        //         var b = sortedB[i];
        //         if (a.Name != b.Name || a.Type != b.Type || a.Value != b.Value) return false;
        //     }
        //     return true;
        // }
        //
        // public int HashProperties()
        // {
        //     if (Properties == null) return 0;
        //     var hasher = new HashCode();
        //     foreach (var prop in Properties.OrderBy(t => t.Name))
        //     {
        //         hasher.Add(prop.Name);
        //         hasher.Add(prop.Type);
        //         hasher.Add(prop.Value);
        //     }
        //     return hasher.ToHashCode();
        // }
        //
        // public ActionableTiles ExpandHeight() => this with { Height = Height + 1 };
        // public ActionableTiles ExpandWidth() => this with { Width = Width + 1 };

        public TiledLayerObject CreateTiledLayerObject()
        {
            return new TiledLayerObject
            {
                // Id = screenY * World.WorldWidth + screenX,
                X = TileX * World.TileWidth,
                Y = TileY * World.TileHeight,
                Width = Width * World.BlockWidth,
                Height = Height * World.BlockHeight,
                Name = $"{Action}",
                Visible = true,
                Properties = [
                    // new TiledProperty(TiledObjectProperties.Type, GameObjectLayerObjectType.Interactive),
                    // new TiledProperty(TiledObjectProperties.TileAction, Action),
                    // new TiledProperty(TiledObjectProperties.Owner, GetScreenName(screenX, screenY)),
                    .. (Properties ?? [])
                ],
            };
        }
    }

    private int _rowCount;
    private int _colCount;
    private int _startRow;
    private int _startCol;
    private int _tileTypeCount;
    private int _marginRight;
    private int _marginLeft;
    private int _marginBottom;
    private int _marginTop;

    public MapExtractor(MapResources resources, byte[,] wallTileMap)
    {
        _resources = resources;
        _wallTileMap = wallTileMap;
    }

    public unsafe TileMap LoadLayout(RoomId roomId, out ActionableTiles[] actions)
    {
        var isOverworld = _resources.IsOverworld;
        var isCellar = _resources.IsCellarRoom(roomId);
        var resources = (isCellar ? _resources.CellarResources : _resources) ?? throw new Exception();
        _colCount = resources.RoomContext.ColCount;
        _rowCount = resources.RoomContext.RowCount;
        _startRow = resources.RoomContext.StartRow;
        _startCol = resources.RoomContext.StartCol;
        _tileTypeCount = resources.RoomContext.TileTypeCount;
        _marginRight = resources.RoomContext.MarginRight;
        _marginLeft = resources.RoomContext.MarginLeft;
        _marginBottom = resources.RoomContext.MarginBottom;
        _marginTop = resources.RoomContext.MarginTop;

        var tileactions = new List<ActionableTiles>();

        var roomAttrs = resources.RoomAttrs[roomId.Id];
        var uniqueRoomId = roomAttrs.GetUniqueRoomId(roomId) - (isCellar ? 0x3E : 0);

        var maxColumnStartOffset = (_colCount / 2 - 1) * _rowCount / 2;

        var map = new TileMap();
        var rowEnd = _startRow + _rowCount;

        LoadMobDelegate loadMobFunc = isOverworld || isCellar ? LoadOWMapSquare : LoadUWMapSquare;

        var owRoomAttrs = new OWRoomAttr(roomAttrs);
        var uwRoomAttrs = new UWRoomAttr(roomAttrs);

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

        static PointXY GetTileCoords(int x, int y)
        {
            return new(x / TileWidth, (y - TileMapBaseY) / TileHeight);
        }

        var columns = resources.GetRoomColsArray()[uniqueRoomId];
        if (columns.Length != MobColumns) throw new Exception();

        bool FindSparseFlag(Sparse attrId) => resources.SparseTable.FindSparseAttr<SparsePos>(attrId, roomId.Id).HasValue;
        SparsePos? FindSparsePos(Sparse attrId) => resources.SparseTable.FindSparseAttr<SparsePos>(attrId, roomId.Id);
        SparsePos2? FindSparsePos2(Sparse attrId) => resources.SparseTable.FindSparseAttr<SparsePos2>(attrId, roomId.Id);
        SparseRoomItem? FindSparseItem(Sparse attrId) => resources.SparseTable.FindSparseAttr<SparseRoomItem>(attrId, roomId.Id);

        var armosStairs = isOverworld ? FindSparsePos2(Sparse.ArmosStairs) : null;
        var armosItem = isOverworld ? FindSparseItem(Sparse.ArmosItem) : null;
        var hasDock = isOverworld ? FindSparseFlag(Sparse.Dock) : false;
        var hasShortcutStairs = isOverworld ? FindSparseFlag(Sparse.Shortcut) : false;
        // var maze = _sparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, roomId);
        // SecretScroll,
        var doesRoomSupportLadder = FindSparseFlag(Sparse.Ladder);
        var recorderPosition = isOverworld ? FindSparsePos(Sparse.Recorder) : null; ;
        // Fairy,
        // If second quest, do this: _roomAttrs[roomId] = sparseAttr[i].attrs;
        var roomReplacements = isOverworld ? resources.SparseTable.GetItems<byte>(Sparse.RoomReplacement) : default;
        const string shortcutStairsName = "shortcut stairs";
        var levelInfoBlock = resources.LevelInfoBlock;

        var exitPos = owRoomAttrs.GetExitPosition();
        var exitColumnX = (exitPos & 0x0F) * World.BlockWidth;
        var exitRowY = ((exitPos >> 4) + 4) * World.BlockHeight + 0x0D;

        var caveId = owRoomAttrs.GetCaveId();
        var questId = owRoomAttrs.QuestNumber();
        string? caveName = null;
        GameWorldType? caveType = null;
        CaveSpec? caveSpec = null;
        EntryPosition? caveEntryPosition = null;
        if (resources.IsOverworld)
        {
            if ((int)caveId < 9)
            {
                caveName = ((int)caveId).ToString();
                caveType = GameWorldType.Underworld;
            }
            else
            {
                caveName = CommonOverworldRoomName.Cave;
                caveType = GameWorldType.OverworldCommon;
                caveSpec = resources.CaveSpec?.FirstOrDefault(t => (t.CaveId - CaveId.Cave1) == (int)caveId - 0x10);
            }
        }
        else
        {
            var hasCellar = resources.LevelInfoBlock.FindCellarRoomIds(roomId, resources.RoomAttrs, out var left, out var right);
            caveName = hasCellar && left == right ? CommonUnderworldRoomName.ItemCellar : CommonUnderworldRoomName.Transport;
            caveType = GameWorldType.UnderworldCommon;
            var entryX = roomId == left ? 0x30 : 0xC0;
            caveEntryPosition = new EntryPosition(entryX, 0x60, Direction.Down, 0x60);
        }

        var caveEntrance = new Entrance
        {
            DestinationType = caveType.Value,
            Destination = caveName,
            ExitPosition = new PointXY(exitColumnX, exitRowY),
            EntryPosition = caveEntryPosition,
            Cave = caveSpec,
            BlockType = BlockType.Stairs,
        };

        Entrance EntranceWith(BlockType type)
        {
            caveEntrance.BlockType = type;
            return caveEntrance;
        }

        void AddInteraction(int tileX, int tileY, TileAction action, InteractableBlock block)
        {
            AddInteractionWithQuest(tileX, tileY, questId, action, block);
        }

        void AddInteractionWithQuest(int tileX, int tileY, int questId, TileAction action, InteractableBlock block)
        {
            block.CaveItems = block.Entrance?.Cave?.Items;
            var serialized = TiledPropertySerializer<InteractableBlock>.Serialize(block);
            tileactions.Add(new ActionableTiles(tileX, tileY, 1, 1, questId, action, serialized));
        }

        void AddInteractionP(PointXY p, TileAction action, InteractableBlock block)
        {
            AddInteraction(p.X, p.Y, action, block);
        }

        if (recorderPosition != null)
        {
            ReadOnlySpan<int> roomIds = [0x42, 0x06, 0x29, 0x2B, 0x30, 0x3A, 0x3C, 0x58, 0x60, 0x6E, 0x72];

            var i = roomIds.IndexOf(roomId.Id);
            // The first one is level 7 entrance, the others are second quest only.
            var actionQuestId = i switch
            {
                0 => 1,
                >= 1 => 2,
                _ => throw new Exception()
            };

            var effect = actionQuestId == 1 ? InteractionEffect.DryoutWater : InteractionEffect.None;
            var rec = recorderPosition.Value.GetRoomCoord();
            AddInteractionWithQuest(rec.X * 2, rec.Y * 2, actionQuestId, TileAction.Recorder, new InteractableBlock
            {
                Interaction = Interaction.Recorder,
                Entrance = caveEntrance,
                Effect = effect,
            });
        }

        if (resources.IsOverworld)
        {
            var roomItem = FindSparseItem(Sparse.Item);

            if (roomItem != null)
            {
                var item = roomItem.Value;
                AddInteractionP(GetTileCoords(item.X, item.Y), TileAction.Item, new InteractableBlock
                {
                    Interaction = Interaction.None,
                    Item = new RoomItem { Item = item.AsItemId }
                });
            }
        }
        else
        {
            var secret = uwRoomAttrs.GetSecret();

            var uwItemId = uwRoomAttrs.GetItemId();
            if (uwItemId != ItemId.MAX && uwItemId != ItemId.TriforcePiece)
            {
                var posIndex = uwRoomAttrs.GetItemPositionIndex();
                var block = resources.LevelInfoBlock;
                var pos = block.GetShortcutPosition(posIndex);

                if (secret is Secret.FoesItem or Secret.LastBoss)
                {
                    AddInteractionP(GetTileCoords(pos.X, pos.Y), TileAction.Item, new InteractableBlock
                    {
                        Interaction = Interaction.None,
                        Requirements = InteractionRequirements.AllEnemiesDefeated,
                        Item = new RoomItem { Item = uwItemId },
                        Persisted = true,
                    });
                }
                else
                {
                    AddInteractionP(GetTileCoords(pos.X, pos.Y), TileAction.Item, new InteractableBlock
                    {
                        Interaction = Interaction.None,
                        Item = new RoomItem { Item = uwItemId, IsRoomItem = true },
                        Persisted = true,
                    });
                }
            }

            if (secret == Secret.BlockStairs)
            {
                AddInteractionP(GetTileCoords(0xD0, 0x60), TileAction.Stairs, new InteractableBlock
                {
                    Interaction = Interaction.Revealed,
                    Entrance = EntranceWith(BlockType.Stairs),
                });
            }

            if (resources.LevelInfoBlock.TriforceRoomId == roomId.Id)
            {
                AddInteraction(RoomTileWidth / 2 - 1, RoomTileHeight / 2 - 1, TileAction.Item, new InteractableBlock
                {
                    Interaction = Interaction.None,
                    Item = new RoomItem { Item = ItemId.TriforcePiece },
                    Persisted = true,
                });
            }
        }

        var markFirstBlock = !resources.IsOverworld && uwRoomAttrs.HasBlock()
            && uwRoomAttrs.GetSecret() is Secret.BlockDoor or Secret.BlockStairs or Secret.None;

        void CheckUWBlock(int x, int y)
        {
            if (!markFirstBlock) return;
            if (y != UWBlockRow) return;
            if (map.Refs(UWBlockRow, x) != (byte)TileType.Block) return;

            markFirstBlock = false;

            var interaction = uwRoomAttrs.GetSecret() switch
            {
                Secret.BlockStairs => new InteractableBlock
                {
                    Interaction = Interaction.Push,
                    ApparanceBlock = BlockType.Block,
                    Repeatable = false,
                    Reveals = shortcutStairsName,
                },
                Secret.BlockDoor => new InteractableBlock
                {
                    Interaction = Interaction.Push,
                    ApparanceBlock = BlockType.Block,
                    Repeatable = false,
                    Effect = InteractionEffect.OpenShutterDoors,
                    Requirements = InteractionRequirements.AllEnemiesDefeated,

                },
                Secret.None => new InteractableBlock
                {
                    Interaction = Interaction.Push,
                    ApparanceBlock = BlockType.Block,
                    Repeatable = false,
                },
                _ => throw new Exception()
            };

            AddInteraction(x, y, TileAction.PushBlock, interaction);
        }

        if (hasShortcutStairs)
        {
            // Underworld only?
            var stairsIndex = owRoomAttrs.GetShortcutStairsIndex();
            var stairsPos = levelInfoBlock.ShortcutPosition[stairsIndex];
            GetRoomCoord(stairsPos, out var stairsRow, out var stairsCol);
            AddInteraction(stairsCol * 2, stairsRow * 2, TileAction.Cave, new InteractableBlock
            {
                Interaction = Interaction.None,
                Entrance = EntranceWith(BlockType.Cave),
            });
        }

        for (var columnDescIndex = 0; columnDescIndex < _colCount / 2; columnDescIndex++)
        {
            var columnDesc = columns[columnDescIndex];

            var tableIndex = (byte)((columnDesc & 0xF0) >> 4);
            var columnIndex = (byte)(columnDesc & 0x0F);

            var columnTable = resources.ColTable.GetItem(tableIndex);
            var columnStart = GetColumnStart(columnTable, columnIndex, maxColumnStartOffset);
            var columnX = _startCol + columnDescIndex * 2;

            for (var rowIndexY = _startRow; rowIndexY < rowEnd; columnStart++)
            {
                var columnRow = new ColumnRow(columnTable[columnStart], isOverworld || isCellar);
                var tileRef = columnRow.SquareNumber;

                loadMobFunc(ref map, resources, rowIndexY, columnX, tileRef);
                CheckUWBlock(columnX, rowIndexY);

                rowIndexY += 2;

                var repeatCount = columnRow.RepeatCount;
                for (var m = 0; m < repeatCount && rowIndexY < rowEnd; m++)
                {
                    loadMobFunc(ref map, resources, rowIndexY, columnX, tileRef);
                    CheckUWBlock(columnX, rowIndexY);
                    rowIndexY += 2;
                }
            }

            ActionableTiles? lastAction = null;

            // This gives us vertical strips for repeated actions.
            columnStart = GetColumnStart(columnTable, columnIndex, maxColumnStartOffset);
            for (var rowIndexY = _startRow; rowIndexY < rowEnd; columnStart++)
            {
                var columnRow = new ColumnRow(columnTable[columnStart], isOverworld || isCellar);
                var tileRef = columnRow.SquareNumber;
                var attr = resources.TileAttrs[tileRef];
                var action = TileAttr.GetAction(attr);

                if (lastAction != null && lastAction.Action != action)
                {
                    if (lastAction.Action != TileAction.None)
                    {
                        tileactions.Add(lastAction);
                    }
                    lastAction = null;
                }

                var lookup = new Dictionary<TileAction, Interaction>
                {
                    { TileAction.Cave, Interaction.None },
                    { TileAction.Stairs, Interaction.None },
                    { TileAction.Bomb, Interaction.Bomb },
                    { TileAction.Burn, Interaction.Burn },
                    { TileAction.Recorder, Interaction.Recorder },
                    { TileAction.Ghost, Interaction.Touch },
                    { TileAction.Armos, Interaction.TouchOnce },
                    { TileAction.PushHeadstone, Interaction.Push },
                    { TileAction.Push, Interaction.PushVertical },
                    { TileAction.PushBlock, Interaction.Push },
                    { TileAction.Raft, Interaction.Cover },
                };

                if (lookup.TryGetValue(action, out var interactionType))
                {
                    var interaction = new InteractableBlock
                    {
                        Interaction = interactionType,
                    };

                    var isArmosStairs = armosStairs != null && action == TileAction.Armos;
                    if (isArmosStairs)
                    {
                        var pos = GetTileCoords(armosStairs.Value.X, armosStairs.Value.Y);
                        isArmosStairs = pos.X == columnX && pos.Y == rowIndexY;
                    }

                    if (armosItem != null)
                    {
                        var pos = GetTileCoords(armosItem.Value.X, armosItem.Value.Y);
                        if (pos.X == columnX && pos.Y == rowIndexY)
                        {
                            interaction.Item = new RoomItem { Item = armosItem.Value.AsItemId };
                        }
                    }

                    // TODO: Underworld TileAction.Block is different.
                    if (action is TileAction.Burn or TileAction.PushHeadstone or TileAction.Stairs)
                    {
                        interaction.Entrance = EntranceWith(BlockType.Stairs);
                    }
                    else if (action is TileAction.Cave or TileAction.Bomb || isArmosStairs)
                    {
                        interaction.Entrance = EntranceWith(BlockType.Cave);
                    }

                    var actionQuestId = questId;

                    switch (action)
                    {
                        case TileAction.Cave or TileAction.Bomb or TileAction.Burn:
                            interaction.Persisted = true;
                            break;

                        case TileAction.PushHeadstone:
                            interaction.ApparanceBlock = BlockType.Headstone;
                            break;

                        case TileAction.Armos:
                            interaction.SpawnedType = ObjType.Armos;
                            break;

                        case TileAction.Ghost:
                            interaction.SpawnedType = ObjType.FlyingGhini;
                            interaction.Repeatable = true;
                            break;

                        case TileAction.Push:
                            interaction.ApparanceBlock = BlockType.Rock;
                            interaction.ItemRequirement = new InteractionItemRequirement(ItemSlot.Bracelet, 1);
                            interaction.Reveals = shortcutStairsName;
                            break;

                        case TileAction.PushBlock:
                            interaction.Requirements |= InteractionRequirements.AllEnemiesDefeated;
                            interaction.ApparanceBlock = BlockType.Block;
                            switch (uwRoomAttrs.GetSecret())
                            {
                                case Secret.BlockDoor: interaction.Effect |= InteractionEffect.OpenShutterDoors; break;
                                case Secret.BlockStairs: interaction.Reveals = shortcutStairsName; break;
                            }
                            break;

                        case TileAction.Raft:
                            interaction.Raft = new Raft(Direction.Up);
                            interaction.Repeatable = true;
                            interaction.ItemRequirement = new InteractionItemRequirement(ItemSlot.Raft, 1);
                            break;

                        case TileAction.Recorder:
                            ReadOnlySpan<int> roomIds = [0x42, 0x06, 0x29, 0x2B, 0x30, 0x3A, 0x3C, 0x58, 0x60, 0x6E, 0x72];

                            var i = roomIds.IndexOf(roomId.Id);
                            // The first one is level 7 entrance, the others are second quest only.
                            actionQuestId = i switch
                            {
                                0 => 1,
                                > 0 => 2,
                                _ => throw new Exception()
                            };
                            if (actionQuestId == 1)
                            {
                                interaction.Effect |= InteractionEffect.DryoutWater;
                            }
                            break;
                    }

                    AddInteractionWithQuest(columnX, rowIndexY, actionQuestId, action, interaction);
                }

                // if (lastAction != null && !lastAction.CanRepeat(newaction))
                // {
                //     if (lastAction.Action != TileAction.None)
                //     {
                //          tileactions.Add(lastAction);
                //     }
                //     lastAction = newaction;
                // }
                //
                // lastAction ??= newaction;
                // lastAction = lastAction.ExpandHeight();

                rowIndexY += 2;

                var repeatCount = columnRow.RepeatCount;
                for (var m = 0; m < repeatCount && rowIndexY < rowEnd; m++)
                {
                    // loadMobFunc(ref map, resources, rowIndexY, columnX, tileRef);
                    // CheckUWBlock(columnX, rowIndexY);


                    // if (action != TileAction.None)
                    // {
                    //     lastAction = lastAction.ExpandHeight();
                    // }
                    rowIndexY += 2;
                }
            }

            if (lastAction != null && lastAction.Action != TileAction.None)
            {
                tileactions.Add(lastAction);
            }
        }

        // This is now disabled because rectification doesn't matter so much when tiles aren't being marked used for ladders.
        //
        // This is then a pretty basic rectification of those repeated strips.
        // The white sword cave screen is an example of a screen it's not the best.
        // It could be 2 rectangles instead of 3.
        // var toremove = new HashSet<ActionableTiles>();
        // var grouped = tileactions
        //     .GroupBy(t => new ActionableTileGrouping(t.Y, t.Height, t.Action, t.HashProperties()))
        //     .ToArray();
        //
        // foreach (var actionGroup in grouped)
        // {
        //     var ordered = actionGroup.OrderBy(t => t.X).ToArray();
        //     var last = ordered[0];
        //     foreach (var action in ordered.Skip(1))
        //     {
        //         if (action.X == last.Right)
        //         {
        //             toremove.Add(last);
        //             toremove.Add(action);
        //             last = last.ExpandWidth();
        //             tileactions.Add(last);
        //             continue;
        //         }
        //
        //         last = action;
        //     }
        // }
        //
        // tileactions.RemoveAll(toremove.Contains);

        if (!isOverworld) // JOE: TODO: && !isCellar
        {
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

        // for (var i = 0; i < RoomRows * RoomColumns; i++)
        // {
        //     var t = map.Refs(i);
        //     map.Behaviors(i) = _tileBehaviors[t];
        // }

        // PatchTileBehaviors();

        // I now think it's correcter to just use the room flag and then allow it to be negated on a tile by tile basis.
        tileactions = tileactions.Where(t => t.Action != TileAction.Ladder).ToList();
        if (isOverworld && !doesRoomSupportLadder)
        {
        }

        if (!isOverworld || !hasDock)
        {
            tileactions = tileactions.Where(t => t.Action != TileAction.Raft).ToList();
        }

        actions = tileactions.ToArray();

        return map;
    }

    public TiledTile[] DrawMap(TileMap map, RoomId roomId)
    {
        const int firstRow = 0;
        const int lastRow = RoomTileHeight;

        const int firstCol = 0;
        const int lastCol = RoomTileWidth;

        var tileset = _resources.IsOverworld ? 0 : 1;
        var tiles = new List<TiledTile>(lastRow * lastCol);

        for (var rowY = firstRow; rowY < lastRow; rowY++)
        {
            for (var columnX = firstCol; columnX < lastCol; columnX++)
            {
                // If we're in the underworld, we want to draw the walls but leave cutouts for where the doors go.
                if (!_resources.IsOverworld && !_resources.IsCellarRoom(roomId))
                {
                    var mapped = _wallTileMap[rowY, columnX];
                    if (mapped != 0)
                    {
                        var wallTile = TiledTile.Create(mapped, tileset);
                        var halfY = (lastRow / 2) - 2;
                        var halfX = (lastCol / 2) - 2;
                        if ((rowY >= halfY && rowY <= halfY + 3) || (columnX >= halfX && columnX <= halfX + 3))
                        {
                            wallTile = TiledTile.Empty;
                        }
                        tiles.Add(wallTile);
                        continue;
                    }
                }
                var tileRef = map.Refs(rowY, columnX);
                tiles.Add(tileRef == 0 ? TiledTile.Empty : TiledTile.Create(tileRef, tileset));
            }
        }

        return tiles.ToArray();
    }

    public static void GetRoomCoord(int position, out int row, out int col)
    {
        row = position & 0x0F;
        col = (position & 0xF0) >> 4;
        row -= 4;
    }

    private static void LoadOWMapSquare(ref TileMap map, MapResources resources, int row, int col, int squareIndex)
    {
        // Square table:
        // - Is > $10, then refers to upper left, bottom left, upper right, bottom right.
        // - Is <= $10, then refers to secondary table, which are the reused squares.

        // Secondary square table:
        // - Each entry is 4 bytes long. 16 entries in total, indexed by values in the primary square table that are less than 16.
        // - Bytes specify tile numbers (in pattern table 1)

        var primary = resources.PrimaryMobs[squareIndex];

        if (primary == 0xFF)
        {
            var index = squareIndex * 4;
            var secondaries = resources.SecondaryMobs;
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

    private static void LoadUWMapSquare(ref TileMap map, MapResources resources, int row, int col, int squareIndex)
    {
        var primary = resources.PrimaryMobs[squareIndex];

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
    public readonly int GetUniqueRoomId(RoomId roomId) => (roomId.UniqueRoomId ?? (UniqueRoomId & 0x7F));
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
}

internal readonly record struct UWRoomAttr(RoomAttr Attrs)
{
    public DoorType GetDoor(Direction dir) => (DoorType)(dir switch
    {
        Direction.Right => Attrs.B & 7,
        Direction.Left => (Attrs.B >> 3) & 7,
        Direction.Down => Attrs.A & 7,
        Direction.Up => (Attrs.A >> 3) & 7,
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
}

internal enum Secret { None, FoesDoor, Ringleader, LastBoss, BlockDoor, BlockStairs, MoneyOrLife, FoesItem }

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

    public PointXY GetShortcutPosition(int index)
    {
        var bytePos = ShortcutPosition[index];
        return new PointXY(bytePos & 0xF0, (byte)(bytePos << 4));
    }

    public WorldSettings GetWorldInfo(GameWorldType type)
    {
        static byte[][][] GetTriple(Func<int, int, byte[]> func)
        {
            var palettes = new byte[PaletteLength][][];
            for (var index = 0; index < PaletteLength; index++)
            {
                palettes[index] = new byte[FadePals][];
                for (var fade = 0; fade < FadePals; fade++)
                {
                    palettes[index][fade] = func(index, fade);
                }
            }

            return palettes;
        }

        var options = type == GameWorldType.Overworld ? WorldOptions.AllowWhirlwind : WorldOptions.None;

        var that = this;
        return new WorldSettings
        {
            WorldType = type,
            Options = options,
            Palettes = Enumerable.Range(0, LevelPaletteCount).Select(that.GetPalette).ToArray(),
            // EnterAt = new PointXY(StartX, StartY),
            // StartRoomId = StartRoomId,
            // TriforceRoomId = TriforceRoomId,
            // BossRoomId = BossRoomId,
            SongId = (SongId)Song,
            LevelNumber = LevelNumber,
            // EffectiveLevelNumber = EffectiveLevelNumber,
            // DrawnMapOffset = DrawnMapOffset,
            // CellarRoomIds = that.CellarRoomIds.ToArray(),
            OutOfCellarPalette = GetTriple(OutOfCellarPalette),
            InCellarPalette = GetTriple(InCellarPalette),
            DarkPalette = GetTriple(DarkPalette),
            DeathPalette = GetTriple(DeathPalette),
        };
    }

    public byte[] GetPalette(int index)
    {
        fixed (byte* p = Palettes)
        {
            return new ReadOnlySpan<byte>(p + index * PaletteLength, PaletteLength).ToArray();
        }
    }

    public byte[] OutOfCellarPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &OutOfCellarPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength).ToArray();
        }
    }

    public byte[] InCellarPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &InCellarPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength).ToArray();
        }
    }

    public byte[] DarkPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &DarkPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength).ToArray();
        }
    }

    public byte[] DeathPalette(int index, int fade)
    {
        var i = index * FadePals * PaletteLength + fade * PaletteLength; ;
        fixed (byte* p = &DeathPaletteSeq[i])
        {
            return new ReadOnlySpan<byte>(p, PaletteLength).ToArray();
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

    public bool FindCellarRoomIds(RoomId mainRoomId, RoomAttr[] roomAttrs, out RoomId left, out RoomId right)
    {
        for (var i = 0; i < LevelCellarCount; i++)
        {
            var cellarRoomId = CellarRoomIds[i];
            if (cellarRoomId >= 0x80) break;

            var uwRoomAttrs = new UWRoomAttr(roomAttrs[cellarRoomId]);
            left = new RoomId(uwRoomAttrs.GetLeftCellarExitRoomId());
            right = new RoomId(uwRoomAttrs.GetRightCellarExitRoomId());
            if (mainRoomId == left || mainRoomId == right) return true;
        }

        left = new RoomId(0);
        right = new RoomId(0);
        return false;
    }

    public bool FindCellarItemRoomId(RoomId mainRoomId, RoomAttr[] roomAttrs, out RoomId roomId)
    {
        for (var i = 0; i < LevelCellarCount; i++)
        {
            var cellarRoomId = CellarRoomIds[i];
            if (cellarRoomId >= 0x80) break;

            var uwRoomAttrs = new UWRoomAttr(roomAttrs[cellarRoomId]);
            var left = new RoomId(uwRoomAttrs.GetLeftCellarExitRoomId());
            var right = new RoomId(uwRoomAttrs.GetRightCellarExitRoomId());
            if (mainRoomId == left)
            {
                roomId = new RoomId(cellarRoomId);
                return true;
            }
            if (mainRoomId == right)
            {
                roomId = new RoomId(cellarRoomId);
                return true;
            }
        }

        roomId = new RoomId(0);
        return false;
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