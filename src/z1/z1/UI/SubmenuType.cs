using SkiaSharp;

namespace z1.UI;

internal sealed class SubmenuType
{
    private const int ArrowBowUISlot = 2;

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


    readonly record struct PassiveItemSpec(ItemSlot ItemSlot, byte X);
    readonly record struct TileInst(byte Id, byte X, byte Y, byte Palette);

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

    private readonly World _world;
    public const int Width = Global.StdViewWidth;
    public const int Height = 0xAE;
    public const int ActiveItems = 8;
    public const int PassiveItems = 6;

    private bool enabled;
    private bool activated;
    private int activeUISlot;
    private ItemSlot[] activeSlots = new ItemSlot[ActiveItems];
    private ItemId[] activeItems = new ItemId[ActiveItems];
    private SpriteImage cursor = new();

    public SubmenuType(World world)
    {
        _world = world;
    }

    ItemId GetItemIdForUISlot(int uiSlot, ItemSlot itemSlot)
    {
        var slots = new[]
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

        var profile = _world.GetProfile();

        itemSlot = slots[uiSlot];

        if (itemSlot == ItemSlot.Arrow)
        {
            if (profile.Items[ItemSlot.Arrow] != 0
                && profile.Items[ItemSlot.Bow] != 0)
            {
                var arrowId = GlobalFunctions.ItemValueToItemId(_world, ItemSlot.Arrow);
                return arrowId;
            }
        }
        else
        {
            if (itemSlot == ItemSlot.Letter)
            {
                if (profile.Items[ItemSlot.Potion] != 0)
                    itemSlot = ItemSlot.Potion;
            }

            int itemValue = profile.Items[itemSlot];
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
        for (int i = 0; i < ActiveItems; i++)
        {
            activeItems[i] = GetItemIdForUISlot(i, activeSlots[i]);
        }

        // This breaks.
        cursor.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Cursor);

        var profile = _world.GetProfile();

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
            return;

        int dir = 0;

        if (Input.IsButtonPressing(Button.Left))
            dir = -1;
        else if (Input.IsButtonPressing(Button.Right))
            dir = 1;
        else
            return;

        _world.Game.Sound.Play(SoundEffect.Cursor);

        for (int i = 0; i < ActiveItems; i++)
        {
            activeUISlot += dir;

            if (activeUISlot < 0)
                activeUISlot += ActiveItems;
            else if (activeUISlot >= ActiveItems)
                activeUISlot -= ActiveItems;

            if (activeItems[activeUISlot] != ItemId.None)
                break;
        }

        var profile = _world.GetProfile();

        profile.SelectedItem = activeSlots[activeUISlot];
    }

    public void Draw(int bottom)
    {
        if (!enabled)
            return;

        int top = bottom - Height;

        Graphics.SetClip(0, 0, Width, bottom);

        DrawBackground(top);
        DrawPassiveInventory(top);
        DrawActiveInventory(top);
        DrawCurrentSelection(top);

        if (_world.IsOverworld())
            DrawTriforce(top);
        else
            DrawMap(top);

        Graphics.ResetClip();
    }

    void DrawBackground(int top)
    {
        Graphics.Clear(SKColors.Black);

        for (int i = 0; i < uiTiles.Length; i++)
        {
            var tileInst = uiTiles[i];
            GlobalFunctions.DrawChar(tileInst.Id, tileInst.X, tileInst.Y + top, (Palette)tileInst.Palette);
        }
    }

