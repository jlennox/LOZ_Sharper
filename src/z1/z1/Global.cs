using System.Diagnostics;
using z1.Actors;

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

internal readonly record struct ItemGraphics(AnimationId AnimId, Palette PaletteAttrs)
{
    public const byte FlashPalAttr = 0x80;

    public Palette GetPalette() => PaletteAttrs & Palette.Mask;
    public bool HasFlashAttr() => ((int)PaletteAttrs & FlashPalAttr) != 0;

    public static readonly ItemGraphics[] Items = {
        new ItemGraphics(AnimationId.BombItem, Palette.Blue),
        new ItemGraphics(AnimationId.SwordItem, Palette.Player),
        new ItemGraphics(AnimationId.SwordItem, Palette.Blue),
        new ItemGraphics(AnimationId.MSwordItem, Palette.Red),
        new ItemGraphics(AnimationId.FleshItem, Palette.Red),
        new ItemGraphics(AnimationId.RecorderItem, Palette.Red),
        new ItemGraphics(AnimationId.CandleItem, Palette.Blue),
        new ItemGraphics(AnimationId.CandleItem, Palette.Red),
        new ItemGraphics(AnimationId.ArrowItem, Palette.Player),
        new ItemGraphics(AnimationId.ArrowItem, Palette.Blue),
        new ItemGraphics(AnimationId.BowItem, Palette.Player),
        new ItemGraphics(AnimationId.MKeyItem, Palette.Red),
        new ItemGraphics(AnimationId.Raft, Palette.Player),
        new ItemGraphics(AnimationId.Ladder, Palette.Player),
        new ItemGraphics(AnimationId.PowerTriforce, Palette.Red | Palette.FlashAttr),
        new ItemGraphics(AnimationId.RuppeeItem, Palette.Blue),
        new ItemGraphics(AnimationId.WandItem, Palette.Blue),
        new ItemGraphics(AnimationId.BookItem, Palette.Red),
        new ItemGraphics(AnimationId.RingItem, Palette.Blue),
        new ItemGraphics(AnimationId.RingItem, Palette.Red),
        new ItemGraphics(AnimationId.BraceletItem, Palette.Red),
        new ItemGraphics(AnimationId.MapItem, Palette.Blue),
        new ItemGraphics(AnimationId.Compass, Palette.Red),
        new ItemGraphics(AnimationId.MapItem, Palette.Red),
        new ItemGraphics(AnimationId.RuppeeItem, Palette.Red | Palette.FlashAttr),
        new ItemGraphics(AnimationId.KeyItem, Palette.Red),
        new ItemGraphics(AnimationId.HeartContainer, Palette.Red),
        new ItemGraphics(AnimationId.TriforcePiece, Palette.Red | Palette.FlashAttr),
        new ItemGraphics(AnimationId.MShieldItem, Palette.Player),
        new ItemGraphics(AnimationId.Boomerang, Palette.Player),
        new ItemGraphics(AnimationId.Boomerang, Palette.Blue),
        new ItemGraphics(AnimationId.BottleItem, Palette.Blue),
        new ItemGraphics(AnimationId.BottleItem, Palette.Red),
        new ItemGraphics(AnimationId.Clock, Palette.Red),
        new ItemGraphics(AnimationId.Heart, Palette.Red | Palette.FlashAttr),
        new ItemGraphics(AnimationId.Fairy, Palette.Red),
    };
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

    X = 0x21,
    Space = 0x24,
    JustSpace = 0x25,
    Minus = 0x62,
    Plus = 0x64,
}

internal static class GlobalFunctions
{
    public static ItemId ItemValueToItemId(ItemSlot slot, int value)
    {
        static ReadOnlySpan<ItemId> equippedItemIds() => new[]
        {
            (ItemId)0,          // Sword
            (ItemId)0xFF,       // Bombs
            (ItemId)7,          // Arrow
            (ItemId)9,          // Bow
            (ItemId)5,          // Candle
            (ItemId)4,          // Recorder
            (ItemId)3,          // Food
            (ItemId)30,         // Potion
            (ItemId)15,         // Rod
            (ItemId)11,         // Raft
            (ItemId)16,         // Book
            (ItemId)17,         // Ring
            (ItemId)12,         // Ladder
            (ItemId)10,         // MagicKey
            (ItemId)19,         // Bracelet
            (ItemId)20,         // Letter
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
            (ItemId)13,         // PowerTriforce
            (ItemId)28,         // Boomerang
        };

        var itemValue = value;

        if (itemValue == 0) return ItemId.None;

        if (slot is ItemSlot.Bombs or ItemSlot.Letter)
        {
            itemValue = 1;
        }

        return (ItemId)(byte)(equippedItemIds()[(int)slot] + itemValue);
    }

    public static ItemId ItemValueToItemId(World world, ItemSlot slot)
    {
        var profile = world.GetProfile();
        return ItemValueToItemId(slot, profile.Items[slot]);
    }

