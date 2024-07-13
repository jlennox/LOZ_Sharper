using SkiaSharp;

namespace z1.Actors;

internal enum ProjectileState { Flying, Spark, Bounce, Spreading, Unknown5 = 5 }

internal abstract class Projectile : Actor
{
    public Actor Source { get; }
    public bool IsPlayerWeapon => Source.IsPlayer;

    public Direction BounceDir;
    public ProjectileState State;

    public Projectile(Game game, int x, int y, Direction direction) : base(game, x, y)
    {
        // Source = source;
    }

    public virtual bool IsInShotStartState() => State == ProjectileState.Flying;

    public virtual bool IsBlockedByMagicSheild => true;

    public void ShotMove(int speed)
    {
        MoveDirection(speed, Facing);

        if ((TileOffset & 7) == 0)
        {
            TileOffset = 0;
        }
    }

    public void CheckPlayer()
    {
        if (IsPlayerWeapon) return;


    }
}

internal abstract class TODOProjectile : Projectile
{
    protected TODOProjectile(Game game, int x, int y, Direction direction) : base(game, x, y, direction)
    {
    }

    public override void Draw() => throw new NotImplementedException();
    public override void Update() => throw new NotImplementedException();
}

internal sealed class FlyingRockProjectile : TODOProjectile
{
    public FlyingRockProjectile(Game game, int x, int y, Direction direction) : base(game, x, y, direction)
    {
    }
}

internal sealed class Fireball2Projectile : TODOProjectile
{
    public Fireball2Projectile(Game game, int x, int y, Direction direction) : base(game, x, y, direction)
    {
    }
}

internal sealed class PlayerSwordProjectile : TODOProjectile
{
    public PlayerSwordProjectile(Game game, int x, int y, Direction direction) : base(game, x, y, direction)
    {
    }

    public void SpreadOut()
    {
        // TODO
    }
}

internal sealed class ArrowProjectile : TODOProjectile
{
    public ArrowProjectile(Game game, int x, int y, Direction direction) : base(game, x, y, direction)
    {
    }

    public void SetSpark(byte frames = 3)
    {
        State = ProjectileState.Spark;
        ObjTimer = frames;
        // TODO: image.anim = Graphics.GetAnimation(Sheet_PlayerAndItems, Anim_PI_Spark);
    }
}

internal enum MagicWaveType { MagicWave1, MagicWave2 }

internal sealed class MagicWaveProjectile : TODOProjectile
{
    public MagicWaveProjectile(Game game, MagicWaveType type, int x, int y, Direction direction) : base(game, x, y, direction)
    {
    }

    public void AddFire()
    {
        // TODO
    }
}

interface IThrower
{
    void Catch();
}

internal sealed class BoomerangProjectile : TODOProjectile, IDisposable
{
    private int _startX;
    private int _startY;
    private int _distanceTarget;
    private Actor _owner;
    private float _x;
    private float _y;
    private float _leaveSpeed;
    private int _state = 1;
    private int _animTimer;
    private SpriteAnimator _animator;

    public BoomerangProjectile(Game game, int x, int y, Direction direction, int distance, float speed, Actor owner) : base(game, x, y, direction)
    {
        // TODO: animator.anim = Graphics.GetAnimation(Sheet_PlayerAndItems, Anim_PI_Boomerang);
        // TODO: animator.time = 0;
        // TODO: animator.durationFrames = animator.anim->length * 2;

        _startX = x;
        _startY = y;
        _distanceTarget = distance;
        _owner = owner;
        _x = x;
        _y = y;
        _leaveSpeed = speed;
        _state = 1;
        _animTimer = 3;

        if (!IsPlayerWeapon())
            ++Game.World.activeShots;
    }

    public bool IsPlayerWeapon()
    {
        return Game.World.curObjSlot > (int)ObjectSlot.Buffer;
    }

    public void Dispose()
    {
        if (!IsPlayerWeapon())
            --Game.World.activeShots;
    }

    bool IsInShotStartState()
    {
        return _state == 1;
    }

    void SetState(int state)
    {
        _state = state;
    }

    void Update()
    {
        switch (_state)
        {
            case 1: UpdateLeaveFast(); break;
            case 2: UpdateSpark(); break;
            case 3: UpdateLeaveSlow(); break;
            case 4:
            case 5: UpdateReturn(); break;
        }
    }

