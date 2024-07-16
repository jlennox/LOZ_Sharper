using System.Diagnostics;
using SkiaSharp;
using z1.Player;

namespace z1.Actors;

internal enum ProjectileState { Flying, Spark, Bounce, Spreading, Unknown5 = 5 }

internal abstract class Projectile : Actor
{
    public Actor Source { get; }
    public bool IsPlayerWeapon => Game.World.curObjectSlot > ObjectSlot.Buffer;

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

    protected void CheckPlayer()
    {
        if (!IsPlayerWeapon)
        {
            var collision = CheckPlayerCollision();
            if (collision.Collides)
            {
                IsDeleted = true;
            }
            else if (collision.ShotCollides)
            {
                TileOffset = 0;
                State = ProjectileState.Bounce;
                BounceDir = Game.Link.Facing;
            }
        }
    }

    private static readonly int[] xSpeeds = new[] { 2, -2, -1, 1 };
    private static readonly int[] ySpeeds = new[] { -1, -1, 2, -2 };

    protected void UpdateBounce()
    {
        int dirOrd = BounceDir.GetOrdinal();

        X += xSpeeds[dirOrd];
        Y += ySpeeds[dirOrd];
        TileOffset += 2;

        if (TileOffset >= 0x20)
            IsDeleted = true;
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

internal sealed class PlayerSwordProjectile : Projectile
{
    private int _distance;
    private readonly SpriteImage _image;

    public PlayerSwordProjectile(Game game, int x, int y, Direction direction) : base(game, x, y, direction)
    {
        Facing = direction;
        Decoration = 0;

        var dirOrd = Facing.GetOrdinal();;
        var animIndex = PlayerSword.swordAnimMap[dirOrd];
        _image = new SpriteImage {
            Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, animIndex)
        };
    }

    public override void Update()
    {
        switch (State)
        {
            case ProjectileState.Flying: UpdateFlying(); break;
            case ProjectileState.Spreading: UpdateSpreading(); break;
            case ProjectileState.Bounce: UpdateBounce(); break;
        }
    }

    private void UpdateFlying()
    {
        if (Direction.None == CheckWorldMargin(Facing))
        {
            if (IsPlayerWeapon)
                SpreadOut();
            else
                IsDeleted = true;
            return;
        }

        ShotMove(0xC0);
        CheckPlayer();
    }

    private void UpdateSpreading()
    {
        if (_distance == 21)
        {
            // The original game still drew in this frame, but we won't.
            IsDeleted = true;
            return;
        }
        _distance++;
    }

    public override void Draw()
    {
        var palOffset = Game.GetFrameCounter() % Global.ForegroundPalCount;
        var palette = Palette.Player + palOffset;
        var yOffset = Facing.IsHorizontal() ? 3 : 0;

        if (State == ProjectileState.Flying)
        {
            var xOffset = (16 - _image.Animation.Width) / 2;
            _image.Draw(TileSheet.PlayerAndItems, X + xOffset, Y + yOffset, palette);
        }
        else
        {
            if (_distance != 0)
            {
                var xOffset = 4;
                var d = _distance - 1;
                var left = X - 2 - d + xOffset;
                var right = X + 2 + d + xOffset;
                var top = Y - 2 - d + yOffset;
                var bottom = Y + 2 + d + yOffset;

                _image.Draw(TileSheet.PlayerAndItems, left, top, palette, 0);
                _image.Draw(TileSheet.PlayerAndItems, right, top, palette, DrawingFlags.FlipHorizontal);
                _image.Draw(TileSheet.PlayerAndItems, left, bottom, palette, DrawingFlags.FlipVertical);
                _image.Draw(TileSheet.PlayerAndItems, right, bottom, palette,
                    DrawingFlags.FlipHorizontal | DrawingFlags.FlipVertical);
            }
        }
    }

    public void SpreadOut()
    {
        State = ProjectileState.Spreading;
        _distance = 0;
        _image.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Slash);
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

internal interface IThrower
{
    void Catch();
}

internal sealed class BoomerangProjectile : TODOProjectile, IDisposable
{
    private readonly int _startX;
    private readonly int _startY;
    private int _distanceTarget;
    private readonly Actor _owner;
    private float _x;
    private float _y;
    private readonly float _leaveSpeed;
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

    public new bool IsPlayerWeapon()
    {
        return Game.World.curObjSlot > (int)ObjectSlot.Buffer;
    }

    public void Dispose()
    {
        if (!IsPlayerWeapon())
            --Game.World.activeShots;
    }

    new bool IsInShotStartState()
    {
        return _state == 1;
    }

    void SetState(int state)
    {
        _state = state;
    }

    public override void Update()
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
                Game.Sound.PlayEffect(SoundEffect.Boomerang);
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

    public override void Draw()
    {
        int itemValue = Game.World.GetItem(ItemSlot.Boomerang);
        if (itemValue == 0)
            itemValue = 1;
        var pal = (_state == 2) ? Palette.RedFgPalette : (Palette.Player + itemValue - 1);
        int xOffset = (16 - _animator.Animation?.Width ?? 0) / 2;
        _animator.Draw(TileSheet.PlayerAndItems, _x + xOffset, _y, pal);
    }
}

internal readonly record struct WalkerSpec(AnimationId[]? AnimationMap, int AnimationTime, Palette Palette, int Speed = 0, ObjType ShotType = ObjType.None);

internal abstract class WalkerActor : Actor
{
    protected const int StandardSpeed = 0x20;
    protected const int FastSpeed = 0x40;

    protected WalkerSpec Spec { get; set; }
    protected SpriteAnimator Animator;
    protected AnimationId[]? AnimationMap => Spec.AnimationMap;
    protected int AnimationTime => Spec.AnimationTime;
    protected int Speed => Spec.Speed;
    protected virtual Palette Palette => Spec.Palette;

    protected virtual bool HasProjectile => false;
    protected virtual Projectile CreateProjectile() => throw new NotImplementedException();

    protected int CurrentSpeed;
    protected int ShootTimer = 0;
    protected bool WantToShoot = false;

    protected static SKBitmap SpriteFromIndex(int index, int y) => Sprites.FromSheet(Sprites.BadguysOverworld, 8 + index * 17, y);

    public WalkerActor(Game game, ObjType type, WalkerSpec spec, int x, int y) : base(game, type, x, y)
    {
        Spec = spec;
        Animator = new() {
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
        int offsetX = (16 - Animator.Animation.Width) / 2;
        var pal = CalcPalette(Palette);
        Animator.Draw(TileSheet.Npcs, X + offsetX, Y, pal);
    }

    protected void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = AnimationMap != null ? Graphics.GetAnimation(TileSheet.Npcs, AnimationMap[dirOrd]) : null;
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

        // TODO if (WantToShoot && Game.AddProjectile(CreateProjectile()))
        // TODO {
        // TODO     CurrentSpeed = 0;
        // TODO     WantToShoot = false;
        // TODO }
        // TODO else
        // TODO {
        // TODO     CurrentSpeed = Speed;
        // TODO }
    }

    public bool TryBigShove()
    {
        if (TileOffset == 0)
        {
            if (Game.World.CollidesWithTileMoving(X, Y, Facing, false))
                return false;
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
    protected ChaseWalkerActor(Game game, ObjType type, WalkerSpec spec, int x, int y) : base(game, type, spec, x, y)
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

        // ORIGINAL: If player.state = $FF, then skip all this, go to the end (moving := Facing).
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
    protected DelayedWanderer(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y) : base(game, type, spec, turnRate, x, y)
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

    protected WandererWalkerActor(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y) : base(game, type, spec, x, y)
    {
        TurnRate = (byte)turnRate;
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
    public const ObjType ShotFromOctorock = ObjType.FlyingRock;

    private static readonly AnimationId[] octorockAnimMap = new[]
    {
        AnimationId.OW_Octorock_Right,
        AnimationId.OW_Octorock_Left,
        AnimationId.OW_Octorock_Down,
        AnimationId.OW_Octorock_Up,
    };

    private static readonly WalkerSpec blueSlowOctorockSpec = new(octorockAnimMap, 12, Palette.Blue, Global.StdSpeed, ShotFromOctorock);
    private static readonly WalkerSpec blueFastOctorockSpec = new(octorockAnimMap, 12, Palette.Blue, FastSpeed, ShotFromOctorock);
    private static readonly WalkerSpec redSlowOctorockSpec = new(octorockAnimMap, 12, Palette.Red, Global.StdSpeed, ShotFromOctorock);
    private static readonly WalkerSpec redFastOctorockSpec = new(octorockAnimMap, 12, Palette.Red, FastSpeed, ShotFromOctorock);

    protected override bool HasProjectile => true;

    private OctorokActor(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y) : base(game, type, spec, turnRate, x, y)
    {
    }

    public static OctorokActor Make(Game game, ActorColor color, bool isFast, int x, int y)
    {
        return (color, isFast) switch
        {
            (ActorColor.Blue, false) => new OctorokActor(game, ObjType.BlueSlowOctorock, blueSlowOctorockSpec, 0xA0, x, y),
            (ActorColor.Blue, true) => new OctorokActor(game, ObjType.BlueFastOctorock, blueFastOctorockSpec, 0xA0, x, y),
            (ActorColor.Red, false) => new OctorokActor(game, ObjType.RedSlowOctorock, redSlowOctorockSpec, 0x70, x, y),
            (ActorColor.Red, true) => new OctorokActor(game, ObjType.RedFastOctorock, redFastOctorockSpec, 0x70, x, y),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    protected override Projectile CreateProjectile()
    {
        return new FlyingRockProjectile(Game, X, Y, Facing);
    }
}

internal abstract class TODOActor : Actor
{
    protected TODOActor(Game game, int x, int y) : base(game, x, y)
    {
    }
    protected TODOActor(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
    }

    protected TODOActor(Game game, ActorColor color, int x, int y) : base(game, x, y)
    {
        Color = color;
    }
    protected TODOActor(Game game, ActorColor color, ObjType type, int x, int y) : base(game, type, x, y)
    {
        Color = color;
    }

    public override void Draw() => throw new NotImplementedException();
    public override void Update() => throw new NotImplementedException();
}

internal sealed class GleeokHeadActor : FlyingActor
{
    private static readonly AnimationId[] gleeokHeadAnimMap = new[]
    {
        AnimationId.B2_Gleeok_Head2,
        AnimationId.B2_Gleeok_Head2,
        AnimationId.B2_Gleeok_Head2,
        AnimationId.B2_Gleeok_Head2,
    };

    private static readonly FlyerSpec gleeokHeadSpec = new(gleeokHeadAnimMap, TileSheet.Boss, Palette.Red, 0xE0);

    public override bool IsReoccuring => false;
    public GleeokHeadActor(Game game, int x, int y) : base(game, ObjType.GleeokHead, gleeokHeadSpec, x, y)
    {
        Facing = Random.Shared.GetDirection8();

        curSpeed = 0xBF;
        InvincibilityMask = 0xFF;
    }

    public override void Update()
    {
        UpdateStateAndMove();

        int r = Random.Shared.GetByte();

        if (r < 0x20
            && (moveCounter & 1) == 0
            && Game.World.GetObject(ObjectSlot.LastMonster) == null)
        {
            ShootFireball(ObjType.Fireball2, X, Y);
        }

        CheckCollisions();
        Decoration = 0;
        ShoveDirection = 0;
        ShoveDistance = 0;
        InvincibilityTimer = 0;
    }

    protected override void UpdateFullSpeedImpl()
    {
        int nextState = 2;
        int r = Random.Shared.GetByte();

        if (r >= 0xD0)
            nextState++;

        GoToState(nextState, 6);
    }
}

internal sealed class GleeokNeck
{
    private readonly Game _game;
    public const int MaxParts = 5;
    public const int HeadIndex = MaxParts - 1;
    public const int ShooterIndex = HeadIndex;

    struct Limits
    {
        public int value0;
        public int value1;
        public int value2;
    }

    struct Part
    {
        public int x;
        public int y;
    }

    private static readonly byte[] startYs = new byte[] { 0x6F, 0x74, 0x79, 0x7E, 0x83 };

    readonly Part[] parts = new Part[MaxParts];
    readonly SpriteImage neckImage = new SpriteImage();
    readonly SpriteImage headImage = new SpriteImage();

    int startHeadTimer;
    int xSpeed;
    int ySpeed;
    int changeXDirTimer;
    int changeYDirTimer;
    int changeDirsTimer;
    bool isAlive;
    public int hp;

    public GleeokNeck(Game game, int i)
    {
        _game = game;
        Init(i);
    }

    public bool IsAlive()
    {
        return isAlive;
    }

    public void SetDead()
    {
        isAlive = false;
    }

    int GetHP()
    {
        return hp;
    }

    public void SetHP(int value)
    {
        hp = value;
    }

    public Point GetPartLocation(int partIndex) => new Point(parts[partIndex].x, parts[partIndex].y);

    void Init(int index)
    {

        for (int i = 0; i < MaxParts; i++)
        {
            parts[i].x = 0x7C;
            parts[i].y = startYs[i];
        }

        isAlive = true;
        hp = 0xA0;
        changeXDirTimer = 6;
        changeYDirTimer = 3;
        xSpeed = 1;
        ySpeed = 1;
        changeDirsTimer = 0;
        startHeadTimer = 0;

        if (index == 0 || index == 2)
            xSpeed = -1;
        else
            ySpeed = -1;

        switch (index)
        {
            case 1: startHeadTimer = 12; break;
            case 2: startHeadTimer = 24; break;
            case 3: startHeadTimer = 36; break;
        }

        neckImage.Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gleeok_Neck);
        headImage.Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gleeok_Head);
    }

    public void Update()
    {
        MoveNeck();
        MoveHead();
        TryShooting();
    }

    public void Draw()
    {
        for (int i = 0; i < HeadIndex; i++)
        {
            neckImage.Draw(TileSheet.Boss, parts[i].x, parts[i].y, Palette.SeaPal);
        }

        headImage.Draw(TileSheet.Boss, parts[HeadIndex].x, parts[HeadIndex].y, Palette.SeaPal);
    }

    void MoveHead()
    {
        if (startHeadTimer != 0)
        {
            startHeadTimer--;
            return;
        }

        parts[HeadIndex].x += xSpeed;
        parts[HeadIndex].y += ySpeed;

        changeDirsTimer++;
        if (changeDirsTimer < 4)
            return;
        changeDirsTimer = 0;

        changeXDirTimer++;
        if (changeXDirTimer >= 0xC)
        {
            changeXDirTimer = 0;
            xSpeed = -xSpeed;
        }

        changeYDirTimer++;
        if (changeYDirTimer >= 6)
        {
            changeYDirTimer = 0;
            ySpeed = -ySpeed;
        }
    }

    void TryShooting()
    {
        int r = Random.Shared.GetByte();
        if (r < 0x20
            && _game.World.GetObject(ObjectSlot.LastMonster) == null)
        {
            _game.ShootFireball(ObjType.Fireball2, parts[ShooterIndex].x, parts[ShooterIndex].y);
        }
    }

    void MoveNeck()
    {
        Limits xLimits = new();
        Limits yLimits = new();

        int headToEndXDiv4 = (parts[4].x - parts[0].x) / 4;
        int headToEndXDiv4Abs = Math.Abs(headToEndXDiv4);
        GetLimits(headToEndXDiv4Abs, ref xLimits);

        int headToEndYDiv4Abs = Math.Abs(parts[4].y - parts[0].y) / 4;
        GetLimits(headToEndYDiv4Abs, ref yLimits);

        int distance;

        // If passed the capped high limit X or Y from previous part, then bring it back in. (1..4)
        for (int i = 0; i < 4; i++)
        {
            distance = Math.Abs(parts[i].x - parts[i + 1].x);
            if (distance >= xLimits.value2)
            {
                int oldX = parts[i + 1].x;
                int x = oldX + 2;
                if (oldX >= parts[i].x)
                    x -= 4;
                parts[i + 1].x = x;
            }
            distance = Math.Abs(parts[i].y - parts[i + 1].y);
            if (distance >= yLimits.value2)
            {
                int oldY = parts[i + 1].y;
                int y = oldY + 2;
                if (oldY >= parts[i].y)
                    y -= 4;
                parts[i + 1].y = y;
            }
        }

        // Stretch, depending on distance to the next part. (1..3)
        for (int i = 0; i < 3; i++)
        {
            Stretch(i, ref xLimits, ref yLimits);
        }

        // If passed the X limit, then bring it back in. (3..1)
        for (int i = 2; i >= 0; i--)
        {
            int xLimit = parts[0].x;
            for (int j = i; j >= 0; j--)
            {
                xLimit += headToEndXDiv4;
            }
            int x = parts[i + 1].x + 1;
            if (xLimit < parts[i + 1].x)
                x -= 2;
            parts[i + 1].x = x;
        }

        // If part's Y is not in between surrounding parts, then bring it back in. (3..2)
        for (int i = 1; i >= 0; i--)
        {
            int y2 = parts[i + 2].y;
            if (y2 < parts[i + 1].y)
            {
                if (y2 < parts[i + 3].y)
                    parts[i + 2].y++;
            }
            else
            {
                if (y2 >= parts[i + 3].y)
                    parts[i + 2].y--;
            }
        }
    }

    static void GetLimits(int distance, ref Limits limits)
    {
        if (distance > 4)
            distance = 4;
        limits.value0 = distance;

        distance += 4;
        if (distance > 8)
            distance = 8;
        limits.value1 = distance;

        distance += 4;
        if (distance > 11)
            distance = 11;
        limits.value2 = distance;
    }

    void Stretch(int index, ref Limits xLimits, ref Limits yLimits )
    {
        int distance;
        int funcIndex = 0;

        // The original was [index+2] - [index+2]
        distance = Math.Abs(parts[index + 2].x - parts[index + 1].x);
        if (distance >= xLimits.value0)
            funcIndex++;
        if (distance >= xLimits.value1)
            funcIndex++;

        distance = Math.Abs(parts[index + 2].y - parts[index + 1].y);
        if (distance >= yLimits.value0)
            funcIndex += 3;
        if (distance >= yLimits.value1)
            funcIndex += 3;

        var funcs = new Action<int>[]
        {
            CrossedNoLimits,
            CrossedLowLimit,
            CrossedMidXLimit,
            CrossedLowLimit,
            CrossedLowLimit,
            CrossedMidXLimit,
            CrossedMidYLimit,
            CrossedMidYLimit,
            CrossedBothMidLimits,
        };

        Debug.Assert(funcIndex >= 0 && funcIndex < funcs.Length);

        funcs[funcIndex](index);
    }

    void CrossedNoLimits(int index)
    {
        int r = Random.Shared.Next(2);
        if (r == 0)
        {
            int oldX = parts[index + 1].x;
            int x = oldX + 2;
            if (oldX < parts[index + 2].x)
                x -= 4;
            parts[index + 1].x = x;
        }
        else
        {
            int oldY = parts[index + 1].y;
            int y = oldY + 2;
            if (oldY <= parts[index + 2].y)
                y -= 4;
            parts[index + 1].y = y;
        }
    }

    void CrossedLowLimit(int index)
    {
        // Nothing to do
    }

    void CrossedMidYLimit(int index)
    {
        int oldY = parts[index + 1].y;
        int y = oldY + 2;
        if (oldY > parts[index + 2].y)
            y -= 4;
        parts[index + 1].y = y;
    }

    void CrossedMidXLimit(int index)
    {
        int oldX = parts[index + 1].x;
        int x = oldX + 2;
        if (oldX >= parts[index + 2].x)
            x -= 4;
        parts[index + 1].x = x;
    }

    void CrossedBothMidLimits(int index)
    {
        int r = Random.Shared.Next(2);
        if (r == 0)
            CrossedMidXLimit(index);
        else
            CrossedMidYLimit(index);
    }
}

internal sealed class GleeokActor : Actor
{
    private const int GleeokX = 0x74;
    private const int GleeokY = 0x57;

    private const int MaxNecks = 4;
    private const int NormalAnimFrames = 17 * 4;
    private const int WrithingAnimFrames = 7 * 4;
    private const int TotalWrithingFrames = 7 * 7;

    private static readonly byte[] palette = new byte[] { 0, 0x2A, 0x1A, 0x0C };

    readonly SpriteAnimator Animator;
    int writhingTimer;
    int neckCount;
    readonly GleeokNeck[] necks = new GleeokNeck[MaxNecks];

    public override bool IsReoccuring => false;
    public GleeokActor(Game game, int headCount, int x = GleeokX, int y = GleeokY) : base(game, x, y)
    {
        ObjType = headCount switch
        {
            1 => ObjType.Gleeok1,
            2 => ObjType.Gleeok2,
            3 => ObjType.Gleeok3,
            4 => ObjType.Gleeok4,
            _ => throw new ArgumentOutOfRangeException(),
        };

        Decoration = 0;
        InvincibilityMask = 0xFE;

        Animator = new SpriteAnimator {
            DurationFrames = NormalAnimFrames,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gleeok_Body)
        };

        for (int i = 0; i < neckCount; i++)
        {
            necks[i] = new GleeokNeck(game, i);
        }

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, palette);
        Graphics.UpdatePalettes();

        Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance );
    }

    public override void Update()
    {
        Animate();

        for (int i = 0; i < neckCount; i++)
        {
            if (!necks[i].IsAlive())
                continue;

            if ((Game.GetFrameCounter() % MaxNecks) == i)
                necks[i].Update();

            CheckNeckCollisions(i);
        }
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.SeaPal);
        Animator.Draw(TileSheet.Boss, X, Y, pal);

        for (int i = 0; i < neckCount; i++)
        {
            if (necks[i].IsAlive())
                necks[i].Draw();
        }
    }

    void Animate()
    {
        Animator.Advance();

        if (writhingTimer != 0)
        {
            writhingTimer--;
            if (writhingTimer == 0)
            {
                Animator.DurationFrames = NormalAnimFrames;
                Animator.Time = 0;
            }
        }
    }

    void CheckNeckCollisions(int index)
    {
        var neck = necks[index];
        var partIndexes = new[] { 0, GleeokNeck.HeadIndex };
        int origX = X;
        int origY = Y;
        int bodyDecoration = 0;

        for (int i = 0; i < 2; i++)
        {
            int partIndex = partIndexes[i];
            Point loc = neck.GetPartLocation(partIndex);

            X = loc.X;
            Y = loc.Y;
            HP = (byte)neck.hp;

            CheckCollisions();

            neck.SetHP(HP);

            if (ShoveDirection != 0)
            {
                writhingTimer = TotalWrithingFrames;
                Animator.DurationFrames = WrithingAnimFrames;
                Animator.Time = 0;
            }

            ShoveDirection = 0;
            ShoveDistance = 0;

            if (partIndex != GleeokNeck.HeadIndex)
            {
                Decoration = 0;
            }
            else
            {
                PlayBossHitSoundIfHit();

                if (Decoration != 0)
                {
                    neck.SetDead();

                    var slot = ObjectSlot.Monster1 + index + 6;
                    var head = new GleeokHeadActor(Game, X, Y);
                    Game.World.SetObject(slot, head);

                    int aliveCount = 0;
                    for (var jj = 0; jj < neckCount; jj++)
                    {
                        if (necks[jj].IsAlive())
                            aliveCount++;
                    }

                    if (aliveCount == 0)
                    {
                        Game.Sound.PlayEffect(SoundEffect.BossHit);
                        Game.Sound.StopEffect(StopEffect.AmbientInstance);

                        bodyDecoration = 0x11;
                        // Don't include the last slot, which is used for fireballs.
                        for (var jj = ObjectSlot.Monster1 + 1; jj < ObjectSlot.LastMonster; jj++)
                        {
                            Game.World.SetObject(jj, null);
                        }
                    }
                }
            }
        }

        Y = origY;
        X = origX;
        Decoration = (byte)bodyDecoration;
    }
}

internal sealed class GanonActor : BlueWizzrobeBase
{
    public override bool IsReoccuring => false;

