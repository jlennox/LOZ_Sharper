using SkiaSharp;

namespace z1.UI;

internal sealed class SubmenuType
{
    public const int Width = Global.StdViewWidth;
    public const int Height = 0xAE;
    public const int ActiveItems = 8;
    public const int PassiveItems = 6;
    public const int YScrollSpeed = 3;

    private const int CurItemX = 0x40;
    private const int CurItemY = 0x28;

    private const int PassiveItemX = 0x80;
    private const int PassiveItemY = 0x10;

    private const int ActiveItemX = 0x80;
    private const int ActiveItemY = 0x28;
    private const int ActiveItemStrideX = 0x18;
    private const int ActiveItemStrideY = 0x10;

    private const int ActiveMapX = 0x80;
    private const int ActiveMapY = 0x58;

    private const int ItemsPerRow = 4;

    private readonly record struct PassiveItemSpec(ItemSlot ItemSlot, byte X);
    private readonly record struct TriforcePieceSpec(byte X, byte Y, byte[][] OffTiles, byte[][] OnTiles);

    private static readonly byte[] equippedUISlots = new byte[]
    {
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
    };

    private static readonly TileInst[] uiTiles = new TileInst[]
    {
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

        new(0x6D, 0xD8, 0x48, 0),
    };

    private static readonly PassiveItemSpec[] passiveItems =
    {
        new(ItemSlot.Raft,     PassiveItemX),
        new(ItemSlot.Book,     PassiveItemX + 0x18),
        new(ItemSlot.Ring,     PassiveItemX + 0x24),
        new(ItemSlot.Ladder,   PassiveItemX + 0x30),
        new(ItemSlot.MagicKey, PassiveItemX + 0x44),
        new(ItemSlot.Bracelet, PassiveItemX + 0x50),
    };

    private static readonly ItemSlot[] inventoryOrder = new[]
    {
        ItemSlot.Boomerang,
        ItemSlot.Bombs,
        ItemSlot.Arrow,
        ItemSlot.Candle,

        ItemSlot.Recorder,
        ItemSlot.Food,
        ItemSlot.Letter,
        ItemSlot.Rod,
    };

    private static readonly int ArrowBowUISlot = Array.IndexOf(inventoryOrder, ItemSlot.Arrow);

    private readonly Game _game;
    private bool enabled;
    private bool activated;
    private int activeUISlot;
    private ItemSlot[] activeSlots = new ItemSlot[ActiveItems];
    private ItemId[] activeItems = new ItemId[ActiveItems];
    private SpriteImage cursor = new();

    public SubmenuType(Game game)
    {
        _game = game;
    }

    private ItemId GetItemIdForUISlot(int uiSlot, ref ItemSlot itemSlot)
    {
        var profile = _game.World.GetProfile();

        itemSlot = inventoryOrder[uiSlot];

        if (itemSlot == ItemSlot.Arrow)
        {
            if (profile.Items[ItemSlot.Arrow] != 0
                && profile.Items[ItemSlot.Bow] != 0)
            {
                var arrowId = GlobalFunctions.ItemValueToItemId(_game.World, ItemSlot.Arrow);
                return arrowId;
            }
        }
        else
        {
            if (itemSlot == ItemSlot.Letter)
            {
                if (profile.Items[ItemSlot.Potion] != 0)
                {
                    itemSlot = ItemSlot.Potion;
                }
            }

            var itemValue = profile.Items[itemSlot];
            if (itemValue != 0)
            {
                var itemId = GlobalFunctions.ItemValueToItemId(itemSlot, itemValue);
                return itemId;
            }
        }

        itemSlot = 0;
        return ItemId.None;
    }

    public void Enable()
    {
        // JOE: TODO: Write an enumerator for this?
        for (var i = 0; i < ActiveItems; i++)
        {
            activeItems[i] = GetItemIdForUISlot(i, ref activeSlots[i]);
        }

        // JOE: TODO: Can this be in the constructor?
        cursor.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Cursor);

        var profile = _game.World.GetProfile();

