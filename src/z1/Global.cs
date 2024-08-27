using System.Diagnostics;
using z1.Actors;
using z1.IO;
using z1.UI;

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

    public const float TWO_PI = 6.283185307179586f;
    public const float PI_OVER_16 = 0.196349540849362f;
    public const float PI_OVER_8 = 0.392699081698724f;
    public const float NEG_PI_OVER_8 = -0.392699081698724f;
}

internal enum NumberSign
{
    None,
    Negative,
    Positive,
}

internal enum ItemId
{
    Bomb,
    WoodSword,
    WhiteSword,
    MagicSword,
    Food,
    Recorder,
    BlueCandle,
    RedCandle,
    WoodArrow,
    SilverArrow,
    Bow,
    MagicKey,
    Raft,
    Ladder,
    PowerTriforce,
    FiveRupees,
    Rod,
    Book,
    BlueRing,
    RedRing,
    Bracelet,
    Letter,
    Compass,
    Map,
    Rupee,
    Key,
    HeartContainer,
    TriforcePiece,
    MagicShield,
    WoodBoomerang,
    MagicBoomerang,
    BluePotion,
    RedPotion,
    Clock,
    Heart,
    Fairy,

    MAX = 0x3F,
    None = MAX
}

internal readonly record struct ItemGraphics(AnimationId AnimId, Palette Palette, bool Flash = false)
{
    public Palette GetPalette() => Palette;
    public bool HasFlashAttr() => Flash;

    // This can not be an ImmutableArray<ItemGraphics> because of this bug: https://github.com/dotnet/runtime/issues/104511
    public static readonly ItemGraphics[] Items = [
        new(AnimationId.BombItem, Palette.Blue),
        new(AnimationId.SwordItem, Palette.Player),
        new(AnimationId.SwordItem, Palette.Blue),
        new(AnimationId.MSwordItem, Palette.Red),
        new(AnimationId.FleshItem, Palette.Red),
        new(AnimationId.RecorderItem, Palette.Red),
        new(AnimationId.CandleItem, Palette.Blue),
        new(AnimationId.CandleItem, Palette.Red),
        new(AnimationId.ArrowItem, Palette.Player),
        new(AnimationId.ArrowItem, Palette.Blue),
        new(AnimationId.BowItem, Palette.Player),
        new(AnimationId.MKeyItem, Palette.Red),
        new(AnimationId.Raft, Palette.Player),
        new(AnimationId.Ladder, Palette.Player),
        new(AnimationId.PowerTriforce, Palette.Red, true),
        new(AnimationId.RuppeeItem, Palette.Blue),
        new(AnimationId.WandItem, Palette.Blue),
        new(AnimationId.BookItem, Palette.Red),
        new(AnimationId.RingItem, Palette.Blue),
        new(AnimationId.RingItem, Palette.Red),
        new(AnimationId.BraceletItem, Palette.Red),
        new(AnimationId.MapItem, Palette.Blue),
        new(AnimationId.Compass, Palette.Red),
        new(AnimationId.MapItem, Palette.Red),
        new(AnimationId.RuppeeItem, Palette.Red, true),
        new(AnimationId.KeyItem, Palette.Red),
        new(AnimationId.HeartContainer, Palette.Red),
        new(AnimationId.TriforcePiece, Palette.Red, true),
        new(AnimationId.MShieldItem, Palette.Player),
        new(AnimationId.Boomerang, Palette.Player),
        new(AnimationId.Boomerang, Palette.Blue),
        new(AnimationId.BottleItem, Palette.Blue),
        new(AnimationId.BottleItem, Palette.Red),
        new(AnimationId.Clock, Palette.Red),
        new(AnimationId.Heart, Palette.Red, true),
        new(AnimationId.Fairy, Palette.Red),
    ];
}

internal enum Char
{
    FullHeart = 0xF2,
    HalfHeart = 0x65,
    EmptyHeart = 0x66,

    BoxTL = 0x69,
    BoxTR = 0x6B,
    BoxBL = 0x6E,
    BoxBR = 0x6D,
    BoxHorizontal = 0x6A,
    BoxVertical = 0x6C,

    X = 0x21,
    Space = 0x24,
    JustSpace = 0x25,
    Minus = 0x62,
    Plus = 0x64,
}

// The top bit indicates it'll be directly translated to a byte when inside a string.
internal enum StringChar
{
    BoxHorizontal = Char.BoxHorizontal |  0x80,
    BoxVertical = Char.BoxVertical | 0x80,
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

    public static ItemId ItemValueToItemId(World world, ItemSlot slot)
    {
        return ItemValueToItemId(slot, world.Profile.Items[slot]);
    }

    // JOE: TODO: These should take Game to make them uniform.
    public static Actor MakeProjectile(World world, ObjType type, int x, int y, Direction moving, ObjectSlot slot)
    {
        var origSlot = world.CurObjSlot;
        world.CurObjSlot = (int)slot;

        Actor obj = type switch {
            ObjType.FlyingRock => new FlyingRockProjectile(world.Game, x, y, moving),
            ObjType.PlayerSwordShot => new PlayerSwordProjectile(world.Game, x, y, moving),
            ObjType.Arrow => new ArrowProjectile(world.Game, x, y, moving),
            ObjType.MagicWave => new MagicWaveProjectile(world.Game, ObjType.MagicWave, x, y, moving),
            ObjType.MagicWave2 => new MagicWaveProjectile(world.Game, ObjType.MagicWave2, x, y, moving),
            _ => throw new Exception()
        };

        world.CurObjSlot = origSlot;
        return obj;
    }

