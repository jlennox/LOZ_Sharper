using System.Collections.Immutable;
using z1.Actors;
using z1.Render;
using z1.IO;

namespace z1;

internal interface IProjectile : IBlockableProjectile
{
    bool IsPlayerWeapon { get; }
    bool IsInShotStartState();
}

internal interface IBlockableProjectile
{
    bool RequiresMagicShield => false;
}

internal enum ProjectileState { Flying, Spark, Bounce, Spreading }

internal abstract class Projectile : Actor, IProjectile
{
    private static readonly DebugLog _log = new(nameof(Projectile));

    public ProjectileState State = ProjectileState.Flying;

    public bool IsPlayerWeapon => Owner!.IsPlayer;

    private Direction _bounceDir = Direction.None;

    protected Projectile(World world, ObjType type, int x, int y, Actor owner)
        : base(world, type, x, y)
    {
        Owner = owner;
    }

    public virtual bool IsInShotStartState() => State == ProjectileState.Flying;

    public static Actor MakeProjectile(World world, ObjType type, int x, int y, Direction moving, Actor actor)
    {
        return type switch
        {
            ObjType.FlyingRock => new FlyingRockProjectile(world, x, y, moving, actor),
            ObjType.PlayerSwordShot => new PlayerSwordProjectile(world, x, y, moving, actor),
            ObjType.Arrow => new ArrowProjectile(world, x, y, moving, actor),
            ObjType.MagicWave => new MagicWaveProjectile(world, ObjType.MagicWave, x, y, moving, actor),
            ObjType.MagicWave2 => new MagicWaveProjectile(world, ObjType.MagicWave2, x, y, moving, actor),
            _ => throw new Exception()
        };
    }

    public static BoomerangProjectile MakeBoomerang(
        World world, int x, int y, Direction moving, int distance, float speed, Actor owner)
    {
        return new BoomerangProjectile(world, x, y, moving, distance, speed, owner);
    }

    protected void Move(int speed)
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
                Delete();
            }
            else if (collision.ShotCollides)
            {
                TileOffset = 0;
                State = ProjectileState.Bounce;
                _bounceDir = Game.Player.Facing;
            }
        }
    }

    protected void UpdateBounce()
    {
        ReadOnlySpan<int> xSpeeds = [2, -2, -1, 1];
        ReadOnlySpan<int> ySpeeds = [-1, -1, 2, -2];

        var dirOrd = _bounceDir.GetOrdinal();

        X += xSpeeds[dirOrd];
        Y += ySpeeds[dirOrd];
        TileOffset += 2;

        if (TileOffset >= 0x20)
        {
            Delete();
        }
    }
}

internal sealed class PlayerSwordProjectile : Projectile, IBlockableProjectile
{
    public bool RequiresMagicShield => true;

    private int _distance;
    private readonly SpriteImage _image;

    public PlayerSwordProjectile(World world, int x, int y, Direction direction, Actor owner)
        : base(world, ObjType.PlayerSwordShot, x, y, owner)
    {
        Facing = direction;
        Decoration = 0;

        var dirOrd = Facing.GetOrdinal(); ;
        var animIndex = PlayerSwordActor.SwordAnimMap[dirOrd];
        _image = new SpriteImage(TileSheet.PlayerAndItems, animIndex);
    }

    public static int PlayerCount(World world)
    {
        return world.GetObjects<PlayerSwordProjectile>().Count(t => t.Owner!.IsPlayer);
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
            {
                SpreadOut();
            }
            else
            {
                Delete();
            }

            return;
        }