    void UpdateLeaveFast()
    {
        // TODO: This is all screwed up. Refer to the orignial source. Need to ref _x, _y
        Position += MoveSimple8(Facing, _leaveSpeed).ToSize();

        if (Direction.None == CheckWorldMargin(Facing))
        {
            _state = 2;
            _animTimer = 3;
            CheckCollision();
        }
        else
        {
            if (Math.Abs(_startX - X) < _distanceTarget  && Math.Abs(_startY - Y) < _distanceTarget)
            {
                AdvanceAnimAndCheckCollision();
            }
            else
            {
                _distanceTarget = 0x10;
                _state = 3;
                _animTimer = 3;
                _animator.Time = 0;
                CheckCollision();
            }
        }
    }

    void UpdateLeaveSlow()
    {
        bool gotoNextState = true;

        if ((Facing & Direction.Left) == 0 || _x >= 2)
        {
            if (((Direction)Moving & Direction.Left) != 0)
                Facing = Direction.Left;
            else if (((Direction)Moving & Direction.Right) != 0)
                Facing = Direction.Right;

            Position += MoveSimple8(Facing, 1);

            _distanceTarget--;
            if (_distanceTarget != 0)
                gotoNextState = false;
        }

        if (gotoNextState)
        {
            _distanceTarget = 0x20;
            _state = 4;
            _animator.Time = 0;
        }

        AdvanceAnimAndCheckCollision();
    }

    void UpdateReturn()
    {
        if (_owner == null || _owner.Decoration != 0)
        {
            IsDeleted = true;
            return;
        }

        var thrower = _owner as IThrower;
        if (thrower == null)
        {
            IsDeleted = true;
            return;
        }

        int yDist = _owner.Y - (int)Math.Floor(_y);
        int xDist = _owner.X - (int)Math.Floor(_x);

        if (Math.Abs(xDist) < 9 && Math.Abs(yDist) < 9)
        {
            thrower.Catch();
            IsDeleted = true;
        }
        else
        {
            var angle = (float)Math.Atan2(yDist, xDist);
            float speed = 2;

            if (_state == 4)
            {
                speed = 1;
                _distanceTarget--;
                if (_distanceTarget == 0)
                {
                    _state = 5;
                    _animator.Time = 0;
                }
            }

            Maffs.PolarToCart(angle, speed, out var xSpeed, out var ySpeed);

            _x += xSpeed;
            _y += ySpeed;
            X = (int)_x;
            Y = (int)_y;

            AdvanceAnimAndCheckCollision();
        }
    }

    void UpdateSpark()
    {
        _animTimer--;
        if (_animTimer == 0)
        {
            _state = 5;
            _animTimer = 3;
            _animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Boomerang);
            _animator.Time = 0;
        }
        else
        {
            _animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Spark);
            _animator.Time = 0;
        }
    }

    void AdvanceAnimAndCheckCollision()
    {
        _animTimer--;
        if (_animTimer == 0)
        {
            // The original game sets animTimer to 2.
            // But the sound from the NSF doesn't sound right at that speed.
            _animTimer = 11;
            if (_owner != null && _owner.IsPlayer)
                Game.Sound.Play(SoundEffect.Boomerang);
        }

        _animator.Advance();

        CheckCollision();
    }

    void CheckCollision()
    {
        if (!IsPlayerWeapon())
        {
            PlayerCollision collision = CheckPlayerCollision();
            if (collision.ShotCollides)
            {
                _state = 2;
                _animTimer = 3;
            }
        }
    }

    void Draw()
    {
        int itemValue = Game.World.GetItem(ItemSlot.Boomerang);
        if (itemValue == 0)
            itemValue = 1;
        var pal = (_state == 2) ? Palette.RedFgPalette : (Palette.Player + itemValue - 1);
        int xOffset = (16 - _animator.Animation?.Width ?? 0) / 2;
        _animator.Draw(TileSheet.PlayerAndItems, _x + xOffset, _y, pal);
    }
}

internal abstract class WalkerActor : Actor
{
    protected const int StandardSpeed = 0x20;
    protected const int FastSpeed = 0x40;