    [Flags]
    private enum Visual
    {
        None,
        Ganon = 1,
        Pile = 2,
        Pieces = 4,
    };

    Visual visual;
    int state;
    byte lastHitTimer;
    int dyingTimer;
    int frame;

    int cloudDist;
    readonly int[] sparksX = new int[8];
    readonly int[] sparksY = new int[8];
    readonly Direction[] piecesDir = new Direction[8];

    SpriteAnimator Animator;
    SpriteAnimator cloudAnimator;
    SpriteImage pileImage;

    private static readonly byte[] ganonNormalPalette = new byte[] { 0x16, 0x2C, 0x3C };
    private static readonly byte[] ganonRedPalette = new byte[] { 0x07, 0x17, 0x30 };

    public GanonActor(Game game, int x, int y) : base(game, ObjType.Ganon, x, y) {
        InvincibilityMask = 0xFA;

        Animator.DurationFrames = 1;
        Animator.Time = 0;
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B3_Ganon);

        pileImage.Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B3_Pile);

        cloudAnimator.DurationFrames = 1;
        cloudAnimator.Time = 0;
        cloudAnimator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Cloud);

        Game.Link.SetState(PlayerState.Paused);
        Game.Link.ObjTimer  =0x40;
        ObjTimer = 0;

        SetBossPalette(ganonNormalPalette);
        // The original game starts roaring here. But, I think it sounds better later.
    }

    public override void Update()
    {
        visual = Visual.None;

        switch (state)
        {
            case 0: UpdateHoldDark(); break;
            case 1: UpdateHoldLight(); break;
            case 2: UpdateActive(); break;
        }
    }


    private readonly record struct SlashSpec(TileSheet Sheet, AnimationId AnimIndex, byte Flags);

    private readonly SlashSpec[] slashSpecs = new[]
    {
        new SlashSpec(TileSheet.Boss,           AnimationId.B3_Slash_U,    0),
        new SlashSpec(TileSheet.PlayerAndItems, AnimationId.Slash,      1),
        new SlashSpec(TileSheet.Boss,           AnimationId.B3_Slash_L,    1),
        new SlashSpec(TileSheet.PlayerAndItems, AnimationId.Slash,      3),
        new SlashSpec(TileSheet.Boss,           AnimationId.B3_Slash_U,    2),
        new SlashSpec(TileSheet.PlayerAndItems, AnimationId.Slash,      2),
        new SlashSpec(TileSheet.Boss,           AnimationId.B3_Slash_L,    0),
        new SlashSpec(TileSheet.PlayerAndItems, AnimationId.Slash,      0),
    };

    public override void Draw()
    {
        if (visual.HasFlag(Visual.Ganon))
        {
            var pal = CalcPalette(Palette.SeaPal);
            Animator.DrawFrame(TileSheet.Boss, X, Y, pal, frame);
        }

        if (visual.HasFlag(Visual.Pile))
        {
            pileImage.Draw(TileSheet.Boss, X, Y, Palette.SeaPal);
        }

        if (visual.HasFlag(Visual.Pieces))
        {
            int cloudFrame = (cloudDist < 6) ? 2 : 1;

            for (int i = 0; i < 8; i++)
            {
                int cloudX = X;
                int cloudY = Y;

                MoveSimple8(ref cloudX, ref cloudY, piecesDir[i], cloudDist);

                cloudAnimator.DrawFrame(TileSheet.PlayerAndItems, cloudX, cloudY, Palette.SeaPal, cloudFrame);
            }

            var slashPal = 4 + (Game.GetFrameCounter() & 3);

            for (int i = 0; i < 8; i++)
            {
                var slashSpec = slashSpecs[i];
                var image = new SpriteImage();

                image.Animation = Graphics.GetAnimation(slashSpec.Sheet, slashSpec.AnimIndex);
                image.Draw(slashSpec.Sheet, sparksX[i], sparksY[i], (Palette)slashPal, (DrawingFlags)slashSpec.Flags);
            }
        }
    }

    void UpdateHoldDark()
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
                state = 1;
                Game.Link.ObjTimer = 0xC0;
            }
            visual = Visual.Ganon;
        }
    }

    void UpdateHoldLight()
    {
        Game.World.LiftItem(ItemId.TriforcePiece, 0);

        if (Game.Link.ObjTimer == 0)
        {
            Game.Link.SetState(PlayerState.Idle);
            Game.World.LiftItem(ItemId.None);
            Game.Sound.PlaySong(SongId.Level9, SongStream.MainSong, true);
            Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance);
            state = 2;
        }

        visual = Visual.Ganon;
    }

    void UpdateActive()
    {
        if (dyingTimer != 0)
        {
            UpdateDying();
        }
        else
        {
            CheckCollision();
            PlayBossHitSoundIfHit();

            if (lastHitTimer != 0)
                UpdateLastHit();
            else if (ObjTimer == 0)
                UpdateMoveAndShoot();
            else if (ObjTimer == 1)
                ResetPosition();
            else
                visual = Visual.Ganon;
        }
    }

    void UpdateDying()
    {
        // This isn't exactly like the original, but the intent is clearer.
        if (dyingTimer < 0xFF)
            dyingTimer++;

        if (dyingTimer < 0x50)
        {
            visual |= Visual.Ganon;
            return;
        }

        if (dyingTimer == 0x50)
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

        visual |= Visual.Pile;

        if (dyingTimer < 0xA0)
        {
            MovePieces();
            visual |= Visual.Pieces;
        }
        else if (dyingTimer == 0xA0)
        {
            Game.World.AddUWRoomItem();
            var triforce = Game.World.GetObject(ObjectSlot.Item) ?? throw new Exception();
            triforce.X = X;
            triforce.Y = Y;
            Game.World.IncrementRoomKillCount();
            Game.Sound.PlayEffect(SoundEffect.RoomItem);
        }
    }

    void CheckCollision()
    {
        var player = Game.Link;

        if (player.InvincibilityTimer == 0)
        {
            CheckPlayerCollisionDirect();
        }

        if (lastHitTimer != 0)
        {
            int itemValue = Game.World.GetItem(ItemSlot.Arrow);
            if (itemValue == 2)
            {
                // The original checks the state of the arrow here and leaves if <> $10.
                // But, CheckArrow does a similar check (>= $20). As far as I can tell, both are equivalent.
                if (CheckArrow(ObjectSlot.Arrow))
                {
                    dyingTimer = 1;
                    InvincibilityTimer = 0x28;
                    cloudDist = 8;
                }
            }
            return;
        }
        else if (ObjTimer != 0)
            return;

        CheckSword(ObjectSlot.PlayerSword);

        if (Decoration != 0)
        {
            HP = 0xF0;
            lastHitTimer--;
            SetBossPalette(ganonRedPalette);
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

    void UpdateLastHit()
    {
        if ((Game.GetFrameCounter() & 1) == 1)
        {
            lastHitTimer--;
            if (lastHitTimer == 0)
            {
                ResetPosition();
                SetBossPalette(ganonNormalPalette);
                return;
            }
        }

        if (lastHitTimer >= 0x30
            || (Game.GetFrameCounter() & 1) == 1)
        {
            visual |= Visual.Ganon;
        }
    }

    void UpdateMoveAndShoot()
    {
        frame++;
        if (frame == 6)
            frame = 0;

        MoveAround();

        if ((Game.GetFrameCounter() & 0x3F) == 0)
            ShootFireball(ObjType.Fireball2, X, Y);
    }

    void MoveAround()
    {
        flashTimer = 1;
        turnTimer++;
        TurnIfNeeded();
        MoveAndCollide();
    }

    void MakePieces()
    {
        for (int i = 0; i < 8; i++)
        {
            sparksX[i] = X + 4;
            sparksY[i] = Y + 4;
            piecesDir[i] = i.GetDirection8();
        }
    }

    void MovePieces()
    {
        if (cloudDist != 0 && (Game.GetFrameCounter() & 7) == 0)
            cloudDist--;

        for (int i = 0; i < 8; i++)
        {
            if (piecesDir[i].IsHorizontal()
                || piecesDir[i].IsVertical()
                || (Game.GetFrameCounter() & 3) != 0)
            {
                MoveSimple8(ref sparksX[i], ref sparksY[i], piecesDir[i], 1);
            }
        }
    }

    void SetBossPalette(byte[] palette)
    {
        Graphics.SetColorIndexed(Palette.SeaPal, 1, palette[0]);
        Graphics.SetColorIndexed(Palette.SeaPal, 2, palette[1]);
        Graphics.SetColorIndexed(Palette.SeaPal, 3, palette[2]);
        Graphics.UpdatePalettes();
    }

    void ResetPosition()
    {
        Y = 0xA0;
        X = (Game.GetFrameCounter() & 1) == 0 ? 0x30 : 0xB0;
    }
}

internal sealed class ZeldaActor : Actor
{
    private const int ZeldaX          = 0x78;
    private const int ZeldaLineX1     = 0x70;
    private const int ZeldaLineX2     = 0x80;
    private const int ZeldaY          = 0x88;
    private const int ZeldaLineY      = 0x95;

    private const int LinkX           = 0x88;
    private const int LinkY           = ZeldaY;

    public override bool IsReoccuring => false;

    int state;
    SpriteImage image;

    private static readonly byte[] xs = new byte[] { 0x60, 0x70, 0x80, 0x90 };
    private static readonly byte[] ys = new byte[] { 0xB5, 0x9D, 0x9D, 0xB5 };

    private ZeldaActor(Game game, int x = ZeldaX, int y = ZeldaY) : base(game, ObjType.Zelda, x, y)
    {
        image.Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B3_Zelda_Stand);
    }

    public static ZeldaActor Make(Game game)
    {
        for (int i = 0; i < xs.Length; i++)
        {
            int y = ys[i];

            var fire = new GuardFireActor(game, xs[i], y);
            game.World.SetObject(ObjectSlot.Monster1 + 1 + i, fire);
        }

        return new ZeldaActor(game);
    }

    public override void Update()
    {
        var player = Game.Link;

        if (state == 0)
        {
            int playerX = player.X;
            int playerY = player.Y;

            if (playerX >= ZeldaLineX1
                && playerX <= ZeldaLineX2
                && playerY <= ZeldaLineY)
            {
                state = 1;
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
        image.Draw(TileSheet.Boss, X, Y, Palette.Player);
    }
}

internal sealed class StandingFireActor : FireActor
{
    public override bool IsReoccuring => false;
    private readonly SpriteAnimator _animator;

    public StandingFireActor(Game game, int x, int y) : base(game, ObjType.StandingFire, x, y)
    {
        _animator = new SpriteAnimator
        {
            Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Fire),
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

internal sealed class GuardFireActor : FireActor
{
    public override bool IsReoccuring => false;
    SpriteAnimator animator;

    public GuardFireActor(Game game, int x, int y) : base(game, ObjType.GuardFire, x, y)
    {
        animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Fire);
        animator.DurationFrames = 12;
        animator.Time = 0;
    }

    public override void Update()
    {
        animator.Advance();
        CheckCollisions();
        if (Decoration != 0)
        {
            var dummy = new DeadDummyActor(Game, X, Y);
            Game.World.SetObject(Game.World.curObjectSlot, dummy);
            dummy.Decoration  = Decoration;
        }
    }

    public override void Draw()
    {
        animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.RedFgPalette);
    }
}

internal sealed class RupeeStashActor : Actor
{
    private static readonly byte[] xs = new byte[] { 0x78, 0x70, 0x80, 0x60, 0x70, 0x80, 0x90, 0x70, 0x80, 0x78 };
    private static readonly byte[] ys = new byte[] { 0x70, 0x80, 0x80, 0x90, 0x90, 0x90, 0x90, 0xA0, 0xA0, 0xB0 };

    private RupeeStashActor(Game game, int x, int y) : base(game, ObjType.RupieStash, x, y) { }

    public static RupeeStashActor Make(Game game)
    {
        for (int i = 0; i < xs.Length; i++)
        {
            var rupee = new RupeeStashActor(game, xs[i], ys[i]);
            game.World.SetObject((ObjectSlot)i, rupee);
        }

        return game.World.GetObject<RupeeStashActor>(ObjectSlot.Monster1) ?? throw new Exception();
    }

    public override void Update()
    {
        var player = Game.Link;
        int distanceX = Math.Abs(player.X - X);
        int distanceY = Math.Abs(player.Y - Y);

        if (distanceX <= 8
            && distanceY <= 8)
        {
            Game.World.PostRupeeWin(1);
            Game.World.IncrementRoomKillCount();
            IsDeleted = true;
        }
    }

    public override void Draw()
    {
        GlobalFunctions.DrawItemWide(Game, ItemId.Rupee, X, Y);
    }
}

internal sealed class FairyActor : FlyingActor
{
    private static readonly AnimationId[] fairyAnimMap = new AnimationId[]
    {
        AnimationId.Fairy,
        AnimationId.Fairy,
        AnimationId.Fairy,
        AnimationId.Fairy
    };

    private static readonly FlyerSpec fairySpec = new(fairyAnimMap, TileSheet.PlayerAndItems, Palette.Red, 0xA0);

    int timer;

    // JOE: TODO: Fairy is an "item," not an actor. IS this a problem?
    public FairyActor(Game game, int x, int y) : base(game, ObjType.None, fairySpec, x, y)
    {
    }

    public override void Update()
    {
        if ((Game.GetFrameCounter() & 1) == 1)
            timer--;

        if (timer == 0)
        {
            IsDeleted = true;
            return;
        }

        UpdateStateAndMove();

        var objSlots = new[] { ObjectSlot.Player, ObjectSlot .Boomerang};
        bool touchedItem = false;

        foreach (var slot in objSlots)
        {
            var obj = Game.World.GetObject(slot);
            if (obj != null && !obj.IsDeleted && TouchesObject(obj ) )
            {
                touchedItem = true;
                break;
            }
        }

        if (touchedItem)
        {
            Game.World.AddItem(ItemId.Fairy);
            IsDeleted = true;
        }
    }

    protected override void UpdateFullSpeedImpl()
    {
        GoToState(3, 6);
    }

    protected override int GetFrame()
    {
        return (moveCounter & 4) >> 2;
    }

    bool TouchesObject(Actor obj)
    {
        int distanceX = Math.Abs(obj.X - X);
        int distanceY = Math.Abs(obj.Y - Y);

        return distanceX <= 8
            && distanceY <= 8;
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

    enum State
    {
        Idle,
        Healing,
        Healed,
    };

    State state;
    SpriteAnimator Animator;
    readonly byte[] heartState = new byte[8];
    readonly byte[] heartAngle = new byte[8];

    public PondFairyActor(Game game) : base(game, ObjType.PondFairy, PondFairyX, PondFairyY)
    {
        Animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Fairy);
        Animator.Time = 0;
        Animator.DurationFrames = 8;

        Game.Sound.PlayEffect(SoundEffect.Item);
    }

    public override void Update()
    {
        Animator.Advance();

        if (state == State.Idle)
            UpdateIdle();
        else if (state == State.Healing)
            UpdateHealing();
    }

    void UpdateIdle()
    {
        var player = Game.Link;
        int playerX = player.X;
        int playerY = player.Y;

        if (playerY != PondFairyLineY
            || playerX < PondFairyLineX1
            || playerX > PondFairyLineX2)
            return;

        state = State.Healing;
        player.SetState(PlayerState.Paused);
    }

    private static readonly byte[] entryAngles = new byte[] { 0, 11, 22, 33, 44, 55, 66, 77 };
    void UpdateHealing()
    {
        for (int i = 0; i< heartState.Length; i++ )
        {
            if (heartState[i] == 0 )
            {
                if (heartAngle[0] == entryAngles[i] )
                    heartState[i] = 1;
            }
            else
            {
                heartAngle[i]++;
                if (heartAngle[i] >= 85 )
                    heartAngle[i] = 0;
            }
        }

        var profile = Game.World.GetProfile();
        int maxHeartsValue = profile.GetMaxHeartsValue();

        Game.Sound.PlayEffect(SoundEffect.Character);

        if (profile.Hearts < maxHeartsValue)
        {
            Game.World.FillHearts(6);
        }
        else if (heartState[7] != 0)
        {
            state = State.Healed;
            var player = Game.Link;
            player.SetState(PlayerState.Idle);
            Game.World.SwordBlocked = false;
        }
    }

    public override void Draw()
    {
        int xOffset = (16 - Animator.Animation.Width) / 2;
        Animator.Draw(TileSheet.PlayerAndItems, PondFairyX + xOffset, PondFairyY, Palette.RedFgPalette);

        if (state != State.Healing)
            return;

        const float Radius = 0x36;
        const float Angle = -Global.TWO_PI / 85.0f;
        SpriteImage heart = new SpriteImage();

        heart.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Heart);

        for (int i = 0; i < heartState.Length; i++)
        {
            if (heartState[i] == 0)
                continue;

            int angleIndex = heartAngle[i] + 22;
            float angle = Angle * angleIndex;
            int x = (int)(Math.Cos(angle) * Radius + PondFairyRingCenterX);
            int y = (int)(Math.Sin(angle) * Radius + PondFairyRingCenterY);

            heart.Draw(TileSheet.PlayerAndItems, x, y, Palette.RedFgPalette);
        }
    }
}

internal sealed class DeadDummyActor : Actor
{
    public DeadDummyActor(Game game, int x, int y) : base(game, ObjType.DeadDummy, x, y)
    {
    }

    public override void Update()
    {
        Decoration = 0x10;
        Game.Sound.PlayEffect(SoundEffect.MonsterDie);
    }

    public override void Draw() { }
}

internal sealed class WhirlwindActor : Actor
{
    private byte _prevRoomId;
    private readonly SpriteAnimator _animator = new();

    public WhirlwindActor(Game game, int x, int y) : base(game, ObjType.Whirlwind, x, y) {
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


internal sealed class GrumbleActor : TODOActor
{
    public override bool ShouldStopAtPersonWall => true;
    public override bool IsReoccuring => false;
    public override bool IsUnderworldPerson => true;

    public GrumbleActor(Game game, int x, int y) : base(game, ObjType.Grumble, x, y) { }
}

internal abstract class StdWanderer : WandererWalkerActor
{
    private readonly WalkerSpec _spec;

    protected StdWanderer(Game game, ObjType type, WalkerSpec spec, int turnRate, int x, int y) : base(game, type, spec, turnRate, x, y)
    {
        _spec = spec;
    }
}

internal sealed class GhiniActor : WandererWalkerActor
{
    private static readonly AnimationId[] ghiniAnimMap = new[]
    {
        AnimationId.OW_Ghini_Right,
        AnimationId.OW_Ghini_Left,
        AnimationId.OW_Ghini_Left,
        AnimationId.OW_Ghini_UpRight,
    };

    private static readonly WalkerSpec ghiniSpec = new(ghiniAnimMap, 12, Palette.Blue, Global.StdSpeed);

    public GhiniActor(Game game, int x, int y) : base(game, ObjType.Ghini, ghiniSpec, 0xFF, x, y)
    {
        InitCommonFacing();
        InitCommonStateTimer(ref ObjTimer);
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
                flying.Decoration = 0x11;
            }
        }
    }
}

internal sealed class GibdoActor : StdWanderer
{
    private static readonly AnimationId[] gibdoAnimMap = new[]
    {
        AnimationId.UW_Gibdo,
        AnimationId.UW_Gibdo,
        AnimationId.UW_Gibdo,
        AnimationId.UW_Gibdo
    };

    private static readonly WalkerSpec gibdoSpec = new(gibdoAnimMap, 16, Palette.Blue, Global.StdSpeed);

    public override bool CanHoldRoomItem => true;
    public GibdoActor(Game game, int x, int y) : base(game, ObjType.Gibdo, gibdoSpec, 0x80, x, y)
    {
    }
}

internal sealed class DarknutActor : StdWanderer
{
    private static readonly AnimationId[] darknutAnimMap = new[]
    {
        AnimationId.UW_Darknut_Right,
        AnimationId.UW_Darknut_Left,
        AnimationId.UW_Darknut_Down,
        AnimationId.UW_Darknut_Up
    };

    private static readonly WalkerSpec redDarknutSpec = new(darknutAnimMap, 16, Palette.Red, Global.StdSpeed);
    private static readonly WalkerSpec blueDarknutSpec = new(darknutAnimMap, 16, Palette.Blue, 0x28);

    private DarknutActor(Game game, ObjType type, WalkerSpec spec, int x, int y) : base(game, type, spec, 0x80, x, y)
    {
        if (type is not (ObjType.RedDarknut or ObjType.BlueDarknut))
        {
            throw new ArgumentOutOfRangeException();
        }

        InvincibilityMask = 0xF6;
    }

    public static DarknutActor Make(Game game, ActorColor type, int x, int y)
    {
        return type switch
        {
            ActorColor.Red => new DarknutActor(game, ObjType.RedDarknut, redDarknutSpec, x, y),
            ActorColor.Blue => new DarknutActor(game, ObjType.BlueDarknut, blueDarknutSpec, x, y),
            _ => throw new ArgumentOutOfRangeException()
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
    public override bool CanHoldRoomItem => true;

    private static readonly AnimationId[] stalfosAnimMap = new[]
    {
        AnimationId.UW_Stalfos,
        AnimationId.UW_Stalfos,
        AnimationId.UW_Stalfos,
        AnimationId.UW_Stalfos,
    };

    private static readonly WalkerSpec stalfosSpec = new(stalfosAnimMap, 16, Palette.Red, Global.StdSpeed, ObjType.PlayerSwordShot);

    public StalfosActor(Game game, int x, int y) : base(game, ObjType.Stalfos, stalfosSpec, 0x80, x, y)
    {
    }

    public override void Update()
    {
        MoveIfNeeded();
        CheckCollisions();
        Animator.Advance();

        if (Game.World.GetProfile().Quest == 1)
        {
            TryShooting();
        }
    }
}

internal sealed class GelActor : WandererWalkerActor
{
    private static readonly byte[] gelWaitTimes = new byte[] { 0x08, 0x18, 0x28, 0x38 };

    private static readonly AnimationId[] gelAnimMap = new[]
    {
        AnimationId.UW_Gel,
        AnimationId.UW_Gel,
        AnimationId.UW_Gel,
        AnimationId.UW_Gel,
    };

    private static readonly WalkerSpec gelSpec = new(gelAnimMap, 4, Palette.SeaPal, 0x40);

    int state;

    public GelActor(Game game, ObjType type, int x, int y, Direction dir, byte fraction) : base(game, type, gelSpec, 0x20, x, y)
    {
        if (type is not (ObjType.Gel or ObjType.ChildGel))
        {
            throw new ArgumentOutOfRangeException();
        }

        Facing = dir;

        if (type == ObjType.Gel)
            state = 2;
        else
            Fraction = fraction;

        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        switch (state)
        {
            case 0:
                ObjTimer = 5;
                state = 1;
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

    void UpdateShove()
    {
        if (ObjTimer != 0)
        {
            if (TryBigShove())
                return;
        }
        X = (X + 8) & 0xF0;
        Y = (Y + 8) & 0xF0;
        Y |= 0xD;
        TileOffset = 0;
        state = 2;
    }

    void UpdateWander()
    {
        if (ObjTimer < 5)
        {
            Move();

            if (ObjTimer == 0 && TileOffset == 0)
            {
                int index = Random.Shared.Next(4);
                ObjTimer = gelWaitTimes[index];
            }
        }
    }
}

internal sealed class ZolActor : WandererWalkerActor
{
    private static readonly byte[] zolWaitTimes = new byte[] { 0x18, 0x28, 0x38, 0x48 };

    private static readonly AnimationId[] zolAnimMap = new[]
    {
        AnimationId.UW_Zol,
        AnimationId.UW_Zol,
        AnimationId.UW_Zol,
        AnimationId.UW_Zol,
    };

    private static readonly WalkerSpec zolSpec = new(zolAnimMap, 16, Palette.SeaPal, 0x18);

    int state;

    public ZolActor(Game game, int x, int y) : base(game, ObjType.Zol, zolSpec, 0x20, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        switch (state)
        {
            case 0: UpdateWander(); break;
            case 1: UpdateShove(); break;
            case 2: UpdateSplit(); break;
        }

        Animator.Advance();
    }

    void UpdateWander()
    {
        if (ObjTimer < 5)
        {
            Move();

            if (ObjTimer == 0 && TileOffset == 0)
            {
                var index = Random.Shared.Next(4);
                ObjTimer = zolWaitTimes[index];
            }
        }

        // Above is almost the same as Gel.UpdateWander.

        CheckCollisions();

        if (Decoration == 0 && InvincibilityTimer != 0)
        {
            // On collision , go to state 2 or 1, depending on alignment.

            const uint AlignedY = 0xD;

            var player = Game.Link;
            uint dirMask = 0;

            if ((Y & 0xF) == AlignedY)
                dirMask |= 3;

            if ((X & 0xF) == 0)
                dirMask |= 0xC;

            if ((dirMask & (ulong)player.Facing) == 0)
                state = 2;
            else
                state = 1;
        }
    }

    void UpdateShove()
    {
        if (!TryBigShove())
            state = 2;
    }

    private static readonly Direction[] sHDirs = { Direction.Right, Direction.Left };
    private static readonly Direction[] sVDirs = { Direction.Down, Direction.Up };

    void UpdateSplit()
    {
        IsDeleted = true;
        Game.World.RoomObjCount++;


        var orthoDirs = Facing.IsHorizontal() ? sVDirs : sHDirs;

        for (int i = 0; i < 2; i++)
        {
            var slot = Game.World.FindEmptyMonsterSlot();
            if (slot < 0)
                break;

            var gel = new GelActor(Game, ObjType.ChildGel, X, Y, orthoDirs[i], Fraction);
            Game.World.SetObject(slot, gel);
            gel.ObjTimer = 0;
        }
    }
}

internal sealed class BubbleActor : WandererWalkerActor
{
    private static readonly AnimationId[] bubbleAnimMap = new[]
    {
        AnimationId.UW_Bubble,
        AnimationId.UW_Bubble,
        AnimationId.UW_Bubble,
        AnimationId.UW_Bubble
    };

    private static readonly WalkerSpec bubbleSpec = new(bubbleAnimMap, 2, Palette.Blue, FastSpeed);

    public override bool CountsAsLiving => false;
    public BubbleActor(Game game, ObjType type, int x, int y) : base(game, type, bubbleSpec, 0x40, x, y)
    {
        if (type is not (ObjType.Bubble1 or ObjType.Bubble2 or ObjType.Bubble3))
        {
            throw new Exception();
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
                Game.World.SetStunTimer(ObjectSlot.NoSwordTimer, 0x10);
            else
                Game.World.SwordBlocked = ObjType == ObjType.Bubble3;

            // The sword blocked state is cleared by touching blue bubbles (Bubble2)
            // and by refilling all hearts with the potion or pond fairy.
        }
    }

    public override void Draw()
    {
        var pal = 4;

        if (ObjType == ObjType.Bubble1)
            pal += Game.GetFrameCounter() % 4;
        else
            pal += ObjType - ObjType.Bubble1;

        Animator.Draw(TileSheet.Npcs, X, Y, (Palette)pal);
    }

}

internal sealed class VireActor : WandererWalkerActor
{
    private static readonly int[] vireOffsetY = new[] { 0, -3, -2, -1, -1, 0, -1, 0, 0, 1, 0, 1, 1, 2, 3, 0 };

    private static readonly AnimationId[] vireAnimMap = new[]
    {
        AnimationId.UW_Vire_Down,
        AnimationId.UW_Vire_Down,
        AnimationId.UW_Vire_Down,
        AnimationId.UW_Vire_Up,
    };

    private static readonly WalkerSpec vireSpec = new(vireAnimMap, 20, Palette.Blue, Global.StdSpeed);

    int state;

    public VireActor(Game game, int x, int y) : base(game, ObjType.Vire, vireSpec, 0x80, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        switch (state)
        {
            case 0: UpdateWander(); break;
            case 1: UpdateShove(); break;
            default: UpdateSplit(); break;
        }

        if (state < 2)
        {
            Animator.Advance();
        }
    }

    void UpdateWander()
    {
        MoveIfNeeded();

        if (!IsStunned && Facing.IsHorizontal())
        {
            int offsetX = Math.Abs(TileOffset);
            Y += vireOffsetY[offsetX];
        }

        CheckCollisions();

        if (Decoration == 0 && InvincibilityTimer != 0)
            state = 1;
    }

    void UpdateShove()
    {
        if (!TryBigShove())
            state = 2;
    }

    void UpdateSplit()
    {
        IsDeleted = true;
        Game.World.RoomObjCount++;

        for (int i = 0; i < 2; i++)
        {
            var slot = Game.World.FindEmptyMonsterSlot();
            if (slot < 0)
                break;

            var keese = KeeseActor.Make(Game, ActorColor.Red, X, Y);
            Game.World.SetObject(slot, keese);
            keese.Facing = Facing;
            keese.ObjTimer = 0;
        }
    }
}

internal sealed class LikeLikeActor : WandererWalkerActor
{
    private static readonly AnimationId[] likeLikeAnimMap = new[]
    {
        AnimationId.UW_LikeLike,
        AnimationId.UW_LikeLike,
        AnimationId.UW_LikeLike,
        AnimationId.UW_LikeLike,
    };

    private static readonly WalkerSpec likeLikeSpec = new(likeLikeAnimMap, 24, Palette.Red, Global.StdSpeed);

    int framesHeld;

    public override bool CanHoldRoomItem => true;
    public LikeLikeActor(Game game, int x, int y) : base(game, ObjType.LikeLike, likeLikeSpec, 0x80, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }

    public override void Update()
    {
        var player = Game.Link;

        if (framesHeld == 0)
        {
            MoveIfNeeded();
            Animator.Advance();

            if (CheckCollisions())
            {
                framesHeld++;

                X = player.X;
                Y = player.Y;
                player.ObjTimer = 0;
                // ORIGINAL: PlayerState.[$405] := 0  (But, what's the point?)
                player.ResetShove();
                player.Paralyzed = true;
                Animator.DurationFrames = Animator.Animation.Length * 4;
                Animator.Time = 0;
                Flags |= ActorFlags.DrawAbovePlayer;
            }
        }
        else
        {
            int frame = Animator.Time / 4;
            if (frame < 3)
                Animator.Advance();

            framesHeld++;
            if (framesHeld >= 0x60)
            {
                Game.World.SetItem(ItemSlot.MagicShield, 0);
                framesHeld = 0xC0;
            }

            CheckCollisions();

            if (Decoration != 0)
                player.Paralyzed = false;
        }
    }
}

internal abstract class DigWanderer : WandererWalkerActor
{
    protected int[] stateTimes;
    protected int state;
    private readonly WalkerSpec[] stateSpecs;

    public static readonly AnimationId[] moundAnimMap = new[]
    {
        AnimationId.OW_Mound,
        AnimationId.OW_Mound,
        AnimationId.OW_Mound,
        AnimationId.OW_Mound,
    };

    protected DigWanderer(Game game, ObjType type, WalkerSpec[] specs, int[] stateTimes, int x, int y)
        : base(game, type, specs[0], 0xA0, x, y)
    {
        stateSpecs = specs;
        this.stateTimes = stateTimes;
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
            state = (state + 1) % 6;
            ObjTimer = (byte)stateTimes[state];
            Spec = stateSpecs[state];
        }

        Animator.Advance();

        // JOE: TODO: Offload to sub classes.
        if (state == 3 || (this is ZoraActor && state is 2 or 4))
        {
            CheckCollisions();
        }
    }

    public override void Draw()
    {
        if (state != 0)
        {
            base.Draw();
        }
    }
}

internal sealed class ZoraActor : DigWanderer
{
    private static readonly AnimationId[] zoraAnimMap = new[]
    {
        AnimationId.OW_Zora_Down,
        AnimationId.OW_Zora_Down,
        AnimationId.OW_Zora_Down,
        AnimationId.OW_Zora_Up,
    };

    static readonly WalkerSpec zoraHiddenSpec = new(null, 32, Palette.SeaPal);
    static readonly WalkerSpec zoraMoundSpec = new(moundAnimMap, 22, Palette.SeaPal);
    static readonly WalkerSpec zoraHalfSpec = new(zoraAnimMap, 2, Palette.SeaPal);
    static readonly WalkerSpec zoraFullSpec = new(zoraAnimMap, 10, Palette.SeaPal);

    static readonly WalkerSpec[] zoraSpecs = new[]
    {
        zoraHiddenSpec,
        zoraMoundSpec,
        zoraHalfSpec,
        zoraFullSpec,
        zoraHalfSpec,
        zoraMoundSpec,
    };

    private static readonly int[] zoraStateTimes = new[] { 2, 0x20, 0x0F, 0x22, 0x10, 0x60 };

    public ZoraActor(Game game, int x, int y) : base(game, ObjType.Zora, zoraSpecs, zoraStateTimes, x, y)
    {
        ObjTimer = (byte)stateTimes[0];
        Decoration = 0;
    }

    public override void Update()
    {
        if (Game.World.GetItem(ItemSlot.Clock) != 0) return;

        UpdateDig();

        if (state == 0)
        {
            if (ObjTimer == 1)
            {
                var player = Game.Link;
                var cell = Game.World.GetRandomWaterTile();

                X = cell.Col * World.TileWidth;
                Y = cell.Row * World.TileHeight - 3;

                if (player.Y >= Y)
                    Facing = Direction.Down;
                else
                    Facing = Direction.Up;
            }
        }
        else if (state == 3)
        {
            // TODO if (ObjTimer == 0x20)
            // TODO     ShootFireball(Obj_Fireball, objX, objY);
        }
    }
}

internal sealed class BlueLeeverActor : DigWanderer
{
    private static readonly AnimationId[] leeverAnimMap = new[]
    {
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
    };

    private static readonly AnimationId[] leeverHalfAnimMap = new[]
    {
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
    };

    private static readonly WalkerSpec blueLeeverHiddenSpec = new(null, 32, Palette.WhiteBgPalette, 0x8);
    private static readonly WalkerSpec blueLeeverMoundSpec = new(moundAnimMap, 22, Palette.WhiteBgPalette, 0xA);
    private static readonly WalkerSpec blueLeeverHalfSpec = new(leeverHalfAnimMap, 2, Palette.WhiteBgPalette, 0x10);
    private static readonly WalkerSpec blueLeeverFullSpec = new(leeverAnimMap, 10, Palette.WhiteBgPalette, Global.StdSpeed);

    private static readonly WalkerSpec[] blueLeeverSpecs = new[]
    {
        blueLeeverHiddenSpec,
        blueLeeverMoundSpec,
        blueLeeverHalfSpec,
        blueLeeverFullSpec,
        blueLeeverHalfSpec,
        blueLeeverMoundSpec,
    };

    private static readonly int[] blueLeeverStateTimes = new[] { 0x80, 0x20, 0x0F, 0xFF, 0x10, 0x60 };

    public BlueLeeverActor(Game game, int x, int y) : base(game, ObjType.BlueLeever, blueLeeverSpecs, blueLeeverStateTimes, x, y)
    {
        Decoration = 0;
        InitCommonStateTimer(ref ObjTimer);
        InitCommonFacing();
        SetFacingAnimation();
    }
}

internal sealed class RedLeeverActor : Actor
{
    private static readonly AnimationId[] leeverAnimMap = new[]
    {
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
        AnimationId.OW_Leever,
    };

    private static readonly AnimationId[] leeverHalfAnimMap = new[]
    {
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
        AnimationId.OW_LeeverHalf,
    };

    private static readonly WalkerSpec redLeeverHiddenSpec = new(null, 32, Palette.Red);
    private static readonly WalkerSpec redLeeverMoundSpec = new(DigWanderer.moundAnimMap, 16, Palette.Red);
    private static readonly WalkerSpec redLeeverHalfSpec = new(leeverHalfAnimMap, 16, Palette.Red);
    private static readonly WalkerSpec redLeeverFullSpec = new(leeverAnimMap, 10, Palette.Red, Global.StdSpeed);

    private static readonly WalkerSpec[] redLeeverSpecs = new[]
    {
        redLeeverHiddenSpec,
        redLeeverMoundSpec,
        redLeeverHalfSpec,
        redLeeverFullSpec,
        redLeeverHalfSpec,
        redLeeverMoundSpec,
    };

    private static readonly int[] redLeeverStateTimes = new[] { 0x00, 0x10, 0x08, 0xFF, 0x08, 0x10 };

    SpriteAnimator Animator;

    int state;
    WalkerSpec spec;

    static int count;
    public RedLeeverActor(Game game, int x, int y) : base(game, ObjType.RedLeever, x, y) {
        Facing = Direction.Right;

        Animator = new() {
            Time = 0,
            DurationFrames = spec.AnimationTime
        };

        InitCommonStateTimer(ref ObjTimer);
        // No need to InitCommonFacing, because the Facing is changed with every update.
        SetFacingAnimation();

        Game.World.SetStunTimer(ObjectSlot.RedLeeverClassTimer, 5);
    }

    public override void Update()
    {
        bool advanceState = false;

        if (state == 0)
        {
            if (count >= 2
                || Game.World.GetStunTimer(ObjectSlot.RedLeeverClassTimer) != 0)
                return;
            if (!TargetPlayer())
                return;
            Game.World.SetStunTimer(ObjectSlot.RedLeeverClassTimer, 2);
            advanceState = true;
        }
        else if (state == 3)
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
                    MoveDirection(spec.Speed, Facing);
                    if ((TileOffset & 0xF) == 0)
                        TileOffset &= 0xF;
                    ObjTimer = 0xFF;
                }
            }
        }

        if (advanceState || (state != 3 && ObjTimer == 0))
        {
            state = (state + 1) % redLeeverStateTimes.Length;
            ObjTimer = (byte)redLeeverStateTimes[state];
            SetSpec(redLeeverSpecs[state]);

            if (state == 1)
                count++;
            else if (state == 0)
                count--;
            Debug.Assert(count >= 0 && count <= 2);
        }

        Animator.Advance();

        if (state == 3)
        {
            CheckCollisions();
            if (Decoration != 0 && this is RedLeeverActor)
                count--;
        }
    }

    public override void Draw()
    {
        if (state != 0)
        {
            var pal = CalcPalette(Palette.Red);
            Animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    void SetSpec(WalkerSpec spec )
    {
        this.spec = spec;
        Animator.SetDuration(spec.AnimationTime );
        SetFacingAnimation();
    }

    void SetFacingAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        if (spec.AnimationMap != null)
            Animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, spec.AnimationMap[dirOrd]);
        else
            Animator.Animation = null;
    }

    bool TargetPlayer()
    {
        var player = Game.Link;
        int x = player.X;
        int y = player.Y;

        Facing = player.Facing;

        int r = Random.Shared.GetByte();
        if (r >= 0xC0)
            Facing = Facing.GetOppositeDirection();

        if (Facing.IsVertical())
        {
            if (Facing == Direction.Down)
                y += 0x28;
            else
                y -= 0x28;
            y = (y & 0xF0) + 0xD;
            // y's going to be assigned to a byte, so truncate it now before we test it.
            y &= 0xFF;
        }
        else
        {
            if (Facing == Direction.Right)
                x += 0x28;
            else
                x -= 0x28;
            x &= 0xF8;

            if (Math.Abs(player.X - x) >= 0x30)
                return false;
        }

        if (y < 0x5D)
            return false;

        if (Game.World.CollidesWithTileStill(x, y))
            return false;

        Facing = Facing.GetOppositeDirection();
        X = x;
        Y = y;
        return true;
    }

    public static void ClearRoomData()
    {
        count = 0;
    }
}

internal readonly record struct FlyerSpec(AnimationId[]? AnimationMap, TileSheet Sheet, Palette Palette, int Speed = 0);

internal abstract class FlyingActor : Actor
{
    protected SpriteAnimator Animator = new SpriteAnimator();

    protected int state;
    protected int sprintsLeft;

    protected readonly Action[] sStateFuncs;

    protected int curSpeed;
    protected int accelStep;

    protected Direction deferredDir;
    protected int moveCounter;

    protected readonly FlyerSpec spec;

    protected FlyingActor(Game game, ObjType type, FlyerSpec spec, int x, int y) : base(game, type, x, y)
    {
        this.spec = spec;
        sStateFuncs = new Action[] {
            UpdateHastening,
            UpdateFullSpeed,
            UpdateChase,
            UpdateTurn,
            UpdateSlowing,
            UpdateStill,
        };

        Animator = new() {
            Time = 0,
            Animation = Graphics.GetAnimation(spec.Sheet, spec.AnimationMap[0])
        };
        Animator.DurationFrames = Animator.Animation.Length;
    }

    protected void UpdateStateAndMove()
    {
        var origFacing = Facing;

        sStateFuncs[state]();

        Move();

        if (Facing != origFacing)
            SetFacingAnimation();
    }

    public override void Draw()
    {
        var pal = CalcPalette(spec.Palette);
        int frame = GetFrame();
        Animator.DrawFrame(spec.Sheet, X, Y, pal, frame);
    }

    protected virtual int GetFrame()
    {
        return moveCounter & 1;
    }

    void Move()
    {
        accelStep += (curSpeed & 0xE0);

        if (accelStep < 0x100)
            return;

        accelStep &= 0xFF;
        moveCounter++;

        if ((Facing & Direction.Right) != 0)
            X++;

        if ((Facing & Direction.Left) != 0)
            X--;

        if ((Facing & Direction.Down) != 0)
            Y++;

        if ((Facing & Direction.Up) != 0)
            Y--;

        if (Direction.None != CheckWorldMargin(Facing))
            return;

        if (this is MoldormActor)
        {
            var slot = Game.World.curObjectSlot;
            if (slot == MoldormActor.HeadSlot1 || slot == MoldormActor.HeadSlot2)
                deferredDir = Facing.GetOppositeDir8();
        }
        else
        {
            Facing = Facing.GetOppositeDir8();
        }
    }

    int GetState()
    {
        return state;
    }

    protected void GoToState(int state, int sprints)
    {
        this.state = state;
        this.sprintsLeft = sprints;
    }

    void SetFacingAnimation()
    {
        int dirOrd = (int)(Facing - 1);

        if ((Facing & Direction.Down) != 0)
        {
            dirOrd = (Facing & Direction.Right) != 0 ? 0 : 1;
        }
        else if ((Facing & Direction.Up) != 0)
        {
            dirOrd = (Facing & Direction.Right) != 0 ? 2 : 3;
        }

        Animator.Animation = Graphics.GetAnimation(spec.Sheet, spec.AnimationMap[dirOrd]);
    }

    void UpdateStill()
    {
        if (ObjTimer == 0)
            state = 0;
    }

    void UpdateHastening()
    {
        curSpeed++;
        if ((curSpeed & 0xE0) >= spec.Speed)
        {
            curSpeed = spec.Speed;
            state = 1;
        }
    }

    void UpdateSlowing()
    {
        curSpeed--;
        if ((curSpeed & 0xE0) <= 0)
        {
            curSpeed = 0;
            state = 5;
            ObjTimer = (byte)(Random.Shared.Next(64) + 64);
        }
    }

    void UpdateFullSpeed()
    {
        UpdateFullSpeedImpl();
    }

    protected virtual void UpdateFullSpeedImpl()
    {
        int r = Random.Shared.GetByte();

        if (r >= 0xB0)
        {
            state = 2;
        }
        else if (r >= 0x20)
        {
            state = 3;
        }
        else
        {
            state = 4;
        }
        sprintsLeft = 6;
    }

    void UpdateTurn()
    {
        UpdateTurnImpl();
    }

    protected virtual void UpdateTurnImpl()
    {
        if (ObjTimer != 0)
            return;

        sprintsLeft--;
        if (sprintsLeft == 0)
        {
            state = 1;
            return;
        }

        ObjTimer = 0x10;

        Facing = TurnRandomly8(Facing);
    }

    void UpdateChase()
    {
        UpdateChaseImpl();
    }

    protected virtual void UpdateChaseImpl()
    {
        if (ObjTimer != 0)
            return;

        sprintsLeft--;
        if (sprintsLeft == 0)
        {
            state = 1;
            return;
        }

        ObjTimer = 0x10;

        Facing = TurnTowardsPlayer8(X, Y, Facing);
    }
}

internal abstract class StdFlyerActor : FlyingActor
{
    protected StdFlyerActor(Game game, ObjType type, FlyerSpec spec, int x, int y, Direction facing) : base(game, type, spec, x, y)
    {
        Facing = facing;
    }
}

internal sealed class PeahatActor : StdFlyerActor
{
    private static readonly AnimationId[] peahatAnimMap = new[]
    {
        AnimationId.OW_Peahat,
        AnimationId.OW_Peahat,
        AnimationId.OW_Peahat,
        AnimationId.OW_Peahat
    };

    private static readonly FlyerSpec peahatSpec = new(peahatAnimMap, TileSheet.Npcs, Palette.Red, 0xA0);

    public PeahatActor(Game game, int x, int y) : base(game, ObjType.Peahat, peahatSpec, x, y, Direction.Up)
    {
        Decoration = 0;
        curSpeed = 0x1F;
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

        if (state == 5)
            CheckCollisions();
        else
            CheckPlayerCollision();
    }

}

internal sealed class FlyingGhiniActor : FlyingActor
{
    private static readonly AnimationId[] flyingGhiniAnimMap = new[]
    {
        AnimationId.OW_Ghini_Right,
        AnimationId.OW_Ghini_Left,
        AnimationId.OW_Ghini_UpRight,
        AnimationId.OW_Ghini_UpLeft,
    };

    private static readonly FlyerSpec flyingGhiniSpec = new(flyingGhiniAnimMap, TileSheet.Npcs, Palette.Blue, 0xA0);

    public FlyingGhiniActor(Game game, int x, int y) : base(game, ObjType.FlyingGhini, flyingGhiniSpec, x, y)
    {
        Decoration = 0;
        Facing = Direction.Up;
        curSpeed = 0x1F;
    }

    public override void Update()
    {
        if (state == 0)
        {
            if (ObjTimer == 0)
            {
                state++;
            }
        }
        else
        {
            if (Game.World.GetItem(ItemSlot.Clock) == 0)
                UpdateStateAndMove();

            CheckPlayerCollision();
        }
    }

    public override void Draw()
    {
        if (state == 0)
        {
            if ((ObjTimer & 1) == 1)
                base.Draw();
        }
        else
        {
            base.Draw();
        }
    }

    protected override void UpdateFullSpeedImpl()
    {
        int r = Random.Shared.GetByte();

        if (r >= 0xA0)
            GoToState(2, 6);
        else if (r >= 8)
            GoToState(3, 6);
        else
            GoToState(4, 6);
    }

    protected override int GetFrame()
    {
        return 0;
    }
}

internal sealed class KeeseActor : FlyingActor
{
    private static readonly AnimationId[] keeseAnimMap = new[]
    {
        AnimationId.UW_Keese,
        AnimationId.UW_Keese,
        AnimationId.UW_Keese,
        AnimationId.UW_Keese,
    };

    private static readonly FlyerSpec blueKeeseSpec = new(keeseAnimMap, TileSheet.Npcs, Palette.Blue, 0xC0);
    private static readonly FlyerSpec redKeeseSpec = new(keeseAnimMap,TileSheet.Npcs,Palette.Red,0xC0);
    private static readonly FlyerSpec blackKeeseSpec = new(keeseAnimMap,TileSheet.Npcs,Palette.LevelFgPalette,0xC0);

    private readonly ActorColor _color;

    private KeeseActor(Game game, ObjType type, FlyerSpec spec, int startSpeed, int x, int y) : base(game, type, spec, x, y)
    {
        curSpeed = startSpeed;
        Facing = Random.Shared.GetDirection8();
    }

    public static KeeseActor Make(Game game, ActorColor color, int x, int y)
    {
        ObjType type;
        int startSpeed;
        FlyerSpec spec;
        switch (color)
        {
            case ActorColor.Red:
                type = ObjType.RedKeese;
                startSpeed = 0x7F;
                spec = redKeeseSpec;
                break;
            case ActorColor.Blue:
                type = ObjType.BlueKeese;
                startSpeed = 0x1F;
                spec = blueKeeseSpec;
                break;
            case ActorColor.Black:
                type = ObjType.BlackKeese;
                startSpeed = 0x7F;
                spec = blackKeeseSpec;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(color));
        }

        return new KeeseActor(game, type, spec, startSpeed, x, y);
    }

    void SetFacing(Direction dir)
    {
        Facing = dir;
    }

    public override void Update()
    {
        if (Game.World.GetItem(ItemSlot.Clock) == 0
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
        int r = Random.Shared.GetByte();

        if (r >= 0xA0)
            GoToState(2, 6);
        else if (r >= 0x20)
            GoToState(3, 6);
        else
            GoToState(4, 6);
    }


    protected override int GetFrame()
    {
        return (moveCounter & 2) >> 1;
    }
}

internal sealed class MoldormActor : FlyingActor
{
    public const ObjectSlot HeadSlot1 = ObjectSlot.Monster1 + 4;
    public const ObjectSlot HeadSlot2 = HeadSlot1 + 5;
    public const ObjectSlot TailSlot1 = ObjectSlot.Monster1;
    public const ObjectSlot TailSlot2 = TailSlot1 + 5;

    private static readonly AnimationId[] moldormAnimMap = new[]
    {
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
        AnimationId.UW_Moldorm,
    };

    private static readonly FlyerSpec moldormSpec = new(moldormAnimMap, TileSheet.Npcs, Palette.Red, 0x80);

    Direction oldFacing;

    public override bool IsReoccuring => false;
    private MoldormActor(Game game, int x, int y) : base(game, ObjType.Moldorm, moldormSpec, x, y)
    {
        ObjType = ObjType.Moldorm;
        Facing = Direction.None;
        oldFacing = Facing;

        curSpeed = 0x80;

        GoToState(2, 1);
    }

    public static MoldormActor MakeSet(Game game)
    {
        for (int i = 0; i < 5 * 2; i++)
        {
            var moldorm = new MoldormActor(game, 0x80, 0x70);
            game.World.SetObject((ObjectSlot)i, moldorm);
        }

        var head1 = game.World.GetObject<MoldormActor>((ObjectSlot)4) ?? throw new Exception();
        var head2 = game.World.GetObject<MoldormActor>((ObjectSlot)9) ?? throw new Exception();

        head1.Facing = Random.Shared.GetDirection8();
        head1.oldFacing = head1.Facing;

        head2.Facing = Random.Shared.GetDirection8();
        head2.oldFacing = head2.Facing;

        game.World.RoomObjCount = 8;

        return game.World.GetObject<MoldormActor>(0) ?? throw new Exception();
    }

    public override void Update()
    {
        if (Facing == Direction.None)
            return;

        if (Game.World.GetItem(ItemSlot.Clock) == 0)
            UpdateStateAndMove();

        CheckMoldormCollisions();
    }

    void CheckMoldormCollisions()
    {
        // ORIGINAL: This is just like CheckLamnolaCollisions; but it saves stateTimer, and plays sounds.

        Direction origFacing = Facing;
        int origStateTimer = ObjTimer;

        CheckCollisions();

        ObjTimer = (byte)origStateTimer;
        Facing = origFacing;

        if (Decoration == 0)
            return;

        Game.Sound.PlayEffect(SoundEffect.BossHit);
        Game.Sound.StopEffect(StopEffect.AmbientInstance);

        var slot = Game.World.curObjectSlot;
        Actor? obj = null;

        slot = slot >= TailSlot2 ? TailSlot2 : TailSlot1;

        for (; ; slot++)
        {
            obj = Game.World.GetObject(slot);
            if (obj != null && obj.GetType() == GetType())
                break;
        }

        if (slot == HeadSlot1 || slot == HeadSlot2)
            return;

        HP = 0x20;
        ShoveDirection = 0;
        ShoveDistance = 0;
        Decoration = 0;

        var dummy = new DeadDummyActor(Game, X, Y);
        Game.World.SetObject(slot, dummy);
    }

    protected override void UpdateTurnImpl()
    {
        var slot = Game.World.curObjectSlot;
        if (slot != HeadSlot1 && slot != HeadSlot2)
            return;

        base.UpdateTurnImpl();
        UpdateSubstates();
    }

    protected override void UpdateChaseImpl()
    {
        var slot = Game.World.curObjectSlot;
        if (slot != HeadSlot1 && slot != HeadSlot2)
            return;

        base.UpdateChaseImpl();
        UpdateSubstates();
    }

    void UpdateSubstates()
    {
        if (ObjTimer == 0)
        {
            int r = Random.Shared.GetByte();
            if (r < 0x40)
                GoToState(3, 8);
            else
                GoToState(2, 8);

            ObjTimer = 0x10;

            // This is the head, so all other parts are at lower indexes.
            var slot = Game.World.curObjectSlot;
            var prevSlot = slot - 1;

            var obj = Game.World.GetObject(prevSlot);
            if (obj != null && obj is MoldormActor && obj.Facing != Direction.None)
                ShiftFacings();
        }
        else
        {
            ShiftFacings();
        }
    }

    void ShiftFacings()
    {
        if (ObjTimer != 0x10)
            return;

        if (deferredDir != Direction.None)
        {
            Facing = deferredDir;
            deferredDir = Direction.None;
        }

        var slot = Game.World.curObjectSlot - 4;

        for (var i = 0; i < 4; i++, slot++)
        {
            var curObj = Game.World.GetObject<MoldormActor>(slot);
            var nextObj = Game.World.GetObject< MoldormActor>(slot + 1);

            if (curObj == null || nextObj == null )
                continue;

            var curMoldorm = curObj;
            var nextMoldorm = nextObj;

            var nextOldFacing = nextMoldorm.oldFacing;
            curMoldorm.oldFacing = nextOldFacing;
            curMoldorm.Facing = nextOldFacing;
        }

        oldFacing = Facing;
    }

    protected override int GetFrame()
    {
        return 0;
    }
}

internal enum PatraType { Circle, Spin }

internal sealed class PatraActor : FlyingActor
{
    private static readonly AnimationId[] patraAnimMap = new[]
    {
        AnimationId.B3_Patra,
        AnimationId.B3_Patra,
        AnimationId.B3_Patra,
        AnimationId.B3_Patra
    };

    private static readonly FlyerSpec patraSpec = new(patraAnimMap, TileSheet.Boss, Palette.Blue, 0x40);

    int xMove;
    int yMove;
    int maneuverState;
    int childStateTimer;

    public static int[] patraAngle = new int[9];
    public static int[] patraState = new int[9];

    public override bool IsReoccuring => false;
    public PatraActor(Game game, PatraType type, int x, int y) : base(game, GetPatraType(type), patraSpec, x, y)
    {


        InvincibilityMask = 0xFE;
        Facing = Direction.Up;
        curSpeed = 0x1F;

        Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);

        Array.Fill(patraAngle, 0);
        Array.Fill(patraState, 0);
    }

    private static ObjType GetPatraType(PatraType type)
    {
        return type switch
        {
            PatraType.Circle => ObjType.Patra1,
            PatraType.Spin => ObjType.Patra2,
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    public int GetXMove()
    {
        return xMove;
    }

    public int GetYMove()
    {
        return yMove;
    }

    public int GetManeuverState()
    {
        return maneuverState;
    }

    public override void Update()
    {
        if (childStateTimer > 0)
            childStateTimer--;

        int origX = X;
        int origY = Y;

        UpdateStateAndMove();

        xMove = X - origX;
        yMove = Y - origY;

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

        if (childStateTimer == 0 && patraAngle[2] == 0)
        {
            maneuverState ^= 1;
            // ORIGINAL: I don't see how this is ever $50. See Patra's Update routine.
            childStateTimer = 0xFF;
        }
    }

    protected override void UpdateFullSpeedImpl()
    {
        int r = Random.Shared.GetByte();

        GoToState(r >= 0x40 ? 2 : 3, 8);
    }
}

internal sealed class PatraChildActor : Actor
{
    private static readonly byte[] patraEntryAngles = new byte[] { 0x14, 0x10, 0xC, 0x8, 0x4, 0, 0x1C };
    private static readonly int[] shiftCounts = new int[] { 6, 5, 6, 6 };
    private static readonly byte[] sinCos = new byte[]
        { 0x00, 0x18, 0x30, 0x47, 0x5A, 0x6A, 0x76, 0x7D, 0x80, 0x7D, 0x76, 0x6A, 0x5A, 0x47, 0x30, 0x18 };

    int x;
    int y;
    SpriteAnimator Animator;

    int angleAccum;

    public override bool IsReoccuring => false;
    public PatraChildActor(Game game, PatraType type, int x, int y) : base(game, x, y)
    {
        ObjType = type switch
        {
            PatraType.Circle => ObjType.PatraChild1,
            PatraType.Spin => ObjType.PatraChild2,
            _ => throw new ArgumentOutOfRangeException(),
        };
        InvincibilityMask = 0xFE;

        Animator = new() {
            DurationFrames = 4,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B3_PatraChild)
        };
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
        var slot = Game.World.curObjectSlot;

        if (PatraActor.patraState[(int)slot] == 0)
        {
            UpdateStart();
        }
        else
        {
            UpdateTurn();
            Animator.Advance();

            if (PatraActor.patraState[0] != 0)
            {
                CheckCollisions();
                if (Decoration != 0)
                {
                    var dummy = new DeadDummyActor(Game, X, Y);
                    Game.World.SetObject(slot, dummy);
                }
            }
        }
    }

    public override void Draw()
    {
        var slot = Game.World.curObjectSlot;

        if (PatraActor.patraState[(int)slot] != 0)
        {
            var pal = CalcPalette(Palette.Red);
            Animator.Draw(TileSheet.Boss, X, Y, pal);
        }
    }

    void UpdateStart()
    {
        var slot = Game.World.curObjectSlot;

        if (slot != (ObjectSlot)1)
        {
            if (PatraActor.patraState[1] == 0)
                return;

            var index = slot - 2;
            if (PatraActor.patraAngle[1] != patraEntryAngles[(int)index])
                return;
        }

        var patra = Game.World.GetObject<PatraActor>(0) ?? throw new Exception();
        int distance = ObjType == ObjType.PatraChild1 ? 0x2C : 0x18;

        if (slot == (ObjectSlot)8)
            PatraActor.patraState[0] = 1;
        PatraActor.patraState[(int)slot] = 1;
        PatraActor.patraAngle[(int)slot] = 0x18;

        x = patra.X << 8;
        y = (patra.Y - distance) << 8;

        X = x >> 8;
        Y = y >> 8;
    }

    void UpdateTurn()
    {
        var slot = Game.World.curObjectSlot;
        var patra = Game.World.GetObject<PatraActor>(0) ?? throw new Exception();

        x += patra.GetXMove() << 8;
        y += patra.GetYMove() << 8;

        int step = ObjType == ObjType.PatraChild1 ? 0x70 : 0x60;
        short angleFix = (short)((PatraActor.patraAngle[(int)slot] << 8) | angleAccum);
        angleFix -= (short)step;
        angleAccum = angleFix & 0xFF;
        PatraActor.patraAngle[(int)slot] = (angleFix >> 8) & 0x1F;


    int yShiftCount;
    int xShiftCount;
    int index = patra.GetManeuverState();

    if (ObjType == ObjType.PatraChild1)
    {
        yShiftCount = shiftCounts[index];
        xShiftCount = shiftCounts[index + 2];
    }
    else
    {
        yShiftCount = shiftCounts[index + 1];
        xShiftCount = yShiftCount;
    }

    const int TurnSpeed = 0x20;


    index = PatraActor.patraAngle[(int)slot] & 0xF;
    byte cos = sinCos[index];
    short n = ShiftMult(cos, TurnSpeed, xShiftCount);

    if ((PatraActor.patraAngle[(int)slot] & 0x18) < 0x10)
        x += n;
    else
        x -= n;

    index = (PatraActor.patraAngle[(int)slot] + 8) & 0xF;
    byte sin = sinCos[index];
    n = ShiftMult(sin, TurnSpeed, yShiftCount);

    if (((PatraActor.patraAngle[(int)slot] - 8) & 0x18) < 0x10)
        y += n;
    else
        y -= n;

    X = x >> 8;
    Y = y >> 8;
    }

}

internal readonly record struct JumperSpec(AnimationId[] AnimationMap, int AnimationTimer, int JumpFrame, Palette Palette, int Speed, byte[] AccelMap);

internal abstract class JumperActor : Actor
{
    public static readonly int[] jumperStartDirs = new[] { 1, 2, 5, 0xA };

    static readonly int[] targetYOffset = new[] { 0, 0, 0, 0, 0, 0x20, 0x20, 0, 0, -0x20, -0x20, };

    int curSpeed;
    int accelStep;
    SpriteAnimator Animator;

    int state;
    int targetY;
    int reversesPending;

    protected abstract JumperSpec spec { get; }

    protected JumperActor(Game game, int x, int y) : this(game, ObjType.None, x, y) { }

    protected JumperActor(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
        Animator = new() {
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Npcs, spec.AnimationMap[0]),
            DurationFrames = spec.AnimationTimer
        };

        int r = Random.Shared.Next(4);
        Facing = (Direction)jumperStartDirs[r];
        ObjTimer = (byte)((int)Facing * 4);

        if (this is BoulderActor)
        {
            BouldersActor.Count++;
            Decoration = 0;
        }
    }

    // JOE: TODO: Jumper.~Jumper()
    // JOE: TODO: {
    // JOE: TODO:     if (GetType() == ObjType.Boulder)
    // JOE: TODO:         Boulders.Count()--;
    // JOE: TODO: }

    public override void Update()
    {
        if (ShoveDirection == 0 && !IsStunned)
        {
            if (state == 0)
                UpdateStill();
            else
                UpdateJump();
        }

        if (this is BoulderActor)
        {
            Animator.Advance();
            CheckPlayerCollision();
            if (Y >= World.WorldLimitBottom)
                IsDeleted = true;
        }
        else
        {
            if (state == 0 && ObjTimer >= 0x21)
                Animator.Advance();
            CheckCollisions();
        }
    }

    public override void Draw()
    {
        var pal = CalcPalette(spec.Palette);

        if (state == 1 && spec.JumpFrame >= 0)
        {
            Animator.DrawFrame(TileSheet.Npcs, X, Y, pal, spec.JumpFrame);
        }
        else
        {
            Animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    void UpdateStill()
    {
        if (ObjTimer != 0)
            return;

        state = 1;
        Facing = TurnTowardsPlayer8(X, Y, Facing);

        if ((Facing & (Direction.Right | Direction.Left)) == 0)
        {
            Facing = Facing | GetXDirToPlayer(X);
        }

        SetupJump();
    }

    void UpdateJump()
    {
        var dir = CheckWorldMarginH(X, Facing, false);
        if (this is BoulderActor)
            dir = CheckWorldMarginV(Y, dir, false);

        if (dir == Direction.None)
        {
            Facing = Facing.GetOppositeDir8();
            reversesPending++;
            SetupJump();
            return;
        }

        ConstrainFacing();
        reversesPending = 0;
        int acceleration = spec.AccelMap[(int)Facing];

        UpdateY(2, acceleration);
        if ((Facing & Direction.Left) != 0)
            X--;
        else if ((Facing & Direction.Right) != 0)
            X++;

        if (curSpeed >= 0 && Math.Abs(Y - targetY) < 3)
        {
            state = 0;
            if (this is BoulderActor) // JOE: TODO: Is this right?
                ObjTimer = 0;
            else
                ObjTimer = (byte)GetRandomStillTime();
        }
    }

    void UpdateY(int maxSpeed, int acceleration)
    {
        Y += curSpeed;
        accelStep += acceleration;

        int carry = accelStep >> 8;
        accelStep &= 0xFF;

        curSpeed += carry;

        if (curSpeed >= maxSpeed && accelStep >= 0x80)
        {
            curSpeed = maxSpeed;
            accelStep = 0;
        }
    }

    void SetupJump()
    {
        if (reversesPending >= 2)
        {
            Facing = Facing ^ (Direction.Right | Direction.Left);
            reversesPending = 0;
        }

        ConstrainFacing();
        targetY = Y + targetYOffset[(int)Facing];
        curSpeed = spec.Speed;
        accelStep = 0;
    }

    int GetRandomStillTime()
    {
        byte r = Random.Shared.GetByte();
        byte t = (byte)(r + 0x10);

        if (t < 0x20)
            t -= 0x40;
        if (this is TektiteActor tektite && tektite.Color == ActorColor.Blue)
        {
            t &= 0x7F;
            if (r >= 0xA0)
                t &= 0x0F;
        }
        return t;
    }

    void ConstrainFacing()
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
    private static readonly AnimationId[] boulderAnimMap = new[]
    {
        AnimationId.OW_Boulder,
        AnimationId.OW_Boulder,
        AnimationId.OW_Boulder,
        AnimationId.OW_Boulder
    };

    private static readonly byte[] boulderSpeeds = new byte[]
    {
        0x60, 0x60,
        0x60, 0x60, 0x60, 0x60, 0x60
    };

    private static readonly JumperSpec boulderSpec = new(boulderAnimMap, 12, -1, Palette.Red, -2, boulderSpeeds);

    protected override JumperSpec spec => boulderSpec;

    public BoulderActor(Game game, int x, int y) : base(game, x, y)
    {
    }
}


internal sealed class TektiteActor : JumperActor
{
    private static readonly AnimationId[] tektiteAnimMap = new[]
    {
        AnimationId.OW_Tektite,
        AnimationId.OW_Tektite,
        AnimationId.OW_Tektite,
        AnimationId.OW_Tektite
    };

    private static readonly byte[] blueTektiteSpeeds = new byte[] { 0, 0x40, 0x40, 0, 0, 0x40, 0x40, 0, 0, 0x30, 0x30 };
    private static readonly byte[] redTektiteSpeeds = new byte[] { 0, 0x80, 0x80, 0, 0, 0x80, 0x80, 0, 0, 0x50, 0x50 };

    private static readonly JumperSpec blueTektiteSpec = new(tektiteAnimMap, 32, 1, Palette.Blue, -3, blueTektiteSpeeds);
    private static readonly JumperSpec redTektiteSpec = new(tektiteAnimMap, 32, 1, Palette.Red, -4, redTektiteSpeeds);

    protected override JumperSpec spec => ObjType switch {
        ObjType.BlueTektite => blueTektiteSpec,
        ObjType.RedTektite => redTektiteSpec,
        _ => throw new ArgumentOutOfRangeException(nameof(Color))
    };

    private TektiteActor(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
        if (type is not (ObjType.BlueTektite or ObjType.RedTektite))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    public static TektiteActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new TektiteActor(game, ObjType.BlueTektite, x, y),
            ActorColor.Red => new TektiteActor(game, ObjType.RedTektite, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(color))
        };
    }
}

internal sealed class BouldersActor : Actor
{
    const int MaxBoulders = 3;

    public static int Count;

    public BouldersActor(Game game, int x, int y) : base(game, x, y)
    {
        int r = Random.Shared.Next(4);
        int Facing = JumperActor.jumperStartDirs[r];
        ObjTimer = (byte)(Facing * 4);
        Decoration = 0;
    }

    public override void Update()
    {
        if (ObjTimer == 0)
        {
            if (Count < MaxBoulders)
            {
                Point playerPos = Game.World.GetObservedPlayerPos();
                int y = World.WorldLimitTop;
                int x = Random.Shared.GetByte();

                // Make sure the new boulder is in the same half of the screen.
                if (playerPos.X < World.WorldMidX)
                    x = x % 0x80;
                else
                    x = x | 0x80;

                var slot = Game.World.FindEmptyMonsterSlot();
                if (slot >= 0)
                {
                    var obj = FromType(ObjType.Boulder, Game, x, y);
                    Game.World.SetObject(slot, obj);

                    ObjTimer = (byte)Random.Shared.Next(32);
                }
            }
            else
            {
                int r = Random.Shared.GetByte();
                ObjTimer = (byte)((ObjTimer + r) % 256);
            }
        }
    }

    public override void Draw()
    {
    }

    public static void ClearRoomData() => Count = 0;
}

internal sealed class TrapActor : Actor
{
    private static readonly Point[] trapPos = new[]
    {
        new Point(0x20, 0x60),
        new Point(0x20, 0xC0),
        new Point(0xD0, 0x60),
        new Point(0xD0, 0xC0),
        new Point(0x40, 0x90),
        new Point(0xB0, 0x90),
    };

    private static readonly int[] trapAllowedDirs = new int[] { 5, 9, 6, 0xA, 1, 2 };

    SpriteImage image;

    int trapIndex;
    int state;
    int speed;
    int origCoord;

    public override bool CountsAsLiving => false;
    private TrapActor(Game game, int trapIndex, int x, int y) : base(game, x, y)
    {
        this.trapIndex = trapIndex;
        image = new() {
            Animation = Graphics.GetAnimation(TileSheet.Npcs, AnimationId.UW_Trap)
        };
    }

    public static TrapActor MakeSet(Game game, int count)
    {
        Debug.Assert(count is >= 1 and <= 6);
        if (count < 1)
            count = 1;
        if (count > 6)
            count = 6;

        var slot = ObjectSlot.Monster1;

        for (var i = 0; i < count; i++, slot++)
        {
            var obj = new TrapActor(game, i, trapPos[i].X, trapPos[i].Y);
            game.World.SetObject(slot, obj);
        }

        return game.World.GetObject<TrapActor>(ObjectSlot.Monster1) ?? throw new Exception();;
    }

    public override void Update()
    {
        if (state == 0)
            UpdateIdle();
        else
            UpdateMoving();

        CheckCollisions();
    }

    void UpdateIdle()
    {
        var player = Game.Link;
        int playerX = player.X;
        int playerY = player.Y;
        Direction dir = Direction.None;
        int distX = Math.Abs(playerX - X);
        int distY = Math.Abs(playerY - Y);

        if (distY >= 0xE)
        {
            if (distX < 0xE)
            {
                dir = playerY < Y ? Direction.Up : Direction.Down;
                origCoord = Y;
            }
        }
        else
        {
            if (distX >= 0xE)
            {
                dir = playerX < X ? Direction.Left : Direction.Right;
                origCoord = X;
            }
        }

        if (dir != Direction.None)
        {
            if ((dir & (Direction)trapAllowedDirs[trapIndex]) != 0)
            {
                Facing = dir;
                state++;
                speed = 0x70;
            }
        }
    }

    void UpdateMoving()
    {
        MoveDirection(speed, Facing);

        if ((TileOffset & 0xF) == 0)
            TileOffset &= 0xF;

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

        if (state == 1)
        {
            if (Math.Abs(coord - limit) < 5)
            {
                Facing = Facing.GetOppositeDirection();
                speed = 0x20;
                state++;
            }
        }
        else
        {
            if (coord == origCoord)
                state = 0;
        }
    }

    public override void Draw()
    {
        image.Draw(TileSheet.Npcs, X, Y, Palette.Blue);
    }
}

internal sealed class RopeActor : Actor
{
    private static readonly AnimationId[] ropeAnimMap = new[]
    {
        AnimationId.UW_Rope_Right,
        AnimationId.UW_Rope_Left,
        AnimationId.UW_Rope_Right,
        AnimationId.UW_Rope_Right
    };

    const int RopeNormalSpeed = 0x20;
    const int RopeFastSpeed = 0x60;

    SpriteAnimator Animator;

    int speed;

    public RopeActor(Game game, int x, int y) : base(game, ObjType.Rope, x, y)
    {
        Animator = new() {
            Time = 0,
            DurationFrames = 20
        };

        InitCommonFacing();
        SetFacingAnimation();

        var profile = Game.World.GetProfile();

        HP = (byte)(profile.Quest == 0 ? 0x10 : 0x40);
    }

    public override void Update()
    {
        Direction origFacing = Facing;

        MovingDirection = Facing;

        if (!IsStunned)
        {
            ObjMove(speed);

            if ((TileOffset & 0xF) == 0)
                TileOffset &= 0xF;

            if (speed != RopeFastSpeed && ObjTimer == 0)
            {
                ObjTimer = (byte)Random.Shared.Next(0x40);
                TurnToUnblockedDir();
            }
        }

        if (Facing != origFacing)
            speed = RopeNormalSpeed;

        TargetPlayer();

        Animator.Advance();

        CheckCollisions();
        SetFacingAnimation();
    }

    void TargetPlayer()
    {
        if (speed != RopeNormalSpeed || TileOffset != 0)
            return;

        var player = Game.Link;

        int xDist = Math.Abs(player.X - X);
        if (xDist < 8)
        {
            Facing = player.Y < Y ? Direction.Up : Direction.Down;
            speed = RopeFastSpeed;
        }
        else
        {
            int yDist = Math.Abs(player.Y - Y);
            if (yDist < 8)
            {
                Facing = player.X < X ? Direction.Left : Direction.Right;
                speed = RopeFastSpeed;
            }
        }
    }

    public override void Draw()
    {
        var profile = Game.World.GetProfile();
        Palette pal;

        if (profile.Quest == 0)
            pal = CalcPalette(Palette.Red);
        else
            pal = Palette.Player + (Game.GetFrameCounter() & 3);

        Animator.Draw(TileSheet.Npcs, X, Y, pal);
    }

    void SetFacingAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, ropeAnimMap[dirOrd]);
    }
}


