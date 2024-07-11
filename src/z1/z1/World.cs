﻿using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace z1;

using System.Text;
using SkiaSharp;
using z1.Actors;
using z1.UI;

internal enum DoorType { Open, None, FalseWall, FalseWall2, Bombable, Key, Key2, Shutter }
internal enum TileInteraction { Load, Push, Touch, Cover }
internal enum SpritePriority { None, AboveBg, BelowBg }

internal record class Cell(byte Row, byte Col)
{
    public const int MobPatchCellCount = 16;

    public static Cell[] MakeMobPatchCell() => new Cell[MobPatchCellCount];
};

internal abstract class ResourceLoader {}
internal class TableResource<T> : ResourceLoader
    where T : struct
{
    public T[] this[int i]
    {
        get => throw new Exception();
        set => throw new Exception();
    }

    public T this[int i, int j]
    {
        get => throw new Exception();
        set => throw new Exception();
    }

    public readonly int Length;
    public readonly short[] Offsets;
    public readonly T[] Heap;

    public TableResource(int length, short[] offsets, T[] heap)
    {
        Length = length;
        Offsets = offsets;
        Heap = heap;
    }

    public static TableResource<T> Load(string file)
    {
        Span<byte> bytes = File.ReadAllBytes(Assets.Root.GetPath("out", file));

        var length = BitConverter.ToInt16(bytes);
        bytes = bytes[sizeof(short)..];

        var offsetLength = length * sizeof(short);
        var offsets = MemoryMarshal.Cast<byte, short>(bytes[..offsetLength]);

        var heap = bytes[offsetLength..];
        return new TableResource<T>(length, offsets.ToArray(), MemoryMarshal.Cast<byte, T>(heap).ToArray());
    }

    public T[] GetItem(int index) => Heap[Offsets[index]..];

    public TAs GetItem<TAs>(World.Extra extra) where TAs : struct => MemoryMarshal.Cast<T, TAs>(GetItem((int)extra))[0];
    public Span<TAs> GetItems<TAs>(World.Extra extra) where TAs : struct => MemoryMarshal.Cast<T, TAs>(GetItem((int)extra));
    public TAs GetItem<TAs>(World.Sparse extra) where TAs : struct => MemoryMarshal.Cast<T, TAs>(GetItem((int)extra))[0];
    public Span<TAs> GetItems<TAs>(World.Sparse extra) where TAs : struct => MemoryMarshal.Cast<T, TAs>(GetItem((int)extra));

    public TSparse FindSparseAttr<TSparse>(World.Sparse attrId, int elemId)
    {
        // vconst byte*  valueArray = (byte*) table.GetItem( attrId );
        // int count = valueArray[0];
        // int elemSize = valueArray[1];
        // const byte* elem = &valueArray[2];
        //
        // for (int i = 0; i<count; i++, elem += elemSize )
        // {
        //     if ( *elem == elemId )
        //         return elem;
        // }
        //
        // return nullptr;
        throw new NotImplementedException();
    }
}
internal class ListResource<T> : ResourceLoader
    where T : struct
{
    public T this[int i]
    {
        get => _backing[i];
        set => _backing[i] = value;
    }

    public int Length => _backing.Length;

    private readonly T[] _backing;

    public ListResource(T[] backing)
    {
        _backing = backing;
    }

    public static ListResource<T> Load(string file)
    {
        file = Assets.Root.GetPath("out", file);
        Span<byte> bytes = File.ReadAllBytes(file);
        var length = BitConverter.ToInt16(bytes);
        bytes = bytes[sizeof(short)..];
        if (bytes.Length != length) throw new InvalidOperationException();
        return new ListResource<T>(MemoryMarshal.Cast<byte, T>(bytes).ToArray());
    }

    public static T[] LoadList(string file, int amount)
    {
        file = Assets.Root.GetPath("out", file);
        Span<byte> bytes = File.ReadAllBytes(file);
        return MemoryMarshal.Cast<byte, T>(bytes[..amount]).ToArray();
    }

    public static T LoadSingle(string file) => LoadList(file, 1)[0];
}

internal unsafe struct FixedString
{
    public fixed byte Str[32];

    public bool IsNull => Str[0] == 0;

    public static implicit operator string(FixedString b) => Encoding.UTF8.GetString(b.Str, 32);
}

internal struct LevelDirectory
{
    public FixedString LevelInfoBlock;
    public FixedString RoomCols;
    public FixedString ColTables;
    public FixedString TileAttrs;
    public FixedString TilesImage;
    public FixedString PlayerImage;
    public FixedString PlayerSheet;
    public FixedString NpcImage;
    public FixedString NpcSheet;
    public FixedString BossImage;
    public FixedString BossSheet;
    public FixedString RoomAttrs;
    public FixedString LevelInfoEx;
    public FixedString ObjLists;
    public FixedString Extra1;
    public FixedString Extra2;
    public FixedString Extra3;
    public FixedString Extra4;
}

internal unsafe struct RoomCols
{
    public fixed byte ColumnDesc[World.MobColumns];
}

internal struct HPAttr
{
    public byte Data;

    public int GetHP(int type)
    {
        return (type & 1) switch {
            0 => Data & 0xF0,
            _ => Data << 4
        };
    }
}

internal unsafe struct TileMap
{
    public const int Size = World.Rows * World.Columns;

    public fixed byte tileRefs[World.Rows * World.Columns];
    public fixed byte tileBehaviors[World.Rows * World.Columns];

    public ref byte Refs(int row, int col)
    {
        return ref tileRefs[row * World.Columns + col];
    }

    public ref byte Behaviors(int row, int col)
    {
        return ref tileBehaviors[row * World.Columns + col];
    }

    public TileBehavior AsBehaviors(int row, int col)
    {
        return (TileBehavior)tileBehaviors[row * World.Columns + col];
    }
}

internal class SparsePos
{
    public byte roomId;
    public byte pos;
}

internal class SparsePos2
{
    public byte roomId;
    public byte x;
    public byte y;
}

internal unsafe struct LevelInfoBlock
{
    public const int LevelPaletteCount = 8;
    public const int LevelShortcutCount = 4;
    public const int LevelCellarCount = 10;
    public const int FadeLength = 4;
    public const int FadePals = 2;
    public const int MapLength = 16;
    public const int PaletteCount = 8;
    public const int PaletteLength = 4;
    public const int ForegroundPalCount = 4;
    public const int BackgroundPalCount = 4;

    public ReadOnlySpan<byte> GetPalette(int index)
    {
        fixed (byte* p = Palettes)
        {
            return new ReadOnlySpan<byte>(p + index * PaletteLength, PaletteLength);
        }
    }

    public ReadOnlySpan<byte> OutOfCellarPalette(int index, int fade)
    {
        throw new NotImplementedException();
    }

    public ReadOnlySpan<byte> InCellarPalette(int index, int fade)
    {
        throw new NotImplementedException();
    }

    public ReadOnlySpan<byte> DarkPalette(int index, int fade)
    {
        throw new NotImplementedException();
    }

    public ReadOnlySpan<byte> DeathPalette(int index, int fade)
    {
        throw new NotImplementedException();
    }

    public byte[][] DeathPalettes(int index)
    {
        throw new NotImplementedException();
    }

    public fixed byte Palettes[LevelPaletteCount * PaletteLength];
    public byte StartY;
    public byte StartRoomId;
    public byte TriforceRoomId;
    public byte BossRoomId;
    public SongId Song;
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
}

internal struct ObjectAttr
{
    [Flags]
    public enum Type
    {
        CustomCollision = 1,
        CustomDraw = 4,
        Unknown10__ = 0x10,
        InvincibleToWeapons = 0x20,
        HalfWidth = 0x40,
        Unknown80__ = 0x80,
        WorldCollision = 0x100,
    }

    public short Data;

    public Type Typed => (Type)Data;

    public bool GetCustomCollision() => Typed.HasFlag(Type.CustomCollision);
    public bool GetUnknown10__() => Typed.HasFlag(Type.Unknown10__);
    public bool GetInvincibleToWeapons() => Typed.HasFlag(Type.InvincibleToWeapons);
    public bool GetHalfWidth() => Typed.HasFlag(Type.HalfWidth);
    public bool GetUnknown80__() => Typed.HasFlag(Type.Unknown80__);
    public bool GetWorldCollision() => Typed.HasFlag(Type.WorldCollision);
    public int GetItemDropClass() => (Data >> 9) & 7;
}

internal sealed unsafe partial class World
{
    const int LevelGroups = 3;

    internal enum Cave
    {
        Items = 0x79,
        Shortcut = 0x7A,
    }

    internal enum Secret
    {
        None,
        FoesDoor,
        Ringleader,
        LastBoss,
        BlockDoor,
        BlockStairs,
        MoneyOrLife,
        FoesItem
    }

    internal enum TileScheme
    {
        Overworld,
        UnderworldMain,
        UnderworldCellar,
    };

    struct SparseRoomAttr
    {
        public byte roomId;
        public RoomAttrs attrs;

        public OWRoomAttrs OWRoomAttrs => attrs;
    }

    class SparseMaze
    {
        public byte roomId;
        public Direction exitDir;
        public Direction[] path = new Direction[4];
    }

    class SparseRoomItem
    {
        public byte roomId;
        public byte x;
        public byte y;
        public byte itemId;

        public ItemId AsItemId => (ItemId)itemId;
    }

    public enum Sparse
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

    public enum Extra
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

    unsafe struct ColorSeq
    {
        public int Length;
        public fixed byte Colors[1];
    }

    unsafe struct SpotSeq
    {
        public int Length;
        public fixed byte Spots[1];

        public Span<byte> GetSpots()
        {
            fixed (byte* p = Spots)
            {
                return new Span<byte>(p, Length); // Is this right?
            }
        }
    }

    unsafe struct PaletteSet
    {
        public int Length;
        public int Start;
        public fixed byte PaletteA[LevelInfoBlock.PaletteLength];
        public fixed byte PaletteB[LevelInfoBlock.PaletteLength];

        public ReadOnlySpan<byte> GetPalette(int i)
        {
            fixed (byte* p = PaletteA)
            fixed (byte* p2 = PaletteB)
            {
                return new ReadOnlySpan<byte>(i == 0 ? p : p2, LevelInfoBlock.PaletteLength);
            }
        }
    }

    unsafe struct CaveSpecList
    {
        public int Count;
        public CaveSpec SpecA;
        public CaveSpec SpecB;

        public ReadOnlySpan<CaveSpec> Specs => new[] { SpecA, SpecB };
    };

    unsafe struct LevelPersonStrings
    {
        public fixed byte StringIds[LevelGroups * (int)ObjType.PersonTypes];

        public ReadOnlySpan<byte> GetStringIds(int levelTableIndex)
        {
            fixed (byte* p = StringIds)
            {
                return new ReadOnlySpan<byte>(p + levelTableIndex * (int)ObjType.PersonTypes, (int)ObjType.PersonTypes);
            }
        }
    }

    public const int MobRows = 11;
    public const int MobColumns = 16;
    public const int Rows = 22;
    public const int Columns = 32;
    public const int BaseRows = 8;
    public const int MobTileWidth = 16;
    public const int MobTileHeight = 16;
    public const int TileWidth = 8;
    public const int TileHeight = 8;
    public const int TileMapWidth = Columns * TileWidth;
    public const int TileMapHeight = Rows * TileHeight;
    public const int TileMapBaseY = 64;
    public const int Doors = 4;

    public const int WorldLimitLeft = 0;
    public const int WorldLimitRight = TileMapWidth;
    public const int WorldLimitTop = TileMapBaseY;
    public const int WorldLimitBottom = WorldLimitTop + TileMapHeight;

    public const int WorldMidX = WorldLimitLeft + TileMapWidth / 2;

    public const int WorldWidth = 16;
    public const int WorldHeight = 8;

    public const int StartX = 0x78;
    public const int FirstCaveIndex = 0x10;
    public const int TriforcePieceX = 0x78;

    private const int Rooms = 128;
    private const int UniqueRooms = 124;
    private const int ColumnTables = 16;
    private const int ScrollSpeed = 4;
    private const int MobTypes = 56;
    private const int TileTypes = 256;
    private const int TileActions = 16;
    private const int LoadingTileActions = 4;
    private const int SparseAttrs = 11;
    private const int RoomHistoryLength = 6;
    private const int Modes = (int)GameMode.Max;

    private const int OWMarginRight = 0xE0;
    private const int OWMarginLeft = 0x10;
    private const int OWMarginTop = 0x4D;
    private const int OWMarginBottom = 0xCD;

    private const int UWMarginRight = 0xD0;
    private const int UWMarginLeft = 0x20;
    private const int UWMarginTop = 0x5D;
    private const int UWMarginBottom = 0xBD;

    private const int UWBorderRight = 0xE0;
    private const int UWBorderLeft = 0x20;
    private const int UWBorderTop = 0x60;
    private const int UWBorderBottom = 0xD0;

    private const int UWBlockRow = 10;

    private const int DoorWidth = 32;
    private const int DoorHeight = 32;
    private const int DoorOverlayBaseY = 128;
    private const int DoorUnderlayBaseY = 0;

    private const int DoorHoleCoordH = 0x90;
    private const int DoorHoleCoordV = 0x78;

    private const int UWBombRadius = 32;

    public delegate void LoadMobFunc(TileMap map, int row, int col, int mobIndex);

    public LevelDirectory directory;
    public LevelInfoBlock infoBlock;
    public RoomCols[] roomCols = new RoomCols[UniqueRooms];
    public TableResource<byte> colTables;
    public TileMap[] tileMaps = new TileMap[2];
    public RoomAttrs[] roomAttrs = new RoomAttrs[Rooms];
    public int curRoomId;
    public int curTileMapIndex;
    public byte[] tileAttrs = new byte[MobTypes];
    public byte[] tileBehaviors = new byte[TileTypes];
    public TableResource<byte> sparseRoomAttrs;
    public TableResource<byte> extraData;
    public TableResource<byte> objLists;
    public TableResource<byte> textTable;
    public ListResource<byte> primaryMobs;
    public ListResource<byte> secondaryMobs;
    public LoadMobFunc loadMobFunc;

    public int rowCount;
    public int colCount;
    public int startRow;
    public int startCol;
    public int tileTypeCount;
    public int marginRight;
    public int marginLeft;
    public int marginBottom;
    public int marginTop;
    public SKBitmap? wallsBmp;
    public SKBitmap? doorsBmp;

    public GameMode lastMode;
    public GameMode curMode;
    public StatusBar statusBar;
    public SubmenuType menu;
    public Credits credits;
    public TextBox textBox1;
    public TextBox textBox2;
    public Menu gameMenu;
    public Menu nextGameMenu;

    public WorldState State;
    public int CurColorSeqNum;
    public int DarkRoomFadeStep;
    public int CurMazeStep;
    public int SpotIndex;
    public int TempShutterRoomId;
    public Direction TempShutterDoorDir;
    public int TempShuttersRoomId;
    public bool TempShutters;
    public bool PrevRoomWasCellar;
    public int SavedOWRoomId;
    public int EdgeX;
    public int EdgeY;
    public int NextRoomHistorySlot;    // 620
    public int RoomObjCount;           // 34E
    //public int RoomObjId;              // 35F
    public Actor? RoomObj;              // 35F
    public byte WorldKillCycle;         // 52A
    public byte WorldKillCount;         // 627
    public byte HelpDropCounter;        // 50
    public byte HelpDropValue;          // 51
    public int RoomKillCount;          // 34F
    public bool RoomAllDead;            // 34D
    public bool MadeRoomItem;
    public bool EnablePersonFireballs;
    public bool SwordBlocked;           // 52E
    public byte WhirlwindTeleporting;   // 522
    public byte TeleportingRoomIndex;   // 523
    public byte Pause;                  // E0
    public byte Submenu;                // E1
    public int SubmenuOffsetY;         // EC
    public bool StatusBarVisible;
    public int[] LevelKillCounts = new int[(int)LevelBlock.Rooms];
    public byte[] RoomHistory = new byte[RoomHistoryLength];

    public Link Player => Game.Link;
    public bool giveFakePlayerPos;
    public int playerPosTimer;
    public Point fakePlayerPos;

    public Actor?[] objects = new Actor[(int)ObjectSlot.MaxObjects];
    public Actor?[] objectsToDelete = new Actor[(int)ObjectSlot.MaxObjects];
    public int objectsToDeleteCount;
    public int[] objectTimers = new int[(int)ObjectSlot.MaxObjects];
    public int curObjSlot;
    public ObjectSlot curObjectSlot
    {
        get => (ObjectSlot)curObjectSlot;
        set => curObjSlot = (int)value;
    }
    public int longTimer;
    public int[] stunTimers = new int[(int)ObjectSlot.MaxObjects];
    public byte[] placeholderTypes = new byte[(int)ObjectSlot.MaxObjects];

    public Direction doorwayDir;         // 53
    public int triggeredDoorCmd;   // 54
    public Direction triggeredDoorDir;   // 55
    public int fromUnderground;    // 5A
    public int activeShots;        // 34C
    public bool triggerShutters;    // 4CE
    public bool summonedWhirlwind;  // 508
    public bool powerTriforceFanfare;   // 509
    public int recorderUsed;       // 51B
    public bool candleUsed;         // 513
    public Direction shuttersPassedDirs; // 519
    public bool brightenRoom;       // 51E
    public int profileSlot;
    public PlayerProfile profile;
    public UWRoomFlags[] curUWBlockFlags = new UWRoomFlags[] { };
    public int ghostCount;
    public int armosCount;
    public Cell[] ghostCells = Cell.MakeMobPatchCell();
    public Cell[] armosCells = Cell.MakeMobPatchCell();

    private UWRoomAttrs CurrentUWRoomAttrs => roomAttrs[curRoomId];
    private OWRoomAttrs CurrentOWRoomAttrs => roomAttrs[curRoomId];

    public Game Game;
    public World(Game game)
    {
        Game = game;
    }

    void LoadOpenRoomContext()
    {
        colCount = 32;
        rowCount = 22;
        startRow = 0;
        startCol = 0;
        tileTypeCount = 56;
        marginRight = OWMarginRight;
        marginLeft = OWMarginLeft;
        marginBottom = OWMarginBottom;
        marginTop = OWMarginTop;
    }

    void LoadClosedRoomContext()
    {
        colCount = 24;
        rowCount = 14;
        startRow = 4;
        startCol = 4;
        tileTypeCount = 9;
        marginRight = UWMarginRight;
        marginLeft = UWMarginLeft;
        marginBottom = UWMarginBottom;
        marginTop = UWMarginTop;
    }

    void LoadMapResourcesFromDirectory(int uniqueRoomCount)
    {
        roomCols = ListResource<RoomCols>.LoadList(directory.RoomCols, uniqueRoomCount);
        colTables = TableResource<byte>.Load(directory.ColTables);
        tileAttrs = ListResource<byte>.LoadList(directory.TileAttrs, TileTypes);

        Graphics.LoadTileSheet(TileSheet.Background, directory.TilesImage);
    }

    void LoadOverworldContext()
    {
        LoadOpenRoomContext();
        LoadMapResourcesFromDirectory(124);
        primaryMobs = ListResource<byte>.Load("owPrimaryMobs.list");
        secondaryMobs = ListResource<byte>.Load("owSecondaryMobs.list");
        tileBehaviors = ListResource<byte>.LoadList("owTileBehaviors.dat", TileTypes);
    }

