using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace z1;

[Flags]
internal enum DrawingFlags
{
    None = 0,
    FlipHorizontal = 0x00001,
    FlipVertical = 0x00002
}

internal static class Graphics
{
    private static SKSurface? _surface;

    private static readonly int _paletteBmpWidth = Math.Max(Global.PaletteLength, 16);
    private static readonly int _paletteBmpHeight = Math.Max(Global.PaletteCount, 16);

    private static readonly SKBitmap?[] _tileSheets = new SKBitmap[(int)TileSheet.Max];
    private static readonly byte[] _paletteBuf;
    private static readonly int _paletteStride = _paletteBmpWidth * Unsafe.SizeOf<SKColor>();
    private static readonly int[] _systemPalette = new int[Global.SysPaletteLength];
    private static readonly int[] _grayscalePalette = new int[Global.SysPaletteLength];
    private static int[] _activeSystemPalette = _systemPalette;
    private static readonly byte[] _palettes = new byte[Global.PaletteCount * Global.PaletteLength];

    public static ref byte GetPalette(Palette paletteIndex, int colorIndex) => ref _palettes[(int)paletteIndex * Global.PaletteLength + colorIndex];
    public static Span<byte> GetPalette(Palette paletteIndex) => MemoryMarshal.CreateSpan(ref _palettes[(int)paletteIndex * Global.PaletteLength], Global.PaletteLength);

    private static readonly TableResource<SpriteAnimationStruct>[] _animSpecs = new TableResource<SpriteAnimationStruct>[(int)TileSheet.Max];

    static Graphics()
    {
        var size = _paletteBmpWidth * _paletteStride * _paletteBmpHeight;
        _paletteBuf = new byte[size];
    }

    public static void SetSurface(SKSurface surface)
    {
        _surface = surface;
    }

    public static void Begin()
    {
    }

    public static void End()
    {
    }

    public static void LoadTileSheet(TileSheet sheet, string file)
    {
        var slot = (int)sheet;

        ref var foundRef = ref _tileSheets[slot];
        if (foundRef != null)
        {
            foundRef.Dispose();
            foundRef = null;
        }

        var bitmap = SKBitmap.Decode(Assets.Root.GetPath("out", file)) ?? throw new Exception();
        _tileSheets[slot] = bitmap;
    }

    public static void LoadTileSheet(TileSheet sheet, string path, string animationFile)
    {
        LoadTileSheet(sheet, path);
        _animSpecs[(int)sheet] = TableResource<SpriteAnimationStruct>.Load(animationFile);
    }

    public static SpriteAnimation GetAnimation(TileSheet sheet, AnimationId id)
    {
        return _animSpecs[(int)sheet].LoadVariableLengthData<SpriteAnimation>((int)id);
    }

    public static void LoadSystemPalette(int[] colorsArgb8)
    {
        colorsArgb8.CopyTo(_systemPalette.AsSpan());

        for (var i = 0; i < Global.SysPaletteLength; i++)
        {
            _grayscalePalette[i] = _systemPalette[i & 0x30];
        }
    }

    public static SKColor GetSystemColor(int sysColor)
    {
        var argb8 = _activeSystemPalette[sysColor];
        return new SKColor(
            (byte)((argb8 >> 16) & 0xFF),
            (byte)((argb8 >> 8) & 0xFF),
            (byte)((argb8 >> 0) & 0xFF),
            (byte)((argb8 >> 24) & 0xFF)
        );
    }