internal sealed class PolsVoiceActor : Actor
{
    private static readonly int[] polsVoiceXSpeeds = new int[] { 1, -1, 0, 0 };
    private static readonly int[] polsVoiceYSpeeds = new int[] { 0, 0, 1, -1 };
    private static readonly int[] polsVoiceJumpSpeeds = new int[] { -3, -3, -1, -4 };
    private static readonly int[] polsVoiceJumpLimits = new int[] { 0, 0, 0x20, -0x20 };

    int curSpeed;
    int accelStep;
    int state;
    int stateTimer;
    int targetY;
    SpriteAnimator Animator;

    public PolsVoiceActor(Game game, int x, int y) : base(game, ObjType.PolsVoice, x, y) {
        InitCommonFacing();

        Animator = new() {
            Animation = Graphics.GetAnimation(TileSheet.Npcs, AnimationId.UW_PolsVoice),
            DurationFrames = 16,
            Time = 0
        };
    }

    public override void Update()
    {
        if (!IsStunned && (Game.GetFrameCounter() & 1) == 0)
            Move();

        Animator.Advance();
        InvincibilityMask = 0xFE;
        CheckCollisions();
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.Player);
        Animator.Draw(TileSheet.Npcs, X, Y, pal);
    }

    void Move()
    {
        UpdateX();
        if (!UpdateY())
            return;

        TileCollision collision;
        int x = X;
        int y = Y;

        collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
        {
            x += 0xE;
            y += 6;
            collision = Game.World.CollidesWithTileStill(x, y);
            if (!collision.Collides)
                return;
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

    void UpdateX()
    {
        int ord = Facing.GetOrdinal();
        X += polsVoiceXSpeeds[ord];
    }

    bool UpdateY()
    {
        return state == 1 ? UpdateJumpY() : UpdateWalkY();
    }

    bool UpdateJumpY()
    {
        const int Acceleration = 0x38;

        accelStep += Acceleration;

        int carry = accelStep >> 8;
        accelStep &= 0xFF;

        curSpeed += carry;
        Y += curSpeed;

        if (curSpeed >= 0 && Y >= targetY)
        {
            state = 0;
            curSpeed = 0;
            accelStep = 0;
            int r = Random.Shared.GetByte();
            Facing = (r & 3).GetOrdDirection();
            stateTimer = (r & 0x40) + 0x30;
            X = (X + 8) & 0xF0;
            Y = (Y + 8) & 0xF0;
            Y -= 3;
        }
        return true;
    }

    bool UpdateWalkY()
    {
        if (stateTimer == 0)
        {
            SetupJump();
            return false;
        }

        stateTimer--;
        int ord = Facing.GetOrdinal();
        Y += polsVoiceYSpeeds[ord];
        return true;
    }

    void SetupJump()
    {
        if (state != 0)
            return;

        int dirOrd = Facing.GetOrdinal();

        if (Y < 0x78)
            dirOrd = 2;
        else if (Y >= 0xA8)
            dirOrd = 3;

        curSpeed = polsVoiceJumpSpeeds[dirOrd];
        targetY = Y + polsVoiceJumpLimits[dirOrd];

        Facing = dirOrd.GetOrdDirection();
        state = 1;
    }
}

internal sealed class RedWizzrobeActor : Actor
{
    private static readonly Direction[] wizzrobeDirs = new[]
    {
        Direction.Down,
        Direction.Up,
        Direction.Right,
        Direction.Left
    };

    private static readonly int[] wizzrobeXOffsets = new[]
    {
        0x00, 0x00, -0x20, 0x20, 0x00, 0x00, -0x40, 0x40,
        0x00, 0x00, -0x30, 0x30, 0x00, 0x00, -0x50, 0x50
    };

    private static readonly int[] wizzrobeYOffsets = new[]
    {
        -0x20, 0x20, 0x00, 0x00, -0x40, 0x40, 0x00, 0x00,
        -0x30, 0x30, 0x00, 0x00, -0x50, 0x50, 0x00, 0x00
    };

    private static readonly int[] allWizzrobeCollisionXOffsets = new[] { 0xF, 0, 0, 4, 8, 0, 0, 4, 8, 0 };
    private static readonly int[] allWizzrobeCollisionYOffsets = new[] { 4, 4, 0, 8, 8, 8, 0, -8, 0, 0 };

    SpriteAnimator Animator;

    byte stateTimer;
    byte flashTimer;

    Action[] sStateFuncs;

    public RedWizzrobeActor(Game game, int x, int y) : base(game, ObjType.RedWizzrobe, x, y)
    {
        Decoration = 0;
        Animator = new() {
            DurationFrames = 8,
            Time = 0
        };

        sStateFuncs = new Action[]
        {
           UpdateHidden,
           UpdateGoing,
           UpdateVisible,
           UpdateComing,
        };
    }

    public override void Update()
    {
        if (Game.World.GetItem(ItemSlot.Clock) != 0)
        {
            Animator.Advance();
            CheckRedWizzrobeCollisions();
            return;
        }

        stateTimer--;

        var state = GetState();

        sStateFuncs[state]();

        Animator.Advance();
    }

    public override void Draw()
    {
        var state = GetState();

        if (state == 2 || (state > 0 && (flashTimer & 1) == 0))
        {
            var pal = CalcPalette(Palette.Red);
            Animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    int GetState()
    {
        return stateTimer >> 6;
    }

    void SetFacingAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, BlueWizzrobeBase.wizzrobeAnimMap[dirOrd]);
    }

    void UpdateHidden()
    {
        // Nothing to do
    }

    void UpdateGoing()
    {
        if (stateTimer == 0x7F)
            stateTimer = 0x4F;

        flashTimer++;

        if ((flashTimer & 1) == 0)
            CheckRedWizzrobeCollisions();
    }

    void UpdateVisible()
    {
        if (stateTimer == 0xB0)
        {
            if (Game.World.GetItem(ItemSlot.Clock) == 0)
            {
                Game.Sound.PlayEffect(SoundEffect.MagicWave);
                Shoot(ObjType.MagicWave2, X, Y, Facing);
            }
        }

        CheckRedWizzrobeCollisions();
    }

    int CheckWizzrobeTileCollision(int x, int y, Direction dir)
    {
        var ord = dir - 1;
        x += allWizzrobeCollisionXOffsets[(int)ord];
        y += allWizzrobeCollisionYOffsets[(int)ord];

        TileCollision collision;

        collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
            return 0;

        // This isn't quite the same as the original game, because the original contrasted
        // blocks and water together with everything else.

        if (World.CollidesWall(collision.TileBehavior))
            return 1;

        return 2;
    }

    void UpdateComing()
    {
        if (stateTimer == 0xFF)
        {
            var player = Game.Link;

            var r = Random.Shared.Next(16);
            int dirOrd = r % 4;
            Facing = wizzrobeDirs[dirOrd];

            X = (player.X + wizzrobeXOffsets[r]) & 0xF0;
            Y = (player.Y + wizzrobeYOffsets[r] + 3) & 0xF0 - 3;

            if (Y < 0x5D || Y >= 0xC4)
            {
                stateTimer++;    // Try again
            }
            else
            {
                int collisionResult = CheckWizzrobeTileCollision(X, Y, Facing);

                if (collisionResult != 0)
                    stateTimer++;    // Try again
            }

            if (stateTimer != 0)
                SetFacingAnimation();
        }
        else
        {
            if (stateTimer == 0x7F)
                stateTimer = 0x4F;

            flashTimer++;
            if ((flashTimer & 1) == 0)
                CheckRedWizzrobeCollisions();
        }
    }

    void CheckRedWizzrobeCollisions()
    {
        // If I really wanted, I could make a friend function or class to do this, which is the same
        // as in BlueWizzrobe.

        InvincibilityMask = 0xF6;
        if (InvincibilityTimer == 0)
        {
            CheckWave(ObjectSlot.PlayerSwordShot);
            CheckBombAndFire(ObjectSlot.Bomb);
            CheckBombAndFire(ObjectSlot.Bomb2);
            CheckBombAndFire(ObjectSlot.Fire);
            CheckBombAndFire(ObjectSlot.Fire2);
            CheckSword(ObjectSlot.PlayerSwordShot);
        }
        CheckPlayerCollision();
    }
}

internal abstract class BlueWizzrobeBase : WizzrobeBase
{
    public static readonly AnimationId[] wizzrobeAnimMap = new[]
    {
        AnimationId.UW_Wizzrobe_Right,
        AnimationId.UW_Wizzrobe_Left,
        AnimationId.UW_Wizzrobe_Right,
        AnimationId.UW_Wizzrobe_Up
    };

    private static readonly int[] blueWizzrobeXSpeeds = new int[] { 0, 1, -1, 0, 0, 1, -1, 0, 0, 1, -1 };
    private static readonly int[] blueWizzrobeYSpeeds = new int[] { 0, 0, 0, 0, 1, 1, 1, 0, -1, -1, -1 };

    protected byte flashTimer;
    protected byte turnTimer;

    public BlueWizzrobeBase(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
    }

    void TruncatePosition()
    {
        X = (X + 8) & 0xF0;
        Y = (Y + 8) & 0xF0;
        Y -= 3;
    }

    protected void MoveOrTeleport()
    {
        if (ObjTimer != 0)
        {
            if (ObjTimer >= 0x10)
            {
                if ((Game.GetFrameCounter() & 1) == 1)
                {
                    TurnIfNeeded();
                }
                else
                {
                    turnTimer++;
                    TurnIfNeeded();
                    MoveAndCollide();
                }
            }
            else if (ObjTimer == 1)
                TryTeleporting();
            return;
        }

        if (flashTimer == 0)
        {
            int r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);
            TruncatePosition();
            Turn();
            return;
        }

        flashTimer--;
        MoveAndCollide();
    }

    protected void MoveAndCollide()
    {
        Move();

        int collisionResult = CheckWizzrobeTileCollision(X, Y, Facing);

        if (collisionResult == 1)
        {
            if ((Facing & (Direction)0xC) != 0)
                Facing = (Direction)(Facing ^ (Direction)0xC);
            if ((Facing & (Direction)3) != 0)
                Facing = (Direction)(Facing ^ (Direction)3);

            Move();
        }
        else if (collisionResult == 2)
        {
            if (flashTimer == 0)
            {
                flashTimer = 0x20;
                turnTimer ^= 0x40;
                ObjTimer = 0;
                TruncatePosition();
            }
        }
    }

    void Move()
    {
        X += blueWizzrobeXSpeeds[(int)Facing];
        Y += blueWizzrobeYSpeeds[(int)Facing];
    }

    protected void TryShooting()
    {
        if (Game.World.GetItem(ItemSlot.Clock) != 0)
            return;
        if (flashTimer != 0)
            return;
        if ((Game.GetFrameCounter() % 0x20) != 0)
            return;

        var player = Game.Link;
        Direction dir;

        if ((player.Y & 0xF0) != (Y & 0xF0))
        {
            if (player.X != (X & 0xF0))
                return;

            dir = GetYDirToTruePlayer(Y);
        }
        else
        {
            dir = GetXDirToTruePlayer(X);
        }

        if (dir != Facing)
            return;

        Game.Sound.PlayEffect(SoundEffect.MagicWave);
        Shoot(ObjType.MagicWave, X, Y, Facing);
    }

    protected void TurnIfNeeded()
    {
        if ((turnTimer & 0x3F) == 0)
            Turn();
    }

    void Turn()
    {
        Direction dir;

        if ((turnTimer & 0x40) != 0)
        {
            dir = GetYDirToTruePlayer(Y);
        }
        else
        {
            dir = GetXDirToTruePlayer(X);
        }

        if (dir == Facing)
            return;

        Facing = dir;
        TruncatePosition();
    }

    private static readonly int[] blueWizzrobeTeleportXOffsets = new[] { -0x20, 0x20, -0x20, 0x20 };
    private static readonly int[] blueWizzrobeTeleportYOffsets = new[] { -0x20, -0x20, 0x20, 0x20 };
    private static readonly int[] blueWizzrobeTeleportDirs = new[] { 0xA, 9, 6, 5 };

    void TryTeleporting()
    {
        int index = Random.Shared.Next(4);

        int teleportX = X + blueWizzrobeTeleportXOffsets[index];
        int teleportY = Y + blueWizzrobeTeleportYOffsets[index];
        Direction dir = (Direction)blueWizzrobeTeleportDirs[index];

        int collisionResult = CheckWizzrobeTileCollision(teleportX, teleportY, dir);

        if (collisionResult != 0)
        {
            int r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);
        }
        else
        {
            Facing = dir;

            flashTimer = 0x20;
            turnTimer ^= 0x40;
            ObjTimer = 0;
        }

        TruncatePosition();
    }
}

