using System.Collections.Immutable;
using System.Diagnostics;
using z1.IO;
using z1.Render;

namespace z1.Actors;

internal interface IThrower
{
    void Catch();
}

internal enum WorldLevel
{
    Overworld, Underworld
}

internal static class WorldLevelExtensions
{
    public static TileSheet GetNpcTileSheet(this WorldLevel level)
    {
        return level switch
        {
            WorldLevel.Overworld => TileSheet.NpcsOverworld,
            WorldLevel.Underworld => TileSheet.NpcsUnderworld,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, $"Invalid {nameof(WorldLevel)} for {nameof(WalkerActor)}."),
        };
    }

    public static TileSheet GetBackgroundTileSheet(this WorldLevel level)
    {
        return level switch
        {
            WorldLevel.Overworld => TileSheet.BackgroundOverworld,
            WorldLevel.Underworld => TileSheet.BackgroundUnderworld,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, $"Invalid {nameof(WorldLevel)} for {nameof(WalkerActor)}."),
        };
    }

    public static TileSheet GetBackgroundTileSheet(this GameWorld world)
    {
        return world.IsOverworld switch
        {
            true => TileSheet.BackgroundOverworld,
            false => TileSheet.BackgroundUnderworld,
        };
    }
}

internal readonly record struct WalkerSpec(
    ImmutableArray<AnimationId>? AnimationMap,
    int AnimationTime,
    Palette Palette,
    int Speed = 0,
    ObjType ShotType = ObjType.None);

internal abstract class WalkerActor : MonsterActor
{
    protected const int StandardSpeed = 0x20;
    protected const int FastSpeed = 0x40;

    protected WalkerSpec Spec { get; private set; }
    protected SpriteAnimator Animator;
    protected ImmutableArray<AnimationId>? AnimationMap => Spec.AnimationMap;
    protected int AnimationTime => Spec.AnimationTime;
    protected int Speed => Spec.Speed;

    protected int CurrentSpeed;
    protected int ShootTimer;
    protected bool WantToShoot;

    protected bool HasProjectile => Spec.ShotType != ObjType.None;

    private readonly TileSheet _tileSheet;

    protected WalkerActor(World world, ObjType type, WorldLevel level, WalkerSpec spec, int x, int y)
        : base(world, type, x, y)
    {
        _tileSheet = level.GetNpcTileSheet();
        Spec = spec;
        Animator = new SpriteAnimator
        {
            Time = 0,
            DurationFrames = AnimationTime
        };

        Facing = Direction.Left; // ???

        CurrentSpeed = Speed;
        SetFacingAnimation();
    }

    public override void Draw()
    {
        SetFacingAnimation();
        var offsetX = (16 - Animator.Animation.Width) / 2;
        var pal = CalcPalette(Spec.Palette);
        Animator.Draw(_tileSheet, X + offsetX, Y, pal);
    }

    protected void SetSpec(WalkerSpec spec)
    {
        Spec = spec;
        CurrentSpeed = spec.Speed;
        Animator.DurationFrames = AnimationTime;
        SetFacingAnimation();
    }

    protected void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = AnimationMap != null
            ? Graphics.GetAnimation(_tileSheet, AnimationMap.Value[dirOrd])
            : null;
    }

    protected void TryShooting()
    {
        if (!HasProjectile) return;

        if (ObjType.IsBlueWalker() || ShootTimer != 0 || Game.Random.Next(0xFF) >= 0xF8)
        {
            if (InvincibilityTimer > 0)
            {
                ShootTimer = 0;
            }
            else
            {
                if (ShootTimer > 0)
                {
                    ShootTimer--;
                }
                else if (WantToShoot)
                {
                    ShootTimer = 0x30;
                }
            }
        }

        if (ShootTimer == 0)
        {
            CurrentSpeed = Speed;
            return;
        }

        if (ShootTimer != 0x10 || IsStunned)
        {
            CurrentSpeed = 0;
            return;
        }

        var shot = Shoot(Spec.ShotType);
        if (shot != null)
        {
            ObjTimer = 0x80;
            CurrentSpeed = 0;
            WantToShoot = false;
        }
        else
        {
            CurrentSpeed = Speed;
        }
    }

    protected Actor? Shoot(ObjType shotType)
    {
        return WantToShoot
            ? Shoot(shotType, X, Y, Facing)
            : null;
    }

    public bool TryBigShove()
    {
        if (TileOffset == 0)
        {
            if (World.CollidesWithTileMoving(X, Y, Facing, false)) return false;
        }

        if (CheckWorldMargin(Facing) == Direction.None) return false;

        MoveDirection(0xFF, Facing);

        if ((TileOffset & 0x0F) == 0)
        {
            TileOffset &= 0x0F;
        }

        return true;
    }
}

internal abstract class ChaseWalkerActor : WalkerActor
{
    protected ChaseWalkerActor(World world, ObjType type, WorldLevel level, WalkerSpec spec, int x, int y)
        : base(world, type, level, spec, x, y)
    {
    }

    public override void Update()
    {
        Animator.Advance();
        UpdateNoAnimation();
    }

    protected void UpdateNoAnimation()
    {
        ObjMove(CurrentSpeed);
        TargetPlayer();
        TryShooting();
    }

    protected void TargetPlayer()
    {
        if (ShoveDirection != 0) return;

        if (CurrentSpeed == 0 || (TileOffset & 0x0F) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0x0F;

        // ORIGINAL: If player.state = $FF, then skip all this, go to the end (moving := Facing).
        //           But, I don't see how the player can get in that state.

        var observedPos = World.GetObservedPlayerPos();
        var xDiff = Math.Abs(X - observedPos.X);
        var yDiff = Math.Abs(Y - observedPos.Y);
        int maxDiff;
        Direction dir;

        if (yDiff >= xDiff)
        {
            maxDiff = yDiff;
            dir = (Y > observedPos.Y) ? Direction.Up : Direction.Down;
        }
        else
        {
            maxDiff = xDiff;
            dir = (X > observedPos.X) ? Direction.Left : Direction.Right;
        }

        if (maxDiff < 0x51)
        {
            WantToShoot = true;
            Facing = dir;
        }
        else
        {
            WantToShoot = false;
        }

        Moving = (byte)Facing;
    }
}

internal abstract class DelayedWanderer : WandererWalkerActor
{
    protected DelayedWanderer(World world, ObjType type, WorldLevel level, WalkerSpec spec, int turnRate, int x, int y)
        : base(world, type, level, spec, turnRate, x, y)
    {
        InitCommonFacing();
        InitCommonStateTimer();
        SetFacingAnimation();

        if (type is ObjType.BlueFastOctorock or ObjType.RedFastOctorock)
        {
            CurrentSpeed = 0x30;
        }
    }
}

internal abstract class WandererWalkerActor : WalkerActor
{
    private byte _turnTimer;
    private readonly byte _turnRate;

    protected WandererWalkerActor(World world, ObjType type, WorldLevel level, WalkerSpec spec, int turnRate, int x, int y)
        : base(world, type, level, spec, x, y)
    {
        _turnRate = (byte)turnRate;
    }

    public override void Update()
    {
        Animator.Advance();
        Move();
        TryShooting();
        CheckCollisions();
    }

    protected void Move()
    {
        ObjMove(CurrentSpeed);
        TargetPlayer();
    }

    protected void MoveIfNeeded()
    {
        if (ShoveDirection != 0)
        {
            ObjShove();
            return;
        }

        if (IsStunned) return;

        ObjMove(CurrentSpeed);
        TargetPlayer();
    }

    private void TargetPlayer()
    {
        if (_turnTimer > 0)
        {
            _turnTimer--;
        }

        if (ShoveDirection != 0) return;

        if (CurrentSpeed == 0 || (TileOffset & 0x0F) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0x0F;

        var r = Game.Random.GetByte();

        // ORIGINAL: If (r > turnRate) or (player.state = $FF), then ...
        //           But, I don't see how the player can get in that state.

        if (r > _turnRate)
        {
            TurnIfTime();
        }
        else
        {
            var playerPos = World.GetObservedPlayerPos();

            if (Math.Abs(X - playerPos.X) < 9)
            {
                TurnY();
            }
            else if (Math.Abs(Y - playerPos.Y) < 9)
            {
                TurnX();
            }
            else
            {
                TurnIfTime();
            }
        }

        Moving = (byte)Facing;
    }

    private void TurnIfTime()
    {
        WantToShoot = false;

        if (_turnTimer != 0) return;

        if (Facing.IsVertical())
        {
            TurnX();
        }
        else
        {
            TurnY();
        }
    }

    private void TurnX()
    {
        Facing = GetXDirToPlayer(X);
        _turnTimer = Game.Random.GetByte();
        WantToShoot = true;
    }

    private void TurnY()
    {
        Facing = GetYDirToPlayer(Y);
        _turnTimer = Game.Random.GetByte();
        WantToShoot = true;
    }
}

internal sealed class OctorokActor : DelayedWanderer
{
    public const ObjType ShotFromOctorock = ObjType.FlyingRock;

    private static readonly ImmutableArray<AnimationId> _octorockAnimMap = [
        AnimationId.OW_Octorock_Right,
        AnimationId.OW_Octorock_Left,
        AnimationId.OW_Octorock_Down,
        AnimationId.OW_Octorock_Up
    ];

    private static readonly WalkerSpec _blueSlowOctorockSpec = new(_octorockAnimMap, 12, Palette.Blue, StandardSpeed, ShotFromOctorock);
    private static readonly WalkerSpec _blueFastOctorockSpec = new(_octorockAnimMap, 12, Palette.Blue, FastSpeed, ShotFromOctorock);
    private static readonly WalkerSpec _redSlowOctorockSpec = new(_octorockAnimMap, 12, Palette.Red, StandardSpeed, ShotFromOctorock);
    private static readonly WalkerSpec _redFastOctorockSpec = new(_octorockAnimMap, 12, Palette.Red, FastSpeed, ShotFromOctorock);

    private OctorokActor(World world, ObjType type, WalkerSpec spec, int turnRate, int x, int y)
        : base(world, type, WorldLevel.Overworld, spec, turnRate, x, y)
    {
    }

    public static OctorokActor Make(World world, ActorColor color, bool isFast, int x, int y)
    {
        return (color, isFast) switch
        {
            (ActorColor.Blue, false) => new OctorokActor(world, ObjType.BlueSlowOctorock, _blueSlowOctorockSpec, 0xA0, x, y),
            (ActorColor.Blue, true) => new OctorokActor(world, ObjType.BlueFastOctorock, _blueFastOctorockSpec, 0xA0, x, y),
            (ActorColor.Red, false) => new OctorokActor(world, ObjType.RedSlowOctorock, _redSlowOctorockSpec, 0x70, x, y),
            (ActorColor.Red, true) => new OctorokActor(world, ObjType.RedFastOctorock, _redFastOctorockSpec, 0x70, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(OctorokActor)}."),
        };
    }
}


internal sealed class GanonActor : BlueWizzrobeBase
{
    private readonly record struct SlashSpec(TileSheet Sheet, AnimationId AnimIndex, byte Flags);

    internal enum GanonState
    {
        HoldDark,
        HoldLight,
        Active,
    }

    [Flags]
    private enum Visual
    {
        None,
        Ganon = 1,
        Pile = 2,
        Pieces = 4,
    }

    private static readonly DebugLog _log = new(nameof(GanonActor));

    private static readonly ImmutableArray<byte> _ganonNormalPalette = [0x16, 0x2C, 0x3C];
    private static readonly ImmutableArray<byte> _ganonRedPalette = [0x07, 0x17, 0x30];

    private readonly ImmutableArray<SlashSpec> _slashSpecs = [
        new(TileSheet.Boss9,          AnimationId.B3_Slash_U, 0),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      1),
        new(TileSheet.Boss9,          AnimationId.B3_Slash_L, 1),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      3),
        new(TileSheet.Boss9,          AnimationId.B3_Slash_U, 2),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      2),
        new(TileSheet.Boss9,          AnimationId.B3_Slash_L, 0),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      0)
    ];

    public override bool IsReoccuring => false;

    private Visual _visual;
    private GanonState _state;
    private byte _lastHitTimer;
    private int _dyingTimer;
    private int _frame;

    private int _cloudDist;
    private readonly int[] _sparksX = new int[8];
    private readonly int[] _sparksY = new int[8];
    private readonly Direction[] _piecesDir = new Direction[8];

    private readonly SpriteAnimator _animator;
    private readonly SpriteAnimator _cloudAnimator;
    private readonly SpriteImage _pileImage;

    public GanonActor(World world, int x, int y)
        : base(world, ObjType.Ganon, x, y)
    {
        InvincibilityMask = 0xFA;

        _animator = new SpriteAnimator(TileSheet.Boss9, AnimationId.B3_Ganon)
        {
            DurationFrames = 1,
            Time = 0,
        };

        _pileImage = new SpriteImage(TileSheet.Boss9, AnimationId.B3_Pile);

        _cloudAnimator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Cloud)
        {
            DurationFrames = 1,
            Time = 0,
        };

        Game.Player.SetState(PlayerState.Paused);
        Game.Player.ObjTimer = 0x40;
        ObjTimer = 0;

        SetBossPalette(_ganonNormalPalette);
        // The original game starts roaring here. But, I think it sounds better later.
    }

    public override void Update()
    {
        _visual = Visual.None;

        switch (_state)
        {
            case GanonState.HoldDark: UpdateHoldDark(); break;
            case GanonState.HoldLight: UpdateHoldLight(); break;
            case GanonState.Active: UpdateActive(); break;
        }
    }

    public override void Draw()
    {
        if (_visual.HasFlag(Visual.Ganon))
        {
            var pal = CalcPalette(Palette.SeaPal);
            _animator.DrawFrame(TileSheet.Boss9, X, Y, pal, _frame);
        }

        if (_visual.HasFlag(Visual.Pile))
        {
            _pileImage.Draw(TileSheet.Boss9, X, Y, Palette.SeaPal);
        }

        if (_visual.HasFlag(Visual.Pieces))
        {
            var cloudFrame = (_cloudDist < 6) ? 2 : 1;

            for (var i = 0; i < 8; i++)
            {
                var cloudX = X;
                var cloudY = Y;

                MoveSimple8(ref cloudX, ref cloudY, _piecesDir[i], _cloudDist);

                _cloudAnimator.DrawFrame(TileSheet.PlayerAndItems, cloudX, cloudY, Palette.SeaPal, cloudFrame);
            }

            var slashPal = 4 + (Game.FrameCounter & 3);

            for (var i = 0; i < 8; i++)
            {
                var slashSpec = _slashSpecs[i];
                var image = new SpriteImage(slashSpec.Sheet, slashSpec.AnimIndex);

                image.Draw(slashSpec.Sheet, _sparksX[i], _sparksY[i], (Palette)slashPal, (DrawingFlags)slashSpec.Flags);
            }
        }
    }

    private void UpdateHoldDark()
    {
        World.LiftItem(ItemId.TriforcePiece, 0);

        if (Game.Player.ObjTimer != 0)
        {
            if (Game.Player.ObjTimer == 1)
            {
                Game.Sound.PlayEffect(SoundEffect.BossHit);
                Game.Sound.PlaySong(SongId.Ganon, SongStream.MainSong, false);
                //       The original game does it in the else part below, but only when [$51C] = $C0
                //       Which is in the first frame that the player's object timer is 0.
            }
        }
        else
        {
            World.FadeIn();

            if (World.GetFadeStep() == 0)
            {
                _state = GanonState.HoldLight;
                Game.Player.ObjTimer = 0xC0;
            }
            _visual = Visual.Ganon;
        }
    }

    private void UpdateHoldLight()
    {
        World.LiftItem(ItemId.TriforcePiece, 0);

        if (Game.Player.ObjTimer == 0)
        {
            Game.Player.SetState(PlayerState.Idle);
            World.LiftItem(ItemId.None);
            Game.Sound.PlaySong(SongId.Level9, SongStream.MainSong, true);
            Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance);
            _state = GanonState.Active;
        }

        _visual = Visual.Ganon;
    }

    private void UpdateActive()
    {
        if (_dyingTimer != 0)
        {
            UpdateDying();
            return;
        }

        CheckCollision();
        PlayBossHitSoundIfHit();

        if (_lastHitTimer != 0)
        {
            UpdateLastHit();
        }
        else if (ObjTimer == 0)
        {
            UpdateMoveAndShoot();
        }
        else if (ObjTimer == 1)
        {
            ResetPosition();
        }
        else
        {
            _visual = Visual.Ganon;
        }
    }

    private void UpdateDying()
    {
        // This isn't exactly like the original, but the intent is clearer.
        if (_dyingTimer < 0xFF)
        {
            _dyingTimer++;
        }

        if (_dyingTimer < 0x50)
        {
            _visual |= Visual.Ganon;
            return;
        }

        if (_dyingTimer == 0x50)
        {
            GlobalFunctions.SetPilePalette();
            Graphics.UpdatePalettes();
            X += 8;
            Y += 8;
            MakePieces();
            Game.Sound.PlayEffect(SoundEffect.BossHit);
            Game.Sound.StopEffect(StopEffect.AmbientInstance);
            Game.Sound.PlaySong(SongId.Ganon, SongStream.MainSong, false);
        }

        _visual |= Visual.Pile;

        if (_dyingTimer < 0xA0)
        {
            MovePieces();
            _visual |= Visual.Pieces;
        }
        else if (_dyingTimer == 0xA0)
        {
            // JOE: TODO: This seems like it needs to be fixed after the map rewrite...
            // World.AddUWRoomItem();
            var triforce = World.GetObject<ItemObjActor>() ?? throw new Exception();
            triforce.X = X;
            triforce.Y = Y;
            World.IncrementRoomKillCount();
            Game.Sound.PlayEffect(SoundEffect.RoomItem);
        }
    }

    private void CheckCollision()
    {
        var player = Game.Player;

        if (player.InvincibilityTimer == 0)
        {
            CheckPlayerCollisionDirect();
        }

        if (_lastHitTimer != 0)
        {
            var itemValue = World.GetItem(ItemSlot.Arrow);
            if (itemValue == 2)
            {
                // The original checks the state of the arrow here and leaves if <> $10.
                // But, CheckArrow does a similar check (>= $20). As far as I can tell, both are equivalent.
                if (CheckArrow())
                {
                    _dyingTimer = 1;
                    InvincibilityTimer = 0x28;
                    _cloudDist = 8;
                }
            }
            return;
        }

        if (ObjTimer != 0) return;

        CheckSword();

        if (Decoration != 0)
        {
            HP = 0xF0;
            _lastHitTimer--;
            SetBossPalette(_ganonRedPalette);
        }

        if (InvincibilityTimer != 0)
        {
            PlayBossHitSoundIfHit();
            ObjTimer = 0x40;
        }

        Decoration = 0;
        ShoveDirection = 0;
        ShoveDistance = 0;
        InvincibilityTimer = 0;
    }

    private void UpdateLastHit()
    {
        if ((Game.FrameCounter & 1) == 1)
        {
            _lastHitTimer--;
            if (_lastHitTimer == 0)
            {
                ResetPosition();
                SetBossPalette(_ganonNormalPalette);
                return;
            }
        }

        if (_lastHitTimer >= 0x30 || (Game.FrameCounter & 1) == 1)
        {
            _visual |= Visual.Ganon;
        }
    }

    // Ganon_MoveAndShoot
    private void UpdateMoveAndShoot()
    {
        _frame++;
        if (_frame == 6)
        {
            _frame = 0;
        }

        MoveAround();

        if ((Game.FrameCounter & 0x3F) == 0)
        {
            ShootFireball(ObjType.Fireball2, X, Y);
        }
    }

    // Ganon_MoveAndShoot.@Move
    private void MoveAround()
    {
        FlashTimer = 1;
        // BlueWizzrobe_TurnSometimesAndMoveAndCheckTile
        TurnTimer++;
        TurnIfNeeded();
        MoveAndCollide();
    }

    private void MakePieces()
    {
        for (var i = 0; i < 8; i++)
        {
            _sparksX[i] = X + 4;
            _sparksY[i] = Y + 4;
            _piecesDir[i] = i.GetDirection8();
        }
    }

    private void MovePieces()
    {
        if (_cloudDist != 0 && (Game.FrameCounter & 7) == 0)
        {
            _cloudDist--;
        }

        for (var i = 0; i < 8; i++)
        {
            if (_piecesDir[i].IsHorizontal()
                || _piecesDir[i].IsVertical()
                || (Game.FrameCounter & 3) != 0)
            {
                MoveSimple8(ref _sparksX[i], ref _sparksY[i], _piecesDir[i], 1);
            }
        }
    }

    private void SetBossPalette(ImmutableArray<byte> palette)
    {
        Graphics.SetColorIndexed(Palette.SeaPal, 1, palette[0]);
        Graphics.SetColorIndexed(Palette.SeaPal, 2, palette[1]);
        Graphics.SetColorIndexed(Palette.SeaPal, 3, palette[2]);
        Graphics.UpdatePalettes();
    }

    private void ResetPosition()
    {
        Y = 0xA0;
        X = (Game.FrameCounter & 1) == 0 ? 0x30 : 0xB0;
    }
}