    // TODO: this method has to consider the picture format
    public static void SetColor(Palette paletteIndex, int colorIndex, uint colorArgb8)
    {
        var y = (int)paletteIndex;
        var x = colorIndex;

        var line = MemoryMarshal.Cast<byte, uint>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);
        line[x] = colorArgb8;
    }

    public static void SetColor(Palette paletteIndex, int colorIndex, int colorArgb8) => SetColor(paletteIndex, colorIndex, (uint)colorArgb8);

    public static void SetPalette(Palette paletteIndex, ReadOnlySpan<int> colorsArgb8)
    {
        var y = (int)paletteIndex;
        var line = MemoryMarshal.Cast<byte, int>(_paletteBuf.AsSpan()[(y * _paletteStride)..]);

        for (var x = 0; x < Global.PaletteLength; x++)
        {
            line[x] = colorsArgb8[x];
        }
    }

    public static void SetColorIndexed(Palette paletteIndex, int colorIndex, int sysColor)
    {
        var colorArgb8 = 0;
        if (colorIndex != 0)
            colorArgb8 = _activeSystemPalette[sysColor];
        SetColor(paletteIndex, colorIndex, colorArgb8);
        GetPalette(paletteIndex, colorIndex) = (byte)sysColor;
        TileCache.Clear();
    }

    public static void SetPaletteIndexed(Palette paletteIndex, ReadOnlySpan<byte> sysColors)
    {
        var colorsArgb8 = new[]
        {
            0,
            _activeSystemPalette[sysColors[1]],
            _activeSystemPalette[sysColors[2]],
            _activeSystemPalette[sysColors[3]],
        };

        SetPalette(paletteIndex, colorsArgb8);
        var dest = GetPalette(paletteIndex);
        sysColors[..Global.PaletteLength].CopyTo(dest);
        TileCache.Clear();
    }

    public static void UpdatePalettes()
    {
        TileCache.Clear();
    }

    public static void SwitchSystemPalette(int[] newSystemPalette)
    {
        if (newSystemPalette == _activeSystemPalette)
            return;

        _activeSystemPalette = newSystemPalette;

        for (var i = 0; i < Global.PaletteCount; i++)
        {
            var sysColors = GetPalette((Palette)i);
            var colorsArgb8 = new[]
            {
                0,
                _activeSystemPalette[sysColors[1]],
                _activeSystemPalette[sysColors[2]],
                _activeSystemPalette[sysColors[3]],
            };
            SetPalette((Palette)i, colorsArgb8);
        }
        UpdatePalettes();
    }

    public static void EnableGrayscale()
    {
        SwitchSystemPalette(_grayscalePalette);
    }

    public static void DisableGrayscale()
    {
        SwitchSystemPalette(_systemPalette);
    }

    public static SpriteAnimator GetSpriteAnimator(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));
    public static SpriteImage GetSpriteImage(TileSheet sheet, AnimationId id) => new(GetAnimation(sheet, id));

    public static void DrawBitmap(
        SKBitmap? bitmap,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags
    )
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(_surface);

        var destRect = new SKRect(destX, destY, destX + width, destY + height);

        var cacheKey = new TileCache(null, bitmap, _activeSystemPalette, srcX, srcY, palette, flags);
        var tile = cacheKey.GetValue(width, height);
        _surface.Canvas.DrawBitmap(tile, destRect);
    }

    public static void DrawSpriteTile(
        TileSheet slot,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags
    )
    {
        DrawTile(slot, srcX, srcY, width, height, destX, destY + 1, palette, flags);
    }

    private readonly record struct TileCache(TileSheet? Slot, SKBitmap? Bitmap, int[] SystemPalette, int X, int Y, Palette Palette, DrawingFlags Flags)
    {
        private static readonly Dictionary<TileCache, SKBitmap> _tileCache = new(200);

        public bool TryGetValue([MaybeNullWhen(false)] out SKBitmap bitmap) => _tileCache.TryGetValue(this, out bitmap);
        public void Set(SKBitmap bitmap) => _tileCache[this] = bitmap;
        // JOE: Arg. This makes me hate the tile cache even more. Also, this leaks all those SKBitmaps.
        public static void Clear() => _tileCache.Clear();

        public unsafe SKBitmap GetValue(int width, int height)
        {
            if (!TryGetValue(out var tile))
            {
                var sheet = Bitmap ?? _tileSheets[(int)Slot] ?? throw new Exception();
                tile = sheet.Extract(X, Y, width, height, null, Flags);

                var locked = tile.Lock();
                for (var y = 0; y < locked.Height; ++y)
                {
                    var px = locked.PtrFromPoint(0, y);
                    for (var x = 0; x < locked.Width; ++x, ++px)
                    {
                        var r = px->Blue;
                        if (r == 0)
                        {
                            *px = SKColors.Transparent;
                            continue;
                        }

                        var paletteX = r / 16;
                        var paletteY = (int)Palette;

                        var val = MemoryMarshal.Cast<byte, SKColor>(_paletteBuf.AsSpan())[paletteY * _paletteBmpWidth + paletteX];
                        var color = new SKColor(val.Blue, val.Green, val.Red, val.Alpha);
                        *px = color;
                    }
                }

                Set(tile);
            }

            return tile;
        }
    }

    public static void DrawTile(
        TileSheet slot,
        int srcX,
        int srcY,
        int width,
        int height,
        int destX,
        int destY,
        Palette palette,
        DrawingFlags flags
    )
    {
        ArgumentNullException.ThrowIfNull(_surface);
        Debug.Assert(slot < TileSheet.Max);

        var cacheKey = new TileCache(slot, null, _activeSystemPalette, srcX, srcY, palette, flags);
        var tile = cacheKey.GetValue(width, height);

        var destRect = new SKRect(destX, destY, destX + width, destY + height);
        _surface.Canvas.DrawBitmap(tile, destRect);
    }

    public static void DrawStripSprite16X16(TileSheet slot, int firstTile, int destX, int destY, Palette palette)
    {
        static ReadOnlySpan<byte> OffsetsX() => new byte[] { 0, 0, 8, 8 };
        static ReadOnlySpan<byte> OffsetsY() => new byte[] { 0, 8, 0, 8 };

        var tileRef = firstTile;

        for (var i = 0; i < 4; i++)
        {
            var srcX = (tileRef & 0x0F) * World.TileWidth;
            var srcY = ((tileRef & 0xF0) >> 4) * World.TileHeight;
            tileRef++;

            DrawTile(
                slot, srcX, srcY,
                World.TileWidth, World.TileHeight,
                destX + OffsetsX()[i], destY + OffsetsY()[i],
                palette, 0);
        }
    }

    public static void Clear(SKColor color)
    {
        ArgumentNullException.ThrowIfNull(_surface);
        _surface.Canvas.Clear(color);
    }

    public static void Clear(SKColor color, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(_surface);
        _surface.Canvas.Clear(color);
    }

    public readonly struct UnclipScope : IDisposable
    {
        public void Dispose() => ResetClip();
    }

    public static UnclipScope SetClip(int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(_surface);
        var y2 = y + height;

        if (y2 < 0)
        {
            height = 0;
            y = 0;
        }
        else if (y > Global.StdViewHeight)
        {
            height = 0;
            y = Global.StdViewHeight;
        }
        else
        {
            if (y < 0)
            {
                height += y;
                y = 0;
            }

            if (y2 > Global.StdViewHeight)
            {
                height = Global.StdViewHeight - y;
            }
        }

        _surface.Canvas.Save();
        _surface.Canvas.ClipRect(new SKRect(x, y, x + width, y + height));
        return new UnclipScope();
    }

    public static void ResetClip()
    {
        ArgumentNullException.ThrowIfNull(_surface);
        _surface.Canvas.Restore();
    }
}

