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

internal enum TileInstType
{
    Normal, SwordButton
}

internal readonly record struct TileInst(byte Id, byte X, byte Y, byte PaletteId, TileInstType Type = TileInstType.Normal)
{
    public Palette Palette => (Palette)PaletteId;
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

    public const int TileFullHeart = 0xF2;
    public const int TileHalfHeart = 0x65;
    public const int TileEmptyHeart = 0x66;

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
        new(0x0A, 128+24, 24, 0, TileInstType.SwordButton),
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
            var tileId = tileInst.Id;

            // If the sword is blocked, show "X" instead of the usual "B".
            if (tileInst.Type == TileInstType.SwordButton)
            {
                if (_world.SwordBlocked || _world.GetStunTimer(StunTimerSlot.NoSword) != 0)
                {
                    tileId = (byte)GameString.ByteFromChar('X');
                }
            }

            DrawTile(tileId, tileInst.X, tileInst.Y + baseY, tileInst.Palette, DrawOrder.Background);
        }

        DrawMiniMap(baseY);
        DrawItems(baseY);
    }

    private void DrawMiniMap(int baseY)
    {
        if (!_world.CurrentRoom.Settings.HideMap)
        {
            DrawMiniMapInner(baseY);
        }

        if (_world.CurrentWorld.LevelString != null)
        {
            GlobalFunctions.DrawString(_world.CurrentWorld.LevelString, LevelNameX, baseY + LevelNameY, 0);
        }
    }

    private static void DrawTile(int tile, int x, int y, Palette palette, DrawOrder order)
    {
        GlobalFunctions.DrawChar((byte)tile, x, y, palette, 0, order);
    }

    private static void DrawTile(int tile, int x, int y, MiniMapSettings settings)
    {
        GlobalFunctions.DrawChar((byte)tile, x, y, settings.TileWidth, settings.TileHeight, settings.Palette);
    }

    private static void DrawUWTile(int tile, int x, int y, MiniMapSettings settings)
    {
        Graphics.DrawTile(TileSheet.Font, 0x7 * 8, 0x6 * 8, 8, 4, x, y, settings.Palette, 0, DrawOrder.Background);
    }

    private delegate void DrawMiniMapTileDelegate(int tile, int x, int y, MiniMapSettings settings);

    private readonly record struct MiniMapSettings(int Tile, DrawMiniMapTileDelegate DrawTileFn, int CursorXOffset, int TileWidth, int TileHeight, Palette Palette, bool RequiresMap)
    {
        public static readonly MiniMapSettings Overworld = new(0xF5, DrawTile, 0, OWMapTileWidth, OWMapTileHeight, 0, false);
        public static readonly MiniMapSettings Underworld = new(0xF5, DrawUWTile, 2, UWMapTileWidth, UWMapTileHeight, 0, true);
    }

    private void DrawMiniMapInner(int baseY)
    {
        var settings = _world.IsOverworld() ? MiniMapSettings.Overworld : MiniMapSettings.Underworld;
        if (settings.RequiresMap && !_world.HasCurrentMap()) return;

        const int maxMapHeight = 8;
        // I hate this :)
        var maxMapWidth = (0x10 * OWMapTileWidth) / settings.TileWidth;

        var world = _world.CurrentWorld;
        var map = world.GameWorldMap;
        var grid = map.RoomGrid;

        var showMapCursors = _features.HasFlag(StatusBarFeatures.MapCursors);
        var showTriforce = showMapCursors && _world.HasCurrentCompass();

        var y = baseY + MiniMapY + (maxMapHeight - map.Height) * settings.TileHeight; // bottom align
        var basex = MiniMapX + (maxMapWidth - map.Width) / 2 * settings.TileWidth; // center align

        // var yoff = (maxMapHeight - map.Height);
        // var xoff = (maxMapWidth - map.Width) / 2;
        //
        // var infblock = _world._infoBlock;
        // var drawnMap = infblock.DrawnMap;

        for (var yi = 0; yi < map.Height; yi++, y += settings.TileHeight)
        {
            var x = basex;
            for (var xi = 0; xi < map.Width; xi++, x += settings.TileWidth)
            {
                var room = grid[xi, yi];
                if (room == null) continue;

                // int b = drawnMap[xi + MiniMapColumnOffset + xoff] << (yi + yoff);
                //if ((b & 0x80) != 0)
                // We still want to display the player's cursor in room's hidden from the map.
                if (!room.Settings.HiddenFromMap)
                {
                    var tile = settings.Tile;
                    settings.DrawTileFn(tile, x, y, settings);

                    // If this looks wrong, it's likely related to MiniMapColumnOffset.
                    if (showTriforce && room.IsTriforceRoom)
                    {
                        var palette = (_world.Game.FrameCounter & 0x10) == 0 && room.HasTriforce
                            ? Palette.Red
                            : Palette.SeaPal;

                        DrawTile(0xE0, x + 2, y, palette, DrawOrder.Sprites);
                    }
                }

                if (showMapCursors && room == _world.CurrentRoom && !room.HidePlayerMapCursor)
                {
                    DrawTile(0xE0, x + settings.CursorXOffset, y, Palette.Player, DrawOrder.Foreground);
                }
            }
        }
    }

    private void DrawCount(ItemSlot itemSlot, int x, int y)
    {
        var count = _world.GetItem(itemSlot);
        if (count < 100)
        {
            GlobalFunctions.DrawChar((byte)Chars.X, x, y, 0);
            x += 8;
        }

        Span<char> charBuf = stackalloc char[16];
        var str = GameString.NumberToString((byte)count, NumberSign.None, charBuf, 0);
        GlobalFunctions.DrawString(str, x, y, 0);
    }

    private void DrawItems(int baseY)
    {
        if (_features.HasFlag(StatusBarFeatures.Counters))
        {
            if (_world.GetItem(ItemSlot.MagicKey) != 0)
            {
                ReadOnlySpan<byte> xa = [(byte)Chars.X, 0x0A];
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

        var itemValue = profile.Items.Get(profile.SelectedItem);
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