internal sealed class PrincessActor : MonsterActor
{
    private const int PrincessX = 0x78;
    private const int PrincessY = 0x88;
    private const int PrincessLineX1 = 0x70;
    private const int PrincessLineX2 = 0x80;
    private const int PrincessLineY = 0x95;

    private const int PlayerX = 0x88;
    private const int PlayerY = PrincessY;

    public override bool IsReoccuring => false;

    private int _state;
    private readonly SpriteImage _image;

    private PrincessActor(World world, int x = PrincessX, int y = PrincessY)
        : base(world, ObjType.Princess, x, y)
    {
        _image = new SpriteImage(TileSheet.Boss9, AnimationId.B3_Princess_Stand);
    }

    public static PrincessActor Make(World world)
    {
        ReadOnlySpan<byte> xs = [0x60, 0x70, 0x80, 0x90];
        ReadOnlySpan<byte> ys = [0xB5, 0x9D, 0x9D, 0xB5];

        for (var i = 0; i < xs.Length; i++)
        {
            var fire = new GuardFireActor(world, xs[i], ys[i]);
            world.AddObject(fire);
        }

        return new PrincessActor(world);
    }

    public override void Update()
    {
        var player = Game.Player;

        if (_state == 0)
        {
            var playerX = player.X;
            var playerY = player.Y;

            if (playerX >= PrincessLineX1
                && playerX <= PrincessLineX2
                && playerY <= PrincessLineY)
            {
                _state = 1;
                player.SetState(PlayerState.Paused);
                player.X = PlayerX;
                player.Y = PlayerY;
                player.Facing = Direction.Left;
                Game.Sound.PlaySong(SongId.Princess, SongStream.MainSong, false);
                ObjTimer = 0x80;
            }
        }
        else
        {
            // ORIGINAL: Calls $F229. But, I don't see why we need to.
            if (ObjTimer == 0)
            {
                player.SetState(PlayerState.Idle);
                World.WinGame();
            }
        }
    }

    public override void Draw()
    {
        _image.Draw(TileSheet.Boss9, X, Y, Palette.Player);
    }
}

internal sealed class StandingFireActor : MonsterActor
{
    public override bool IsReoccuring => false;
    private readonly SpriteAnimator _animator;

    public StandingFireActor(World world, int x, int y)
        : base(world, ObjType.StandingFire, x, y)
    {
        _animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Fire)
        {
            DurationFrames = 12,
            Time = 0
        };
    }

    public override void Update()
    {
        CheckPlayerCollision();
        _animator.Advance();
    }

    public override void Draw()
    {
        _animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Red);
    }
}

internal sealed class GuardFireActor : MonsterActor
{
    public override bool IsReoccuring => false;
    private readonly SpriteAnimator _animator;

    public GuardFireActor(World world, int x, int y)
        : base(world, ObjType.GuardFire, x, y)
    {
        _animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Fire)
        {
            DurationFrames = 12,
            Time = 0
        };
    }

    public override void Update()
    {
        _animator.Advance();
        CheckCollisions();
        if (Decoration != 0)
        {
            var dummy = new DeadDummyActor(World, X, Y, Decoration);
            World.AddOnlyObject(this, dummy);
        }
    }

    public override void Draw()
    {
        _animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Red);
    }
}

internal sealed class RupeeStashActor : MonsterActor
{
    private RupeeStashActor(World world, int x, int y)
        : base(world, ObjType.RupieStash, x, y) { }

    public static RupeeStashActor Make(World world)
    {
        ReadOnlySpan<Point> points = [
            new(0x78, 0x70), new(0x70, 0x80), new(0x80, 0x80), new(0x60, 0x90), new(0x70, 0x90), new(0x80, 0x90),
            new(0x90, 0x90), new(0x70, 0xA0), new(0x80, 0xA0), new(0x78, 0xB0)];

        RupeeStashActor? first = null;
        foreach (var point in points)
        {
            var rupee = new RupeeStashActor(world, point.X, point.Y);
            first ??= rupee;
            world.AddObject(rupee);
        }

        return first ?? throw new Exception();
    }

    public override void Update()
    {
        var player = Game.Player;
        var distanceX = Math.Abs(player.X - X);
        var distanceY = Math.Abs(player.Y - Y);

        if (distanceX <= 8 && distanceY <= 8)
        {
            World.PostRupeeWin(1);
            World.IncrementRoomKillCount();
            Delete();
        }
    }

    public override void Draw()
    {
        GlobalFunctions.DrawItemWide(Game, ItemId.Rupee, X, Y);
    }
}

internal sealed class FairyActor : FlyingActor
{
    private static readonly ImmutableArray<AnimationId> _fairyAnimMap = [
        AnimationId.Fairy,
        AnimationId.Fairy,
        AnimationId.Fairy,
        AnimationId.Fairy
    ];

    private static readonly FlyerSpec _fairySpec = new(_fairyAnimMap, TileSheet.PlayerAndItems, Palette.Red, 0xA0);

    private int _timer;

    // JOE: TODO: Fairy is an "item," not an actor. IS this a problem?
    public FairyActor(World world, int x, int y)
        : base(world, ObjType.Item, _fairySpec, x, y)
    {
        _timer = 0xFF;

        Decoration = 0;
        Facing = Direction.Up;
        CurSpeed = 0x7F;

        Game.Sound.PlayEffect(SoundEffect.Item);
    }

    public override void Update()
    {
        if ((Game.FrameCounter & 1) == 1)
        {
            _timer--;
        }

        if (_timer == 0)
        {
            Delete();
            return;
        }

        UpdateStateAndMove();

        ReadOnlySpan<Actor> canPickupFairy = [
            Game.Player,
            .. World.GetObjects<BoomerangProjectile>(static t => t.IsPlayerWeapon)
        ];

        foreach (var obj in canPickupFairy)
        {
            if (!obj.IsDeleted && TouchesObject(obj))
            {
                World.AddItem(ItemId.Fairy);
                Delete();
                break;
            }
        }
    }

    protected override void UpdateFullSpeedImpl()
    {
        GoToState(FlyingActorState.Turn, 6);
    }

    protected override int GetFrame()
    {
        return (MoveCounter & 4) >> 2;
    }

    private bool TouchesObject(Actor obj)
    {
        var distanceX = Math.Abs(obj.X - X);
        var distanceY = Math.Abs(obj.Y - Y);

        return distanceX <= 8 && distanceY <= 8;
    }
}

internal sealed class PondFairyActor : MonsterActor
{
    private const int PondFairyX = 0x78;
    private const int PondFairyY = 0x7D;
    private const int PondFairyLineX1 = 0x70;
    private const int PondFairyLineX2 = 0x80;
    private const int PondFairyLineY = 0xAD;
    private const int PondFairyRingCenterX = 0x80;
    private const int PondFairyRingCenterY = 0x98;

    private enum PondFairyState { Idle, Healing, Healed }

    private readonly byte[] _heartState = new byte[8];
    private readonly byte[] _heartAngle = new byte[8];

    private PondFairyState _pondFairyState;
    private readonly SpriteAnimator _animator;

    public PondFairyActor(World world, int x = PondFairyX, int y = PondFairyY)
        : base(world, ObjType.PondFairy, x, y)
    {
        _animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Fairy)
        {
            Time = 0,
            DurationFrames = 8
        };

        Game.Sound.PlayEffect(SoundEffect.Item);
    }

    public override void Update()
    {
        _animator.Advance();

        switch (_pondFairyState)
        {
            case PondFairyState.Idle: UpdateIdle(); break;
            case PondFairyState.Healing: UpdateHealing(); break;
        }
    }

    private void UpdateIdle()
    {
        var player = Game.Player;
        var playerX = player.X;
        var playerY = player.Y;

        if (playerY != PondFairyLineY
            || playerX < PondFairyLineX1
            || playerX > PondFairyLineX2)
        {
            return;
        }

        _pondFairyState = PondFairyState.Healing;
        player.SetState(PlayerState.Paused);
    }

    private void UpdateHealing()
    {
        ReadOnlySpan<byte> entryAngles = [0, 11, 22, 33, 44, 55, 66, 77];

        for (var i = 0; i < _heartState.Length; i++)
        {
            if (_heartState[i] == 0)
            {
                if (_heartAngle[0] == entryAngles[i])
                {
                    _heartState[i] = 1;
                }
            }
            else
            {
                _heartAngle[i]++;
                if (_heartAngle[i] >= 85)
                {
                    _heartAngle[i] = 0;
                }
            }
        }

        var profile = World.Profile;
        var maxHeartsValue = profile.GetMaxHeartsValue();

        Game.Sound.PlayEffect(SoundEffect.Character);

        if (profile.Hearts < maxHeartsValue)
        {
            World.FillHearts(6);
        }
        else if (_heartState[7] != 0)
        {
            _pondFairyState = PondFairyState.Healed;
            var player = Game.Player;
            player.SetState(PlayerState.Idle);
            World.SwordBlocked = false;
        }
    }

    public override void Draw()
    {
        const float radius = 0x36;
        const float angler = -Pi.TwoPi / 85.0f;

        var xOffset = (16 - _animator.Animation.Width) / 2;
        _animator.Draw(TileSheet.PlayerAndItems, PondFairyX + xOffset, PondFairyY, Palette.Red);

        if (_pondFairyState != PondFairyState.Healing) return;

        var heart = new SpriteImage(TileSheet.PlayerAndItems, AnimationId.Heart);

        for (var i = 0; i < _heartState.Length; i++)
        {
            if (_heartState[i] == 0) continue;

            var angleIndex = _heartAngle[i] + 22;
            var angle = angler * angleIndex;
            var x = (int)(Math.Cos(angle) * radius + PondFairyRingCenterX);
            var y = (int)(Math.Sin(angle) * radius + PondFairyRingCenterY);

            heart.Draw(TileSheet.PlayerAndItems, x, y, Palette.Red);
        }
    }
}

internal sealed class DeadDummyActor : MonsterActor
{
    public DeadDummyActor(World world, int x, int y, byte decoration = 0)
        : base(world, ObjType.DeadDummy, x, y)
    {
        Decoration = decoration;
    }

    public override void Update()
    {
        Decoration = 0x10;
        Game.Sound.PlayEffect(SoundEffect.MonsterDie);
    }

    public override void Draw() { }
}

internal abstract class StdWanderer : WandererWalkerActor
{
    protected StdWanderer(World world, ObjType type, WorldLevel level, WalkerSpec spec, int turnRate, int x, int y)
        : base(world, type, level, spec, turnRate, x, y)
    {
    }
}

internal sealed class GhiniActor : WandererWalkerActor
{
    private static readonly ImmutableArray<AnimationId> _ghiniAnimMap = [
        AnimationId.OW_Ghini_Right,
        AnimationId.OW_Ghini_Left,
        AnimationId.OW_Ghini_Left,
        AnimationId.OW_Ghini_UpRight
    ];

    private static readonly WalkerSpec _ghiniSpec = new(_ghiniAnimMap, 12, Palette.Blue, StandardSpeed);

    public GhiniActor(World world, int x, int y)
        : base(world, ObjType.Ghini, WorldLevel.Overworld, _ghiniSpec, 0xFF, x, y)
    {
        InitCommonFacing();
        InitCommonStateTimer();
        SetFacingAnimation();
    }

    public override void Update()
    {
        Animator.Advance();
        MoveIfNeeded();
        CheckCollisions();

        if (Decoration != 0)
        {
            foreach (var flying in World.GetObjects<FlyingGhiniActor>())
            {
                // JOE: TODO: Is this a death state?
                flying.Decoration = 0x11;
            }
        }
    }
}

internal sealed class GibdoActor : StdWanderer
{
    private static readonly ImmutableArray<AnimationId> _gibdoAnimMap = [
        AnimationId.UW_Gibdo,
        AnimationId.UW_Gibdo,
        AnimationId.UW_Gibdo,
        AnimationId.UW_Gibdo
    ];

    private static readonly WalkerSpec _gibdoSpec = new(_gibdoAnimMap, 16, Palette.Blue, StandardSpeed);

    public override bool CanHoldRoomItem => true;

    public GibdoActor(World world, int x, int y)
        : base(world, ObjType.Gibdo, WorldLevel.Underworld, _gibdoSpec, 0x80, x, y)
    {
    }
}

internal sealed class DarknutActor : StdWanderer
{
    private static readonly ImmutableArray<AnimationId> _darknutAnimMap = [
        AnimationId.UW_Darknut_Right,
        AnimationId.UW_Darknut_Left,
        AnimationId.UW_Darknut_Down,
        AnimationId.UW_Darknut_Up
    ];

    private static readonly WalkerSpec _redDarknutSpec = new(_darknutAnimMap, 16, Palette.Red, StandardSpeed);
    private static readonly WalkerSpec _blueDarknutSpec = new(_darknutAnimMap, 16, Palette.Blue, 0x28);

    private DarknutActor(World world, ObjType type, WalkerSpec spec, int x, int y)
        : base(world, type, WorldLevel.Underworld, spec, 0x80, x, y)
    {
        if (type is not (ObjType.RedDarknut or ObjType.BlueDarknut))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(DarknutActor)}.");
        }

        InvincibilityMask = 0xF6;
    }

    public static DarknutActor Make(World world, ActorColor type, int x, int y)
    {
        return type switch
        {
            ActorColor.Red => new DarknutActor(world, ObjType.RedDarknut, _redDarknutSpec, x, y),
            ActorColor.Blue => new DarknutActor(world, ObjType.BlueDarknut, _blueDarknutSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(DarknutActor)}.")
        };
    }

    public override void Update()
    {
        MoveIfNeeded();
        CheckCollisions();
        StunTimer = 0;
        Animator.Advance();
    }
}

internal sealed class StalfosActor : StdWanderer
{
    private static readonly ImmutableArray<AnimationId> _stalfosAnimMap = [
        AnimationId.UW_Stalfos,
        AnimationId.UW_Stalfos,
        AnimationId.UW_Stalfos,
        AnimationId.UW_Stalfos
    ];

    private static readonly WalkerSpec _stalfosSpec = new(_stalfosAnimMap, 16, Palette.Red, StandardSpeed, ObjType.PlayerSwordShot);

    public override bool CanHoldRoomItem => true;

    public StalfosActor(World world, int x, int y)
        : base(world, ObjType.Stalfos, WorldLevel.Underworld, _stalfosSpec, 0x80, x, y)
    {
    }

    public override void Update()
    {
        MoveIfNeeded();
        CheckCollisions();
        Animator.Advance();

        if (false) // JOE: TODO: QUEST World.Profile.Quest == 1)
        {
            TryShooting();
        }
    }
}

internal sealed class GelActor : WandererWalkerActor
{
    private static readonly ImmutableArray<byte> _gelWaitTimes = [0x08, 0x18, 0x28, 0x38];

    private static readonly ImmutableArray<AnimationId> _gelAnimMap = [
        AnimationId.UW_Gel,
        AnimationId.UW_Gel,
        AnimationId.UW_Gel,
        AnimationId.UW_Gel
    ];

    private static readonly WalkerSpec _gelSpec = new(_gelAnimMap, 4, Palette.SeaPal, 0x40);

    private int _state; // JOE: TODO: Enumify this.

    public GelActor(World world, ObjType type, int x, int y, Direction dir, byte fraction)
        : base(world, type, WorldLevel.Underworld, _gelSpec, 0x20, x, y)
    {
        if (type is not (ObjType.Gel or ObjType.ChildGel))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(GelActor)}.");
        }

        Facing = dir;

        if (type == ObjType.Gel)
        {
            _state = 2;
        }
        else
        {
            Fraction = fraction;
        }

        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        switch (_state)
        {
            case 0:
                ObjTimer = 5;
                _state = 1;
                break;

            case 1:
                UpdateShove();
                break;

            case 2:
                UpdateWander();
                break;
        }

        CheckCollisions();
        Animator.Advance();
    }

    private void UpdateShove()
    {
        if (ObjTimer != 0 && TryBigShove()) return;

        X = (X + 8) & 0xF0;
        Y = (Y + 8) & 0xF0;
        Y |= 0xD;
        TileOffset = 0;
        _state = 2;
    }

    private void UpdateWander()
    {
        if (ObjTimer < 5)
        {
            Move();

            if (ObjTimer == 0 && TileOffset == 0)
            {
                var index = Game.Random.Next(4);
                ObjTimer = _gelWaitTimes[index];
            }
        }
    }
}

