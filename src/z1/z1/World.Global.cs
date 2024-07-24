using SkiaSharp;
using z1.Actors;

namespace z1;

internal partial class World
{
    void GetWorldCoord(int roomId, out int row, out int col )
    {
        row = (roomId & 0xF0) >> 4;
        col = roomId & 0x0F;
    }

    int MakeRoomId(int row, int col)
    {
        return (row << 4) | col;
    }

    int GetNextRoomId(int curRoomId, Direction dir)
    {
        GetWorldCoord(curRoomId, out var row, out var col);

        switch (dir)
        {
            case Direction.Left:
                if (col == 0)
                    return curRoomId;
                col--;
                break;

            case Direction.Right:
                if (col == WorldWidth - 1)
                    return curRoomId;
                col++;
                break;

            case Direction.Up:
                if (row == 0)
                    return curRoomId;
                row--;
                break;

            case Direction.Down:
                if (row == WorldHeight - 1)
                    return curRoomId;
                row++;
                break;
        }

        var nextRoomId = MakeRoomId(row, col);
        return nextRoomId;
    }

    static void GetRoomCoord(int position, out int row, out int col)
    {
        row = position & 0x0F;
        col = (position & 0xF0) >> 4;
        row -= 4;
    }

    static void GetRSpotCoord(int position, ref int x, ref int y)
    {
        x = (position & 0x0F) << 4;
        y = (position & 0xF0) | 0xD;
    }

    Point GetRoomItemPosition(byte position)
    {
        return new(position & 0xF0, (byte)(position << 4));
    }

    int GetDoorStateFace(DoorType type, bool state)
    {
        if (state)
            return doorFaces[(int)type].Open;
        return doorFaces[(int)type].Closed;
    }

    void ClearScreen()
    {
        Graphics.Clear(SKColors.Black);
    }

    void ClearScreen(int sysColor)
    {
        Graphics.Clear(Graphics.GetSystemColor(sysColor));
    }

    void ClearDeadObjectQueue()
    {
        for (var i = 0; i < ObjectsToDeleteCount; i++)
        {
            ObjectsToDelete[i] = null;
        }

        ObjectsToDeleteCount = 0;
    }

    void SetOnlyObject(ObjectSlot slot, Actor? obj)
    {
        // TODO assert(slot >= 0 && slot < (int)ObjectSlot.MaxObjects);
        if (Objects[(int)slot] != null)
        {
            if (ObjectsToDeleteCount == (int)ObjectSlot.MaxObjects)
                ClearDeadObjectQueue();
            ObjectsToDelete[ObjectsToDeleteCount] = Objects[(int)slot];
            ObjectsToDeleteCount++;
        }
        Objects[(int)slot] = obj;
    }

    LadderActor? GetLadderObj()
    {
        return GetObject(ObjectSlot.Ladder) as LadderActor;
    }

    void SetLadderObj(LadderActor? ladder)
    {
        SetOnlyObject(ObjectSlot.Ladder, ladder);
    }

    void SetBlockObj(Actor block)
    {
        SetOnlyObject(ObjectSlot.Block, block);
    }

    void DeleteObjects()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            Objects[i] = null;
        }

        ClearDeadObjectQueue();
    }

    void CleanUpRoomItems()
    {
        DeleteObjects();
        SetItem(ItemSlot.Clock, 0);
    }

    void DeleteDeadObjects()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            var obj = Objects[i];
            if (obj != null && obj.IsDeleted)
            {
                Objects[i] = null;
            }
        }

        ClearDeadObjectQueue();
    }

    void InitObjectTimers()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            ObjectTimers[i] = 0;
        }
    }

    void DecrementObjectTimers()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            if (ObjectTimers[i] != 0)
                ObjectTimers[i]--;

            Objects[i]?.DecrementObjectTimer();
        }

        // ORIGINAL: Here the player isn't part of the array, but in the original it's the first element.
        Game.Link.DecrementObjectTimer();
    }

    void InitStunTimers()
    {
        LongTimer = 0;
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            StunTimers[i] = 0;
        }
    }

    void DecrementStunTimers()
    {
        if (LongTimer > 0)
        {
            LongTimer--;
            return;
        }

        LongTimer = 9;

        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            if (StunTimers[i] != 0)
                StunTimers[i]--;

            Objects[i]?.DecrementStunTimer();
        }

        // ORIGINAL: Here the player isn't part of the array, but in the original it's the first element.
        Game.Link.DecrementStunTimer();
    }

    void InitPlaceholderTypes()
    {
        Array.Clear(PlaceholderTypes);
    }

    public ObjectSlot FindEmptyMonsterSlot()
    {
        for (var i = (int)ObjectSlot.LastMonster; i >= 0; i--)
        {
            if (Objects[i] == null) return (ObjectSlot)i;
        }
        return ObjectSlot.NoneFound;
    }

    public bool TryFindEmptyMonsterSlot(out ObjectSlot slot)
    {
        slot = FindEmptyMonsterSlot();
        return slot != ObjectSlot.NoneFound;
    }

    void ClearRoomItemData()
    {
        RecorderUsed = 0;
        CandleUsed = false;
        SummonedWhirlwind = false;
        ShuttersPassedDirs = Direction.None;
        BrightenRoom = false;
        ActiveShots = 0;
    }

    void SetPlayerColor()
    {
        static ReadOnlySpan<byte> palette() => new byte[] { 0x29, 0x32, 0x16 };

        var value = Profile.Items[ItemSlot.Ring];
        Graphics.SetColorIndexed(Palette.Player, 1, palette()[value]);
    }

    void SetFlashPalette()
    {
        static ReadOnlySpan<byte> palette() => new byte[] { 0x0F, 0x30, 0x30, 0x30 };

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, palette());
        }

        Graphics.UpdatePalettes();
    }

    void SetLevelPalettes(byte[][] palettes) // const byte palettes[2][PaletteLength] )
    {
        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)2 + i, palettes[i]);
        }

        Graphics.UpdatePalettes();
    }

    void SetLevelPalette()
    {
        var infoBlock = GetLevelInfo();

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, infoBlock.GetPalette(i));
        }

        Graphics.UpdatePalettes();
    }

    void SetLevelFgPalette()
    {
        var infoBlock = GetLevelInfo();
        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, infoBlock.GetPalette((int)Palette.LevelFgPalette));
    }
}
