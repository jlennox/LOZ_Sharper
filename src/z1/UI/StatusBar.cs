using System.Collections.Immutable;
using SkiaSharp;
using z1.Render;

namespace z1.UI;

[Flags]
public enum StatusBarFeatures
{
    None = 0,
    Counters = 1,
    Equipment = 2,
    MapCursors = 4,

    All = Counters | Equipment | MapCursors,
    EquipmentAndMap = Equipment | MapCursors,
}

internal readonly record struct TileInst(byte Id, byte X, byte Y, byte PaletteId)
{
    public readonly Palette Palette => (Palette)PaletteId;
}

internal sealed class StatusBar
{
    public const int StatusBarHeight = 0x40;
    public const int StatusBarWidth = World.TileMapWidth;

    public const int LevelNameX = 16;
    public const int LevelNameY = 16;

    public const int MiniMapX = 16;
    public const int MiniMapY = 24;
    public const int MiniMapColumnOffset = 4;

    public const int OWMapTileWidth = 4;
    public const int OWMapTileHeight = 4;
    public const int UWMapTileWidth = 8;
    public const int UWMapTileHeight = 4;

    public const int CountersX = 0x60;
    public const int EquipmentY = 0x20;
    public const int HeartsX = 0xB8;
    public const int HeartsY = 0x30;

    public const int Tile_FullHeart = 0xF2;
    public const int Tile_HalfHeart = 0x65;
    public const int Tile_EmptyHeart = 0x66;

    private static readonly ImmutableArray<TileInst> _uiTiles = [
        new(0xF7, 88, 24, 1),
        new(0xF9, 88, 40, 1),
        new(0x61, 88, 48, 0),

        // Item A Box
        new(0x69, 120, 24, 0),
        new(0x0B, 128, 24, 0),
        new(0x6B, 136, 24, 0),
        new(0x6C, 120, 32, 0),
        new(0x6C, 136, 32, 0),
        new(0x6C, 120, 40, 0),
        new(0x6C, 136, 40, 0),
        new(0x6E, 120, 48, 0),
        new(0x6A, 128, 48, 0),
        new(0x6D, 136, 48, 0),

        // Item B Box
        new(0x69, 120+24, 24, 0),
        new(0x0A, 128+24, 24, 0),
        new(0x6B, 136+24, 24, 0),
        new(0x6C, 120+24, 32, 0),
        new(0x6C, 136+24, 32, 0),
        new(0x6C, 120+24, 40, 0),
        new(0x6C, 136+24, 40, 0),
        new(0x6E, 120+24, 48, 0),
        new(0x6A, 128+24, 48, 0),
        new(0x6D, 136+24, 48, 0),

        // -LIFE-
        new(0x62, 184, 24, 1),
        new(0x15, 192, 24, 1),
        new(0x12, 200, 24, 1),
        new(0x0F, 208, 24, 1),
        new(0x0E, 216, 24, 1),
        new(0x62, 224, 24, 1),
    ];

    private readonly World _world;
    private StatusBarFeatures _features;

    public StatusBar(World world)
    {
        _world = world;
        _features = StatusBarFeatures.All;
    }

    public void EnableFeatures(StatusBarFeatures features, bool enable)
    {
        _features = enable ? (_features | features) : (_features ^ features);
    }

    public void Draw(int baseY)
    {
        Draw(baseY, SKColors.Black);
    }

    public void Draw(int baseY, SKColor backColor)
    {
        using var _ = Graphics.SetClip(0, baseY, StatusBarWidth, StatusBarHeight);
        Graphics.Clear(backColor);

        foreach (var tileInst in _uiTiles)
        {
            DrawTile(tileInst.Id, tileInst.X, tileInst.Y + baseY, tileInst.Palette);
        }

        DrawMiniMap(baseY);
        DrawItems(baseY);
    }

    private static readonly byte[] _levelStr = [0x15, 0x0E, 0x1F, 0x0E, 0x15, 0x62, 0];