    protected SpriteAnimator Animator = new();
    protected abstract int AnimationTime { get; }
    protected abstract int Speed { get; }
    protected abstract bool IsBlue { get; }

    protected virtual bool HasProjectile => false;
    protected virtual Projectile CreateProjectile() => throw new NotImplementedException();

    protected int CurrentSpeed;
    protected int ShootTimer = 0;
    protected bool WantToShoot = false;

    protected Palette _palette;

    protected abstract ReadOnlySpan<AnimationId> AnimationMap { get; }

    protected static SKBitmap SpriteFromIndex(int index, int y) => Sprites.FromSheet(Sprites.BadguysOverworld, 8 + index * 17, y);

    public WalkerActor(Game game, int x, int y) : base(game, x, y)
    {
        Animator.Time = 0;
        Animator.DurationFrames = AnimationTime;

        Facing = Direction.Left; // ???

        CurrentSpeed = Speed;
        SetFacingAnimation();
    }

    public override void Draw()
    {
        SetFacingAnimation();
        int offsetX = (16 - Animator.Animation.Value.Width) / 2;
        var pal = CalcPalette(_palette);
        Animator.Draw(TileSheet.Npcs, X + offsetX, Y, pal);
    }

    protected void SetFacingAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        if (AnimationMap != null)
            Animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, AnimationMap[dirOrd]);
        else
            Animator.Animation = null;
    }

    protected void TryShooting()
    {
        if (!HasProjectile) return;

        if (IsBlue || ShootTimer != 0 || Random.Shared.Next(0xFF) >= 0xF8)
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

        if (WantToShoot && Game.AddProjectile(CreateProjectile()))
        {
            CurrentSpeed = 0;
            WantToShoot = false;
        }
        else
        {
            CurrentSpeed = Speed;
        }
    }


    public bool TryBigShove()
    {
        if (TileOffset == 0)
        {
            // TODO if (Game.World.CollidesWithTileMoving(X, Y, Facing, false))
            // TODO     return false;
        }

        if (CheckWorldMargin(Facing) == Direction.None)
            return false;

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
    protected ChaseWalkerActor(Game game, int x, int y) : base(game, x, y)
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
        if (ShoveDirection != 0)
            return;

        if (CurrentSpeed == 0 || (TileOffset & 0xF) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0xF;

        // ORIGINAL: If player.state = $FF, then skip all this, go to the end (moving := facing).
        //           But, I don't see how the player can get in that state.

        var observedPos = Game.ObservedPlayer.Position;
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
    protected DelayedWanderer(Game game, int x, int y) : base(game, x, y)
    {
        InitCommonFacing();
        InitCommonStateTimer(ref ObjTimer);
        SetFacingAnimation();
    }
}

internal abstract class WandererWalkerActor : WalkerActor
{
    protected byte TurnTimer;
    protected byte TurnRate;

    protected WandererWalkerActor(Game game, int x, int y) : base(game, x, y)
    {
    }

    public override void Update()
    {
        Animator.Advance();
        Move();
        TryShooting();
        // CheckCollisions();
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

    void TargetPlayer()
    {
        if (TurnTimer > 0)
            TurnTimer--;

        if (ShoveDirection != 0)
            return;

        if (CurrentSpeed == 0 || (TileOffset & 0xF) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0xF;

        var r = Random.Shared.GetByte();

        // ORIGINAL: If (r > turnRate) or (player.state = $FF), then ...
        //           But, I don't see how the player can get in that state.

        if (r > TurnRate)
        {
            TurnIfTime();
        }
        else
        {
            var playerPos = Game.ObservedPlayer.Position;

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

    void TurnIfTime()
    {
        WantToShoot = false;

        if (TurnTimer != 0) return;

        if (Facing.IsVertical())
        {
            TurnX();
        }
        else
        {
            TurnY();
        }
    }

    void TurnX()
    {
        Facing = Game.GetXDirToPlayer(X);
        TurnTimer = Random.Shared.GetByte();
        WantToShoot = true;
    }

    void TurnY()
    {
        Facing = Game.GetYDirToPlayer(Y);
        TurnTimer = Random.Shared.GetByte();
        WantToShoot = true;
    }
}

internal sealed class OctorokActor : DelayedWanderer
{
    protected override int AnimationTime => 12;
    protected override int Speed => _speed;
    protected override bool HasProjectile => true;

    private readonly int _speed = StandardSpeed;

    protected override bool IsBlue => false;

    public OctorokActor(Game game, ActorColor color, bool isFast, int x, int y) : base(game, x, y)
    {
        ObjType = (color, isFast) switch
        {
            (ActorColor.Blue, false) => ObjType.BlueSlowOctorock,
            (ActorColor.Blue, true) => ObjType.BlueFastOctorock,
            (ActorColor.Red, false) => ObjType.RedSlowOctorock,
            (ActorColor.Red, true) => ObjType.RedFastOctorock,
            _ => throw new ArgumentOutOfRangeException(),
        };

        if (isFast) _speed = 0x30;
        TurnRate = (byte)(color == ActorColor.Red ? 0x70 : 0xA0);
    }

    protected override Projectile CreateProjectile()
    {
        return new FlyingRockProjectile(Game, X, Y, Facing);
    }

    protected override ReadOnlySpan<AnimationId> AnimationMap => new[] {
        AnimationId.OW_Octorock_Right,
        AnimationId.OW_Octorock_Left,
        AnimationId.OW_Octorock_Down,
        AnimationId.OW_Octorock_Up,
    };
}

internal abstract class TODOActor : Actor
{
    protected TODOActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
    protected TODOActor(Game game, ObjType type, int x = 0, int y = 0) : base(game, type, x, y)
    {
    }

    protected TODOActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, x, y)
    {
        Color = color;
    }
    protected TODOActor(Game game, ActorColor color, ObjType type, int x = 0, int y = 0) : base(game, type, x, y)
    {
        Color = color;
    }

    public override void Draw() => throw new NotImplementedException();
    public override void Update() => throw new NotImplementedException();
}

internal sealed class GanonActor : TODOActor
{
    public override bool IsReoccuring => false;

    public GanonActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Ganon, x, y) { }
}

internal sealed class WhirlwindActor : TODOActor
{
    private byte _prevRoomId;
    private readonly SpriteAnimator _animator = new();

    public WhirlwindActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Whirlwind, x, y) {
        Facing = Direction.Right;

       _animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, AnimationId.OW_Whirlwind);
       _animator.DurationFrames = 2;
       _animator.Time = 0;
    }

    public void SetTeleportPrevRoomId(byte roomId)
    {
        _prevRoomId = roomId;
    }

    public override void Update()
    {
        X += 2;

        var player = Game.Link;

        if (player.GetState() != PlayerState.Paused || Game.World.WhirlwindTeleporting == 0)
        {
            var thisMiddle = new Point(X + 8, Y + 5);
            var playerMiddle = player.GetMiddle();

            if (Math.Abs(thisMiddle.X - playerMiddle.X) < 14
                && Math.Abs(thisMiddle.Y - playerMiddle.Y) < 14)
            {
                player.Facing = Direction.Right;
                player.Stop();
                player.SetState(PlayerState.Paused);
                Game.World.WhirlwindTeleporting = 1;

                player.Y = 0xF8;
            }
        }
        else
        {
            player.X = X;

            if (Game.World.WhirlwindTeleporting == 2 && X == 0x80)
            {
                player.SetState(PlayerState.Idle);
                player.Y = Y;
                Game.World.WhirlwindTeleporting = 0;
                IsDeleted = true;
            }
        }

        if (X >= 0xF0)
        {
            IsDeleted = true;
            if (Game.World.WhirlwindTeleporting != 0)
            {
                Game.World.LeaveRoom(Direction.Right, _prevRoomId);
            }
        }

        _animator.Advance();
    }

    public override void Draw()
    {
        var pal = Palette.Player + (Game.GetFrameCounter() & 3);
        _animator.Draw(TileSheet.Npcs, X, Y, pal);
    }
}

internal abstract class DarknutActor : TODOActor
{
    public DarknutActor(Game game, ActorColor color, ObjType type, int x = 0, int y = 0) : base(game, color, x, y) { }
}

internal sealed class RedDarknutActor : DarknutActor
{
    public RedDarknutActor(Game game, int x = 0, int y = 0) : base(game, ActorColor.Red, ObjType.RedDarknut, x, y) { }
}

internal sealed class BlueDarknutActor : DarknutActor
{
    public BlueDarknutActor(Game game, int x = 0, int y = 0) : base(game, ActorColor.Red, ObjType.BlueDarknut, x, y) { }
}

internal sealed class VireActor : TODOActor
{
    public VireActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Vire, x, y) { }
}