    public static BoomerangProjectile MakeBoomerang(
        Game game, int x, int y, Direction moving, int distance, float speed, Actor? owner, ObjectSlot slot)
    {
        var origSlot = game.World.CurObjSlot;
        game.World.CurObjSlot = (int)slot;
        var boomerang = new BoomerangProjectile(game, x, y, moving, distance, speed, owner);
        game.World.CurObjSlot = origSlot;
        return boomerang;
    }

    public static Actor MakePerson(Game game, ObjType type, CaveSpec spec, int x, int y)
    {
        return new PersonActor(game, type, spec, x, y);
    }

    public static Actor MakeItem(Game game, ItemId itemId, int x, int y, bool isRoomItem)
    {
        if (itemId == ItemId.Fairy)
        {
            return new FairyActor(game, x, y);
        }

        return new ItemObjActor(game, itemId, isRoomItem, x, y);
    }

    public static ItemGraphics? GetItemGraphics(int itemId)
    {
        if (itemId >= 0x3F) return null;

        // JOE: TODO: Should this be an exception?
        if (itemId >= ItemGraphics.Items.Length)
        {
            _log.Write($"GetItemGraphics: Invalid item id: {itemId}");
            itemId = 0;
        }

        return ItemGraphics.Items[itemId];
    }

    public static void DrawItem(Game game, ItemId itemId, int x, int y, int width)
    {
        var graphics = GetItemGraphics((int)itemId);
        if (graphics == null) return;

        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, graphics.Value.AnimId);
        var xOffset = width != 0 ? (width - image.Animation.Width) / 2 : 0;

        var pal = graphics.Value.HasFlashAttr()
            ? (game.FrameCounter & 8) == 0 ? Palette.Blue : Palette.Red
            : graphics.Value.GetPalette();

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

    public static void DrawChar(Char ch, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency) => DrawChar((byte)ch, x, y, palette, flags);
    public static void DrawChar(int ch, int x, int y, Palette palette, DrawingFlags flags = DrawingFlags.NoTransparency)
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

        foreach (var t in ZeldaString.EnumerateText(str))
        {
            DrawChar(t, x, y, palette, flags);
            x += 8;
        }
    }

    public static void DrawSparkle(int x, int y, Palette palette, int frame)
    {
        var animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Sparkle);
        animator.DrawFrame(TileSheet.PlayerAndItems, x, y, palette, frame);
    }

    public static void DrawBox(int x, int y, int width, int height)
    {
        var x2 = x + width - 8;
        var y2 = y + height - 8;
        var xs = new[] { x, x2 };
        var ys = new[] { y, y2 };

        DrawChar(Char.BoxTL, x, y, 0);
        DrawChar(Char.BoxTR, x2, y, 0);
        DrawChar(Char.BoxBL, x, y2, 0);
        DrawChar(Char.BoxBR, x2, y2, 0);

        for (var i = 0; i < 2; i++)
        {
            for (var xx = x + 8; xx < x2; xx += 8)
            {
                DrawChar(Char.BoxHorizontal, xx, ys[i], 0);
            }

            for (var yy = y + 8; yy < y2; yy += 8)
            {
                DrawChar(Char.BoxVertical, xs[i], yy, 0);
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
                _ when i < fullHearts => Char.FullHeart,
                _ when i < fullAndPartialHearts => Char.HalfHeart,
                _ => Char.EmptyHeart,
            };

            DrawChar((byte)tile, x, y, Palette.RedBgPalette);

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
        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, palette);
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
        ManhandlaActor.ClearRoomData();
        Statues.Init();
    }

    // TODO: Make these use a saner .ToString/ZeldaString method that allows arbitrary lengths.
    public static byte[] NumberToStringR(int number, NumberSign sign, ref Span<byte> charBuf)
    {
        return NumberToStringR((byte)number, sign, ref charBuf);
    }

    public static byte[] NumberToStringR(byte number, NumberSign sign, ref Span<byte> charBuf)
    {
        // JOE: TODO: Make this 0 allocation. Return the length, I believe? Callers can use charBuf.
        // var signstr = sign switch
        // {
        //     NumberSign.Negative => "-",
        //     NumberSign.Positive => "+",
        //     _ => "",
        // };
        //
        // var length = number switch
        // {
        //     < 10 => 1,
        //     < 100 => 2,
        //     _ => 3,
        // };
        //
        // if (sign != NumberSign.None) ++length;
        //
        // var str = ZeldaString.EnumerateText((signstr + number).PadLeft(charBuf.Length - length)).ToArray();
        // str.CopyTo(charBuf);
        // return str;

        var bufLen = charBuf.Length;

        Debug.Assert(bufLen >= 3);
        Debug.Assert(sign == NumberSign.None || bufLen >= 4);

        var n = number;
        var pChar = bufLen - 1;

        while (true)
        {
            var digit = n % 10;
            charBuf[pChar] = (byte)digit;
            pChar--;
            n /= 10;
            if (n == 0) break;
        }

        if (sign != NumberSign.None && number != 0)
        {
            charBuf[pChar] = sign == NumberSign.Negative ? (byte)Char.Minus : (byte)Char.Plus;
            pChar--;
        }

        var strLeft = pChar + 1;

        for (; pChar >= 0; pChar--)
        {
            charBuf[pChar] = (byte)Char.Space;
        }

        charBuf = charBuf[strLeft..];

        return charBuf.ToArray();
    }
}