internal sealed class ZolActor : WandererWalkerActor
{
    private static readonly ImmutableArray<byte> _zolWaitTimes = [0x18, 0x28, 0x38, 0x48];

    private static readonly ImmutableArray<AnimationId> _zolAnimMap = [
        AnimationId.UW_Zol,
        AnimationId.UW_Zol,
        AnimationId.UW_Zol,
        AnimationId.UW_Zol
    ];

    private static readonly WalkerSpec _zolSpec = new(_zolAnimMap, 16, Palette.SeaPal, 0x18);

    private int _state;

    public ZolActor(World world, int x, int y)
        : base(world, ObjType.Zol, WorldLevel.Underworld, _zolSpec, 0x20, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        switch (_state)
        {
            case 0: UpdateWander(); break;
            case 1: UpdateShove(); break;
            case 2: UpdateSplit(); break;
        }

        Animator.Advance();
    }

    private void UpdateWander()
    {
        if (ObjTimer < 5)
        {
            Move();

            if (ObjTimer == 0 && TileOffset == 0)
            {
                var index = Game.Random.Next(4);
                ObjTimer = _zolWaitTimes[index];
            }
        }

        // Above is almost the same as Gel.UpdateWander.

        CheckCollisions();

        if (Decoration == 0 && InvincibilityTimer != 0)
        {
            // On collision , go to state 2 or 1, depending on alignment.

            const uint alignedY = 0xD;

            var player = Game.Player;
            uint dirMask = 0;

            if ((Y & 0x0F) == alignedY)
            {
                dirMask |= 3;
            }

            if ((X & 0x0F) == 0)
            {
                dirMask |= 0xC;
            }

            _state = (dirMask & (ulong)player.Facing) == 0 ? 2 : 1;
        }
    }

    private void UpdateShove()
    {
        if (!TryBigShove())
        {
            _state = 2;
        }
    }

    private void UpdateSplit()
    {
        ReadOnlySpan<Direction> sHDirs = [Direction.Right, Direction.Left];
        ReadOnlySpan<Direction> sVDirs = [Direction.Down, Direction.Up];

        Delete();
        World.RoomObjCount++;

        var orthoDirs = Facing.IsHorizontal() ? sVDirs : sHDirs;

        for (var i = 0; i < 2; i++)
        {
            var gel = new GelActor(World, ObjType.ChildGel, X, Y, orthoDirs[i], Fraction);
            World.AddObject(gel);
            gel.ObjTimer = 0;
        }
    }
}

internal sealed class BubbleActor : WandererWalkerActor
{
    private static readonly ImmutableArray<AnimationId> _bubbleAnimMap = [
        AnimationId.UW_Bubble,
        AnimationId.UW_Bubble,
        AnimationId.UW_Bubble,
        AnimationId.UW_Bubble
    ];

    private static readonly WalkerSpec _bubbleSpec = new(_bubbleAnimMap, 2, Palette.Blue, FastSpeed);

    public override bool CountsAsLiving => false;

    public BubbleActor(World world, ObjType type, int x, int y)
        : base(world, type, WorldLevel.Underworld, _bubbleSpec, 0x40, x, y)
    {
        if (type is not (ObjType.Bubble1 or ObjType.Bubble2 or ObjType.Bubble3))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(BubbleActor)}.");
        }

        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        MoveIfNeeded();

        if (CheckPlayerCollision())
        {
            if (ObjType == ObjType.Bubble1)
            {
                World.SetStunTimer(StunTimerSlot.NoSword, 0x10);
            }
            else
            {
                World.SwordBlocked = ObjType == ObjType.Bubble3;
            }

            // The sword blocked state is cleared by touching blue bubbles (Bubble2)
            // and by refilling all hearts with the potion or pond fairy.
        }
    }

    public override void Draw()
    {
        var pal = 4;

        if (ObjType == ObjType.Bubble1)
        {
            pal += Game.FrameCounter % 4;
        }
        else
        {
            pal += ObjType - ObjType.Bubble1;
        }

        Animator.Draw(TileSheet.NpcsUnderworld, X, Y, (Palette)pal);
    }

}

internal sealed class VireActor : WandererWalkerActor
{
    private static readonly ImmutableArray<AnimationId> _vireAnimMap = [
        AnimationId.UW_Vire_Down,
        AnimationId.UW_Vire_Down,
        AnimationId.UW_Vire_Down,
        AnimationId.UW_Vire_Up
    ];

    private static readonly WalkerSpec _vireSpec = new(_vireAnimMap, 20, Palette.Blue, StandardSpeed);

    private int _state;

    public VireActor(World world, int x, int y)
        : base(world, ObjType.Vire, WorldLevel.Underworld, _vireSpec, 0x80, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        switch (_state)
        {
            case 0: UpdateWander(); break;
            case 1: UpdateShove(); break;
            default: UpdateSplit(); break;
        }

        if (_state < 2)
        {
            Animator.Advance();
        }
    }

    private void UpdateWander()
    {
        ReadOnlySpan<int> vireOffsetY = [0, -3, -2, -1, -1, 0, -1, 0, 0, 1, 0, 1, 1, 2, 3, 0];

        MoveIfNeeded();

        if (!IsStunned && Facing.IsHorizontal())
        {
            var offsetX = Math.Abs(TileOffset);
            Y += vireOffsetY[offsetX];
        }

        CheckCollisions();

        if (Decoration == 0 && InvincibilityTimer != 0)
        {
            _state = 1;
        }
    }

    private void UpdateShove()
    {
        if (!TryBigShove())
        {
            _state = 2;
        }
    }

    private void UpdateSplit()
    {
        Delete();
        World.RoomObjCount++;

        for (var i = 0; i < 2; i++)
        {
            var keese = KeeseActor.Make(World, ActorColor.Red, X, Y);
            World.AddObject(keese);
            keese.Facing = Facing;
            keese.ObjTimer = 0;
        }
    }
}

internal sealed class LikeLikeActor : WandererWalkerActor
{
    private static readonly ImmutableArray<AnimationId> _likeLikeAnimMap = [
        AnimationId.UW_LikeLike,
        AnimationId.UW_LikeLike,
        AnimationId.UW_LikeLike,
        AnimationId.UW_LikeLike
    ];

    private static readonly WalkerSpec _likeLikeSpec = new(_likeLikeAnimMap, 24, Palette.Red, StandardSpeed);

    private static readonly DebugLog _log = new(nameof(LikeLikeActor));

    public override bool CanHoldRoomItem => true;

    private int _framesHeld;
    private IDisposable? _paralyzedToken;

    public LikeLikeActor(World world, int x, int y)
        : base(world, ObjType.LikeLike, WorldLevel.Underworld, _likeLikeSpec, 0x80, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        var player = Game.Player;

        // Player is not being held.
        if (_framesHeld == 0)
        {
            MoveIfNeeded();
            Animator.Advance();

            if (CheckCollisions())
            {
                _framesHeld++;

                X = player.X;
                Y = player.Y;
                player.ObjTimer = 0;
                // ORIGINAL: PlayerState.[$405] := 0  (But, what's the point?)
                player.ResetShove();
                _paralyzedToken?.Dispose();
                _paralyzedToken = player.Paralyze();
                Animator.DurationFrames = Animator.Animation.Length * 4;
                Animator.Time = 0;
                Flags |= ActorFlags.DrawAbovePlayer;
            }
            return;
        }

        // Player is held.
        var frame = Animator.Time / 4;
        if (frame < 3)
        {
            Animator.Advance();
        }

        _framesHeld++;
        if (_framesHeld >= 0x60)
        {
            World.SetItem(ItemSlot.MagicShield, 0);
            _framesHeld = 0xC0;
        }

        CheckCollisions();

        if (Decoration != 0)
        {
            _paralyzedToken?.Dispose();
        }
    }

    public override bool Delete()
    {
        if (base.Delete())
        {
            _paralyzedToken?.Dispose();
            return true;
        }

        return false;
    }
}

internal abstract class DigWanderer : WandererWalkerActor
{
    protected ImmutableArray<int> StateTimes;
    protected int State;
    private readonly ImmutableArray<WalkerSpec> _stateSpecs;

    public static readonly ImmutableArray<AnimationId> MoundAnimMap = [
        AnimationId.OW_Mound,
        AnimationId.OW_Mound,
        AnimationId.OW_Mound,
        AnimationId.OW_Mound
    ];

    protected DigWanderer(World world, ObjType type, WorldLevel level, ImmutableArray<WalkerSpec> specs, ImmutableArray<int> stateTimes, int x, int y)
        : base(world, type, level, specs[0], 0xA0, x, y)
    {
        ObjTimer = 0;
        _stateSpecs = specs;
        StateTimes = stateTimes;
    }

    public override void Update()
    {
        Move();
        UpdateDig();
    }

    protected void UpdateDig()
    {
        if (ObjTimer == 0)
        {
            State = (State + 1) % 6;
            ObjTimer = (byte)StateTimes[State];
            SetSpec(_stateSpecs[State]);
        }

        Animator.Advance();

        // JOE: TODO: Offload to sub classes.
        if (State == 3 || (this is ZoraActor && State is 2 or 4))
        {
            CheckCollisions();
        }
    }

    public override void Draw()
    {
        if (State != 0)
        {
            base.Draw();
        }
    }
}

internal sealed class ZoraActor : DigWanderer
{
    private static readonly ImmutableArray<AnimationId> _zoraAnimMap = [
        AnimationId.OW_Zora_Down,
        AnimationId.OW_Zora_Down,
        AnimationId.OW_Zora_Down,
        AnimationId.OW_Zora_Up
    ];

    private static readonly WalkerSpec _zoraHiddenSpec = new(null, 32, Palette.SeaPal);
    private static readonly WalkerSpec _zoraMoundSpec = new(MoundAnimMap, 22, Palette.SeaPal);
    private static readonly WalkerSpec _zoraHalfSpec = new(_zoraAnimMap, 2, Palette.SeaPal);
    private static readonly WalkerSpec _zoraFullSpec = new(_zoraAnimMap, 10, Palette.SeaPal);

    private static readonly ImmutableArray<WalkerSpec> _zoraSpecs = [
        _zoraHiddenSpec,
        _zoraMoundSpec,
        _zoraHalfSpec,
        _zoraFullSpec,
        _zoraHalfSpec,
        _zoraMoundSpec
    ];

    private static readonly ImmutableArray<int> _zoraStateTimes = [2, 0x20, 0x0F, 0x22, 0x10, 0x60];

    public ZoraActor(World world, int x, int y)
        : base(world, ObjType.Zora, WorldLevel.Overworld, _zoraSpecs, _zoraStateTimes, x, y)
    {
        ObjTimer = (byte)StateTimes[0];
        Decoration = 0;
    }

    public override void Update()
    {
        if (World.HasItem(ItemSlot.Clock)) return;

        UpdateDig();

        if (State == 0)
        {
            if (ObjTimer == 1)
            {
                var player = Game.Player;
                var cell = World.CurrentRoom.GetRandomWaterTile();

                X = cell.X * World.TileWidth;
                Y = cell.Y * World.TileHeight - 3;

                Facing = player.Y >= Y ? Direction.Down : Direction.Up;
            }
        }
        else if (State == 3)
        {
            if (ObjTimer == 0x20)
            {
                ShootFireball(ObjType.Fireball, X, Y);
            }
        }
    }
}

internal sealed class BlueLeeverActor : DigWanderer
{
    private static readonly ImmutableArray<AnimationId> _leeverAnimMap = [
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever
    ];

    private static readonly ImmutableArray<AnimationId> _leeverHalfAnimMap = [
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf
    ];

    private static readonly WalkerSpec _blueLeeverHiddenSpec = new(null, 32, Palette.Blue, 0x8);
    private static readonly WalkerSpec _blueLeeverMoundSpec = new(MoundAnimMap, 22, Palette.Blue, 0xA);
    private static readonly WalkerSpec _blueLeeverHalfSpec = new(_leeverHalfAnimMap, 2, Palette.Blue, 0x10);
    private static readonly WalkerSpec _blueLeeverFullSpec = new(_leeverAnimMap, 10, Palette.Blue, StandardSpeed);

    private static readonly ImmutableArray<WalkerSpec> _blueLeeverSpecs = [
        _blueLeeverHiddenSpec,
        _blueLeeverMoundSpec,
        _blueLeeverHalfSpec,
        _blueLeeverFullSpec,
        _blueLeeverHalfSpec,
        _blueLeeverMoundSpec
    ];

    private static readonly ImmutableArray<int> _blueLeeverStateTimes = [0x80, 0x20, 0x0F, 0xFF, 0x10, 0x60];

    public BlueLeeverActor(World world, int x, int y)
        : base(world, ObjType.BlueLeever, WorldLevel.Overworld, _blueLeeverSpecs, _blueLeeverStateTimes, x, y)
    {
        Decoration = 0;
        InitCommonStateTimer();
        InitCommonFacing();
        SetFacingAnimation();
    }
}

internal sealed class RedLeeverActor : Actor
{
    private static readonly ImmutableArray<AnimationId> _leeverAnimMap = [
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever
    ];

    private static readonly ImmutableArray<AnimationId> _leeverHalfAnimMap = [
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf
    ];

    private static readonly WalkerSpec _redLeeverHiddenSpec = new(null, 32, Palette.Red);
    private static readonly WalkerSpec _redLeeverMoundSpec = new(DigWanderer.MoundAnimMap, 16, Palette.Red);
    private static readonly WalkerSpec _redLeeverHalfSpec = new(_leeverHalfAnimMap, 16, Palette.Red);
    private static readonly WalkerSpec _redLeeverFullSpec = new(_leeverAnimMap, 10, Palette.Red, Global.StdSpeed);

    private static readonly ImmutableArray<WalkerSpec> _redLeeverSpecs = [
        _redLeeverHiddenSpec,
        _redLeeverMoundSpec,
        _redLeeverHalfSpec,
        _redLeeverFullSpec,
        _redLeeverHalfSpec,
        _redLeeverMoundSpec
    ];

    private static readonly ImmutableArray<int> _redLeeverStateTimes = [0x00, 0x10, 0x08, 0xFF, 0x08, 0x10];

    private static int _count;

    private readonly SpriteAnimator _animator;

    private int _state;
    private WalkerSpec _spec;

    public RedLeeverActor(World world, int x, int y)
        : base(world, ObjType.RedLeever, x, y)
    {
        Decoration = 0;
        Facing = Direction.Right;

        _animator = new SpriteAnimator
        {
            Time = 0,
            DurationFrames = _spec.AnimationTime
        };

        InitCommonStateTimer();
        // No need to InitCommonFacing, because the Facing is changed with every update.
        SetFacingAnimation();

        World.SetStunTimer(StunTimerSlot.RedLeever, 5);
    }

    public override void Update()
    {
        var advanceState = false;

        if (_state == 0)
        {
            if (_count >= 2 || World.GetStunTimer(StunTimerSlot.RedLeever) != 0) return;
            if (!TargetPlayer()) return;
            World.SetStunTimer(StunTimerSlot.RedLeever, 2);
            advanceState = true;
        }
        else if (_state == 3)
        {
            if (ShoveDirection != 0)
            {
                ObjShove();
            }
            else if (!IsStunned)
            {
                if (World.CollidesWithTileMoving(X, Y, Facing, false)
                    || CheckWorldMargin(Facing) == Direction.None)
                {
                    advanceState = true;
                }
                else
                {
                    MoveDirection(_spec.Speed, Facing);
                    if ((TileOffset & 0x0F) == 0)
                    {
                        TileOffset &= 0x0F;
                    }
                    ObjTimer = 0xFF;
                }
            }
        }

        if (advanceState || (_state != 3 && ObjTimer == 0))
        {
            _state = (_state + 1) % _redLeeverStateTimes.Length;
            ObjTimer = (byte)_redLeeverStateTimes[_state];
            SetSpec(_redLeeverSpecs[_state]);

            var countChange = _state switch {
                0 => -1,
                1 => 1,
                _ => 0,
            };
            _count += countChange;
            Debug.Assert(_count is >= 0 and <= 2);
        }

        _animator.Advance();

        if (_state == 3)
        {
            CheckCollisions();
            if (Decoration != 0 && this is RedLeeverActor)
            {
                _count--;
            }
        }
    }

    public override void Draw()
    {
        if (_state != 0)
        {
            var pal = CalcPalette(Palette.Red);
            _animator.Draw(TileSheet.NpcsOverworld, X, Y, pal);
        }
    }

    private void SetSpec(WalkerSpec spec)
    {
        _spec = spec;
        _animator.SetDuration(spec.AnimationTime);
        SetFacingAnimation();
    }

    private void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        _animator.Animation = _spec.AnimationMap != null
            ? Graphics.GetAnimation(TileSheet.NpcsOverworld, _spec.AnimationMap.Value[dirOrd])
            : null;
    }

    private bool TargetPlayer()
    {
        var player = Game.Player;
        var x = player.X;
        var y = player.Y;

        Facing = player.Facing;

        var r = Game.Random.GetByte();
        if (r >= 0xC0)
        {
            Facing = Facing.GetOppositeDirection();
        }

        if (Facing.IsVertical())
        {
            y += Facing == Direction.Down ? 0x28 : -0x28;
            y = (y & 0xF0) + 0xD;
            // y's going to be assigned to a byte, so truncate it now before we test it.
            y &= 0xFF;
        }
        else
        {
            x += Facing == Direction.Right ? 0x28 : -0x28;
            x &= 0xF8;

            if (Math.Abs(player.X - x) >= 0x30) return false;
        }

        if (y < 0x5D) return false;
        if (World.CollidesWithTileStill(x, y)) return false;

        Facing = Facing.GetOppositeDirection();
        X = x;
        Y = y;
        return true;
    }

    public static void ClearRoomData()
    {
        _count = 0;
    }
}

internal readonly record struct FlyerSpec(ImmutableArray<AnimationId> AnimationMap, TileSheet Sheet, Palette Palette, int Speed = 0);

internal enum FlyingActorState
{
    Hastening, // 0
    FullSpeed, // 1
    Chase, // 2
    Turn, // 3
    Slowing, // 4
    Still, // 5
}

internal abstract class FlyingActor : MonsterActor
{
    protected SpriteAnimator Animator;
    protected FlyingActorState State;
    protected int SprintsLeft;
    protected int CurSpeed;
    protected int AccelStep;
    protected Direction DeferredDir;
    protected int MoveCounter;

    protected readonly FlyerSpec Spec;

