using System.Runtime.InteropServices;

namespace z1;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RoomAttrs
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
    public readonly int GetMonsterCount() => (PalettesAndMonsterCount >> 4) & 0xF;

    public static implicit operator OWRoomAttrs(RoomAttrs b) => new(b);
    public static implicit operator UWRoomAttrs(RoomAttrs b) => new(b);
}

internal readonly record struct OWRoomAttrs(RoomAttrs Attrs)
{
    public byte GetExitPosition() => Attrs.A;
    public int GetCaveId() => Attrs.B & 0x3F;
    public int GetShortcutStairsIndex() => (Attrs.C & 0x03);
    public bool HasZora() => (Attrs.C & 0x04) != 0;
    public bool MonstersEnter() => (Attrs.C & 0x08) != 0;
    public bool HasAmbientSound() => (Attrs.C & 0x10) != 0;
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

internal readonly record struct UWRoomAttrs(RoomAttrs Attrs)
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
    public World.Secret GetSecret() => (World.Secret)(Attrs.D & 7);
    public bool HasBlock() => (Attrs.D & 0x08) != 0;
    public bool IsDark() => (Attrs.D & 0x10) != 0;
    public int GetAmbientSound() => (Attrs.D >> 5) & 3;

    public int GetUniqueRoomId() => Attrs.GetUniqueRoomId();
    public Palette GetOuterPalette() => Attrs.GetOuterPalette();
    public Palette GetInnerPalette() => Attrs.GetInnerPalette();
    public int GetMonsterCount() => Attrs.GetMonsterCount();
}