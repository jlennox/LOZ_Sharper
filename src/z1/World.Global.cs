using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using SkiaSharp;
using z1.Actors;
using z1.Render;

namespace z1;

internal partial class World
{
    private static bool TryGetConnectedRoom(GameRoom currentRoom, Direction direction, [MaybeNullWhen(false)] out GameRoom room)
    {
        return currentRoom.Connections.TryGetValue(direction, out room);
    }

    private GameRoom GetNextRoom(Direction direction, out EntranceHistoryEntry? entry)
    {
        if (TryGetConnectedRoom(CurrentRoom, direction, out var nextRoom))
        {
            entry = null;
            return nextRoom;
        }

        // Underworlds/subrooms are exited by going out of bounds. The overworld should just stop the player.
        // JOE: Do we support world wrap, per the original?
        if (CurrentRoom.GameWorld.IsOverworld)
        {
            entry = null;
            return CurrentRoom;
        }

        entry = _entranceHistory.TakePreviousEntranceOrDefault();
        return entry.Value.Room;
    }

    private static void ClearScreen(Graphics graphics)
    {
        graphics.Clear(SKColors.Black);
    }

    private static void ClearScreen(Graphics graphics, int sysColor)
    {
        graphics.Clear(GraphicPalettes.GetSystemColor(sysColor));
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
        Game.Player.DecrementObjectTimer();
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
        Game.Player.DecrementStunTimer();
    }

    private void InitPlaceholderTypes()
    {
        _pendingEdgeSpawns.Clear();
    }

    private void ClearRoomItemData()
    {
        Player.CandleUsed = false;
        _summonedWhirlwind = false;
        _brightenRoom = false;
    }

    private void SetFlashPalette()
    {
        if (Game.Enhancements.ReduceFlashing) return;

        ReadOnlySpan<byte> palette = [0x0F, 0x30, 0x30, 0x30];

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            GraphicPalettes.SetPaletteIndexed((Palette)i, palette);
        }

        GraphicPalettes.UpdatePalettes();
    }

    private void SetLevelPalettes(ImmutableArray<ImmutableArray<byte>> palettes) // const byte palettes[2][PaletteLength] )
    {
        for (var i = 0; i < 2; i++)
        {
            GraphicPalettes.SetPaletteIndexed((Palette)2 + i, palettes[i]);
        }

        GraphicPalettes.UpdatePalettes();
    }

    // JOE: TODO: Cleanup.
    private void SetLevelPalettes(byte[][] palettes) // const byte palettes[2][PaletteLength] )
    {
        for (var i = 0; i < 2; i++)
        {
            GraphicPalettes.SetPaletteIndexed((Palette)2 + i, palettes[i]);
        }

        GraphicPalettes.UpdatePalettes();
    }

    private void SetLevelPalette()
    {
        var infoBlock = CurrentWorld.Settings;

        for (var i = 2; i < Global.BackgroundPalCount; i++)
        {
            GraphicPalettes.SetPaletteIndexed((Palette)i, infoBlock.Palettes[i]);
        }

        GraphicPalettes.UpdatePalettes();
    }

    private void SetLevelFgPalette()
    {
        var infoBlock = CurrentWorld.Settings;
        GraphicPalettes.SetPaletteIndexed(Palette.SeaPal, infoBlock.Palettes[(int)Palette.SeaPal]);
    }
}