    void LoadUnderworldContext()
    {
        LoadClosedRoomContext();
        LoadMapResourcesFromDirectory(64);
        primaryMobs = ListResource<byte>.Load("uwPrimaryMobs.list");
        tileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes);
    }

    void LoadCellarContext()
    {
        LoadOpenRoomContext();

        roomCols = ListResource<RoomCols>.LoadList("underworldCellarRoomCols.dat", 2);
        colTables = TableResource<byte>.Load("underworldCellarCols.tab");

        tileAttrs = ListResource<byte>.LoadList("underworldCellarTileAttrs.dat", tileTypeCount);

        Graphics.LoadTileSheet(TileSheet.Background, "underworldTiles.png");

        primaryMobs = ListResource<byte>.Load("uwCellarPrimaryMobs.list");
        secondaryMobs = ListResource<byte>.Load("uwCellarSecondaryMobs.list");
        tileBehaviors = ListResource<byte>.LoadList("uwTileBehaviors.dat", TileTypes);
    }

    void LoadLevel(int level)
    {
        var levelDirName = $"levelDir_{profile.Quest}_{level}.dat";

        directory = ListResource<LevelDirectory>.LoadSingle(levelDirName);
        infoBlock = ListResource<LevelInfoBlock>.LoadSingle(directory.LevelInfoBlock);

        wallsBmp?.Dispose();
        wallsBmp = null;
        doorsBmp?.Dispose();
        doorsBmp = null;

        TempShutterRoomId = 0;
        TempShutterDoorDir = 0;
        TempShuttersRoomId = 0;
        TempShutters = false;
        PrevRoomWasCellar = false;
        DarkRoomFadeStep = 0;
        Array.Clear(LevelKillCounts);
        Array.Clear(RoomHistory);
        WhirlwindTeleporting = 0;

        if (level == 0)
        {
            LoadOverworldContext();
            curUWBlockFlags = null;
        }
        else
        {
            LoadUnderworldContext();
            wallsBmp = SKBitmap.Decode(directory.Extra2);
            doorsBmp = SKBitmap.Decode(directory.Extra3);
            if (level < 7)
                curUWBlockFlags = profile.LevelFlags1;
            else
                curUWBlockFlags = profile.LevelFlags2;

            for (int i = 0; i < tileMaps.Length; i++)
            {
                for (var x = 0; x < TileMap.Size; x++)
                {
                    tileMaps[i].tileRefs[x] = (byte)BlockObjType.Tile_WallEdge;
                }
            }
        }

        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, directory.PlayerImage, directory.PlayerSheet);
        Graphics.LoadTileSheet(TileSheet.Npcs, directory.NpcImage, directory.NpcSheet);

        if (!directory.BossImage.IsNull)
        {
            Graphics.LoadTileSheet(TileSheet.Boss, directory.BossImage, directory.BossSheet);
        }

        roomAttrs = ListResource<RoomAttrs>.LoadList(directory.RoomAttrs, Rooms);
        extraData = TableResource<byte>.Load(directory.LevelInfoEx);
        objLists = TableResource<byte>.Load(directory.ObjLists);
        sparseRoomAttrs = TableResource<byte>.Load(directory.Extra1);

        var facing = Game.Link?.Facing ?? Direction.Up;

        Game.Link = new(Game);
        Game.Link.Facing = facing;

        // Replace room attributes, if in second quest.

        if (level == 0 && profile.Quest == 1)
        {
            var pReplacement = sparseRoomAttrs.GetItems<byte>(Sparse.RoomReplacement);
            int replacementCount = pReplacement[0];
            var sparseAttr = MemoryMarshal.Cast<byte, SparseRoomAttr>(pReplacement[2..]); // JOE: Go until replacementCount * sizeof(SparseRoomAttr) ??

            for (int i = 0; i < replacementCount; i++)
            {
                int roomId = sparseAttr[i].roomId;
                roomAttrs[roomId] = sparseAttr[i].attrs;
            }
        }
    }

    void Init()
    {
        var sysPal = ListResource<int>.LoadList("pal.dat", Global.SysPaletteLength);
        Graphics.LoadSystemPalette(sysPal);

        Graphics.LoadTileSheet(TileSheet.Font, "font.png");
        Graphics.LoadTileSheet(TileSheet.PlayerAndItems, "playerItem.png", "playerItemsSheet.tab");

        textTable = TableResource<byte>.Load("text.tab");

        GotoFileMenu();
    }

    void Start(int slot, PlayerProfile profile)
    {
        this.profile = profile;
        this.profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);
        profileSlot = slot;

        GotoLoadLevel(0, true);
    }

    void Update()
    {
        GameMode mode = GetMode();

        if (lastMode != mode)
        {
            if (IsPlaying(lastMode) && mode != GameMode.WinGame)
            {
                CleanUpRoomItems();
                Graphics.DisableGrayscale();
                if (mode != GameMode.Unfurl)
                {
                    OnLeavePlay();
                    Game.Link?.Stop();
                }
            }

            lastMode = mode;

            gameMenu = nextGameMenu;
            nextGameMenu = null;
        }

        sModeFuncs[(int)curMode]();
    }

    void Draw()
    {
        if (StatusBarVisible)
            statusBar.Draw(SubmenuOffsetY);

        sDrawFuncs[(int)curMode]();
    }

    void DrawRoom()
    {
        DrawMap(curRoomId, curTileMapIndex, 0, 0);
    }

    void PauseFillHearts()
    {
        Pause = 2;
    }

    public void LeaveRoom(Direction dir, int roomId)
    {
        GotoLeave(dir, roomId);
    }

    void LeaveCellar()
    {
        GotoLeaveCellar();
    }

    void LeaveCellarByShortcut(int targetRoomId)
    {
        curRoomId = targetRoomId;
        TakeShortcut();
        LeaveCellar();
    }

    void Die()
    {
        GotoDie();
    }

    void UnfurlLevel()
    {
        GotoUnfurl();
    }

    void ChooseFile(ProfileSummarySnapshot summaries )
    {
        GotoFileMenu(summaries);
    }

    void RegisterFile(ProfileSummarySnapshot summaries )
    {
        GotoRegisterMenu(summaries);
    }

    void EliminateFile(ProfileSummarySnapshot summaries )
    {
        GotoEliminateMenu(summaries);
    }

    bool IsPlaying()
    {
        return IsPlaying(curMode);
    }

    bool IsPlaying(GameMode mode)
    {
        return mode is GameMode.Play or GameMode.PlayCave or GameMode.PlayCellar or GameMode.PlayShortcuts;
    }

    bool IsPlayingCave()
    {
        return GetMode() == GameMode.PlayCave;
    }

    GameMode GetMode()
    {
        if (curMode == GameMode.InitPlayCave)
            return GameMode.PlayCave;
        if (curMode == GameMode.InitPlayCellar)
            return GameMode.PlayCellar;
        return curMode;
    }

    Point GetObservedPlayerPos()
    {
        return fakePlayerPos;
    }

    LadderActor GetLadder()
    {
        return GetLadderObj();
    }

    void SetLadder(LadderActor ladder)
    {
        SetLadderObj(ladder);
    }

    void UseRecorder()
    {
        Game.Sound.PushSong(SongId.Recorder);
        objectTimers[(int)ObjectSlot.FluteMusic] = 0x98;

        if (IsOverworld())
        {
            if (IsPlaying() && State.Play.roomType == PlayState.RoomType.Regular)
            {
                static ReadOnlySpan<byte> roomIds() => new byte[] { 0x42, 0x06, 0x29, 0x2B, 0x30, 0x3A, 0x3C, 0x58, 0x60, 0x6E, 0x72 };

                bool makeWhirlwind = true;

                for (int i = 0; i < roomIds().Length; i++)
                {
                    if (roomIds()[i] == curRoomId)
                    {
                        if ((i == 0 && profile.Quest == 0)
                            || (i != 0 && profile.Quest != 0))
                            makeWhirlwind = false;
                        break;
                    }
                }

                if (makeWhirlwind)
                    SummonWhirlwind();
                else
                    MakeFluteSecret();
            }
        }
        else
        {
            recorderUsed = 1;
        }
    }

    void SummonWhirlwind()
    {
        if (!summonedWhirlwind
            && WhirlwindTeleporting == 0
            && IsOverworld()
            && IsPlaying()
            && State.Play.roomType == PlayState.RoomType.Regular
            && GetItem(ItemSlot.TriforcePieces) != 0)
        {
            var slot = FindEmptyMonsterSlot();
            if (slot != ObjectSlot.NoneFound)
            {
                static ReadOnlySpan<byte> TeleportRoomIds() => new byte[] { 0x36, 0x3B, 0x73, 0x44, 0x0A, 0x21, 0x41, 0x6C };

                var whirlwind = new WhirlwindActor(Game, 0, Game.Link.Y);
                SetObject(slot, whirlwind);

                summonedWhirlwind = true;
                TeleportingRoomIndex = GetNextTeleportingRoomIndex();
                whirlwind.SetTeleportPrevRoomId(TeleportRoomIds()[TeleportingRoomIndex]);
            }
        }
    }

    void MakeFluteSecret()
    {
        // TODO:
        // The original game makes a FluteSecret object (type $5E) and puts it in one of the first 9
        // object slots that it finds going from higher to lower slots. The FluteSecret object manages
        // the animation. See $EFA4, and the FluteSecret's update routine at $FEF4.
        // But, for now we'll keep doing it as we have been.

        if (!State.Play.uncoveredRecorderSecret && FindSparseFlag(Sparse.Recorder, curRoomId))
        {
            State.Play.uncoveredRecorderSecret = true;
            State.Play.animatingRoomColors = true;
            State.Play.timer = 88;
        }
    }

    TileBehavior GetTileBehavior(int row, int col)
    {
        return (TileBehavior)tileMaps[curTileMapIndex].tileBehaviors[row * col];
    }

    TileBehavior GetTileBehaviorXY(int x, int y)
    {
        int col = x / TileWidth;
        int row = (y - TileMapBaseY) / TileHeight;

        return GetTileBehavior(row, col);
    }

    public void SetMobXY(int x, int y, BlockObjType mobIndex)
    {
        int fineCol = x / TileWidth;
        int fineRow = (y - TileMapBaseY) / TileHeight;

        if (fineCol is < 0 or >= Columns || fineRow is < 0 or >= Rows)
            return;

        SetMob(fineRow, fineCol, mobIndex);
    }

    void SetMob(int row, int col, BlockObjType mobIndex)
    {
        loadMobFunc(tileMaps[curTileMapIndex], row, col, (byte)mobIndex); // FIX CALL SITE TO BE BlockObjTypes

        for (int r = row; r < row + 2; r++)
        {
            for (int c = col; c < col + 2; c++)
            {
                byte t = tileMaps[curTileMapIndex].tileRefs[r * c];
                tileMaps[curTileMapIndex].tileBehaviors[r * c] = tileBehaviors[t];
            }
        }

        // TODO: Will we need to run some function to initialize the map object, like in LoadLayout?
    }

    Palette GetInnerPalette()
    {
        return roomAttrs[curRoomId].GetInnerPalette();
    }

    Cell GetRandomWaterTile()
    {
        var waterList = new Cell[Rows * Columns];
        int waterCount = 0;

        for (int r = 0; r < Rows - 1; r++)
        {
            for (int c = 0; c < Columns - 1; c++)
            {
                if (GetTileBehavior(r, c) == TileBehavior.Water
                    && GetTileBehavior(r, c + 1) == TileBehavior.Water
                    && GetTileBehavior(r + 1, c) == TileBehavior.Water
                    && GetTileBehavior(r + 1, c + 1) == TileBehavior.Water)
                {
                    waterList[waterCount] = new Cell((byte)r, (byte)c);
                    waterCount++;
                }
            }
        }

        if (waterCount <= 0) throw new Exception();

        int waterRandom = Random.Shared.Next(0, waterCount);
        var cell = waterList[waterRandom];
        return new((byte)(cell.Row + BaseRows), cell.Col);
    }

    Actor? GetObject(ObjectSlot slot)
    {
        if (slot == ObjectSlot.Player)
            return Game.Link;

        return objects[(int)slot];
    }

    void SetObject(ObjectSlot slot, Actor obj)
    {
        SetOnlyObject(slot, obj);
    }

    int FindEmptyFireSlot()
    {
        for (int i = (int)ObjectSlot.FirstFire; i < (int)ObjectSlot.LastFire; i++)
        {
            if (objects[i] == null)
                return i;
        }
        return -1;
    }

    ref int GetObjectTimer(ObjectSlot slot)
    {
        return ref objectTimers[(int)slot];
    }


    void PushTile(int row, int col) => InteractTile(row, col, TileInteraction.Push);
    void TouchTile(int row, int col) => InteractTile(row, col, TileInteraction.Touch);
    void CoverTile(int row, int col) => InteractTile(row, col, TileInteraction.Cover);

    void InteractTile(int row, int col, TileInteraction interaction)
    {
        if (row < 0 || col < 0 || row >= Rows || col >= Columns) return;

        var behavior = GetTileBehavior(row, col);
        var behaviorFunc = sBehaviorFuncs[(int)behavior];
        behaviorFunc(row, col, interaction);
    }

    static bool CollidesWall(TileBehavior behavior) => behavior is TileBehavior.Wall or TileBehavior.Doorway or TileBehavior.Door;
    static bool CollidesTile(TileBehavior behavior) => behavior >= TileBehavior.FirstSolid;

    TileCollision CollidesWithTileStill(int x, int y)
    {
        return CollidesWithTile(x, y, Direction.None, 0);
    }

    TileCollision CollidesWithTileMoving(int x, int y, Direction dir, bool isPlayer)
    {
        int offset;

        if (dir == Direction.Right)
            offset = 0x10;
        else if (dir == Direction.Down)
            offset = 8;
        else if (isPlayer)
            offset = -8;
        else
            offset = -0x10;

        var collision = CollidesWithTile(x, y, dir, offset);

        // Special tile collision in top right corner of OW. Go thru the wall.
        if (isPlayer
            && infoBlock.LevelNumber == 0
            && curRoomId == 0x1F
            && dir.IsVertical()
            && x == 0x80
            && y < 0x56)
        {
            collision.Collides = false;
        }

        return collision;
    }

    TileCollision CollidesWithTile(
        int x, int y, Direction dir, int offset)
    {
        y += 0xB;

        if (dir.IsVertical())
        {
            if (dir == Direction.Up || y < 0xDD)
                y += offset;
        }
        else
        {
            if ((dir == Direction.Left && x >= 0x10) || (dir == Direction.Right && x < 0xF0))
                x += offset;
        }

        var behavior = TileBehavior.FirstWalkable;
        byte fineRow = (byte)((y - TileMapBaseY) / 8);
        byte fineCol1 = (byte)(x / 8);
        byte fineCol2;
        byte hitFineCol = fineCol1;

        if (dir.IsVertical())
            fineCol2 = (byte)((x + 8) / 8);
        else
            fineCol2 = fineCol1;

        for (byte c = fineCol1; c <= fineCol2; c++)
        {
            TileBehavior curBehavior = GetTileBehavior(fineRow, c);

            if (curBehavior == TileBehavior.Water && State.Play.allowWalkOnWater)
                curBehavior = TileBehavior.GenericWalkable;

            if (curBehavior > behavior)
            {
                behavior = curBehavior;
                hitFineCol = c;
            }
        }

        return new(CollidesTile(behavior), behavior, hitFineCol, fineRow);
    }

    TileCollision PlayerCoversTile(int x, int y)
    {
        y += 3;

        TileBehavior behavior = TileBehavior.FirstWalkable;
        byte fineRow1 = (byte)((y - TileMapBaseY) / 8);
        byte fineRow2 = (byte)((y + 15 - TileMapBaseY) / 8);
        byte fineCol1 = (byte)(x / 8);
        byte fineCol2 = (byte)((x + 15) / 8);
        byte hitFineCol = fineCol1;
        byte hitFineRow = fineRow1;

        for (byte r = fineRow1; r <= fineRow2; r++)
        {
            for (byte c = fineCol1; c <= fineCol2; c++)
            {
                TileBehavior curBehavior = GetTileBehavior(r, c);

                if (curBehavior == TileBehavior.Water && State.Play.allowWalkOnWater)
                    curBehavior = TileBehavior.GenericWalkable;

                // TODO: this isn't the best way to check covered tiles
                //       but it'll do for now.
                if (curBehavior > behavior)
                {
                    behavior = curBehavior;
                    hitFineCol = c;
                    hitFineRow = r;
                }
            }
        }

        return new(false, behavior, hitFineCol, hitFineRow);
    }

    public void OnPushedBlock()
    {
        Game.Sound.Play(SoundEffect.Secret);

        if (IsOverworld())
        {
            if (!GotShortcut(curRoomId))
            {
                if (FindSparseFlag(Sparse.Shortcut, curRoomId))
                {
                    TakeShortcut();
                    ShowShortcutStairs(curRoomId, curTileMapIndex);
                }
            }
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            var secret = uwRoomAttrs.GetSecret();

            if (secret == Secret.BlockDoor)
            {
                triggerShutters = true;
            }
            else if (secret == Secret.BlockStairs)
            {
                AddUWRoomStairs();
            }
        }
    }

    void OnActivatedArmos(int x, int y)
    {
        var pos = FindSparsePos2(Sparse.ArmosStairs, curRoomId);

        if (pos != null && x == pos.x && y == pos.y)
        {
            SetMobXY(x, y, BlockObjType.Mob_Stairs);
            Game.Sound.Play(SoundEffect.Secret);
        }
        else
        {
            SetMobXY(x, y, BlockObjType.Mob_Ground);
        }

        if (!GotItem())
        {
            var roomItem = FindSparseItem(Sparse.ArmosItem, curRoomId);

            if (roomItem != null && x == roomItem.x && y == roomItem.y)
            {
                var itemObj = Actor.FromType((ObjType)roomItem.itemId, Game, roomItem.x, roomItem.y, true);
                objects[(int)ObjectSlot.Item] = itemObj;
            }
        }
    }

    private static Span<byte> _onTouchedPowerTriforcePalette => new byte[] { 0, 0x0F, 0x10, 0x30 };

    void OnTouchedPowerTriforce()
    {
        powerTriforceFanfare = true;
        Game.Link.SetState(PlayerState.Paused);
        Game.Link.ObjTimer = 0xC0;

        Graphics.SetPaletteIndexed(Palette.LevelForeground, _onTouchedPowerTriforcePalette);
        Graphics.UpdatePalettes();
    }

    void CheckPowerTriforceFanfare()
    {
        if (!powerTriforceFanfare)
            return;

        if (Game.Link.ObjTimer == 0)
        {
            powerTriforceFanfare = false;
            Game.Link.SetState(PlayerState.Idle);
            AddItem(ItemId.PowerTriforce);
            GlobalFunctions.SetPilePalette();
            Graphics.UpdatePalettes();
            Game.Sound.PlaySong(SongId.Level9, SongStream.MainSong, true);
        }
        else
        {
            var timer = Game.Link.ObjTimer;
            if ((timer & 4) > 0)
                SetFlashPalette();
            else
                SetLevelPalette();
        }
    }

    void AdjustInventory()
    {
        if (profile.SelectedItem == 0)
            profile.SelectedItem = ItemSlot.Boomerang;

        for (int i = 0; i < 10; i++)
        {
            if (profile.SelectedItem is ItemSlot.Arrow or ItemSlot.Bow)
            {
                if (profile.Items[ItemSlot.Arrow] != 0
                    && profile.Items[ItemSlot.Bow] != 0)
                    break;
            }
            else
            {
                if (profile.Items[profile.SelectedItem] != 0)
                    break;
            }

            switch ((int)profile.SelectedItem)
            {
                case 0x07: profile.SelectedItem = (ItemSlot)0x0F; break;
                case 0x0F: profile.SelectedItem = (ItemSlot)0x06; break;
                case 0x01: profile.SelectedItem = (ItemSlot)0x1B; break;
                case 0x1B: profile.SelectedItem = (ItemSlot)0x08; break;
                default: profile.SelectedItem--; break;
            }
        }
    }

    void WarnLowHPIfNeeded()
    {
        if (profile.Hearts >= 0x100)
            return;

        Game.Sound.Play(SoundEffect.LowHp);
    }

    void PlayAmbientSounds()
    {
        bool playedSound = false;

        if (IsOverworld())
        {
            if (GetMode() == GameMode.Play)
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                if (owRoomAttrs.HasAmbientSound())
                {
                    Game.Sound.PlayEffect(SoundEffect.Sea, true, Sound.AmbientInstance);
                    playedSound = true;
                }
            }
        }
        else
        {
            if (curUWBlockFlags[infoBlock.BossRoomId].GetObjCount() == 0)
            {
                var uwRoomAttrs = CurrentUWRoomAttrs;
                int ambientSound = uwRoomAttrs.GetAmbientSound();
                if (ambientSound != 0)
                {
                    int id = (int)SoundEffect.BossRoar1 + ambientSound - 1;
                    Game.Sound.PlayEffect((SoundEffect)id, true, Sound.AmbientInstance);
                    playedSound = true;
                }
            }
        }

        if (!playedSound)
            Game.Sound.StopEffects();
    }

    void ShowShortcutStairs(int roomId, int tileMapIndex)
    {
        var owRoomAttrs = CurrentOWRoomAttrs;
        int index = owRoomAttrs.GetShortcutStairsIndex();
        int pos = infoBlock.ShortcutPosition[index];
        GetRoomCoord(pos, out var row, out var col);
        SetMob(row * 2, col * 2, BlockObjType.Mob_Stairs);
    }

    void DrawMap(int roomId, int mapIndex, int offsetX, int offsetY)
    {
        Graphics.Begin();

        var outerPalette = roomAttrs[roomId].GetOuterPalette();
        var innerPalette = roomAttrs[roomId].GetInnerPalette();
        var map = tileMaps[mapIndex];

        if (IsUWCellar(roomId)  || IsPlayingCave())
        {
            outerPalette = (Palette)3;
            innerPalette = (Palette)2;
        }

        int firstRow = 0;
        int lastRow = Rows;
        int tileOffsetY = offsetY;

        int firstCol = 0;
        int lastCol = Columns;
        int tileOffsetX = offsetX;

        if (offsetY < 0)
        {
            firstRow = -offsetY / TileHeight;
            tileOffsetY = -(-offsetY % TileHeight);
        }
        else if (offsetY > 0)
        {
            lastRow = Rows - offsetY / TileHeight;
        }
        else if (offsetX < 0)
        {
            firstCol = -offsetX / TileWidth;
            tileOffsetX = -(-offsetX % TileWidth);
        }
        else if (offsetX > 0)
        {
            lastCol = Columns - offsetX / TileWidth;
        }

        int endCol = startCol + colCount;
        int endRow = startRow + rowCount;

        int y = TileMapBaseY + tileOffsetY;

        if (IsUWMain(roomId))
        {
            Graphics.DrawBitmap(
                wallsBmp,
                0, 0,
                TileMapWidth, TileMapHeight,
                offsetX, TileMapBaseY + offsetY,
                outerPalette, 0);
        }

        for (int r = firstRow; r < lastRow; r++, y += TileHeight)
        {
            if (r < startRow || r >= endRow)
                continue;

            int x = tileOffsetX;

            for (int c = firstCol; c < lastCol; c++, x += TileWidth)
            {
                if (c < startCol || c >= endCol)
                    continue;

                int tileRef = map.tileRefs[r * x];
                int srcX = (tileRef & 0x0F) * TileWidth;
                int srcY = ((tileRef & 0xF0) >> 4) * TileHeight;

                var palette = (r is < 4 or >= 18 || c is < 4 or >= 28)  ? outerPalette : innerPalette;

                Graphics.DrawTile(
                    TileSheet.Background,
                    srcX, srcY,
                    TileWidth, TileHeight,
                    x, y,
                    palette, 0);
            }
        }

        if (IsUWMain(roomId))
            DrawDoors(roomId, false, offsetX, offsetY);

        Graphics.End();
    }

    void DrawDoors(int roomId, bool above, int offsetX, int offsetY)
    {
        var outerPalette = roomAttrs[roomId].GetOuterPalette();
        int baseY = above ? DoorOverlayBaseY : DoorUnderlayBaseY;
        var uwRoomAttr = CurrentUWRoomAttrs;

        for (int i = 0; i < 4; i++)
        {
            var doorDir = i.GetOrdDirection();
            var doorType = uwRoomAttr.GetDoor(i);
            bool doorState = GetDoorState(roomId, doorDir);
            if (TempShutterDoorDir != 0 && roomId == TempShutterRoomId && doorType == DoorType.Shutter)
            {
                if (doorDir == TempShutterDoorDir)
                    doorState = true;
            }
            if (doorType == DoorType.Shutter && TempShutters && TempShutterRoomId == roomId)
                doorState = true;
            int doorFace = GetDoorStateFace(doorType, doorState);
            Graphics.DrawBitmap(
                doorsBmp,
                DoorWidth * doorFace,
                doorSrcYs[i] + baseY,
                DoorWidth,
                DoorHeight,
                doorPos[i].X + offsetX,
                doorPos[i].Y + offsetY,
                outerPalette,
                0);
        }
    }

    public PlayerProfile GetProfile()
    {
        return profile;
    }

    public int GetItem(ItemSlot itemSlot)
    {
        return profile.Items[itemSlot];
    }

    void SetItem(ItemSlot itemSlot, int value)
    {
        profile.Items[itemSlot] = value;
    }

    void PostRupeeChange(byte value, ItemSlot itemSlot)
    {
        var curValue = profile.Items[itemSlot];
        var newValue = curValue + value;

        if (newValue < curValue)
            newValue = 255;

        profile.Items[itemSlot] = newValue;
    }

    void PostRupeeWin(byte value) => PostRupeeChange(value, ItemSlot.RupeesToAdd);
    void PostRupeeLoss(byte value) => PostRupeeChange(value, ItemSlot.RupeesToSubtract);

    void FillHearts(int heartValue)
    {
        var maxHeartValue = profile.Items[ItemSlot.HeartContainers] << 8;

        profile.Hearts += heartValue;

        if (profile.Hearts >= maxHeartValue)
            profile.Hearts = maxHeartValue - 1;
    }

    void AddItem(ItemId itemId)
    {
        if ((int)itemId >= (int)ItemId.None)
            return;

        GlobalFunctions.PlayItemSound(Game, itemId);

        var equip = sItemToEquipment[(int)itemId];
        var slot = equip.Slot;
        var value = equip.Value;

        if (itemId is ItemId.Heart or ItemId.Fairy)
        {
            var heartValue = value << 8;
            FillHearts(heartValue);
        }
        else
        {
            if (slot == ItemSlot.Bombs)
            {
                value += (byte)profile.Items[ItemSlot.Bombs];
                if (value > profile.Items[ItemSlot.MaxBombs])
                    value = (byte)profile.Items[ItemSlot.MaxBombs];
            }
            else if (slot is ItemSlot.RupeesToAdd or ItemSlot.Keys or ItemSlot.HeartContainers)
            {
                value += (byte)profile.Items[slot];
                if (value > 255)
                    value = 255;
            }
            else if (itemId == ItemId.Compass)
            {
                if (infoBlock.LevelNumber < 9)
                {
                    var bit = 1 << (infoBlock.LevelNumber - 1);
                    value = (byte)(profile.Items[ItemSlot.Compass] | bit);
                    slot = ItemSlot.Compass;
                }
            }
            else if (itemId == ItemId.Map)
            {
                if (infoBlock.LevelNumber < 9)
                {
                    var bit = 1 << (infoBlock.LevelNumber - 1);
                    value = (byte)(profile.Items[ItemSlot.Map] | bit);
                    slot = ItemSlot.Map;
                }
            }
            else if (itemId == ItemId.TriforcePiece)
            {
                var bit = 1 << (infoBlock.LevelNumber - 1);
                value = (byte)(profile.Items[ItemSlot.TriforcePieces] | bit);
            }

            profile.Items[slot] = value;

            if (slot == ItemSlot.Ring)
            {
                SetPlayerColor();
                Graphics.UpdatePalettes();
            }
            if (slot == ItemSlot.HeartContainers)
                FillHearts(0x100);
        }
    }

    void DecrementItem(ItemSlot itemSlot)
    {
        var val = GetItem(itemSlot);
        if (val != 0)
            profile.Items[itemSlot] = val - 1;
    }

    bool HasCurrentMap() => HasCurrentLevelItem(ItemSlot.Map, ItemSlot.Map9);
    bool HasCurrentCompass() => HasCurrentLevelItem(ItemSlot.Compass, ItemSlot.Compass9);

    bool HasCurrentLevelItem(ItemSlot itemSlot1To8, ItemSlot itemSlot9)
    {
        if (infoBlock.LevelNumber == 0)
            return false;

        if (infoBlock.LevelNumber < 9)
        {
            int itemValue = profile.Items[itemSlot1To8];
            int bit = 1 << (infoBlock.LevelNumber - 1);
            return (itemValue & bit) != 0;
        }

        return profile.Items[itemSlot9] != 0;
    }

    DoorType GetDoorType(Direction dir)
    {
        return GetDoorType(curRoomId, dir);
    }

    DoorType GetDoorType(int roomId, Direction dir)
    {
        int dirOrd = dir.GetOrdinal();
        var uwRoomAttrs = CurrentUWRoomAttrs;
        return uwRoomAttrs.GetDoor(dirOrd);
    }

    bool GetEffectiveDoorState(int roomId, Direction doorDir)
    {
        // TODO: the original game does it a little different, by looking at $EE.
        return GetDoorState(roomId, doorDir)
            || (GetDoorType((Direction)doorDir) == DoorType.Shutter
                && TempShutters && roomId == TempShutterRoomId)
            || (TempShutterDoorDir == doorDir && roomId == TempShutterRoomId);
    }

    bool GetEffectiveDoorState(Direction doorDir)
    {
        return GetEffectiveDoorState(curRoomId, doorDir);
    }

    UWRoomFlags GetUWRoomFlags(int curRoomId)
    {
        return curUWBlockFlags[curRoomId];
    }

    LevelInfoBlock GetLevelInfo()
    {
        return infoBlock;
    }

    bool IsOverworld()
    {
        return infoBlock.LevelNumber == 0;
    }

    bool DoesRoomSupportLadder()
    {
        return FindSparseFlag(Sparse.Ladder, curRoomId);
    }

    TileAction GetTileAction(int tileRef) => TileAttr.GetAction(tileAttrs[tileRef]);

    bool IsUWMain(int roomId)
    {
        return !IsOverworld() && (roomAttrs[roomId].GetUniqueRoomId() < 0x3E);
    }

    bool IsUWCellar(int roomId)
    {
        return !IsOverworld() && (roomAttrs[roomId].GetUniqueRoomId() >= 0x3E);
    }

    bool IsUWCellar()
    {
        return IsUWCellar(curRoomId);
    }

    bool GotShortcut(int roomId)
    {
        return profile.OverworldFlags[roomId].GetShortcutState();
    }

    bool GotSecret()
    {
        return profile.OverworldFlags[curRoomId].GetSecretState();
    }

    ReadOnlySpan<byte> GetShortcutRooms()
    {
        var valueArray = sparseRoomAttrs.GetItems<byte>(Sparse.Shortcut);
        // elemSize is at 1, but we don't need it.
        return valueArray[2..valueArray[0]];
    }

    void TakeShortcut()
    {
        profile.OverworldFlags[curRoomId].SetShortcutState();
    }

    public void TakeSecret()
    {
        profile.OverworldFlags[curRoomId].SetSecretState();
    }

    bool GotItem() => GotItem(curRoomId);

    bool GotItem(int roomId)
    {
        if (IsOverworld())
        {
            return profile.OverworldFlags[roomId].GetItemState();
        }
        else
        {
            return curUWBlockFlags[roomId].GetItemState();
        }
    }

    void MarkItem()
    {
        if (IsOverworld())
        {
            profile.OverworldFlags[curRoomId].SetItemState();
        }
        else
        {
            curUWBlockFlags[curRoomId].SetItemState();
        }
    }

    void LiftItem(ItemId itemId, short timer)
    {
        if (!IsPlaying())
            return;

        if (itemId is ItemId.None or 0)
        {
            State.Play.liftItemTimer = 0;
            State.Play.liftItemId = 0;
            return;
        }

        State.Play.liftItemTimer = timer;
        State.Play.liftItemId = itemId;

        Game.Link.SetState(PlayerState.Paused);
    }

    bool IsLiftingItem()
    {
        if (!IsPlaying())
            return false;

        return State.Play.liftItemId != 0;
    }

    void OpenShutters()
    {
        TempShutters = true;
        TempShutterRoomId = curRoomId;
        Game.Sound.Play(SoundEffect.Door);

        for (int i = 0; i < Doors; i++)
        {
            Direction dir = i.GetOrdDirection();
            DoorType type = GetDoorType(dir);

            if (type == DoorType.Shutter)
                UpdateDoorTileBehavior(i);
        }
    }

    void IncrementKilledObjectCount(bool allowBombDrop)
    {
        WorldKillCount++;

        if (HelpDropCounter < 0xA)
        {
            HelpDropCounter++;
            if (HelpDropCounter == 0xA)
            {
                if (allowBombDrop)
                    HelpDropValue++;
            }
        }
    }

    // $7B67
    void ResetKilledObjectCount()
    {
        WorldKillCount = 0;
        HelpDropCounter = 0;
        HelpDropValue = 0;
    }

    void IncrementRoomKillCount()
    {
        RoomKillCount++;
    }

    void SetBombItemDrop()
    {
        HelpDropCounter = 0xA;
        HelpDropValue = 0xA;
    }

    void SetObservedPlayerPos(int x, int y)
    {
        fakePlayerPos.X = x;
        fakePlayerPos.Y = y;
    }

    void SetPersonWallY(int y)
    {
        State.Play.personWallY = y;
    }

    int GetFadeStep()
    {
        return DarkRoomFadeStep;
    }

    void BeginFadeIn()
    {
        if (DarkRoomFadeStep > 0)
            brightenRoom = true;
    }

    void FadeIn()
    {
        if (DarkRoomFadeStep == 0)
        {
            brightenRoom = false;
            return;
        }

        var timer = GetObjectTimer(ObjectSlot.FadeTimer);

        if (timer == 0)
        {
            DarkRoomFadeStep--;
            timer = 10; // TODO: Does this reference still work?

            for (int i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)(i + 2), infoBlock.DarkPalette(DarkRoomFadeStep, i));
            }
            Graphics.UpdatePalettes();
        }
    }

    bool UseKey()
    {
        if (GetItem(ItemSlot.MagicKey) != 0)
            return true;

        int keyCount = GetItem(ItemSlot.Keys);

        if (keyCount > 0)
        {
            keyCount--;
            SetItem(ItemSlot.Keys, keyCount);
            return true;
        }

        return false;
    }

    bool GetDoorState(int roomId, Direction door)
    {
        return curUWBlockFlags[roomId].GetDoorState(door);
    }

    void SetDoorState(int roomId, Direction door)
    {
        curUWBlockFlags[roomId].SetDoorState(door);
    }

    bool IsRoomInHistory()
    {
        for (int i = 0; i < RoomHistoryLength; i++)
        {
            if (RoomHistory[i] == curRoomId)
                return true;
        }
        return false;
    }

    void AddRoomToHistory()
    {
        int i = 0;

        for (; i < RoomHistoryLength; i++)
        {
            if (RoomHistory[i] == curRoomId)
                break;
        }

        if (i == RoomHistoryLength)
        {
            RoomHistory[NextRoomHistorySlot] = (byte)curRoomId;
            NextRoomHistorySlot++;
            if (NextRoomHistorySlot >= RoomHistoryLength)
                NextRoomHistorySlot = 0;
        }
    }

    bool FindSparseFlag(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId) != null;
    }

    SparsePos FindSparsePos(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparsePos>(attrId, roomId);
    }

    SparsePos2 FindSparsePos2(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparsePos2>(attrId, roomId);
    }

    SparseRoomItem FindSparseItem(Sparse attrId, int roomId)
    {
        return sparseRoomAttrs.FindSparseAttr<SparseRoomItem>(attrId, roomId);
    }

    Span<ObjectAttr> GetObjectAttrs()
    {
        return extraData.GetItems<ObjectAttr>(Extra.ObjAttrs);
    }

    public ObjectAttr GetObjectAttrs(ObjType type)
    {
        return GetObjectAttrs()[(int)type];
    }

    int GetObjectMaxHP(int type)
    {
        var hpAttrs = extraData.GetItems<HPAttr>(Extra.HitPoints);
        int index = type / 2;
        return hpAttrs[index].GetHP(type);
    }

    int GetPlayerDamage(int type)
    {
        var damageAttrs = extraData.GetItems<byte>(Extra.PlayerDamage);
        byte damageByte = damageAttrs[type];
        return ((damageByte & 0xF) << 8) | (damageByte & 0xF0);
    }

    void LoadRoom(int roomId, int tileMapIndex)
    {
        if (IsUWCellar(roomId))
        {
            LoadCellarContext();
            PrevRoomWasCellar = true;
        }
        else if (PrevRoomWasCellar)
        {
            LoadUnderworldContext();
            PrevRoomWasCellar = false;
        }

        curRoomId = roomId;
        curTileMapIndex = tileMapIndex;

        LoadMap(roomId, tileMapIndex);

        if (IsOverworld())
        {
            if (GotShortcut(roomId))
            {
                if (FindSparseFlag(Sparse.Shortcut, roomId))
                {
                    ShowShortcutStairs(roomId, tileMapIndex);
                }
            }

            if (!GotItem())
            {
                var roomItem = FindSparseItem(Sparse.Item, roomId);
                if (roomItem != null)
                {
                    // TODO: Arg.
                    var itemObj = new ItemActor(Game, roomItem.AsItemId, roomItem.x, roomItem.y, true);
                    objects[(int)ObjectSlot.Item] = itemObj;
                }
            }
        }
        else
        {
            if (!GotItem())
            {
                var uwRoomAttrs = CurrentUWRoomAttrs;

                if (uwRoomAttrs.GetSecret() != Secret.FoesItem
                    && uwRoomAttrs.GetSecret() != Secret.LastBoss)
                    AddUWRoomItem(roomId);
            }
        }
    }

    void AddUWRoomItem()
    {
        AddUWRoomItem(curRoomId);
    }

    void AddUWRoomItem(int roomId)
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;
        var itemId = uwRoomAttrs.GetItemId();

        if (itemId != ItemId.None)
        {
            int posIndex = uwRoomAttrs.GetItemPositionIndex();
            Point pos = GetRoomItemPosition(infoBlock.ShortcutPosition[posIndex]);

            if (itemId == ItemId.TriforcePiece)
                pos.X = TriforcePieceX;

            // Arg
            var itemObj = new ItemActor(Game, itemId, pos.X, pos.Y, true);
            objects[(int)ObjectSlot.Item] = itemObj;

            if (uwRoomAttrs.GetSecret() == Secret.FoesItem
                || uwRoomAttrs.GetSecret() == Secret.LastBoss)
                Game.Sound.Play(SoundEffect.RoomItem);
        }
    }

    void LoadCaveRoom(Cave uniqueRoomId)
    {
        curTileMapIndex = 0;

        LoadLayout((int)uniqueRoomId, 0, TileScheme.Overworld);
    }

    void LoadMap(int roomId, int tileMapIndex)
    {
        TileScheme tileScheme;
        int uniqueRoomId = roomAttrs[roomId].GetUniqueRoomId();

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

        LoadLayout(uniqueRoomId, tileMapIndex, tileScheme);

        if (tileScheme == TileScheme.UnderworldMain)
        {
            for (int i = 0; i < Doors; i++)
            {
                UpdateDoorTileBehavior(roomId, tileMapIndex, i);
            }
        }
    }

    void LoadOWMob(TileMap map, int row, int col, int mobIndex)
    {
        var primary = primaryMobs[mobIndex];

        if (primary == 0xFF)
        {
            int index = mobIndex * 4;
            var secondaries = secondaryMobs;
            map.tileRefs[(row) * (col)] = secondaries[(index + 0)];
            map.tileRefs[(row) * (col + 1)] = secondaries[(index + 2)];
            map.tileRefs[(row + 1) * (col)] = secondaries[(index + 1)];
            map.tileRefs[(row + 1) * (col + 1)] = secondaries[(index + 3)];
        }
        else
        {
            map.tileRefs[(row) * (col)] = primary;
            map.tileRefs[(row) * (col + 1)] = (byte)(primary + 2);
            map.tileRefs[(row + 1) * (col)] = (byte)(primary + 1);
            map.tileRefs[(row + 1) * (col + 1)] = (byte)(primary + 3);
        }
    }

    void LoadUWMob(TileMap map, int row, int col, int mobIndex)
    {
        var primary = primaryMobs[mobIndex];

        if (primary is < 0x70 or > 0xF2)
        {
            map.tileRefs[(row) * (col)] = primary;
            map.tileRefs[(row) * (col + 1)] = primary;
            map.tileRefs[(row + 1) * (col)] = primary;
            map.tileRefs[(row + 1) * (col + 1)] = primary;
        }
        else
        {
            map.tileRefs[(row) * (col)] = primary;
            map.tileRefs[(row) * (col + 1)] = (byte)(primary + 2);
            map.tileRefs[(row + 1) * (col)] = (byte)(primary + 1);
            map.tileRefs[(row + 1) * (col + 1)] = (byte)(primary + 3);
        }
    }

    void LoadLayout(int uniqueRoomId, int tileMapIndex, TileScheme tileScheme)
    {
        var maxColumnStartOffset = (colCount / 2 - 1) * rowCount / 2;

        var columns = roomCols[uniqueRoomId];
        var map = tileMaps[tileMapIndex];
        int rowEnd = startRow + rowCount;
        bool owLayoutFormat;

        owLayoutFormat = tileScheme is TileScheme.Overworld or TileScheme.UnderworldCellar;

        switch (tileScheme)
        {
            case TileScheme.Overworld: loadMobFunc = LoadOWMob; break;
            case TileScheme.UnderworldMain: loadMobFunc = LoadUWMob; break;
            case TileScheme.UnderworldCellar: loadMobFunc = LoadOWMob; break;
        }

        for (int i = 0; i < colCount / 2; i++)
        {
            byte columnDesc = columns.ColumnDesc[i];
            byte tableIndex = (byte)((columnDesc & 0xF0) >> 4);
            byte columnIndex = (byte)(columnDesc & 0x0F);

            var table = colTables[tableIndex];
            int k = 0;
            int j = 0;

            for (j = 0; j <= maxColumnStartOffset; j++)
            {
                byte t = table[j];

                if ((t & 0x80) != 0)
                {
                    if (k == columnIndex)
                        break;
                    k++;
                }
            }

            if (j > maxColumnStartOffset) throw new Exception();

            int c = startCol + i * 2;

            for (int r = startRow; r < rowEnd; j++)
            {
                byte t = table[j];
                byte tileRef;

                if (owLayoutFormat)
                    tileRef = (byte)(t & 0x3F);
                else
                    tileRef = (byte)(t & 0x7);

                loadMobFunc(map, r, c, tileRef);

                byte attr = tileAttrs[tileRef];
                var action = TileAttr.GetAction(attr);
                TileActionDel actionFunc = null;

                if (action != 0)
                {
                    actionFunc = sActionFuncs[(int)action];
                    actionFunc(r, c, TileInteraction.Load);
                }

                r += 2;

                if (owLayoutFormat)
                {
                    if ((t & 0x40) != 0 && r < rowEnd)
                    {
                        loadMobFunc(map, r, c, tileRef);

                        if (actionFunc != null)
                            actionFunc(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
                else
                {
                    int repeat = (t >> 4) & 0x7;
                    for (int m = 0; m < repeat && r < rowEnd; m++)
                    {
                        loadMobFunc(map, r, c, tileRef);

                        if (actionFunc != null)
                            actionFunc(r, c, TileInteraction.Load);
                        r += 2;
                    }
                }
            }
        }

        if (IsUWMain(curRoomId))
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            if (uwRoomAttrs.HasBlock())
            {
                for (int c = startCol; c < startCol + colCount; c += 2)
                {
                    var tileRef = tileMaps[curTileMapIndex].tileRefs[UWBlockRow * c];
                    if (tileRef == (byte)BlockObjType.Tile_Block)
                    {
                        sActionFuncs[(int)TileAction.Block](UWBlockRow, c, TileInteraction.Load);
                        break;
                    }
                }
            }
        }

        for (var i = 0; i < Rows * Columns; i++)
        {
            var t = map.tileRefs[i];
            map.tileBehaviors[i] = tileBehaviors[t];
        }

        PatchTileBehaviors();
    }

    void PatchTileBehaviors()
    {
        PatchTileBehavior(ghostCount, ghostCells, TileBehavior.Ghost0);
        PatchTileBehavior(armosCount, armosCells, TileBehavior.Armos0);
    }

    void PatchTileBehavior(int count, Cell[] cells, TileBehavior baseBehavior)
    {
        for (int i = 0; i < count; i++)
        {
            int row = cells[i].Row;
            int col = cells[i].Col;
            var behavior = (byte)((int)baseBehavior + 15 - i);
            tileMaps[curTileMapIndex].tileBehaviors[(row) * (col)] = behavior;
            tileMaps[curTileMapIndex].tileBehaviors[(row) * (col + 1)] = behavior;
            tileMaps[curTileMapIndex].tileBehaviors[(row + 1) * (col)] = behavior;
            tileMaps[curTileMapIndex].tileBehaviors[(row + 1) * (col + 1)] = behavior;
        }
    }

    void UpdateDoorTileBehavior(int doorOrd)
    {
        UpdateDoorTileBehavior(curRoomId, curTileMapIndex, doorOrd);
    }

    void UpdateDoorTileBehavior(int roomId, int tileMapIndex, int doorOrd)
    {
        var map = tileMaps[tileMapIndex];
        Direction dir = doorOrd.GetOrdDirection();
        Cell corner = doorCorners[doorOrd];
        DoorType type = GetDoorType(roomId, dir);
        bool state = GetEffectiveDoorState(roomId, dir);
        var behavior = (byte)(state ? doorBehaviors[(int)type].Open : doorBehaviors[(int)type].Closed);

        map.tileBehaviors[corner.Row * (corner.Col)] = behavior;
        map.tileBehaviors[corner.Row * (corner.Col + 1)] = behavior;
        map.tileBehaviors[(corner.Row + 1) * (corner.Col)] = behavior;
        map.tileBehaviors[(corner.Row + 1) * (corner.Col + 1)] = behavior;

        if ((TileBehavior)behavior == TileBehavior.Doorway)
        {
            corner = behindDoorCorners[doorOrd];
            map.Behaviors(corner.Row, corner.Col) = behavior;
            map.Behaviors(corner.Row, corner.Col + 1) = behavior;
            map.Behaviors(corner.Row + 1, corner.Col) = behavior;
            map.Behaviors(corner.Row + 1, corner.Col + 1) = behavior;
        }
    }

    void GotoPlay(PlayState.RoomType roomType = PlayState.RoomType.Regular)
    {
        switch (roomType)
        {
            case PlayState.RoomType.Regular: curMode = GameMode.Play; break;
            case PlayState.RoomType.Cave: curMode = GameMode.PlayCave; break;
            case PlayState.RoomType.Cellar: curMode = GameMode.PlayCellar; break;
            default:
                throw new Exception();
                curMode = GameMode.Play;
                break;
        }
        CurColorSeqNum = 0;
        TempShutters = false;
        RoomObjCount = 0;
        RoomObj = null;
        RoomKillCount = 0;
        RoomAllDead = false;
        MadeRoomItem = false;
        EnablePersonFireballs = false;
        ghostCount = 0;
        armosCount = 0;

        State.Play.substate = PlayState.Substates.Active;
        State.Play.animatingRoomColors = false;
        State.Play.allowWalkOnWater = false;
        State.Play.uncoveredRecorderSecret = false;
        State.Play.roomType = roomType;
        State.Play.liftItemTimer = 0;
        State.Play.liftItemId = 0;
        State.Play.personWallY = 0;

        if (FindSparseFlag(Sparse.Dock, curRoomId))
        {
            var slot = FindEmptyMonsterSlot();
            var dock = new DockActor(Game, 0, 0);
            SetObject(slot, dock);
        }

        // Set the level's level foreground palette before making objects,
        // so that if we make a boss, we won't override a palette that it might set.
        SetLevelFgPalette();
        Graphics.UpdatePalettes();
        PlayAmbientSounds();

        Direction dir = Game.Link.Facing;

        ClearRoomItemData();
        GlobalFunctions.ClearRoomMonsterData();
        InitObjectTimers();
        InitStunTimers();
        InitPlaceholderTypes();
        MakeObjects(dir);
        MakeWhirlwind();
        AddRoomToHistory();
        MoveRoomItem();

        if (!IsOverworld())
            curUWBlockFlags[curRoomId].SetVisitState();
    }

    void UpdatePlay()
    {
        if (State.Play.substate == PlayState.Substates.Active)
        {
            if (brightenRoom)
            {
                FadeIn();
                DecrementObjectTimers();
                DecrementStunTimers();
                return;
            }

            if (Submenu != 0)
            {
                if (Input.IsButtonPressing(Button.Select))
                {
                    Submenu = 0;
                    SubmenuOffsetY = 0;
                    GotoContinueQuestion();
                }
                else
                {
                    UpdateSubmenu();
                }
                return;
            }

            if (Pause == 0)
            {
                if (Input.IsButtonPressing(Button.Select))
                {
                    Pause = 1;
                    Game.Sound.Pause();
                    return;
                }
                else if (Input.IsButtonPressing(Button.Start))
                {
                    Submenu = 1;
                    return;
                }
            }
            else if (Pause == 1)
            {
                if (Input.IsButtonPressing(Button.Select))
                {
                    Pause = 0;
                    Game.Sound.Unpause();
                }
                return;
            }

            DecrementObjectTimers();
            DecrementStunTimers();

            if (objectTimers[(int)ObjectSlot.FluteMusic] != 0)
                return;

            if (Pause == 2)
            {
                FillHeartsStep();
                return;
            }

            if (State.Play.animatingRoomColors)
                UpdateRoomColors();

            if (IsUWMain(curRoomId))
                CheckBombables();

            UpdateRupees();
            UpdateLiftItem();

            curObjSlot = (int)ObjectSlot.Player;
            Game.Link.DecInvincibleTimer();
            Game.Link.Update();

            // The player's update might have changed the world's State.
            if (!IsPlaying())
                return;

            UpdateObservedPlayerPos();

            for (curObjSlot = (int)ObjectSlot.MaxObjects - 1; curObjSlot >= 0; curObjSlot--)
            {
                var obj = objects[curObjSlot];
                if (obj != null && !obj.IsDeleted)
                {
                    if (obj.DecoratedUpdate())
                        HandleNormalObjectDeath();
                }
                else if (placeholderTypes[curObjSlot] != 0)
                {
                    PutEdgeObject();
                }
            }

            DeleteDeadObjects();

            CheckSecrets();
            CheckShutters();
            UpdateDoors2();
            UpdateStatues();
            MoveRoomItem();
            CheckPowerTriforceFanfare();
            AdjustInventory();
            WarnLowHPIfNeeded();
        }
    }

    void UpdateSubmenu()
    {
        if (Submenu == 1)
        {
            menu.Enable();
            Submenu++;
            statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
        }
        else if (Submenu == 7)
        {
            SubmenuOffsetY += 3;
            if (SubmenuOffsetY >= SubmenuType.Height)
            {
                menu.Activate();
                Submenu++;
            }
        }
        else if (Submenu == 8)
        {
            if (Input.IsButtonPressing(Button.Start))
            {
                menu.Deactivate();
                Submenu++;
            }
        }
        else if (Submenu == 9)
        {
            SubmenuOffsetY -= 3;
            if (SubmenuOffsetY == 0)
            {
                menu.Disable();
                Submenu = 0;
                statusBar.EnableFeatures(StatusBarFeatures.Equipment, true);
            }
        }
        else
        {
            Submenu++;
        }

        if (Submenu != 0)
            menu.Update();
    }

    void CheckShutters()
    {
        if (triggerShutters)
        {
            triggerShutters = false;

            Direction dirs = 0;

            for (int i = 0; i < 4; i++)
            {
                Direction dir = i.GetOrdDirection();

                if (GetDoorType(dir) == DoorType.Shutter
                    && !GetEffectiveDoorState(dir))
                {
                    dirs |= dir;
                }
            }

            if (dirs != 0 && triggeredDoorCmd == 0)
            {
                triggeredDoorCmd = 6;
                triggeredDoorDir |= (Direction)0x10;
            }
        }
    }

    void UpdateDoors2()
    {
        if (GetMode() == GameMode.EndLevel
            || objectTimers[(int)ObjectSlot.Door] != 0
            || triggeredDoorCmd == 0)
        {
            return;
        }

        if ((triggeredDoorCmd & 1) == 0)
        {
            triggeredDoorCmd++;
            objectTimers[(int)ObjectSlot.Door] = 8;
        }
        else
        {
            if ((triggeredDoorDir & (Direction)0x10) != 0)
            {
                OpenShutters();
            }

            int d = 1;

            for (int i = 0; i < 4; i++, d <<= 1)
            {
                if ((triggeredDoorDir & (Direction)d) == 0)
                    continue;

                Direction dir = (Direction)d;
                DoorType type = GetDoorType(dir);

                if (type is DoorType.Bombable or DoorType.Key or DoorType.Key2)
                {
                    if (!GetDoorState(curRoomId, dir))
                    {
                        Direction oppositeDir = dir.GetOppositeDirection();
                        int nextRoomId = GetNextRoomId(curRoomId, dir);

                        SetDoorState(curRoomId, dir);
                        SetDoorState(nextRoomId, oppositeDir);
                        if (type != DoorType.Bombable)
                            Game.Sound.Play(SoundEffect.Door);
                        UpdateDoorTileBehavior(i);
                    }
                }
            }

            triggeredDoorCmd = 0;
            triggeredDoorDir = Direction.None;
        }
    }

    byte GetNextTeleportingRoomIndex()
    {
        Direction facing = Game.Link.Facing;
        bool growing = facing is Direction.Up or Direction.Right;

        var pieces = GetItem(ItemSlot.TriforcePieces);
        var index = TeleportingRoomIndex;
        var mask = 1 << TeleportingRoomIndex;

        if (pieces == 0)
            return 0;

        do
        {
            if (growing)
            {
                index = (byte)((index + 1) & 7);
                mask <<= 1;
                if (mask >= 0x100)
                    mask = 1;
            }
            else
            {
                index = (byte)((index - 1) & 7);
                mask >>= 1;
                if (mask == 0)
                    mask = 0x80;
            }
        } while ((pieces & mask) == 0);

        return index;
    }

    void UpdateRoomColors()
    {
        if (State.Play.timer == 0)
        {
            State.Play.animatingRoomColors = false;
            var posAttr = FindSparsePos(Sparse.Recorder, curRoomId);
            if (posAttr != null)
            {
                GetRoomCoord(posAttr.pos, out var row, out var col);
                SetMob(row * 2, col * 2, BlockObjType.Mob_Stairs);
                Game.Sound.Play(SoundEffect.Secret);
            }
            return;
        }

        if ((State.Play.timer % 8) == 0)
        {
            var colorSeq = extraData.GetItem<ColorSeq>(Extra.PondColors);
            if (CurColorSeqNum < colorSeq.Length - 1)
            {
                if (CurColorSeqNum == colorSeq.Length - 2)
                {
                    State.Play.allowWalkOnWater = true;
                }

                int colorIndex = colorSeq.Colors[CurColorSeqNum];
                CurColorSeqNum++;
                Graphics.SetColorIndexed((Palette)3, 3, colorIndex);
                Graphics.UpdatePalettes();
            }
        }

        State.Play.timer--;
    }

    void CheckBombables()
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;

        for (int iBomb = (int)ObjectSlot.FirstBomb; iBomb < (int)ObjectSlot.LastBomb; iBomb++)
        {
            var bomb = objects[iBomb] as BombActor;
            if (bomb == null || bomb.BombState != BombState.Fading) continue;

            int bombX = bomb.X + 8;
            int bombY = bomb.Y + 8;

            for (int iDoor = 0; iDoor < 4; iDoor++)
            {
                var doorType = uwRoomAttrs.GetDoor(iDoor);
                if (doorType == DoorType.Bombable)
                {
                    var doorDir = iDoor.GetOrdDirection();
                    bool doorState = GetDoorState(curRoomId, doorDir);
                    if (!doorState)
                    {
                        if (Math.Abs(bombX - doorMiddles[iDoor].X) < UWBombRadius
                            && Math.Abs(bombY - doorMiddles[iDoor].Y) < UWBombRadius)
                        {
                            triggeredDoorCmd = 6;
                            triggeredDoorDir = doorDir;
                            break;
                        }
                    }
                }
            }
        }
    }

    public bool HasLivingObjects()
    {
        return !RoomAllDead;
    }

    bool CalcHasLivingObjects()
    {
        for (int i = (int)ObjectSlot.Monster1 ; i < (int)ObjectSlot.MonsterEnd; i++)
        {
            var obj = objects[i];
            // if (obj != null)
            // {
            //     ObjType type = obj.GetType();
            //     if (type < Obj_Bubble1
            //         || (type > Obj_Bubble3 && type < Obj_Trap))
            //         return true;
            // }
            if (obj != null && obj.CountsAsLiving) return true;
        }

        return false;
    }

    void CheckSecrets()
    {
        if (IsOverworld())
            return;

        if (!RoomAllDead)
        {
            if (!CalcHasLivingObjects())
            {
                // TODO Game.Link.SetParalyzed(false);
                RoomAllDead = true;
            }
        }

        var uwRoomAttrs = CurrentUWRoomAttrs;
        var secret = uwRoomAttrs.GetSecret();

        switch (secret)
        {
            case Secret.Ringleader:
                if (GetObject(ObjectSlot.Monster1) == null  || GetObject(ObjectSlot.Monster1) is PersonActor)
                {
                    KillAllObjects();
                }
                break;

            case Secret.LastBoss:
                if (GetItem(ItemSlot.PowerTriforce) != 0)
                    triggerShutters = true;
                break;

            case Secret.FoesItem:
                // ORIGINAL: BlockDoor and BlockStairs are handled here.
                if (RoomAllDead)
                {
                    if (!MadeRoomItem && !GotItem())
                    {
                        MadeRoomItem = true;
                        AddUWRoomItem(curRoomId);
                    }
                }
                // fall thru
                goto case Secret.FoesDoor;
            case Secret.FoesDoor:
                if (RoomAllDead)
                    triggerShutters = true;
                break;
        }
    }

    void AddUWRoomStairs()
    {
        SetMobXY(0xD0, 0x60, BlockObjType.Mob_UW_Stairs);
    }

    void KillAllObjects()
    {
        for (int i = (int)ObjectSlot.Monster1; i < (int)ObjectSlot.MonsterEnd; i++)
        {
            var obj = objects[i];
            if (obj != null
                && obj.ObjType < ObjType.PersonEnd
                && obj.Decoration == 0)
            {
                obj.Decoration = 0x10;
            }
        }
    }

    void MoveRoomItem()
    {
        var foe = GetObject(ObjectSlot.Monster1);
        if (foe == null)
            return;

        var item = GetObject(ObjectSlot.Item);


        if (item != null && foe.CanHoldRoomItem)
        {
            item.X = foe.X;
            item.Y = foe.Y;
        }
    }

    private static Span<int> _fireballLayouts => new[] { 0x24, 0x23 };

    void UpdateStatues()
    {
        if (IsOverworld())
            return;

        var pattern = -1;

        if (EnablePersonFireballs)
        {
            pattern = 2;
        }
        else
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            int layoutId = uwRoomAttrs.GetUniqueRoomId();

            for (int i = 0; i < _fireballLayouts.Length; i++)
            {
                if (_fireballLayouts[i] == layoutId)
                {
                    pattern = i;
                    break;
                }
            }
        }

        if (pattern >= 0)
            Statues.Update((Statues.PatternType)pattern);
    }

    void OnLeavePlay()
    {
        if (lastMode == GameMode.Play)
            SaveObjectCount();
    }

    void ClearLevelData()
    {
        CurColorSeqNum = 0;
        DarkRoomFadeStep = 0;
        CurMazeStep = 0;
        TempShutterRoomId = 0;
        TempShutterDoorDir = 0;
        TempShutterRoomId = 0;
        TempShutters = false;
        PrevRoomWasCellar = false;
        WhirlwindTeleporting = 0;

        RoomKillCount = 0;
        RoomAllDead = false;
        MadeRoomItem = false;
        EnablePersonFireballs = false;
    }

    static bool IsRecurringFoe(ObjType type)
    {
        return type is < ObjType.OneDodongo or ObjType.RedLamnola or ObjType.BlueLamnola or >= ObjType.Trap;
    }

    void SaveObjectCount()
    {
        if (IsOverworld())
        {
            var flags = profile.OverworldFlags[curRoomId];
            int savedCount = flags.GetObjCount();
            int count;

            if (RoomKillCount >= RoomObjCount)
            {
                count = 7;
            }
            else
            {
                count = (RoomKillCount & 7) + savedCount;
                if (count > 7)
                    count = 7;
            }

            flags.SetObjCount(count);
        }
        else
        {
            var flags = curUWBlockFlags[curRoomId];
            int count;

            if (RoomObjCount != 0)
            {
                if (RoomKillCount == 0  || (RoomObj != null && RoomObj.IsReoccuring))
                {
                    if (RoomKillCount < RoomObjCount)
                    {
                        LevelKillCounts[curRoomId] += RoomKillCount;
                        if (LevelKillCounts[curRoomId] < 3)
                            count = LevelKillCounts[curRoomId];
                        else
                            count = 2;
                        flags.SetObjCount((byte)count);
                        return;
                    }
                }
            }

            LevelKillCounts[curRoomId] = 0xF;
            flags.SetObjCount(3);
        }
    }

    void CalcObjCountToMake(ref ObjType type, ref int count)
    {
        if (IsOverworld())
        {
            var flags = profile.OverworldFlags[curRoomId];

            if (!IsRoomInHistory() && (flags.GetObjCount() == 7))
            {
                flags.SetObjCount(0);
            }
            else
            {
                if (flags.GetObjCount() == 7)
                {
                    type = ObjType.None;
                    count = 0;
                }
                else if (flags.GetObjCount() != 0)
                {
                    int savedCount = flags.GetObjCount();
                    if (count < savedCount)
                    {
                        type = ObjType.None;
                        count = 0;
                    }
                    else
                    {
                        count -= savedCount;
                    }
                }
            }
        }
        else // Is Underworld
        {
            var flags = curUWBlockFlags[curRoomId];

            if (IsRoomInHistory() || flags.GetObjCount() != 3)
            {
                if (count < LevelKillCounts[curRoomId])
                {
                    type = ObjType.None;
                    count = 0;
                }
                else
                {
                    count -= LevelKillCounts[curRoomId];
                }
            }
            else
            {
                if (IsRecurringFoe(type))
                {
                    flags.SetObjCount(0);
                    LevelKillCounts[curRoomId] = 0;
                }
                else
                {
                    type = ObjType.None;
                    count = 0;
                }
            }
        }
    }

    void UpdateObservedPlayerPos()
    {
        // ORIGINAL: This happens after player updates and before player items update.

        if (!giveFakePlayerPos)
        {
            fakePlayerPos.X = Game.Link.X;
            fakePlayerPos.Y = Game.Link.Y;
        }

        // ORIGINAL: This happens after player items update and before the rest of objects update.

        if (stunTimers[(int)ObjectSlot.ObservedPlayerTimer] == 0)
        {
            stunTimers[(int)ObjectSlot.ObservedPlayerTimer] = Random.Shared.Next(0, 8);

            giveFakePlayerPos = !giveFakePlayerPos;
            if (giveFakePlayerPos)
            {
                if (fakePlayerPos.X == Game.Link.X)
                {
                    fakePlayerPos.X ^= 0xFF;
                    fakePlayerPos.Y ^= 0xFF;
                }
            }
        }
    }

    void UpdateRupees()
    {
        if ((Game.GetFrameCounter() & 1) == 0)
        {
            var rupeesToAdd = profile.Items[ItemSlot.RupeesToAdd];
            var rupeesToSubtract = profile.Items[ItemSlot.RupeesToSubtract];

            if (rupeesToAdd > 0 && rupeesToSubtract == 0)
            {
                if (profile.Items[ItemSlot.Rupees] < 255)
                    profile.Items[ItemSlot.Rupees]++;
                else
                    profile.Items[ItemSlot.RupeesToAdd] = 0;

                Game.Sound.Play(SoundEffect.Character);
            }
            else if (rupeesToAdd == 0 && rupeesToSubtract > 0)
            {
                if (profile.Items[ItemSlot.Rupees] > 0)
                    profile.Items[ItemSlot.Rupees]--;
                else
                    profile.Items[ItemSlot.RupeesToSubtract] = 0;

                Game.Sound.Play(SoundEffect.Character);
            }

            if (profile.Items[ItemSlot.RupeesToAdd] > 0)
                profile.Items[ItemSlot.RupeesToAdd]--;

            if (profile.Items[ItemSlot.RupeesToSubtract] > 0)
                profile.Items[ItemSlot.RupeesToSubtract]--;
        }
    }

    void UpdateLiftItem()
    {
        if (State.Play.liftItemId == 0)
            return;

        State.Play.liftItemTimer--;

        if (State.Play.liftItemTimer == 0)
        {
            State.Play.liftItemId = 0;
            Game.Link.SetState(PlayerState.Idle);
        }
        else
        {
            Game.Link.SetState(PlayerState.Paused);
        }
    }

    void DrawPlay()
    {
        if (Submenu != 0)
        {
            DrawSubmenu();
            return;
        }

        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        ClearScreen();
        DrawRoom();
        Graphics.ResetClip();

        Actor? objOverPlayer = null;

        DrawObjects(ref objOverPlayer);

        if (IsLiftingItem())
            DrawLinkLiftingItem(State.Play.liftItemId);
        else
            Game.Link.Draw();

        if (objOverPlayer != null)
            objOverPlayer.DecoratedDraw();

        if (IsUWMain(curRoomId))
            DrawDoors(curRoomId, true, 0, 0);
    }

    void DrawSubmenu()
    {
        Graphics.SetClip(0, TileMapBaseY + SubmenuOffsetY, TileMapWidth, TileMapHeight - SubmenuOffsetY);
        ClearScreen();
        DrawMap(curRoomId, curTileMapIndex, 0, SubmenuOffsetY);
        Graphics.ResetClip();

        if (IsUWMain(curRoomId))
            DrawDoors(curRoomId, true, 0, SubmenuOffsetY);

        menu.Draw(SubmenuOffsetY);
    }

    void DrawObjects(ref Actor? objOverPlayer)
    {
        for (int i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            curObjSlot = i;

            var obj = objects[i];
            if (obj != null && !obj.IsDeleted)
            {
                if (!obj.Flags.HasFlag(ActorFlags.DrawAbovePlayer) || objOverPlayer == null)
                {
                    obj.DecoratedDraw();
                }
                else
                {
                    objOverPlayer = obj;
                }
            }
        }
    }

    void DrawZeldaLiftingTriforce(int x, int y)
    {
        var image = Graphics.GetSpriteImage(TileSheet.Boss, AnimationId.B3_Zelda_Lift);
        image.Draw(TileSheet.Boss, x, y, Palette.Player);

        GlobalFunctions.DrawItem(Game, ItemId.TriforcePiece, x, y - 0x10, 0);
    }

    void DrawLinkLiftingItem(ItemId itemId)
    {
        var animIndex = itemId == ItemId.TriforcePiece ? AnimationId.LinkLiftHeavy : AnimationId.LinkLiftLight;
        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, animIndex);
        image.Draw(TileSheet.PlayerAndItems, Game.Link.X, Game.Link.Y, Palette.Player);

        GlobalFunctions.DrawItem(Game, itemId, Game.Link.X, Game.Link.Y - 0x10, 0);
    }

    void MakeObjects(Direction entryDir)
    {
        if (IsUWCellar(curRoomId))
        {
            MakeCellarObjects();
            return;
        }
        else if (State.Play.roomType == PlayState.RoomType.Cave)
        {
            MakeCaveObjects();
            return;
        }

        var roomAttr = roomAttrs[curRoomId];
        ObjectSlot slot = ObjectSlot.Monster1;
        var objId = (ObjType)roomAttr.MonsterListId;
        bool edgeObjects = false;

        if (objId is >= ObjType.Person1 and < ObjType.PersonEnd or ObjType.Grumble)
        {
            MakeUnderworldPerson(objId);
            return;
        }

        if (IsOverworld())
        {
            var owRoomAttrs = CurrentOWRoomAttrs;
            edgeObjects = owRoomAttrs.MonstersEnter();
        }

        int count = roomAttr.GetMonsterCount();

        if (objId is >= ObjType.OneDodongo and < ObjType.Rock)
            count = 1;

        CalcObjCountToMake(ref objId, ref count);
        RoomObjCount = count;

        if (objId > 0 && count > 0)
        {
            bool isList = objId >= ObjType.Rock;
            var repeatedIds = new byte[(int)ObjectSlot.MaxMonsters];
            ReadOnlySpan<byte> list = null;

            if (isList)
            {
                int listId = objId - ObjType.Rock;
                list = objLists.GetItem(listId);
            }
            else
            {
                Array.Fill(repeatedIds, (byte)objId, 0, count);
                list = repeatedIds;
            }

            int dirOrd = entryDir.GetOrdinal();
            var spotSeq = extraData.GetItem<SpotSeq>(Extra.SpawnSpots);
            int spotsLen = spotSeq.Length / 4;
            var dirSpots = spotSeq.GetSpots()[(spotsLen * dirOrd)..]; // JOE: This is very sus.

            for (int i = 0; i < count; i++, slot++)
            {
                // An earlier objects that's made might make some objects in slots after it.
                // Maybe MakeMonster should take a reference to the current index.
                if (GetObject(slot) != null)
                    continue;

                curObjSlot = (int)slot;

                var type = (ObjType)list[(int)slot];

                if (edgeObjects
                    && type != ObjType.Zora
                    && type != ObjType.Armos
                    && type != ObjType.StandingFire
                    && type != ObjType.Whirlwind
                    )
                {
                    placeholderTypes[(int)slot] = (byte)type;
                }
                else if (FindSpawnPos(type, dirSpots, spotsLen, out var x, out var y))
                {
                    var obj = Actor.FromType((ObjType)type, Game, x, y, false);
                    objects[(int)slot] = obj;
                }
            }
        }

        var monster = GetObject(ObjectSlot.Monster1);
        if (monster != null)
            RoomObj = monster;

        if (IsOverworld())
        {
            var owRoomAttr = CurrentOWRoomAttrs;
            if (owRoomAttr.HasZora())
            {
                curObjSlot = (int)slot;

                var zora = Actor.FromType(ObjType.Zora, Game, 0, 0, false);
                SetObject(slot, zora);
                slot++;
            }
        }
    }


    void MakeCellarObjects()
    {
        // TODO: Make static
        static ReadOnlySpan<int> startXs() => new int[] { 0x20, 0x60, 0x90, 0xD0 };
        const int startY = 0x9D;

        for (int i = 0; i < 4; i++)
        {
            curObjSlot = i;

            var keese = Actor.FromType(ObjType.BlueKeese, Game, startXs()[i], startY, false);
            Game.SetObject((ObjectSlot)i, keese);
        }
    }

    void MakeCaveObjects()
    {
        var owRoomAttrs = CurrentOWRoomAttrs;
        int caveIndex = owRoomAttrs.GetCaveId() - FirstCaveIndex;

        var caves = extraData.GetItem<CaveSpecList>(Extra.Caves);

        if (caveIndex >= caves.Count)
            return;

        var cave = caves.Specs[caveIndex];
        var type = (ObjType)((int)ObjType.Cave1 + caveIndex);

        MakePersonRoomObjects(type, cave);
    }

    void MakeUnderworldPerson(ObjType type)
    {
        var cave = new CaveSpec();

        cave.Items[0] = (byte)ItemId.None;
        cave.Items[1] = (byte)ItemId.None;
        cave.Items[2] = (byte)ItemId.None;

        var uwRoomAttrs = CurrentUWRoomAttrs;
        Secret secret = (Secret)uwRoomAttrs.GetSecret();

        if (type == ObjType.Grumble)
        {
            cave.String = "Grumble Grumble";
            cave.DwellerType = DwellerType.FriendlyMoblin;
        }
        else if (secret == Secret.MoneyOrLife)
        {
            cave.String = "Money or life";
            cave.DwellerType = DwellerType.OldMan;
            cave.Items[0] = (byte)ItemId.HeartContainer;
            cave.Prices[0] = 1;
            cave.Items[2] = (byte)ItemId.Rupee;
            cave.Prices[2] = 50;
            cave.SetShowNegative();
            cave.SetShowItems();
            cave.SetSpecial();
            cave.SetPickUp();
        }
        else
        {
            var stringIdTables = extraData.GetItem<LevelPersonStrings>(Extra.LevelPersonStringIds);

            int levelIndex = infoBlock.EffectiveLevelNumber - 1;
            int levelTableIndex = levelGroups[levelIndex];
            int stringSlot = type - ObjType.Person1;
            var stringId = (StringId)stringIdTables.GetStringIds(levelTableIndex)[stringSlot];

            cave.DwellerType = DwellerType.OldMan;
            cave.String = "I'm old man";

            if (stringId == StringId.MoreBombs)
            {
                cave.Items[1] = (byte)ItemId.Rupee;
                cave.Prices[1] = 100;
                cave.SetShowNegative();
                cave.SetShowItems();
                cave.SetSpecial();
                cave.SetPickUp();
            }
        }

        MakePersonRoomObjects(type, cave);
    }

    // JOE: type is no longer a type I think? It's a cave ID.
    void MakePersonRoomObjects(ObjType type, CaveSpec spec)
    {
        static ReadOnlySpan<int> fireXs() => new[] { 0x48, 0xA8 };

        if (spec.DwellerType != DwellerType.None)
        {
            curObjSlot = 0;
            var person = GlobalFunctions.MakePerson(Game, type, spec, 0x78, 0x80);
            Game.SetObject(0, person);
        }

        for (int i = 0; i < 2; i++)
        {
            curObjSlot++;
            var fire = new StandingFireActor(Game, fireXs()[i], 0x80);
            Game.SetObject((ObjectSlot)curObjSlot, fire );
        }
    }

    void MakeWhirlwind()
    {
        static Span<int> teleportYs() => new[] { 0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D };

        if (WhirlwindTeleporting != 0)
        {
            int y = teleportYs()[TeleportingRoomIndex];

            WhirlwindTeleporting = 2;

            var whirlwind = new WhirlwindActor(Game, 0, y);
            Game.SetObject(ObjectSlot.Whirlwind, whirlwind);

            Game.Link.SetState(PlayerState.Paused);
            Game.Link.X = whirlwind.X;
            Game.Link.Y = 0xF8;
        }
    }

    bool FindSpawnPos(ObjType type, ReadOnlySpan<byte> spots, int len, out int x, out int y)
    {
        var objAttrs = GetObjectAttrs();

        int playerX = Game.Link.X;
        int playerY = Game.Link.Y;
        bool noWorldCollision = !objAttrs[(int)type].GetWorldCollision();
        x = 0;
        y = 0;
        int i;
        for (i = 0; i < (int)ObjectSlot.MaxObjListSize; i++)
        {
            GetRSpotCoord(spots[SpotIndex], out x, out y);
            SpotIndex = (SpotIndex + 1) % len;

            if ((playerX != x || playerY != y)
                && (noWorldCollision || !CollidesWithTileStill(x, y)))
            {
                break;
            }
        }

        if (x == 0 && y == 0) throw new Exception();

        return i != 9;
    }

    void PutEdgeObject()
    {
        if (stunTimers[(int)ObjectSlot.EdgeObjTimer] != 0)
            return;

        stunTimers[(int)ObjectSlot.EdgeObjTimer] = Random.Shared.Next(0, 4) + 2;

        int x = EdgeX;
        int y = EdgeY;

        for (; ; )
        {
            if (x == 0)
                y += 0x10;
            else if (x == 0xF0)
                y -= 0x10;

            if (y == 0x40)
                x -= 0x10;
            else if (y == 0xE0)
                x += 0x10;

            int row = (y / 8) - 8;
            int col = (x / 8);
            TileBehavior behavior = GetTileBehavior(row, col);

            if ((behavior != TileBehavior.Sand) && !CollidesTile(behavior))
                break;
            if (y == EdgeY && x == EdgeX)
                break;
        }

        EdgeX = x;
        EdgeY = y;

        if (Math.Abs(Game.Link.X - x) >= 0x22 || Math.Abs(Game.Link.Y - y) >= 0x22)
        {
            // What?
            var obj = Actor.FromType((ObjType)placeholderTypes[curObjSlot], Game, x, y - 3, false);
            objects[curObjSlot] = obj;
            placeholderTypes[curObjSlot] = 0;
            obj.Decoration = 0;
        }
    }

    void HandleNormalObjectDeath()
    {
        var obj = objects[curObjSlot] ?? throw new Exception("Missing object");
        int x = obj.X;
        int y = obj.Y;

        objects[curObjSlot] = null;

        if (obj is not (ChildGelActor or DeadDummyActor or KeeseActor { Color: ActorColor.Red }))
        {
            int cycle = WorldKillCycle + 1;
            if (cycle == 10)
                cycle = 0;
            WorldKillCycle = (byte)cycle;

            if (obj is not ZoraActor)
                RoomKillCount++;
        }

        TryDroppingItem(obj, x, y);
    }

    private static Span<int> _classBases => new[] { 0, 10, 20, 30 };
    private static Span<int> _classRates => new[] { 0x50, 0x98, 0x68, 0x68 };
    private static Span<int> _dropItems => new[] {
        0x22, 0x18, 0x22, 0x18, 0x23, 0x18, 0x22, 0x22, 0x18, 0x18, 0x0F, 0x18, 0x22, 0x18, 0x0F, 0x22,
        0x21, 0x18, 0x18, 0x18, 0x22, 0x00, 0x18, 0x21, 0x18, 0x22, 0x00, 0x18, 0x00, 0x22, 0x22, 0x22,
        0x23, 0x18, 0x22, 0x23, 0x22, 0x22, 0x22, 0x18
    };


    void TryDroppingItem(Actor origType, int x, int y)
    {
        if (curObjSlot == (int)ObjectSlot.Monster1 && origType is StalfosActor or GibdoActor)
            return;

        int objClass = origType.Attributes.GetItemDropClass();
        if (objClass == 0)
            return;
        objClass--;

        ItemId itemId;

        if (WorldKillCount == 0x10)
        {
            itemId = ItemId.Fairy;
            HelpDropCounter = 0;
            HelpDropValue = 0;
        }
        else if (HelpDropCounter >= 0xA)
        {
            if (HelpDropValue == 0)
                itemId = ItemId.FiveRupees;
            else
                itemId = ItemId.Bomb;
            HelpDropCounter = 0;
            HelpDropValue = 0;
        }
        else
        {
            int r = Random.Shared.GetByte();
            int rate = _classBases[objClass];

            if (r >= rate)
                return;

            int classIndex = _classBases[objClass] + WorldKillCycle;
            itemId = (ItemId)_dropItems[classIndex];
        }

        var obj = GlobalFunctions.MakeItem(Game, itemId, x, y, false);
        objects[curObjSlot] = obj;
    }

    void FillHeartsStep()
    {
        Game.Sound.Play(SoundEffect.Character);

        var profile = GetProfile();
        int maxHeartsValue = profile.GetMaxHeartsValue();

        FillHearts(6);

        if (profile.Hearts == maxHeartsValue)
        {
            Pause = 0;
            SwordBlocked = false;
        }
    }

    void GotoScroll(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        State.Scroll.curRoomId = curRoomId;
        State.Scroll.scrollDir = dir;
        State.Scroll.substate = ScrollState.Substates.Start;
        curMode = GameMode.Scroll;
    }

    void GotoScroll(Direction dir, int currentRoomId)
    {
        GotoScroll(dir);
        State.Scroll.curRoomId = currentRoomId;
    }

    bool CalcMazeStayPut(Direction dir)
    {
        if (!IsOverworld())
            return false;

        bool stayPut = false;
        var maze = sparseRoomAttrs.FindSparseAttr<SparseMaze>(Sparse.Maze, curRoomId);
        if (maze != null)
        {
            if (dir != maze.exitDir)
            {
                if (dir == maze.path[CurMazeStep])
                {
                    CurMazeStep++;
                    if (CurMazeStep == maze.path.Length)
                    {
                        CurMazeStep = 0;
                        Game.Sound.Play(SoundEffect.Secret);
                    }
                    else
                        stayPut = true;
                }
                else
                {
                    CurMazeStep = 0;
                    stayPut = true;
                }
            }
            else
            {
                CurMazeStep = 0;
            }
        }
        return stayPut;
    }

    void UpdateScroll()
    {
        sScrollFuncs[(int)State.Scroll.substate]();
    }

    void UpdateScroll_Start()
    {
        GetWorldCoord(State.Scroll.curRoomId, out var roomRow, out var roomCol);

        Actor.MoveSimple(ref roomCol, ref roomRow, State.Scroll.scrollDir, 1);

        int nextRoomId;
        if (CalcMazeStayPut(State.Scroll.scrollDir))
            nextRoomId = State.Scroll.curRoomId;
        else
            nextRoomId = MakeRoomId(roomRow, roomCol);

        State.Scroll.nextRoomId = nextRoomId;
        State.Scroll.substate = ScrollState.Substates.AnimatingColors;
    }

    void UpdateScroll_AnimatingColors()
    {
        if (CurColorSeqNum == 0)
        {
            State.Scroll.substate = ScrollState.Substates.LoadRoom;
            return;
        }

        if ((Game.GetFrameCounter() & 4) != 0)
        {
            CurColorSeqNum--;

            var colorSeq = extraData.GetItem< ColorSeq>(Extra.PondColors); // TODO
            int color = colorSeq.Colors[CurColorSeqNum];
            Graphics.SetColorIndexed((Palette)3, 3, color);
            Graphics.UpdatePalettes();

            if (CurColorSeqNum == 0)
                State.Scroll.substate = ScrollState.Substates.LoadRoom;
        }
    }

    void UpdateScroll_FadeOut()
    {
        if (State.Scroll.timer == 0)
        {
            for (int i = 0; i < 2; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.DarkPalette(DarkRoomFadeStep, i));
            }
            Graphics.UpdatePalettes();

            DarkRoomFadeStep++;

            if (DarkRoomFadeStep == 4)
            {
                State.Scroll.substate = ScrollState.Substates.Scroll;
                State.Scroll.timer = ScrollState.StateTime;
            }
            else
            {
                State.Scroll.timer = 9;
            }
        }
        else
        {
            State.Scroll.timer--;
        }
    }

    void UpdateScroll_LoadRoom()
    {
        if (State.Scroll.scrollDir == Direction.Down
            && !IsOverworld()
            && curRoomId == infoBlock.StartRoomId)
        {
            GotoLoadLevel(0);
            return;
        }

        State.Scroll.offsetX = 0;
        State.Scroll.offsetY = 0;
        State.Scroll.speedX = 0;
        State.Scroll.speedY = 0;
        State.Scroll.oldMapToNewMapDistX = 0;
        State.Scroll.oldMapToNewMapDistY = 0;

        switch (State.Scroll.scrollDir)
        {
            case Direction.Left:
                State.Scroll.offsetX = -TileMapWidth;
                State.Scroll.speedX = ScrollSpeed;
                State.Scroll.oldMapToNewMapDistX = TileMapWidth;
                break;

            case Direction.Right:
                State.Scroll.offsetX = TileMapWidth;
                State.Scroll.speedX = -ScrollSpeed;
                State.Scroll.oldMapToNewMapDistX = -TileMapWidth;
                break;

            case Direction.Up:
                State.Scroll.offsetY = -TileMapHeight;
                State.Scroll.speedY = ScrollSpeed;
                State.Scroll.oldMapToNewMapDistY = TileMapHeight;
                break;

            case Direction.Down:
                State.Scroll.offsetY = TileMapHeight;
                State.Scroll.speedY = -ScrollSpeed;
                State.Scroll.oldMapToNewMapDistY = -TileMapHeight;
                break;
        }

        State.Scroll.oldRoomId = curRoomId;

        int nextRoomId = State.Scroll.nextRoomId;
        int nextTileMapIndex = (curTileMapIndex + 1) % 2;
        State.Scroll.oldTileMapIndex = curTileMapIndex;

        TempShutterRoomId = nextRoomId;
        TempShutterDoorDir = State.Scroll.scrollDir.GetOppositeDirection();

        LoadRoom(nextRoomId, nextTileMapIndex);

        var uwRoomAttrs = CurrentUWRoomAttrs;
        if (uwRoomAttrs.IsDark() && DarkRoomFadeStep == 0)
        {
            State.Scroll.substate = ScrollState.Substates.FadeOut;
            State.Scroll.timer = 9;
        }
        else
        {
            State.Scroll.substate = ScrollState.Substates.Scroll;
            State.Scroll.timer = ScrollState.StateTime;
        }
    }

    void UpdateScroll_Scroll()
    {
        if (State.Scroll.timer > 0)
        {
            State.Scroll.timer--;
            return;
        }

        if (State.Scroll.offsetX == 0 && State.Scroll.offsetY == 0)
        {
            GotoEnter(State.Scroll.scrollDir);
            if (IsOverworld() && State.Scroll.nextRoomId == 0x0F)
                Game.Sound.Play(SoundEffect.Secret);
            return;
        }

        State.Scroll.offsetX += State.Scroll.speedX;
        State.Scroll.offsetY += State.Scroll.speedY;

        var playerLimits = Link.PlayerLimits;

        if (State.Scroll.speedX != 0)
        {
            int x = Game.Link.X + State.Scroll.speedX;
            if (x < playerLimits[1])
                x = playerLimits[1];
            else if (x > playerLimits[0])
                x = playerLimits[0];
            Game.Link.X = x;
        }
        else
        {
            int y = Game.Link.Y + State.Scroll.speedY;
            if (y < playerLimits[3])
                y = playerLimits[3];
            else if (y > playerLimits[2])
                y = playerLimits[2];
            Game.Link.Y = y;
        }

        Game.Link.Animator.Advance();
    }

    void DrawScroll()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        ClearScreen();

        if (State.Scroll.substate is ScrollState.Substates.Scroll or ScrollState.Substates.FadeOut)
        {
            int oldMapOffsetX = State.Scroll.offsetX + State.Scroll.oldMapToNewMapDistX;
            int oldMapOffsetY = State.Scroll.offsetY + State.Scroll.oldMapToNewMapDistY;

            DrawMap(curRoomId, curTileMapIndex, State.Scroll.offsetX, State.Scroll.offsetY);
            DrawMap(State.Scroll.oldRoomId, State.Scroll.oldTileMapIndex, oldMapOffsetX, oldMapOffsetY);
        }
        else
        {
            DrawMap(curRoomId, curTileMapIndex, 0, 0);
        }

        Graphics.ResetClip();

        if (IsOverworld())
            Game.Link.Draw();
    }

    void GotoLeave(Direction dir)
    {
        if (dir == Direction.None) throw new ArgumentOutOfRangeException(nameof(dir));

        State.Leave.curRoomId = curRoomId;
        State.Leave.scrollDir = dir;
        State.Leave.timer = LeaveState.StateTime;
        curMode = GameMode.Leave;
    }

    void GotoLeave(Direction dir, int currentRoomId)
    {
        GotoLeave(dir);
        State.Leave.curRoomId = currentRoomId;
    }

    void UpdateLeave()
    {
        var playerLimits = Link.PlayerLimits;
        int dirOrd = Game.Link.Facing.GetOrdinal();
        var coord = Game.Link.Facing.IsVertical() ? Game.Link.Y : Game.Link.X;

        if (coord != playerLimits[dirOrd])
        {
            Game.Link.MoveLinear(State.Leave.scrollDir, Link.WalkSpeed);
            Game.Link.Animator.Advance();
            return;
        }

        if (State.Leave.timer == 0)
        {
            Game.Link.Animator.AdvanceFrame();
            GotoScroll(State.Leave.scrollDir, State.Leave.curRoomId);
            return;
        }
        State.Leave.timer--;
    }

    void DrawLeave()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects();
        Graphics.ResetClip();
    }

    void GotoEnter(Direction dir)
    {
        State.Enter.substate = EnterState.Substates.Start;
        State.Enter.scrollDir = dir;
        State.Enter.timer = 0;
        State.Enter.playerPriority = SpritePriority.AboveBg;
        State.Enter.playerSpeed = Link.WalkSpeed;
        State.Enter.gotoPlay = false;
        curMode = GameMode.Enter;
    }

    void MovePlayer(Direction dir, int speed, ref int fraction)
    {
        fraction += speed;
        int carry = fraction >> 8;
        fraction &= 0xFF;

        int x = Game.Link.X;
        int y = Game.Link.Y;
        Actor.MoveSimple(ref x, ref y, dir, carry);

        Game.Link.X = x;
        Game.Link.Y = y;
    }

    void UpdateEnter()
    {
        sEnterFuncs[(int)State.Enter.substate]();

        if (State.Enter.gotoPlay)
        {
            var origShutterDoorDir = TempShutterDoorDir;
            TempShutterDoorDir = Direction.None;
            if (IsUWMain(curRoomId)
                && (origShutterDoorDir != Direction.None)
                && GetDoorType(curRoomId, origShutterDoorDir) == DoorType.Shutter)
            {
                Game.Sound.Play(SoundEffect.Door);
                int doorOrd = origShutterDoorDir.GetOrdinal();
                UpdateDoorTileBehavior(doorOrd);
            }

            statusBar.EnableFeatures(StatusBarFeatures.All, true);
            if (IsOverworld() && fromUnderground != 0)
            {
                Game.Sound.PlaySong(infoBlock.Song, SongStream.MainSong, true);
            }
            GotoPlay();
            return;
        }
        Game.Link.Animator.Advance();
    }

    void UpdateEnter_Start()
    {
        triggeredDoorCmd = 0;
        triggeredDoorDir = Direction.None;

        if (IsOverworld())
        {
            TileBehavior behavior = GetTileBehaviorXY(Game.Link.X, Game.Link.Y + 3);
            if (behavior == TileBehavior.Cave)
            {
                Game.Link.Y = Game.Link.Y + MobTileHeight;
                Game.Link.Facing = Direction.Down;

                State.Enter.playerFraction = 0;
                State.Enter.playerSpeed = 0x40;
                State.Enter.playerPriority = SpritePriority.BelowBg;
                State.Enter.scrollDir = Direction.Up;
                State.Enter.targetX = Game.Link.X;
                State.Enter.targetY = Game.Link.Y - 0x10;
                State.Enter.substate = EnterState.Substates.WalkCave;

                Game.Sound.StopAll();
                Game.Sound.Play(SoundEffect.Stairs);
            }
            else
            {
                State.Enter.substate = EnterState.Substates.Wait;
                State.Enter.timer = EnterState.StateTime;
            }
        }
        else if (State.Enter.scrollDir != Direction.None)
        {
            var uwRoomAttrs = CurrentUWRoomAttrs;
            Direction oppositeDir = State.Enter.scrollDir.GetOppositeDirection();
            int door = oppositeDir.GetOrdinal();
            DoorType doorType = uwRoomAttrs.GetDoor(door);
            int distance;

            if (doorType is DoorType.Shutter or DoorType.Bombable)
                distance = MobTileWidth * 2;
            else
                distance = MobTileWidth;

            State.Enter.targetX = Game.Link.X;
            State.Enter.targetY = Game.Link.Y;
            Actor.MoveSimple(
                ref State.Enter.targetX,
                ref State.Enter.targetY,
                State.Enter.scrollDir,
                distance);

            if (!uwRoomAttrs.IsDark() && DarkRoomFadeStep > 0)
            {
                State.Enter.substate = EnterState.Substates.FadeIn;
                State.Enter.timer = 9;
            }
            else
            {
                State.Enter.substate = EnterState.Substates.Walk;
            }

            Game.Link.Facing = State.Enter.scrollDir;
        }
        else
        {
            State.Enter.substate = EnterState.Substates.Wait;
            State.Enter.timer = EnterState.StateTime;
        }

        if (IsUWMain(curRoomId))
            doorwayDir = State.Enter.scrollDir;
        else
            doorwayDir = Direction.None;
    }

    void UpdateEnter_Wait()
    {
        State.Enter.timer--;
        if (State.Enter.timer == 0)
            State.Enter.gotoPlay = true;
    }

    void UpdateEnter_FadeIn()
    {
        if (DarkRoomFadeStep == 0)
        {
            State.Enter.substate = EnterState.Substates.Walk;
        }
        else
        {
            if (State.Enter.timer == 0)
            {
                DarkRoomFadeStep--;
                State.Enter.timer = 9;

                for (int i = 0; i < 2; i++)
                {
                    Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.DarkPalette(DarkRoomFadeStep, i));
                }
                Graphics.UpdatePalettes();
            }
            else
            {
                State.Enter.timer--;
            }
        }
    }

    void UpdateEnter_Walk()
    {
        if (Game.Link.X == State.Enter.targetX
            && Game.Link.Y == State.Enter.targetY)
        {
            State.Enter.gotoPlay = true;
        }
        else
        {
            Game.Link.MoveLinear(State.Enter.scrollDir, State.Enter.playerSpeed);
        }
    }

    void UpdateEnter_WalkCave()
    {
        if (Game.Link.X == State.Enter.targetX
            && Game.Link.Y == State.Enter.targetY)
        {
            State.Enter.gotoPlay = true;
        }
        else
        {
            MovePlayer(State.Enter.scrollDir, State.Enter.playerSpeed, ref State.Enter.playerFraction);
        }
    }

    void DrawEnter()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.Enter.substate != EnterState.Substates.Start)
            DrawRoomNoObjects(State.Enter.playerPriority);

        Graphics.ResetClip();
    }

    void GotoLoadLevel(int level, bool restartOW = false)
    {
        State.LoadLevel.level = level;
        State.LoadLevel.substate = LoadLevelState.Substates.Load;
        State.LoadLevel.timer = 0;
        State.LoadLevel.restartOW = restartOW;

        curMode = GameMode.LoadLevel;
    }

    void SetPlayerExitPosOW(int roomId)
    {
        int row, col;
        var owRoomAttrs = CurrentOWRoomAttrs;
        var exitRPos = owRoomAttrs.GetExitPosition();

        col = exitRPos & 0xF;
        row = (exitRPos >> 4) + 4;

        Game.Link.X = col * MobTileWidth;
        Game.Link.Y = row * MobTileHeight + 0xD;
    }

    // string GetString(int stringId)
    // {
    //     return sWorld.GetString(stringId);
    // }

    string GetString(int stringId)
    {
        return Encoding.UTF8.GetString(textTable.GetItem(stringId));
    }

    void UpdateLoadLevel()
    {
        if (State.LoadLevel.substate == LoadLevelState.Substates.Load)
        {
            State.LoadLevel.timer = LoadLevelState.StateTime;
            State.LoadLevel.substate = LoadLevelState.Substates.Wait;

            int origLevel = infoBlock.LevelNumber;
            int origRoomId = curRoomId;

            Game.Sound.StopAll();
            StatusBarVisible = false;
            LoadLevel(State.LoadLevel.level);

            // Let the Unfurl game mode load the room and reset colors.

            if (State.LoadLevel.level == 0)
            {
                curRoomId = SavedOWRoomId;
                SavedOWRoomId = -1;
                fromUnderground = 2;
            }
            else
            {
                curRoomId = infoBlock.StartRoomId;
                if (origLevel == 0)
                    SavedOWRoomId = origRoomId;
            }
        }
        else if (State.LoadLevel.substate == LoadLevelState.Substates.Wait)
        {
            if (State.LoadLevel.timer == 0)
            {
                GotoUnfurl(State.LoadLevel.restartOW);
                return;
            }

            State.LoadLevel.timer--;
        }
    }

    void DrawLoadLevel()
    {
        Graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight);
        ClearScreen();
        Graphics.ResetClip();
    }

    void GotoUnfurl(bool restartOW = false)
    {
        State.Unfurl.substate = UnfurlState.Substates.Start;
        State.Unfurl.timer = UnfurlState.StateTime;
        State.Unfurl.stepTimer = 0;
        State.Unfurl.left = 0x80;
        State.Unfurl.right = 0x80;
        State.Unfurl.restartOW = restartOW;

        ClearLevelData();

        curMode = GameMode.Unfurl;
    }

    void UpdateUnfurl()
    {
        if (State.Unfurl.substate == UnfurlState.Substates.Start)
        {
            State.Unfurl.substate = UnfurlState.Substates.Unfurl;
            StatusBarVisible = true;
            statusBar.EnableFeatures(StatusBarFeatures.All, false);

            if (infoBlock.LevelNumber == 0 && !State.Unfurl.restartOW)
            {
                LoadRoom(curRoomId, 0);
                SetPlayerExitPosOW(curRoomId);
            }
            else
            {
                LoadRoom(infoBlock.StartRoomId, 0);
                Game.Link.X = StartX;
                Game.Link.Y = infoBlock.StartY;
            }

            for (int i = 0; i < LevelInfoBlock.LevelPaletteCount; i++)
            {
                Graphics.SetPaletteIndexed((Palette)i, infoBlock.GetPalette(i));
            }

            SetPlayerColor();
            Graphics.UpdatePalettes();
            return;
        }

        if (State.Unfurl.timer > 0)
        {
            State.Unfurl.timer--;
            return;
        }

        if (State.Unfurl.left == 0)
        {
            statusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, true);
            if (!IsOverworld())
                Game.Sound.PlaySong(infoBlock.Song, SongStream.MainSong, true);
            GotoEnter(Direction.Up);
            return;
        }

        if (State.Unfurl.stepTimer == 0)
        {
            State.Unfurl.left -= 8;
            State.Unfurl.right += 8;
            State.Unfurl.stepTimer = 4;
        }
        else
        {
            State.Unfurl.stepTimer--;
        }
    }

    void DrawUnfurl()
    {
        if (State.Unfurl.substate == UnfurlState.Substates.Start)
            return;

        int width = State.Unfurl.right - State.Unfurl.left;

        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        ClearScreen();
        Graphics.ResetClip();

        Graphics.SetClip(State.Unfurl.left, TileMapBaseY, width, TileMapHeight);
        DrawRoomNoObjects(SpritePriority.None);
        Graphics.ResetClip();
    }

    void EndLevel()
    {
        GotoEndLevel();
    }

    void GotoEndLevel()
    {
        State.EndLevel.substate = EndLevelState.Substates.Start;
        curMode = GameMode.EndLevel;
    }

    void UpdateEndLevel()
    {
        sEndLevelFuncs[(int)State.EndLevel.substate]();
    }

    void UpdateEndLevel_Start()
    {
        State.EndLevel.substate = EndLevelState.Substates.Wait1;
        State.EndLevel.timer = EndLevelState.Wait1Time;

        State.EndLevel.left = 0;
        State.EndLevel.right = TileMapWidth;
        State.EndLevel.stepTimer = 4;

        statusBar.EnableFeatures(StatusBarFeatures.Equipment, false);
        Game.Sound.PlaySong(SongId.Triforce, SongStream.MainSong, false);
    }

    void UpdateEndLevel_Wait()
    {
        if (State.EndLevel.timer == 0)
        {
            if (State.EndLevel.substate == EndLevelState.Substates.Wait3)
                GotoLoadLevel(0);
            else
            {
                State.EndLevel.substate += 1;
                if (State.EndLevel.substate == EndLevelState.Substates.Flash)
                    State.EndLevel.timer = EndLevelState.FlashTime;
            }
        }
        else
            State.EndLevel.timer--;
    }

    void UpdateEndLevel_Flash()
    {
        if (State.EndLevel.timer == 0)
            State.EndLevel.substate += 1;
        else
        {
            int step = State.EndLevel.timer & 0x7;
            if (step == 0)
                SetFlashPalette();
            else if (step == 3)
                SetLevelPalette();
            State.EndLevel.timer--;
        }
    }

    void UpdateEndLevel_FillHearts()
    {
        int maxHeartValue = profile.GetMaxHeartsValue();

        Game.Sound.Play(SoundEffect.Character);

        if (profile.Hearts == maxHeartValue)
        {
            State.EndLevel.substate += 1;
            State.EndLevel.timer = EndLevelState.Wait2Time;
        }
        else
        {
            FillHearts(6);
        }
    }

    void UpdateEndLevel_Furl()
    {
        if (State.EndLevel.left == WorldMidX)
        {
            State.EndLevel.substate += 1;
            State.EndLevel.timer = EndLevelState.Wait3Time;
        }
        else if (State.EndLevel.stepTimer == 0)
        {
            State.EndLevel.left += 8;
            State.EndLevel.right -= 8;
            State.EndLevel.stepTimer = 4;
        }
        else
        {
            State.EndLevel.stepTimer--;
        }
    }

    void DrawEndLevel()
    {
        int left = 0;
        int width = TileMapWidth;

        if (State.EndLevel.substate >= EndLevelState.Substates.Furl)
        {
            left = State.EndLevel.left;
            width = State.EndLevel.right - State.EndLevel.left;

            Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
            ClearScreen();
            Graphics.ResetClip();

        }

        Graphics.SetClip(left, TileMapBaseY, width, TileMapHeight);
        DrawRoomNoObjects(SpritePriority.None);
        Graphics.ResetClip();

        DrawLinkLiftingItem(ItemId.TriforcePiece);
    }

    void WinGame()
    {
        GotoWinGame();
    }

    void GotoWinGame()
    {
        State.WinGame.substate = WinGameState.Substates.Start;
        State.WinGame.timer = 162;
        State.WinGame.left = 0;
        State.WinGame.right = TileMapWidth;
        State.WinGame.stepTimer = 0;
        State.WinGame.npcVisual = WinGameState.NpcVisual.Npc_Stand;

        curMode = GameMode.WinGame;
    }

    void UpdateWinGame()
    {
        sWinGameFuncs[(int)State.WinGame.substate]();
    }

    void UpdateWinGame_Start()
    {
        if (State.WinGame.timer > 0)
        {
            State.WinGame.timer--;
        }
        else if (State.WinGame.left == WorldMidX)
        {
            State.WinGame.substate = WinGameState.Substates.Text1;
            statusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, false);

            // A959
            textBox1 = new TextBox("You did a win"); // FIX
        }
        else if (State.WinGame.stepTimer == 0)
        {
            State.WinGame.left += 8;
            State.WinGame.right -= 8;
            State.WinGame.stepTimer = 4;
        }
        else
        {
            State.WinGame.stepTimer--;
        }
    }

    void UpdateWinGame_Text1()
    {
        textBox1.Update();
        if (textBox1.IsDone())
        {
            State.WinGame.substate = WinGameState.Substates.Stand;
            State.WinGame.timer = 76;
        }
    }

    void UpdateWinGame_Stand()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Hold1;
            State.WinGame.timer = 64;
        }
    }

    void UpdateWinGame_Hold1()
    {
        State.WinGame.npcVisual = WinGameState.NpcVisual.Npc_Lift;
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Colors;
            State.WinGame.timer = 127;
        }
    }

    void UpdateWinGame_Colors()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.Hold2;
            State.WinGame.timer = 131;
            Game.Sound.PlaySong(SongId.Ending, SongStream.MainSong, true);
        }
    }

    void UpdateWinGame_Hold2()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            // AB07
