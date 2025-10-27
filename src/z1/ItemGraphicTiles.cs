using System;
using System.Collections.Immutable;
using SkiaSharp;
using z1.IO;
using z1.Render;

namespace z1;

// The top bit indicates it'll be directly translated to a byte when inside a string.
internal enum StringChar
{
    BoxHorizontal = Chars.BoxHorizontal | 0x80,
    BoxVertical = Chars.BoxVertical | 0x80,
}

internal readonly record struct ItemGraphics(AnimationId AnimId, Palette Palette, bool DoesFlash = false)
{
    // This can not be an ImmutableArray<ItemGraphics> because of this bug: https://github.com/dotnet/runtime/issues/104511
    public static ItemGraphics[] Items => [
        new ItemGraphics(AnimationId.BombItem, Palette.Blue),
        new ItemGraphics(AnimationId.SwordItem, Palette.Player),
        new ItemGraphics(AnimationId.SwordItem, Palette.Blue),
        new ItemGraphics(AnimationId.MSwordItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.FleshItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.RecorderItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.CandleItem, Palette.Blue),
        new ItemGraphics(AnimationId.CandleItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.ArrowItem, Palette.Player),
        new ItemGraphics(AnimationId.ArrowItem, Palette.Blue),
        new ItemGraphics(AnimationId.BowItem, Palette.Player),
        new ItemGraphics(AnimationId.MKeyItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.Raft, Palette.Player),
        new ItemGraphics(AnimationId.Ladder, Palette.Player),
        new ItemGraphics(AnimationId.PowerTriforce, Palette.RedBackground, true),
        new ItemGraphics(AnimationId.RuppeeItem, Palette.Blue),
        new ItemGraphics(AnimationId.WandItem, Palette.Blue),
        new ItemGraphics(AnimationId.BookItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.RingItem, Palette.Blue),
        new ItemGraphics(AnimationId.RingItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.BraceletItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.MapItem, Palette.Blue),
        new ItemGraphics(AnimationId.Compass, Palette.RedBackground),
        new ItemGraphics(AnimationId.MapItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.RuppeeItem, Palette.RedBackground, true),
        new ItemGraphics(AnimationId.KeyItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.HeartContainer, Palette.RedBackground),
        new ItemGraphics(AnimationId.TriforcePiece, Palette.RedBackground, true),
        new ItemGraphics(AnimationId.MShieldItem, Palette.Player),
        new ItemGraphics(AnimationId.Boomerang, Palette.Player),
        new ItemGraphics(AnimationId.Boomerang, Palette.Blue),
        new ItemGraphics(AnimationId.BottleItem, Palette.Blue),
        new ItemGraphics(AnimationId.BottleItem, Palette.RedBackground),
        new ItemGraphics(AnimationId.Clock, Palette.RedBackground),
        new ItemGraphics(AnimationId.Heart, Palette.RedBackground, true),
        new ItemGraphics(AnimationId.Fairy, Palette.RedBackground),
    ];
}

internal static class ItemGraphicTiles
{
    private static readonly DebugLog _log = new(nameof(ItemGraphicTiles));

    public static ItemId ItemValueToItemId(ItemSlot slot, int value)
    {
        static ReadOnlySpan<ItemId> EquippedItemIds() =>
        [
            (ItemId)0,    // Sword
            (ItemId)0xFF, // Bombs
            (ItemId)7,    // Arrow
            (ItemId)9,    // Bow
            (ItemId)5,    // Candle
            (ItemId)4,    // Recorder
            (ItemId)3,    // Food
            (ItemId)30,   // Potion
            (ItemId)15,   // Rod
            (ItemId)11,   // Raft
            (ItemId)16,   // Book
            (ItemId)17,   // Ring
            (ItemId)12,   // Ladder
            (ItemId)10,   // MagicKey
            (ItemId)19,   // Bracelet
            (ItemId)20,   // Letter
            ItemId.None,  // Compass
            ItemId.None,  // Map
            ItemId.None,  // Compass9
            ItemId.None,  // Map9
            ItemId.None,  // Clock
            ItemId.None,  // Rupees
            ItemId.None,  // Keys
            ItemId.None,  // HeartContainers
            ItemId.None,  // PartialHeart
            ItemId.None,  // TriforcePieces
            (ItemId)13,   // PowerTriforce
            (ItemId)28,   // Boomerang
        ];

        var itemValue = value;
        if (itemValue == 0) return ItemId.None;

        if (slot is ItemSlot.Bombs or ItemSlot.Letter)
        {
            itemValue = 1;
        }

        return (ItemId)(byte)(EquippedItemIds()[(int)slot] + itemValue);
    }

    public static ItemId ItemValueToItemId(Game game, ItemSlot slot)
    {
        return ItemValueToItemId(slot, game.World.Profile.Items.Get(slot));
    }

    public static ItemGraphics? GetItemGraphics(int itemId)
    {
        if (itemId >= 0x3F) return null;

        if (itemId >= ItemGraphics.Items.Length)
        {
            _log.Error($"GetItemGraphics: Invalid item id: {itemId}");
            itemId = 0;
#if DEBUG
            throw new Exception();
#endif
        }

        return ItemGraphics.Items[itemId];
    }
}

internal static class ItemGraphicTilesExtensions
{
    public static void DrawItem(this Graphics graphics, Game game, ItemId itemId, int x, int y, int width, DrawOrder order = DrawOrder.Sprites)
    {
        var itemGraphics = ItemGraphicTiles.GetItemGraphics((int)itemId);
        if (itemGraphics == null) return;

        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, itemGraphics.Value.AnimId);
        var xOffset = width != 0 ? (width - image.Animation.Width) / 2 : 0;