    protected FlyingActor(World world, ObjType type, FlyerSpec spec, int x, int y)
        : base(world, type, x, y)
    {
        Spec = spec;
        Animator = new SpriteAnimator(spec.Sheet, spec.AnimationMap[0])
        {
            Time = 0,
        };
        Animator.DurationFrames = Animator.Animation.Length;
    }

    protected void UpdateStateAndMove()
    {
        var origFacing = Facing;

        switch (State)
        {
            case FlyingActorState.Hastening: UpdateHastening(); break;
            case FlyingActorState.FullSpeed: UpdateFullSpeed(); break;
            case FlyingActorState.Chase: UpdateChase(); break;
            case FlyingActorState.Turn: UpdateTurn(); break;
            case FlyingActorState.Slowing: UpdateSlowing(); break;
            case FlyingActorState.Still: UpdateStill(); break;
            default: throw new ArgumentOutOfRangeException(nameof(State), State, $"Invalid state for {ObjType}.");
        }

        Move();

        if (Facing != origFacing)
        {
            SetFacingAnimation();
        }
    }

    public override void Draw()
    {
        var pal = CalcPalette(Spec.Palette);
        var frame = GetFrame();
        Animator.DrawFrame(Spec.Sheet, X, Y, pal, frame);
    }

    protected virtual int GetFrame()
    {
        return MoveCounter & 1;
    }

    private void Move()
    {
        AccelStep += CurSpeed & 0xE0;
        if (AccelStep < 0x100) return;

        AccelStep &= 0xFF;
        MoveCounter++;

        if ((Facing & Direction.Right) != 0) X++;
        if ((Facing & Direction.Left) != 0) X--;
        if ((Facing & Direction.Down) != 0) Y++;
        if ((Facing & Direction.Up) != 0) Y--;

        if (Direction.None != CheckWorldMargin(Facing)) return;

        // JOE: Localize this.
        if (this is MoldormActor moldorm)
        {
            if (moldorm.IsHead)
            {
                DeferredDir = Facing.GetOppositeDir8();
            }
        }
        else
        {
            Facing = Facing.GetOppositeDir8();
        }
    }

    protected void GoToState(FlyingActorState state, int sprints)
    {
        State = state;
        SprintsLeft = sprints;
    }

    private void SetFacingAnimation()
    {
        var dirOrd = (int)(Facing - 1);

        if ((Facing & Direction.Down) != 0)
        {
            dirOrd = (Facing & Direction.Right) != 0 ? 0 : 1;
        }
        else if ((Facing & Direction.Up) != 0)
        {
            dirOrd = (Facing & Direction.Right) != 0 ? 2 : 3;
        }

        Animator.Animation = Graphics.GetAnimation(Spec.Sheet, Spec.AnimationMap[dirOrd]);
    }

    private void UpdateStill()
    {
        if (ObjTimer == 0)
        {
            State = 0;
        }
    }

    private void UpdateHastening()
    {
        CurSpeed++;
        if ((CurSpeed & 0xE0) >= Spec.Speed)
        {
            CurSpeed = Spec.Speed;
            State = FlyingActorState.FullSpeed;
        }
    }

    private void UpdateSlowing()
    {
        CurSpeed--;
        if ((CurSpeed & 0xE0) <= 0)
        {
            CurSpeed = 0;
            State = FlyingActorState.Still;
            ObjTimer = (byte)(Game.Random.Next(64) + 64);
        }
    }

    private void UpdateFullSpeed()
    {
        UpdateFullSpeedImpl();
    }

    protected virtual void UpdateFullSpeedImpl()
    {
        var r = Game.Random.GetByte();

        State = r switch {
            >= 0xB0 => FlyingActorState.Chase,
            >= 0x20 => FlyingActorState.Turn,
            _ => FlyingActorState.Slowing,
        };
        SprintsLeft = 6;
    }

    private void UpdateTurn()
    {
        UpdateTurnImpl();
    }

    protected virtual void UpdateTurnImpl()
    {
        if (ObjTimer != 0) return;

        SprintsLeft--;
        if (SprintsLeft == 0)
        {
            State = FlyingActorState.FullSpeed;
            return;
        }

        ObjTimer = 0x10;

        Facing = TurnRandomly8(Facing);
    }

    private void UpdateChase()
    {
        UpdateChaseImpl();
    }

    protected virtual void UpdateChaseImpl()
    {
        if (ObjTimer != 0) return;

        SprintsLeft--;
        if (SprintsLeft == 0)
        {
            State = FlyingActorState.FullSpeed;
            return;
        }

        ObjTimer = 0x10;

        Facing = TurnTowardsPlayer8(X, Y, Facing);
    }
}

internal abstract class StdFlyerActor : FlyingActor
{
    protected StdFlyerActor(World world, ObjType type, FlyerSpec spec, int x, int y, Direction facing)
        : base(world, type, spec, x, y)
    {
        Facing = facing;
    }
}

internal sealed class PeahatActor : StdFlyerActor
{
    private static readonly ImmutableArray<AnimationId> _peahatAnimMap = [
        AnimationId.OW_Peahat,
        AnimationId.OW_Peahat,
        AnimationId.OW_Peahat,
        AnimationId.OW_Peahat
    ];

    private static readonly FlyerSpec _peahatSpec = new(_peahatAnimMap, TileSheet.NpcsOverworld, Palette.Red, 0xA0);

    public PeahatActor(World world, int x, int y)
        : base(world, ObjType.Peahat, _peahatSpec, x, y, Direction.Up)
    {
        Decoration = 0;
        CurSpeed = 0x1F;
        ObjTimer = 0;
    }

    public override void Update()
    {
        if (ShoveDirection != 0)
        {
            ObjShove();
        }
        else if (!IsStunned)
        {
            UpdateStateAndMove();
        }

        if (State == FlyingActorState.Still)
        {
            CheckCollisions();
        }
        else
        {
            CheckPlayerCollision();
        }
    }
}

internal sealed class FlyingGhiniActor : FlyingActor
{
    private enum FlyingGhiniState
    {
        FadingIn, Flying
    }

    private static readonly ImmutableArray<AnimationId> _flyingGhiniAnimMap = [
        AnimationId.OW_Ghini_Right,
        AnimationId.OW_Ghini_Left,
        AnimationId.OW_Ghini_UpRight,
        AnimationId.OW_Ghini_UpLeft
    ];

    private static readonly FlyerSpec _flyingGhiniSpec = new(_flyingGhiniAnimMap, TileSheet.NpcsOverworld, Palette.Blue, 0xA0);

    private FlyingGhiniState _ghiniState;

    public FlyingGhiniActor(World world, int x, int y)
        : base(world, ObjType.FlyingGhini, _flyingGhiniSpec, x, y)
    {
        Decoration = 0;
        Facing = Direction.Up;
        CurSpeed = 0x1F;
    }

    public override void Update()
    {
        if (_ghiniState == FlyingGhiniState.FadingIn)
        {
            if (ObjTimer == 0) _ghiniState = FlyingGhiniState.Flying;
            return;
        }

        if (!World.HasItem(ItemSlot.Clock))
        {
            UpdateStateAndMove();
        }

        CheckPlayerCollision();
    }

    public override void Draw()
    {
        if (_ghiniState == FlyingGhiniState.FadingIn)
        {
            if ((ObjTimer & 1) == 1)
            {
                base.Draw();
            }

            return;
        }

        base.Draw();
    }

    protected override void UpdateFullSpeedImpl()
    {
        var r = Game.Random.GetByte();
        var newState = r switch
        {
            >= 0xA0 => FlyingActorState.Chase,
            >= 8 => FlyingActorState.Turn,
            _ => FlyingActorState.Slowing,
        };

        GoToState(newState, 6);
    }

    protected override int GetFrame()
    {
        return 0;
    }
}

internal sealed class KeeseActor : FlyingActor
{
    private static readonly ImmutableArray<AnimationId> _keeseAnimMap = [
        AnimationId.UW_Keese,
        AnimationId.UW_Keese,
        AnimationId.UW_Keese,
        AnimationId.UW_Keese
    ];

    private static readonly FlyerSpec _blueKeeseSpec = new(_keeseAnimMap, TileSheet.NpcsUnderworld, Palette.Blue, 0xC0);
    private static readonly FlyerSpec _redKeeseSpec = new(_keeseAnimMap, TileSheet.NpcsUnderworld, Palette.Red, 0xC0);
    private static readonly FlyerSpec _blackKeeseSpec = new(_keeseAnimMap, TileSheet.NpcsUnderworld, Palette.SeaPal, 0xC0);

    private KeeseActor(World world, ObjType type, FlyerSpec spec, int startSpeed, int x, int y)
        : base(world, type, spec, x, y)
    {
        CurSpeed = startSpeed;
        Facing = Game.Random.GetDirection8();
    }

    public static KeeseActor Make(World world, ActorColor color, int x, int y)
    {
        return color switch {
            ActorColor.Red => new KeeseActor(world, ObjType.RedKeese, _redKeeseSpec, 0x7F, x, y),
            ActorColor.Blue => new KeeseActor(world, ObjType.BlueKeese, _blueKeeseSpec, 0x1F, x, y),
            ActorColor.Black => new KeeseActor(world, ObjType.BlackKeese, _blackKeeseSpec, 0x7F, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(KeeseActor)}.")
        };
    }

    public override void Update()
    {
        if (!World.HasItem(ItemSlot.Clock)
            && !World.IsLiftingItem())
        {
            UpdateStateAndMove();
        }

        CheckCollisions();

        ShoveDirection = 0;
        ShoveDistance = 0;
    }

    protected override void UpdateFullSpeedImpl()
    {
        var r = Game.Random.GetByte();
        var newstate = r switch {
            >= 0xA0 => FlyingActorState.Chase,
            >= 0x20 => FlyingActorState.Turn,
            _ => FlyingActorState.Slowing,
        };

        GoToState(newstate, 6);
    }

    protected override int GetFrame()
    {
        return (MoveCounter & 2) >> 1;
    }
}

internal sealed class MoldormActor : FlyingActor
{
    private const int Count = 2;
    private const int Length = 4;

    private static readonly ImmutableArray<AnimationId> _moldormAnimMap = [
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm
    ];

    private static readonly FlyerSpec _moldormSpec = new(_moldormAnimMap, TileSheet.NpcsUnderworld, Palette.Red, 0x80);

    public bool IsHead => _head == null;

    private readonly MoldormActor? _head;
    private Direction _oldFacing;
    private readonly List<MoldormActor> _bodyParts = new();

    public override bool IsReoccuring => false;

    private MoldormActor(World world, MoldormActor? head, int x, int y, Direction facing = Direction.None)
        : base(world, ObjType.Moldorm, _moldormSpec, x, y)
    {
        _head = head;
        Decoration = 0;
        Facing = facing;
        _oldFacing = facing;

        CurSpeed = 0x80;

        GoToState(FlyingActorState.Chase, 1);
    }

    public static MoldormActor MakeSet(
        World world,
        int x = 0x80,
        int y = 0x70,
        int count = Count,
        int length = Length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count, nameof(count));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length, nameof(length));

        MoldormActor? firstMoldorm = null;

        for (var moldormCount = 0; moldormCount < count; moldormCount++)
        {
            var head = new MoldormActor(world, null, x, y, world.Game.Random.GetDirection8());
            world.AddObject(head);
            firstMoldorm ??= head;

            for (var i = 0; i < length; i++)
            {
                var bodyPart = new MoldormActor(world, head, x, y);
                head._bodyParts.Add(bodyPart);
                world.AddObject(bodyPart);
            }

            head._bodyParts.Add(head);
        }

        world.RoomObjCount += count * length;

        return firstMoldorm ?? throw new Exception();
    }

    public override void Update()
    {
        if (Facing == Direction.None) return;

        if (!World.HasItem(ItemSlot.Clock))
        {
            UpdateStateAndMove();
        }

        CheckMoldormCollisions();
    }

    private void CheckMoldormCollisions()
    {
        // ORIGINAL: This is just like CheckLamnolaCollisions; but it saves stateTimer, and plays sounds.
        // Check there for more details.

        var origFacing = Facing;
        var origStateTimer = ObjTimer;

        CheckCollisions();

        ObjTimer = origStateTimer;
        Facing = origFacing;

        // Don't do anything else if this part isn't yet dead.
        if (Decoration == 0) return;

        Game.Sound.PlayEffect(SoundEffect.BossHit);
        Game.Sound.StopEffect(StopEffect.AmbientInstance);

        // When a part dies, we want to kill the lowest part on the tail.
        var head = _head ?? this;
        var tailmost = head._bodyParts[0];

        // The last part, the head, is dead. Let's return because the job is done, all parts have died.
        if (tailmost.IsHead) return;

        // Bring this segment back to life...
        HP = 0x20;
        ShoveDirection = 0;
        ShoveDistance = 0;
        Decoration = 0;

        // ...then kill the tail-most piece by replacing with a dummy.
        var dummy = new DeadDummyActor(World, X, Y);
        World.AddOnlyObject(tailmost, dummy);
        head._bodyParts.RemoveAt(0);
    }

    protected override void UpdateTurnImpl()
    {
        if (IsHead)
        {
            base.UpdateTurnImpl();
            UpdateHeadSubstates();
        }
    }

    protected override void UpdateChaseImpl()
    {
        if (IsHead)
        {
            base.UpdateChaseImpl();
            UpdateHeadSubstates();
        }
    }

    private void UpdateHeadSubstates()
    {
        if (ObjTimer == 0)
        {
            var r = Game.Random.GetByte();
            GoToState(r < 0x40 ? FlyingActorState.Turn : FlyingActorState.Chase, 8);

            ObjTimer = 0x10;

            // This is the head and the head is the last body part, so get the
            // one attached to it.
            if (_bodyParts.Count > 1)
            {
                var obj = _bodyParts[^2];
                if (obj.Facing != Direction.None)
                {
                    ShiftFacings();
                }
            }
        }
        else
        {
            ShiftFacings();
        }
    }

    private void ShiftFacings()
    {
        if (ObjTimer != 0x10) return;

        if (DeferredDir != Direction.None)
        {
            Facing = DeferredDir;
            DeferredDir = Direction.None;
        }

        for (var i = 0; i < _bodyParts.Count - 1; i++)
        {
            var curMoldorm = _bodyParts[i];
            var nextMoldorm = _bodyParts[i + 1];

            var nextOldFacing = nextMoldorm._oldFacing;
            curMoldorm._oldFacing = nextOldFacing;
            curMoldorm.Facing = nextOldFacing;
        }

        _oldFacing = Facing;
    }

    protected override int GetFrame()
    {
        return 0;
    }
}

internal enum PatraType { Circle, Spin }

internal sealed class PatraActor : FlyingActor
{
    private const int PatraX = 0x80;
    private const int PatraY = 0x70;
    public const int ChildPatraCount = 8;

    private static readonly ImmutableArray<AnimationId> _patraAnimMap = [
        AnimationId.B3_Patra,
        AnimationId.B3_Patra,
        AnimationId.B3_Patra,
        AnimationId.B3_Patra
    ];

    private static readonly FlyerSpec _patraSpec = new(_patraAnimMap, TileSheet.Boss9, Palette.Blue, 0x40);

    private int _xMove;
    private int _yMove;
    private int _maneuverState;
    private int _childStateTimer;

    // JOE: Oh man, this could use a reworking.
    public int[] PatraAngle = new int[ChildPatraCount + 1];
    public int[] PatraState = new int[ChildPatraCount + 1];

    public override bool IsReoccuring => false;

    private PatraActor(World world, ObjType type, int x, int y)
        : base(world, type, _patraSpec, x, y)
    {
        InvincibilityMask = 0xFE;
        Facing = Direction.Up;
        CurSpeed = 0x1F;

        Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);

        Array.Fill(PatraAngle, 0);
        Array.Fill(PatraState, 0);
    }

    public static PatraActor MakePatra(World world, PatraType patraType, int x = PatraX, int y = PatraY)
    {
        var type = patraType switch
        {
            PatraType.Circle => ObjType.Patra1,
            PatraType.Spin => ObjType.Patra2,
            _ => throw new ArgumentOutOfRangeException(nameof(patraType), patraType, "patraType unknown."),
        };

        var patra = new PatraActor(world, type, x, y);
        world.AddObject(patra);

        // Index 0 is used for the parent.
        for (var i = 1; i <= ChildPatraCount; i++)
        {
            var child = PatraChildActor.Make(world, patra, i, patraType);
            world.AddObject(child);
        }

        return patra;
    }

    public int GetXMove() => _xMove;
    public int GetYMove() => _yMove;
    public int GetManeuverState() => _maneuverState;

    public override void Update()
    {
        if (_childStateTimer > 0)
        {
            _childStateTimer--;
        }

        var origX = X;
        var origY = Y;

        UpdateStateAndMove();

        _xMove = X - origX;
        _yMove = Y - origY;

        var foundChild = World.GetObjects<PatraChildActor>().Any();

        if (foundChild)
        {
            CheckPlayerCollision();
        }
        else
        {
            CheckCollisions();
            PlayBossHitSoundIfHit();
            PlayBossHitSoundIfDied();
        }

        if (_childStateTimer == 0 && PatraAngle[2] == 0)
        {
            _maneuverState ^= 1;
            // ORIGINAL: I don't see how this is ever $50. See Patra's Update routine.
            _childStateTimer = 0xFF;
        }
    }

    protected override void UpdateFullSpeedImpl()
    {
        var r = Game.Random.GetByte();
        GoToState(r >= 0x40 ? FlyingActorState.Chase : FlyingActorState.Turn, 8);
    }
}

internal sealed class PatraChildActor : MonsterActor
{
    private static readonly ImmutableArray<byte> _patraEntryAngles = [0x14, 0x10, 0xC, 0x8, 0x4, 0, 0x1C];
    private static readonly ImmutableArray<int> _shiftCounts = [6, 5, 6, 6];
    private static readonly ImmutableArray<byte> _sinCos = [0x00, 0x18, 0x30, 0x47, 0x5A, 0x6A, 0x76, 0x7D, 0x80, 0x7D, 0x76, 0x6A, 0x5A, 0x47, 0x30, 0x18];

    private int _x;
    private int _y;
    private int _angleAccum;
    private readonly SpriteAnimator _animator;
    private readonly PatraActor _parent;
    private readonly int _index;

    public override bool IsReoccuring => false;

    private PatraChildActor(World world, PatraActor parent, int index, ObjType type, int x, int y)
        : base(world, type, x, y)
    {
        if (type is not (ObjType.PatraChild1 or ObjType.PatraChild2))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid type for PatraChildActor.");
        }

        InvincibilityMask = 0xFE;
        Decoration = 0;

        ObjTimer = 0;