    void DrawActiveInventory(int top)
    {
        var profile = _world.GetProfile();
        var x = ActiveItemX;
        var y = ActiveItemY + top;

        for (int i = 0; i < ActiveItems; i++)
        {
            var itemSlot = activeSlots[i];
            var itemId = activeItems[i];

            if (i == ArrowBowUISlot)
            {
                if (profile.Items[ItemSlot.Arrow] != 0)
                {
                    itemId = GlobalFunctions.ItemValueToItemId(_world, ItemSlot.Arrow);
                    GlobalFunctions.DrawItemNarrow(_world.Game, itemId, x, y);
                }
                if (profile.Items[ItemSlot.Bow] != 0)
                    GlobalFunctions.DrawItemNarrow(_world.Game, ItemId.Bow, x + 8, y);
            }
            else if (itemId != ItemId.None)
            {
                GlobalFunctions.DrawItemWide(_world.Game, itemId, x, y);
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

        var cursorPals = new Palette[] { Palette.BlueFgPalette, Palette.RedFgPalette };
        var cursorPal = cursorPals[(_world.Game.GetFrameCounter() >> 3) & 1];
        cursor.Draw(TileSheet.PlayerAndItems, x, y, cursorPal);
    }

    void DrawPassiveInventory(int top)
    {
        var profile = _world.GetProfile();

        for (int i = 0; i < PassiveItems; i++)
        {
            var slot = passiveItems[i].ItemSlot;
            var value = profile.Items[slot];

            if (value != 0)
            {
                var itemId = GlobalFunctions.ItemValueToItemId(slot, value);
                GlobalFunctions.DrawItem(_world.Game, itemId, passiveItems[i].X, PassiveItemY + top, 0);
            }
        }
    }

    void DrawCurrentSelection(int top)
    {
        var profile = _world.GetProfile();
        var curSlot = profile.SelectedItem;

        if (curSlot != 0)
        {
            var itemId = GlobalFunctions.ItemValueToItemId(_world, curSlot);
            GlobalFunctions.DrawItemWide(_world.Game, itemId, CurItemX, CurItemY + top);
        }
    }


    // struct TriforcePieceSpec
    // {
    //     byte  X;
    //     byte  Y;
    //     byte  OffTiles[2][2];
    //     byte  OnTiles[2][2];
    // };

    void DrawTriforce(int top)
    {
        // TODO var Triforce = new byte[] { 0x1D, 0x1B, 0x12, 0x0F, 0x18, 0x1B, 0x0C, 0x0E };
        // TODO
        // TODO static const TriforcePieceSpec pieceSpecs[] =
        // TODO {
        // TODO { 0x70, 0x70, { { 0xED, 0xE9 }, { 0xE9, 0x24 } }, { { 0xED, 0xE7 }, { 0xE7, 0xF5 } } },
        // TODO { 0x80, 0x70, { { 0xEA, 0xEE }, { 0x24, 0xEA } }, { { 0xE8, 0xEE }, { 0xF5, 0xE8 } } },
        // TODO { 0x60, 0x80, { { 0xED, 0xE9 }, { 0xE9, 0x24 } }, { { 0xED, 0xE7 }, { 0xE7, 0xF5 } } },
        // TODO { 0x90, 0x80, { { 0xEA, 0xEE }, { 0x24, 0xEA } }, { { 0xE8, 0xEE }, { 0xF5, 0xE8 } } },
        // TODO { 0x70, 0x80, { { 0x24, 0x24 }, { 0x24, 0x24 } }, { { 0xE5, 0xF5 }, { 0x24, 0xE5 } } },
        // TODO { 0x70, 0x80, { { 0x24, 0x24 }, { 0x24, 0x24 } }, { { 0xE8, 0x24 }, { 0xF5, 0xE8 } } },
        // TODO { 0x80, 0x80, { { 0x24, 0x24 }, { 0x24, 0x24 } }, { { 0xF5, 0xE6 }, { 0xE6, 0x24 } } },
        // TODO { 0x80, 0x80, { { 0x24, 0x24 }, { 0x24, 0x24 } }, { { 0x24, 0xE7 }, { 0xE7, 0xF5 } } },
        // TODO  };
        // TODO
        // TODO GlobalFunctions.DrawChar(0xED, 0x78, 0x68 + top, 1);
        // TODO GlobalFunctions.DrawChar(0xEE, 0x80, 0x68 + top, 1);
        // TODO
        // TODO GlobalFunctions.DrawChar(0xED, 0x68, 0x78 + top, 1);
        // TODO GlobalFunctions.DrawChar(0xEE, 0x90, 0x78 + top, 1);
        // TODO
        // TODO GlobalFunctions.DrawChar(0xED, 0x58, 0x88 + top, 1);
        // TODO GlobalFunctions.DrawChar(0xEE, 0xA0, 0x88 + top, 1);
        // TODO
        // TODO GlobalFunctions.DrawChar(0xEB, 0x50, 0x90 + top, 1);
        // TODO GlobalFunctions.DrawChar(0xEF, 0x58, 0x90 + top, 1);
        // TODO GlobalFunctions.DrawChar(0xF0, 0xA0, 0x90 + top, 1);
        // TODO GlobalFunctions.DrawChar(0xEC, 0xA8, 0x90 + top, 1);
        // TODO
        // TODO DrawString(Triforce, _countof(Triforce), 0x60, 0xA0 + top, 1);
        // TODO
        // TODO int x = 0x60;
        // TODO for (int i = 0; i < 8; i++, x += 8)
        // TODO {
        // TODO     GlobalFunctions.DrawChar(0xF1, x, 0x90 + top, 1);
        // TODO }
        // TODO
        // TODO uint pieces = _world.GetItem(ItemSlot.TriforcePieces);
        // TODO uint piece = pieces;
        // TODO
        // TODO for (int i = 0; i < 8; i++, piece >>= 1)
        // TODO {
        // TODO     const byte * tiles = nullptr;
        // TODO     uint have = piece & 1;
        // TODO
        // TODO     if (have)
        // TODO     {
        // TODO         tiles = (byte *)pieceSpecs[i].OnTiles;
        // TODO     }
        // TODO     else
        // TODO     {
        // TODO         tiles = (byte *)pieceSpecs[i].OffTiles;
        // TODO     }
        // TODO
        // TODO     for (int r = 0; r < 2; r++)
        // TODO     {
        // TODO         for (int c = 0; c < 2; c++, tiles++)
        // TODO         {
        // TODO             int x = pieceSpecs[i].X + (c * 8);
        // TODO             int y = pieceSpecs[i].Y + (r * 8) + top;
        // TODO             GlobalFunctions.DrawChar(*tiles, x, y, 1);
        // TODO         }
        // TODO     }
        // TODO }
        // TODO
        // TODO if ((pieces & 0x30) == 0x30)
        // TODO {
        // TODO     GlobalFunctions.DrawChar(0xF5, 0x70, 0x80 + top, 1);
        // TODO     GlobalFunctions.DrawChar(0xF5, 0x78, 0x88 + top, 1);
        // TODO }
        // TODO
        // TODO if ((pieces & 0xC0) == 0xC0)
        // TODO {
        // TODO     GlobalFunctions.DrawChar(0xF5, 0x88, 0x80 + top, 1);
        // TODO     GlobalFunctions.DrawChar(0xF5, 0x80, 0x88 + top, 1);
        // TODO }
    }

    unsafe void DrawMap(int top)
    {
        var Map = new byte[] { 0x16, 0x0A, 0x19 };
        var Compass = new byte[] { 0x0C, 0x18, 0x16, 0x19, 0x0A, 0x1C, 0x1C };
        var TopMapLine = new byte[]
            { 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xF5, 0xFD, 0xF5, 0xF5, 0xF5 };
        var BottomMapLine = new byte[]
            { 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5, 0xF5, 0xF5, 0xFE, 0xF5 };

        GlobalFunctions.DrawString(Map, 0x28, 0x58 + top, (Palette)1);
        GlobalFunctions.DrawString(Compass, 0x18, 0x80 + top, (Palette)1);
        GlobalFunctions.DrawString(TopMapLine, 0x60, 0x50 + top, (Palette)1);
        GlobalFunctions.DrawString(BottomMapLine, 0x60, 0x98 + top, (Palette)1);

        int y = 0x58 + top;
        for (int r = 0; r < 8; r++, y += 8)
        {
            int xx = 0;
            for (int c = 0; c < 4; c++, xx += 8)
            {
                GlobalFunctions.DrawChar(0xF5, 0x60 + xx, y, (Palette)1);
                GlobalFunctions.DrawChar(0xF5, 0xC0 + xx, y, (Palette)1);
            }
        }

        var levelInfo = _world.GetLevelInfo();
        bool hasMap = _world.HasCurrentMap();
        bool hasCompass = _world.HasCurrentCompass();

        if (hasMap)
            GlobalFunctions.DrawItemNarrow(_world.Game, ItemId.Map, 0x30, 0x68 + top);
        if (hasCompass)
            GlobalFunctions.DrawItemNarrow(_world.Game, ItemId.Compass, 0x30, 0x90 + top);

        int x = ActiveMapX;
        for (int c = 0; c < 8; c++, x += 8)
        {
            uint mapMaskByte = levelInfo.DrawnMap[c + 4];
            y = ActiveMapY + top;
            for (int r = 0; r < 8; r++, y += 8, mapMaskByte <<= 1)
            {
                int roomId = (r << 4) | (c - levelInfo.DrawnMapOffset + 0x10 + 4) & 0xF;
                var roomFlags = _world.GetUWRoomFlags(roomId);
                byte  tile = 0xF5;
                if ((mapMaskByte & 0x80) == 0x80 && roomFlags.GetVisitState())
                {
                    var doorSum = 0;
                    var doorBit = 8;
                    for (; doorBit != 0; doorBit >>= 1)
                    {
                        DoorType doorType = _world.GetDoorType(roomId, (Direction)doorBit);
                        if (doorType == DoorType.Open)
                            doorSum |= doorBit;
                        else if (roomFlags.GetDoorState((Direction)doorBit)
                            && (doorType == DoorType.Bombable
                            || doorType == DoorType.Key
                            || doorType == DoorType.Key2))
                            doorSum |= doorBit;
                    }
                    tile = (byte)(0xD0 + doorSum);
                }
                GlobalFunctions.DrawChar(tile, x, y, (Palette)1);
            }
        }

        int curRoomId = _world.curRoomId;
        int playerRow = (curRoomId >> 4) & 0xF;
        int playerCol = curRoomId & 0xF;
        playerCol = (playerCol + levelInfo.DrawnMapOffset) & 0xF;
        playerCol -= 4;

        y = ActiveMapY + top + playerRow * 8 + 3;
        x = ActiveMapX + playerCol * 8 + 2;
        GlobalFunctions.DrawChar(0xE0, x, y, Palette.Player);
    }

}