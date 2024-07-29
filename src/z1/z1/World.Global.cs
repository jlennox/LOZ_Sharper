using System.Diagnostics;
using SkiaSharp;
using z1.Actors;

namespace z1;

internal partial class World
{
    private static void GetWorldCoord(int roomId, out int row, out int col)
    {
        row = (roomId & 0xF0) >> 4;
        col = roomId & 0x0F;
    }

    private static int MakeRoomId(int row, int col)
    {
        return (row << 4) | col;
    }

    private static int GetNextRoomId(int curRoomId, Direction dir)
    {
        GetWorldCoord(curRoomId, out var row, out var col);

        switch (dir)
        {
            case Direction.Left:
                if (col == 0) return curRoomId;
                col--;
                break;
            case Direction.Right:
                if (col == WorldWidth - 1) return curRoomId;
                col++;
                break;
            case Direction.Up:
                if (row == 0) return curRoomId;
                row--;
                break;
            case Direction.Down:
                if (row == WorldHeight - 1) return curRoomId;
                row++;
                break;
        }

        var nextRoomId = MakeRoomId(row, col);
        return nextRoomId;
    }

    private static void GetRoomCoord(int position, out int row, out int col)
    {
        row = position & 0x0F;
        col = (position & 0xF0) >> 4;
        row -= 4;
    }

    private static void GetRSpotCoord(int position, ref int x, ref int y)
    {
        x = (position & 0x0F) << 4;
        y = (position & 0xF0) | 0xD;
    }

    private static Point GetRoomItemPosition(byte position)
    {
        return new Point(position & 0xF0, (byte)(position << 4));
    }

    private static int GetDoorStateFace(DoorType type, bool state)
    {
        var doorface = doorFaces[(int)type];
        return state ? doorface.Open : doorface.Closed;
    }

    private static void ClearScreen()
    {
        Graphics.Clear(SKColors.Black);
    }

    private static void ClearScreen(int sysColor)
    {
        Graphics.Clear(Graphics.GetSystemColor(sysColor));
    }

    private void ClearDeadObjectQueue()
    {
        for (var i = 0; i < _objectsToDeleteCount; i++)
        {
            _objectsToDelete[i] = null;
        }

        _objectsToDeleteCount = 0;
    }

    private void SetOnlyObject(ObjectSlot slot, Actor? obj)
    {
        Debug.Assert(slot >= 0 && slot < ObjectSlot.MaxObjects);

        if (_objects[(int)slot] != null)
        {
            if (_objectsToDeleteCount == (int)ObjectSlot.MaxObjects)
            {
                ClearDeadObjectQueue();
            }
            _objectsToDelete[_objectsToDeleteCount] = _objects[(int)slot];
            _objectsToDeleteCount++;
        }
        _objects[(int)slot] = obj;
    }

    private LadderActor? GetLadderObj()
    {
        return GetObject(ObjectSlot.Ladder) as LadderActor;
    }

    private void SetLadderObj(LadderActor? ladder)
    {
        SetOnlyObject(ObjectSlot.Ladder, ladder);
    }

    private void SetBlockObj(Actor block)
    {
        SetOnlyObject(ObjectSlot.Block, block);
    }

    private void DeleteObjects()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            _objects[i] = null;
        }

        ClearDeadObjectQueue();
    }

    private void CleanUpRoomItems()
    {
        DeleteObjects();
        SetItem(ItemSlot.Clock, 0);
    }

    private void DeleteDeadObjects()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            var obj = _objects[i];
            if (obj != null && obj.IsDeleted)
            {
                _objects[i] = null;
            }
        }

        ClearDeadObjectQueue();
    }

    private void InitObjectTimers()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            _objectTimers[i] = 0;
        }
    }

    private void DecrementObjectTimers()
    {
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            if (_objectTimers[i] != 0)
            {
                _objectTimers[i]--;
            }

            _objects[i]?.DecrementObjectTimer();
        }

        // ORIGINAL: Here the player isn't part of the array, but in the original it's the first element.
        Game.Link.DecrementObjectTimer();
    }

    private void InitStunTimers()
    {
        _longTimer = 0;
        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            _stunTimers[i] = 0;
        }
    }

    private void DecrementStunTimers()
    {
        if (_longTimer > 0)
        {
            _longTimer--;
            return;
        }

        _longTimer = 9;

        for (var i = 0; i < (int)ObjectSlot.MaxObjects; i++)
        {
            if (_stunTimers[i] != 0)
            {
                _stunTimers[i]--;
            }

            _objects[i]?.DecrementStunTimer();
        }

        // ORIGINAL: Here the player isn't part of the array, but in the original it's the first element.
        Game.Link.DecrementStunTimer();
    }

    private void InitPlaceholderTypes()
    {
        Array.Clear(_placeholderTypes);
    }

    public ObjectSlot FindEmptyMonsterSlot()
    {
        for (var i = (int)ObjectSlot.LastMonster; i >= 0; i--)
        {
            if (_objects[i] == null) return (ObjectSlot)i;
        }
        return ObjectSlot.NoneFound;
    }

    public bool TryFindEmptyMonsterSlot(out ObjectSlot slot)
    {
        slot = FindEmptyMonsterSlot();
        return slot != ObjectSlot.NoneFound;
    }

    private void ClearRoomItemData()
    {
        RecorderUsed = 0;
        CandleUsed = false;
        _summonedWhirlwind = false;
        _shuttersPassedDirs = Direction.None;
        _brightenRoom = false;
        ActiveShots = 0;
    }

    private void SetPlayerColor()
    {
        ReadOnlySpan<byte> palette = [0x29, 0x32, 0x16];

        var value = Profile.Items[ItemSlot.Ring];
        Graphics.SetColorIndexed(Palette.Player, 1, palette[value]);
    }

    private void SetFlashPalette()
    {
        ReadOnlySpan<byte> palette = [0x0F, 0x30, 0x30, 0x30];

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, palette);
        }

        Graphics.UpdatePalettes();
    }

    private static void SetLevelPalettes(byte[][] palettes) // const byte palettes[2][PaletteLength] )
    {
        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)2 + i, palettes[i]);
        }

        Graphics.UpdatePalettes();
    }

    private void SetLevelPalette()
    {
        var infoBlock = GetLevelInfo();

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, infoBlock.GetPalette(i));
        }

        Graphics.UpdatePalettes();
    }

    private void SetLevelFgPalette()
    {
        var infoBlock = GetLevelInfo();
        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, infoBlock.GetPalette((int)Palette.LevelFgPalette));
    }
}
