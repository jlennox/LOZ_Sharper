using System.Collections.Immutable;
using SkiaSharp;
using z1.IO;
using z1.Render;

namespace z1.UI;

internal sealed class SubmenuType
{
    public const int Width = Global.StdViewWidth;
    public const int Height = 0xAE;
    public const int ActiveItemCount = 8;
    public const int PassiveItemCount = 6;
    public const int YScrollSpeed = 3;

    private const int CurItemX = 0x40;
    private const int CurItemY = 0x28;

    private const int PassiveItemX = 0x80;
    private const int PassiveItemY = 0x10;

    private const int ActiveItemX = 0x80;
    private const int ActiveItemY = 0x28;
    private const int ActiveItemStrideX = 0x18;
    private const int ActiveItemStrideY = 0x10;

    private const int ActiveMapX = 80; // 0x80; // in Sharper maps can be wider, so start further left.
    private const int ActiveMapY = 0x58;

    private const int ItemsPerRow = 4;

    private static readonly DebugLog _log = new(nameof(SubmenuType));

    private readonly record struct PassiveItemSpec(ItemSlot ItemSlot, byte X);
    private readonly record struct TriforcePieceSpec(byte X, byte Y, byte[][] OffTiles, byte[][] OnTiles);

    private static readonly ImmutableArray<byte> _equippedUISlots = [
        0,          // Sword
        1,          // Bombs
        2,          // Arrow
        0,          // Bow
        3,          // Candle
        4,          // Recorder
        5,          // Food
        6,          // Potion
        7,          // Rod
        0,          // Raft
        0,          // Book
        0,          // Ring
        0,          // Ladder
        0,          // MagicKey
        0,          // Bracelet
        6,          // Letter
        0,          // Compass
        0,          // Map
        0,          // Compass9
        0,          // Map9
        0,          // Clock
        0,          // Rupees
        0,          // Keys
        0,          // HeartContainers
        0,          // PartialHeart
        0,          // TriforcePieces
        0,          // PowerTriforce
        0,          // Boomerang
    ];

    private static readonly ImmutableArray<TileInst> _uiTiles = [
        // INVENTORY
        new(0x12, 0x20, 0x10, 1),
        new(0x17, 0x28, 0x10, 1),
        new(0x1F, 0x30, 0x10, 1),
        new(0x0E, 0x38, 0x10, 1),
        new(0x17, 0x40, 0x10, 1),
        new(0x1D, 0x48, 0x10, 1),
        new(0x18, 0x50, 0x10, 1),
        new(0x1B, 0x58, 0x10, 1),
        new(0x22, 0x60, 0x10, 1),

        // Item B Box
        new(0x69, 0x38, 0x20, 0),
        new(0x6A, 0x40, 0x20, 0),
        new(0x6A, 0x48, 0x20, 0),
        new(0x6B, 0x50, 0x20, 0),
        new(0x6C, 0x38, 0x28, 0),
        new(0x6C, 0x50, 0x28, 0),
        new(0x6C, 0x38, 0x30, 0),
        new(0x6C, 0x50, 0x30, 0),
        new(0x6E, 0x38, 0x38, 0),
        new(0x6A, 0x40, 0x38, 0),
        new(0x6A, 0x48, 0x38, 0),
        new(0x6D, 0x50, 0x38, 0),

        // USE B BUTTON FOR THIS
        new(0x1E, 0x10, 0x40, 0),
        new(0x1C, 0x18, 0x40, 0),
        new(0x0E, 0x20, 0x40, 0),

        new(0x0B, 0x30, 0x40, 0),

        new(0x0B, 0x40, 0x40, 0),
        new(0x1E, 0x48, 0x40, 0),
        new(0x1D, 0x50, 0x40, 0),
        new(0x1D, 0x58, 0x40, 0),
        new(0x18, 0x60, 0x40, 0),
        new(0x17, 0x68, 0x40, 0),

        new(0x0F, 0x20, 0x48, 0),
        new(0x18, 0x28, 0x48, 0),
        new(0x1B, 0x30, 0x48, 0),

        new(0x1D, 0x40, 0x48, 0),
        new(0x11, 0x48, 0x48, 0),
        new(0x12, 0x50, 0x48, 0),
        new(0x1C, 0x58, 0x48, 0),

        // Inventory Box
        new(0x69, 0x78, 0x20, 0),
        new(0x6A, 0x80, 0x20, 0),
        new(0x6A, 0x88, 0x20, 0),
        new(0x6A, 0x90, 0x20, 0),
        new(0x6A, 0x98, 0x20, 0),
        new(0x6A, 0xA0, 0x20, 0),
        new(0x6A, 0xA8, 0x20, 0),
        new(0x6A, 0xB0, 0x20, 0),
        new(0x6A, 0xB8, 0x20, 0),

        new(0x6A, 0xC0, 0x20, 0),
        new(0x6A, 0xC8, 0x20, 0),
        new(0x6A, 0xD0, 0x20, 0),

        new(0x6B, 0xD8, 0x20, 0),

        new(0x6C, 0x78, 0x28, 0),
        new(0x6C, 0xD8, 0x28, 0),

        new(0x6C, 0x78, 0x30, 0),
        new(0x6C, 0xD8, 0x30, 0),

        new(0x6C, 0x78, 0x38, 0),
        new(0x6C, 0xD8, 0x38, 0),

        new(0x6C, 0x78, 0x40, 0),
        new(0x6C, 0xD8, 0x40, 0),

        //---
        new(0x6E, 0x78, 0x48, 0),
        new(0x6A, 0x80, 0x48, 0),
        new(0x6A, 0x88, 0x48, 0),
        new(0x6A, 0x90, 0x48, 0),
        new(0x6A, 0x98, 0x48, 0),
        new(0x6A, 0xA0, 0x48, 0),
        new(0x6A, 0xA8, 0x48, 0),
        new(0x6A, 0xB0, 0x48, 0),
        new(0x6A, 0xB8, 0x48, 0),

        new(0x6A, 0xC0, 0x48, 0),
        new(0x6A, 0xC8, 0x48, 0),
        new(0x6A, 0xD0, 0x48, 0),

        new(0x6D, 0xD8, 0x48, 0)
    ];