internal sealed class GrumbleActor : TODOActor
{
    public override bool ShouldStopAtPersonWall => true;
    public override bool IsReoccuring => false;
    public override bool IsUnderworldPerson => true;

    public GrumbleActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Grumble, x, y) { }
}

internal sealed class ZolActor : TODOActor
{
    public ZolActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Zol, x, y) { }
}

internal sealed class GohmaActor : TODOActor
{
    private SpriteAnimator animator;
    private SpriteAnimator leftAnimator;
    private SpriteAnimator rightAnimator;

    private bool _changeFacing;
    private short _speedAccum;
    private int _distance;
    private int _sprints;
    private int _startOpenEyeTimer;
    private int _eyeOpenTimer;
    private int _eyeClosedTimer;
    private int _shootTimer;
    private int _frame;
    private int _curCheckPart;

    public override bool IsReoccuring => false;

    public GohmaActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, color, x, y)
    {
        ObjType = color switch {
            ActorColor.Red => ObjType.RedGohma,
            ActorColor.Blue => ObjType.BlueGohma,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public int GetCurrentCheckPart() => _curCheckPart;
    public int GetEyeFrame() => _frame;
}

internal sealed class PolsVoiceActor : TODOActor
{
    public PolsVoiceActor(Game game, int x = 0, int y = 0) : base(game, ObjType.PolsVoice, x, y) { }
}

internal enum BombState { Initing, Ticking, Blasting, Fading }

internal sealed class BombActor : TODOActor
{
    public BombState BombState;

    public BombActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Bomb, x, y) { }
}