        activeUISlot = equippedUISlots[(int)profile.SelectedItem];
        enabled = true;
    }

    public void Disable()
    {
        enabled = false;
    }

    public void Activate()
    {
        activated = true;
    }

    public void Deactivate()
    {
        activated = false;
    }

    public void Update()
    {
        if (!activated)
        {
            return;
        }

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
            if (_game.Enhancements)
            {
                var amount = ItemsPerRow * ydir;
                var target = (activeUISlot + amount) % ActiveItems;
                if (target < 0) target = activeUISlot - amount;

                if (activeItems[target] != ItemId.None)
                {
                    activeUISlot = target;
                }
            }
        }
        else
        {
            SelectNextItem(xdir);
        }

        var profile = _game.World.GetProfile();
        profile.SelectedItem = activeSlots[activeUISlot];
    }

    public void SelectNextItem(int xdir = 1)
    {
        for (var i = 0; i < ActiveItems; i++)
        {
            activeUISlot += xdir;

            if (activeUISlot < 0)
                activeUISlot += ActiveItems;
            else if (activeUISlot >= ActiveItems)
                activeUISlot -= ActiveItems;

            if (activeItems[activeUISlot] != ItemId.None)
                break;
        }

        var profile = _game.World.GetProfile();
        profile.SelectedItem = activeSlots[activeUISlot];
    }

    public void Draw(int bottom)
    {
        if (!enabled) return;

        var top = bottom - Height;

        using var _ = Graphics.SetClip(0, 0, Width, bottom);

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

        for (var i = 0; i < uiTiles.Length; i++)
        {
            var tileInst = uiTiles[i];
            GlobalFunctions.DrawChar(tileInst.Id, tileInst.X, tileInst.Y + top, tileInst.Palette);
        }
    }

    private void DrawActiveInventory(int top)
    {
        var profile = _game.World.GetProfile();
        var x = ActiveItemX;
        var y = ActiveItemY + top;

        for (var i = 0; i < ActiveItems; i++)
        {
            var itemId = activeItems[i];

            if (i == ArrowBowUISlot)
            {
                if (profile.Items[ItemSlot.Arrow] != 0)
                {
                    itemId = GlobalFunctions.ItemValueToItemId(_game.World, ItemSlot.Arrow);
                    GlobalFunctions.DrawItemNarrow(_game.World.Game, itemId, x, y);
                }
                if (profile.Items[ItemSlot.Bow] != 0)
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

        x = ActiveItemX + (activeUISlot % 4) * ActiveItemStrideX;
        y = ActiveItemY + (activeUISlot / 4) * ActiveItemStrideY + top;

        var cursorPals = new[] { Palette.BlueFgPalette, Palette.RedFgPalette };
        var cursorPal = cursorPals[(_game.World.Game.GetFrameCounter() >> 3) & 1];
        cursor.Draw(TileSheet.PlayerAndItems, x, y, cursorPal);
    }

    private void DrawPassiveInventory(int top)
    {
        var profile = _game.World.GetProfile();

        for (var i = 0; i < PassiveItems; i++)
        {
            var slot = passiveItems[i].ItemSlot;
            var value = profile.Items[slot];

            if (value != 0)
            {
                var itemId = GlobalFunctions.ItemValueToItemId(slot, value);
                GlobalFunctions.DrawItem(_game.World.Game, itemId, passiveItems[i].X, PassiveItemY + top, 0);
            }
        }
    }

    private void DrawCurrentSelection(int top)
    {
        var profile = _game.World.GetProfile();
        var curSlot = profile.SelectedItem;

        if (curSlot != 0)
        {
            var itemId = GlobalFunctions.ItemValueToItemId(_game.World, curSlot);
            GlobalFunctions.DrawItemWide(_game.World.Game, itemId, CurItemX, CurItemY + top);
        }
    }

    private static readonly TriforcePieceSpec[] pieceSpecs = new TriforcePieceSpec[]
    {
        new(0x70, 0x70, new[]{ new byte[]{ 0xED, 0xE9 }, new byte[]{ 0xE9, 0x24 } }, new[]{ new byte[]{ 0xED, 0xE7 }, new byte[]{ 0xE7, 0xF5 } }),
        new(0x80, 0x70, new[]{ new byte[]{ 0xEA, 0xEE }, new byte[]{ 0x24, 0xEA } }, new[]{ new byte[]{ 0xE8, 0xEE }, new byte[]{ 0xF5, 0xE8 } }),
        new(0x60, 0x80, new[]{ new byte[]{ 0xED, 0xE9 }, new byte[]{ 0xE9, 0x24 } }, new[]{ new byte[]{ 0xED, 0xE7 }, new byte[]{ 0xE7, 0xF5 } }),
        new(0x90, 0x80, new[]{ new byte[]{ 0xEA, 0xEE }, new byte[]{ 0x24, 0xEA } }, new[]{ new byte[]{ 0xE8, 0xEE }, new byte[]{ 0xF5, 0xE8 } }),
        new(0x70, 0x80, new[]{ new byte[]{ 0x24, 0x24 }, new byte[]{ 0x24, 0x24 } }, new[]{ new byte[]{ 0xE5, 0xF5 }, new byte[]{ 0x24, 0xE5 } }),
        new(0x70, 0x80, new[]{ new byte[]{ 0x24, 0x24 }, new byte[]{ 0x24, 0x24 } }, new[]{ new byte[]{ 0xE8, 0x24 }, new byte[]{ 0xF5, 0xE8 } }),
        new(0x80, 0x80, new[]{ new byte[]{ 0x24, 0x24 }, new byte[]{ 0x24, 0x24 } }, new[]{ new byte[]{ 0xF5, 0xE6 }, new byte[]{ 0xE6, 0x24 } }),
        new(0x80, 0x80, new[]{ new byte[]{ 0x24, 0x24 }, new byte[]{ 0x24, 0x24 } }, new[]{ new byte[]{ 0x24, 0xE7 }, new byte[]{ 0xE7, 0xF5 } }),
     };

    private static readonly byte[] Triforce = new byte[] { 0x1D, 0x1B, 0x12, 0x0F, 0x18, 0x1B, 0x0C, 0x0E };

    private void DrawTriforce(int top)
    {
        GlobalFunctions.DrawChar(0xED, 0x78, 0x68 + top, Palette.RedBgPalette);
        GlobalFunctions.DrawChar(0xEE, 0x80, 0x68 + top, Palette.RedBgPalette);

        GlobalFunctions.DrawChar(0xED, 0x68, 0x78 + top, Palette.RedBgPalette);
        GlobalFunctions.DrawChar(0xEE, 0x90, 0x78 + top, Palette.RedBgPalette);

        GlobalFunctions.DrawChar(0xED, 0x58, 0x88 + top, Palette.RedBgPalette);
        GlobalFunctions.DrawChar(0xEE, 0xA0, 0x88 + top, Palette.RedBgPalette);

        GlobalFunctions.DrawChar(0xEB, 0x50, 0x90 + top, Palette.RedBgPalette);
        GlobalFunctions.DrawChar(0xEF, 0x58, 0x90 + top, Palette.RedBgPalette);
        GlobalFunctions.DrawChar(0xF0, 0xA0, 0x90 + top, Palette.RedBgPalette);
        GlobalFunctions.DrawChar(0xEC, 0xA8, 0x90 + top, Palette.RedBgPalette);

        GlobalFunctions.DrawString(Triforce, 0x60, 0xA0 + top, Palette.RedBgPalette);

        var x = 0x60;
        for (var i = 0; i < 8; i++, x += 8)
        {
            GlobalFunctions.DrawChar(0xF1, x, 0x90 + top, Palette.RedBgPalette);
        }

        var pieces = _game.World.GetItem(ItemSlot.TriforcePieces);
        var piece = pieces;

        for (var i = 0; i < 8; i++, piece >>= 1)
        {
            var have = (piece & 1) != 0;
            var tiles = have ? pieceSpecs[i].OnTiles : pieceSpecs[i].OffTiles;

            var ii = 0;
            for (var r = 0; r < 2; r++)
            {
                for (var c = 0; c < 2; c++, ii++)
                {
                    var xx = pieceSpecs[i].X + (c * 8);
                    var yy = pieceSpecs[i].Y + (r * 8) + top;
                    // JOE: TODO: Uh, is this right? Maybe just flatten the array?
                    GlobalFunctions.DrawChar(tiles[ii / tiles.Length][ii % tiles.Length], xx, yy, Palette.RedBgPalette);
                }
            }
        }

        if ((pieces & 0x30) == 0x30)
        {
            GlobalFunctions.DrawChar(0xF5, 0x70, 0x80 + top, Palette.RedBgPalette);
            GlobalFunctions.DrawChar(0xF5, 0x78, 0x88 + top, Palette.RedBgPalette);
        }

        if ((pieces & 0xC0) == 0xC0)
        {
            GlobalFunctions.DrawChar(0xF5, 0x88, 0x80 + top, Palette.RedBgPalette);
            GlobalFunctions.DrawChar(0xF5, 0x80, 0x88 + top, Palette.RedBgPalette);
        }
    }

    private static readonly byte[] Map = new byte[] { 0x16, 0x0A, 0x19 };
    private static readonly byte[] Compass = new byte[] { 0x0C, 0x18, 0x16, 0x19, 0x0A, 0x1C, 0x1C };
    private static readonly byte[] TopMapLine = new byte[] { 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xF5 };
    private static readonly byte[] BottomMapLine = new byte[] { 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5 };

    private unsafe void DrawMap(int top)
    {
        GlobalFunctions.DrawString(Map, 0x28, 0x58 + top, (Palette)1);
        GlobalFunctions.DrawString(Compass, 0x18, 0x80 + top, (Palette)1);
        GlobalFunctions.DrawString(TopMapLine, 0x60, 0x50 + top, (Palette)1);
        GlobalFunctions.DrawString(BottomMapLine, 0x60, 0x98 + top, (Palette)1);

        var y = 0x58 + top;
        for (var r = 0; r < 8; r++, y += 8)
        {
            var xx = 0;
            for (var c = 0; c < 4; c++, xx += 8)
            {
                GlobalFunctions.DrawChar(0xF5, 0x60 + xx, y, (Palette)1);
                GlobalFunctions.DrawChar(0xF5, 0xC0 + xx, y, (Palette)1);
            }
        }

        var levelInfo = _game.World.GetLevelInfo();
        var hasMap = _game.World.HasCurrentMap();
        var hasCompass = _game.World.HasCurrentCompass();

        if (hasMap)
            GlobalFunctions.DrawItemNarrow(_game.World.Game, ItemId.Map, 0x30, 0x68 + top);
        if (hasCompass)
            GlobalFunctions.DrawItemNarrow(_game.World.Game, ItemId.Compass, 0x30, 0x90 + top);

        var x = ActiveMapX;
        for (var c = 0; c < 8; c++, x += 8)
        {
            uint mapMaskByte = levelInfo.DrawnMap[c + 4];
            y = ActiveMapY + top;
            for (var r = 0; r < 8; r++, y += 8, mapMaskByte <<= 1)
            {
                var roomId = (r << 4) | (c - levelInfo.DrawnMapOffset + 0x10 + 4) & 0xF;
                var roomFlags = _game.World.GetUWRoomFlags(roomId);
                byte  tile = 0xF5;
                if ((mapMaskByte & 0x80) == 0x80 && roomFlags.GetVisitState())
                {
                    var doorSum = 0;
                    var doorBit = 8;
                    for (; doorBit != 0; doorBit >>= 1)
                    {
                        var doorType = _game.World.GetDoorType(roomId, (Direction)doorBit);
                        if (doorType == DoorType.Open)
                        {
                            doorSum |= doorBit;
                        }
                        else if (roomFlags.GetDoorState((Direction)doorBit)
                            && doorType is DoorType.Bombable or DoorType.Key or DoorType.Key2)
                        {
                            doorSum |= doorBit;
                        }
                    }
                    tile = (byte)(0xD0 + doorSum);
                }
                GlobalFunctions.DrawChar(tile, x, y, (Palette)1);
            }
        }

        var curRoomId = _game.World.curRoomId;
        var playerRow = (curRoomId >> 4) & 0xF;
        var playerCol = curRoomId & 0xF;
        playerCol = (playerCol + levelInfo.DrawnMapOffset) & 0xF;
        playerCol -= 4;

        y = ActiveMapY + top + playerRow * 8 + 3;
        x = ActiveMapX + playerCol * 8 + 2;
        GlobalFunctions.DrawChar(0xE0, x, y, Palette.Player);
    }
}