internal sealed class BlueWizzrobeActor : BlueWizzrobeBase
{
    readonly SpriteAnimator Animator;

    public BlueWizzrobeActor(Game game, int x, int y) : base(game, ObjType.BlueWizzrobe, x, y)
    {
        Animator = new() {
            DurationFrames = 16,
            Time = 0
        };
    }

    public override void Update()
    {
        if (Game.World.GetItem(ItemSlot.Clock) != 0)
        {
            AnimateAndCheckCollisions();
            return;
        }

        Direction origFacing = Facing;

        MoveOrTeleport();
        TryShooting();

        if ((flashTimer & 1) == 0)
            AnimateAndCheckCollisions();
        SetFacingAnimation();
    }

    public override void Draw()
    {
        if ((flashTimer & 1) == 0 && Facing != Direction.None)
        {
            var pal = CalcPalette(Palette.Blue);
            Animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    void SetFacingAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, wizzrobeAnimMap[dirOrd]);
    }

    void AnimateAndCheckCollisions()
    {
        Animator.Advance();

        // If I really wanted, I could make a friend function or class to do this, which is the same
        // as in RedWizzrobe.

        InvincibilityMask = 0xF6;
        if (InvincibilityTimer == 0)
        {
            CheckWave(ObjectSlot.PlayerSwordShot);
            CheckBombAndFire(ObjectSlot.Bomb);
            CheckBombAndFire(ObjectSlot.Bomb2);
            CheckBombAndFire(ObjectSlot.Fire);
            CheckBombAndFire(ObjectSlot.Fire2);
            CheckSword(ObjectSlot.PlayerSwordShot);
        }
        CheckPlayerCollision();
    }
}

