using System.Collections.Immutable;
using System.Diagnostics;
using z1.Actors;
using z1.Render;
using z1.IO;
using SkiaSharp;

namespace z1;

internal static class Global
{
    public const int StdViewWidth = 256;
    public const int StdViewHeight = 240;

    public const int PaletteCount = 8;
    public const int PaletteLength = 4;
    public const int ForegroundPalCount = 4;
    public const int BackgroundPalCount = 4;

    public const int LevelBlockWidth = 16;
    public const int LevelBlockHeight = 8;
    public const int LevelBlockRooms = 128;

    public const int SysPaletteLength = 64;

    public const int StdSpeed = 0x20;
}

internal static class Pi
{
    public const float TwoPi = 6.283185307179586f;
    public const float PiOver16 = 0.196349540849362f;
    public const float PiOver8 = 0.392699081698724f;
    public const float NegPiOver8 = -0.392699081698724f;
}

internal readonly record struct ItemGraphics(AnimationId AnimId, Palette Palette, bool DoesFlash = false)
{
    // This can not be an ImmutableArray<ItemGraphics> because of this bug: https://github.com/dotnet/runtime/issues/104511
    public static readonly ItemGraphics[] Items = [
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

// The top bit indicates it'll be directly translated to a byte when inside a string.
internal enum StringChar
{
    BoxHorizontal = Chars.BoxHorizontal |  0x80,
    BoxVertical = Chars.BoxVertical | 0x80,
}

internal static class GlobalFunctions
{
    private static readonly DebugLog _log = new(nameof(GlobalFunctions));

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
        return ItemValueToItemId(slot, game.World.Profile.GetItem(slot));
    }

    public static Actor MakeProjectile(Game game, ObjType type, int x, int y, Direction moving, Actor actor)
    {
        return type switch
        {
            ObjType.FlyingRock => new FlyingRockProjectile(game, x, y, moving, actor),
            ObjType.PlayerSwordShot => new PlayerSwordProjectile(game, x, y, moving, actor),
            ObjType.Arrow => new ArrowProjectile(game, x, y, moving, actor),
            ObjType.MagicWave => new MagicWaveProjectile(game, ObjType.MagicWave, x, y, moving, actor),
            ObjType.MagicWave2 => new MagicWaveProjectile(game, ObjType.MagicWave2, x, y, moving, actor),
            _ => throw new Exception()
        };
    }

    public static BoomerangProjectile MakeBoomerang(
        Game game, int x, int y, Direction moving, int distance, float speed, Actor owner)
    {
        return new BoomerangProjectile(game, x, y, moving, distance, speed, owner);
    }

    public static ItemGraphics? GetItemGraphics(int itemId)
    {
        if (itemId >= 0x3F) return null;

        if (itemId >= ItemGraphics.Items.Length)
        {
            _log.Write($"GetItemGraphics: Invalid item id: {itemId}");
            itemId = 0;
#if DEBUG
            throw new Exception();
#endif
        }

        return ItemGraphics.Items[itemId];
    }

    public static void DrawItem(Game game, ItemId itemId, int x, int y, int width)
    {
        var graphics = GetItemGraphics((int)itemId);
        if (graphics == null) return;

        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, graphics.Value.AnimId);
        var xOffset = width != 0 ? (width - image.Animation.Width) / 2 : 0;

        var pal = graphics.Value.DoesFlash
            ? (game.FrameCounter & 8) == 0 ? Palette.Blue : Palette.Red
            : graphics.Value.Palette;

        image.Draw(TileSheet.PlayerAndItems, x + xOffset, y, pal);
    }

    public static void DrawItemNarrow(Game game, ItemId itemId, int x, int y)
    {
        DrawItem(game, itemId, x, y, 8);
    }

    public static void DrawItemWide(Game game, ItemId itemId, int x, int y)
    {
        DrawItem(game, itemId, x, y, 16);
    }

    public static void DrawChar(Chars ch, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency) => DrawChar((byte)ch, x, y, palette, flags);
    public static void DrawChar(int ch, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        var srcX = (ch % 16) * 8;
        var srcY = (ch / 16) * 8;

        Graphics.DrawTile(TileSheet.Font, srcX, srcY, 8, 8, x, y, palette, flags);
    }

    public static void DrawChar(int ch, int x, int y, int width, int height, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        var srcX = (ch % 16) * 8;
        var srcY = (ch / 16) * 8;

        Graphics.DrawTile(TileSheet.Font, srcX, srcY, width, height, x, y, palette, flags);
    }

    public static void DrawChar(int ch, int x, int y, ReadOnlySpan<SKColor> palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        var srcX = (ch % 16) * 8;
        var srcY = (ch / 16) * 8;

        Graphics.DrawTile(TileSheet.Font, srcX, srcY, 8, 8, x, y, palette, flags);
    }

    public static void DrawString(ReadOnlySpan<int> str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar(t, x, y, palette);
            x += 8;
        }
    }

    public static void DrawString(ReadOnlySpan<char> str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar(t, x, y, palette);
            x += 8;
        }
    }

    public static void DrawString(ImmutableArray<byte> str, int x, int y, Palette palette) => DrawString(str.AsSpan(), x, y, palette);

    public static void DrawString(ReadOnlySpan<byte> str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar(t, x, y, palette);
            x += 8;
        }
    }

    public static void DrawString(string? str, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        if (str == null) return;

        foreach (var t in GameString.EnumerateText(str))
        {
            DrawChar(t, x, y, palette, flags);
            x += 8;
        }
    }

    public static void DrawChar(char c, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
    {
        var t = GameString.ByteFromChar(c);
        DrawChar(t, x, y, palette, flags);
    }

    public static void DrawSparkle(int x, int y, Palette palette, int frame)
    {
        var animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Sparkle);
        animator.DrawFrame(TileSheet.PlayerAndItems, x, y, palette, frame);
    }

    public static void DrawBox(Rectangle rect)
    {
        DrawBox(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static void DrawBox(int x, int y, int width, int height)
    {
        var x2 = x + width - 8;
        var y2 = y + height - 8;
        var xs = new[] { x, x2 };
        var ys = new[] { y, y2 };

        DrawChar(Chars.BoxTL, x, y, 0);
        DrawChar(Chars.BoxTR, x2, y, 0);
        DrawChar(Chars.BoxBL, x, y2, 0);
        DrawChar(Chars.BoxBR, x2, y2, 0);

        for (var i = 0; i < 2; i++)
        {
            for (var xx = x + 8; xx < x2; xx += 8)
            {
                DrawChar(Chars.BoxHorizontal, xx, ys[i], 0);
            }

            for (var yy = y + 8; yy < y2; yy += 8)
            {
                DrawChar(Chars.BoxVertical, xs[i], yy, 0);
            }
        }
    }

    public static void DrawHearts(int heartsValue, int totalHearts, int left, int top)
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

            DrawChar((byte)tile, x, y, Palette.RedBackground);

            x += 8;
            if ((i % 8) == 7)
            {
                x = left;
                y -= 8;
            }
        }
    }

    public static void SetPilePalette()
    {
        ReadOnlySpan<byte> palette = [0, 0x27, 0x06, 0x16];
        Graphics.SetPaletteIndexed(Palette.SeaPal, palette);
    }

    public static void PlayItemSound(Game game, ItemId itemId)
    {
        var soundId = SoundEffect.Item;

        switch (itemId)
        {
            case ItemId.Heart:
            case ItemId.Key:
                soundId = SoundEffect.KeyHeart;
                break;

            case ItemId.FiveRupees:
            case ItemId.Rupee:
                soundId = SoundEffect.Cursor;
                break;

            case ItemId.PowerTriforce:
                return;
        }

        game.Sound.PlayEffect(soundId);
    }

    public static void ClearRoomMonsterData()
    {
        RedLeeverActor.ClearRoomData();
        BouldersActor.ClearRoomData();
        Statues.Init();
    }
}