internal enum UWNpcsAnimIds
{
    UW_Bubble,
    UW_Gel,
    UW_Trap,
    UW_OldMan,
    UW_Keese,
    UW_Moldorm,
    UW_Wallmaster,
    UW_Rope_Right,
    UW_Rope_Left,
    UW_Stalfos,
    UW_Goriya_Down,
    UW_Goriya_Up,
    UW_Goriya_Right,
    UW_Goriya_Left,
    UW_PolsVoice,
    UW_Gibdo,
    UW_Zol,
    UW_Darknut_Down,
    UW_Darknut_Up,
    UW_Darknut_Right,
    UW_Darknut_Left,
    UW_LanmolaHead,
    UW_LanmolaBody,
    UW_LikeLike,
    UW_Vire_Down,
    UW_Vire_Up,
    UW_Wizzrobe_Right,
    UW_Wizzrobe_Left,
    UW_Wizzrobe_Up,
}

internal enum BossAnimationIds
{
    B1_Aquamentus,
    B1_Aquamentus_Mouth_Open,
    B1_Aquamentus_Mouth_Closed,
    B1_Digdogger_Big,
    B1_Digdogger_Little,
    B1_Dodongo_R,
    B1_Dodongo_L,
    B1_Dodongo_D,
    B1_Dodongo_U,
    B1_Dodongo_Bloated_R,
    B1_Dodongo_Bloated_L,
    B1_Dodongo_Bloated_D,
    B1_Dodongo_Bloated_U,