internal sealed class LamnolaActor : Actor
{
    private const ObjectSlot HeadSlot1 = ObjectSlot.Monster1 + 4;
    private const ObjectSlot HeadSlot2 = HeadSlot1 + 5;
    private const ObjectSlot TailSlot1 = ObjectSlot.Monster1;
    private const ObjectSlot TailSlot2 = TailSlot1 + 5;

    readonly SpriteImage image;

    private LamnolaActor(Game game, ActorColor color, bool isHead, int x, int y) : base(game, x, y)
    {
        ObjType = color switch
        {
            ActorColor.Red => ObjType.RedLamnola,
            ActorColor.Blue => ObjType.BlueLamnola,
            _ => throw new ArgumentOutOfRangeException(nameof(color))
        };

        var animationId = isHead ? AnimationId.UW_LanmolaHead : AnimationId.UW_LanmolaBody;
        image = new() {
            Animation = Graphics.GetAnimation(TileSheet.Npcs, animationId)
        };
    }

    public static LamnolaActor MakeSet(Game game, ActorColor color)
    {
        const int Y = 0x8D;

        for (int i = 0; i < 5 * 2; i++)
        {
            bool isHead = (i == 4) || (i == 9);
            var lamnola = new LamnolaActor(game, color, isHead, 0x40, Y);
            game.World.SetObject((ObjectSlot)i, lamnola);
        }

        var head1 = game.World.GetObject<LamnolaActor>((ObjectSlot)4) ?? throw new Exception();
        var head2 = game.World.GetObject<LamnolaActor>((ObjectSlot)9) ?? throw new Exception();

        head1.Facing = Direction.Up;
        head2.Facing = Direction.Up;

        game.World.RoomObjCount = 8;

        return game.World.GetObject<LamnolaActor>(0) ?? throw new Exception();
    }