        _parent = parent;
        _index = index;
        _animator = new SpriteAnimator(TileSheet.Boss9, AnimationId.B3_PatraChild)
        {
            DurationFrames = 4,
            Time = 0,
        };
    }

    public static PatraChildActor Make(World world, PatraActor parent, int index, PatraType patraType, int x = 0, int y = 0)
    {
        var objtype = patraType switch
        {
            PatraType.Circle => ObjType.PatraChild1,
            PatraType.Spin => ObjType.PatraChild2,
            _ => throw new ArgumentOutOfRangeException(nameof(patraType), patraType, "patraType unknown."),
        };

        return new PatraChildActor(world, parent, index, objtype, x, y);
    }

    private static short ShiftMult(int mask, int addend, int shiftCount)
    {
        short n = 0;

        do
        {
            n <<= 1;
            mask <<= 1;
            if ((mask & 0x100) != 0)
            {
                n += (short)addend;
            }
            shiftCount--;
        } while (shiftCount != 0);

        return n;
    }

    public override void Update()
    {
        if (_parent.PatraState[_index] == 0)
        {
            UpdateStart();
            return;
        }

        UpdateTurn();
        _animator.Advance();

        if (_parent.PatraState[0] == 0) return;

        CheckCollisions();
        if (Decoration != 0)
        {
            var dummy = new DeadDummyActor(World, X, Y);
            World.AddOnlyObject(this, dummy);
        }
    }

    public override void Draw()
    {
        if (_parent.PatraState[_index] != 0)
        {
            var pal = CalcPalette(Palette.Red);
            _animator.Draw(TileSheet.Boss9, X, Y, pal);
        }
    }

    private bool IsLastChild => _index == PatraActor.ChildPatraCount;

    private void UpdateStart()
    {
        if (_index > 1)
        {
            if (_parent.PatraState[1] == 0) return;
            var index = _index - 2;
            if (_parent.PatraAngle[1] != _patraEntryAngles[index]) return;
        }

        var distance = ObjType == ObjType.PatraChild1 ? 0x2C : 0x18;

        if (IsLastChild)
        {
            _parent.PatraState[0] = 1;
        }
        _parent.PatraState[_index] = 1;
        _parent.PatraAngle[_index] = 0x18;

        _x = _parent.X << 8;
        _y = (_parent.Y - distance) << 8;

        X = _x >> 8;
        Y = _y >> 8;
    }

    private void UpdateTurn()
    {
        _x += _parent.GetXMove() << 8;
        _y += _parent.GetYMove() << 8;

        var step = ObjType == ObjType.PatraChild1 ? 0x70 : 0x60;
        var angleFix = (short)((_parent.PatraAngle[_index] << 8) | _angleAccum);
        angleFix -= (short)step;
        _angleAccum = angleFix & 0xFF;
        _parent.PatraAngle[_index] = (angleFix >> 8) & 0x1F;

        int yShiftCount;
        int xShiftCount;
        var index = _parent.GetManeuverState();

        if (ObjType == ObjType.PatraChild1)
        {
            yShiftCount = _shiftCounts[index];
            xShiftCount = _shiftCounts[index + 2];
        }
        else
        {
            yShiftCount = _shiftCounts[index + 1];
            xShiftCount = yShiftCount;
        }

        const int turnSpeed = 0x20;

        index = _parent.PatraAngle[_index] & 0x0F;
        var cos = _sinCos[index];
        var n = ShiftMult(cos, turnSpeed, xShiftCount);

        if ((_parent.PatraAngle[_index] & 0x18) < 0x10)
        {
            _x += n;
        }
        else
        {
            _x -= n;
        }

        index = (_parent.PatraAngle[_index] + 8) & 0x0F;
        var sin = _sinCos[index];
        n = ShiftMult(sin, turnSpeed, yShiftCount);

        if (((_parent.PatraAngle[_index] - 8) & 0x18) < 0x10)
        {
            _y += n;
        }
        else
        {
            _y -= n;
        }

        X = _x >> 8;
        Y = _y >> 8;
    }
}

internal readonly record struct JumperSpec(
    ImmutableArray<AnimationId> AnimationMap,
    int AnimationTimer,
    int JumpFrame,
    Palette Palette,
    int Speed,
    ImmutableArray<byte> AccelMap);

internal abstract class JumperActor : MonsterActor
{
    public static readonly ImmutableArray<Direction> JumperStartDirs = [(Direction)1, (Direction)2, (Direction)5, (Direction)0xA];
    private static readonly ImmutableArray<int> _targetYOffset = [0, 0, 0, 0, 0, 0x20, 0x20, 0, 0, -0x20, -0x20];

    private int _curSpeed;
    private int _accelStep;
    private int _state;
    private int _targetY;
    private int _reversesPending;

    private readonly SpriteAnimator _animator;
    private readonly JumperSpec _spec;
    private readonly TileSheet _tilesheet;

    protected JumperActor(World world, ObjType type, WorldLevel level, JumperSpec spec, int x, int y)
        : base(world, type, x, y)
    {
        _spec = spec;
        _tilesheet = level.GetNpcTileSheet();

        _animator = new SpriteAnimator(_tilesheet, spec.AnimationMap[0])
        {
            Time = 0,
            DurationFrames = spec.AnimationTimer
        };

        Facing = world.Game.Random.GetRandom(JumperStartDirs);
        ObjTimer = (byte)((int)Facing * 4);

        if (this is BoulderActor)
        {
            BouldersActor.Count++;
            Decoration = 0;
        }
    }

    public override bool Delete()
    {
        if (base.Delete())
        {
            if (this is BoulderActor)
            {
                BouldersActor.Count--;
            }
            return true;
        }
        return false;
    }

    public override void Update()
    {
        if (ShoveDirection == 0 && !IsStunned)
        {
            if (_state == 0)
            {
                UpdateStill();
            }
            else
            {
                UpdateJump();
            }
        }

        if (this is BoulderActor)
        {
            _animator.Advance();
            CheckPlayerCollision();
            if (Y >= World.WorldLimitBottom)
            {
                Delete();
            }
        }
        else
        {
            if (_state == 0 && ObjTimer >= 0x21)
            {
                _animator.Advance();
            }
            CheckCollisions();
        }
    }

    public override void Draw()
    {
        var pal = CalcPalette(_spec.Palette);

        if (_state == 1 && _spec.JumpFrame >= 0)
        {
            _animator.DrawFrame(_tilesheet, X, Y, pal, _spec.JumpFrame);
        }
        else
        {
            _animator.Draw(_tilesheet, X, Y, pal);
        }
    }

    private void UpdateStill()
    {
        if (ObjTimer != 0) return;

        _state = 1;
        Facing = TurnTowardsPlayer8(X, Y, Facing);

        if ((Facing & (Direction.Right | Direction.Left)) == 0)
        {
            Facing |= GetXDirToPlayer(X);
        }

        SetupJump();
    }

    private void UpdateJump()
    {
        var dir = CheckWorldMarginH(X, Facing, false);
        if (this is not BoulderActor)
        {
            dir = CheckWorldMarginV(Y, dir, false);
        }

        if (dir == Direction.None)
        {
            Facing = Facing.GetOppositeDir8();
            _reversesPending++;
            SetupJump();
            return;
        }

        ConstrainFacing();
        _reversesPending = 0;
        var acceleration = _spec.AccelMap[(int)Facing];

        UpdateY(2, acceleration);
        if ((Facing & Direction.Left) != 0)
        {
            X--;
        }
        else if ((Facing & Direction.Right) != 0)
        {
            X++;
        }

        if (_curSpeed >= 0 && Math.Abs(Y - _targetY) < 3)
        {
            _state = 0;
            ObjTimer = this is BoulderActor ? (byte)0 : (byte)GetRandomStillTime();
        }
    }

    private void UpdateY(int maxSpeed, int acceleration)
    {
        Y += _curSpeed;
        _accelStep += acceleration;

        var carry = _accelStep >> 8;
        _accelStep &= 0xFF;

        _curSpeed += carry;

        if (_curSpeed >= maxSpeed && _accelStep >= 0x80)
        {
            _curSpeed = maxSpeed;
            _accelStep = 0;
        }
    }

    private void SetupJump()
    {
        if (_reversesPending >= 2)
        {
            Facing ^= (Direction.Right | Direction.Left);
            _reversesPending = 0;
        }

        ConstrainFacing();
        _targetY = Y + _targetYOffset[(int)Facing];
        _curSpeed = _spec.Speed;
        _accelStep = 0;
    }

    private int GetRandomStillTime()
    {
        var r = Game.Random.GetByte();
        var t = (byte)(r + 0x10);

        if (t < 0x20)
        {
            t -= 0x40;
        }

        if (ObjType != ObjType.BlueTektite)
        {
            t &= 0x7F;
            if (r >= 0xA0)
            {
                t &= 0x0F;
            }
        }
        return t;
    }

    private void ConstrainFacing()
    {
        if (this is BoulderActor)
        {
            Facing &= (Direction.Right | Direction.Left);
            Facing |= Direction.Down;
        }
    }
}

internal sealed class BoulderActor : JumperActor
{
    private static readonly ImmutableArray<AnimationId> _boulderAnimMap = [
        AnimationId.OW_Boulder,
        AnimationId.OW_Boulder,
        AnimationId.OW_Boulder,
        AnimationId.OW_Boulder
    ];

    private static readonly ImmutableArray<byte> _boulderSpeeds = [
        0x60, 0x60,
        0x60, 0x60, 0x60, 0x60, 0x60
    ];

    private static readonly JumperSpec _boulderSpec = new(_boulderAnimMap, 12, -1, Palette.Red, -2, _boulderSpeeds);

    public BoulderActor(World world, int x, int y)
        : base(world, ObjType.Boulder, WorldLevel.Overworld, _boulderSpec, x, y)
    {
    }
}

internal sealed class TektiteActor : JumperActor
{
    private static readonly ImmutableArray<AnimationId> _tektiteAnimMap = [
        AnimationId.OW_Tektite,
        AnimationId.OW_Tektite,
        AnimationId.OW_Tektite,
        AnimationId.OW_Tektite
    ];

    private static readonly ImmutableArray<byte> _blueTektiteSpeeds = [0, 0x40, 0x40, 0, 0, 0x40, 0x40, 0, 0, 0x30, 0x30];
    private static readonly ImmutableArray<byte> _redTektiteSpeeds = [0, 0x80, 0x80, 0, 0, 0x80, 0x80, 0, 0, 0x50, 0x50];

    private static readonly JumperSpec _blueTektiteSpec = new(_tektiteAnimMap, 32, 1, Palette.Blue, -3, _blueTektiteSpeeds);
    private static readonly JumperSpec _redTektiteSpec = new(_tektiteAnimMap, 32, 1, Palette.Red, -4, _redTektiteSpeeds);

    private TektiteActor(World world, ObjType type, JumperSpec spec, int x, int y)
        : base(world, type, WorldLevel.Overworld, spec, x, y)
    {
        if (type is not (ObjType.BlueTektite or ObjType.RedTektite))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(TektiteActor)}.");
        }
    }

    public static TektiteActor Make(World world, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new TektiteActor(world, ObjType.BlueTektite, _blueTektiteSpec, x, y),
            ActorColor.Red => new TektiteActor(world, ObjType.RedTektite, _redTektiteSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(TektiteActor)}.")
        };
    }
}

internal sealed class BouldersActor : MonsterActor
{
    private const int MaxBoulders = 3;

    public static int Count;

    public BouldersActor(World world, int x, int y)
        : base(world, ObjType.Boulders, x, y)
    {
        var facing = (int)world.Game.Random.GetRandom(JumperActor.JumperStartDirs);
        ObjTimer = (byte)(facing * 4);
        Decoration = 0;
    }

    public override void Update()
    {
        if (ObjTimer != 0) return;

        if (Count < MaxBoulders)
        {
            var playerPos = World.GetObservedPlayerPos();
            const int y = World.WorldLimitTop;
            var x = Game.Random.GetByte();

            // Make sure the new boulder is in the same half of the screen.
            if (playerPos.X < World.WorldMidX)
            {
                x %= 0x80;
            }
            else
            {
                x |= 0x80;
            }

            var boulder = new BoulderActor(World, x, y);
            World.AddObject(boulder);

            ObjTimer = (byte)Game.Random.Next(32);

            return;
        }

        var r = Game.Random.GetByte();
        ObjTimer = (byte)((ObjTimer + r) % 256);
    }

    public override void Draw()
    {
    }

    public static void ClearRoomData() => Count = 0;
}

internal sealed class TrapActor : MonsterActor
{
    private static readonly ImmutableArray<Point> _trapPos = [
        new Point(0x20, 0x60),
        new Point(0x20, 0xC0),
        new Point(0xD0, 0x60),
        new Point(0xD0, 0xC0),
        new Point(0x40, 0x90),
        new Point(0xB0, 0x90)
    ];

    private static readonly ImmutableArray<int> _trapAllowedDirs = [5, 9, 6, 0xA, 1, 2];

    private int _state;
    private int _speed;
    private int _origCoord;

    private readonly SpriteImage _image;
    private readonly int _trapIndex;

    public override bool CountsAsLiving => false;

    private TrapActor(World world, int trapIndex, int x, int y)
        : base(world, ObjType.Trap, x, y)
    {
        _trapIndex = trapIndex;
        _image = new SpriteImage(TileSheet.NpcsUnderworld, AnimationId.UW_Trap);
    }

    public static TrapActor MakeSet(World world, int count)
    {
        Debug.Assert(count is >= 1 and <= 6);
        count = Math.Clamp(count, 1, 6);

        TrapActor? first = null;
        for (var i = 0; i < count; i++)
        {
            var obj = new TrapActor(world, i, _trapPos[i].X, _trapPos[i].Y);
            first ??= obj;
            world.AddObject(obj);
        }

        return first ?? throw new Exception(); ;
    }

    public override void Update()
    {
        if (_state == 0)
        {
            UpdateIdle();
        }
        else
        {
            UpdateMoving();
        }

        CheckCollisions();
    }

    private void UpdateIdle()
    {
        var player = Game.Player;
        var playerX = player.X;
        var playerY = player.Y;
        var dir = Direction.None;
        var distX = Math.Abs(playerX - X);
        var distY = Math.Abs(playerY - Y);

        if (distY >= 0xE)
        {
            if (distX < 0xE)
            {
                dir = playerY < Y ? Direction.Up : Direction.Down;
                _origCoord = Y;
            }
        }
        else
        {
            if (distX >= 0xE)
            {
                dir = playerX < X ? Direction.Left : Direction.Right;
                _origCoord = X;
            }
        }

        if (dir != Direction.None)
        {
            if ((dir & (Direction)_trapAllowedDirs[_trapIndex]) != 0)
            {
                Facing = dir;
                _state++;
                _speed = 0x70;
            }
        }
    }

    private void UpdateMoving()
    {
        MoveDirection(_speed, Facing);

        if ((TileOffset & 0x0F) == 0)
        {
            TileOffset &= 0x0F;
        }

        CheckPlayerCollision();

        int coord;
        int limit;

        if (Facing.IsVertical())
        {
            coord = Y;
            limit = 0x90;
        }
        else
        {
            coord = X;
            limit = 0x78;
        }

        if (_state == 1)
        {
            if (Math.Abs(coord - limit) < 5)
            {
                Facing = Facing.GetOppositeDirection();
                _speed = 0x20;
                _state++;
            }
        }
        else
        {
            if (coord == _origCoord)
            {
                _state = 0;
            }
        }
    }

    public override void Draw()
    {
        _image.Draw(TileSheet.NpcsUnderworld, X, Y, Palette.Blue);
    }
}

internal sealed class RopeActor : MonsterActor
{
    private static readonly ImmutableArray<AnimationId> _ropeAnimMap = [
        AnimationId.UW_Rope_Right,
        AnimationId.UW_Rope_Left,
        AnimationId.UW_Rope_Right,
        AnimationId.UW_Rope_Right
    ];

    private const int RopeNormalSpeed = 0x20;
    private const int RopeFastSpeed = 0x60;

    private int _speed;
    private readonly SpriteAnimator _animator;

    public RopeActor(World world, int x, int y)
        : base(world, ObjType.Rope, x, y)
    {
        _animator = new SpriteAnimator
        {
            Time = 0,
            DurationFrames = 20
        };

        InitCommonFacing();
        SetFacingAnimation();

        var profile = World.Profile;

        HP = (byte)0x10; // JOE: TODO: QUEST  (profile.Quest == 0 ? 0x10 : 0x40);
    }

    public override void Update()
    {
        var origFacing = Facing;

        MovingDirection = Facing;

        if (!IsStunned)
        {
            ObjMove(_speed);

            if ((TileOffset & 0x0F) == 0)
            {
                TileOffset &= 0x0F;
            }

            if (_speed != RopeFastSpeed && ObjTimer == 0)
            {
                ObjTimer = (byte)Game.Random.Next(0x40);
                TurnToUnblockedDir();
            }
        }

        if (Facing != origFacing)
        {
            _speed = RopeNormalSpeed;
        }

        TargetPlayer();

        _animator.Advance();

        CheckCollisions();
        SetFacingAnimation();
    }

    private void TargetPlayer()
    {
        if (_speed != RopeNormalSpeed || TileOffset != 0) return;

        var player = Game.Player;

        var xDist = Math.Abs(player.X - X);
        if (xDist < 8)
        {
            Facing = player.Y < Y ? Direction.Up : Direction.Down;
            _speed = RopeFastSpeed;
        }
        else
        {
            var yDist = Math.Abs(player.Y - Y);
            if (yDist < 8)
            {
                Facing = player.X < X ? Direction.Left : Direction.Right;
                _speed = RopeFastSpeed;
            }
        }
    }

    public override void Draw()
    {
        var profile = World.Profile;
        var pal = true // JOE: TODO: QUEST profile.Quest == 0
            ? CalcPalette(Palette.Red)
            : Palette.Player + (Game.FrameCounter & 3);

        _animator.Draw(TileSheet.NpcsUnderworld, X, Y, pal);
    }

    private void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        _animator.Animation = Graphics.GetAnimation(TileSheet.NpcsUnderworld, _ropeAnimMap[dirOrd]);
    }
}

internal sealed class PolsVoiceActor : MonsterActor
{
    private static readonly ImmutableArray<int> _polsVoiceXSpeeds = [1, -1, 0, 0];
    private static readonly ImmutableArray<int> _polsVoiceYSpeeds = [0, 0, 1, -1];
    private static readonly ImmutableArray<int> _polsVoiceJumpSpeeds = [-3, -3, -1, -4];
    private static readonly ImmutableArray<int> _polsVoiceJumpLimits = [0, 0, 0x20, -0x20];

    private int _curSpeed;
    private int _accelStep;
    private int _state;
    private int _stateTimer;
    private int _targetY;
    private readonly SpriteAnimator _animator;