    B2_Gleeok_Body = 0,
    B2_Gleeok_Neck,
    B2_Gleeok_Head,
    B2_Gleeok_Head2,
    B2_Manhandla_Hand_L,
    B2_Manhandla_Hand_R,
    B2_Manhandla_Hand_U,
    B2_Manhandla_Hand_D,
    B2_Manhandla_Body,
    B2_Gohma_Legs_L,
    B2_Gohma_Legs_R,
    B2_Gohma_Eye_Closed,
    B2_Gohma_Eye_Mid,
    B2_Gohma_Eye_Open,
    B2_Gohma_Eye_All,

    B3_Ganon = 0,
    B3_Slash_U,
    B3_Slash_D,
    B3_Slash_L,
    B3_Slash_R,
    B3_Pile,
    B3_Zelda_Lift,
    B3_Zelda_Stand,
    B3_Triforce,
    B3_Patra,
    B3_PatraChild,
}

internal enum OverworldAnimationIds
{
    OW_Boulder,
    OW_Whirlwind,
    OW_OldMan,
    OW_OldWoman,
    OW_Merchant,
    OW_FlyingRock,
    OW_Armos_Down,
    OW_Armos_Up,
    OW_Armos_Right,
    OW_Armos_Left,
    OW_Octorock_Down,
    OW_Octorock_Left,
    OW_Octorock_Up,
    OW_Octorock_Right,
    OW_Mound,
    OW_LeeverHalf,
    OW_Leever,
    OW_Peahat,
    OW_Tektite,
    OW_Lynel_Right,
    OW_Lynel_Left,
    OW_Lynel_Down,
    OW_Lynel_Up,
    OW_Ghini_Left,
    OW_Ghini_Right,
    OW_Ghini_UpLeft,
    OW_Ghini_UpRight,
    OW_Zora_Down,
    OW_Zora_Up,
    OW_Moblin_Right,
    OW_Moblin_Left,
    OW_Moblin_Down,
    OW_Moblin_Up,
}

internal enum AnimationId
{
    LinkWalk_NoShield_Right,
    LinkWalk_NoShield_Left,
    LinkWalk_NoShield_Down,
    LinkWalk_NoShield_Up,
    LinkWalk_LittleShield_Right,
    LinkWalk_LittleShield_Left,
    LinkWalk_LittleShield_Down,
    LinkWalk_LittleShield_Up,
    LinkWalk_BigShield_Right,
    LinkWalk_BigShield_Left,
    LinkWalk_BigShield_Down,
    LinkWalk_BigShield_Up,
    LinkThrust_Right,
    LinkThrust_Left,
    LinkThrust_Down,
    LinkThrust_Up,
    LinkLiftLight,
    LinkLiftHeavy,
    SwordItem,
    FleshItem,
    RecorderItem,
    CandleItem,
    ArrowItem,
    BowItem,
    MKeyItem,
    KeyItem,
    RuppeeItem,
    BombItem,
    BoomerangItem,
    Spark,
    Slash,
    BottleItem,
    BookItem,
    Fireball,
    RingItem,
    MSwordItem,
    WandItem,
    MapItem,
    BraceletItem,
    Fairy,
    MShieldItem,
    Heart,
    Fire,
    Sparkle,
    Clock,
    HeartContainer,
    Compass,
    Raft,
    TriforcePiece,
    Cloud,
    Ladder,
    Wave_Right,
    Wave_Left,
    Wave_Down,
    Wave_Up,
    Sword_Right,
    Sword_Left,
    Sword_Down,
    Sword_Up,
    Arrow_Right,
    Arrow_Left,
    Arrow_Down,
    Arrow_Up,
    Wand_Right,
    Wand_Left,
    Wand_Down,
    Wand_Up,
    Boomerang,
    OldMan,
    OldWoman,
    Merchant,
    Moblin,
    PowerTriforce,
    Cursor,