    // JOE: TODO: These should take Game to make them uniform.
    public static Actor MakeProjectile(World world, ObjType type, int x, int y, Direction moving, ObjectSlot slot)
    {
        Actor? obj;
        var origSlot = world.CurObjSlot;
        world.CurObjSlot = (int)slot;

        switch (type)
        {
            case ObjType.FlyingRock: obj = new FlyingRockProjectile(world.Game, x, y, moving); break;
            case ObjType.PlayerSwordShot: obj = new PlayerSwordProjectile(world.Game, x, y, moving); break;
            case ObjType.Arrow: obj = new ArrowProjectile(world.Game, x, y, moving); break;
            case ObjType.MagicWave: obj = new MagicWaveProjectile(world.Game, ObjType.MagicWave, x, y, moving); break;
            case ObjType.MagicWave2: obj = new MagicWaveProjectile(world.Game, ObjType.MagicWave2, x, y, moving); break;
            default: throw new Exception();
        }

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

    public static ItemGraphics? GetItemGraphics( int itemId )
    {
        if (itemId >= 0x3F )
            return null;
        if (itemId >= ItemGraphics.Items.Length)
            itemId = 0;

        return ItemGraphics.Items[itemId];
    }

    public static void DrawItem(Game game, ItemId itemId, int x, int y, int width)
    {
        var graphics = GetItemGraphics((int)itemId);
        if (graphics == null)
            return;

        var image = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, graphics.Value.AnimId);

        var xOffset = width != 0 ? (width - image.Animation.Width) / 2 : 0;

        var pal = graphics.Value.HasFlashAttr()
            ? (game.GetFrameCounter() & 8) == 0 ? Palette.Blue : Palette.Red
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

    public static void DrawChar(Char ch, int x, int y, Palette palette) => DrawChar((byte)ch, x, y, palette);
    public static void DrawChar(byte ch, int x, int y, Palette palette)
    {
        var srcX = (ch & 0x0F) * 8;
        var srcY = (ch & 0xF0) / 2;

        Graphics.DrawTile(TileSheet.Font, srcX, srcY, 8, 8, x, y, palette, 0);
    }

    public static void DrawString(ReadOnlySpan<byte> str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar(t, x, y, palette);
            x += 8;
        }
    }

    public static void DrawString(string str, int x, int y, Palette palette)
    {
        foreach (var t in str)
        {
            DrawChar((byte)((byte)char.ToLower(t) - (byte)'a' + 0x0A), x, y, palette);
            x += 8;
        }
    }

    public static void DrawSparkle(int x, int y, Palette palette, int frame)
    {
        var animator = new SpriteAnimator {
            Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Sparkle)
        };
        animator.DrawFrame(TileSheet.PlayerAndItems, x, y, palette, frame);
    }

    public static void DrawBox(int x, int y, int width, int height)
    {
        var x2 = x + width - 8;
        var y2 = y + height - 8;
        var xs = new[] { x, x2 };
        var ys = new[] { y, y2 };

        DrawChar(0x69, x, y, 0);
        DrawChar(0x6B, x2, y, 0);
        DrawChar(0x6E, x, y2, 0);
        DrawChar(0x6D, x2, y2, 0);

        for (var i = 0; i < 2; i++)
        {
            for (var xx = x + 8; xx < x2; xx += 8)
            {
                DrawChar(0x6A, xx, ys[i], 0);
            }

            for (var yy = y + 8; yy < y2; yy += 8)
            {
                DrawChar(0x6C, xs[i], yy, 0);
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
                fullHearts++;
        }

        var x = left;
        var y = top;

        for (var i = 0; i < totalHearts; i++)
        {
            Char tile;

            if (i < fullHearts)
                tile = Char.FullHeart;
            else if (i < fullAndPartialHearts)
                tile = Char.HalfHeart;
            else
                tile = Char.EmptyHeart;

            DrawChar((byte)tile, x, y, Palette.RedBgPalette);

            x += 8;
            if ((i % 8) == 7)
            {
                x = left;
                y -= 8;
            }
        }
    }

    public static void DrawFileIcon(int x, int y, int quest)
    {
        if (quest == 1)
        {
            var sword = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.SwordItem);
            sword.Draw(TileSheet.PlayerAndItems, x + 12, y - 3, (Palette)7);
        }

        var player = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.LinkWalk_NoShield_Down);
        player.Draw(TileSheet.PlayerAndItems, x, y, Palette.Player);
    }

    public static void SetPilePalette()
    {
        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, new byte[] { 0, 0x27, 0x06, 0x16 });
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

    public static byte[] NumberToStringR(byte number, NumberSign sign, ref Span<byte> charBuf)
    {
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
            if (n == 0)
                break;
        }

        if (sign != NumberSign.None && number != 0)
        {
            if (sign == NumberSign.Negative)
                charBuf[pChar] = (byte)Char.Minus;
            else
                charBuf[pChar] = (byte)Char.Plus;
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