    public override void Update()
    {
        if (Facing == Direction.None)
            return;

        if (Game.World.GetItem(ItemSlot.Clock) == 0)
        {
            int speed = ObjType - ObjType.RedLamnola + 1;
            var slot = Game.World.curObjectSlot;

            MoveSimple(Facing, speed);

            if (slot == HeadSlot1 || slot == HeadSlot2)
                UpdateHead();
        }

        CheckLamnolaCollisions();
    }

    public override void Draw()
    {
        var pal = ObjType == ObjType.RedLamnola ? Palette.Red : Palette.Blue;
        pal = CalcPalette(pal);
        int xOffset = (16 - image.Animation.Width) / 2;
        image.Draw(TileSheet.Npcs, X + xOffset, Y, pal);
    }

    void UpdateHead()
    {
        const uint Adjustment = 3;

        if ((X & 7) != 0 || ((Y + Adjustment) & 7) != 0)
            return;

        var slot = Game.World.curObjectSlot;

        for (var i = slot - 4; i < slot; i++)
        {
            var lamnola1 = Game.World.GetObject< LamnolaActor>(i);
            var lamnola2 = Game.World.GetObject< LamnolaActor>(i + 1);

            if (lamnola1 != null && lamnola2 != null)
                lamnola1.Facing = lamnola2.Facing;
        }

        if ((X & 0xF) != 0 || ((Y + Adjustment) & 0xF) != 0)
            return;

        Turn();
    }