    B1_Aquamentus = BossAnimationIds.B1_Aquamentus,
    B1_Aquamentus_Mouth_Open = BossAnimationIds.B1_Aquamentus_Mouth_Open,
    B1_Aquamentus_Mouth_Closed = BossAnimationIds.B1_Aquamentus_Mouth_Closed,
    B1_Digdogger_Big = BossAnimationIds.B1_Digdogger_Big,
    B1_Digdogger_Little = BossAnimationIds.B1_Digdogger_Little,
    B1_Dodongo_R = BossAnimationIds.B1_Dodongo_R,
    B1_Dodongo_L = BossAnimationIds.B1_Dodongo_L,
    B1_Dodongo_D = BossAnimationIds.B1_Dodongo_D,
    B1_Dodongo_U = BossAnimationIds.B1_Dodongo_U,
    B1_Dodongo_Bloated_R = BossAnimationIds.B1_Dodongo_Bloated_R,
    B1_Dodongo_Bloated_L = BossAnimationIds.B1_Dodongo_Bloated_L,
    B1_Dodongo_Bloated_D = BossAnimationIds.B1_Dodongo_Bloated_D,
    B1_Dodongo_Bloated_U = BossAnimationIds.B1_Dodongo_Bloated_U,


    B2_Gleeok_Body = BossAnimationIds.B2_Gleeok_Body,
    B2_Gleeok_Neck = BossAnimationIds.B2_Gleeok_Neck,
    B2_Gleeok_Head = BossAnimationIds.B2_Gleeok_Head,
    B2_Gleeok_Head2 = BossAnimationIds.B2_Gleeok_Head2,
    B2_Manhandla_Hand_L = BossAnimationIds.B2_Manhandla_Hand_L,
    B2_Manhandla_Hand_R = BossAnimationIds.B2_Manhandla_Hand_R,
    B2_Manhandla_Hand_U = BossAnimationIds.B2_Manhandla_Hand_U,
    B2_Manhandla_Hand_D = BossAnimationIds.B2_Manhandla_Hand_D,
    B2_Manhandla_Body = BossAnimationIds.B2_Manhandla_Body,
    B2_Gohma_Legs_L = BossAnimationIds.B2_Gohma_Legs_L,
    B2_Gohma_Legs_R = BossAnimationIds.B2_Gohma_Legs_R,
    B2_Gohma_Eye_Closed = BossAnimationIds.B2_Gohma_Eye_Closed,
    B2_Gohma_Eye_Mid = BossAnimationIds.B2_Gohma_Eye_Mid,
    B2_Gohma_Eye_Open = BossAnimationIds.B2_Gohma_Eye_Open,
    B2_Gohma_Eye_All = BossAnimationIds.B2_Gohma_Eye_All,

    B3_Ganon = BossAnimationIds.B3_Ganon,
    B3_Slash_U = BossAnimationIds.B3_Slash_U,
    B3_Slash_D = BossAnimationIds.B3_Slash_D,
    B3_Slash_L = BossAnimationIds.B3_Slash_L,
    B3_Slash_R = BossAnimationIds.B3_Slash_R,
    B3_Pile = BossAnimationIds.B3_Pile,
    B3_Zelda_Lift = BossAnimationIds.B3_Zelda_Lift,
    B3_Zelda_Stand = BossAnimationIds.B3_Zelda_Stand,
    B3_Triforce = BossAnimationIds.B3_Triforce,
    B3_Patra = BossAnimationIds.B3_Patra,
    B3_PatraChild = BossAnimationIds.B3_PatraChild,