    private static readonly ImmutableArray<PassiveItemSpec> _passiveItems = [
        new(ItemSlot.Raft,     PassiveItemX),
        new(ItemSlot.Book,     PassiveItemX + 0x18),
        new(ItemSlot.Ring,     PassiveItemX + 0x24),
        new(ItemSlot.Ladder,   PassiveItemX + 0x30),
        new(ItemSlot.MagicKey, PassiveItemX + 0x44),
        new(ItemSlot.Bracelet, PassiveItemX + 0x50)
    ];

    private static readonly ImmutableArray<ItemSlot> _inventoryOrder = [
        ItemSlot.Boomerang,
        ItemSlot.Bombs,
        ItemSlot.Arrow,
        ItemSlot.Candle,

        ItemSlot.Recorder,
        ItemSlot.Food,
        ItemSlot.Letter,
        ItemSlot.Rod
    ];

    private static readonly int _arrowBowUISlot = _inventoryOrder.IndexOf(ItemSlot.Arrow);

    private readonly Game _game;
    private bool _enabled;
    private bool _activated;
    private int _activeUISlot;
    private readonly ItemSlot[] _activeSlots = new ItemSlot[ActiveItemCount];
    private readonly ItemId[] _activeItems = new ItemId[ActiveItemCount];
    private readonly SpriteImage _cursor = new();

    public SubmenuType(Game game)
    {
        _game = game;
    }

    private ItemId GetItemIdForUISlot(int uiSlot, ref ItemSlot itemSlot)
    {
        var profile = _game.World.Profile;

        itemSlot = _inventoryOrder[uiSlot];

        if (itemSlot == ItemSlot.Arrow)
        {
            if (profile.GetItem(ItemSlot.Arrow) != 0
                && profile.GetItem(ItemSlot.Bow) != 0)
            {
                var arrowId = GlobalFunctions.ItemValueToItemId(_game, ItemSlot.Arrow);
                return arrowId;
            }
        }
        else
        {
            if (itemSlot == ItemSlot.Letter)
            {
                if (profile.GetItem(ItemSlot.Potion) != 0)
                {
                    itemSlot = ItemSlot.Potion;
                }
            }

            var itemValue = profile.GetItem(itemSlot);
            if (itemValue != 0)
            {
                var itemId = GlobalFunctions.ItemValueToItemId(itemSlot, itemValue);
                return itemId;
            }
        }

        itemSlot = 0;
        return ItemId.None;
    }

    private void UpdateActiveItems()
    {
        // JOE: TODO: Write an enumerator for this?
        for (var i = 0; i < ActiveItemCount; i++)
        {
            _activeItems[i] = GetItemIdForUISlot(i, ref _activeSlots[i]);
        }
    }

    public void Enable()
    {
        UpdateActiveItems();

        // JOE: TODO: Can this be in the constructor?
        _cursor.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Cursor);

        var profile = _game.World.Profile;

