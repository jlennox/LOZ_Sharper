using System.Collections.Immutable;
using System.Diagnostics;
using z1.IO;

namespace z1.Actors;

internal interface IThrower
{
    void Catch();
}

internal readonly record struct WalkerSpec(
    ImmutableArray<AnimationId>? AnimationMap,
    int AnimationTime,
    Palette Palette,
    int Speed = 0,
    ObjType ShotType = ObjType.None);

internal abstract class WalkerActor : Actor
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

    protected WalkerActor(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, x, y)
    {
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
        Animator.Draw(TileSheet.Npcs, X + offsetX, Y, pal);
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
            ? Graphics.GetAnimation(TileSheet.Npcs, AnimationMap.Value[dirOrd])
            : null;
    }

    protected void TryShooting()
    {
        if (!HasProjectile) return;

        if (ObjType.IsBlueWalker() || ShootTimer != 0 || Random.Shared.Next(0xFF) >= 0xF8)
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
        if (shot != ObjectSlot.NoneFound)
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

    protected ObjectSlot Shoot(ObjType shotType)
    {
        return WantToShoot
            ? Shoot(shotType, X, Y, Facing)
            : ObjectSlot.NoneFound;
    }

    public bool TryBigShove()
    {
        if (TileOffset == 0)
        {
            if (Game.World.CollidesWithTileMoving(X, Y, Facing, false)) return false;
        }

        if (CheckWorldMargin(Facing) == Direction.None) return false;

        MoveDirection(0xFF, Facing);

        if ((TileOffset & 0xF) == 0)
        {
            TileOffset &= 0xF;
        }

        return true;
    }
}

internal abstract class ChaseWalkerActor : WalkerActor
{
    protected ChaseWalkerActor(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, spec, x, y)
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

        if (CurrentSpeed == 0 || (TileOffset & 0xF) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0xF;

        // ORIGINAL: If player.state = $FF, then skip all this, go to the end (moving := Facing).
        //           But, I don't see how the player can get in that state.

        var observedPos = Game.World.GetObservedPlayerPos();
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
    protected DelayedWanderer(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y)
        : base(game, type, spec, turnRate, x, y)
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

    protected WandererWalkerActor(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y)
        : base(game, type, spec, x, y)
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

        if (CurrentSpeed == 0 || (TileOffset & 0xF) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0xF;

        var r = Random.Shared.GetByte();

        // ORIGINAL: If (r > turnRate) or (player.state = $FF), then ...
        //           But, I don't see how the player can get in that state.

        if (r > _turnRate)
        {
            TurnIfTime();
        }
        else
        {
            var playerPos = Game.World.GetObservedPlayerPos();

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
        _turnTimer = Random.Shared.GetByte();
        WantToShoot = true;
    }

    private void TurnY()
    {
        Facing = GetYDirToPlayer(Y);
        _turnTimer = Random.Shared.GetByte();
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

    private OctorokActor(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y)
        : base(game, type, spec, turnRate, x, y)
    {
    }

    public static OctorokActor Make(Game game, ActorColor color, bool isFast, int x, int y)
    {
        return (color, isFast) switch
        {
            (ActorColor.Blue, false) => new OctorokActor(game, ObjType.BlueSlowOctorock, _blueSlowOctorockSpec, 0xA0, x, y),
            (ActorColor.Blue, true) => new OctorokActor(game, ObjType.BlueFastOctorock, _blueFastOctorockSpec, 0xA0, x, y),
            (ActorColor.Red, false) => new OctorokActor(game, ObjType.RedSlowOctorock, _redSlowOctorockSpec, 0x70, x, y),
            (ActorColor.Red, true) => new OctorokActor(game, ObjType.RedFastOctorock, _redFastOctorockSpec, 0x70, x, y),
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
        new(TileSheet.Boss,           AnimationId.B3_Slash_U, 0),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      1),
        new(TileSheet.Boss,           AnimationId.B3_Slash_L, 1),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      3),
        new(TileSheet.Boss,           AnimationId.B3_Slash_U, 2),
        new(TileSheet.PlayerAndItems, AnimationId.Slash,      2),
        new(TileSheet.Boss,           AnimationId.B3_Slash_L, 0),
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

    public GanonActor(Game game, int x, int y)
        : base(game, ObjType.Ganon, x, y)
    {
        InvincibilityMask = 0xFA;

        _animator = new SpriteAnimator(TileSheet.Boss, AnimationId.B3_Ganon)
        {
            DurationFrames = 1,
            Time = 0,
        };

        _pileImage = new SpriteImage(TileSheet.Boss, AnimationId.B3_Pile);

        _cloudAnimator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Cloud)
        {
            DurationFrames = 1,
            Time = 0,
        };

        Game.Link.SetState(PlayerState.Paused);
        Game.Link.ObjTimer = 0x40;
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
            _animator.DrawFrame(TileSheet.Boss, X, Y, pal, _frame);
        }

        if (_visual.HasFlag(Visual.Pile))
        {
            _pileImage.Draw(TileSheet.Boss, X, Y, Palette.SeaPal);
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
        Game.World.LiftItem(ItemId.TriforcePiece, 0);

        if (Game.Link.ObjTimer != 0)
        {
            if (Game.Link.ObjTimer == 1)
            {
                Game.Sound.PlayEffect(SoundEffect.BossHit);
                Game.Sound.PlaySong(SongId.Ganon, SongStream.MainSong, false);
                //       The original game does it in the else part below, but only when [$51C] = $C0
                //       Which is in the first frame that the player's object timer is 0.
            }
        }
        else
        {
            Game.World.FadeIn();

            if (Game.World.GetFadeStep() == 0)
            {
                _state = GanonState.HoldLight;
                Game.Link.ObjTimer = 0xC0;
            }
            _visual = Visual.Ganon;
        }
    }

    private void UpdateHoldLight()
    {
        Game.World.LiftItem(ItemId.TriforcePiece, 0);

        if (Game.Link.ObjTimer == 0)
        {
            Game.Link.SetState(PlayerState.Idle);
            Game.World.LiftItem(ItemId.None);
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
            Game.World.AddUWRoomItem();
            var triforce = Game.World.GetObject(ObjectSlot.Item) ?? throw new Exception();
            triforce.X = X;
            triforce.Y = Y;
            Game.World.IncrementRoomKillCount();
            Game.Sound.PlayEffect(SoundEffect.RoomItem);
        }
    }

    private void CheckCollision()
    {
        var player = Game.Link;

        if (player.InvincibilityTimer == 0)
        {
            CheckPlayerCollisionDirect();
        }

        if (_lastHitTimer != 0)
        {
            var itemValue = Game.World.GetItem(ItemSlot.Arrow);
            if (itemValue == 2)
            {
                // The original checks the state of the arrow here and leaves if <> $10.
                // But, CheckArrow does a similar check (>= $20). As far as I can tell, both are equivalent.
                if (CheckArrow(ObjectSlot.Arrow))
                {
                    _dyingTimer = 1;
                    InvincibilityTimer = 0x28;
                    _cloudDist = 8;
                }
            }
            return;
        }

        if (ObjTimer != 0) return;

        CheckSword(ObjectSlot.PlayerSword);

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

internal sealed class ZeldaActor : Actor
{
    private const int ZeldaX = 0x78;
    private const int ZeldaLineX1 = 0x70;
    private const int ZeldaLineX2 = 0x80;
    private const int ZeldaY = 0x88;
    private const int ZeldaLineY = 0x95;

    private const int LinkX = 0x88;
    private const int LinkY = ZeldaY;

    public override bool IsReoccuring => false;

    private int _state;
    private readonly SpriteImage _image;

    private ZeldaActor(Game game, int x = ZeldaX, int y = ZeldaY)
        : base(game, ObjType.Zelda, x, y)
    {
        _image = new SpriteImage(TileSheet.Boss, AnimationId.B3_Zelda_Stand);
    }

    public static ZeldaActor Make(Game game)
    {
        ReadOnlySpan<byte> xs = [0x60, 0x70, 0x80, 0x90];
        ReadOnlySpan<byte> ys = [0xB5, 0x9D, 0x9D, 0xB5];

        for (var i = 0; i < xs.Length; i++)
        {
            var fire = new GuardFireActor(game, xs[i], ys[i]);
            game.World.SetObject(ObjectSlot.Monster1 + 1 + i, fire);
        }

        return new ZeldaActor(game);
    }

    public override void Update()
    {
        var player = Game.Link;

        if (_state == 0)
        {
            var playerX = player.X;
            var playerY = player.Y;

            if (playerX >= ZeldaLineX1
                && playerX <= ZeldaLineX2
                && playerY <= ZeldaLineY)
            {
                _state = 1;
                player.SetState(PlayerState.Paused);
                player.X = LinkX;
                player.Y = LinkY;
                player.Facing = Direction.Left;
                Game.Sound.PlaySong(SongId.Zelda, SongStream.MainSong, false);
                ObjTimer = 0x80;
            }
        }
        else
        {
            // ORIGINAL: Calls $F229. But, I don't see why we need to.
            if (ObjTimer == 0)
            {
                player.SetState(PlayerState.Idle);
                Game.World.WinGame();
            }
        }
    }

    public override void Draw()
    {
        _image.Draw(TileSheet.Boss, X, Y, Palette.Player);
    }
}

internal sealed class StandingFireActor : Actor
{
    public override bool IsReoccuring => false;
    private readonly SpriteAnimator _animator;

    public StandingFireActor(Game game, int x, int y)
        : base(game, ObjType.StandingFire, x, y)
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
        _animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.RedFgPalette);
    }
}

internal sealed class GuardFireActor : Actor
{
    public override bool IsReoccuring => false;
    private readonly SpriteAnimator _animator;

    public GuardFireActor(Game game, int x, int y)
        : base(game, ObjType.GuardFire, x, y)
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
            var dummy = new DeadDummyActor(Game, X, Y);
            Game.World.SetObject(Game.World.CurObjectSlot, dummy);
            dummy.Decoration = Decoration;
        }
    }