internal enum FireState { Moving, Standing }

internal class FireActor : TODOActor
{
    public FireState FireState;

    protected FireActor(Game game, ObjType type, int x = 0, int y = 0) : base(game, type, x, y) { }
    public FireActor(Game game, int x = 0, int y = 0) : this(game, ObjType.Fire, x, y) { }
}

internal abstract class PlayerWeapon : TODOActor
{
    public int State;
    public int Timer;

    public PlayerWeapon(Game game, int x, int y, Actor source) : base(game, x, y)
    {
    }
}

internal sealed class SwordPlayerWeapon : PlayerWeapon
{
    public SwordPlayerWeapon(Game game, int x, int y, Actor source) : base(game, x, y, source)
    {
    }
}

internal sealed class RodPlayerWeapon : PlayerWeapon
{
    public RodPlayerWeapon(Game game, int x, int y, Actor source) : base(game, x, y, source)
    {
    }
}

internal sealed class FlyingGhiniActor : TODOActor
{
    public FlyingGhiniActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class GhiniActor : TODOActor
{
    public GhiniActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class ArmosActor : TODOActor
{
    public ArmosActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal enum ActorColor { Undefined, Blue, Red, Black }

internal sealed class KeeseActor : TODOActor
{
    public KeeseActor(Game game, ActorColor type, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class TektiteActor : TODOActor
{
    public TektiteActor(Game game, ActorColor type, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class GoriyaActor : TODOActor
{
    public GoriyaActor(Game game, ActorColor type, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class DeadDummyActor : TODOActor
{
    public DeadDummyActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class GelActor : TODOActor
{
    public GelActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class ChildGelActor : TODOActor
{
    public ChildGelActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class ZoraActor : TODOActor
{
    public ZoraActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class StalfosActor : TODOActor
{
    public override bool CanHoldRoomItem => true;
    public StalfosActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class GibdoActor : TODOActor
{
    public override bool CanHoldRoomItem => true;
    public GibdoActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class Bubble1Actor : TODOActor
{
    public override bool CountsAsLiving => false;
    public Bubble1Actor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class Bubble2Actor : TODOActor
{
    public override bool CountsAsLiving => false;
    public Bubble2Actor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class Bubble3Actor : TODOActor
{
    public override bool CountsAsLiving => false;
    public Bubble3Actor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class TrapActor : TODOActor
{
    public override bool CountsAsLiving => false;
    public TrapActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class LikeLikeActor : TODOActor
{
    public override bool CanHoldRoomItem => true;
    public LikeLikeActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal static class Statues
{
    public enum PatternType
    {
        Patterns = 3,
        MaxStatues = 4,
    }

    public static byte[] Timers = new byte[(int)PatternType.MaxStatues];

    public static void Init() {}
    public static void Update(PatternType pattern) { }
};


internal sealed class RupieStashActor : TODOActor
{
    public override bool IsReoccuring => false;
    public RupieStashActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class ZeldaActor : TODOActor
{
    public override bool IsReoccuring => false;
    public ZeldaActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

public enum DigdoggerType { One, Two, Little }
internal sealed class DigdoggerActor : TODOActor
{
    public override bool IsReoccuring => false;
    public DigdoggerActor(Game game, DigdoggerType type, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class DodongosActor : TODOActor
{
    public override bool IsReoccuring => false;
    public DodongosActor(Game game, int count, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class ManhandlaActor : TODOActor
{
    public override bool IsReoccuring => false;
    public ManhandlaActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class AquamentusActor : TODOActor
{
    public override bool IsReoccuring => false;
    public AquamentusActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class GuardFireActor : FireActor
{
    public override bool IsReoccuring => false;
    public GuardFireActor(Game game, int x = 0, int y = 0) : base(game, ObjType.GuardFire, x, y)
    {
    }
}
internal sealed class StandingFireActor : FireActor
{
    public override bool IsReoccuring => false;
    public StandingFireActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = ObjType.StandingFire;
    }
}
internal sealed class MoldormActor : TODOActor
{
    public override bool IsReoccuring => false;
    public MoldormActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = ObjType.Moldorm;
    }
}
internal sealed class GleeokActor : TODOActor
{
    public override bool IsReoccuring => false;
    public GleeokActor(Game game, int headCount, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = headCount switch
        {
            1 => ObjType.Gleeok1,
            2 => ObjType.Gleeok2,
            3 => ObjType.Gleeok3,
            4 => ObjType.Gleeok4,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
internal sealed class GleeokHeadActor : TODOActor
{
    public override bool IsReoccuring => false;
    public GleeokHeadActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = ObjType.GleeokHead;
    }
}

internal enum PatraType { Circle, Spin }

internal sealed class PatraActor : TODOActor
{
    public override bool IsReoccuring => false;
    public PatraActor(Game game, PatraType type, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = type switch
        {
            PatraType.Circle => ObjType.Patra1,
            PatraType.Spin => ObjType.Patra2,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

internal sealed class PatraChildActor : TODOActor
{
    public override bool IsReoccuring => false;
    public PatraChildActor(Game game, PatraType type, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = type switch
        {
            PatraType.Circle => ObjType.PatraChild1,
            PatraType.Spin => ObjType.PatraChild2,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

internal sealed class LeeverActor : TODOActor
{
    public LeeverActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = color switch
        {
            ActorColor.Blue => ObjType.BlueLeever,
            ActorColor.Red => ObjType.RedLeever,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

internal sealed class LynelActor : TODOActor
{
    public LynelActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = color switch
        {
            ActorColor.Blue => ObjType.BlueLynel,
            ActorColor.Red => ObjType.RedLynel,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

internal sealed class MoblinActor : TODOActor
{
    public MoblinActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class WizzrobeActor : TODOActor
{
    public WizzrobeActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class LamnolaActor : TODOActor
{
    public LamnolaActor(Game game, ActorColor color, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class PeahatActor : TODOActor
{
    public PeahatActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class WallmasterActor: TODOActor
{
    public WallmasterActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class BouldersActor : TODOActor
{
    public BouldersActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class BoulderActor : TODOActor
{
    public BoulderActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class RopeActor : TODOActor
{
    public RopeActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}

internal sealed class PondFairyActor : TODOActor
{
    public PondFairyActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}
internal sealed class FairyActor : TODOActor
{
    public FairyActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }
}