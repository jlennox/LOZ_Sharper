using System.Collections.Immutable;
using SkiaSharp;
using z1.Actors;
using z1.Render;

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

    private static void GetRoomCoord(int position, out int tileY, out int tileX)
    {
        tileY = position & 0x0F;
        tileX = (position & 0xF0) >> 4;
        tileY -= 4;
    }

    private static Point GetRoomItemPosition(byte position)
    {
        return new Point(position & 0xF0, (byte)(position << 4));
    }

    private static int GetDoorStateFace(DoorType type, bool state)
    {
        var doorface = _doorFaces[(int)type];
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

    private void AddOnlyObjectOfType<T>(T obj)
        where T : Actor
    {
        var oldObject = GetObject<T>();
        if (oldObject != null) ClearObject(oldObject);
        AddObject(obj);
    }

    public void ClearObject(Actor obj)
    {
        obj.Delete();
        _objects.Remove(obj);
    }

    public void ClearObjects(Func<Actor, bool> pred)
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            var obj = _objects[i];
            if (pred(obj))
            {
                obj.Delete();
                _objects.RemoveAt(i);
            }
        }
    }

    private void SetBlockObj(Actor block)
    {
        AddOnlyObjectOfType(block);
    }

    private void DeleteObjects()
    {
        foreach (var obj in _objects) obj.Delete();
    }

    private void CleanUpRoomItems()
    {
        DeleteObjects();
        SetItem(ItemSlot.Clock, 0);
    }

    private void DeleteDeadObjects()
    {
        _objects.RemoveAll(static t => t.IsDeleted);
    }

    private void InitObjectTimers()
    {
        _objectTimers.Clear();
    }

    private void DecrementObjectTimers()
    {
        foreach (var (obj, timer) in _objectTimers)
        {
            if (timer != 0) _objectTimers[obj]--;
        }

        foreach (var obj in _objects)
        {
            obj.DecrementObjectTimer();
        }

        // ORIGINAL: Here the player isn't part of the array, but in the original it's the first element.
        Game.Link.DecrementObjectTimer();
    }

    private void InitStunTimers()
    {
        _longTimer = 0;
        _stunTimers.Clear();
    }

    private void DecrementStunTimers()
    {
        if (_longTimer > 0)
        {
            _longTimer--;
            return;
        }

        _longTimer = 9;

        foreach (var (obj, timer) in _stunTimers)
        {
            if (timer != 0) _stunTimers[obj]--;
        }

        foreach (var obj in _objects) obj.DecrementStunTimer();

        // ORIGINAL: Here the player isn't part of the array, but in the original it's the first element.
        Game.Link.DecrementStunTimer();
    }

    private void InitPlaceholderTypes()
    {
        _pendingEdgeSpawns.Clear();
    }

    private void ClearRoomItemData()
    {
        RecorderUsed = 0;
        CandleUsed = false;
        _summonedWhirlwind = false;
        _brightenRoom = false;
        ActiveShots = 0;
    }

    private void SetFlashPalette()
    {
        if (Game.Enhancements.ReduceFlashing) return;

        ReadOnlySpan<byte> palette = [0x0F, 0x30, 0x30, 0x30];

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, palette);
        }

        Graphics.UpdatePalettes();
    }

    private static void SetLevelPalettes(ImmutableArray<ImmutableArray<byte>> palettes) // const byte palettes[2][PaletteLength] )
    {
        for (var i = 0; i < 2; i++)
        {
            Graphics.SetPaletteIndexed((Palette)2 + i, palettes[i]);
        }

        Graphics.UpdatePalettes();
    }

    // JOE: TODO: Cleanup.
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
        Graphics.SetPaletteIndexed(Palette.SeaPal, infoBlock.GetPalette((int)Palette.SeaPal));
    }
}