    OW_Boulder = OverworldAnimationIds.OW_Boulder,
    OW_Whirlwind = OverworldAnimationIds.OW_Whirlwind,
    OW_OldMan = OverworldAnimationIds.OW_OldMan,
    OW_OldWoman = OverworldAnimationIds.OW_OldWoman,
    OW_Merchant = OverworldAnimationIds.OW_Merchant,
    OW_FlyingRock = OverworldAnimationIds.OW_FlyingRock,
    OW_Armos_Down = OverworldAnimationIds.OW_Armos_Down,
    OW_Armos_Up = OverworldAnimationIds.OW_Armos_Up,
    OW_Armos_Right = OverworldAnimationIds.OW_Armos_Right,
    OW_Armos_Left = OverworldAnimationIds.OW_Armos_Left,
    OW_Octorock_Down = OverworldAnimationIds.OW_Octorock_Down,
    OW_Octorock_Left = OverworldAnimationIds.OW_Octorock_Left,
    OW_Octorock_Up = OverworldAnimationIds.OW_Octorock_Up,
    OW_Octorock_Right = OverworldAnimationIds.OW_Octorock_Right,
    OW_Mound = OverworldAnimationIds.OW_Mound,
    OW_LeeverHalf = OverworldAnimationIds.OW_LeeverHalf,
    OW_Leever = OverworldAnimationIds.OW_Leever,
    OW_Peahat = OverworldAnimationIds.OW_Peahat,
    OW_Tektite = OverworldAnimationIds.OW_Tektite,
    OW_Lynel_Right = OverworldAnimationIds.OW_Lynel_Right,
    OW_Lynel_Left = OverworldAnimationIds.OW_Lynel_Left,
    OW_Lynel_Down = OverworldAnimationIds.OW_Lynel_Down,
    OW_Lynel_Up = OverworldAnimationIds.OW_Lynel_Up,
    OW_Ghini_Left = OverworldAnimationIds.OW_Ghini_Left,
    OW_Ghini_Right = OverworldAnimationIds.OW_Ghini_Right,
    OW_Ghini_UpLeft = OverworldAnimationIds.OW_Ghini_UpLeft,
    OW_Ghini_UpRight = OverworldAnimationIds.OW_Ghini_UpRight,
    OW_Zora_Down = OverworldAnimationIds.OW_Zora_Down,
    OW_Zora_Up = OverworldAnimationIds.OW_Zora_Up,
    OW_Moblin_Right = OverworldAnimationIds.OW_Moblin_Right,
    OW_Moblin_Left = OverworldAnimationIds.OW_Moblin_Left,
    OW_Moblin_Down = OverworldAnimationIds.OW_Moblin_Down,
    OW_Moblin_Up = OverworldAnimationIds.OW_Moblin_Up,

    UW_Bubble = UWNpcsAnimIds.UW_Bubble,
    UW_Gel = UWNpcsAnimIds.UW_Gel,
    UW_Trap = UWNpcsAnimIds.UW_Trap,
    UW_OldMan = UWNpcsAnimIds.UW_OldMan,
    UW_Keese = UWNpcsAnimIds.UW_Keese,
    UW_Moldorm = UWNpcsAnimIds.UW_Moldorm,
    UW_Wallmaster = UWNpcsAnimIds.UW_Wallmaster,
    UW_Rope_Right = UWNpcsAnimIds.UW_Rope_Right,
    UW_Rope_Left = UWNpcsAnimIds.UW_Rope_Left,
    UW_Stalfos = UWNpcsAnimIds.UW_Stalfos,
    UW_Goriya_Down = UWNpcsAnimIds.UW_Goriya_Down,
    UW_Goriya_Up = UWNpcsAnimIds.UW_Goriya_Up,
    UW_Goriya_Right = UWNpcsAnimIds.UW_Goriya_Right,
    UW_Goriya_Left = UWNpcsAnimIds.UW_Goriya_Left,
    UW_PolsVoice = UWNpcsAnimIds.UW_PolsVoice,
    UW_Gibdo = UWNpcsAnimIds.UW_Gibdo,
    UW_Zol = UWNpcsAnimIds.UW_Zol,
    UW_Darknut_Down = UWNpcsAnimIds.UW_Darknut_Down,
    UW_Darknut_Up = UWNpcsAnimIds.UW_Darknut_Up,
    UW_Darknut_Right = UWNpcsAnimIds.UW_Darknut_Right,
    UW_Darknut_Left = UWNpcsAnimIds.UW_Darknut_Left,
    UW_LanmolaHead = UWNpcsAnimIds.UW_LanmolaHead,
    UW_LanmolaBody = UWNpcsAnimIds.UW_LanmolaBody,
    UW_LikeLike = UWNpcsAnimIds.UW_LikeLike,
    UW_Vire_Down = UWNpcsAnimIds.UW_Vire_Down,
    UW_Vire_Up = UWNpcsAnimIds.UW_Vire_Up,
    UW_Wizzrobe_Right = UWNpcsAnimIds.UW_Wizzrobe_Right,
    UW_Wizzrobe_Left = UWNpcsAnimIds.UW_Wizzrobe_Left,
    UW_Wizzrobe_Up = UWNpcsAnimIds.UW_Wizzrobe_Up,
}