    public PolsVoiceActor(World world, int x, int y)
        : base(world, ObjType.PolsVoice, x, y)
    {
        InitCommonFacing();

        _animator = new SpriteAnimator(TileSheet.NpcsUnderworld, AnimationId.UW_PolsVoice)
        {
            DurationFrames = 16,
            Time = 0
        };
    }

    public override void Update()
    {
        if (!IsStunned && (Game.FrameCounter & 1) == 0)
        {
            Move();
        }

        _animator.Advance();
        InvincibilityMask = 0xFE;
        CheckCollisions();
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.Player);
        _animator.Draw(TileSheet.NpcsUnderworld, X, Y, pal);
    }

    private void Move()
    {
        UpdateX();
        if (!UpdateY()) return;

        var x = X;
        var y = Y;

        var collision = World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
        {
            x += 0xE;
            y += 6;
            collision = World.CollidesWithTileStill(x, y);
            if (!collision.Collides) return;
        }

        if (collision.TileBehavior.CollidesWall())
        {
            Facing = Facing.GetOppositeDirection();

            if (Facing.IsHorizontal())
            {
                UpdateX();
                UpdateX();
            }
        }
        else
        {
            SetupJump();
        }
    }

    private void UpdateX()
    {
        var ord = Facing.GetOrdinal();
        X += _polsVoiceXSpeeds[ord];
    }

    private bool UpdateY()
    {
        return _state == 1 ? UpdateJumpY() : UpdateWalkY();
    }

    private bool UpdateJumpY()
    {
        const int acceleration = 0x38;

        _accelStep += acceleration;

        var carry = _accelStep >> 8;
        _accelStep &= 0xFF;

        _curSpeed += carry;
        Y += _curSpeed;

        if (_curSpeed >= 0 && Y >= _targetY)
        {
            _state = 0;
            _curSpeed = 0;
            _accelStep = 0;
            var r = Game.Random.GetByte();
            Facing = (r & 3).GetOrdDirection();
            _stateTimer = (r & 0x40) + 0x30;
            X = (X + 8) & 0xF0;
            Y = (Y + 8) & 0xF0;
            Y -= 3;
        }
        return true;
    }

    private bool UpdateWalkY()
    {
        if (_stateTimer == 0)
        {
            SetupJump();
            return false;
        }

        _stateTimer--;
        var ord = Facing.GetOrdinal();
        Y += _polsVoiceYSpeeds[ord];
        return true;
    }

    private void SetupJump()
    {
        if (_state != 0) return;

        var dirOrd = Y switch {
            < 0x78 => 2,
            >= 0xA8 => 3,
            _ => Facing.GetOrdinal()
        };

        _curSpeed = _polsVoiceJumpSpeeds[dirOrd];
        _targetY = Y + _polsVoiceJumpLimits[dirOrd];

        Facing = dirOrd.GetOrdDirection();
        _state = 1;
    }
}

internal sealed class RedWizzrobeActor : WizzrobeBase
{
    private static readonly ImmutableArray<Direction> _wizzrobeDirs = [
        Direction.Down,
        Direction.Up,
        Direction.Right,
        Direction.Left
    ];

    private static readonly ImmutableArray<int> _wizzrobeXOffsets = [
        0x00, 0x00, -0x20, 0x20, 0x00, 0x00, -0x40, 0x40,
        0x00, 0x00, -0x30, 0x30, 0x00, 0x00, -0x50, 0x50
    ];

    private static readonly ImmutableArray<int> _wizzrobeYOffsets = [
        -0x20, 0x20, 0x00, 0x00, -0x40, 0x40, 0x00, 0x00,
        -0x30, 0x30, 0x00, 0x00, -0x50, 0x50, 0x00, 0x00
    ];

    private static readonly ImmutableArray<int> _allWizzrobeCollisionXOffsets = [0x0F, 0, 0, 4, 8, 0, 0, 4, 8, 0];
    private static readonly ImmutableArray<int> _allWizzrobeCollisionYOffsets = [4, 4, 0, 8, 8, 8, 0, -8, 0, 0];

    private byte _stateTimer;
    private byte _flashTimer;

    private readonly SpriteAnimator _animator;

    public RedWizzrobeActor(World world, int x, int y)
        : base(world, ObjType.RedWizzrobe, x, y)
    {
        Decoration = 0;
        ObjTimer = 0;
        _animator = new SpriteAnimator
        {
            DurationFrames = 8,
            Time = 0
        };
    }

    public override void Update()
    {
        if (World.HasItem(ItemSlot.Clock))
        {
            // Force them visible.
            _stateTimer = 2 << 6;
            _animator.Advance();
            CheckWizzrobeCollisions();
            return;
        }

        _stateTimer--;

        var state = GetState();

        Action func = state switch
        {
            0 => UpdateHidden,
            1 => UpdateGoing,
            2 => UpdateVisible,
            3 => UpdateComing,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, $"Invalid state for {ObjType}.")
        };

        func();

        _animator.Advance();
    }

    public override void Draw()
    {
        var state = GetState();

        if (state == 2
            || (state > 0 && (_flashTimer & 1) == 0))
        {
            var pal = CalcPalette(Palette.Red);
            _animator.Draw(TileSheet.NpcsUnderworld, X, Y, pal);
        }
    }

    private int GetState() // JOE: TODO: What the heck is this state? Enumify this?
    {
        return _stateTimer >> 6;
    }

    private void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        _animator.Animation = Graphics.GetAnimation(TileSheet.NpcsUnderworld, BlueWizzrobeBase.WizzrobeAnimMap[dirOrd]);
    }

    private void UpdateHidden()
    {
        // Nothing to do
    }

    private void UpdateGoing()
    {
        if (_stateTimer == 0x7F)
        {
            _stateTimer = 0x4F;
        }

        _flashTimer++;

        if ((_flashTimer & 1) == 0)
        {
            CheckWizzrobeCollisions();
        }
    }

    private void UpdateVisible()
    {
        if (_stateTimer == 0xB0)
        {
            if (!World.HasItem(ItemSlot.Clock))
            {
                Game.Sound.PlayEffect(SoundEffect.MagicWave);
                Shoot(ObjType.MagicWave2, X, Y, Facing);
            }
            else
            {
                // JOE: NOTE: This branch should be logically unreachable.
                throw new Exception($"{nameof(RedWizzrobeActor)} UpdateVisible");
            }
        }

        CheckWizzrobeCollisions();
    }

    private int CheckWizzrobeTileCollision(int x, int y, Direction dir)
    {
        var ord = dir - 1;
        x += _allWizzrobeCollisionXOffsets[(int)ord];
        y += _allWizzrobeCollisionYOffsets[(int)ord];

        var collision = World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
        {
            return 0;
        }

        // This isn't quite the same as the original game, because the original contrasted
        // blocks and water together with everything else.
        return collision.TileBehavior.CollidesWall() ? 1 : 2;
    }

    private void UpdateComing()
    {
        if (_stateTimer == 0xFF)
        {
            var player = Game.Player;

            var r = Game.Random.Next(16);
            var dirOrd = r % 4;
            Facing = _wizzrobeDirs[dirOrd];

            X = (player.X + _wizzrobeXOffsets[r]) & 0xF0;
            Y = (player.Y + _wizzrobeYOffsets[r] + 3) & 0xF0 - 3;

            if (Y < 0x5D || Y >= 0xC4)
            {
                _stateTimer++;    // Try again
            }
            else
            {
                var collisionResult = CheckWizzrobeTileCollision(X, Y, Facing);
                if (collisionResult != 0)
                {
                    _stateTimer++;    // Try again
                }
            }

            if (_stateTimer != 0)
            {
                SetFacingAnimation();
            }
            return;
        }

        if (_stateTimer == 0x7F)
        {
            _stateTimer = 0x4F;
        }

        _flashTimer++;
        if ((_flashTimer & 1) == 0)
        {
            CheckWizzrobeCollisions();
        }
    }
}

internal sealed class LamnolaActor : MonsterActor
{
    private const int Count = 2;
    private const int Length = 4;

    private bool IsHead => _head == null;

    private readonly SpriteImage _image;
    private readonly LamnolaActor? _head;
    private readonly List<LamnolaActor> _bodyParts = new(); // Only available on heads.
    private readonly int _speed;

    private LamnolaActor(World world, ObjType type, LamnolaActor? head, int x, int y)
        : base(world, type, x, y)
    {
        _head = head;
        Decoration = 0;

        var animationId = IsHead ? AnimationId.UW_LanmolaHead : AnimationId.UW_LanmolaBody;
        _image = new SpriteImage(TileSheet.NpcsUnderworld, animationId);
        _speed = ObjType switch
        {
            ObjType.RedLamnola => 1,
            ObjType.BlueLamnola => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(ObjType), ObjType, $"Invalid {nameof(ObjType)} for {nameof(LamnolaActor)}.")
        };
    }

    public static LamnolaActor MakeSet(
        World world,
        ActorColor color,
        int x = 0x40,
        int y = 0x8D,
        int count = Count,
        int length = Length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count, nameof(count));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length, nameof(length));

        var objtype = color switch
        {
            ActorColor.Red => ObjType.RedLamnola,
            ActorColor.Blue => ObjType.BlueLamnola,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(LamnolaActor)}."),
        };

        LamnolaActor? first = null;

        for (var lamnolaCount = 0; lamnolaCount < count; lamnolaCount++)
        {
            var head = new LamnolaActor(world, objtype, null, x, y) { Facing = Direction.Up };
            first ??= head;
            world.AddObject(head);

            for (var i = 0; i < length; i++)
            {
                var body = new LamnolaActor(world, objtype, head, x, y);
                head._bodyParts.Add(body);
                world.AddObject(body);
            }

            // In the NES game logic, the head was the last part. They're killed in order, so it makes sense.
            head._bodyParts.Add(head);
        }

        world.RoomObjCount += count * length;

        return first ?? throw new Exception();
    }

    public override void Update()
    {
        if (Facing == Direction.None) return;

        if (!World.HasItem(ItemSlot.Clock))
        {
            MoveSimple(Facing, _speed);

            if (IsHead)
            {
                UpdateHead();
            }
        }

        CheckLamnolaCollisions();
    }

    public override void Draw()
    {
        var pal = ObjType == ObjType.RedLamnola ? Palette.Red : Palette.Blue;
        pal = CalcPalette(pal);
        var xOffset = (16 - _image.Animation.Width) / 2;
        _image.Draw(TileSheet.NpcsUnderworld, X + xOffset, Y, pal);
    }

    private void UpdateHead()
    {
        const uint adjustment = 3;

        if ((X & 7) != 0 || ((Y + adjustment) & 7) != 0)
        {
            return;
        }

        for (var i = 0; i < _bodyParts.Count - 1; i++)
        {
            var lamnola1 = _bodyParts[i];
            var lamnola2 = _bodyParts[i + 1];
            lamnola1.Facing = lamnola2.Facing;
        }

        if ((X & 0xF) != 0 || ((Y + adjustment) & 0xF) != 0)
        {
            return;
        }

        Turn();
    }

    private void Turn()
    {
        var oppositeDir = Facing.GetOppositeDirection();
        var dirMask = ~oppositeDir;
        var r = Game.Random.GetByte();
        Direction dir;

        if (r < 128)
        {
            var xDir = GetXDirToTruePlayer(X);
            var yDir = GetYDirToTruePlayer(Y);

            dir = ((xDir & dirMask) == 0 || (xDir & Facing) == 0) ? yDir : xDir;
        }
        else
        {
            dir = Facing;
            r = Game.Random.GetByte();

            if (r < 128)
            {
                while (true)
                {
                    dir = (Direction)((int)dir >> 1);
                    if (dir == 0)
                    {
                        dir = Direction.Up;
                    }

                    if ((dir & dirMask) != 0)
                    {
                        if (r >= 64)
                        {
                            break;
                        }
                        r = 64;
                    }
                }
            }
        }

        for (var i = 0; i < 4; ++i)
        {
            Facing = dir;

            if (CheckWorldMargin(Facing) != Direction.None
                && !World.CollidesWithTileMoving(X, Y, Facing, false))
            {
                break;
            }

            // If there were a room that had lamnolas, and they could get surrounded on 3 sides,
            // then this would get stuck in an infinite loop. But, the only room with that configuration
            // has those blocks blocked off with a push block, which can only be pushed after all foes
            // are killed.
            // JOE: Because the game is more flexible now, I turned this into a max of 4 iterations.

            do
            {
                dir = (Direction)((int)dir >> 1);
                if (dir == 0)
                {
                    dir = Direction.Up;
                }
            } while ((dir & dirMask) == 0);
        }
    }

    private void CheckLamnolaCollisions()
    {
        // The logic here can be a bit confusing, but whenever any piece of the
        // lamnola dies, we want to kill the lowest piece of tail still alive.
        var origFacing = Facing;
        CheckCollisions();
        Facing = origFacing;

        // Don't do anything else if this part isn't yet dead.
        if (Decoration == 0) return;

        // When a part dies, we want to kill the lowest part on the tail.
        var head = _head ?? this;
        var tailmost = head._bodyParts[0];

        // The last part, the head, is dead. Let's return because the job is done, all parts have died.
        if (tailmost.IsHead) return;

        // Bring this segment back to life...
        HP = 0x20;
        ShoveDirection = 0;
        ShoveDistance = 0;
        Decoration = 0;

        // ...then kill the tail-most piece by replacing with a dummy.
        var dummy = new DeadDummyActor(World, X, Y);
        World.AddOnlyObject(tailmost, dummy);
        head._bodyParts.RemoveAt(0);
    }
}

internal sealed class WallmasterActor : MonsterActor
{
    private static readonly ImmutableArray<byte> _startXs = [0x00, 0xF0];
    private static readonly ImmutableArray<byte> _startYs = [0x3D, 0xDD];

    private static readonly ImmutableArray<byte> _wallmasterDirs = [
        0x01, 0x01, 0x08, 0x08, 0x08, 0x02, 0x02, 0x02,
        0xC1, 0xC1, 0xC4, 0xC4, 0xC4, 0xC2, 0xC2, 0xC2,
        0x42, 0x42, 0x48, 0x48, 0x48, 0x41, 0x41, 0x41,
        0x82, 0x82, 0x84, 0x84, 0x84, 0x81, 0x81, 0x81,
        0xC4, 0xC4, 0xC2, 0xC2, 0xC2, 0xC8, 0xC8, 0xC8,
        0x84, 0x84, 0x81, 0x81, 0x81, 0x88, 0x88, 0x88,
        0x48, 0x48, 0x42, 0x42, 0x42, 0x44, 0x44, 0x44,
        0x08, 0x08, 0x01, 0x01, 0x01, 0x04, 0x04, 0x04
    ];

    private int _state;
    private int _dirIndex;
    private int _tilesCrossed;
    private bool _holdingPlayer;
    private readonly SpriteAnimator _animator;

    public WallmasterActor(World world, int x, int y)
        : base(world, ObjType.Wallmaster, x, y)
    {
        Decoration = 0;
        ObjTimer = 0;

        _animator = new SpriteAnimator(TileSheet.NpcsUnderworld, AnimationId.UW_Wallmaster)
        {
            DurationFrames = 16,
            Time = 0,
        };
    }

    private void CalcStartPosition(
        int playerOrthoCoord, int playerCoord, Direction dir,
        int baseDirIndex, int leastCoord, ref int orthoCoord, ref int coordIndex)
    {
        var player = Game.Player;
        var offset = 0x24;

        _dirIndex = baseDirIndex;
        if (player.Moving != 0)
        {
            offset = 0x32;
        }
        if (player.Facing == dir)
        {
            _dirIndex += 8;
            offset = -offset;
        }
        orthoCoord = playerOrthoCoord + offset;
        coordIndex = 0;
        if (playerCoord != leastCoord)
        {
            _dirIndex += 0x10;
            coordIndex++;
        }
    }

    public override void Update()
    {
        if (_state == 0)
        {
            UpdateIdle();
        }
        else
        {
            UpdateMoving();
        }
    }

    public override void Draw()
    {
        if (_state != 0)
        {
            var flags = _wallmasterDirs[_dirIndex] >> 6;
            var pal = CalcPalette(Palette.Blue);

            if (_holdingPlayer)
            {
                _animator.DrawFrame(TileSheet.NpcsUnderworld, X, Y, pal, 1, (DrawingFlags)flags);
            }
            else
            {
                _animator.Draw(TileSheet.NpcsUnderworld, X, Y, pal, (DrawingFlags)flags);
            }
        }
    }

    private void UpdateIdle()
    {
        if (World.GetObjectTimer(World.ObjectTimer.Monster1) != 0) return;

        var player = Game.Player;

        if (player.GetState() == PlayerState.Paused) return;

        var playerX = player.X;
        var playerY = player.Y;

        if (playerX < 0x29 || playerX >= 0xC8)
        {
            if (playerY < 0x6D || playerY >= 0xB5)
            {
                return;
            }
        }

        const int leastY = 0x5D;
        const int mostY = 0xBD;

        if (playerX == 0x20 || playerX == 0xD0)
        {
            var y = 0;
            var xIndex = 0;
            CalcStartPosition(playerY, playerX, Direction.Up, 0, 0x20, ref y, ref xIndex);
            X = _startXs[xIndex];
            Y = y;
        }
        else if (playerY == leastY || playerY == mostY)
        {
            var x = 0;
            var yIndex = 0;
            CalcStartPosition(playerX, playerY, Direction.Left, 0x20, leastY, ref x, ref yIndex);
            Y = _startYs[yIndex];
            X = x;
        }
        else
        {
            return;
        }

        _state = 1;
        _tilesCrossed = 0;
        World.SetObjectTimer(World.ObjectTimer.Monster1, 0x60);
        Facing = (Direction)(_wallmasterDirs[_dirIndex] & 0x0F);
        TileOffset = 0;
    }

    private void UpdateMoving()
    {
        var player = Game.Player;

        if (ShoveDirection != 0)
        {
            ObjShove();
        }
        else if (!IsStunned)
        {
            MoveDirection(0x18, Facing);

            if (TileOffset is 0x10 or -0x10)
            {
                TileOffset = 0;
                _dirIndex++;
                _tilesCrossed++;
                Facing = (Direction)(_wallmasterDirs[_dirIndex] & 0x0F);

                if (_tilesCrossed >= 7)
                {
                    _state = 0;
                    if (_holdingPlayer)
                    {
                        player.SetState(PlayerState.Idle);
                        World.GotoUnfurl();
                    }
                    return;
                }
            }
        }

        if (_holdingPlayer)
        {
            player.X = X;
            player.Y = Y;
            player.Animator.Advance();
        }
        else
        {
            if (CheckCollisions())
            {
                _holdingPlayer = true;
                player.SetState(PlayerState.Paused);
                player.ResetShove();
                Flags |= ActorFlags.DrawAbovePlayer;
            }
            _animator.Advance();
        }
    }
}