    void Turn()
    {
        Direction oppositeDir = Facing.GetOppositeDirection();
        var dirMask = ~oppositeDir;
        int r = Random.Shared.GetByte();
        Direction dir;

        if (r < 128)
        {
            Direction xDir = GetXDirToTruePlayer(X);
            Direction yDir = GetYDirToTruePlayer(Y);

            if ((xDir & dirMask) == 0 || (xDir & Facing) == 0)
                dir = yDir;
            else
                dir = xDir;
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
                        dir = Direction.Up;

                    if ((dir & dirMask) != 0)
                    {
                        if (r >= 64)
                            break;
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
                break;

            // If there were a room that had lamnolas, and they could get surrounded on 3 sides,
            // then this would get stuck in an infinite loop. But, the only room with that configuration
            // has those blocks blocked off with a push block, which can only be pushed after all foes
            // are killed.

            do
            {
                dir = (Direction)((int)dir >> 1);
                if (dir == 0)
                    dir = Direction.Up;
            } while ((dir & dirMask) == 0);
        }
    }

    void CheckLamnolaCollisions()
    {
        Direction origFacing = Facing;
        CheckCollisions();
        Facing = origFacing;

        if (Decoration == 0)
            return;

        var slot = Game.World.curObjectSlot;
        Actor? obj = null;

        slot = slot >= TailSlot2 ? TailSlot2 : TailSlot1;

        for (; ; slot++)
        {
            obj = Game.World.GetObject(slot);
            if (obj != null && obj.GetType() == GetType())
                break;
        }

        if (slot == HeadSlot1 || slot == HeadSlot2)
            return;

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
    private static readonly byte[] startXs = new byte[] { 0x00, 0xF0 };
    private static readonly byte[] startYs = new byte[] { 0x3D, 0xDD };

    private static readonly byte[] wallmasterDirs = new byte[]
    {
        0x01, 0x01, 0x08, 0x08, 0x08, 0x02, 0x02, 0x02,
        0xC1, 0xC1, 0xC4, 0xC4, 0xC4, 0xC2, 0xC2, 0xC2,
        0x42, 0x42, 0x48, 0x48, 0x48, 0x41, 0x41, 0x41,
        0x82, 0x82, 0x84, 0x84, 0x84, 0x81, 0x81, 0x81,
        0xC4, 0xC4, 0xC2, 0xC2, 0xC2, 0xC8, 0xC8, 0xC8,
        0x84, 0x84, 0x81, 0x81, 0x81, 0x88, 0x88, 0x88,
        0x48, 0x48, 0x42, 0x42, 0x42, 0x44, 0x44, 0x44,
        0x08, 0x08, 0x01, 0x01, 0x01, 0x04, 0x04, 0x04
    };

    readonly SpriteAnimator Animator;

    int state;
    int dirIndex;
    int tilesCrossed;
    bool holdingPlayer;

    public WallmasterActor(Game game, int x, int y) : base(game, ObjType.Wallmaster, x, y)
    {
        Animator = new() {
            DurationFrames = 16,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Npcs, AnimationId.UW_Wallmaster)
        };
    }

    void CalcStartPosition(
        int playerOrthoCoord, int playerCoord, Direction dir,
        int baseDirIndex, int leastCoord, ref int orthoCoord, ref int coordIndex)
    {
        var player = Game.Link;
        int offset = 0x24;

        dirIndex = baseDirIndex;
        if (player.Moving != 0)
            offset = 0x32;
        if (player.Facing == dir)
        {
            dirIndex += 8;
            offset = -offset;
        }
        orthoCoord = playerOrthoCoord + offset;
        coordIndex = 0;
        if (playerCoord != leastCoord)
        {
            dirIndex += 0x10;
            coordIndex++;
        }
    }

    public override void Update()
    {
        if (state == 0)
            UpdateIdle();
        else
            UpdateMoving();
    }

    public override void Draw()
    {
        if (state != 0)
        {
            var flags = wallmasterDirs[dirIndex] >> 6;
            var pal = CalcPalette(Palette.Blue);

            if (holdingPlayer)
                Animator.DrawFrame(TileSheet.Npcs, X, Y, pal, 1, (DrawingFlags)flags);
            else
                Animator.Draw(TileSheet.Npcs, X, Y, pal, (DrawingFlags)flags);
        }
    }

    void UpdateIdle()
    {
        if (Game.World.GetObjectTimer(ObjectSlot.Monster1) != 0)
            return;

        var player = Game.Link;

        if (player.GetState() == PlayerState.Paused)
            return;

        int playerX = player.X;
        int playerY = player.Y;

        if (playerX < 0x29 || playerX >= 0xC8)
        {
            if (playerY < 0x6D || playerY >= 0xB5)
                return;
        }

        const int LeastY = 0x5D;
        const int MostY = 0xBD;

        if (playerX == 0x20 || playerX == 0xD0)
        {
            int y = 0;
            int xIndex = 0;
            CalcStartPosition(playerY, playerX, Direction.Up, 0, 0x20, ref y, ref xIndex);
            X = startXs[xIndex];
            Y = y;
        }
        else if (playerY == LeastY || playerY == MostY)
        {
            int x = 0;
            int yIndex = 0;
            CalcStartPosition(playerX, playerY, Direction.Left, 0x20, LeastY, ref x, ref yIndex);
            Y = startYs[yIndex];
            X = x;
        }
        else
        {
            return;
        }

        state = 1;
        tilesCrossed = 0;
        Game.World.SetObjectTimer(ObjectSlot.Monster1, 0x60);
        Facing = (Direction)(wallmasterDirs[dirIndex] & 0xF);
        TileOffset = 0;
    }

    void UpdateMoving()
    {
        var player = Game.Link;

        if (ShoveDirection != 0)
        {
            ObjShove();
        }
        else if (!IsStunned)
        {
            MoveDirection(0x18, Facing);

            if (TileOffset == 0x10 || TileOffset == -0x10)
            {
                TileOffset = 0;
                dirIndex++;
                tilesCrossed++;
                Facing = (Direction)(wallmasterDirs[dirIndex] & 0xF);

                if (tilesCrossed >= 7)
                {
                    state = 0;
                    if (holdingPlayer)
                    {
                        player.SetState(PlayerState.Idle);
                        Game.World.UnfurlLevel();
                    }
                    return;
                }
            }
        }

        if (holdingPlayer)
        {
            player.X = X;
            player.Y = Y;
            player.Animator.Advance();
        }
        else
        {
            if (CheckCollisions())
            {
                holdingPlayer = true;
                player.SetState(PlayerState.Paused);
                player.ResetShove();
                Flags |= ActorFlags.DrawAbovePlayer;
            }
            Animator.Advance();
        }
    }
}


internal sealed class AquamentusActor : Actor
{
    private const int AquamentusX = 0xB0;
    private const int AquamentusY = 0x80;

    private static readonly byte[] palette = new byte[] { 0, 0x0A, 0x29, 0x30 };

    readonly SpriteAnimator Animator;
    readonly SpriteImage mouthImage;

    int distance;
    readonly byte[] fireballOffsets = new byte[(int)ObjectSlot.MaxMonsters];

    public override bool IsReoccuring => false;
    public AquamentusActor(Game game, int x = AquamentusX, int y = AquamentusY) : base(game, ObjType.Aquamentus, x, y)
    {
        InvincibilityMask = 0xE2;


        Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance);

        Animator = new() {
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B1_Aquamentus),
            DurationFrames = 32,
            Time = 0
        };

        mouthImage = new() {
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B1_Aquamentus_Mouth_Closed)
        };

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, palette);
        Graphics.UpdatePalettes();
    }

    public override void Update()
    {
        if (Game.World.GetItem(ItemSlot.Clock) == 0)
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
        Animator.Draw(TileSheet.Boss, X, Y, pal);
        mouthImage.Draw(TileSheet.Boss, X, Y, pal);
    }

    void Move()
    {
        if (distance == 0)
        {
            int r = Random.Shared.Next(16);
            distance = r | 7;
            Facing = (Direction)((r & 1) + 1);
            return;
        }

        if ((Game.GetFrameCounter() & 7) != 0)
            return;

        if (X < 0x88)
        {
            X = 0x88;
            Facing = Direction.Right;
            distance = 7;
        }
        else if (X >= 0xC8)
        {
            X = 0xC7;
            Facing = Direction.Left;
            distance = 7;
        }

        if (Facing == Direction.Right)
            X++;
        else
            X--;

        distance--;
    }

    private static readonly sbyte[] yOffsets = new sbyte[] { 1, 0, -1 };

    void TryShooting()
    {
        if (ObjTimer == 0)
        {
            int r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);


            for (int i = 0; i < 3; i++)
            {
                var slot = Game.World.FindEmptyMonsterSlot();
                if (slot < 0)
                    break;

                ShootFireball(ObjType.Fireball, X, Y);
                fireballOffsets[(int)slot] = (byte)yOffsets[i];
            }
        }
        else
        {
            for (int i = 0; i < (int)ObjectSlot.MaxMonsters; i++)
            {
                var obj = Game.World.GetObject((ObjectSlot)i);

                if (obj == null || obj is not Fireball fireball)
                    continue;
                if ((Game.GetFrameCounter() & 1) == 1)
                    continue;

                fireball.Y += fireballOffsets[i];
            }
        }
    }

    void Animate()
    {
        AnimationId mouthAnimIndex;

        mouthAnimIndex = ObjTimer < 0x20 ? AnimationId.B1_Aquamentus_Mouth_Open : AnimationId.B1_Aquamentus_Mouth_Closed;

        mouthImage.Animation = Graphics.GetAnimation(TileSheet.Boss, mouthAnimIndex);
        Animator.Advance();
    }
}

internal sealed class Fireball : TODOActor { // JOE: TODO
    public Fireball(Game game, ObjType type, int x, int y, float whoknows) : base(game, type, x, y) { }
}

internal sealed class DodongoActor : WandererWalkerActor
{
    delegate void StateFunc();

    private static readonly AnimationId[] dodongoWalkAnimMap = new[]
    {
        AnimationId.B1_Dodongo_R,
        AnimationId.B1_Dodongo_L,
        AnimationId.B1_Dodongo_D,
        AnimationId.B1_Dodongo_U
    };

    private static readonly AnimationId[] dodongoBloatAnimMap = new[]
    {
        AnimationId.B1_Dodongo_Bloated_R,
        AnimationId.B1_Dodongo_Bloated_L,
        AnimationId.B1_Dodongo_Bloated_D,
        AnimationId.B1_Dodongo_Bloated_U
    };

    private static readonly WalkerSpec dodongoWalkSpec = new(dodongoWalkAnimMap, 20, Palette.Red, Global.StdSpeed);

    private static readonly byte[] palette = new byte[] { 0, 0x17, 0x27, 0x30 };

    private static readonly int[] negBounds = new int[] { -0x10, 0, -8, 0, -8, -4, -4, -0x10, 0, 0 };
    private static readonly int[] posBounds = new int[] { 0, 0x10, 8, 0, 8, 4, 4, 0, 0, 0x10 };

    int state;
    int bloatedSubstate;
    int bloatedTimer;
    int bombHits;

    readonly StateFunc[] sStateFuncs;
    readonly StateFunc[] sBloatedSubstateFuncs;

    public override bool IsReoccuring => false;
    private DodongoActor(Game game, ObjType type, int x, int y) : base(game, type, dodongoWalkSpec, 0x20, x, y)
    {
        sStateFuncs = new StateFunc[]
        {
            UpdateMoveState,
            UpdateBloatedState,
            UpdateStunnedState,
        };

        sBloatedSubstateFuncs = new StateFunc[]
        {
            UpdateBloatedWait,
            UpdateBloatedWait,
            UpdateBloatedWait,
            UpdateBloatedDie,
            UpdateBloatedEnd,
        };

        Game.Sound.PlayEffect(SoundEffect.BossRoar2, true, Sound.AmbientInstance);
        int r = Random.Shared.Next(2);
        Facing = r == 1 ? Direction.Left : Direction.Right;

        Animator.DurationFrames = 16;
        Animator.Time = 0;
        SetWalkAnimation();

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, palette);
        Graphics.UpdatePalettes();
    }

    public static DodongoActor Make(Game game, int count, int x, int y)
    {
        return count switch {
            1 => new DodongoActor(game, ObjType.OneDodongo, x, y),
            3 => new DodongoActor(game, ObjType.ThreeDodongos, x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(count))
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
        if (state == 1 && (bloatedSubstate == 2 || bloatedSubstate == 3))
        {
            if ((Game.GetFrameCounter() & 2) == 0)
                return;
        }

        Animator.Draw(TileSheet.Boss, X, Y, Palette.LevelFgPalette);
    }

    void SetWalkAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss, dodongoWalkAnimMap[dirOrd]);
    }

    void SetBloatAnimation()
    {
        int dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Boss, dodongoBloatAnimMap[dirOrd]);
    }

    void UpdateState()
    {
        sStateFuncs[state]();
    }

    void CheckPlayerHit()
    {
        CheckPlayerHitStdSize();
        if (InvincibilityTimer == 0)
        {
            if (Facing.IsVertical())
                return;

            X += 0x10;
            CheckPlayerHitStdSize();
            X -= 0x10;

            if (InvincibilityTimer == 0)
                return;
        }

        UpdateBloatedDie();
        Game.World.SetBombItemDrop();
    }

    void CheckPlayerHitStdSize()
    {
        InvincibilityMask = 0xFF;
        CheckCollisions();

        if (state == 2)
        {
            InvincibilityMask = 0xFE;
            CheckSword(ObjectSlot.PlayerSword);
        }
    }

    void CheckBombHit()
    {
        if (state != 0)
            return;

        var bomb = Game.World.GetObject<BombActor>(ObjectSlot.FirstBomb);
        if (bomb == null || bomb.IsDeleted)
            return;

        var bombState = bomb.BombState;
        int bombX = bomb.X + 8;
        int bombY = bomb.Y + 8;
        int thisX = X + 8;
        int thisY = Y + 8;

        if (Facing.IsHorizontal())
            thisX += 8;

        int xDist = thisX - bombX;
        int yDist = thisY - bombY;

        if (bombState == BombState.Ticking)
        {
            CheckTickingBombHit(bomb, xDist, yDist);
        }
        else    // Blasting or Fading
        {
            if (Overlaps(xDist, yDist, 0))
                state = 2;
        }
    }


    void CheckTickingBombHit(BombActor bomb, int xDist, int yDist)
    {
        if (!Overlaps(xDist, yDist, 1))
            return;


        var index = (int)Facing >> 1;
        int dist = xDist;

        for (int i = 0; i< 2; i++ )
        {
            if (dist<negBounds[index]
                || dist >= posBounds[index] )
                return;

            index += 5;
            dist = yDist;
        }

        state++;
        bloatedSubstate = 0;
        bomb.IsDeleted = true;
    }

    private static readonly int[] posBoundsOverlaps = new int[] { 0xC, 0x11 };
    private static readonly int[] negBoundsOverlaps = new int[] { -0xC, -0x10 };

    bool Overlaps(int xDist, int yDist, int boundsIndex)
    {

        var distances = new[] { xDist, yDist };

        for (int i = 1; i >= 0; i--)
        {
            if (distances[i] >= posBoundsOverlaps[boundsIndex]
                || distances[i] < negBoundsOverlaps[boundsIndex])
                return false;
        }

        return true;
    }

    void Animate()
    {
        if (state == 0)
            Animator.SetDuration(16);
        else
            Animator.SetDuration(64);

        if (state == 0 || state == 2 || bloatedSubstate == 0)
            SetWalkAnimation();
        else
            SetBloatAnimation();

        Animator.Advance();
    }

    void UpdateMoveState()
    {
        Direction origFacing = Facing;
        int xOffset = 0;

        if (Facing != Direction.Left)
        {
            X += 0x10;
            xOffset = 0x10;
        }

        Move();

        X -= xOffset;
        if (X < 0x20)
            Facing = Direction.Right;

        if (Facing != origFacing)
            SetWalkAnimation();
    }

    void UpdateBloatedState()
    {
        sBloatedSubstateFuncs[bloatedSubstate]();
    }

    void UpdateStunnedState()
    {
        if (StunTimer == 0)
        {
            StunTimer = 0x20;
        }
        else if (StunTimer == 1)
        {
            state = 0;
            bloatedSubstate = 0;
        }
    }

    private static readonly int[] waitTimes = new int[] { 0x20, 0x40, 0x40 };

    void UpdateBloatedWait()
    {

        if (bloatedTimer == 0)
        {
            bloatedTimer = waitTimes[bloatedSubstate];
            if (bloatedSubstate == 0)
            {
                var bomb = Game.World.GetObject<BombActor>(ObjectSlot.FirstBomb);
                if (bomb != null)
                    bomb.IsDeleted = true;
                bombHits++;
            }
        }
        else if (bloatedTimer == 1)
        {
            bloatedSubstate++;
            if (bloatedSubstate >= 2 && bombHits < 2)
                bloatedSubstate = 4;
        }

        bloatedTimer--;
    }

    void UpdateBloatedDie()
    {
        Game.Sound.PlayEffect(SoundEffect.MonsterDie);
        Game.Sound.PlayEffect(SoundEffect.BossHit);
        Game.Sound.StopEffect(StopEffect.AmbientInstance);
        Decoration = 0x10;
        state = 0;
        bloatedSubstate = 0;
    }

    void UpdateBloatedEnd()
    {
        state = 0;
        bloatedSubstate = 0;
    }
}


internal sealed class ManhandlaActor : Actor
{
    private static readonly AnimationId[] manhandlaAnimMap = new[]
    {
        AnimationId.B2_Manhandla_Hand_U,
        AnimationId.B2_Manhandla_Hand_D,
        AnimationId.B2_Manhandla_Hand_L,
        AnimationId.B2_Manhandla_Hand_R,
        AnimationId.B2_Manhandla_Body,
    };

    readonly SpriteAnimator Animator;

    short curSpeedFix;
    short speedAccum;
    short frameAccum;
    int frame;
    int oldFrame;

    static int sPartsDied;
    static Direction sFacingAtFrameBegin;
    static Direction sBounceDir;

    private static readonly int[] xOffsets = new int[] { 0, 0, -0x10, 0x10, 0 };
    private static readonly int[] yOffsets = new int[] { -0x10, 0x10, 0, 0, 0 };