    public override void Draw()
    {
        _animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.RedFgPalette);
    }
}

internal sealed class RupeeStashActor : Actor
{
    private RupeeStashActor(Game game, int x, int y)
        : base(game, ObjType.RupieStash, x, y) { }

    public static RupeeStashActor Make(Game game)
    {
        ReadOnlySpan<Point> points = [
            new(0x78, 0x70), new(0x70, 0x80), new(0x80, 0x80), new(0x60, 0x90), new(0x70, 0x90), new(0x80, 0x90),
            new(0x90, 0x90), new(0x70, 0xA0), new(0x80, 0xA0), new(0x78, 0xB0)];

        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var rupee = new RupeeStashActor(game, point.X, point.Y);
            game.World.SetObject((ObjectSlot)i, rupee);
        }

        return game.World.GetObject<RupeeStashActor>(ObjectSlot.Monster1) ?? throw new Exception();
    }

    public override void Update()
    {
        var player = Game.Link;
        var distanceX = Math.Abs(player.X - X);
        var distanceY = Math.Abs(player.Y - Y);

        if (distanceX <= 8 && distanceY <= 8)
        {
            Game.World.PostRupeeWin(1);
            Game.World.IncrementRoomKillCount();
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
    public FairyActor(Game game, int x, int y)
        : base(game, ObjType.Item, _fairySpec, x, y)
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

        ReadOnlySpan<ObjectSlot> canPickupFairy = [ObjectSlot.Player, ObjectSlot.Boomerang];

        foreach (var slot in canPickupFairy)
        {
            var obj = Game.World.GetObject(slot);
            if (obj != null && !obj.IsDeleted && TouchesObject(obj))
            {
                Game.World.AddItem(ItemId.Fairy);
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

internal sealed class PondFairyActor : Actor
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

    public PondFairyActor(Game game, int x = PondFairyX, int y = PondFairyY)
        : base(game, ObjType.PondFairy, x, y)
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
        var player = Game.Link;
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

        var profile = Game.World.Profile;
        var maxHeartsValue = profile.GetMaxHeartsValue();

        Game.Sound.PlayEffect(SoundEffect.Character);

        if (profile.Hearts < maxHeartsValue)
        {
            Game.World.FillHearts(6);
        }
        else if (_heartState[7] != 0)
        {
            _pondFairyState = PondFairyState.Healed;
            var player = Game.Link;
            player.SetState(PlayerState.Idle);
            Game.World.SwordBlocked = false;
        }
    }

    public override void Draw()
    {
        const float radius = 0x36;
        const float angler = -Global.TWO_PI / 85.0f;

        var xOffset = (16 - _animator.Animation.Width) / 2;
        _animator.Draw(TileSheet.PlayerAndItems, PondFairyX + xOffset, PondFairyY, Palette.RedFgPalette);

        if (_pondFairyState != PondFairyState.Healing) return;

        var heart = new SpriteImage(TileSheet.PlayerAndItems, AnimationId.Heart);

        for (var i = 0; i < _heartState.Length; i++)
        {
            if (_heartState[i] == 0) continue;

            var angleIndex = _heartAngle[i] + 22;
            var angle = angler * angleIndex;
            var x = (int)(Math.Cos(angle) * radius + PondFairyRingCenterX);
            var y = (int)(Math.Sin(angle) * radius + PondFairyRingCenterY);

            heart.Draw(TileSheet.PlayerAndItems, x, y, Palette.RedFgPalette);
        }
    }
}

internal sealed class DeadDummyActor : Actor
{
    public DeadDummyActor(Game game, int x, int y)
        : base(game, ObjType.DeadDummy, x, y)
    {
        Decoration = 0;
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
    protected StdWanderer(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y)
        : base(game, type, spec, turnRate, x, y)
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

    public GhiniActor(Game game, int x, int y)
        : base(game, ObjType.Ghini, _ghiniSpec, 0xFF, x, y)
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
            foreach (var flying in Game.World.GetMonsters<FlyingGhiniActor>())
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

    public GibdoActor(Game game, int x, int y)
        : base(game, ObjType.Gibdo, _gibdoSpec, 0x80, x, y)
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

    private DarknutActor(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, spec, 0x80, x, y)
    {
        if (type is not (ObjType.RedDarknut or ObjType.BlueDarknut))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(DarknutActor)}.");
        }

        InvincibilityMask = 0xF6;
    }

    public static DarknutActor Make(Game game, ActorColor type, int x, int y)
    {
        return type switch
        {
            ActorColor.Red => new DarknutActor(game, ObjType.RedDarknut, _redDarknutSpec, x, y),
            ActorColor.Blue => new DarknutActor(game, ObjType.BlueDarknut, _blueDarknutSpec, x, y),
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

    public StalfosActor(Game game, int x, int y)
        : base(game, ObjType.Stalfos, _stalfosSpec, 0x80, x, y)
    {
    }

    public override void Update()
    {
        MoveIfNeeded();
        CheckCollisions();
        Animator.Advance();

        if (Game.World.Profile.Quest == 1)
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

    public GelActor(Game game, ObjType type, int x, int y, Direction dir, byte fraction)
        : base(game, type, _gelSpec, 0x20, x, y)
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
                var index = Random.Shared.Next(4);
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

    public ZolActor(Game game, int x, int y)
        : base(game, ObjType.Zol, _zolSpec, 0x20, x, y)
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
                var index = Random.Shared.Next(4);
                ObjTimer = _zolWaitTimes[index];
            }
        }

        // Above is almost the same as Gel.UpdateWander.

        CheckCollisions();

        if (Decoration == 0 && InvincibilityTimer != 0)
        {
            // On collision , go to state 2 or 1, depending on alignment.

            const uint alignedY = 0xD;

            var player = Game.Link;
            uint dirMask = 0;

            if ((Y & 0xF) == alignedY)
            {
                dirMask |= 3;
            }

            if ((X & 0xF) == 0)
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
        Game.World.RoomObjCount++;

        var orthoDirs = Facing.IsHorizontal() ? sVDirs : sHDirs;

        for (var i = 0; i < 2; i++)
        {
            var slot = Game.World.FindEmptyMonsterSlot();
            if (slot < 0) break;

            var gel = new GelActor(Game, ObjType.ChildGel, X, Y, orthoDirs[i], Fraction);
            Game.World.SetObject(slot, gel);
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

    public BubbleActor(Game game, ObjType type, int x, int y)
        : base(game, type, _bubbleSpec, 0x40, x, y)
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
                Game.World.SetStunTimer(ObjectSlot.NoSwordTimer, 0x10);
            }
            else
            {
                Game.World.SwordBlocked = ObjType == ObjType.Bubble3;
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

        Animator.Draw(TileSheet.Npcs, X, Y, (Palette)pal);
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

    public VireActor(Game game, int x, int y)
        : base(game, ObjType.Vire, _vireSpec, 0x80, x, y)
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
        Game.World.RoomObjCount++;

        for (var i = 0; i < 2; i++)
        {
            var slot = Game.World.FindEmptyMonsterSlot();
            if (slot < 0) break;

            var keese = KeeseActor.Make(Game, ActorColor.Red, X, Y);
            Game.World.SetObject(slot, keese);
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

    public LikeLikeActor(Game game, int x, int y)
        : base(game, ObjType.LikeLike, _likeLikeSpec, 0x80, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        var player = Game.Link;

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
                player.IsParalyzed = true;
                Animator.DurationFrames = Animator.Animation.Length * 4;
                Animator.Time = 0;
                Flags |= ActorFlags.DrawAbovePlayer;
            }

            if (Decoration != 0)
            {
                _log.Write("🚨🚨 LikeLike killed same frame as being held.");
                // player.IsParalyzed = false;
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
            Game.World.SetItem(ItemSlot.MagicShield, 0);
            _framesHeld = 0xC0;
        }

        CheckCollisions();

        if (Decoration != 0)
        {
            _log.Write("🚨🚨 LikeLike released.");

            player.IsParalyzed = false;
        }
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

    protected DigWanderer(Game game, ObjType type, ImmutableArray<WalkerSpec> specs, ImmutableArray<int> stateTimes, int x, int y)
        : base(game, type, specs[0], 0xA0, x, y)
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

    public ZoraActor(Game game, int x, int y)
        : base(game, ObjType.Zora, _zoraSpecs, _zoraStateTimes, x, y)
    {
        ObjTimer = (byte)StateTimes[0];
        Decoration = 0;
    }

    public override void Update()
    {
        if (Game.World.HasItem(ItemSlot.Clock)) return;

        UpdateDig();

        if (State == 0)
        {
            if (ObjTimer == 1)
            {
                var player = Game.Link;
                var cell = Game.World.GetRandomWaterTile();

                X = cell.Col * World.TileWidth;
                Y = cell.Row * World.TileHeight - 3;

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

    public BlueLeeverActor(Game game, int x, int y)
        : base(game, ObjType.BlueLeever, _blueLeeverSpecs, _blueLeeverStateTimes, x, y)
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

    public RedLeeverActor(Game game, int x, int y)
        : base(game, ObjType.RedLeever, x, y)
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

        Game.World.SetStunTimer(ObjectSlot.RedLeeverClassTimer, 5);
    }

    public override void Update()
    {
        var advanceState = false;

        if (_state == 0)
        {
            if (_count >= 2 || Game.World.GetStunTimer(ObjectSlot.RedLeeverClassTimer) != 0) return;
            if (!TargetPlayer()) return;
            Game.World.SetStunTimer(ObjectSlot.RedLeeverClassTimer, 2);
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
                if (Game.World.CollidesWithTileMoving(X, Y, Facing, false)
                    || CheckWorldMargin(Facing) == Direction.None)
                {
                    advanceState = true;
                }
                else
                {
                    MoveDirection(_spec.Speed, Facing);
                    if ((TileOffset & 0xF) == 0)
                    {
                        TileOffset &= 0xF;
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
            _animator.Draw(TileSheet.Npcs, X, Y, pal);
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
            ? Graphics.GetAnimation(TileSheet.Npcs, _spec.AnimationMap.Value[dirOrd])
            : null;
    }

    private bool TargetPlayer()
    {
        var player = Game.Link;
        var x = player.X;
        var y = player.Y;

        Facing = player.Facing;

        var r = Random.Shared.GetByte();
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
        if (Game.World.CollidesWithTileStill(x, y)) return false;

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

internal abstract class FlyingActor : Actor
{
    protected SpriteAnimator Animator;
    protected FlyingActorState State;
    protected int SprintsLeft;
    protected int CurSpeed;
    protected int AccelStep;
    protected Direction DeferredDir;
    protected int MoveCounter;

    protected readonly FlyerSpec Spec;

    protected FlyingActor(Game game, ObjType type, FlyerSpec spec, int x, int y)
        : base(game, type, x, y)
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

        if (this is MoldormActor)
        {
            var slot = Game.World.CurObjectSlot;
            if (slot is MoldormActor.HeadSlot1 or MoldormActor.HeadSlot2)
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
            ObjTimer = (byte)(Random.Shared.Next(64) + 64);
        }
    }

    private void UpdateFullSpeed()
    {
        UpdateFullSpeedImpl();
    }

    protected virtual void UpdateFullSpeedImpl()
    {
        var r = Random.Shared.GetByte();

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
    protected StdFlyerActor(Game game, ObjType type, FlyerSpec spec, int x, int y, Direction facing)
        : base(game, type, spec, x, y)
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

    private static readonly FlyerSpec _peahatSpec = new(_peahatAnimMap, TileSheet.Npcs, Palette.Red, 0xA0);

    public PeahatActor(Game game, int x, int y)
        : base(game, ObjType.Peahat, _peahatSpec, x, y, Direction.Up)
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

    private static readonly FlyerSpec _flyingGhiniSpec = new(_flyingGhiniAnimMap, TileSheet.Npcs, Palette.Blue, 0xA0);

    private FlyingGhiniState _ghiniState;

    public FlyingGhiniActor(Game game, int x, int y)
        : base(game, ObjType.FlyingGhini, _flyingGhiniSpec, x, y)
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

        if (!Game.World.HasItem(ItemSlot.Clock))
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
        var r = Random.Shared.GetByte();
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

    private static readonly FlyerSpec _blueKeeseSpec = new(_keeseAnimMap, TileSheet.Npcs, Palette.Blue, 0xC0);
    private static readonly FlyerSpec _redKeeseSpec = new(_keeseAnimMap, TileSheet.Npcs, Palette.Red, 0xC0);
    private static readonly FlyerSpec _blackKeeseSpec = new(_keeseAnimMap, TileSheet.Npcs, Palette.LevelFgPalette, 0xC0);

    private KeeseActor(Game game, ObjType type, FlyerSpec spec, int startSpeed, int x, int y)
        : base(game, type, spec, x, y)
    {
        CurSpeed = startSpeed;
        Facing = Random.Shared.GetDirection8();
    }

    public static KeeseActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch {
            ActorColor.Red => new KeeseActor(game, ObjType.RedKeese, _redKeeseSpec, 0x7F, x, y),
            ActorColor.Blue => new KeeseActor(game, ObjType.BlueKeese, _blueKeeseSpec, 0x1F, x, y),
            ActorColor.Black => new KeeseActor(game, ObjType.BlackKeese, _blackKeeseSpec, 0x7F, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(KeeseActor)}.")
        };
    }

    public override void Update()
    {
        if (!Game.World.HasItem(ItemSlot.Clock)
            && !Game.World.IsLiftingItem())
        {
            UpdateStateAndMove();
        }

        CheckCollisions();

        ShoveDirection = 0;
        ShoveDistance = 0;
    }

    protected override void UpdateFullSpeedImpl()
    {
        var r = Random.Shared.GetByte();
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
    public const ObjectSlot HeadSlot1 = ObjectSlot.Monster1 + 4;
    public const ObjectSlot HeadSlot2 = HeadSlot1 + 5;
    public const ObjectSlot TailSlot1 = ObjectSlot.Monster1;
    public const ObjectSlot TailSlot2 = TailSlot1 + 5;

    private static readonly ImmutableArray<AnimationId> _moldormAnimMap = [
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm
    ];

    private static readonly FlyerSpec _moldormSpec = new(_moldormAnimMap, TileSheet.Npcs, Palette.Red, 0x80);

    private Direction _oldFacing;

    public override bool IsReoccuring => false;
    private MoldormActor(Game game, int x, int y)
        : base(game, ObjType.Moldorm, _moldormSpec, x, y)
    {
        Decoration = 0;
        Facing = Direction.None;
        _oldFacing = Facing;

        CurSpeed = 0x80;

        GoToState(FlyingActorState.Chase, 1);
    }

    public static MoldormActor MakeSet(Game game)
    {
        for (var i = 0; i < 5 * 2; i++)
        {
            var moldorm = new MoldormActor(game, 0x80, 0x70);
            game.World.SetObject((ObjectSlot)i, moldorm);
        }

        var head1 = game.World.GetObject<MoldormActor>((ObjectSlot)4) ?? throw new Exception();
        var head2 = game.World.GetObject<MoldormActor>((ObjectSlot)9) ?? throw new Exception();

        head1.Facing = Random.Shared.GetDirection8();
        head1._oldFacing = head1.Facing;

        head2.Facing = Random.Shared.GetDirection8();
        head2._oldFacing = head2.Facing;

        game.World.RoomObjCount = 8;

        return game.World.GetObject<MoldormActor>(0) ?? throw new Exception();
    }

    public override void Update()
    {
        if (Facing == Direction.None) return;

        if (!Game.World.HasItem(ItemSlot.Clock))
        {
            UpdateStateAndMove();
        }

        CheckMoldormCollisions();
    }

    private void CheckMoldormCollisions()
    {
        // ORIGINAL: This is just like CheckLamnolaCollisions; but it saves stateTimer, and plays sounds.

        var origFacing = Facing;
        var origStateTimer = ObjTimer;

        CheckCollisions();

        ObjTimer = origStateTimer;
        Facing = origFacing;

        if (Decoration == 0) return;

        Game.Sound.PlayEffect(SoundEffect.BossHit);
        Game.Sound.StopEffect(StopEffect.AmbientInstance);

        var slot = Game.World.CurObjectSlot;

        slot = slot >= TailSlot2 ? TailSlot2 : TailSlot1;

        for (; ; slot++)
        {
            var obj = Game.World.GetObject(slot);
            if (obj != null && obj.ObjType == ObjType) break;
        }

        if (slot is HeadSlot1 or HeadSlot2) return;

        HP = 0x20;
        ShoveDirection = 0;
        ShoveDistance = 0;
        Decoration = 0;

        var dummy = new DeadDummyActor(Game, X, Y);
        Game.World.SetObject(slot, dummy);
    }

    protected override void UpdateTurnImpl()
    {
        var slot = Game.World.CurObjectSlot;
        if (slot is not (HeadSlot1 or HeadSlot2)) return;

        base.UpdateTurnImpl();
        UpdateSubstates();
    }

    protected override void UpdateChaseImpl()
    {
        var slot = Game.World.CurObjectSlot;
        if (slot != HeadSlot1 && slot != HeadSlot2) return;

        base.UpdateChaseImpl();
        UpdateSubstates();
    }

    private void UpdateSubstates()
    {
        if (ObjTimer == 0)
        {
            var r = Random.Shared.GetByte();
            GoToState(r < 0x40 ? FlyingActorState.Turn : FlyingActorState.Chase, 8);

            ObjTimer = 0x10;

            // This is the head, so all other parts are at lower indexes.
            var slot = Game.World.CurObjectSlot;
            var prevSlot = slot - 1;

            var obj = Game.World.GetObject(prevSlot);
            if (obj is MoldormActor && obj.Facing != Direction.None)
            {
                ShiftFacings();
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

        var slot = Game.World.CurObjectSlot - 4;

        for (var i = 0; i < 4; i++, slot++)
        {
            var curMoldorm = Game.World.GetObject<MoldormActor>(slot);
            var nextMoldorm = Game.World.GetObject<MoldormActor>(slot + 1);

            if (curMoldorm == null || nextMoldorm == null) continue;

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

    private const ObjectSlot FirstChildSlot = ObjectSlot.Monster1;
    private const ObjectSlot LastChildSlot = ObjectSlot.Monster9;

    private static readonly ImmutableArray<AnimationId> _patraAnimMap = [
        AnimationId.B3_Patra,
        AnimationId.B3_Patra,
        AnimationId.B3_Patra,
        AnimationId.B3_Patra
    ];

    private static readonly FlyerSpec _patraSpec = new(_patraAnimMap, TileSheet.Boss, Palette.Blue, 0x40);

    private int _xMove;
    private int _yMove;
    private int _maneuverState;
    private int _childStateTimer;

    public static int[] PatraAngle = new int[9];
    public static int[] PatraState = new int[9];

    public override bool IsReoccuring => false;

    private PatraActor(Game game, ObjType type, int x = PatraX, int y = PatraY)
        : base(game, type, _patraSpec, x, y)
    {
        InvincibilityMask = 0xFE;
        Facing = Direction.Up;
        CurSpeed = 0x1F;

        Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);

        Array.Fill(PatraAngle, 0);
        Array.Fill(PatraState, 0);
    }

    public static PatraActor MakePatra(Game game, PatraType patraType)
    {
        var type = patraType switch
        {
            PatraType.Circle => ObjType.Patra1,
            PatraType.Spin => ObjType.Patra2,
            _ => throw new ArgumentOutOfRangeException(nameof(patraType), patraType, "patraType unknown."),
        };

        var patra = new PatraActor(game, type);
        game.World.SetObject(ObjectSlot.Monster1, patra);

        for (var i = FirstChildSlot; i <= LastChildSlot; i++)
        {
            var child = PatraChildActor.Make(game, patraType);
            game.World.SetObject(i, child);
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

        var foundChild = Game.World.GetMonsters<PatraChildActor>(true).Any();

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
        var r = Random.Shared.GetByte();
        GoToState(r >= 0x40 ? FlyingActorState.Chase : FlyingActorState.Turn, 8);
    }
}

internal sealed class PatraChildActor : Actor
{
    private static readonly ImmutableArray<byte> _patraEntryAngles = [0x14, 0x10, 0xC, 0x8, 0x4, 0, 0x1C];
    private static readonly ImmutableArray<int> _shiftCounts = [6, 5, 6, 6];
    private static readonly ImmutableArray<byte> _sinCos = [0x00, 0x18, 0x30, 0x47, 0x5A, 0x6A, 0x76, 0x7D, 0x80, 0x7D, 0x76, 0x6A, 0x5A, 0x47, 0x30, 0x18];

    private int _x;
    private int _y;
    private int _angleAccum;
    private readonly SpriteAnimator _animator;

    public override bool IsReoccuring => false;

    private PatraChildActor(Game game, ObjType type, int x, int y)
        : base(game, type, x, y)
    {
        InvincibilityMask = 0xFE;
        Decoration = 0;

        ObjTimer = 0;

        _animator = new SpriteAnimator(TileSheet.Boss, AnimationId.B3_PatraChild)
        {
            DurationFrames = 4,
            Time = 0,
        };
    }

    public static PatraChildActor Make(Game game, PatraType patraType, int x = 0, int y = 0)
    {
        var objtype = patraType switch
        {
            PatraType.Circle => ObjType.PatraChild1,
            PatraType.Spin => ObjType.PatraChild2,
            _ => throw new ArgumentOutOfRangeException(nameof(patraType), patraType, "patraType unknown."),
        };

        return new PatraChildActor(game, objtype, x, y);
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
        var slot = Game.World.CurObjectSlot;

        if (PatraActor.PatraState[(int)slot] == 0)
        {
            UpdateStart();
            return;
        }

        UpdateTurn();
        _animator.Advance();

        if (PatraActor.PatraState[0] == 0) return;

        CheckCollisions();
        if (Decoration != 0)
        {
            var dummy = new DeadDummyActor(Game, X, Y);
            Game.World.SetObject(slot, dummy);
        }
    }

    public override void Draw()
    {
        var slot = Game.World.CurObjectSlot;

        if (PatraActor.PatraState[(int)slot] != 0)
        {
            var pal = CalcPalette(Palette.Red);
            _animator.Draw(TileSheet.Boss, X, Y, pal);
        }
    }

    private void UpdateStart()
    {
        var slot = Game.World.CurObjectSlot;
        if (slot != (ObjectSlot)1)
        {
            if (PatraActor.PatraState[1] == 0) return;
            var index = slot - 2;
            if (PatraActor.PatraAngle[1] != _patraEntryAngles[(int)index]) return;
        }

        var patra = Game.World.GetObject<PatraActor>(0) ?? throw new Exception();
        var distance = ObjType == ObjType.PatraChild1 ? 0x2C : 0x18;

        if (slot == (ObjectSlot)8)
        {
            PatraActor.PatraState[0] = 1;
        }
        PatraActor.PatraState[(int)slot] = 1;
        PatraActor.PatraAngle[(int)slot] = 0x18;

        _x = patra.X << 8;
        _y = (patra.Y - distance) << 8;

        X = _x >> 8;
        Y = _y >> 8;
    }

    private void UpdateTurn()
    {
        var slot = Game.World.CurObjectSlot;
        var patra = Game.World.GetObject<PatraActor>(0) ?? throw new Exception();

        _x += patra.GetXMove() << 8;
        _y += patra.GetYMove() << 8;

        var step = ObjType == ObjType.PatraChild1 ? 0x70 : 0x60;
        var angleFix = (short)((PatraActor.PatraAngle[(int)slot] << 8) | _angleAccum);
        angleFix -= (short)step;
        _angleAccum = angleFix & 0xFF;
        PatraActor.PatraAngle[(int)slot] = (angleFix >> 8) & 0x1F;

        int yShiftCount;
        int xShiftCount;
        var index = patra.GetManeuverState();

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

        index = PatraActor.PatraAngle[(int)slot] & 0xF;
        var cos = _sinCos[index];
        var n = ShiftMult(cos, turnSpeed, xShiftCount);

        if ((PatraActor.PatraAngle[(int)slot] & 0x18) < 0x10)
        {
            _x += n;
        }
        else
        {
            _x -= n;
        }

        index = (PatraActor.PatraAngle[(int)slot] + 8) & 0xF;
        var sin = _sinCos[index];
        n = ShiftMult(sin, turnSpeed, yShiftCount);

        if (((PatraActor.PatraAngle[(int)slot] - 8) & 0x18) < 0x10)
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

internal abstract class JumperActor : Actor
{
    public static readonly ImmutableArray<int> JumperStartDirs = [1, 2, 5, 0xA];
    private static readonly ImmutableArray<int> _targetYOffset = [0, 0, 0, 0, 0, 0x20, 0x20, 0, 0, -0x20, -0x20];

    private int _curSpeed;
    private int _accelStep;
    private int _state;
    private int _targetY;
    private int _reversesPending;

    private readonly SpriteAnimator _animator;
    private readonly JumperSpec _spec;

    protected JumperActor(Game game, ObjType type, JumperSpec spec, int x, int y)
        : base(game, type, x, y)
    {
        _spec = spec;

        _animator = new SpriteAnimator(TileSheet.Npcs, spec.AnimationMap[0])
        {
            Time = 0,
            DurationFrames = spec.AnimationTimer
        };

        Facing = (Direction)JumperStartDirs.GetRandom();
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
            _animator.DrawFrame(TileSheet.Npcs, X, Y, pal, _spec.JumpFrame);
        }
        else
        {
            _animator.Draw(TileSheet.Npcs, X, Y, pal);
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
        var r = Random.Shared.GetByte();
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

    public BoulderActor(Game game, int x, int y)
        : base(game, ObjType.Boulder, _boulderSpec, x, y)
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

    private TektiteActor(Game game, ObjType type, JumperSpec spec, int x, int y)
        : base(game, type, spec, x, y)
    {
        if (type is not (ObjType.BlueTektite or ObjType.RedTektite))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(TektiteActor)}.");
        }
    }

    public static TektiteActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new TektiteActor(game, ObjType.BlueTektite, _blueTektiteSpec, x, y),
            ActorColor.Red => new TektiteActor(game, ObjType.RedTektite, _redTektiteSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(TektiteActor)}.")
        };
    }
}

internal sealed class BouldersActor : Actor
{
    private const int MaxBoulders = 3;

    public static int Count;

    public BouldersActor(Game game, int x, int y)
        : base(game, ObjType.Boulders, x, y)
    {
        var facing = JumperActor.JumperStartDirs.GetRandom();
        ObjTimer = (byte)(facing * 4);
        Decoration = 0;
    }

    public override void Update()
    {
        if (ObjTimer != 0) return;

        if (Count < MaxBoulders)
        {
            var playerPos = Game.World.GetObservedPlayerPos();
            const int y = World.WorldLimitTop;
            var x = Random.Shared.GetByte();

            // Make sure the new boulder is in the same half of the screen.
            if (playerPos.X < World.WorldMidX)
            {
                x %= 0x80;
            }
            else
            {
                x |= 0x80;
            }

            var slot = Game.World.FindEmptyMonsterSlot();
            if (slot >= 0)
            {
                var obj = FromType(ObjType.Boulder, Game, x, y);
                Game.World.SetObject(slot, obj);

                ObjTimer = (byte)Random.Shared.Next(32);
            }

            return;
        }

        var r = Random.Shared.GetByte();
        ObjTimer = (byte)((ObjTimer + r) % 256);
    }

    public override void Draw()
    {
    }

    public static void ClearRoomData() => Count = 0;
}

internal sealed class TrapActor : Actor
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

    private TrapActor(Game game, int trapIndex, int x, int y)
        : base(game, ObjType.Trap, x, y)
    {
        _trapIndex = trapIndex;
        _image = new SpriteImage(TileSheet.Npcs, AnimationId.UW_Trap);
    }

    public static TrapActor MakeSet(Game game, int count)
    {
        Debug.Assert(count is >= 1 and <= 6);
        count = Math.Clamp(count, 1, 6);

        var slot = ObjectSlot.Monster1;

        for (var i = 0; i < count; i++, slot++)
        {
            var obj = new TrapActor(game, i, _trapPos[i].X, _trapPos[i].Y);
            game.World.SetObject(slot, obj);
        }

        return game.World.GetObject<TrapActor>(ObjectSlot.Monster1) ?? throw new Exception(); ;
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
        var player = Game.Link;
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

        if ((TileOffset & 0xF) == 0)
        {
            TileOffset &= 0xF;
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
        _image.Draw(TileSheet.Npcs, X, Y, Palette.Blue);
    }
}

internal sealed class RopeActor : Actor
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

    public RopeActor(Game game, int x, int y)
        : base(game, ObjType.Rope, x, y)
    {
        _animator = new SpriteAnimator
        {
            Time = 0,
            DurationFrames = 20
        };

        InitCommonFacing();
        SetFacingAnimation();

        var profile = Game.World.Profile;

        HP = (byte)(profile.Quest == 0 ? 0x10 : 0x40);
    }

    public override void Update()
    {
        var origFacing = Facing;

        MovingDirection = Facing;

        if (!IsStunned)
        {
            ObjMove(_speed);

            if ((TileOffset & 0xF) == 0)
            {
                TileOffset &= 0xF;
            }

            if (_speed != RopeFastSpeed && ObjTimer == 0)
            {
                ObjTimer = (byte)Random.Shared.Next(0x40);
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

        var player = Game.Link;

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
        var profile = Game.World.Profile;
        var pal = profile.Quest == 0
            ? CalcPalette(Palette.Red)
            : Palette.Player + (Game.FrameCounter & 3);

        _animator.Draw(TileSheet.Npcs, X, Y, pal);
    }

    private void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        _animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, _ropeAnimMap[dirOrd]);
    }
}

internal sealed class PolsVoiceActor : Actor
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

    public PolsVoiceActor(Game game, int x, int y)
        : base(game, ObjType.PolsVoice, x, y)
    {
        InitCommonFacing();

        _animator = new SpriteAnimator(TileSheet.Npcs, AnimationId.UW_PolsVoice)
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
        _animator.Draw(TileSheet.Npcs, X, Y, pal);
    }

    private void Move()
    {
        UpdateX();
        if (!UpdateY()) return;

        var x = X;
        var y = Y;

        var collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
        {
            x += 0xE;
            y += 6;
            collision = Game.World.CollidesWithTileStill(x, y);
            if (!collision.Collides) return;
        }

        if (World.CollidesWall(collision.TileBehavior))
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
            var r = Random.Shared.GetByte();
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

    private static readonly ImmutableArray<int> _allWizzrobeCollisionXOffsets = [0xF, 0, 0, 4, 8, 0, 0, 4, 8, 0];
    private static readonly ImmutableArray<int> _allWizzrobeCollisionYOffsets = [4, 4, 0, 8, 8, 8, 0, -8, 0, 0];

    private byte _stateTimer;
    private byte _flashTimer;

    private readonly SpriteAnimator _animator;

    public RedWizzrobeActor(Game game, int x, int y)
        : base(game, ObjType.RedWizzrobe, x, y)
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
        if (Game.World.HasItem(ItemSlot.Clock))
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
            _animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    private int GetState() // JOE: TODO: What the heck is this state? Enumify this?
    {
        return _stateTimer >> 6;
    }

    private void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        _animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, BlueWizzrobeBase.WizzrobeAnimMap[dirOrd]);
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
            if (!Game.World.HasItem(ItemSlot.Clock))
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

        var collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
        {
            return 0;
        }

        // This isn't quite the same as the original game, because the original contrasted
        // blocks and water together with everything else.
        return World.CollidesWall(collision.TileBehavior) ? 1 : 2;
    }

    private void UpdateComing()
    {
        if (_stateTimer == 0xFF)
        {
            var player = Game.Link;

            var r = Random.Shared.Next(16);
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

internal sealed class LamnolaActor : Actor
{
    private const ObjectSlot TailSlot1 = ObjectSlot.Monster1;
    private const ObjectSlot HeadSlot1 = TailSlot1 + 4;
    private const ObjectSlot TailSlot2 = HeadSlot1 + 1;
    private const ObjectSlot HeadSlot2 = TailSlot2 + 4;

    private readonly SpriteImage _image;

    private LamnolaActor(Game game, ObjType type, bool isHead, int x, int y)
        : base(game, type, x, y)
    {
        Decoration = 0;

        var animationId = isHead ? AnimationId.UW_LanmolaHead : AnimationId.UW_LanmolaBody;
        _image = new SpriteImage(TileSheet.Npcs, animationId);
    }

    public static LamnolaActor MakeSet(Game game, ActorColor color)
    {
        var objtype = color switch
        {
            ActorColor.Red => ObjType.RedLamnola,
            ActorColor.Blue => ObjType.BlueLamnola,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(LamnolaActor)}."),
        };

        const int y = 0x8D;

        for (var i = TailSlot1; i <= HeadSlot2; i++)
        {
            var isHead = i is HeadSlot1 or HeadSlot2;
            var lamnola = new LamnolaActor(game, objtype, isHead, 0x40, y);
            game.World.SetObject(i, lamnola);
        }

        var head1 = game.World.GetObject<LamnolaActor>(HeadSlot1) ?? throw new Exception();
        var head2 = game.World.GetObject<LamnolaActor>(HeadSlot2) ?? throw new Exception();

        head1.Facing = Direction.Up;
        head2.Facing = Direction.Up;

        game.World.RoomObjCount = 8;

        return game.World.GetObject<LamnolaActor>(0) ?? throw new Exception();
    }

    public override void Update()
    {
        if (Facing == Direction.None) return;

        if (!Game.World.HasItem(ItemSlot.Clock))
        {
            var speed = ObjType - ObjType.RedLamnola + 1;
            var slot = Game.World.CurObjectSlot;

            MoveSimple(Facing, speed);

            if (slot is HeadSlot1 or HeadSlot2)
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
        _image.Draw(TileSheet.Npcs, X + xOffset, Y, pal);
    }

    private void UpdateHead()
    {
        const uint adjustment = 3;

        if ((X & 7) != 0 || ((Y + adjustment) & 7) != 0)
        {
            return;
        }

        var slot = Game.World.CurObjectSlot;

        for (var i = slot - 4; i < slot; i++)
        {
            var lamnola1 = Game.World.GetObject<LamnolaActor>(i);
            var lamnola2 = Game.World.GetObject<LamnolaActor>(i + 1);

            if (lamnola1 != null && lamnola2 != null)
            {
                lamnola1.Facing = lamnola2.Facing;
            }
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
        var r = Random.Shared.GetByte();
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
            r = Random.Shared.GetByte();

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

        while (true)
        {
            Facing = dir;

            if (Direction.None != CheckWorldMargin(Facing)
                && !Game.World.CollidesWithTileMoving(X, Y, Facing, false))
            {
                break;
            }

            // If there were a room that had lamnolas, and they could get surrounded on 3 sides,
            // then this would get stuck in an infinite loop. But, the only room with that configuration
            // has those blocks blocked off with a push block, which can only be pushed after all foes
            // are killed.

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
        var origFacing = Facing;
        CheckCollisions();
        Facing = origFacing;

        if (Decoration == 0) return;

        var slot = Game.World.CurObjectSlot;
        slot = slot >= TailSlot2 ? TailSlot2 : TailSlot1;

        for (; ; slot++)
        {
            var obj = Game.World.GetObject(slot);
            if (obj != null && obj.ObjType == ObjType)
            {
                break;
            }
        }

        if (slot is HeadSlot1 or HeadSlot2) return;

        HP = 0x20;
        ShoveDirection = 0;
        ShoveDistance = 0;
        Decoration = 0;

        var dummy = new DeadDummyActor(Game, X, Y);
        Game.World.SetObject(slot, dummy);
    }
}

internal sealed class WallmasterActor : Actor
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

    public WallmasterActor(Game game, int x, int y)
        : base(game, ObjType.Wallmaster, x, y)
    {
        Decoration = 0;
        ObjTimer = 0;

        _animator = new SpriteAnimator(TileSheet.Npcs, AnimationId.UW_Wallmaster)
        {
            DurationFrames = 16,
            Time = 0,
        };
    }

    private void CalcStartPosition(
        int playerOrthoCoord, int playerCoord, Direction dir,
        int baseDirIndex, int leastCoord, ref int orthoCoord, ref int coordIndex)
    {
        var player = Game.Link;
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
                _animator.DrawFrame(TileSheet.Npcs, X, Y, pal, 1, (DrawingFlags)flags);
            }
            else
            {
                _animator.Draw(TileSheet.Npcs, X, Y, pal, (DrawingFlags)flags);
            }
        }
    }

    private void UpdateIdle()
    {
        if (Game.World.GetObjectTimer(ObjectSlot.Monster1) != 0) return;

        var player = Game.Link;

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
        Game.World.SetObjectTimer(ObjectSlot.Monster1, 0x60);
        Facing = (Direction)(_wallmasterDirs[_dirIndex] & 0xF);
        TileOffset = 0;
    }

    private void UpdateMoving()
    {
        var player = Game.Link;

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
                Facing = (Direction)(_wallmasterDirs[_dirIndex] & 0xF);

                if (_tilesCrossed >= 7)
                {
                    _state = 0;
                    if (_holdingPlayer)
                    {
                        player.SetState(PlayerState.Idle);
                        Game.World.UnfurlLevel();
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

internal sealed class AquamentusActor : Actor
{
    private const int AquamentusX = 0xB0;
    private const int AquamentusY = 0x80;

    private static readonly DebugLog _traceLog = new(nameof(AquamentusActor), DebugLogDestination.None);

    private static readonly ImmutableArray<byte> _palette = [0, 0x0A, 0x29, 0x30];

    private int _distance;
    private readonly SpriteAnimator _animator;
    private readonly SpriteImage _mouthImage;
    private readonly int[] _fireballOffsets = new int[(int)ObjectSlot.MaxMonsters];

    public override bool IsReoccuring => false;

    public AquamentusActor(Game game, int x = AquamentusX, int y = AquamentusY)
        : base(game, ObjType.Aquamentus, x, y)
    {
        InvincibilityMask = 0xE2;

        Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance);

        _animator = new SpriteAnimator(TileSheet.Boss, AnimationId.B1_Aquamentus)
        {
            DurationFrames = 32,
            Time = 0
        };

        _mouthImage = new SpriteImage(TileSheet.Boss, AnimationId.B1_Aquamentus_Mouth_Closed);

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, _palette);
        Graphics.UpdatePalettes();
    }

    public override void Update()
    {
        if (!Game.World.HasItem(ItemSlot.Clock))
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
        _animator.Draw(TileSheet.Boss, X, Y, pal);
        _mouthImage.Draw(TileSheet.Boss, X, Y, pal);
    }

    private void Move()
    {
        if (_distance == 0)
        {
            var r = Random.Shared.Next(16);
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

    private void TryShooting()
    {
        if (ObjTimer == 0)
        {
            var r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);

            for (var i = 0; i < 3; i++)
            {
                var slot = ShootFireball(ObjType.Fireball, X, Y);
                if (slot < 0) break;
                ReadOnlySpan<int> yOffsets = [1, 0, -1];
                _fireballOffsets[(int)slot] = yOffsets[i];
            }

            return;
        }

        for (var i = ObjectSlot.Monster1; i < ObjectSlot.MaxMonsters; i++)
        {
            var fireball = Game.World.GetObject<FireballProjectile>(i);

            if (fireball == null) continue;
            if ((Game.FrameCounter & 1) == 1) continue;

            var offset = _fireballOffsets[(int)i];
            _traceLog.Write($"Fireball {i} ({fireball.X:X2},{fireball.Y:X2}) += {offset}");

            fireball.Y += offset;
        }
    }

    private void Animate()
    {
        var mouthAnimIndex = ObjTimer < 0x20
            ? AnimationId.B1_Aquamentus_Mouth_Open
            : AnimationId.B1_Aquamentus_Mouth_Closed;

        _mouthImage.Animation = Graphics.GetAnimation(TileSheet.Boss, mouthAnimIndex);
        _animator.Advance();
    }
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

    private int _state;
    private int _bloatedSubstate;
    private int _bloatedTimer;
    private int _bombHits;

    private readonly ImmutableArray<StateFunc> _stateFuncs;
    private readonly ImmutableArray<StateFunc> _bloatedSubstateFuncs;

    public override bool IsReoccuring => false;

    private DodongoActor(Game game, ObjType type, int x, int y)
        : base(game, type, _dodongoWalkSpec, 0x20, x, y)
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
        var r = Random.Shared.Next(2);
        Facing = r == 1 ? Direction.Left : Direction.Right;

        Animator.DurationFrames = 16;
        Animator.Time = 0;
        SetWalkAnimation();

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, _palette);
        Graphics.UpdatePalettes();
    }

    public static DodongoActor Make(Game game, int count, int x, int y)
    {
        return count switch
        {
            1 => new DodongoActor(game, ObjType.OneDodongo, x, y),
            3 => new DodongoActor(game, ObjType.ThreeDodongos, x, y),
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
        if (_state == 1 && _bloatedSubstate is 2 or 3)
        {
            if ((Game.FrameCounter & 2) == 0) return;
        }

        Animator.Draw(TileSheet.Boss, X, Y, Palette.LevelFgPalette);
    }

    private void SetWalkAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss, _dodongoWalkAnimMap[dirOrd]);
    }

    private void SetBloatAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss, _dodongoBloatAnimMap[dirOrd]);
    }

    private void UpdateState()
    {
        _stateFuncs[_state]();
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
        Game.World.SetBombItemDrop();
    }

    private void CheckPlayerHitStdSize()
    {
        InvincibilityMask = 0xFF;
        CheckCollisions();

        if (_state == 2)
        {
            InvincibilityMask = 0xFE;
            CheckSword(ObjectSlot.PlayerSword);
        }
    }

    private void CheckBombHit()
    {
        if (_state != 0) return;

        var bomb = Game.World.GetObject<BombActor>(ObjectSlot.FirstBomb);
        if (bomb == null || bomb.IsDeleted) return;

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
                _state = 2;
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

        if (_state == 0 || _state == 2 || _bloatedSubstate == 0)
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
                    var bomb = Game.World.GetObject<BombActor>(ObjectSlot.FirstBomb);
                    if (bomb != null)
                    {
                        bomb.Delete();
                    }
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

    public List<ManhandlaActor> Parts = new();
}

internal sealed class ManhandlaActor : Actor
{
    private const ObjectSlot ManhandlaCenterBodySlot = (ObjectSlot)4;

    private static readonly ImmutableArray<AnimationId> _manhandlaAnimMap = [
        AnimationId.B2_Manhandla_Hand_U,
        AnimationId.B2_Manhandla_Hand_D,
        AnimationId.B2_Manhandla_Hand_L,
        AnimationId.B2_Manhandla_Hand_R,
        AnimationId.B2_Manhandla_Body
    ];

    private static readonly ImmutableArray<int> _xOffsets = [0, 0, -0x10, 0x10, 0];
    private static readonly ImmutableArray<int> _yOffsets = [-0x10, 0x10, 0, 0, 0];

    private readonly SpriteAnimator _animator;
    private readonly ManhandlaParent _parent;

    private ushort _curSpeedFix;
    private ushort _speedAccum;
    private ushort _frameAccum;
    private int _frame;
    private int _oldFrame;

    public override bool IsReoccuring => false;

    private ManhandlaActor(Game game, ManhandlaParent parent, int index, int x, int y, Direction facing)
        : base(game, ObjType.Manhandla, x, y)
    {
        _parent = parent;
        _curSpeedFix = 0x80;
        InvincibilityMask = 0xE2;
        Decoration = 0;
        Facing = facing;

        _animator = new SpriteAnimator(TileSheet.Boss, _manhandlaAnimMap[index])
        {
            DurationFrames = 1,
            Time = 0,
        };
    }

    public static ManhandlaActor Make(Game game, int x, int y)
    {
        var dir = Random.Shared.GetDirection8();

        game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);

        var parent = new ManhandlaParent();

        for (var i = 0; i < 5; i++)
        {
            // ORIGINAL: Get the base X and Y from the fifth spawn spot.
            var xPos = x + _xOffsets[i];
            var yPos = y + _yOffsets[i];

            var manhandla = new ManhandlaActor(game, parent, i, xPos, yPos, dir);
            parent.Parts.Add(manhandla);
            game.World.SetObject((ObjectSlot)i, manhandla);
        }

        return game.World.GetObject<ManhandlaActor>(0) ?? throw new Exception();
    }

    private IEnumerable<ManhandlaActor> GetManhandlas(bool excludeCenter = false)
    {
        // JOE: TODO: Move this over to parent.Parts so that we can avoid filling the monster slots.
        for (var i = ObjectSlot.Monster1; i <= ManhandlaCenterBodySlot; i++)
        {
            if (excludeCenter && i == ManhandlaCenterBodySlot) continue;

            var manhandla = Game.World.GetObject<ManhandlaActor>(i);
            if (manhandla != null)
            {
                yield return manhandla;
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
        var slot = Game.World.CurObjectSlot;
        if (slot == ManhandlaCenterBodySlot)
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

        if (slot != ManhandlaCenterBodySlot)
        {
            TryShooting();
        }
    }

    public override void Draw()
    {
        var slot = Game.World.CurObjectSlot;
        var pal = CalcPalette(Palette.Blue);

        if (slot == ManhandlaCenterBodySlot)
        {
            _animator.Draw(TileSheet.Boss, X, Y, pal);
        }
        else
        {
            _animator.DrawFrame(TileSheet.Boss, X, Y, pal, _frame);
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

        Debug.Assert(Game.World.CurObjectSlot == ObjectSlot.Monster1 + 4);

        if (ObjTimer == 0)
        {
            ObjTimer = 0x10;

            var r = Random.Shared.Next(2);
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

        _frameAccum += (ushort)(Random.Shared.Next(4) + speed);

        if (Direction.None == CheckWorldMargin(Facing))
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
                && Random.Shared.GetByte() >= 0xE0
                && Game.World.GetObject((ObjectSlot)6) == null)
            {
                ShootFireball(ObjType.Fireball2, X, Y);
            }
        }
    }

    private void CheckManhandlaCollisions()
    {
        var objSlot = Game.World.CurObjectSlot;

        var origFacing = Facing;
        var origStateTimer = ObjTimer;

        CheckCollisions();

        ObjTimer = origStateTimer;
        Facing = origFacing;

        if (objSlot == ManhandlaCenterBodySlot)
        {
            InvincibilityTimer = 0;
        }

        PlayBossHitSoundIfHit();

        if (Decoration == 0) return;

        ShoveDirection = 0;
        ShoveDistance = 0;

        if (objSlot == ManhandlaCenterBodySlot)
        {
            Decoration = 0;
            return;
        }

        var handCount = GetManhandlas(true).Count();

        var dummy = new DeadDummyActor(Game, X, Y)
        {
            Decoration = Decoration
        };

        if (handCount > 1)
        {
            Game.World.SetObject(objSlot, dummy);
        }
        else
        {
            Game.Sound.PlayEffect(SoundEffect.BossHit);
            Game.Sound.StopEffect(StopEffect.AmbientInstance);
            Game.World.SetObject((ObjectSlot)4, dummy);
        }

        _parent.PartsDied++;
    }

    public static void ClearRoomData()
    {
        // JOE: TODO: Not needed anymore?
        // _sPartsDied = 0;
        // _sFacingAtFrameBegin = Direction.None;
        // _sBounceDir = Direction.None;
    }
}

internal abstract class DigdoggerActorBase : Actor
{
    protected short CurSpeedFix = 0x003F;
    protected short SpeedAccum;
    protected short TargetSpeedFix = 0x0080;
    protected short AccelDir;
    protected bool IsChild;

    protected DigdoggerActorBase(Game game, ObjType type, int x, int y)
        : base(game, type, x, y)
    {
        Facing = Random.Shared.GetDirection8();
        IsChild = this is DigdoggerChildActor;

        Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);
    }

    protected void UpdateMove()
    {
        if (ObjTimer == 0)
        {
            ObjTimer = 0x10;

            var r = Random.Shared.Next(2);
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

    public override bool IsReoccuring => false;

    private DigdoggerActor(Game game, int x, int y, int childCount)
        : base(game, ObjType.Digdogger1, x, y)
    {
        _childCount = childCount;
        _updateBig = true;

        _animator = new SpriteAnimator(TileSheet.Boss, AnimationId.B1_Digdogger_Big)
        {
            DurationFrames = 12,
            Time = 0,
        };

        _littleAnimator = new SpriteAnimator(TileSheet.Boss, AnimationId.B1_Digdogger_Little)
        {
            DurationFrames = 12,
            Time = 0,
        };

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, _palette);
        Graphics.UpdatePalettes();
    }

    public static DigdoggerActor Make(Game game, int x, int y, int childCount)
    {
        return new DigdoggerActor(game, x, y, childCount);
    }

    public override void Update()
    {
        if (!IsStunned)
        {
            if (Game.World.RecorderUsed == 0)
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
        var pal = CalcPalette(Palette.LevelFgPalette);

        if (_updateBig)
        {
            _animator.Draw(TileSheet.Boss, X, Y, pal);
        }
        _littleAnimator.Draw(TileSheet.Boss, X + 8, Y + 8, pal);
    }

    private void UpdateSplit()
    {
        if (Game.World.RecorderUsed == 1)
        {
            ObjTimer = 0x40;
            Game.World.RecorderUsed = 2;
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
            Game.World.RecorderUsed = 1;
            Game.World.RoomObjCount = _childCount;
            for (var i = 1; i <= _childCount; i++)
            {
                var child = DigdoggerChildActor.Make(Game, X, Y);
                Game.World.SetObject((ObjectSlot)i, child);
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

    private DigdoggerChildActor(Game game, int x, int y)
        : base(game, ObjType.LittleDigdogger, x, y)
    {
        TargetSpeedFix = 0x0180;

        _animator = new SpriteAnimator(TileSheet.Boss, AnimationId.B1_Digdogger_Little)
        {
            DurationFrames = 12,
            Time = 0,
        };
    }

    public static DigdoggerChildActor Make(Game game, int x, int y)
    {
        return new DigdoggerChildActor(game, x, y);
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
        var pal = CalcPalette(Palette.LevelFgPalette);
        _animator.Draw(TileSheet.Boss, X, Y, pal);
    }
}

internal sealed class GohmaActor : Actor
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

    private GohmaActor(Game game, ObjType type, int x = GohmaX, int y = GohmaY)
        : base(game, type, x, y)
    {
        Decoration = 0;
        InvincibilityMask = 0xFB;

        _animator = new SpriteAnimator(TileSheet.Boss, AnimationId.B2_Gohma_Eye_All)
        {
            DurationFrames = 1,
            Time = 0,
        };

        _leftAnimator = new SpriteAnimator(TileSheet.Boss, AnimationId.B2_Gohma_Legs_L)
        {
            DurationFrames = 32,
            Time = 0,
        };

        _rightAnimator = new SpriteAnimator(TileSheet.Boss, AnimationId.B2_Gohma_Legs_R)
        {
            DurationFrames = 32,
            Time = 0,
        };
    }

    public static GohmaActor Make(Game game, ActorColor color, int x = GohmaX, int y = GohmaY)
    {
        return color switch
        {
            ActorColor.Red => new GohmaActor(game, ObjType.RedGohma, x, y),
            ActorColor.Blue => new GohmaActor(game, ObjType.BlueGohma, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(GohmaActor)}."),
        };
    }

    public int GetCurrentCheckPart()
    {
        return _curCheckPart;
    }

    public int GetEyeFrame()
    {
        return _frame;
    }

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

        _animator.DrawFrame(TileSheet.Boss, X, Y, pal, _frame);
        _leftAnimator.Draw(TileSheet.Boss, X - 0x10, Y, pal);
        _rightAnimator.Draw(TileSheet.Boss, X + 0x10, Y, pal);
    }

    private void ChangeFacing()
    {
        var dir = 1;
        var r = Random.Shared.GetByte();

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
            _startOpenEyeTimer = 0xC0 | Random.Shared.GetByte();
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

internal sealed class ArmosActor : ChaseWalkerActor
{
    private static readonly ImmutableArray<AnimationId> _armosAnimMap = [
        AnimationId.OW_Armos_Right,
        AnimationId.OW_Armos_Left,
        AnimationId.OW_Armos_Down,
        AnimationId.OW_Armos_Up
    ];

    private static readonly WalkerSpec _armosSpec = new(_armosAnimMap, 12, Palette.Red, StandardSpeed);

    private int _state;

    public ArmosActor(Game game, int x, int y)
        : base(game, ObjType.Armos, _armosSpec, x, y)
    {
        Decoration = 0;
        Facing = Direction.Down;
        SetFacingAnimation();

        // Set this to make up for the fact that this armos begins completely aligned with tile.
        TileOffset = 3;

        CurrentSpeed = Random.Shared.GetRandom(0x20, 0x60);
    }

    public override void Update()
    {
        var slot = Game.World.CurObjectSlot;

        if (_state == 0)
        {
            // ORIGINAL: Can hit the player, but not get hit.
            if (ObjTimer == 0)
            {
                _state++;
                Game.World.OnActivatedArmos(X, Y);
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
                var dummy = new DeadDummyActor(Game, X, Y)
                {
                    Decoration = Decoration
                };
                Game.World.SetObject(slot, dummy);
            }
        }
    }

    public override void Draw()
    {
        if (_state == 0)
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

    private GoriyaActor(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, spec, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public static GoriyaActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new GoriyaActor(game, ObjType.BlueGoriya, _blueGoriyaSpec, x, y),
            ActorColor.Red => new GoriyaActor(game, ObjType.RedGoriya, _redGoriyaSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(GoriyaActor)}."),
        };
    }

    public void Catch()
    {
        _shotRef = null;

        var r = Random.Shared.GetByte();
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
            var r = Random.Shared.GetByte();
            if (r != 0x23 && r != 0x77)
            {
                return;
            }
        }

        if (Game.World.HasItem(ItemSlot.Clock)) return;

        var shot = Shoot(ObjType.Boomerang);
        if (shot != ObjectSlot.NoneFound)
        {
            WantToShoot = false;
            _shotRef = Game.World.GetObject(shot);
            ObjTimer = (byte)Random.Shared.Next(0x40);
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

        var slot = game.World.FindEmptyMonsterSlot();
        if (slot < ObjectSlot.Monster1 + 5) return;

        var player = game.Link;
        var statueCount = _statueCounts[(int)pattern];

        for (var i = 0; i < statueCount; i++)
        {
            var timer = _timers[i];
            _timers[i]--;

            if (timer != 0) continue;

            var r = Random.Shared.GetByte();
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
    protected StdChaseWalker(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, spec, x, y)
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

    private LynelActor(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, spec, x, y)
    {
        if (type is not (ObjType.BlueLynel or ObjType.RedLynel))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(LynelActor)}.");
        }
    }

    public static LynelActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new LynelActor(game, ObjType.BlueLynel, _blueLynelSpec, x, y),
            ActorColor.Red => new LynelActor(game, ObjType.RedLynel, _redLynelSpec, x, y),
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

    private MoblinActor(Game game, ObjType type, WalkerSpec spec, int x, int y)
        : base(game, type, spec, 0xA0, x, y)
    {
        if (type is not (ObjType.BlueMoblin or ObjType.RedMoblin))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(MoblinActor)}.");
        }
    }

    public static MoblinActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new MoblinActor(game, ObjType.BlueMoblin, _blueMoblinSpec, x, y),
            ActorColor.Red => new MoblinActor(game, ObjType.RedMoblin, _redMoblinSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, $"Invalid color for {nameof(MoblinActor)}."),
        };
    }
}