internal sealed class AquamentusActor : MonsterActor
{
    private const int AquamentusX = 0xB0;
    private const int AquamentusY = 0x80;

    private static readonly DebugLog _traceLog = new(nameof(AquamentusActor), DebugLogDestination.None);

    private static readonly ImmutableArray<byte> _palette = [0, 0x0A, 0x29, 0x30];

    private int _distance;
    private readonly SpriteAnimator _animator;
    private readonly SpriteImage _mouthImage;

    public override bool IsReoccuring => false;

    public AquamentusActor(World world, int x = AquamentusX, int y = AquamentusY)
        : base(world, ObjType.Aquamentus, x, y)
    {
        InvincibilityMask = 0xE2;

        Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance);

        _animator = new SpriteAnimator(TileSheet.Boss1257, AnimationId.B1_Aquamentus)
        {
            DurationFrames = 32,
            Time = 0
        };

        _mouthImage = new SpriteImage(TileSheet.Boss1257, AnimationId.B1_Aquamentus_Mouth_Closed);

        Graphics.SetPaletteIndexed(Palette.SeaPal, _palette);
        Graphics.UpdatePalettes();
    }

    public override void Update()
    {
        if (!World.HasItem(ItemSlot.Clock))
        {
            Move();
            TryShooting();
        }
        Animate();
        CheckCollisions();
        PlayBossHitSoundIfHit();
        PlayBossHitSoundIfDied();
        ShoveDirection = 0;
        ShoveDistance = 0;
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.SeaPal);
        _animator.Draw(TileSheet.Boss1257, X, Y, pal);
        _mouthImage.Draw(TileSheet.Boss1257, X, Y, pal);
    }

    private void Move()
    {
        if (_distance == 0)
        {
            var r = Game.Random.Next(16);
            _distance = r | 7;
            Facing = (Direction)((r & 1) + 1);
            return;
        }

        if ((Game.FrameCounter & 7) != 0) return;

        if (X < 0x88)
        {
            X = 0x88;
            Facing = Direction.Right;
            _distance = 7;
        }
        else if (X >= 0xC8)
        {
            X = 0xC7;
            Facing = Direction.Left;
            _distance = 7;
        }

        X += Facing == Direction.Right ? 1 : -1;

        _distance--;
    }

    private readonly List<FireballProjectile> _fireballs = new();

    private void TryShooting()
    {
        if (ObjTimer == 0)
        {
            var r = Game.Random.GetByte();
            ObjTimer = (byte)(r | 0x70);

            for (var i = 0; i < 3; i++)
            {
                ReadOnlySpan<int> yOffsets = [1, 0, -1];
                var shot = ShootFireball(ObjType.Fireball, X, Y, yOffsets[i]);
                if (shot != null) _fireballs.Add(shot);
            }

            return;
        }

        for (var i = _fireballs.Count - 1; i >= 0; i--)
        {
            var fireball = _fireballs[i];
            if (fireball.IsDeleted)
            {
                _fireballs.RemoveAt(i);
                continue;
            }

            if ((Game.FrameCounter & 1) == 1) continue;

            var offset = fireball.Offset ?? throw new Exception();
            _traceLog.Write($"Fireball {i} ({fireball.X:X2},{fireball.Y:X2}) += {offset}");

            fireball.Y += offset;
        }
    }

    private void Animate()
    {
        var mouthAnimIndex = ObjTimer < 0x20
            ? AnimationId.B1_Aquamentus_Mouth_Open
            : AnimationId.B1_Aquamentus_Mouth_Closed;

        _mouthImage.Animation = Graphics.GetAnimation(TileSheet.Boss1257, mouthAnimIndex);
        _animator.Advance();
    }
}

internal enum DodongoState
{
    Move,
    Bloated,
    Stunned,
}

internal sealed class DodongoActor : WandererWalkerActor
{
    private delegate void StateFunc();

    private static readonly ImmutableArray<AnimationId> _dodongoWalkAnimMap = [
        AnimationId.B1_Dodongo_R,
        AnimationId.B1_Dodongo_L,
        AnimationId.B1_Dodongo_D,
        AnimationId.B1_Dodongo_U
    ];

    private static readonly ImmutableArray<AnimationId> _dodongoBloatAnimMap = [
        AnimationId.B1_Dodongo_Bloated_R,
        AnimationId.B1_Dodongo_Bloated_L,
        AnimationId.B1_Dodongo_Bloated_D,
        AnimationId.B1_Dodongo_Bloated_U
    ];

    private static readonly WalkerSpec _dodongoWalkSpec = new(_dodongoWalkAnimMap, 20, Palette.Red, StandardSpeed);

    private static readonly ImmutableArray<byte> _palette = [0, 0x17, 0x27, 0x30];
    private static readonly ImmutableArray<int> _negBounds = [-0x10, 0, -8, 0, -8, -4, -4, -0x10, 0, 0];
    private static readonly ImmutableArray<int> _posBounds = [0, 0x10, 8, 0, 8, 4, 4, 0, 0, 0x10];

    private DodongoState _state;
    private int _bloatedSubstate;
    private int _bloatedTimer;
    private int _bombHits;

    private readonly ImmutableArray<StateFunc> _stateFuncs;
    private readonly ImmutableArray<StateFunc> _bloatedSubstateFuncs;

    public override bool IsReoccuring => false;

    private DodongoActor(World world, ObjType type, int x, int y)
        : base(world, type, WorldLevel.Underworld, _dodongoWalkSpec, 0x20, x, y)
    {
        _stateFuncs = [
            UpdateMoveState,
            UpdateBloatedState,
            UpdateStunnedState
        ];

        _bloatedSubstateFuncs = [
            UpdateBloatedWait,
            UpdateBloatedWait,
            UpdateBloatedWait,
            UpdateBloatedDie,
            UpdateBloatedEnd
        ];

        Game.Sound.PlayEffect(SoundEffect.BossRoar2, true, Sound.AmbientInstance);
        var r = Game.Random.Next(2);
        Facing = r == 1 ? Direction.Left : Direction.Right;

        Animator.DurationFrames = 16;
        Animator.Time = 0;
        SetWalkAnimation();

        Graphics.SetPaletteIndexed(Palette.SeaPal, _palette);
        Graphics.UpdatePalettes();
    }

    public static DodongoActor Make(World world, int count, int x, int y)
    {
        return count switch
        {
            1 => new DodongoActor(world, ObjType.OneDodongo, x, y),
            3 => new DodongoActor(world, ObjType.ThreeDodongos, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(count), count, $"Invalid count for {nameof(DodongoActor)}.")
        };
    }

    public override void Update()
    {
        UpdateState();
        CheckPlayerHit();
        CheckBombHit();
        Animate();
    }

    public override void Draw()
    {
        if (_state == DodongoState.Bloated && _bloatedSubstate is 2 or 3)
        {
            if ((Game.FrameCounter & 2) == 0) return;
        }

        Animator.Draw(TileSheet.Boss1257, X, Y, Palette.SeaPal);
    }

    private void SetWalkAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss1257, _dodongoWalkAnimMap[dirOrd]);
    }

    private void SetBloatAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss1257, _dodongoBloatAnimMap[dirOrd]);
    }

    private void UpdateState()
    {
        _stateFuncs[(int)_state]();
    }

    private void CheckPlayerHit()
    {
        CheckPlayerHitStdSize();
        if (InvincibilityTimer == 0)
        {
            if (Facing.IsVertical()) return;

            X += 0x10;
            CheckPlayerHitStdSize();
            X -= 0x10;

            if (InvincibilityTimer == 0) return;
        }

        UpdateBloatedDie();
        World.SetBombItemDrop();
    }

    private void CheckPlayerHitStdSize()
    {
        InvincibilityMask = 0xFF;
        CheckCollisions();

        if (_state == DodongoState.Stunned)
        {
            InvincibilityMask = 0xFE;
            CheckSword();
        }
    }

    private void CheckBombHit()
    {
        foreach (var bomb in World.GetObjects<BombActor>())
        {
            if (bomb.IsDeleted) continue;
            CheckBombHit(bomb);
        }
    }

    private void CheckBombHit(BombActor bomb)
    {
        if (_state != 0) return;

        if (bomb.IsDeleted) return;

        var bombState = bomb.BombState;
        var bombX = bomb.X + 8;
        var bombY = bomb.Y + 8;
        var thisX = X + 8;
        var thisY = Y + 8;

        if (Facing.IsHorizontal())
        {
            thisX += 8;
        }

        var xDist = thisX - bombX;
        var yDist = thisY - bombY;

        if (bombState == BombState.Ticking)
        {
            CheckTickingBombHit(bomb, xDist, yDist);
        }
        else    // Blasting or Fading
        {
            if (Overlaps(xDist, yDist, 0))
            {
                _state = DodongoState.Stunned;
            }
        }
    }

    private void CheckTickingBombHit(BombActor bomb, int xDist, int yDist)
    {
        if (!Overlaps(xDist, yDist, 1)) return;

        var index = (int)Facing >> 1;
        var dist = xDist;

        for (var i = 0; i < 2; i++)
        {
            if (dist < _negBounds[index] || dist >= _posBounds[index]) return;

            index += 5;
            dist = yDist;
        }

        _state++;
        _bloatedSubstate = 0;
        bomb.Delete();
    }

    private static bool Overlaps(int xDist, int yDist, int boundsIndex)
    {
        ReadOnlySpan<int> posBoundsOverlaps = [0xC, 0x11];
        ReadOnlySpan<int> negBoundsOverlaps = [-0xC, -0x10];
        ReadOnlySpan<int> distances = [xDist, yDist];

        for (var i = 1; i >= 0; i--)
        {
            if (distances[i] >= posBoundsOverlaps[boundsIndex]
                || distances[i] < negBoundsOverlaps[boundsIndex])
            {
                return false;
            }
        }

        return true;
    }

    private void Animate()
    {
        Animator.SetDuration(_state == 0 ? 16 : 64);

        if (_state == DodongoState.Move || _state == DodongoState.Stunned || _bloatedSubstate == 0)
        {
            SetWalkAnimation();
        }
        else
        {
            SetBloatAnimation();
        }

        Animator.Advance();
    }

    private void UpdateMoveState()
    {
        var origFacing = Facing;
        var xOffset = 0;

        if (Facing != Direction.Left)
        {
            X += 0x10;
            xOffset = 0x10;
        }

        Move();

        X -= xOffset;
        if (X < 0x20)
        {
            Facing = Direction.Right;
        }

        if (Facing != origFacing)
        {
            SetWalkAnimation();
        }
    }

    private void UpdateBloatedState()
    {
        _bloatedSubstateFuncs[_bloatedSubstate]();
    }

    private void UpdateStunnedState()
    {
        switch (StunTimer)
        {
            case 0:
                StunTimer = 0x20;
                break;

            case 1:
                _state = 0;
                _bloatedSubstate = 0;
                break;
        }
    }

    private void UpdateBloatedWait()
    {
        ReadOnlySpan<int> waitTimes = [0x20, 0x40, 0x40];

        switch (_bloatedTimer)
        {
            case 0:
                _bloatedTimer = waitTimes[_bloatedSubstate];
                if (_bloatedSubstate == 0)
                {
                    // JOE: I do not get why this exists?
                    // It feels wrong because CheckTickingBombHit already calls delete.
                    // var bomb = World.GetObject<BombActor>();
                    // bomb?.Delete();
                    _bombHits++;
                }
                break;

            case 1:
                _bloatedSubstate++;
                if (_bloatedSubstate >= 2 && _bombHits < 2)
                {
                    _bloatedSubstate = 4;
                }
                break;
        }

        _bloatedTimer--;
    }

    private void UpdateBloatedDie()
    {
        Game.Sound.PlayEffect(SoundEffect.MonsterDie);
        Game.Sound.PlayEffect(SoundEffect.BossHit);
        Game.Sound.StopEffect(StopEffect.AmbientInstance);
        Decoration = 0x10;
        _state = 0;
        _bloatedSubstate = 0;
    }

    private void UpdateBloatedEnd()
    {
        _state = 0;
        _bloatedSubstate = 0;
    }
}

internal sealed class ManhandlaParent
{
    public int PartsDied;
    public Direction FacingAtFrameBegin;
    public Direction BounceDir;
    public ManhandlaActor BodyCenter;
    public FireballProjectile? Fireball;

    public List<ManhandlaActor> Parts = new();
}

[DebuggerDisplay("Manhandla({IsBodyCenter})")]
internal sealed class ManhandlaActor : MonsterActor
{
    private static readonly ImmutableArray<AnimationId> _manhandlaAnimMap = [
        AnimationId.B2_Manhandla_Hand_U,
        AnimationId.B2_Manhandla_Hand_D,
        AnimationId.B2_Manhandla_Hand_L,
        AnimationId.B2_Manhandla_Hand_R,
        AnimationId.B2_Manhandla_Body
    ];

    private static readonly ImmutableArray<int> _xOffsets = [0, 0, -0x10, 0x10, 0];
    private static readonly ImmutableArray<int> _yOffsets = [-0x10, 0x10, 0, 0, 0];

    private bool IsBodyCenter => this == _parent.BodyCenter;

    private readonly SpriteAnimator _animator;
    private readonly ManhandlaParent _parent;

    private ushort _curSpeedFix;
    private ushort _speedAccum;
    private ushort _frameAccum;
    private int _frame;
    private int _oldFrame;

    public override bool IsReoccuring => false;

    private ManhandlaActor(World world, ManhandlaParent parent, int index, int x, int y, Direction facing)
        : base(world, ObjType.Manhandla, x, y)
    {
        _parent = parent;
        _curSpeedFix = 0x80;
        InvincibilityMask = 0xE2;
        Decoration = 0;
        Facing = facing;

        _animator = new SpriteAnimator(TileSheet.Boss3468, _manhandlaAnimMap[index])
        {
            DurationFrames = 1,
            Time = 0,
        };
    }

    public static ManhandlaActor Make(World world, int x, int y)
    {
        var dir = world.Game.Random.GetDirection8();

        world.Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);

        var parent = new ManhandlaParent();

        for (var i = 0; i < 5; i++)
        {
            // ORIGINAL: Get the base X and Y from the fifth spawn spot.
            var xPos = x + _xOffsets[i];
            var yPos = y + _yOffsets[i];

            var manhandla = new ManhandlaActor(world, parent, i, xPos, yPos, dir);
            parent.Parts.Add(manhandla);
            world.AddObject(manhandla);
        }

        parent.BodyCenter = parent.Parts[^1]; // The body center is the last one created.

        return parent.BodyCenter ?? throw new Exception();
    }

    private IEnumerable<ManhandlaActor> GetManhandlas(bool excludeCenter = false)
    {
        foreach (var part in _parent.Parts)
        {
            if (excludeCenter && part.IsBodyCenter) continue;

            if (!part.IsDeleted)
            {
                yield return part;
            }
        }
    }

    private void SetPartFacings(Direction dir)
    {
        foreach (var manhandla in GetManhandlas())
        {
            manhandla.Facing = dir;
        }
    }

    public override void Update()
    {
        if (IsBodyCenter)
        {
            UpdateBody();
            _parent.FacingAtFrameBegin = Facing;
        }

        Move();
        CheckManhandlaCollisions();

        if (Facing != _parent.FacingAtFrameBegin)
        {
            _parent.BounceDir = Facing;
        }

        _frame = (_frameAccum & 0x10) >> 4;

        if (!IsBodyCenter)
        {
            TryShooting();
        }
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.Blue);

        if (IsBodyCenter)
        {
            _animator.Draw(TileSheet.Boss3468, X, Y, pal);
        }
        else
        {
            _animator.DrawFrame(TileSheet.Boss3468, X, Y, pal, _frame);
        }
    }

    private void UpdateBody()
    {
        if (_parent.PartsDied != 0)
        {
            foreach (var manhandla in GetManhandlas())
            {
                manhandla._curSpeedFix += 0x80;
            }
            _parent.PartsDied = 0;
        }

        if (_parent.BounceDir != Direction.None)
        {
            SetPartFacings(_parent.BounceDir);
            _parent.BounceDir = Direction.None;
        }

        Debug.Assert(IsBodyCenter);

        if (ObjTimer == 0)
        {
            ObjTimer = 0x10;

            var r = Game.Random.Next(2);
            Facing = r == 0 ? TurnRandomly8(Facing) : TurnTowardsPlayer8(X, Y, Facing);

            // The original game set sBounceDir = Facing here, instead of to Direction.None above.
            SetPartFacings(Facing);
        }
    }

    private void Move()
    {
        _speedAccum &= 0xFF;
        _speedAccum += (ushort)(_curSpeedFix & 0xFFE0);
        var speed = _speedAccum >> 8;

        MoveSimple8(Facing, speed);

        _frameAccum += (ushort)(Game.Random.Next(4) + speed);

        if (CheckWorldMargin(Facing) == Direction.None)
        {
            Facing = Facing.GetOppositeDir8();
        }
    }

    private void TryShooting()
    {
        if (_frame != _oldFrame)
        {
            _oldFrame = _frame;

            if (_frame == 0
                && Game.Random.GetByte() >= 0xE0
                && (_parent.Fireball == null || _parent.Fireball.IsDeleted))
            {
                _parent.Fireball = ShootFireball(ObjType.Fireball2, X, Y);
            }
        }
    }

    private void CheckManhandlaCollisions()
    {
        var origFacing = Facing;
        var origStateTimer = ObjTimer;

        CheckCollisions();

        ObjTimer = origStateTimer;
        Facing = origFacing;

        if (IsBodyCenter)
        {
            InvincibilityTimer = 0;
        }

        PlayBossHitSoundIfHit();

        if (Decoration == 0) return;

        ShoveDirection = 0;
        ShoveDistance = 0;

        if (IsBodyCenter)
        {
            Decoration = 0;
            return;
        }

        var handCount = GetManhandlas(true).Count();

        var dummy = new DeadDummyActor(World, X, Y)
        {
            Decoration = Decoration
        };

        if (handCount > 1)
        {
            World.AddOnlyObject(this, dummy);
        }
        else
        {
            Game.Sound.PlayEffect(SoundEffect.BossHit);
            Game.Sound.StopEffect(StopEffect.AmbientInstance);
            World.AddOnlyObject(_parent.BodyCenter, dummy);
        }

        _parent.PartsDied++;
    }
}

internal abstract class DigdoggerActorBase : MonsterActor
{
    protected short CurSpeedFix = 0x003F;
    protected short SpeedAccum;
    protected short TargetSpeedFix = 0x0080;
    protected short AccelDir;
    protected bool IsChild;