    public override bool IsReoccuring => false;
    private ManhandlaActor(Game game, int index, int x, int y, Direction facing) : base(game, ObjType.Manhandla, x, y)
    {
        InvincibilityMask = 0xE2;
        Decoration = 0;
        Facing = facing;

        Animator = new() {
            DurationFrames = 1,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, manhandlaAnimMap[index])
        };
    }

    public static ManhandlaActor Make(Game game, int x, int y)
    {
        var dir = Random.Shared.GetDirection8();

        game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);

        for (int i = 0; i< 5; i++ )
        {
            // ORIGINAL: Get the base X and Y from the fifth spawn spot.
            int xPos = x + xOffsets[i];
            int yPos = y + yOffsets[i];

            var manhandla = new ManhandlaActor(game, i, xPos, yPos, dir);

            game.World.SetObject((ObjectSlot)i, manhandla);
        }

        return game.World.GetObject<ManhandlaActor>(0) ?? throw new Exception();
    }

    void SetPartFacings(Direction dir)
    {
        for (int i = 0; i < 5; i++)
        {
            var manhandla = Game.World.GetObject<ManhandlaActor>((ObjectSlot)i);
            if (manhandla != null)
                manhandla.Facing = dir;
        }
    }

    public override void Update()
    {
        var slot = Game.World.curObjectSlot;

        if (slot == (ObjectSlot)4)
        {
            UpdateBody();
            sFacingAtFrameBegin = Facing;
        }

        Move();
        CheckManhandlaCollisions();

        if (Facing != sFacingAtFrameBegin)
            sBounceDir = Facing;

        frame = (frameAccum & 0x10) >> 4;

        if (slot != (ObjectSlot)4)
            TryShooting();
    }

    public override void Draw()
    {
        var slot = Game.World.curObjectSlot;

        var pal = CalcPalette(Palette.Blue);

        if (slot == (ObjectSlot)4)
        {
            Animator.Draw(TileSheet.Boss, X, Y, pal);
        }
        else
        {
            Animator.DrawFrame(TileSheet.Boss, X, Y, pal, frame);
        }
    }

    void UpdateBody()
    {
        if (sPartsDied != 0)
        {
            for (int i = 0; i < 5; i++)
            {
                var manhandla = Game.World.GetObject<ManhandlaActor>((ObjectSlot)i);
                if (manhandla != null)
                    manhandla.curSpeedFix += 0x80;
            }
            sPartsDied = 0;
        }

        if (sBounceDir != Direction.None)
        {
            SetPartFacings(sBounceDir);
            sBounceDir = Direction.None;
        }

        Debug.Assert(Game.World.curObjectSlot == ObjectSlot.Monster1 + 4);

        if (ObjTimer == 0)
        {
            ObjTimer = 0x10;

            int r = Random.Shared.Next(2);
            Facing = r == 0 ? TurnRandomly8(Facing) : TurnTowardsPlayer8(X, Y, Facing);

            // The original game set sBounceDir = Facing here, instead of to Direction.None above.
            SetPartFacings(Facing);
        }
    }

    void Move()
    {
        speedAccum &= 0xFF;
        speedAccum += (short)(curSpeedFix & 0xFFE0);
        int speed = speedAccum >> 8;

        MoveSimple8(Facing, speed);

        frameAccum += (short)(Random.Shared.Next(4) + speed);

        if (Direction.None == CheckWorldMargin(Facing))
        {
            Facing = Facing.GetOppositeDir8();
        }
    }

    void TryShooting()
    {
        if (frame != oldFrame)
        {
            oldFrame = frame;

            if (frame == 0
                && Random.Shared.GetByte() >= 0xE0
                && Game.World.GetObject((ObjectSlot)6) == null)
            {
                ShootFireball(ObjType.Fireball2, X, Y);
            }
        }
    }

    void CheckManhandlaCollisions()
    {
        var objSlot = Game.World.curObjectSlot;

        var origFacing = Facing;
        int origStateTimer = ObjTimer;

        CheckCollisions();

        ObjTimer = (byte)origStateTimer;
        Facing = origFacing;

        // JOE: TODO: Make (ObjectSlot)4 into a named enum member.
        if (objSlot == (ObjectSlot)4)
            InvincibilityTimer = 0;

        PlayBossHitSoundIfHit();

        if (Decoration == 0)
            return;

        ShoveDirection = 0;
        ShoveDistance = 0;

        if (objSlot == (ObjectSlot)4)
        {
            Decoration = 0;
            return;
        }

        int handCount = 0;

        for (var i = ObjectSlot.Monster1; i < ObjectSlot.Monster1 + 4; i++)
        {
            var obj = Game.World.GetObject(i);
            if (obj != null && obj is ManhandlaActor)
                handCount++;
        }

        var dummy = new DeadDummyActor(Game, X, Y) {
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

        sPartsDied++;
    }

    public static void ClearRoomData()
    {
        sPartsDied = 0;
        sFacingAtFrameBegin = Direction.None;
        sBounceDir = Direction.None;
    }
}

internal abstract class DigdoggerActorBase : Actor
{
    protected short curSpeedFix = 0x003F;
    protected short speedAccum;
    protected short targetSpeedFix = 0x0080;
    protected short accelDir;
    protected bool isChild;

    protected DigdoggerActorBase(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
        Facing = Random.Shared.GetDirection8();
        isChild = this is DigdoggerChildActor;

        Game.Sound.PlayEffect(SoundEffect.BossRoar3, true, Sound.AmbientInstance);
    }

    protected void UpdateMove()
    {
        if (ObjTimer == 0)
        {
            ObjTimer = 0x10;

            int r = Random.Shared.Next(2);
            if (r == 0)
                Facing = TurnRandomly8(Facing);
            else
                Facing = TurnTowardsPlayer8(X, Y, Facing);
        }

        Accelerate();
        Move();
    }

    void Move()
    {
        speedAccum &= 0xFF;
        speedAccum += (short)(curSpeedFix & 0xFFE0);
        int speed = speedAccum >> 8;

        MoveSimple8(Facing, speed);
    }

    void Accelerate()
    {
        if (accelDir == 0)
            IncreaseSpeed();
        else
            DecreaseSpeed();
    }

    void IncreaseSpeed()
    {
        curSpeedFix++;

        if (curSpeedFix != targetSpeedFix)
            return;

        accelDir++;
        targetSpeedFix = 0x0040;

        if (isChild)
            targetSpeedFix += 0x0100;
    }

    void DecreaseSpeed()
    {
        curSpeedFix--;

        if (curSpeedFix != targetSpeedFix)
            return;

        accelDir--;
        targetSpeedFix = 0x0080;

        if (isChild)
            targetSpeedFix += 0x0100;
    }
};

internal sealed class DigdoggerActor : DigdoggerActorBase
{
    readonly SpriteAnimator Animator;
    readonly SpriteAnimator littleAnimator;

    readonly int childCount;
    bool updateBig;

    private static readonly byte[] palette = new byte[] { 0, 0x17, 0x27, 0x30 };
    private static readonly int[] offsetsX = new int[] { 0, 0x10, 0, -0x10 };
    private static readonly int[] offsetsY = new int[] { 0, 0x10, -0x10, 0x10 };

    public override bool IsReoccuring => false;

    private DigdoggerActor(Game game, int x, int y, int childCount) : base(game, ObjType.Digdogger1, x, y)
    {
        this.childCount = childCount;
        updateBig = true;

        Animator = new() {
            DurationFrames = 12,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B1_Digdogger_Big)
        };

        littleAnimator = new() {
            DurationFrames = 12,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B1_Digdogger_Little)
        };

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, palette);
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
            if (Game.World.recorderUsed == 0)
                UpdateMove();
            else
                UpdateSplit();
        }


        if (updateBig )
        {
            int x = X;
            int y = Y;

            for (int i = 0; i< 4; i++ )
            {
                X += offsetsX[i];
                Y += offsetsY[i];

                if (Direction.None == CheckWorldMargin(Facing))
                {
                    Facing = Facing.GetOppositeDir8();
                }

                CheckCollisions();
            }

            X = x;
            Y = y;

            Animator.Advance();
        }

        littleAnimator.Advance();
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.LevelFgPalette);

        if (updateBig)
        {
            Animator.Draw(TileSheet.Boss, X, Y, pal);
        }
        littleAnimator.Draw(TileSheet.Boss, X + 8, Y + 8, pal);
    }

    void UpdateSplit()
    {
        if (Game.World.recorderUsed == 1)
        {
            ObjTimer = 0x40;
            Game.World.recorderUsed = 2;
        }
        else
        {
            updateBig = false;

            if (ObjTimer != 0)
            {
                if ((ObjTimer & 7) == 0)
                {
                    isChild = !isChild;
                    if (!isChild)
                        updateBig = true;
                }
            }
            else
            {
                Game.World.recorderUsed = 1;
                Game.World.RoomObjCount = childCount;
                for (var i = 1; i <= childCount; i++)
                {
                    var child = DigdoggerChildActor.Make(Game, X, Y);
                    Game.World.SetObject((ObjectSlot)i, child);
                }
                Game.Sound.PlayEffect(SoundEffect.BossHit);
                Game.Sound.StopEffect(StopEffect.AmbientInstance);
                IsDeleted = true;
            }
        }
    }
}

internal sealed class DigdoggerChildActor : DigdoggerActorBase
{
    readonly SpriteAnimator Animator;

    private DigdoggerChildActor(Game game, int x, int y) : base(game, ObjType.LittleDigdogger, x, y)
    {
        targetSpeedFix = 0x0180;

        Animator = new() {
            DurationFrames = 12,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B1_Digdogger_Little),
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
        Animator.Advance();
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.LevelFgPalette);
        Animator.Draw(TileSheet.Boss, X, Y, pal);
    }
}

internal sealed class GohmaActor : TODOActor
{
    private readonly ActorColor _color;
    private const int GohmaX = 0x80;
    private const int GohmaY = 0x70;

    private readonly SpriteAnimator Animator;
    private readonly SpriteAnimator leftAnimator;
    private readonly SpriteAnimator rightAnimator;

    private bool changeFacing = true;
    private short speedAccum;
    private int distance;
    private int sprints;
    private int startOpenEyeTimer;
    private int eyeOpenTimer;
    private int eyeClosedTimer;
    private int shootTimer = 1;
    private int frame;
    private int curCheckPart;

    public override bool IsReoccuring => false;

    public GohmaActor(Game game, ActorColor color, int x = GohmaX, int y = GohmaY) : base(game, color, x, y)
    {
        _color = color;
        ObjType = color switch {
            ActorColor.Red => ObjType.RedGohma,
            ActorColor.Blue => ObjType.BlueGohma,
            _ => throw new ArgumentOutOfRangeException(),
        };

        Decoration = 0;
        InvincibilityMask = 0xFB;

        Animator = new()
        {
            DurationFrames = 1,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gohma_Eye_All)
        };

        leftAnimator = new()
        {
            DurationFrames = 32,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gohma_Legs_L)
        };

        rightAnimator = new()
        {
            DurationFrames = 32,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gohma_Legs_R)
        };
    }

    public int GetCurrentCheckPart()
    {
        return curCheckPart;
    }

    public int GetEyeFrame()
    {
        return frame;
    }

    public override void Update()
    {
        if (changeFacing)
            ChangeFacing();
        else
            Move();

        AnimateEye();
        TryShooting();
        CheckGohmaCollisions();

        leftAnimator.Advance();
        rightAnimator.Advance();
    }

    public override void Draw()
    {
        var pal = _color == ActorColor.Blue ? Palette.Blue : Palette.Red;
        pal = CalcPalette(pal);

        Animator.DrawFrame(TileSheet.Boss, X, Y, pal, frame);
        leftAnimator.Draw(TileSheet.Boss, X - 0x10, Y, pal);
        rightAnimator.Draw(TileSheet.Boss, X + 0x10, Y, pal);
    }

    void ChangeFacing()
    {
        int dir = 1;
        int r = Random.Shared.GetByte();

        if (r < 0xB0)
        {
            dir <<= 1;
            if (r < 0x60)
                dir <<= 1;
        }

        Facing = (Direction)dir;
        changeFacing = false;
    }

    void Move()
    {
        speedAccum &= 0xFF;
        speedAccum += 0x80;

        if (speedAccum >= 0x0100)
        {
            distance++;
            MoveSimple(Facing, 1);

            if (distance == 0x20)
            {
                distance = 0;
                Facing = Facing.GetOppositeDirection();

                sprints++;
                if ((sprints & 1) == 0)
                    changeFacing = true;
            }
        }
    }

    void AnimateEye()
    {
        if (startOpenEyeTimer == 0)
        {
            eyeOpenTimer = 0x80;
            startOpenEyeTimer = 0xC0 | Random.Shared.GetByte();
        }

        if ((Game.GetFrameCounter() & 1) == 1)
        {
            startOpenEyeTimer--;
        }

        if (eyeOpenTimer == 0)
        {
            eyeClosedTimer++;
            if (eyeClosedTimer == 8)
            {
                eyeClosedTimer = 0;
                frame = (frame & 1) ^ 1;
            }
        }
        else
        {
            int t = eyeOpenTimer;
            eyeOpenTimer--;
            frame = 2;
            if (t < 0x70 && t >= 0x10)
                frame++;
        }
    }

    void TryShooting()
    {
        shootTimer--;
        if (shootTimer == 0)
        {
            shootTimer = 0x41;
            ShootFireball(ObjType.Fireball2, X, Y);
        }
    }

    void CheckGohmaCollisions()
    {
        int origX = X;
        X -= 0x10;

        for (int i = 5; i > 0; i--)
        {
            curCheckPart = i;
            // With other object types, we'd only call CheckCollisions. But, Gohma needs
            // to pass down the index of the current part.
            CheckCollisions();
            X += 8;
        }

        X = origX;
    }
}

internal enum BombState { Initing, Ticking, Blasting, Fading }

internal sealed class BombActor : TODOActor
{
    public BombState BombState;

    public BombActor(Game game, int x, int y) : base(game, ObjType.Bomb, x, y) { }
}

internal enum FireState { Moving, Standing }

internal class FireActor : TODOActor
{
    public FireState FireState;

    protected FireActor(Game game, ObjType type, int x, int y) : base(game, type, x, y) { }
    public FireActor(Game game, int x, int y) : this(game, ObjType.Fire, x, y) { }
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

internal sealed class ArmosActor : TODOActor
{
    public ArmosActor(Game game, int x, int y) : base(game, x, y)
    {
    }
}

internal enum ActorColor { Undefined, Blue, Red, Black }

internal sealed class GoriyaActor : TODOActor
{
    public GoriyaActor(Game game, ActorColor type, int x, int y) : base(game, x, y)
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

    private static readonly byte[] statueCounts = new byte[] { 4, 2, 2 };
    private static readonly byte[] startTimers = new byte[] { 0x50, 0x80, 0xF0, 0x60 };
    private static readonly byte[] patternOffsets = new byte[] { 0, 4, 6 };
    private static readonly byte[] xs = new byte[] { 0x24, 0xC8, 0x24, 0xC8, 0x64, 0x88, 0x48, 0xA8 };
    private static readonly byte[] ys = new byte[] { 0xC0, 0xBC, 0x64, 0x5C, 0x94, 0x8C, 0x82, 0x86 };

    public static readonly byte[] Timers = new byte[(int)PatternType.MaxStatues];

    public static void Init()
    {
        Array.Fill(Timers, (byte)0);
    }

    public static void Update(Game game, PatternType pattern)
    {
        if (pattern < 0 || pattern >= PatternType.Patterns)
            return;

        var slot = game.World.FindEmptyMonsterSlot();
        if (slot < ObjectSlot.Monster1 + 5)
            return;

        var player = game.Link;
        int statueCount = statueCounts[(int)pattern];

        for (int i = 0; i < statueCount; i++)
        {
            int timer = Timers[i];
            Timers[i]--;

            if (timer != 0)
                continue;

            int r = Random.Shared.GetByte();
            if (r >= 0xF0)
                continue;

            int j = r & 3;
            Timers[i] = startTimers[j];

            int offset = i + patternOffsets[(int)pattern];
            int x = xs[offset];
            int y = ys[offset];

            if (Math.Abs(x - player.X) >= 0x18
                || Math.Abs(y - player.Y) >= 0x18)
            {
                game.ShootFireball(ObjType.Fireball, x, y);
            }
        }
    }
}

internal sealed class RupieStashActor : TODOActor
{
    public override bool IsReoccuring => false;
    public RupieStashActor(Game game, int x, int y) : base(game, x, y)
    {
    }
}

internal abstract class StdChaseWalker : ChaseWalkerActor
{
    protected StdChaseWalker(Game game, ObjType type, WalkerSpec spec, int x, int y) : base(game, type, spec, x, y)
    {
        InitCommonFacing();
        SetFacingAnimation();
    }
}

internal sealed class LynelActor : StdChaseWalker
{
    public const ObjType ShotFromLynel = ObjType.PlayerSwordShot;

    private static readonly AnimationId[] lynelAnimMap = new[]
    {
        AnimationId.OW_Lynel_Right,
        AnimationId.OW_Lynel_Left,
        AnimationId.OW_Lynel_Down,
        AnimationId.OW_Lynel_Up,
    };

    private static readonly WalkerSpec blueLynelSpec = new(lynelAnimMap, 12, Palette.Blue, Global.StdSpeed, ShotFromLynel);
    private static readonly WalkerSpec redLynelSpec = new(lynelAnimMap, 12, Palette.Red, Global.StdSpeed, ShotFromLynel);

    private LynelActor(Game game, ObjType type, WalkerSpec spec, int x, int y) : base(game, type, spec, x, y)
    {
        if (type is not (ObjType.BlueLynel or ObjType.RedLynel))
        {
            throw new ArgumentOutOfRangeException();
        }
    }

    public static LynelActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new LynelActor(game, ObjType.BlueLynel, blueLynelSpec, x, y),
            ActorColor.Red => new LynelActor(game, ObjType.RedLynel, redLynelSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

internal sealed class MoblinActor : StdWanderer
{
    public const ObjType ShotFromMoblin = ObjType.Arrow;

    private static readonly AnimationId[] moblinAnimMap = new[]
    {
        AnimationId.OW_Moblin_Right,
        AnimationId.OW_Moblin_Left,
        AnimationId.OW_Moblin_Down,
        AnimationId.OW_Moblin_Up,
    };

    private static readonly WalkerSpec blueMoblinSpec = new(moblinAnimMap, 12, (Palette)7, Global.StdSpeed, ShotFromMoblin);
    private static readonly WalkerSpec redMoblinSpec = new(moblinAnimMap, 12, Palette.Red, Global.StdSpeed, ShotFromMoblin);

    private MoblinActor(Game game, ObjType type, WalkerSpec spec, int x, int y) : base(game, type, spec, 0xA0, x, y)
    {
        if (type is not (ObjType.BlueMoblin or ObjType.RedMoblin))
        {
            throw new ArgumentOutOfRangeException();
        }
    }

    public static MoblinActor Make(Game game, ActorColor color, int x, int y)
    {
        return color switch
        {
            ActorColor.Blue => new MoblinActor(game, ObjType.BlueMoblin, blueMoblinSpec, x, y),
            ActorColor.Red => new MoblinActor(game, ObjType.RedMoblin, redMoblinSpec, x, y),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}

internal sealed class WizzrobeActor : TODOActor
{
    public WizzrobeActor(Game game, ActorColor color, int x, int y) : base(game, x, y)
    {
    }
}

internal abstract class WizzrobeBase : Actor
{
    private static Direction[] wizzrobeDirs = new[]
    {
        Direction.Down,
        Direction.Up,
        Direction.Right,
        Direction.Left
    };

    private static int[] wizzrobeXOffsets = new int[]
    {
        0x00, 0x00, -0x20, 0x20, 0x00, 0x00, -0x40, 0x40,
        0x00, 0x00, -0x30, 0x30, 0x00, 0x00, -0x50, 0x50
    };

    private static int[] wizzrobeYOffsets = new int[]
    {
        -0x20, 0x20, 0x00, 0x00, -0x40, 0x40, 0x00, 0x00,
        -0x30, 0x30, 0x00, 0x00, -0x50, 0x50, 0x00, 0x00
    };

    private static readonly int[] allWizzrobeCollisionXOffsets = new[] { 0xF, 0, 0, 4, 8, 0, 0, 4, 8, 0 };
    private static readonly int[] allWizzrobeCollisionYOffsets = new[] { 4, 4, 0, 8, 8, 8, 0, -8, 0, 0 };

    protected int CheckWizzrobeTileCollision(int x, int y, Direction dir)
    {
        var ord = dir - 1;
        x += allWizzrobeCollisionXOffsets[(int)ord];
        y += allWizzrobeCollisionYOffsets[(int)ord];

        TileCollision collision;

        collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
            return 0;

        // This isn't quite the same as the original game, because the original contrasted
        // blocks and water together with everything else.

        if (World.CollidesWall(collision.TileBehavior))
            return 1;

        return 2;
    }

    protected WizzrobeBase(Game game, int x, int y) : base(game, x, y) { }
    protected WizzrobeBase(Game game, ObjType type, int x, int y) : base(game, type, x, y) { }
}