        Move(0xC0);
        CheckPlayer();
    }

    private void UpdateSpreading()
    {
        if (_distance == 21)
        {
            // The original game still drew in this frame, but we won't.
            Delete();
            return;
        }
        _distance++;
    }

    public override void Draw()
    {
        var palOffset = Game.FrameCounter % Global.ForegroundPalCount;
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
                const int xOffset = 4;
                var d = _distance - 1;
                var left = X - 2 - d + xOffset;
                var right = X + 2 + d + xOffset;
                var top = Y - 2 - d + yOffset;
                var bottom = Y + 2 + d + yOffset;

                _image.Draw(TileSheet.PlayerAndItems, left, top, palette);
                _image.Draw(TileSheet.PlayerAndItems, right, top, palette, DrawingFlags.FlipX);
                _image.Draw(TileSheet.PlayerAndItems, left, bottom, palette, DrawingFlags.FlipY);
                _image.Draw(TileSheet.PlayerAndItems, right, bottom, palette,
                    DrawingFlags.FlipX | DrawingFlags.FlipY);
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

internal sealed class FlyingRockProjectile : Projectile
{
    private readonly SpriteImage _image;

    public FlyingRockProjectile(World world, int x, int y, Direction moving, Actor owner)
        : base(world, ObjType.FlyingRock, x, y, owner)
    {
        Facing = moving;
        Decoration = 0;

        _image = new SpriteImage(TileSheet.NpcsOverworld, AnimationId.OW_FlyingRock);
    }

    public override void Update()
    {
        switch (State)
        {
            case ProjectileState.Flying: UpdateFlying(); break;
            case ProjectileState.Bounce: UpdateBounce(); break;
        }
    }

    private void UpdateFlying()
    {
        if (ObjTimer == 0)
        {
            if (World.CollidesWithTileMoving(X, Y, Facing, false))
            {
                Delete();
                return;
            }
        }

        if (Direction.None == CheckWorldMargin(Facing))
        {
            Delete();
            return;
        }

        Move(0xC0);
        CheckPlayer();
    }

    public override void Draw()
    {
        var xOffset = (16 - _image.Animation.Width) / 2;
        _image.Draw(TileSheet.NpcsOverworld, X + xOffset, Y, Palette.Player);
    }
}

internal enum FireballState { Initializing, Firing }

internal sealed class FireballProjectile : Actor, IBlockableProjectile
{
    private static readonly DebugLog _traceLog = new(nameof(FireballProjectile), DebugLogDestination.None);

    private static readonly ImmutableArray<Direction> _sector16Dirs = [
        Direction.Right,
        Direction.Right | Direction.Down,
        Direction.Right | Direction.Down,
        Direction.Right | Direction.Down,
        Direction.Down,
        Direction.Down | Direction.Left,
        Direction.Down | Direction.Left,
        Direction.Down | Direction.Left,
        Direction.Left,
        Direction.Left | Direction.Up,
        Direction.Left | Direction.Up,
        Direction.Left | Direction.Up,
        Direction.Up,
        Direction.Up | Direction.Right,
        Direction.Up | Direction.Right,
        Direction.Up | Direction.Right
    ];

    public bool RequiresMagicShield => true;

    public new int X
    {
        get => (int)_x;
        set
        {
            base.X = value;
            _x = value;
        }
    }

    public new int Y
    {
        get => (int)_y;
        set
        {
            base.Y = value;
            _y = value;
        }
    }

    // Used only by Aquamentis
    public int? Offset { get; }

    // JOE: NOTE: These mirror the original X/Y values but are floats to keep precision.
    private float _x;
    private float _y;
    private readonly float _speedX;
    private readonly float _speedY;
    private readonly SpriteImage _image;
    private FireballState _state;

    public FireballProjectile(World world, ObjType type, int x, int y, float speed, int? offset)
        : base(world, type, x, y)
    {
        if (type is not (ObjType.Fireball or ObjType.Fireball2))
        {
            throw new ArgumentOutOfRangeException(nameof(type), ObjType.Fireball, "Invalid projectile type");
        }

        Offset = offset;

        Decoration = 0;
        _image = new SpriteImage(TileSheet.PlayerAndItems, AnimationId.Fireball);

        _x = x;
        _y = y;
        base.X = (int)_x;
        base.Y = (int)_y;

        _state = FireballState.Initializing;
        var xDist = Game.Player.X - x;
        var yDist = Game.Player.Y - y;

        var sector = new PointF(xDist, yDist).Rotate(Pi.PiOver16).GetSector16();
        var angle = Pi.PiOver8 * sector;

        Facing = _sector16Dirs[sector];

        _speedX = (float)Math.Cos(angle) * speed;
        _speedY = (float)Math.Sin(angle) * speed;

        _traceLog.Write($"ctor({type}, {X:X2},{Y:X2}, {speed}) pos:({_x},{_y}) speed:({_speedX},{_speedY})");
    }

    public override void Update()
    {
        if (_state == 0)
        {
            ObjTimer = 0x10;
            _state = FireballState.Firing;
            _traceLog.Write($"Update() {X:X2},{Y:X2} pos:({_x},{_y}) _state == 0");
            return;
        }

        if (ObjTimer == 0)
        {
            if (CheckWorldMargin(Facing, out var reason) == Direction.None)
            {
                _traceLog.Write($"Update() {X:X2},{Y:X2} pos:({_x},{_y}) ObjTimer == 0, IsDeleted = true {reason}");
                Delete();
                return;
            }

            _x += _speedX;
            _y += _speedY;
            base.X = (int)_x;
            base.Y = (int)_y;

            _traceLog.Write($"Update() {X:X2},{Y:X2} pos:({_x},{_y}) speed:({_speedX},{_speedY})");
        }

        var collision = CheckPlayerCollision();
        if (collision.Collides || collision.ShotCollides)
        {
            _traceLog.Write($"Update() collision.Collides, IsDeleted = true");
            Delete();
        }
    }

    public override void Draw()
    {
        var palOffset = Game.FrameCounter % Global.ForegroundPalCount;
        var palette = Palette.Player + palOffset;

        _image.Draw(TileSheet.PlayerAndItems, X, Y, palette);
    }
}

internal enum BoomerangState { Unknown0, Unknown1, Unknown2, Unknown3, Unknown4, Unknown5 }

internal sealed class BoomerangProjectile : Actor, IProjectile
{
    public const int YellowsDistance = 0x31;
    public const int RedsDistance = 0xFF;

    private static readonly DebugLog _log = new(nameof(BoomerangProjectile));

    public bool IsPlayerWeapon => Owner!.IsPlayer;

    private readonly int _startX;
    private readonly int _startY;
    private int _distanceTarget;
    private float _x;
    private float _y;
    private readonly float _leaveSpeed;
    private BoomerangState _state;
    private int _animTimer;
    private readonly SpriteAnimator _animator;

    public BoomerangProjectile(World world, int x, int y, Direction moving, int distance, float speed, Actor owner)
        : base(world, ObjType.Boomerang, x, y)
    {
        _startX = x;
        _startY = y;
        _distanceTarget = distance;
        Owner = owner;
        _x = x;
        _y = y;
        _leaveSpeed = speed;
        _state = BoomerangState.Unknown1;
        _animTimer = 3;

        Facing = moving;
        Decoration = 0;

        _animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Boomerang)
        {
            Time = 0
        };
        _animator.DurationFrames = _animator.Animation!.Length * 2;
    }

    public static int PlayerCount(World world)
    {
        return world.GetObjects<BoomerangProjectile>().Count(t => t.Owner!.IsPlayer);
    }

    public bool IsInShotStartState()
    {
        return _state == BoomerangState.Unknown1;
    }

    public void SetState(BoomerangState state)
    {
        _state = state;
    }

    public override void Update()
    {
        switch (_state)
        {
            case BoomerangState.Unknown1: UpdateLeaveFast(); break;
            case BoomerangState.Unknown2: UpdateSpark(); break;
            case BoomerangState.Unknown3: UpdateLeaveSlow(); break;
            case BoomerangState.Unknown4:
            case BoomerangState.Unknown5: UpdateReturn(); break;
        }
    }

    private void UpdateLeaveFast()
    {
        MoveSimple8(ref _x, ref _y, Facing, _leaveSpeed);
        X = (int)_x;
        Y = (int)_y;

        if (CheckWorldMargin(Facing) == Direction.None)
        {
            _state = BoomerangState.Unknown2;
            _animTimer = 3;
            CheckCollision();
            return;
        }

        if (Math.Abs(_startX - X) < _distanceTarget && Math.Abs(_startY - Y) < _distanceTarget)
        {
            AdvanceAnimAndCheckCollision();
            return;
        }

        _distanceTarget = 0x10;
        _state = BoomerangState.Unknown3;
        _animTimer = 3;
        _animator.Time = 0;
        CheckCollision();
    }

    private void UpdateLeaveSlow()
    {
        var gotoNextState = true;

        if ((Facing & Direction.Left) == 0 || _x >= 2)
        {
            if (MovingDirection.HasFlag(Direction.Left))
            {
                Facing = Direction.Left;
            }
            else if (MovingDirection.HasFlag(Direction.Right))
            {
                Facing = Direction.Right;
            }

            MoveSimple8(ref _x, ref _y, Facing, 1);
            X = (int)_x;
            Y = (int)_y;

            _distanceTarget--;
            if (_distanceTarget != 0)
            {
                gotoNextState = false;
            }
        }

        if (gotoNextState)
        {
            _distanceTarget = 0x20;
            _state = BoomerangState.Unknown4;
            _animator.Time = 0;
        }

        AdvanceAnimAndCheckCollision();
    }

    private void UpdateReturn()
    {
        if (Owner == null || Owner.Decoration != 0)
        {
            Delete();
            return;
        }

        if (Owner is not IThrower thrower)
        {
            Delete();
            return;
        }

        var yDist = Owner.Y - (int)Math.Floor(_y);
        var xDist = Owner.X - (int)Math.Floor(_x);

        if (Math.Abs(xDist) < 9 && Math.Abs(yDist) < 9)
        {
            thrower.Catch();
            Delete();
            return;
        }

        var angle = (float)Math.Atan2(yDist, xDist);
        float speed = 2;

        if (_state == BoomerangState.Unknown4)
        {
            speed = 1;
            _distanceTarget--;
            if (_distanceTarget == 0)
            {
                _state = BoomerangState.Unknown5;
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

    private void UpdateSpark()
    {
        _animTimer--;
        if (_animTimer == 0)
        {
            _state = BoomerangState.Unknown5;
            _animTimer = 3;
            _animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Boomerang);
            _animator.Time = 0;
            return;
        }

        _animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Spark);
        _animator.Time = 0;
    }

    private void AdvanceAnimAndCheckCollision()
    {
        _animTimer--;
        if (_animTimer == 0)
        {
            // The original game sets animTimer to 2.
            // But the sound from the NSF doesn't sound right at that speed.
            _animTimer = 11;
            if (Owner != null && Owner.IsPlayer)
            {
                Game.Sound.PlayEffect(SoundEffect.Boomerang);
            }
        }

        _animator.Advance();

        CheckCollision();
    }

    private void CheckCollision()
    {
        if (!IsPlayerWeapon)
        {
            var collision = CheckPlayerCollision();
            if (collision.ShotCollides)
            {
                _state = BoomerangState.Unknown2;
                _animTimer = 3;
            }
        }
    }

    public override void Draw()
    {
        var itemValue = World.GetItem(ItemSlot.Boomerang);
        if (itemValue == 0)
        {
            itemValue = 1;
        }

        var pal = _state == BoomerangState.Unknown2 ? Palette.Red : (Palette.Player + itemValue - 1);
        var xOffset = (16 - _animator.Animation?.Width ?? 0) / 2;
        _animator.Draw(TileSheet.PlayerAndItems, _x + xOffset, _y, pal);
    }
}

internal sealed class MagicWaveProjectile : Projectile, IBlockableProjectile
{
    private static readonly ImmutableArray<AnimationId> _waveAnimMap = [
        AnimationId.Wave_Right,
        AnimationId.Wave_Left,
        AnimationId.Wave_Down,
        AnimationId.Wave_Up
    ];

    public bool RequiresMagicShield => true;

    private readonly SpriteImage _image;

    public MagicWaveProjectile(World world, ObjType type, int x, int y, Direction direction, Actor owner)
        : base(world, type, x, y, owner)
    {
        if (type is not (ObjType.MagicWave or ObjType.MagicWave2))
        {
            throw new ArgumentOutOfRangeException(nameof(type), ObjType.MagicWave, "Invalid projectile type");
        }

        Facing = direction;
        Decoration = 0;

        var dirOrd = direction.GetOrdinal();
        _image = new SpriteImage(TileSheet.PlayerAndItems, _waveAnimMap[dirOrd]);
    }

    public static int PlayerCount(World world)
    {
        return world.GetObjects<MagicWaveProjectile>().Count(t => t.Owner.IsPlayer);
    }

    public override void Update()
    {
        switch (State)
        {
            case ProjectileState.Flying: UpdateFlying(); break;
            case ProjectileState.Bounce: UpdateBounce(); break;
        }
    }

    private void UpdateFlying()
    {
        if (Direction.None == CheckWorldMargin(Facing))
        {
            if (IsPlayerWeapon && !World.IsOverworld())
            {
                AddFire();
            }

            Delete();
            return;
        }

        Move(0xA0);
        CheckPlayer();
    }

    public override void Draw()
    {
        var pal = 4 + Game.FrameCounter % 4;
        _image.Draw(TileSheet.PlayerAndItems, X, Y, (Palette)pal);
    }

    public void AddFire()
    {
        if (World.GetItem(ItemSlot.Book) == 0) return;

        var fire = new FireActor(World, Owner!, X, Y, Facing)
        {
            ObjTimer = 0x4F,
            State = FireState.Standing
        };
        World.AddObject(fire);
    }
}

internal sealed class ArrowProjectile : Projectile
{
    private static readonly ImmutableArray<AnimationId> _arrowAnimMap = [
        AnimationId.Arrow_Right,
        AnimationId.Arrow_Left,
        AnimationId.Arrow_Down,
        AnimationId.Arrow_Up
    ];

    private int _timer;
    private readonly SpriteImage _image;

    public ArrowProjectile(World world, int x, int y, Direction direction, Actor owner)
        : base(world, ObjType.Arrow, x, y, owner)
    {
        Facing = direction;
        Decoration = 0;

        var dirOrd = direction.GetOrdinal();
        _image = new SpriteImage(TileSheet.PlayerAndItems, _arrowAnimMap[dirOrd]);
    }

    public static int PlayerCount(World world)
    {
        return world.GetObjects<ArrowProjectile>().Count(t => t.Owner.IsPlayer);
    }

    public void SetSpark(int frames = 3)
    {
        State = ProjectileState.Spark;
        _timer = frames;
        _image.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Spark);
    }

    public override void Update()
    {
        switch (State)
        {
            case ProjectileState.Flying: UpdateArrow(); break;
            case ProjectileState.Spark: UpdateSpark(); break;
            case ProjectileState.Bounce: UpdateBounce(); break;
        }
    }

    private void UpdateArrow()
    {
        if (ObjTimer != 0)
        {
            // The original game seems to do something if the owner is gone, but I don't see any effect.
            ObjTimer = 0;
            return;
        }

        if (CheckWorldMargin(Facing) == Direction.None)
        {
            SetSpark();
        }
        else
        {
            var speed = IsPlayerWeapon ? 0xC0 : 0x80;
            Move(speed);
            CheckPlayer();
        }
    }

    private void UpdateSpark()
    {
        _timer--;
        if (_timer == 0)
        {
            Delete();
        }
    }

    public override void Draw()
    {
        ReadOnlySpan<int> yOffsets = [3, 3, 0, 0];

        var pal = Palette.Blue;

        if (State != ProjectileState.Spark)
        {
            if (IsPlayerWeapon)
            {
                var itemValue = World.GetItem(ItemSlot.Arrow);
                pal = Palette.Player + itemValue - 1;
            }
            else
            {
                pal = Palette.Red;
            }
        }

        var dirOrd = Facing.GetOrdinal();
        var yOffset = yOffsets[dirOrd];

        var x = X;
        var y = Y + yOffset;

        if (State == ProjectileState.Spark && Facing.IsHorizontal())
        {
            x += 4;
        }

        _image.Draw(TileSheet.PlayerAndItems, x, y, pal);
    }
}