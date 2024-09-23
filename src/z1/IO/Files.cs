using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace z1.IO;

internal readonly struct TableResource<T>
    where T : struct
{
    public readonly Asset Asset; // for debugging purposes.
    public readonly int Length;
    public readonly short[] Offsets;
    public readonly byte[] Heap;

    public TableResource(Asset asset, int length, short[] offsets, byte[] heap)
    {
        Asset = asset;
        Length = length;
        Offsets = offsets;
        Heap = heap;
    }

    public static TableResource<T> Load(string file) => Load(new Asset(file));
    public static TableResource<T> Load(Asset file)
    {
        Span<byte> bytes = file.ReadAllBytes();

        var length = BitConverter.ToInt16(bytes);
        bytes = bytes[sizeof(short)..];

        var offsetLength = length * sizeof(short);
        var offsets = MemoryMarshal.Cast<byte, short>(bytes[..offsetLength]);

        var heap = bytes[offsetLength..];
        return new TableResource<T>(file, length, offsets.ToArray(), heap.ToArray());
    }

    public ReadOnlySpan<T> GetItem(int index)
    {
        if (index >= Length) return null;
        if (Heap == null || index >= Offsets.Length) throw new Exception();

        var offset = Offsets[index];
        if (Heap.Length <= offset) throw new Exception();

        return MemoryMarshal.Cast<byte, T>(Heap.AsSpan()[offset..]);
    }

    // JOE: TODO: Make this use LoadVariableLengthData?
    public ReadOnlySpan<T> ReadLengthPrefixedItem(int index)
    {
        var offset = Offsets[index];
        var offsetHeap = Heap.AsSpan()[offset..];
        var length = MemoryMarshal.Read<int>(offsetHeap);
        return MemoryMarshal.Cast<byte, T>(offsetHeap[sizeof(int)..length]);
    }

    // JOE: TODO: Put this into its own class so `ILoadVariableLengthData` does not need to return object.
    public TAs LoadVariableLengthData<TAs>(int index)
    {
        var offset = Offsets[index];
        if (Heap.Length <= offset) throw new Exception();

        var localHeap = Heap.AsSpan()[offset..];
        var item = MemoryMarshal.Read<T>(localHeap);

        if (item is not ILoadVariableLengthData<TAs> more) throw new ArgumentOutOfRangeException();
        return more.LoadVariableData(localHeap[Unsafe.SizeOf<T>()..]);
    }

    public TAs LoadVariableLengthData<TIn, TAs>(int index)
        where TIn : struct, ILoadVariableLengthData<TAs>
    {
        var offset = Offsets[index];
        if (Heap.Length <= offset) throw new Exception();

        var localHeap = Heap.AsSpan()[offset..];
        var item = MemoryMarshal.Read<TIn>(localHeap);

        return item.LoadVariableData(localHeap[Unsafe.SizeOf<TIn>()..]);
    }

    public TAs GetItem<TAs>(Extra extra) where TAs : struct => GetItem<TAs>((int)extra);
    public ReadOnlySpan<TAs> GetItems<TAs>(Extra extra) where TAs : struct => GetItems<TAs>((int)extra);
    public TAs GetItem<TAs>(Sparse extra) where TAs : struct => GetItem<TAs>((int)extra);
    public ReadOnlySpan<TAs> GetItems<TAs>(Sparse extra) where TAs : struct => GetItems<TAs>((int)extra);

    public ReadOnlySpan<TAs> GetItems<TAs>(int index) where TAs : struct => MemoryMarshal.Cast<T, TAs>(GetItem(index));
    public TAs GetItem<TAs>(int index) where TAs : struct => GetItems<TAs>(index)[0];

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

internal readonly struct ListResource<T>
    where T : struct
{
    public T this[int i]
    {
        get => _backing[i];
        set => _backing[i] = value;
    }

    private readonly T[] _backing;

    public ListResource(T[] backing)
    {
        _backing = backing;
    }

    public static ListResource<T> Load(string file) => Load(new Asset(file));
    public static ListResource<T> Load(Asset file)
    {
        Span<byte> bytes = file.ReadAllBytes();
        var length = BitConverter.ToInt16(bytes);
        bytes = bytes[sizeof(short)..];
        if (bytes.Length != length) throw new InvalidOperationException();
        return new ListResource<T>(MemoryMarshal.Cast<byte, T>(bytes).ToArray());
    }

    public static ReadOnlySpan<T> LoadList(string file, int amount) => LoadList(new Asset(file), amount);
    public static ReadOnlySpan<T> LoadList(Asset file, int amount)
    {
        Span<byte> bytes = file.ReadAllBytes();
        return MemoryMarshal.Cast<byte, T>(bytes)[..amount];
    }

    public static T LoadSingle(string file) => LoadSingle(new Asset(file));
    public static T LoadSingle(Asset file) => LoadList(file, 1)[0];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct RoomCols
{
    public fixed byte ColumnDesc[World.ScreenBlockWidth];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct HPAttr
{
    public byte Data;

    public readonly int GetHP(int type)
    {
        return (type & 1) switch
        {
            0 => Data & 0xF0,
            _ => Data << 4
        };
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparsePos
{
    public byte roomId;
    public byte pos;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparsePos2
{
    public byte roomId;
    public byte x;
    public byte y;
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
    public const int PaletteLength = Global.PaletteLength;

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

    public readonly SongId SongId => (SongId)Song;

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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ObjectAttr
{
    [Flags]
    private enum Type
    {
        None = 0,
        CustomCollision = 1,
        CustomDraw = 4,
        Unknown10__ = 0x10,
        InvincibleToWeapons = 0x20,
        HalfWidth = 0x40,
        Unknown80__ = 0x80,
        WorldCollision = 0x100,
    }

    public short Data;

    private readonly Type Typed => (Type)Data;

    public readonly bool GetCustomCollision() => Typed.HasFlag(Type.CustomCollision);
    public readonly bool GetUnknown10__() => Typed.HasFlag(Type.Unknown10__);
    public readonly bool GetInvincibleToWeapons() => Typed.HasFlag(Type.InvincibleToWeapons);
    public readonly bool GetHalfWidth() => Typed.HasFlag(Type.HalfWidth);
    public readonly bool GetUnknown80__() => Typed.HasFlag(Type.Unknown80__);
    public readonly bool GetWorldCollision() => Typed.HasFlag(Type.WorldCollision);
    public readonly int GetItemDropClass() => Data >> 9 & 7;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparseRoomAttr
{
    public readonly byte roomId;
    public readonly RoomAttrs attrs;
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SparseRoomItem
{
    public readonly byte roomId;
    public readonly byte x;
    public readonly byte y;
    public readonly byte itemId;

    public readonly ItemId AsItemId => (ItemId)itemId;
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct LevelPersonStrings
{
    public fixed byte StringIds[World.LevelGroups * (int)ObjType.PersonTypes];

    public ReadOnlySpan<byte> GetStringIds(int levelTableIndex)
    {
        fixed (byte* p = StringIds)
        {
            return new ReadOnlySpan<byte>(p + levelTableIndex * (int)ObjType.PersonTypes, (int)ObjType.PersonTypes);
        }
    }
}