//             const static byte str2[] = {
//     0x25, 0x25, 0x25, 0x25, 0x25, 0x25, 0x25, 0x25,
//     0x0f, 0x12, 0x17, 0x0a, 0x15, 0x15, 0x22, 0x28,
//     0xa5, 0x65,
//     0x19, 0x0e, 0x0a, 0x0c, 0x0e, 0x24, 0x1b, 0x0e,
//     0x1d, 0x1e, 0x1b, 0x17, 0x1c, 0x24, 0x1d, 0x18, 0x24, 0x11, 0x22, 0x1b, 0x1e, 0x15, 0x0e, 0x2c,
//     0xa5, 0x65, 0x65, 0x25, 0x25,
//     0x1d, 0x11, 0x12, 0x1c, 0x24, 0x0e, 0x17, 0x0d, 0x1c, 0x24, 0x1d, 0x11, 0x0e, 0x24, 0x1c, 0x1d,
//     0x18, 0x1b, 0x22, 0x2c, 0xe5
//         };

            State.WinGame.substate = WinGameState.Substates.Text2;
            textBox2 = new TextBox("UpdateWinGame_Hold2", 8); // TODO
            textBox2.SetY(WinGameState.TextBox2Top);
        }
    }

    void UpdateWinGame_Text2()
    {
        textBox2.Update();
        if (textBox2.IsDone())
        {
            State.WinGame.substate = WinGameState.Substates.Hold3;
            State.WinGame.timer = 129;
        }
    }

    void UpdateWinGame_Hold3()
    {
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            State.WinGame.substate = WinGameState.Substates.NoObjects;
            State.WinGame.timer = 32;
        }
    }

    void UpdateWinGame_NoObjects()
    {
        State.WinGame.npcVisual = WinGameState.NpcVisual.Npc_None;
        State.WinGame.timer--;
        if (State.WinGame.timer == 0)
        {
            credits = new Credits();
            State.WinGame.substate = WinGameState.Substates.Credits;
        }
    }

    void UpdateWinGame_Credits()
    {
        // TODO TextBox** boxes[] = { &textBox1, &textBox2 };
        // TODO int startYs[] = { TextBox::StartY, WinGameState.Substates.TextBox2Top };
        // TODO
        // TODO for (int i = 0; i < _countof(boxes); i++)
        // TODO {
        // TODO     TextBox* box = *boxes[i];
        // TODO     if (box != null)
        // TODO     {
        // TODO         int textToCreditsY = Credits::StartY - startYs[i];
        // TODO         box.SetY(credits.GetTop() - textToCreditsY);
        // TODO         int bottom = box.Y + box.GetHeight();
        // TODO         if (bottom <= 0)
        // TODO         {
        // TODO             delete box;
        // TODO             *boxes[i] = null;
        // TODO         }
        // TODO     }
        // TODO }
        // TODO
        // TODO credits.Update();
        // TODO if (credits.IsDone())
        // TODO {
        // TODO     if (Input.IsButtonPressing(Button.Start))
        // TODO     {
        // TODO         delete credits;
        // TODO         credits = null;
        // TODO         delete player;
        // TODO         player = null;
        // TODO         DeleteObjects();
        // TODO         SubmenuOffsetY = 0;
        // TODO         statusBarVisible = false;
        // TODO         statusBar.EnableFeatures(StatusBar::Feature_All, true);
        // TODO
        // TODO         byte name[MaxNameLength];
        // TODO         byte nameLength = profile.NameLength;
        // TODO         byte deaths = profile.Deaths;
        // TODO         memcpy(name, profile.Name, nameLength);
        // TODO         memset(&profile, 0, sizeof profile);
        // TODO         memcpy(profile.Name, name, nameLength);
        // TODO         profile.NameLength = nameLength;
        // TODO         profile.Deaths = deaths;
        // TODO         profile.Quest = 1;
        // TODO         profile.Items[ItemSlot.HeartContainers] = DefaultHearts;
        // TODO         profile.Items[ItemSlot.MaxBombs] = DefaultBombs;
        // TODO         SaveFolder::WriteProfile(profileSlot, profile);
        // TODO
        // TODO         Game.Sound.StopAll();
        // TODO         GotoFileMenu();
        // TODO     }
        // TODO }
        // TODO else
        // TODO {
        // TODO     int statusTop = credits.GetTop() - Credits::StartY;
        // TODO     int statusBottom = statusTop + StatusBar::StatusBarHeight;
        // TODO     if (statusBottom > 0)
        // TODO         SubmenuOffsetY = statusTop;
        // TODO     else
        // TODO         SubmenuOffsetY = -StatusBar::StatusBarHeight;
        // TODO }
    }

    void DrawWinGame()
    {
        // TODO ALLEGRO_COLOR backColor;
        // TODO
        // TODO Graphics.SetClip(0, 0, StdViewWidth, StdViewHeight);
        // TODO if (State.WinGame.substate == WinGameState.Substates.Colors)
        // TODO {
        // TODO     int sysColors[] = { 0x0F, 0x2A, 0x16, 0x12 };
        // TODO     int frame = State.WinGame.timer & 3;
        // TODO     int sysColor = sysColors[frame];
        // TODO     ClearScreen(sysColor);
        // TODO     backColor = Graphics.GetSystemColor(sysColor);
        // TODO }
        // TODO else
        // TODO {
        // TODO     ClearScreen();
        // TODO     backColor = al_map_rgb(0, 0, 0);
        // TODO }
        // TODO Graphics.ResetClip();
        // TODO
        // TODO statusBar.Draw(SubmenuOffsetY, backColor);
        // TODO
        // TODO if (State.WinGame.substate == WinGameState.Substates.Start)
        // TODO {
        // TODO     int left = State.WinGame.left;
        // TODO     int width = State.WinGame.right - State.WinGame.left;
        // TODO
        // TODO     Graphics.SetClip(left, TileMapBaseY, width, TileMapHeight);
        // TODO     DrawRoomNoObjects(SpritePriority.None);
        // TODO     Graphics.ResetClip();
        // TODO
        // TODO     Game.Link.Draw();
        // TODO     DrawObjects();
        // TODO }
        // TODO else
        // TODO {
        // TODO     var zelda = objects[ObjectSlot.Monster1];
        // TODO
        // TODO     if (State.WinGame.npcVisual == WinGameState.NpcVisual.Npc_Stand)
        // TODO     {
        // TODO         zelda.Draw();
        // TODO         Game.Link.Draw();
        // TODO     }
        // TODO     else if (State.WinGame.npcVisual == WinGameState.NpcVisual.Npc_Lift)
        // TODO     {
        // TODO         DrawZeldaLiftingTriforce(zelda.X, zelda.Y);
        // TODO         DrawLinkLiftingItem(ItemId.TriforcePiece);
        // TODO     }
        // TODO
        // TODO     if (credits != null)
        // TODO         credits.Draw();
        // TODO     if (textBox1 != null)
        // TODO         textBox1.Draw();
        // TODO     if (textBox2 != null)
        // TODO         textBox2.Draw();
        // TODO }
    }

    void GotoStairs(TileBehavior behavior)
    {
        State.Stairs.substate = StairsState.Substates.Start;
        State.Stairs.tileBehavior = behavior;
        State.Stairs.playerPriority = SpritePriority.AboveBg;

        curMode = GameMode.Stairs;
    }

    void UpdateStairsState()
    {
        if (State.Stairs.substate == StairsState.Substates.Start)
        {
            State.Stairs.playerPriority = SpritePriority.BelowBg;

            if (IsOverworld())
                Game.Sound.StopAll();

            if (State.Stairs.tileBehavior == TileBehavior.Cave)
            {
                Game.Link.Facing = Direction.Up;

                State.Stairs.targetX = Game.Link.X;
                State.Stairs.targetY = Game.Link.Y + 0x10;
                State.Stairs.scrollDir = Direction.Down;
                State.Stairs.playerSpeed = 0x40;
                State.Stairs.playerFraction = 0;

                State.Stairs.substate = StairsState.Substates.WalkCave;
                Game.Sound.Play(SoundEffect.Stairs);
            }
            else
            {
                State.Stairs.substate = StairsState.Substates.Walk;
            }
        }
        else if (State.Stairs.substate == StairsState.Substates.Walk)
        {
            if (IsOverworld())
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                int cave = owRoomAttrs.GetCaveId();

                if (cave <= 9)
                    GotoLoadLevel(cave);
                else
                    GotoPlayCave();
            }
            else
                GotoPlayCellar();
        }
        else if (State.Stairs.substate == StairsState.Substates.WalkCave)
        {
            if (Game.Link.X == State.Stairs.targetX
               && Game.Link.Y == State.Stairs.targetY)
            {
                var owRoomAttrs = CurrentOWRoomAttrs;
                int cave = owRoomAttrs.GetCaveId();

                if (cave <= 9)
                    GotoLoadLevel(cave);
                else
                    GotoPlayCave();
            }
            else
            {
                MovePlayer(State.Stairs.scrollDir, State.Stairs.playerSpeed, ref State.Stairs.playerFraction);
                Game.Link.Animator.Advance();
            }
        }
    }

    void DrawStairsState()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(State.Stairs.playerPriority);
        Graphics.ResetClip();
    }

    void GotoPlayCellar()
    {
        State.PlayCellar.substate = PlayCellarState.Substates.Start;
        State.PlayCellar.playerPriority = SpritePriority.None;

        curMode = GameMode.InitPlayCellar;
    }

    void UpdatePlayCellar()
    {
        sPlayCellarFuncs[(int)State.PlayCellar.substate]();
    }

    void UpdatePlayCellar_Start()
    {
        State.PlayCellar.substate = PlayCellarState.Substates.FadeOut;
        State.PlayCellar.fadeTimer = 11;
        State.PlayCellar.fadeStep = 0;
    }

    void UpdatePlayCellar_FadeOut()
    {
        if (State.PlayCellar.fadeTimer == 0)
        {
            for (int i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                int step = State.PlayCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.OutOfCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.PlayCellar.fadeTimer = 9;
            State.PlayCellar.fadeStep++;

            if (State.PlayCellar.fadeStep == LevelInfoBlock.FadeLength)
                State.PlayCellar.substate = PlayCellarState.Substates.LoadRoom;
        }
        else
        {
            State.PlayCellar.fadeTimer--;
        }
    }

    void UpdatePlayCellar_LoadRoom()
    {
        int roomId = FindCellarRoomId(curRoomId, out var isLeft);

        if (roomId >= 0)
        {
            int x = isLeft ? 0x30 : 0xC0;

            LoadRoom(roomId, 0);

            Game.Link.X = x;
            Game.Link.Y = 0x44;
            Game.Link.Facing = Direction.Down;

            State.PlayCellar.targetY = 0x60;
            State.PlayCellar.substate = PlayCellarState.Substates.FadeIn;
            State.PlayCellar.fadeTimer = 35;
            State.PlayCellar.fadeStep = 3;
        }
        else
        {
            GotoPlay();
        }
    }

    void UpdatePlayCellar_FadeIn()
    {
        if (State.PlayCellar.fadeTimer == 0)
        {
            for (int i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                int step = State.PlayCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.InCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.PlayCellar.fadeTimer = 9;
            State.PlayCellar.fadeStep--;

            if (State.PlayCellar.fadeStep < 0)
                State.PlayCellar.substate = PlayCellarState.Substates.Walk;
        }
        else
        {
            State.PlayCellar.fadeTimer--;
        }
    }

    void UpdatePlayCellar_Walk()
    {
        State.PlayCellar.playerPriority = SpritePriority.AboveBg;

        if (Game.Link.Y == State.PlayCellar.targetY)
        {
            fromUnderground = 1;
            GotoPlay(PlayState.RoomType.Cellar);
        }
        else
        {
            Game.Link.MoveLinear(Direction.Down, Link.WalkSpeed);
            Game.Link.Animator.Advance();
        }
    }

    void DrawPlayCellar()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
        DrawRoomNoObjects(State.PlayCellar.playerPriority);
        Graphics.ResetClip();
    }

    void GotoLeaveCellar()
    {
        State.LeaveCellar.substate = LeaveCellarState.Substates.Start;

        curMode = GameMode.LeaveCellar;
    }

    void UpdateLeaveCellar()
    {
        sLeaveCellarFuncs[(int)State.LeaveCellar.substate]();
    }

    void UpdateLeaveCellar_Start()
    {
        if (IsOverworld())
        {
            State.LeaveCellar.substate = LeaveCellarState.Substates.Wait;
            State.LeaveCellar.timer = 29;
        }
        else
        {
            State.LeaveCellar.substate = LeaveCellarState.Substates.FadeOut;
            State.LeaveCellar.fadeTimer = 11;
            State.LeaveCellar.fadeStep = 0;
        }
    }

    void UpdateLeaveCellar_FadeOut()
    {
        if (State.LeaveCellar.fadeTimer == 0)
        {
            for (int i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                int step = State.LeaveCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.InCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.LeaveCellar.fadeTimer = 9;
            State.LeaveCellar.fadeStep++;

            if (State.LeaveCellar.fadeStep == LevelInfoBlock.FadeLength)
                State.LeaveCellar.substate = LeaveCellarState.Substates.LoadRoom;
        }
        else
        {
            State.LeaveCellar.fadeTimer--;
        }
    }

    void UpdateLeaveCellar_LoadRoom()
    {
        var uwRoomAttrs = CurrentUWRoomAttrs;
        int nextRoomId;

        if (Game.Link.X < 0x80)
            nextRoomId = uwRoomAttrs.GetLeftCellarExitRoomId();
        else
            nextRoomId = uwRoomAttrs.GetRightCellarExitRoomId();

        LoadRoom(nextRoomId, 0);

        Game.Link.X = 0x60;
        Game.Link.Y = 0xA0;
        Game.Link.Facing = Direction.Down;

        State.LeaveCellar.substate = LeaveCellarState.Substates.FadeIn;
        State.LeaveCellar.fadeTimer = 35;
        State.LeaveCellar.fadeStep = 3;
    }

    void UpdateLeaveCellar_FadeIn()
    {
        if (State.LeaveCellar.fadeTimer == 0)
        {
            for (int i = 0; i < LevelInfoBlock.FadePals; i++)
            {
                int step = State.LeaveCellar.fadeStep;
                Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.OutOfCellarPalette(step, i));
            }
            Graphics.UpdatePalettes();
            State.LeaveCellar.fadeTimer = 9;
            State.LeaveCellar.fadeStep--;

            if (State.LeaveCellar.fadeStep < 0)
                State.LeaveCellar.substate = LeaveCellarState.Substates.Walk;
        }
        else
        {
            State.LeaveCellar.fadeTimer--;
        }
    }

    void UpdateLeaveCellar_Walk()
    {
        GotoEnter(Direction.None);
    }

    void UpdateLeaveCellar_Wait()
    {
        if (State.LeaveCellar.timer == 0)
        {
            State.LeaveCellar.substate = LeaveCellarState.Substates.LoadOverworldRoom;
        }
        else
        {
            State.LeaveCellar.timer--;
        }
    }

    void UpdateLeaveCellar_LoadOverworldRoom()
    {
        for (int i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, infoBlock.GetPalette(i + 2));
        }
        Graphics.UpdatePalettes();

        LoadRoom(curRoomId, 0);
        SetPlayerExitPosOW(curRoomId);
        GotoEnter(Direction.None);
        Game.Link.Facing = Direction.Down;
    }

    void DrawLeaveCellar()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.LeaveCellar.substate == LeaveCellarState.Substates.Start)
        {
        }
        else if (State.LeaveCellar.substate is LeaveCellarState.Substates.Wait or LeaveCellarState.Substates.LoadOverworldRoom)
        {
            ClearScreen();
        }
        else
        {
            DrawRoomNoObjects(SpritePriority.None);
        }

        Graphics.ResetClip();
    }

    void GotoPlayCave()
    {
        State.PlayCave.substate = PlayCaveState.Substates.Start;

        curMode = GameMode.InitPlayCave;
    }

    void UpdatePlayCave()
    {
        sPlayCaveFuncs[(int)State.PlayCave.substate]();
    }

    void UpdatePlayCave_Start()
    {
        State.PlayCave.substate = PlayCaveState.Substates.Wait;
        State.PlayCave.timer = 27;
    }

    void UpdatePlayCave_Wait()
    {
        if (State.PlayCave.timer == 0)
            State.PlayCave.substate = PlayCaveState.Substates.LoadRoom;
        else
            State.PlayCave.timer--;
    }

    void UpdatePlayCave_LoadRoom()
    {
        var paletteSet = extraData.GetItem<PaletteSet>(Extra.CavePalettes);
        Cave caveLayout;

        if (FindSparseFlag(Sparse.Shortcut, curRoomId))
            caveLayout = Cave.Shortcut;
        else
            caveLayout = Cave.Items;

        LoadCaveRoom(caveLayout);

        State.PlayCave.substate = PlayCaveState.Substates.Walk;
        State.PlayCave.targetY = 0xD5;

        Game.Link.X = 0x70;
        Game.Link.Y = 0xDD;
        Game.Link.Facing = Direction.Up;

        for (int i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i + 2, paletteSet.GetPalette(i));
        }
        Graphics.UpdatePalettes();
    }

    void UpdatePlayCave_Walk()
    {
        if (Game.Link.Y == State.PlayCave.targetY)
        {
            fromUnderground = 1;
            GotoPlay(PlayState.RoomType.Cave);
        }
        else
        {
            Game.Link.MoveLinear(Direction.Up, Link.WalkSpeed);
            Game.Link.Animator.Advance();
        }
    }

    void DrawPlayCave()
    {
        Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);

        if (State.PlayCave.substate is PlayCaveState.Substates.Wait or PlayCaveState.Substates.LoadRoom)
        {
            ClearScreen();
        }
        else if (State.PlayCave.substate == PlayCaveState.Substates.Walk)
        {
            DrawRoomNoObjects();
        }

        Graphics.ResetClip();
    }

    void GotoDie()
    {
        State.Death.Substate = DeathState.Substates.Start;

        curMode = GameMode.Death;
    }

    void UpdateDie()
    {
        // ORIGINAL: Some of these are handled with object timers.
        if (State.Death.Timer > 0)
            State.Death.Timer--;

        sDeathFuncs[(int)State.Death.Substate]();
    }

    void UpdateDie_Start()
    {
        Game.Link.InvincibilityTimer = 0x10;
        State.Death.Timer = 0x20;
        State.Death.Substate = DeathState.Substates.Flash;
        Game.Sound.StopEffects();
        Game.Sound.PlaySong(SongId.Death, SongStream.MainSong, false);
    }

    void UpdateDie_Flash()
    {
        Game.Link.DecInvincibleTimer();

        if (State.Death.Timer == 0)
        {
            State.Death.Timer = 6;
            State.Death.Substate = DeathState.Substates.Wait1;
        }
    }

    void UpdateDie_Wait1()
    {
        // TODO: the last 2 frames make the whole play area use palette 3.

        if (State.Death.Timer == 0)
        {
            // TODO: Make static.
            var redPals = new[]
            {
                new byte[] {0x0F, 0x17, 0x16, 0x26 },
                new byte[] {0x0F, 0x17, 0x16, 0x26 },
            };

            SetLevelPalettes(redPals);

            State.Death.Step = 16;
            State.Death.Timer = 0;
            State.Death.Substate = DeathState.Substates.Turn;
        }
    }

    void UpdateDie_Turn()
    {
        if (State.Death.Step == 0)
        {
            State.Death.Step = 4;
            State.Death.Timer = 0;
            State.Death.Substate = DeathState.Substates.Fade;
        }
        else
        {
            if (State.Death.Timer == 0)
            {
                State.Death.Timer = 5;
                State.Death.Step--;

                var dirs = new[]
                {
                    Direction.Down,
                    Direction.Left,
                    Direction.Up,
                    Direction.Right
                };

                Direction dir = dirs[State.Death.Step & 3];
                Game.Link.Facing = dir;
            }
        }
    }

    void UpdateDie_Fade()
    {
        if (State.Death.Step == 0)
        {
            State.Death.Substate = DeathState.Substates.GrayLink;
        }
        else
        {
            if (State.Death.Timer == 0)
            {
                State.Death.Timer = 10;
                State.Death.Step--;

                int seq = 3 - State.Death.Step;

                SetLevelPalettes(infoBlock.DeathPalettes(seq));
            }
        }
    }

    void UpdateDie_GrayLink()
    {
        // static const byte grayPal[4] = { 0, 0x10, 0x30, 0 };

        Graphics.SetPaletteIndexed(Palette.Player, new byte[] { 0, 0x10, 0x30, 0 });
        Graphics.UpdatePalettes();

        State.Death.Substate = DeathState.Substates.Spark;
        State.Death.Timer = 0x18;
        State.Death.Step = 0;
    }

    void UpdateDie_Spark()
    {
        if (State.Death.Timer == 0)
        {
            if (State.Death.Step == 0)
            {
                State.Death.Timer = 10;
                Game.Sound.Play(SoundEffect.Character);
            }
            else if (State.Death.Step == 1)
            {
                State.Death.Timer = 4;
            }
            else
            {
                State.Death.Substate = DeathState.Substates.Wait2;
                State.Death.Timer = 46;
            }
            State.Death.Step++;
        }
    }

    void UpdateDie_Wait2()
    {
        if (State.Death.Timer == 0)
        {
            State.Death.Substate = DeathState.Substates.GameOver;
            State.Death.Timer = 0x60;
        }
    }

    void UpdateDie_GameOver()
    {
        if (State.Death.Timer == 0)
        {
            profile.Deaths++;
            GotoContinueQuestion();
        }
    }

    void DrawDie()
    {
        if (State.Death.Substate < DeathState.Substates.GameOver)
        {
            Graphics.SetClip(0, TileMapBaseY, TileMapWidth, TileMapHeight);
            DrawRoomNoObjects(SpritePriority.None);
            Graphics.ResetClip();
            var player = Game.Link;

            if (State.Death.Substate == DeathState.Substates.Spark && State.Death.Step > 0)
            {
                GlobalFunctions.DrawSparkle(player.X, player.Y, Palette.BlueForeground, State.Death.Step - 1);
            }
            else if (State.Death.Substate <= DeathState.Substates.Spark)
            {
                Game.Link.Draw();
            }
        }
        else
        {
            // static const byte GameOver[] = { 0x10, 0x0A, 0x16, 0x0E, 0x24, 0x18, 0x1F, 0x0E, 0x1B };
            // DrawString(GameOver, sizeof GameOver, 0x60, 0x90, 0);
            GlobalFunctions.DrawString("Game Over", 0x60, 0x90, 0); // ????
        }
    }

    void GotoContinueQuestion()
    {
        State.Continue.Substate = ContinueState.Substates.Start;
        State.Continue.SelectedIndex = 0;

        curMode = GameMode.ContinueQuestion;
    }

    void UpdateContinueQuestion()
    {
        if (State.Continue.Substate == ContinueState.Substates.Start)
        {
            StatusBarVisible = false;
            Game.Sound.PlaySong(SongId.GameOver, SongStream.MainSong, true);
            State.Continue.Substate = ContinueState.Substates.Idle;
        }
        else if (State.Continue.Substate == ContinueState.Substates.Idle)
        {
            if (Input.IsButtonPressing(Button.Select))
            {
                State.Continue.SelectedIndex++;
                if (State.Continue.SelectedIndex == 3)
                    State.Continue.SelectedIndex = 0;
            }
            else if (Input.IsButtonPressing(Button.Start))
            {
                State.Continue.Substate = ContinueState.Substates.Chosen;
                State.Continue.Timer = 0x40;
            }
        }
        else if (State.Continue.Substate == ContinueState.Substates.Chosen)
        {
            if (State.Continue.Timer == 0)
            {
                StatusBarVisible = true;
                Game.Sound.StopAll();

                if (State.Continue.SelectedIndex == 0)
                {
                    // So, that the OW song is played in the Enter mode.
                    fromUnderground = 2;
                    Game.Link = new Link(Game);
                    profile.Hearts = PlayerProfile.GetMaxHeartsValue(PlayerProfile.DefaultHearts);
                    GotoUnfurl(true);
                }
                else if (State.Continue.SelectedIndex == 1)
                {
                    SaveFolder.WriteProfile(profileSlot, profile);
                    GotoFileMenu();
                }
                else if (State.Continue.SelectedIndex == 2)
                {
                    GotoFileMenu();
                }
            }
            else
            {
                State.Continue.Timer--;
            }
        }
    }

    void DrawContinueQuestion()
    {
        // byte Continue[] = { 0x0C, 0x18, 0x17, 0x1D, 0x12, 0x17, 0x1E, 0x0E };
        // byte Save[] = { 0x1C, 0x0A, 0x1F, 0x0E, 0x24, 0x24, 0x24, 0x24 };
        // byte Retry[] = { 0x1B, 0x0E, 0x1D, 0x1B, 0x22, 0x24, 0x24, 0x24 };
        // byte* Strs[] = { Continue, Save, Retry };
        var strs = new[] { "Continue", "Save", "Retry" };

        ClearScreen();

        int y = 0x50;

        for (int i = 0; i < 3; i++, y += 24)
        {
            int pal = 0;
            if (State.Continue.Substate == ContinueState.Substates.Chosen
                && State.Continue.SelectedIndex == i)
            {
                pal = (Game.GetFrameCounter() / 4) & 1;
            }

            GlobalFunctions.DrawString(strs[i], 0x50, y, (Palette)pal);
        }

        y = 0x50 + (State.Continue.SelectedIndex * 24);
        GlobalFunctions.DrawChar(Char.FullHeart, 0x40, y, Palette.RedFgPalette);
    }

    void GotoFileMenu()
    {
        var summaries = new ProfileSummarySnapshot();
        SaveFolder.ReadSummaries(summaries);
        GotoFileMenu(summaries);
    }

    void GotoFileMenu(ProfileSummarySnapshot summaries)
    {
        nextGameMenu = new GameMenu(Game, summaries);
        curMode = GameMode.GameMenu;
    }

    void GotoRegisterMenu(ProfileSummarySnapshot summaries)
    {
        nextGameMenu = new RegisterMenu(Game, summaries);
        curMode = GameMode.Register;
    }

    void GotoEliminateMenu(ProfileSummarySnapshot summaries)
    {
        nextGameMenu = new EliminateMenu(Game, summaries);
        curMode = GameMode.Elimination;
    }

    void UpdateGameMenu() => gameMenu.Update();
    void UpdateRegisterMenu() => gameMenu.Update();
    void UpdateEliminateMenu() => gameMenu.Update();
    void DrawGameMenu() => gameMenu?.Draw();

    int FindCellarRoomId(int mainRoomId, out bool isLeft)
    {
        isLeft = false;
        for (var i = 0; i < LevelInfoBlock.LevelCellarCount; i++)
        {
            var cellarRoomId = infoBlock.CellarRoomIds[i];

            if (cellarRoomId >= 0x80)
                break;

            var uwRoomAttrs = CurrentUWRoomAttrs;
            if (mainRoomId == uwRoomAttrs.GetLeftCellarExitRoomId())
            {
                isLeft = true;
                return cellarRoomId;
            }
            else if (mainRoomId == uwRoomAttrs.GetRightCellarExitRoomId())
            {
                isLeft = false;
                return cellarRoomId;
            }
        }

        return -1;
    }

    void DrawRoomNoObjects(SpritePriority playerPriority = SpritePriority.AboveBg)
    {
        ClearScreen();

        if (playerPriority == SpritePriority.BelowBg)
            Game.Link.Draw();

        DrawRoom();

        if (playerPriority == SpritePriority.AboveBg)
            Game.Link.Draw();

        if (IsUWMain(curRoomId))
            DrawDoors(curRoomId, true, 0, 0);
    }

    void NoneTileAction(int row, int col, TileInteraction interaction)
    {
        // Nothing to do. Should never be called.
    }

    void PushTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        var rock = new RockObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(rock);
    }

    void BombTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        if (GotSecret())
        {
            SetMob(row, col, BlockObjType.Mob_Cave);
        }
        else
        {
            var rockWall = new RockWallActor(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
            SetBlockObj(rockWall);
        }
    }

    void BurnTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        if (GotSecret())
        {
            SetMob(row, col, BlockObjType.Mob_Stairs);
        }
        else
        {
            var tree = new TreeActor(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
            SetBlockObj(tree);
        }
    }

    void HeadstoneTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        var headstone = new HeadstoneObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(headstone);
    }

    void LadderTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Touch)
            return;

        Console.WriteLine("Touch water: {0}, {1}", row, col);
    }

    void RaftTileAction(int row, int col, TileInteraction interaction)
    {
        // TODO: instantiate the Dock here on Load interaction, and set its position.

        if (interaction != TileInteraction.Cover)
            return;

        Console.WriteLine("Cover dock: {0}, {1}", row, col);

        if (Game.GetItem(ItemSlot.Raft) == 0)
            return;
        if (!FindSparseFlag(Sparse.Dock, curRoomId))
            return;
    }

    void CaveTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover)
            return;

        if (IsOverworld())
        {
            TileBehavior behavior = GetTileBehavior(row, col);
            GotoStairs(behavior);
        }

        Console.WriteLine("Cover cave: {0}, {1}", row, col);
    }

    void StairsTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Cover)
            return;

        if (GetMode() == GameMode.Play)
            GotoStairs(TileBehavior.Stairs);

        Console.WriteLine("Cover stairs: {0}, {1}", row, col);
    }

    public void GhostTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Console.WriteLine("Push headstone: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.FlyingGhini, row, col, interaction, ref ghostCount, ghostCells);
    }

    public void ArmosTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction == TileInteraction.Push) Console.WriteLine("Push armos: {0}, {1}", row, col);

        CommonMakeObjectAction(ObjType.Armos, row, col, interaction, ref armosCount, armosCells);
    }

    public void CommonMakeObjectAction(
        ObjType type, int row, int col, TileInteraction interaction, ref int patchCount, Cell[] patchCells)
    {
        if (interaction == TileInteraction.Load)
        {
            if (patchCount < 16)
            {
                patchCells[patchCount] = new Cell((byte)row, (byte)col);
                patchCount++;
            }
        }
        else if (interaction == TileInteraction.Push)
        {
            var map = tileMaps[curTileMapIndex];
            int behavior = map.Behaviors(row, col);

            if (row > 0 && map.Behaviors(row - 1, col) == behavior)
            {
                row--;
            }
            if (col > 0 && map.Behaviors(row, col - 1) == behavior)
            {
                col--;
            }

            MakeActivatedObject(type, row, col);
        }
    }

    public void MakeActivatedObject(ObjType type, int row, int col)
    {
        row += BaseRows;

        int x = col * TileWidth;
        int y = row * TileHeight;

        for (int i = (int)ObjectSlot.LastMonster; i >= 0; i--)
        {
            var obj = objects[i];
            if (obj == null || obj.ObjType != type)
                continue;

            int objCol = obj.X / TileWidth;
            int objRow = obj.Y / TileHeight;

            if (objCol == col && objRow == row)
                return;
        }

        var freeSlot = FindEmptyMonsterSlot();
        if (freeSlot >= 0)
        {
            var obj = Actor.FromType(type, Game, x, y, false);
            objects[(int)freeSlot] = obj;
            obj.ObjTimer = 0x40;
        }
    }

    public void BlockTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Load)
            return;

        var block = new BlockObj(Game, col * TileWidth, TileMapBaseY + row * TileHeight);
        SetBlockObj(block);
    }

    public void DoorTileAction(int row, int col, TileInteraction interaction)
    {
        if (interaction != TileInteraction.Push)
            return;

        // Based on $91D6 and old implementation Player::CheckDoor.

        Console.WriteLine("Push door: {0}, {1}", row, col);
        var player = Player;

        DoorType doorType = GetDoorType(player.MovingDirection);

        switch (doorType)
        {
            case DoorType.FalseWall:
            case DoorType.FalseWall2:
                if (player.ObjTimer == 0)
                {
                    player.ObjTimer = 0x18;
                }
                else if (player.ObjTimer == 1)
                {
                    LeaveRoom(player.Facing, curRoomId);
                    player.Stop();
                }
                break;

            case DoorType.Bombable:
                if (GetEffectiveDoorState(player.MovingDirection))
                {
                    LeaveRoom(player.Facing, curRoomId);
                    player.Stop();
                }
                break;

            case DoorType.Key:
            case DoorType.Key2:
                if (triggeredDoorDir == Direction.None)
                {
                    bool canOpen = false;

                    if (Game.GetItem(ItemSlot.MagicKey) != 0)
                    {
                        canOpen = true;
                    }
                    else if (Game.GetItem(ItemSlot.Keys) != 0)
                    {
                        canOpen = true;
                        Game.DecrementItem(ItemSlot.Keys);
                    }

                    if (canOpen)
                    {
                        // $8ADA
                        triggeredDoorDir = player.MovingDirection;
                        triggeredDoorCmd = 8;
                    }
                }
                break;
        }
    }

}