    private void DrawMiniMap(int baseY)
    {
        var roomId = _world.CurRoomId;
        var row = (roomId >> 4) & 0xF;
        var col = roomId & 0xF;
        var cursorX = MiniMapX;
        var cursorY = MiniMapY + baseY;
        var showCursor = true;

        if (_world.IsOverworld())
        {
            DrawOWMiniMap(baseY);

            cursorX += col * OWMapTileWidth;
            cursorY += row * OWMapTileHeight;
        }
        else
        {
            var levelInfo = _world.GetLevelInfo();

            _levelStr[6] = levelInfo.LevelNumber;
            GlobalFunctions.DrawString(_levelStr, LevelNameX, baseY + LevelNameY, 0);
            DrawUWMiniMap(baseY);

            col = (col + levelInfo.DrawnMapOffset) & 0x0F;
            col -= MiniMapColumnOffset;

            cursorX += col * UWMapTileWidth + 2;
            cursorY += row * UWMapTileHeight;

            if (!_world.IsUWMain(roomId))
            {
                showCursor = false;
            }

            if (_features.HasFlag(StatusBarFeatures.MapCursors) && _world.HasCurrentCompass())
            {
                int triforceRoomId = levelInfo.TriforceRoomId;
                var triforceRow = (triforceRoomId >> 4) & 0x0F;
                var triforceCol = triforceRoomId & 0x0F;
                col = (triforceCol + levelInfo.DrawnMapOffset) & 0x0F;
                col -= MiniMapColumnOffset;
                var triforceX = MiniMapX + col * UWMapTileWidth + 2;
                var triforceY = MiniMapY + baseY + triforceRow * UWMapTileHeight;
                var palette = Palette.LevelFgPalette;

                if (!_world.GotItem(triforceRoomId))
                {
                    if ((_world.Game.FrameCounter & 0x10) == 0)
                    {
                        palette = Palette.RedFgPalette;
                    }
                }

                DrawTile(0xE0, triforceX, triforceY, palette);
            }
        }

        if (_features.HasFlag(StatusBarFeatures.MapCursors))
        {
            if (showCursor)
            {
                DrawTile(0xE0, cursorX, cursorY, Palette.Player);
            }
        }
    }

    private static void DrawTile(int tile, int x, int y, Palette palette)
    {
        GlobalFunctions.DrawChar((byte)tile, x, y, palette);
    }

    private static void DrawOWMiniMap(int baseY)
    {
        var y = MiniMapY + baseY;

        for (var i = 0; i < 4; i++)
        {
            var x = MiniMapX;
            for (var j = 0; j < 8; j++)
            {
                DrawTile(0xF5, x, y, 0);
                x += 8;
            }
            y += 8;
        }
    }

    private unsafe void DrawUWMiniMap(int baseY)
    {
        if (!_world.HasCurrentMap()) return;

        var levelInfo = _world.GetLevelInfo();

        var x = MiniMapX;

        for (var c = 0; c < 12; c++)
        {
            int b = levelInfo.DrawnMap[c + MiniMapColumnOffset];
            var y = baseY + MiniMapY;

            for (var r = 0; r < 8; r++)
            {
                if ((b & 0x80) != 0)
                {
                    Graphics.DrawTile(TileSheet.Font, 0x7 * 8, 0x6 * 8, 8, 4, x, y, 0, 0);
                }
                b <<= 1;
                y += 4;
            }
            x += 8;
        }
    }

    private void DrawCount(ItemSlot itemSlot, int x, int y)
    {
        var charBuf = new byte[4].AsSpan();

        var count = _world.GetItem(itemSlot);
        var strLeft = GlobalFunctions.NumberToStringR((byte)count, NumberSign.None, ref charBuf);

        if (count < 100)
        {
            var newStrLeft = new byte[strLeft.Length + 1];
            newStrLeft[0] = (byte)Chars.X;
            Array.Copy(strLeft, 0, newStrLeft, 1, strLeft.Length);
            strLeft = newStrLeft;
        }

        GlobalFunctions.DrawString(strLeft, x, y, 0);
    }

    private void DrawItems(int baseY)
    {
        if (_features.HasFlag(StatusBarFeatures.Counters))
        {
            if (_world.GetItem(ItemSlot.MagicKey) != 0)
            {
                var xa = new byte[] { (byte)Chars.X, 0x0A };
                GlobalFunctions.DrawString(xa, CountersX, 0x28 + baseY, 0);
            }
            else
            {
                DrawCount(ItemSlot.Keys, CountersX, 0x28 + baseY);
            }

            DrawCount(ItemSlot.Bombs, CountersX, 0x30 + baseY);
            DrawCount(ItemSlot.Rupees, CountersX, 0x18 + baseY);

            DrawHearts(baseY);
        }

        if (_features.HasFlag(StatusBarFeatures.Equipment))
        {
            DrawSword(baseY);
            DrawItemB(baseY);
        }
    }

    private void DrawSword(int baseY)
    {
        var swordValue = _world.GetItem(ItemSlot.Sword);
        if (swordValue == 0) return;

        var itemId = GlobalFunctions.ItemValueToItemId(ItemSlot.Sword, swordValue);
        GlobalFunctions.DrawItemNarrow(_world.Game, itemId, 0x98, EquipmentY + baseY);
    }

    private void DrawItemB(int baseY)
    {
        var profile = _world.Profile;
        if (profile.SelectedItem == 0) return;

        var itemValue = profile.Items[profile.SelectedItem];
        if (itemValue == 0) return;

        var itemId = GlobalFunctions.ItemValueToItemId(profile.SelectedItem, itemValue);

        GlobalFunctions.DrawItemNarrow(_world.Game, itemId, 0x80, EquipmentY + baseY);
    }

    private void DrawHearts(int baseY)
    {
        var totalHearts = _world.GetItem(ItemSlot.HeartContainers);
        var heartsValue = _world.Profile.Hearts;
        var y = HeartsY + baseY;
        GlobalFunctions.DrawHearts(heartsValue, totalHearts, HeartsX, y);
    }
}