        var pal = itemGraphics.Value.DoesFlash
            ? (game.FrameCounter & 8) == 0 ? Palette.Blue : Palette.Red
            : itemGraphics.Value.Palette;

        image.Draw(graphics, TileSheet.PlayerAndItems, x + xOffset, y, pal, order);
    }

    public static void DrawItemNarrow(this Graphics graphics, Game game, ItemId itemId, int x, int y)
    {
        DrawItem(graphics, game, itemId, x, y, 8);
    }

    public static void DrawItemWide(this Graphics graphics, Game game, ItemId itemId, int x, int y)
    {
        DrawItem(graphics, game, itemId, x, y, 16);
    }

    public static void DrawChar(this Graphics graphics, Chars ch, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        DrawChar(graphics, (byte)ch, x, y, palette, flags);
    }
    public static void DrawChar(
        this Graphics graphics,
        int ch, int x, int y, Palette palette,
        DrawingFlags flags = DrawingFlags.NoTransparency,
        DrawOrder order = DrawOrder.Background)
    {
        var srcX = (ch % 16) * 8;
        var srcY = (ch / 16) * 8;

        graphics.DrawTile(TileSheet.Font, srcX, srcY, 8, 8, x, y, palette, flags, order);
    }

    public static void DrawChar(
        this Graphics graphics,
        int ch, int x, int y, int width, int height, Palette palette,
        DrawingFlags flags = DrawingFlags.NoTransparency,
        DrawOrder order = DrawOrder.Background)
    {
        var srcX = (ch % 16) * 8;
        var srcY = (ch / 16) * 8;

        graphics.DrawTile(TileSheet.Font, srcX, srcY, width, height, x, y, palette, flags, order);
    }

    public static void DrawChar(
        this Graphics graphics,
        int ch, int x, int y, ReadOnlySpan<SKColor> palette,
        DrawingFlags flags = DrawingFlags.NoTransparency,
        DrawOrder order = DrawOrder.Background)
    {
        var srcX = (ch % 16) * 8;
        var srcY = (ch / 16) * 8;

        graphics.DrawTile(TileSheet.Font, srcX, srcY, 8, 8, x, y, palette, flags, order);
    }

    public static void DrawString(this Graphics graphics, ReadOnlySpan<int> str, int x, int y, Palette palette, DrawOrder order = DrawOrder.Sprites)
    {
        foreach (var t in str)
        {
            DrawChar(graphics, t, x, y, palette, DrawingFlags.NoTransparency, order);
            x += 8;
        }
    }

    public static void DrawString(this Graphics graphics, ReadOnlySpan<char> str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar(graphics, t, x, y, palette);
            x += 8;
        }
    }

    public static void DrawString(this Graphics graphics, ImmutableArray<byte> str, int x, int y, Palette palette)
    {
        DrawString(graphics, str.AsSpan(), x, y, palette);
    }

    public static void DrawString(this Graphics graphics, ReadOnlySpan<byte> str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar(graphics, t, x, y, palette);
            x += 8;
        }
    }

    public static void DrawString(
        this Graphics graphics,
        string? str, int x, int y, Palette palette,
        DrawingFlags flags = DrawingFlags.NoTransparency,
        DrawOrder order = DrawOrder.Background)
    {
        if (str == null) return;

        foreach (var t in GameString.EnumerateText(str))
        {
            DrawChar(graphics, t, x, y, palette, flags, order);
            x += 8;
        }
    }

    public static void DrawChar(this Graphics graphics, char c, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        var t = GameString.ByteFromChar(c);
        DrawChar(graphics, t, x, y, palette, flags);
    }

    public static void DrawSparkle(this Graphics graphics, int x, int y, Palette palette, int frame, DrawOrder drawOrder = DrawOrder.Sprites)
    {
        var animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Sparkle);
        animator.DrawFrame(graphics, TileSheet.PlayerAndItems, x, y, palette, frame, drawOrder);
    }

    public static void DrawBox(this Graphics graphics, Rectangle rect)
    {
        DrawBox(graphics, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static void DrawBox(this Graphics graphics, int x, int y, int width, int height)
    {
        var x2 = x + width - 8;
        var y2 = y + height - 8;
        var xs = new[] { x, x2 };
        var ys = new[] { y, y2 };

        DrawChar(graphics, Chars.BoxTL, x, y, 0);
        DrawChar(graphics, Chars.BoxTR, x2, y, 0);
        DrawChar(graphics, Chars.BoxBL, x, y2, 0);
        DrawChar(graphics, Chars.BoxBR, x2, y2, 0);

        for (var i = 0; i < 2; i++)
        {
            for (var xx = x + 8; xx < x2; xx += 8)
            {
                DrawChar(graphics, Chars.BoxHorizontal, xx, ys[i], 0);
            }

            for (var yy = y + 8; yy < y2; yy += 8)
            {
                DrawChar(graphics, Chars.BoxVertical, xs[i], yy, 0);
            }
        }
    }

    public static void DrawHearts(this Graphics graphics, int heartsValue, int totalHearts, int left, int top)
    {
        var partialValue = heartsValue & 0xFF;
        var fullHearts = heartsValue >> 8;
        var fullAndPartialHearts = fullHearts;

        if (partialValue > 0)
        {
            fullAndPartialHearts++;

            if (partialValue >= 0x80)
            {
                fullHearts++;
            }
        }

        var x = left;
        var y = top;

        for (var i = 0; i < totalHearts; i++)
        {
            var tile = i switch
            {
                _ when i < fullHearts => Chars.FullHeart,
                _ when i < fullAndPartialHearts => Chars.HalfHeart,
                _ => Chars.EmptyHeart,
            };

            DrawChar(graphics, (byte)tile, x, y, Palette.RedBackground);

            x += 8;
            if ((i % 8) == 7)
            {
                x = left;
                y -= 8;
            }
        }
    }
}