        _activeUISlot = _equippedUISlots[(int)profile.SelectedItem];
        _enabled = true;
    }

    public void Disable() => _enabled = false;
    public void Activate() => _activated = true;
    public void Deactivate() => _activated = false;

    public void Update()
    {
        if (!_activated) return;

        var direction = _game.Input.GetDirectionPressing();
        if (direction == Direction.None) return;

        var xdir = direction switch
        {
            Direction.Left => -1,
            Direction.Right => 1,
            _ => 0
        };

        var ydir = direction switch
        {
            Direction.Up => -1,
            Direction.Down => 1,
            _ => 0
        };

        _game.World.Game.Sound.PlayEffect(SoundEffect.Cursor);

        if (ydir != 0)
        {
            if (_game.Enhancements.ImprovedMenus)
            {
                var amount = ItemsPerRow * ydir;
                var target = (_activeUISlot + amount) % ActiveItemCount;
                if (target < 0) target = _activeUISlot - amount;

                if (_activeItems[target] != ItemId.None)
                {
                    _activeUISlot = target;
                }
            }
        }
        else
        {
            SelectNextItem(xdir);
        }

        var profile = _game.World.Profile;
        profile.SelectedItem = _activeSlots[_activeUISlot];
    }

    public void SelectItem(ItemSlot slot)
    {
        UpdateActiveItems();

        var index = _inventoryOrder.IndexOf(slot);
        if (index == -1)
        {
            _log.Error($"Unable to find index for {slot}");
            return;
        }

        if (_activeItems[index] != ItemId.None)
        {
            _activeUISlot = index;
            var profile = _game.World.Profile;
            profile.SelectedItem = _activeSlots[_activeUISlot];
        }
    }

    public void SelectPreviousItem() => SelectNextItem(-1);

    public void SelectNextItem(int xdir = 1)
    {
        UpdateActiveItems();

        for (var i = 0; i < ActiveItemCount; i++)
        {
            _activeUISlot += xdir;
            _activeUISlot += _activeUISlot switch
            {
                < 0 => ActiveItemCount,
                >= ActiveItemCount => -ActiveItemCount,
                _ => 0,
            };

            if (_activeItems[_activeUISlot] != ItemId.None) break;
        }

        var profile = _game.World.Profile;
        profile.SelectedItem = _activeSlots[_activeUISlot];
    }

    public void Draw(int bottom)
    {
        if (!_enabled) return;

        using var _ = Graphics.SetClip(0, 0, Width, bottom);

        var top = bottom - Height;
        DrawBackground(top);
        DrawPassiveInventory(top);
        DrawActiveInventory(top);
        DrawCurrentSelection(top);

        if (_game.World.IsOverworld())
        {
            DrawTriforce(top);
        }
        else
        {
            DrawMap(top);
        }
    }

    private static void DrawBackground(int top)
    {
        Graphics.Clear(SKColors.Black);

        foreach (var tileInst in _uiTiles)
        {
            GlobalFunctions.DrawChar(tileInst.Id, tileInst.X, tileInst.Y + top, tileInst.Palette);
        }
    }

    private void DrawActiveInventory(int top)
    {
        var profile = _game.World.Profile;
        var x = ActiveItemX;
        var y = ActiveItemY + top;

        for (var i = 0; i < ActiveItemCount; i++)
        {
            var itemId = _activeItems[i];

            if (i == _arrowBowUISlot)
            {
                if (profile.GetItem(ItemSlot.Arrow) != 0)
                {
                    itemId = GlobalFunctions.ItemValueToItemId(_game, ItemSlot.Arrow);
                    GlobalFunctions.DrawItemNarrow(_game.World.Game, itemId, x, y);
                }
                if (profile.GetItem(ItemSlot.Bow) != 0)
                {
                    GlobalFunctions.DrawItemNarrow(_game.World.Game, ItemId.Bow, x + 8, y);
                }
            }
            else if (itemId != ItemId.None)
            {
                GlobalFunctions.DrawItemWide(_game.World.Game, itemId, x, y);
            }

            x += ActiveItemStrideX;
            if ((i % 4) == 3)
            {
                x = ActiveItemX;
                y += ActiveItemStrideY;
            }
        }

        x = ActiveItemX + (_activeUISlot % 4) * ActiveItemStrideX;
        y = ActiveItemY + (_activeUISlot / 4) * ActiveItemStrideY + top;

        var cursorPals = new[] { Palette.Blue, Palette.Red };
        var cursorPal = cursorPals[(_game.World.Game.FrameCounter >> 3) & 1];
        _cursor.Draw(TileSheet.PlayerAndItems, x, y, cursorPal);
    }

    private void DrawPassiveInventory(int top)
    {
        var profile = _game.World.Profile;

        for (var i = 0; i < PassiveItemCount; i++)
        {
            var slot = _passiveItems[i].ItemSlot;
            var value = profile.GetItem(slot);

            if (value != 0)
            {
                var itemId = GlobalFunctions.ItemValueToItemId(slot, value);
                GlobalFunctions.DrawItem(_game.World.Game, itemId, _passiveItems[i].X, PassiveItemY + top, 0);
            }
        }
    }

    private void DrawCurrentSelection(int top)
    {
        var curSlot = _game.World.Profile.SelectedItem;
        if (curSlot == 0) return;

        var itemId = GlobalFunctions.ItemValueToItemId(_game, curSlot);
        if (itemId == ItemId.None) return;

        GlobalFunctions.DrawItemWide(_game.World.Game, itemId, CurItemX, CurItemY + top);
    }

    private static readonly ImmutableArray<TriforcePieceSpec> _pieceSpecs = [
        new(0x70, 0x70, [[0xED, 0xE9], [0xE9, 0x24]], [[0xED, 0xE7], [0xE7, 0xF5]]),
        new(0x80, 0x70, [[0xEA, 0xEE], [0x24, 0xEA]], [[0xE8, 0xEE], [0xF5, 0xE8]]),
        new(0x60, 0x80, [[0xED, 0xE9], [0xE9, 0x24]], [[0xED, 0xE7], [0xE7, 0xF5]]),
        new(0x90, 0x80, [[0xEA, 0xEE], [0x24, 0xEA]], [[0xE8, 0xEE], [0xF5, 0xE8]]),
        new(0x70, 0x80, [[0x24, 0x24], [0x24, 0x24]], [[0xE5, 0xF5], [0x24, 0xE5]]),
        new(0x70, 0x80, [[0x24, 0x24], [0x24, 0x24]], [[0xE8, 0x24], [0xF5, 0xE8]]),
        new(0x80, 0x80, [[0x24, 0x24], [0x24, 0x24]], [[0xF5, 0xE6], [0xE6, 0x24]]),
        new(0x80, 0x80, [[0x24, 0x24], [0x24, 0x24]], [[0x24, 0xE7], [0xE7, 0xF5]])
    ];

    private static readonly ImmutableArray<byte> _triforce = [0x1D, 0x1B, 0x12, 0x0F, 0x18, 0x1B, 0x0C, 0x0E];

    private void DrawTriforce(int top)
    {
        GlobalFunctions.DrawChar(0xED, 0x78, 0x68 + top, Palette.RedBackground);
        GlobalFunctions.DrawChar(0xEE, 0x80, 0x68 + top, Palette.RedBackground);

        GlobalFunctions.DrawChar(0xED, 0x68, 0x78 + top, Palette.RedBackground);
        GlobalFunctions.DrawChar(0xEE, 0x90, 0x78 + top, Palette.RedBackground);

        GlobalFunctions.DrawChar(0xED, 0x58, 0x88 + top, Palette.RedBackground);
        GlobalFunctions.DrawChar(0xEE, 0xA0, 0x88 + top, Palette.RedBackground);

        GlobalFunctions.DrawChar(0xEB, 0x50, 0x90 + top, Palette.RedBackground);
        GlobalFunctions.DrawChar(0xEF, 0x58, 0x90 + top, Palette.RedBackground);
        GlobalFunctions.DrawChar(0xF0, 0xA0, 0x90 + top, Palette.RedBackground);
        GlobalFunctions.DrawChar(0xEC, 0xA8, 0x90 + top, Palette.RedBackground);

        GlobalFunctions.DrawString(_triforce.AsSpan(), 0x60, 0xA0 + top, Palette.RedBackground);

        var x = 0x60;
        for (var i = 0; i < 8; i++, x += 8)
        {
            GlobalFunctions.DrawChar(0xF1, x, 0x90 + top, Palette.RedBackground);
        }

        var pieces = _game.World.GetItem(ItemSlot.TriforcePieces);
        var piece = pieces;

        for (var i = 0; i < 8; i++, piece >>= 1)
        {
            var have = (piece & 1) != 0;
            var tiles = have ? _pieceSpecs[i].OnTiles : _pieceSpecs[i].OffTiles;

            var ii = 0;
            for (var r = 0; r < 2; r++)
            {
                for (var c = 0; c < 2; c++, ii++)
                {
                    var xx = _pieceSpecs[i].X + (c * 8);
                    var yy = _pieceSpecs[i].Y + (r * 8) + top;
                    // JOE: TODO: Uh, is this right? Maybe just flatten the array?
                    GlobalFunctions.DrawChar(tiles[ii / tiles.Length][ii % tiles.Length], xx, yy, Palette.RedBackground);
                }
            }
        }

        if ((pieces & 0x30) == 0x30)
        {
            GlobalFunctions.DrawChar(0xF5, 0x70, 0x80 + top, Palette.RedBackground);
            GlobalFunctions.DrawChar(0xF5, 0x78, 0x88 + top, Palette.RedBackground);
        }

        if ((pieces & 0xC0) == 0xC0)
        {
            GlobalFunctions.DrawChar(0xF5, 0x88, 0x80 + top, Palette.RedBackground);
            GlobalFunctions.DrawChar(0xF5, 0x80, 0x88 + top, Palette.RedBackground);
        }
    }

    private static int GetDoorTileOffset(GameRoom room)
    {
        if (!room.HasUnderworldDoors) return 0;

        var doorSum = 0;
        var doorBit = 8;
        var flags = room.PersistedRoomState;
        for (; doorBit != 0; doorBit >>= 1)
        {
            var direction = (Direction)doorBit;
            if (!room.UnderworldDoors.TryGetValue(direction, out var doorType)) continue;

            var isLockedType = doorType.IsLockedType();
            if (doorType == DoorType.Open || (isLockedType && flags.IsDoorOpen(direction)))
            {
                doorSum |= doorBit;
            }
        }

        return doorSum;
    }

    private static readonly ImmutableArray<byte> _topMapLine = [0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xF5];
    private static readonly ImmutableArray<byte> _bottomMapLine = [0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5];

    private void DrawMap(int top)
    {
        const int mapBackGroundBaseX = 0x60;
        const int mapBaseX = mapBackGroundBaseX;
        const int mapBaseY = 0x58;
        const int mapMaxWidth = 16;
        const int mapMaxHeight = 8;
        const int mapCellWidth = 8;
        const int mapCellHeight = 8;

        GlobalFunctions.DrawString("map", 0x28, 0x58 + top, (Palette)1);
        GlobalFunctions.DrawString("compass", 0x18, 0x80 + top, (Palette)1);
        GlobalFunctions.DrawString(_topMapLine, mapBackGroundBaseX, 0x50 + top, (Palette)1);
        GlobalFunctions.DrawString(_bottomMapLine, mapBackGroundBaseX, 0x98 + top, (Palette)1);

        var y = mapBaseY + top;
        // Blank out the background.
        for (var r = 0; r < mapMaxHeight; r++, y += mapCellHeight)
        {
            var xx = mapBackGroundBaseX;
            for (var c = 0; c < mapMaxWidth; c++, xx += mapCellWidth)
            {
                GlobalFunctions.DrawChar(0xF5, xx, y, (Palette)1);
            }
        }

        var hasMap = _game.World.HasCurrentMap();
        var hasCompass = _game.World.HasCurrentCompass();

        if (hasMap) GlobalFunctions.DrawItemNarrow(_game.World.Game, ItemId.Map, 0x30, 0x68 + top);
        if (hasCompass) GlobalFunctions.DrawItemNarrow(_game.World.Game, ItemId.Compass, 0x30, 0x90 + top);

        var map = _game.World.CurrentWorld.GameWorldMap;

        const int grayColor = 0xC0;
        ReadOnlySpan<SKColor> hasNotSeenPalette = [new SKColor(grayColor, grayColor, grayColor), new SKColor(0), new SKColor(0), new SKColor(0)];

        // Align the map centered horizontally, and along the bottom vertically.
        y = ActiveMapY + top + (mapMaxHeight - map.Height) * mapCellHeight;
        var basex = mapBaseX + ((mapMaxWidth - map.Width) / 2) * mapCellWidth;

        // Draw the map.
        for (var mapY = 0; mapY < map.Height; mapY++, y += mapCellHeight)
        {
            var x = basex;
            for (var mapX = 0; mapX < map.Width; mapX++, x += mapCellWidth)
            {
                var room = map.RoomGrid[mapX, mapY];
                if (room == null) continue;
                if (!room.Settings.HiddenFromMap)
                {
                    if (room.PersistedRoomState.VisitState)
                    {
                        var tile = 0xD0 + GetDoorTileOffset(room);
                        GlobalFunctions.DrawChar(tile, x, y, (Palette)1);
                    }
                    else if (hasMap)
                    {
                        GlobalFunctions.DrawChar(0xD0, x, y, hasNotSeenPalette);
                    }
                }

                if (room == _game.World.CurrentRoom)
                {
                    GlobalFunctions.DrawChar(0xE0, x + 2, y + 3, Palette.Player, DrawingFlags.None);
                }
            }
        }
    }
}