    protected DigdoggerActorBase(World world, ObjType type, int x, int y)
        : base(world, type, x, y)
    {
        Facing = Game.Random.GetDirection8();
        IsChild = this is DigdoggerChildActor;

        Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);
    }

    protected void UpdateMove()
    {
        if (ObjTimer == 0)
        {
            ObjTimer = 0x10;

            var r = Game.Random.Next(2);
            Facing = r == 0 ? TurnRandomly8(Facing) : TurnTowardsPlayer8(X, Y, Facing);
        }

        Accelerate();
        Move();
    }

    private void Move()
    {
        SpeedAccum &= 0xFF;
        SpeedAccum += (short)(CurSpeedFix & 0xFFE0);
        var speed = SpeedAccum >> 8;

        MoveSimple8(Facing, speed);
    }

    private void Accelerate()
    {
        if (AccelDir == 0)
        {
            IncreaseSpeed();
        }
        else
        {
            DecreaseSpeed();
        }
    }

    private void IncreaseSpeed()
    {
        CurSpeedFix++;

        if (CurSpeedFix != TargetSpeedFix) return;

        AccelDir++;
        TargetSpeedFix = 0x0040;

        if (IsChild)
        {
            TargetSpeedFix += 0x0100;
        }
    }

    private void DecreaseSpeed()
    {
        CurSpeedFix--;

        if (CurSpeedFix != TargetSpeedFix) return;

        AccelDir--;
        TargetSpeedFix = 0x0080;

        if (IsChild)
        {
            TargetSpeedFix += 0x0100;
        }
    }
}

internal sealed class DigdoggerActor : DigdoggerActorBase
{
    private static readonly ImmutableArray<byte> _palette = [0, 0x17, 0x27, 0x30];
    private static readonly ImmutableArray<int> _offsetsX = [0, 0x10, 0, -0x10];
    private static readonly ImmutableArray<int> _offsetsY = [0, 0x10, -0x10, 0x10];

    private readonly SpriteAnimator _animator;
    private readonly SpriteAnimator _littleAnimator;

    private readonly int _childCount;
    private bool _updateBig;
    private int _recorderUsed = 0;

    public override bool IsReoccuring => false;

    private DigdoggerActor(World world, int x, int y, int childCount)
        : base(world, ObjType.Digdogger1, x, y)
    {
        _childCount = childCount;
        _updateBig = true;

        _animator = new SpriteAnimator(TileSheet.Boss1257, AnimationId.B1_Digdogger_Big)
        {
            DurationFrames = 12,
            Time = 0,
        };

        _littleAnimator = new SpriteAnimator(TileSheet.Boss1257, AnimationId.B1_Digdogger_Little)
        {
            DurationFrames = 12,
            Time = 0,
        };

        Graphics.SetPaletteIndexed(Palette.SeaPal, _palette);
        Graphics.UpdatePalettes();
    }

    public static DigdoggerActor Make(World world, int x, int y, int childCount)
    {
        return new DigdoggerActor(world, x, y, childCount);
    }

    public override bool NonTargetedAction(Interaction interaction)
    {
        _recorderUsed = 1;
        return base.NonTargetedAction(interaction);
    }

    public override void Update()
    {
        if (!IsStunned)
        {
            if (_recorderUsed == 0)
            {
                UpdateMove();
            }
            else
            {
                UpdateSplit();
            }
        }

        if (_updateBig)
        {
            var x = X;
            var y = Y;

            for (var i = 0; i < 4; i++)
            {
                X += _offsetsX[i];
                Y += _offsetsY[i];

                if (Direction.None == CheckWorldMargin(Facing))
                {
                    Facing = Facing.GetOppositeDir8();
                }

                CheckCollisions();
            }

            X = x;
            Y = y;

            _animator.Advance();
        }

        _littleAnimator.Advance();
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.SeaPal);

        if (_updateBig)
        {
            _animator.Draw(TileSheet.Boss1257, X, Y, pal);
        }
        _littleAnimator.Draw(TileSheet.Boss1257, X + 8, Y + 8, pal);
    }

    private void UpdateSplit()
    {
        if (_recorderUsed == 1)
        {
            ObjTimer = 0x40;
            _recorderUsed = 2;
            return;
        }

        _updateBig = false;

        if (ObjTimer != 0)
        {
            if ((ObjTimer & 7) == 0)
            {
                IsChild = !IsChild;
                if (!IsChild)
                {
                    _updateBig = true;
                }
            }
        }
        else
        {
            _recorderUsed = 1;
            World.RoomObjCount += _childCount;
            for (var i = 1; i <= _childCount; i++)
            {
                var child = DigdoggerChildActor.Make(World, X, Y);
                World.AddObject(child);
            }
            Game.Sound.PlayEffect(SoundEffect.BossHit);
            Game.Sound.StopEffect(StopEffect.AmbientInstance);
            Delete();
        }
    }
}

internal sealed class DigdoggerChildActor : DigdoggerActorBase
{
    private readonly SpriteAnimator _animator;

    private DigdoggerChildActor(World world, int x, int y)
        : base(world, ObjType.LittleDigdogger, x, y)
    {
        TargetSpeedFix = 0x0180;

        _animator = new SpriteAnimator(TileSheet.Boss1257, AnimationId.B1_Digdogger_Little)
        {
            DurationFrames = 12,
            Time = 0,
        };
    }

    public static DigdoggerChildActor Make(World world, int x, int y)
    {
        return new DigdoggerChildActor(world, x, y);
    }

    public override void Update()
    {
        if (!IsStunned)
        {
            UpdateMove();
        }

        if (Direction.None == CheckWorldMargin(Facing))
        {
            Facing = Facing.GetOppositeDir8();
        }

        CheckCollisions();
        _animator.Advance();
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.SeaPal);
        _animator.Draw(TileSheet.Boss1257, X, Y, pal);
    }
}

internal sealed class GohmaActor : MonsterActor
{
    private const int GohmaX = 0x80;
    private const int GohmaY = 0x70;

    private readonly SpriteAnimator _animator;
    private readonly SpriteAnimator _leftAnimator;
    private readonly SpriteAnimator _rightAnimator;

    private bool _changeFacing = true;
    private short _speedAccum;
    private int _distance;
    private int _sprints;
    private int _startOpenEyeTimer;
    private int _eyeOpenTimer;
    private int _eyeClosedTimer;
    private int _shootTimer = 1;
    private int _frame;
    private int _curCheckPart;

    public override bool IsReoccuring => false;

    private GohmaActor(World world, ObjType type, int x = GohmaX, int y = GohmaY)
        : base(world, type, x, y)
    {
        Decoration = 0;
        InvincibilityMask = 0xFB;

        _animator = new SpriteAnimator(TileSheet.Boss3468, AnimationId.B2_Gohma_Eye_All)
        {
            DurationFrames = 1,
            Time = 0,
        };

        _leftAnimator = new SpriteAnimator(TileSheet.Boss3468, AnimationId.B2_Gohma_Legs_L)
        {
            DurationFrames = 32,
            Time = 0,
        };

        _rightAnimator = new SpriteAnimator(TileSheet.Boss3468, AnimationId.B2_Gohma_Legs_R)
        {
            DurationFrames = 32,
            Time = 0,
        };
    }

    public static GohmaActor Make(World world, ActorColor color, int x = GohmaX, int y = GohmaY)
    {
        return color switch
        {
            ActorColor.Red => new GohmaActor(world, ObjType.RedGohma, x, y),
            ActorColor.Blue => new GohmaActor(world, ObjType.BlueGohma, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(GohmaActor)}."),
        };
    }

    public int GetCurrentCheckPart() => _curCheckPart;
    public int GetEyeFrame() => _frame;

    public override void Update()
    {
        if (_changeFacing)
        {
            ChangeFacing();
        }
        else
        {
            Move();
        }

        AnimateEye();
        TryShooting();
        CheckGohmaCollisions();

        _leftAnimator.Advance();
        _rightAnimator.Advance();
    }

    public override void Draw()
    {
        var pal = ObjType == ObjType.BlueGohma ? Palette.Blue : Palette.Red;
        pal = CalcPalette(pal);

        _animator.DrawFrame(TileSheet.Boss3468, X, Y, pal, _frame);
        _leftAnimator.Draw(TileSheet.Boss3468, X - 0x10, Y, pal);
        _rightAnimator.Draw(TileSheet.Boss3468, X + 0x10, Y, pal);
    }

    private void ChangeFacing()
    {
        var dir = 1;
        var r = Game.Random.GetByte();

        if (r < 0xB0)
        {
            dir <<= 1;
            if (r < 0x60)
            {
                dir <<= 1;
            }
        }

        Facing = (Direction)dir;
        _changeFacing = false;
    }

    private void Move()
    {
        _speedAccum &= 0xFF;
        _speedAccum += 0x80;

        if (_speedAccum >= 0x0100)
        {
            _distance++;
            MoveSimple(Facing, 1);

            if (_distance == 0x20)
            {
                _distance = 0;
                Facing = Facing.GetOppositeDirection();

                _sprints++;
                if ((_sprints & 1) == 0)
                {
                    _changeFacing = true;
                }
            }
        }
    }

    private void AnimateEye()
    {
        if (_startOpenEyeTimer == 0)
        {
            _eyeOpenTimer = 0x80;
            _startOpenEyeTimer = 0xC0 | Game.Random.GetByte();
        }

        if ((Game.FrameCounter & 1) == 1)
        {
            _startOpenEyeTimer--;
        }

        if (_eyeOpenTimer == 0)
        {
            _eyeClosedTimer++;
            if (_eyeClosedTimer == 8)
            {
                _eyeClosedTimer = 0;
                _frame = (_frame & 1) ^ 1;
            }
        }
        else
        {
            var t = _eyeOpenTimer;
            _eyeOpenTimer--;
            _frame = 2;
            if (t < 0x70 && t >= 0x10)
            {
                _frame++;
            }
        }
    }

    private void TryShooting()
    {
        _shootTimer--;
        if (_shootTimer == 0)
        {
            _shootTimer = 0x41;
            ShootFireball(ObjType.Fireball2, X, Y);
        }
    }

    private void CheckGohmaCollisions()
    {
        var origX = X;
        X -= 0x10;

        for (var i = 5; i > 0; i--)
        {
            _curCheckPart = i;
            // With other object types, we'd only call CheckCollisions. But, Gohma needs
            // to pass down the index of the current part.
            CheckCollisions();
            X += 8;
        }

        X = origX;
    }
}

internal enum ArmosState { Spawning, Entered }

internal sealed class ArmosActor : ChaseWalkerActor
{
    private static readonly ImmutableArray<AnimationId> _armosAnimMap = [
        AnimationId.OW_Armos_Right,
        AnimationId.OW_Armos_Left,
        AnimationId.OW_Armos_Down,
        AnimationId.OW_Armos_Up
    ];

    private static readonly WalkerSpec _armosSpec = new(_armosAnimMap, 12, Palette.Red, StandardSpeed);

    private ArmosState _state;

    public ArmosActor(World world, int x, int y)
        : base(world, ObjType.Armos, WorldLevel.Overworld, _armosSpec, x, y)
    {
        Decoration = 0;
        Facing = Direction.Down;
        SetFacingAnimation();

        // Set this to make up for the fact that this armos begins completely aligned with tile.
        TileOffset = 3;

        CurrentSpeed = Game.Random.GetRandom(0x20, 0x60);
    }

    public override void Update()
    {
        if (_state == ArmosState.Spawning)
        {
            // ORIGINAL: Can hit the player, but not get hit.
            if (ObjTimer == 0)
            {
                _state = ArmosState.Entered;
            }

            return;
        }

        UpdateNoAnimation();

        if (ShoveDirection == 0)
        {
            Animator.Advance();
            CheckCollisions();
            if (Decoration != 0)
            {
                var dummy = new DeadDummyActor(World, X, Y)
                {
                    Decoration = Decoration
                };
                World.AddOnlyObject(this, dummy);
            }
        }
    }

    public override void Draw()
    {
        if (_state == ArmosState.Spawning)
        {
            if ((ObjTimer & 1) == 1)
            {
                base.Draw();
            }
        }
        else
        {
            base.Draw();
        }
    }
}

internal enum ActorColor { Undefined, Blue, Red, Black }

internal sealed class GoriyaActor : ChaseWalkerActor, IThrower
{
    private static readonly ImmutableArray<AnimationId> _goriyaAnimMap = [
        AnimationId.UW_Goriya_Right,
        AnimationId.UW_Goriya_Left,
        AnimationId.UW_Goriya_Down,
        AnimationId.UW_Goriya_Up
    ];

    private static readonly WalkerSpec _blueGoriyaSpec = new(_goriyaAnimMap, 12, Palette.Blue, StandardSpeed);
    private static readonly WalkerSpec _redGoriyaSpec = new(_goriyaAnimMap, 12, Palette.Red, StandardSpeed);

    private Actor? _shotRef;

    private GoriyaActor(World world, ObjType type, WalkerSpec spec, int x, int y)
        : base(world, type, WorldLevel.Underworld, spec, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public static GoriyaActor Make(World world, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new GoriyaActor(world, ObjType.BlueGoriya, _blueGoriyaSpec, x, y),
            ActorColor.Red => new GoriyaActor(world, ObjType.RedGoriya, _redGoriyaSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(GoriyaActor)}."),
        };
    }

    public void Catch()
    {
        _shotRef = null;

        var r = Game.Random.GetByte();
        var t = r switch {
            < 0x30 => 0x30,
            < 0x70 => 0x50,
            _ => 0x70
        };

        ObjTimer = (byte)t;
    }

    public override void Update()
    {
        Animator.Advance();

        if (_shotRef != null) return;

        ObjMove(CurrentSpeed);
        if (ShoveDirection != 0) return;
        TargetPlayer();
        TryThrowingBoomerang();
    }

    private void TryThrowingBoomerang()
    {
        if (ObjTimer != 0) return;

        if (ObjType == ObjType.RedGoriya)
        {
            var r = Game.Random.GetByte();
            if (r != 0x23 && r != 0x77)
            {
                return;
            }
        }

        if (World.HasItem(ItemSlot.Clock)) return;

        var shot = Shoot(ObjType.Boomerang);
        if (shot != null)
        {
            WantToShoot = false;
            _shotRef = shot;
            ObjTimer = (byte)Game.Random.Next(0x40);
        }
    }
}

internal static class Statues
{
    private const int Patterns = 3;
    private const int MaxStatues = 4;

    private static readonly ImmutableArray<byte> _statueCounts = [4, 2, 2];
    private static readonly ImmutableArray<byte> _startTimers = [0x50, 0x80, 0xF0, 0x60];
    private static readonly ImmutableArray<byte> _patternOffsets = [0, 4, 6];
    private static readonly ImmutableArray<byte> _xs = [0x24, 0xC8, 0x24, 0xC8, 0x64, 0x88, 0x48, 0xA8];
    private static readonly ImmutableArray<byte> _ys = [0xC0, 0xBC, 0x64, 0x5C, 0x94, 0x8C, 0x82, 0x86];

    private static readonly byte[] _timers = new byte[MaxStatues];

    public static void Init()
    {
        Array.Fill(_timers, (byte)0);
    }

    public static void Update(Game game, int pattern)
    {
        if (pattern is < 0 or >= Patterns)
        {
            throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Pattern is out of range.");
        }

        // JOE: FIX URGENT: I'm not sure what this meant.
        // var slot = World.FindEmptyMonsterSlot();
        // if (slot < ObjectSlot.Monster1 + 5) return;

        var player = game.Player;
        var statueCount = _statueCounts[pattern];

        for (var i = 0; i < statueCount; i++)
        {
            var timer = _timers[i];
            _timers[i]--;

            if (timer != 0) continue;

            var r = game.Random.GetByte();
            if (r >= 0xF0) continue;

            var j = r & 3;
            _timers[i] = _startTimers[j];

            var offset = i + _patternOffsets[(int)pattern];
            var x = _xs[offset];
            var y = _ys[offset];

            if (Math.Abs(x - player.X) >= 0x18 || Math.Abs(y - player.Y) >= 0x18)
            {
                game.ShootFireball(ObjType.Fireball, x, y);
            }
        }
    }
}

internal abstract class StdChaseWalker : ChaseWalkerActor
{
    protected StdChaseWalker(World world, ObjType type, WorldLevel level, WalkerSpec spec, int x, int y)
        : base(world, type, level, spec, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }
}

internal sealed class LynelActor : StdChaseWalker
{
    private static readonly ImmutableArray<AnimationId> _lynelAnimMap = [
        AnimationId.OW_Lynel_Right,
        AnimationId.OW_Lynel_Left,
        AnimationId.OW_Lynel_Down,
        AnimationId.OW_Lynel_Up
    ];

    private static readonly WalkerSpec _blueLynelSpec = new(_lynelAnimMap, 12, Palette.Blue, StandardSpeed, ObjType.PlayerSwordShot);
    private static readonly WalkerSpec _redLynelSpec = new(_lynelAnimMap, 12, Palette.Red, StandardSpeed, ObjType.PlayerSwordShot);

    private LynelActor(World world, ObjType type, WalkerSpec spec, int x, int y)
        : base(world, type, WorldLevel.Overworld, spec, x, y)
    {
        if (type is not (ObjType.BlueLynel or ObjType.RedLynel))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(LynelActor)}.");
        }
    }

    public static LynelActor Make(World world, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new LynelActor(world, ObjType.BlueLynel, _blueLynelSpec, x, y),
            ActorColor.Red => new LynelActor(world, ObjType.RedLynel, _redLynelSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(LynelActor)}."),
        };
    }
}

internal sealed class MoblinActor : StdWanderer
{
    private static readonly ImmutableArray<AnimationId> _moblinAnimMap = [
        AnimationId.OW_Moblin_Right,
        AnimationId.OW_Moblin_Left,
        AnimationId.OW_Moblin_Down,
        AnimationId.OW_Moblin_Up
    ];

    private static readonly WalkerSpec _blueMoblinSpec = new(_moblinAnimMap, 12, (Palette)7, StandardSpeed, ObjType.Arrow);
    private static readonly WalkerSpec _redMoblinSpec = new(_moblinAnimMap, 12, Palette.Red, StandardSpeed, ObjType.Arrow);

    private MoblinActor(World world, ObjType type, WalkerSpec spec, int x, int y)
        : base(world, type, WorldLevel.Overworld, spec, 0xA0, x, y)
    {
        if (type is not (ObjType.BlueMoblin or ObjType.RedMoblin))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(MoblinActor)}.");
        }
    }

    public static MoblinActor Make(World world, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new MoblinActor(world, ObjType.BlueMoblin, _blueMoblinSpec, x, y),
            ActorColor.Red => new MoblinActor(world, ObjType.RedMoblin, _redMoblinSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(MoblinActor)}."),
        };
    }
}