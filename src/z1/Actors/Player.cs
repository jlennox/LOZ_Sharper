using System.Collections.Immutable;
using z1.IO;
using z1.Render;

namespace z1.Actors;

internal enum PlayerState { Idle, Wielding, Paused }

internal static class PlayerStateExtensions
{
    // (_state & 0xC0) == 0x40;
    public static bool IsPaused(this Player.PlayerState state) => (state & Player.PlayerState.PausedMask) == Player.PlayerState.Paused;
    public static bool IsAttacking(this Player.PlayerState state) => (int)(state & Player.PlayerState.AttackMask) is 0x10 or 0x20;
}

internal sealed class PlayerParalyzedTokenSource
{
    private long _idCounter = 0;
    private readonly HashSet<long> _activeIds = new();

    public bool IsParalyzed => _activeIds.Count > 0;

    public sealed class Token : IDisposable
    {
        private readonly long _id;
        private readonly PlayerParalyzedTokenSource _source;
        private bool _isDisposed;

        public Token(long id, PlayerParalyzedTokenSource source)
        {
            _id = id;
            _source = source;
            _source._activeIds.Add(_id);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _source._activeIds.Remove(_id);
            _isDisposed = true;
        }
    }

    public Token Create()
    {
        return new Token(_idCounter++, this);
    }

    public void Clear()
    {
        _activeIds.Clear();
        _idCounter = 0;
    }
}

internal sealed class Player : Actor, IThrower
{
    public const int WalkSpeed = 0x60;
    public const int StairsSpeed = 0x30;

    private const int WalkDurationFrames = 12;

    [Flags]
    internal enum PlayerState
    {
        AttackMask = 0x30,

        Paused = 0x40,
        PausedMask = 0xC0, // I do not know what the other bit on this mask does.
    }

    public static ReadOnlySpan<byte> PlayerLimits => [0xF0, 0x00, 0xDD, 0x3D];

    private static readonly DebugLog _log = new(nameof(Player));
    private static readonly DebugLog _movementTraceLog = new(nameof(Player), "MovementTrace", DebugLogDestination.None);

    public PlayerProfile Profile { get; set; }

    public override bool IsPlayer => true;
    public override bool IsMonsterSlot => false;

    private readonly PlayerParalyzedTokenSource _paralyzedTokenSource = new();
    public bool IsParalyzed => _paralyzedTokenSource.IsParalyzed;
    public bool FromUnderground { get; set; }
    public bool CandleUsed { get; set; }

    private int _state; // JOE: TODO: Enumify this as using PlayerState.
    private byte _speed;
    private TileBehavior _tileBehavior;
    private byte _animTimer;
    private byte _avoidTurningWhenDiag;   // 56
    private byte _keepGoingStraight;      // 57
    private HashSet<GameButton> _curButtons; // JOE: TODO: Can this be dropped and instead directly access the input?

    public readonly SpriteAnimator Animator;

    public Player(World world, Direction facing = Direction.Up)
        : base(world, ObjType.Player)
    {
        Animator = new SpriteAnimator
        {
            Time = 0,
            DurationFrames = WalkDurationFrames
        };
        DrawOrder = DrawOrder.Player;

        Initialize(facing);
    }

    public void Initialize(Direction facing = Direction.Up)
    {
        _speed = WalkSpeed;
        Facing = facing;
        Decoration = 0;

        Animator.Time = 0;
        Animator.DurationFrames = WalkDurationFrames;
    }

    public void ClearParalized() => _paralyzedTokenSource.Clear();
    public PlayerParalyzedTokenSource.Token Paralyze() => _paralyzedTokenSource.Create();

    public void DecInvincibleTimer()
    {
        if (InvincibilityTimer > 0 && (Game.FrameCounter & 1) == 0)
        {
            InvincibilityTimer--;
        }
    }

    public override void Update()
    {
        // Do this in order to flash while you have the clock. It doesn't matter if it becomes zero,
        // because foes will check invincibilityTimer AND the clock item.
        // I suspect that the original game did this in the drawing code.
        var profile = World.Profile ?? throw new Exception();
        if (profile.Items.Has(ItemSlot.Clock))
        {
            InvincibilityTimer += 0x10;
        }

        _curButtons = Game.Input.GetButtons();

        // It looks like others set player's state to $40. They don't bitwise-and it with $40.
        if ((_state & 0xC0) == 0x40)
        {
            _movementTraceLog.Write("Update: Paused");
            return;
        }

        HandleInput();

        if (IsParalyzed)
        {
            Moving &= 0xF0;
        }

        if ((_state & 0xF0) != 0x10 && (_state & 0xF0) != 0x20)
        {
            Move();
        }

        if (World.GetMode() == GameMode.LeaveCellar) return;

        if (World.WhirlwindTeleporting == 0)
        {
            CheckWater();
            CheckDoorway();
            Animate();
        }

        // $6EFB
        // The original game hides part of the player if it's under an underworld doorway.
        // But, we do it differently.

        if (TileOffset == 0)
        {
            X = (X & 0xF8);
            Y = (Y & 0xF8) | 5;
        }
    }

    private void Animate()
    {
        // The original game also didn't animate if gameMode was 4 or $10
        if (_state == 0)
        {
            if (Moving != 0) Animator.Advance();
            return;
        }

        if (_animTimer != 0) _animTimer--;

        if (_animTimer == 0)
        {
            switch (_state & 0x30)
            {
                case 0x10:
                case 0x20:
                    Animator.Time = 0;
                    _animTimer = (byte)(_state & 0x0F);
                    _state |= 0x30;
                    break;

                case 0x30:
                    Animator.AdvanceFrame();
                    _state &= 0xC0;
                    break;
            }
        }
    }

    // F23C
    private void CheckWater()
    {
        var mode = World.GetMode();

        if (mode is GameMode.Leave or < GameMode.Play) return;

        if (TileOffset != 0)
        {
            if ((TileOffset & 7) != 0) return;
            TileOffset = 0;
            if (mode != GameMode.Play) return;
            World.Player.FromUnderground = false;
        }

        if (mode != GameMode.Play) return;

        if (World.IsOverworld() && !World.CurrentRoom.Settings.IsLadderAllowed) return;

        if (World.DoorwayDir != Direction.None
            || World.GetItem(ItemSlot.Ladder) == 0
            || (_state & 0xC0) == 0x40
            || World.GetLadder() != null)
        {
            return;
        }

        var collision = World.CollidesWithTileMoving(X, Y, Facing, true);

        // The original game checked for specific water tiles in the OW and UW.
        if (collision.TileBehavior != TileBehavior.Water) return;
        if (Moving == 0) return;
        if (Moving != (int)Facing) return;

        ReadOnlySpan<sbyte> ladderOffsetsX = [0x10, -0x10, 0x00, 0x00];
        ReadOnlySpan<sbyte> ladderOffsetsY = [0x03, 0x03, 0x13, -0x05];

        var dirOrd = MovingDirection.GetOrdinal();

        var ladder = new LadderActor(World, X + ladderOffsetsX[dirOrd], Y + ladderOffsetsY[dirOrd]);
        World.SetLadder(ladder);
    }

    private void CheckDoorway()
    {
        var collision = PlayerCoversTile(X, Y);

        if (collision.TileBehavior == TileBehavior.Doorway)
        {
            if (World.DoorwayDir == Direction.None)
            {
                World.DoorwayDir = Facing;
                _movementTraceLog.Write($"DoorwayDir: {World.DoorwayDir}");
            }
            return;
        }

        World.DoorwayDir = Direction.None;
    }

    private TileCollision PlayerCoversTile(int x, int y)
    {
        y += 3;

        var behavior = TileBehavior.FirstWalkable;
        var fineRow1 = (y - World.TileMapBaseY) / 8;
        var fineRow2 = (y + 15 - World.TileMapBaseY) / 8;
        var fineCol1 = x / 8;
        var fineCol2 = (x + 15) / 8;
        var hitFineCol = fineCol1;
        var hitFineRow = fineRow1;

        for (var r = fineRow1; r <= fineRow2; r++)
        {
            for (var c = fineCol1; c <= fineCol2; c++)
            {
                var curBehavior = World.GetTileBehavior(c, r);

                // TODO: this isn't the best way to check covered tiles
                //       but it'll do for now.
                if (curBehavior > behavior)
                {
                    behavior = curBehavior;
                    hitFineCol = c;
                    hitFineRow = r;
                }
            }
        }

        return new TileCollision(false, behavior, hitFineCol, hitFineRow);
    }

    private static bool IsInBorder(int coord, Direction dir, ReadOnlySpan<byte> border)
    {
        if (dir.IsHorizontal())
        {
            return coord < border[0] || coord >= border[1];
        }

        return coord < border[2] || coord >= border[3];
    }

    // $8D8C
    private void FilterBorderInput()
    {
        // These are reverse from original, because Util::GetDirectionOrd goes in the opposite dir of $7013.
        ReadOnlySpan<byte> outerBorderOW = [0x07, 0xE9, 0x45, 0xD6];
        ReadOnlySpan<byte> outerBorderUW = [0x17, 0xD9, 0x55, 0xC6];
        ReadOnlySpan<byte> innerBorder = [0x1F, 0xD1, 0x54, 0xBE];

        var coord = Facing.IsHorizontal() ? X : Y;
        var outerBorder = World.IsOverworld() ? outerBorderOW : outerBorderUW;

        if (IsInBorder(coord, Facing, outerBorder))
        {
            _curButtons.Clear();
            if (!World.IsOverworld())
            {
                var mask = Facing.IsVertical() ? Direction.VerticalMask : Direction.HorizontalMask;
                Moving = (byte)(Moving & (byte)mask);
                _movementTraceLog.Write($"{nameof(FilterBorderInput)}.outerBorder: {MovingDirection}");
            }
        }
        else if (IsInBorder(coord, Facing, innerBorder))
        {
            _curButtons.Mask(GameButton.A);
            _movementTraceLog.Write($"{nameof(FilterBorderInput)}.innerBorder: {MovingDirection}");
        }
    }

    private void HandleInput()
    {
        var fnlog = _movementTraceLog.CreateFunctionLog();
        Moving = (byte)_curButtons.GetDirection();

        if (MovingDirection != Direction.None)
        {
            fnlog.Write($"{MovingDirection}");
        }

        if (_state == 0)
        {
            FilterBorderInput();

            if (_curButtons.Contains(GameButton.A)) UseWeapon();
            if (_curButtons.Contains(GameButton.B)) UseItem();
        }

        if (ShoveDirection != 0)
        {
            fnlog.Write($"ShoveDirection != 0 {ShoveDirection}");
            return;
        }

        if (!World.IsOverworld())
        {
            SetMovingInDoorway();
        }

        if (TileOffset != 0)
        {
            Align();
        }
        else
        {
            CalcAlignedMoving();
        }
    }

    private void SetMovingInDoorway()
    {
        if (World.DoorwayDir != Direction.None && Moving != 0)
        {
            var dir = MovingDirection & Facing;
            if (dir == 0)
            {
                dir = MovingDirection & Facing.GetOppositeDirection();
                if (dir == 0)
                {
                    dir = Facing;
                }
            }
            Moving = (byte)dir;
            _movementTraceLog.Write($"{nameof(SetMovingInDoorway)}: {MovingDirection}");
        }
    }

    // $B38D
    private void Align()
    {
        var fnlog = _movementTraceLog.CreateFunctionLog();

        if (Moving == 0) return;

        var singleMoving = GetSingleMoving();

        if (singleMoving == Facing)
        {
            SetSpeed();
            fnlog.Write($"singleMoving == Facing: {singleMoving} == {Facing}");
            return;
        }

        var dir = singleMoving | Facing;
        if (dir != Direction.OppositeHorizontals && dir != Direction.OppositeVerticals)
        {
            if (_keepGoingStraight != 0)
            {
                fnlog.Write($"_keepGoingStraight != 0 {_keepGoingStraight}");
                SetSpeed();
                return;
            }

            if (Math.Abs(TileOffset) >= 4)
            {
                fnlog.Write($"Math.Abs(TileOffset) >= 4 {TileOffset}");
                return;
            }

            if (Facing.IsGrowing())
            {
                if (TileOffset < 0)
                {
                    fnlog.Write($"Facing.IsGrowing && TileOffset < 0 {TileOffset}");
                    return;
                }
            }
            else
            {
                if (TileOffset >= 0)
                {
                    fnlog.Write($"!Facing.IsGrowing && TileOffset >= 0 {TileOffset}");
                    return;
                }
            }

            Facing = Facing.GetOppositeDirection();

            TileOffset += TileOffset >= 0 ? (sbyte)-8 : (sbyte)8;
            fnlog.Write($"!Facing.IsGrowing && TileOffset >= 0 {TileOffset}");
        }
        else
        {
            fnlog.Write($"Had opposites: {dir}");
            Facing = singleMoving;
            Moving = (byte)singleMoving;
        }
    }

    // $B2CF
    private void CalcAlignedMoving()
    {
        var fnlog = _movementTraceLog.CreateFunctionLog();

        var lastDir = Direction.None;
        var lastClearDir = Direction.None;
        var dirCount = 0;
        var clearDirCount = 0;

        _keepGoingStraight = 0;

        for (var i = 0; i < 4; i++)
        {
            var dir = i.GetOrdDirection();
            if ((Moving & (int)dir) != 0)
            {
                lastDir = dir;
                dirCount++;

                var collision = World.CollidesWithTileMoving(X, Y, dir, true);
                _tileBehavior = collision.TileBehavior;
                if (!collision.Collides)
                {
                    lastClearDir = dir;
                    clearDirCount++;
                }
            }
        }

        if (dirCount == 0) return;

        fnlog.Write($"dirCount {dirCount} {MovingDirection}");

        if (dirCount == 1)
        {
            _avoidTurningWhenDiag = 0;
            fnlog.Write($"Moving = (byte)lastDir {lastDir}");
            Facing = lastDir;
            Moving = (byte)lastDir;
            SetSpeed();
            return;
        }

        if (clearDirCount == 0)
        {
            fnlog.Write($"clearDirCount");
            Moving = 0;
            return;
        }

        _keepGoingStraight++;

        if (clearDirCount == 1 || World.IsOverworld())
        {
            _avoidTurningWhenDiag = 0;
            fnlog.Write($"lastClearDir {lastClearDir}");
            Facing = lastClearDir;
            Moving = (byte)lastClearDir;
            SetSpeed();
            return;
        }

        if (X is 0x20 or 0xD0)
        {
            if (Y != 0x85 || (Facing & Direction.Down) == 0)
            {
                goto TakeFacingPerpDir;
            }
        }

        if (_avoidTurningWhenDiag == 0)
        {
            goto TakeFacingPerpDir;
        }

        if (World.IsOverworld() || X != 0x78 || Y != 0x5D)
        {
            fnlog.Write($"Moving = (byte)Facing {Facing}");
            Moving = (byte)Facing;
            SetSpeed();
            return;
        }

    // B34D
    TakeFacingPerpDir:
        // Moving dir is diagonal. Take the dir component that's perpendicular to facing.
        _avoidTurningWhenDiag++;

        ReadOnlySpan<byte> axisMasks = [3, 3, 0xC, 0xC];

        var dirOrd = Facing.GetOrdinal();
        var movingInFacingAxis = (uint)(Moving & axisMasks[dirOrd]);
        var perpMoving = Moving ^ movingInFacingAxis;
        fnlog.Write($"TakeFacingPerpDir perpMoving:{perpMoving}");
        Facing = (Direction)perpMoving;
        Moving = (byte)perpMoving;
        SetSpeed();
    }

    public void ApplyEntryPosition(EntryPosition? entry, int defaultX, int defaultY, out int? targetX, out int? targetY)
    {
        if (entry == null)
        {
            X = defaultX;
            Y = defaultY;
            targetX = null;
            targetY = null;
            return;
        }

        X = entry.X;
        Y = entry.Y;
        targetX = entry.TargetX;
        targetY = entry.TargetY;
        if (entry.Facing != Direction.None) Facing = entry.Facing;
    }

    // $B366
    private void SetSpeed()
    {
        byte newSpeed = WalkSpeed;

        if (World.IsOverworld())
        {
            if (_tileBehavior == TileBehavior.SlowStairs)
            {
                newSpeed = StairsSpeed;
                if (_speed != newSpeed)
                {
                    Fraction = 0;
                }
            }
        }

        _speed = newSpeed;
    }

    private static readonly ImmutableArray<AnimationId> _thrustAnimMap = [
        AnimationId.PlayerThrust_Right,
        AnimationId.PlayerThrust_Left,
        AnimationId.PlayerThrust_Down,
        AnimationId.PlayerThrust_Up
    ];

    private static readonly ImmutableArray<ImmutableArray<AnimationId>> _animMap = [
        [
            AnimationId.PlayerWalk_NoShield_Right,
            AnimationId.PlayerWalk_NoShield_Left,
            AnimationId.PlayerWalk_NoShield_Down,
            AnimationId.PlayerWalk_NoShield_Up
        ],
        [
            AnimationId.PlayerWalk_LittleShield_Right,
            AnimationId.PlayerWalk_LittleShield_Left,
            AnimationId.PlayerWalk_LittleShield_Down,
            AnimationId.PlayerWalk_LittleShield_Up
        ],
        [
            AnimationId.PlayerWalk_BigShield_Right,
            AnimationId.PlayerWalk_BigShield_Left,
            AnimationId.PlayerWalk_BigShield_Down,
            AnimationId.PlayerWalk_BigShield_Up
        ]
    ];

    private void SetFacingAnim()
    {
        var shieldState = World.GetItem(ItemSlot.MagicShield) + 1;
        var dirOrd = Facing.GetOrdinal();
        var map = (_state & 0x30) == 0x10 || (_state & 0x30) == 0x20 ? _thrustAnimMap : _animMap[shieldState];
        Animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, map[dirOrd]);
    }

    public override void Draw()
    {
        if (!Visible) return;

        var palette = CalcPalette(Palette.Player);
        var y = Y;

        if (World.IsOverworld() || World.GetMode() == GameMode.PlayCellar)
        {
            y += 2;
        }

        SetFacingAnim();
        Animator.Draw(TileSheet.PlayerAndItems, X, y, palette, DrawOrder);
    }

    public Actors.PlayerState GetState()
    {
        if ((_state & 0xC0) == 0x40) return Actors.PlayerState.Paused;
        if ((_state & 0xF0) != 0) return Actors.PlayerState.Wielding;
        return Actors.PlayerState.Idle;
    }

    public void SetState(Actors.PlayerState state)
    {
        _state = state switch {
            Actors.PlayerState.Paused => 0x40,
            Actors.PlayerState.Idle => 0,
            _ => _state
        };
    }

    public Rectangle GetBounds() => new(X, Y + 8, 16, 8);

    public Point GetMiddle()
    {
        // JOE: This seems silly vs Actor's perhaps having a Width?
        return new Point(X + 8, Y + 8);
    }

    public void ResetShove()
    {
        ShoveDirection = Direction.None;
        ShoveDistance = 0;
    }

    public void Catch()
    {
        // Original game:
        //   player.state := player.state | $20
        //   player.animTimer := 1

        if (_state == 0)
        {
            Animator.Time = 0;
            _animTimer = 6;
            _state = 0x26;
        }
        else
        {
            Animator.Time = 0;
            _animTimer = 1;
            _state = 0x30;
        }
    }

    public void BeHarmed(Actor collider)
    {
        // The original sets [$C] here. [6] was already set to the result of DoObjectsCollide.
        // [$C] takes on the same values as [6], so I don't know why it was needed.

        // var damage = collider.PlayerDamage;
        var damage = Game.Data.GetObjectAttribute(collider.ObjType).Damage;
        BeHarmed(collider, damage);
    }

    // JOE: NOTE: This used to have a `Point& otherMiddle` argument that was unused?
    public void BeHarmed(Actor collider, int damage)
    {
        _log.Write($"BeHarmed: {collider.GetType().Name} dealt {damage} damage.");

        if (Game.Cheats.GodMode) return;

        if (collider is not WhirlwindActor)
        {
            Game.Sound.PlayEffect(SoundEffect.PlayerHit);
        }

        var ringValue = World.Profile.Items.Get(ItemSlot.Ring);

        damage >>= ringValue;

        World.ResetKilledObjectCount();
        collider.ObjectStatistics.DamageTaken += damage;

        if (World.Profile.Hearts <= damage)
        {
            World.Profile.Hearts = 0;
            _state = 0;
            Facing = Direction.Down;
            World.GotoDie();
        }
        else
        {
            World.Profile.Hearts -= damage;
        }
    }

    public void Stop()
    {
        _state = 0;
        ShoveDirection = Direction.None;
        ShoveDistance = 0;
        InvincibilityTimer = 0;
    }

    public void MoveLinear(Direction dir, int speed)
    {
        if ((TileOffset & 7) == 0)
        {
            TileOffset = 0;
        }
        MoveDirection(speed, dir);
    }

    //====================================================================================
    //  UseItem
    //====================================================================================

    public int UseCandle(int x, int y, Direction facingDir)
    {
        var itemValue = World.GetItem(ItemSlot.Candle);
        if (itemValue == 1 && World.Player.CandleUsed) return 0;

        World.Player.CandleUsed = true;

        var count = World.CountObjects<FireActor>();
        var allowed = World.GetItem(ItemSlot.MaxConcurrentProjectiles);

        if (count >= allowed) return 0;

        MoveSimple(ref x, ref y, facingDir, 0x10);

        var fire = new FireActor(World, this, x, y, facingDir);
        World.AddObject(fire);
        Game.Sound.PlayEffect(SoundEffect.Fire);
        return 12;
    }

    private int UseBomb(int x, int y, Direction facingDir)
    {
        var bombs = World.GetObjects<BombActor>();
        var allowed = World.GetItem(ItemSlot.MaxConcurrentProjectiles);
        var stableCount = bombs.Count(b => b.BombState < BombState.Blasting);

        if (stableCount >= allowed) return 0;

        MoveSimple(ref x, ref y, facingDir, 0x10);

        var bomb = new BombActor(World, this, x, y);
        World.AddObject(bomb);
        World.DecrementItem(ItemSlot.Bombs);
        Game.Sound.PlayEffect(SoundEffect.PutBomb);
        return 7;
    }

    private int UseBoomerang(int x, int y, Direction facingDir)
    {
        // ORIGINAL: Trumps food. Look at $05:8E40. The behavior is tied to the statement below.
        //           Skip throw, if there's already a boomerang in the slot. But overwrite Food.
        var count = BoomerangProjectile.PlayerCount(World);
        var allowed = World.GetItem(ItemSlot.MaxConcurrentProjectiles);
        if (count >= allowed) return 0;

        var itemValue = World.GetItem(ItemSlot.Boomerang);

        MoveSimple(ref x, ref y, facingDir, 0x10);

        if (MovingDirection != Direction.None)
        {
            facingDir = MovingDirection;
        }

        var distance = itemValue == 2 ? BoomerangProjectile.RedsDistance : BoomerangProjectile.YellowsDistance;
        var boomerang = Projectile.MakeBoomerang(World, x, y, facingDir, distance, 3.0f, this);
        World.AddObject(boomerang);
        return 6;
    }

    private int UseArrow(int x, int y, Direction facingDir)
    {
        if (World.GetItem(ItemSlot.Rupees) == 0) return 0;

        var count = ArrowProjectile.PlayerCount(World);
        var allowed = World.GetItem(ItemSlot.MaxConcurrentProjectiles);
        if (count >= allowed) return 0;

        World.PostRupeeLoss(1);

        MoveSimple(ref x, ref y, facingDir, 0x10);

        if (facingDir.IsVertical())
        {
            x += 3;
        }

        var bowValue = World.GetItem(ItemSlot.Bow);
        var arrowValue = World.GetItem(ItemSlot.Arrow);
        var arrowDamage = ArrowProjectile.GetDamage(arrowValue);

        var arrowOptions = ProjectileOptions.None;
        if (bowValue > 1) arrowOptions |= ProjectileOptions.Piercing;

        var arrow = Projectile.MakeProjectile(World, ObjType.Arrow, x, y, facingDir, arrowDamage, arrowOptions, this);
        World.AddObject(arrow);
        Game.Sound.PlayEffect(SoundEffect.Boomerang);
        return 6;
    }

    private int UseFood(int x, int y, Direction facingDir)
    {
        if (World.HasObject<FoodActor>()) return 0;

        MoveSimple(ref x, ref y, facingDir, 0x10);

        var food = new FoodActor(World, x, y);
        World.AddObject(food);
        return 6;
    }

    private int UsePotion(int _x, int _y, Direction _facingDir)
    {
        World.DecrementItem(ItemSlot.Potion);
        World.PauseFillHearts();
        return 0;
    }

    private int UseRecorder(int _x, int _y, Direction _facingDir)
    {
        World.UseRecorder();
        return 0;
    }

    private int UseLetter(int _x, int _y, Direction _facingDir)
    {
        var itemValue = World.GetItem(ItemSlot.Letter);
        if (itemValue != 1) return 0;

        var obj = World.GetObject(static obj => obj.ObjType == ObjType.Cave11MedicineShop);
        if (obj == null) return 0;

        World.SetItem(ItemSlot.Letter, 2);
        return 0;
    }

    // JOE: NOTE: Return value is properly unused?
    private int UseItem()
    {
        var profile = World.Profile;
        if (profile.SelectedItem == 0) return 0;

        var itemValue = profile.Items.Get(profile.SelectedItem);
        if (itemValue == 0) return 0;

        if (profile.SelectedItem == ItemSlot.Rod)
        {
            return UseWeapon(ObjType.Rod, ItemSlot.Rod);
        }

        // JOE: NOTE: These waitFrames appear unused?
        var waitFrames = profile.SelectedItem switch {
            ItemSlot.Bombs => UseBomb(X, Y, Facing),
            ItemSlot.Arrow => UseArrow(X, Y, Facing),
            ItemSlot.Candle => UseCandle(X, Y, Facing),
            ItemSlot.Recorder => UseRecorder(X, Y, Facing),
            ItemSlot.Food => UseFood(X, Y, Facing),
            ItemSlot.Potion => UsePotion(X, Y, Facing),
            ItemSlot.Letter => UseLetter(X, Y, Facing),
            ItemSlot.Boomerang => UseBoomerang(X, Y, Facing),
            _ => 0
        };

        if (waitFrames == 0) return 0;
        Animator.Time = 0;
        _animTimer = 6;
        _state = 0x16;
        profile.Statistics.AddItemUse(profile.SelectedItem);
        return waitFrames + 6;
    }

    private int UseWeapon()
    {
        if (World.SwordBlocked || World.GetStunTimer(StunTimerSlot.NoSword) != 0)
        {
            return 0;
        }

        return UseWeapon(ObjType.PlayerSword, ItemSlot.Sword);
    }

    private int UseWeapon(ObjType type, ItemSlot itemSlot)
    {
        if (!World.HasItem(itemSlot)) return 0;
        if (World.HasObject<PlayerSwordActor>()) return 0;

        World.Profile.Statistics.AddItemUse(itemSlot);

        // The original game did this:
        //   player.animTimer := 1
        //   player.state := $10
        Animator.Time = 0;
        _animTimer = 12;
        _state = 0x11;

        var sword = new PlayerSwordActor(World, type, this);
        World.AddObject(sword);
        Game.Sound.PlayEffect(SoundEffect.Sword);
        return 13;
    }

    private void Move()
    {
        if (ShoveDirection != 0)
        {
            ObjShove();
            return;
        }

        var dir = Direction.None;

        if (TileOffset == 0)
        {
            if (Moving != 0)
            {
                var dirOrd = MovingDirection.GetOrdinal();
                dir = dirOrd.GetOrdDirection();
            }
        }
        else if (Moving != 0)
        {
            dir = Facing;
        }

        dir &= Direction.DirectionMask;

        // blocks, personal wall, leave cellar, world margin, doorways
        // tile collision, ladder

        // Original: [$E] := 0
        // Maybe it's only done to set up the call to FindUnblockedDir in CheckTileCollision?

        // The original game resets ~moving~ here, if player's major state is $10 or $20.
        // What we do instead in that case is to avoid calling ObjMove in Player. I think
        // that it's clearer this way.

        dir = StopAtBlock(dir);
        dir = StopAtPersonWallUW(dir);

        if (World.DoorwayDir == Direction.None)
        {
            var mode = World.GetMode();

            if (mode is GameMode.PlayCellar or GameMode.PlayCave or GameMode.PlayShortcuts)
            {
                dir = CheckSubroom(dir);
            }

            // We now check walls using tiles and their behaviors.
        }

        // We now check doorways using tiles and their behaviors.

        dir = CheckTileCollision(dir);
        dir = HandleLadder(dir);

        MoveDirection(_speed * (Game.Cheats.SpeedUp ? 3 : 1), dir);
    }

    // 8ED7
    private Direction CheckSubroom(Direction dir)
    {
        var mode = World.GetMode();

        if (mode == GameMode.PlayCellar)
        {
            if (Y >= 0x40 || (MovingDirection & Direction.Up) == 0)
            {
                return dir;
            }

            World.ReturnToPreviousEntrance();
            dir = Direction.None;
            StopPlayer();
        }
        else    // Shop
        {
            // This is an overworld shop, so having the hard check here is proper.
            dir = StopAtPersonWall(dir);

            // Handling 3 shortcut stairs in shortcut cave is handled by the Person obj, instead of here.

            if (HitsWorldLimit())
            {
                World.ReturnToPreviousEntrance();
                dir = Direction.None;
                StopPlayer();
            }
        }

        return dir;
    }

    // 8F7B
    private Direction HandleLadder(Direction dir)
    {
        var ladder = World.GetLadder();
        if (ladder == null) return dir;

        // Original: if ladder.GetState() = 0, destroy it. But, I don't see how it can get in that state.

        var distance = 0;

        if (ladder.Facing.IsVertical())
        {
            if (X != ladder.X)
            {
                World.RemoveLadder();
                return dir;
            }
            distance = (Y + 3) - ladder.Y;
        }
        else
        {
            if ((Y + 3) != ladder.Y)
            {
                World.RemoveLadder();
                return dir;
            }
            distance = X - ladder.X;
        }

        distance = Math.Abs(distance);

        if (distance < 0x10)
        {
            ladder.State = LadderStates.Unknown2;
            dir = MoveOnLadder(dir, distance);
        }
        else if (distance != 0x10 || Facing != ladder.Facing)
        {
            World.RemoveLadder();
        }
        else if (ladder.State == LadderStates.Unknown1)
        {
            dir = MoveOnLadder(dir, distance);
        }
        else
        {
            World.RemoveLadder();
        }

        return dir;
    }

    // $05:8FCD
    private Direction MoveOnLadder(Direction dir, int distance)
    {
        if (Moving == 0) return Direction.None;

        var ladder = World.GetLadder() ?? throw new Exception();
        if (distance != 0 && Facing == ladder.Facing) return Facing;
        if (ladder.Facing == dir) return dir;

        var oppositeDir = ladder.Facing.GetOppositeDirection();
        if (oppositeDir == Facing) return oppositeDir;

        if (oppositeDir != Direction.Down || MovingDirection != Direction.Up)
        {
            return Direction.None;
        }

        // At this point, ladder faces up, and player moving up.

        dir = MovingDirection;

        if (World.CollidesWithTileMoving(X, Y - 8, dir, true)) return Direction.None;

        // ORIGINAL: The routine will run again. It'll finish, because now (ladder.facing = dir),
        //           which is one of the conditions that ends this function.
        //           But, why not return dir right here?
        return MoveOnLadder(dir, distance);
    }

    // $01:A13E  stop object, if too close to a block
    private Direction StopAtBlock(Direction dir)
    {
        if (Game.Cheats.NoClip) return dir;

        foreach (var block in World.GetObjects().OfType<IHasCollision>())
        {
            if (block.CheckCollision(this) == CollisionResponse.Blocked)
            {
                _movementTraceLog.Write($"StopAtBlock: {block}");
                return Direction.None;
            }
        }
        return dir;
    }

    private new Direction CheckTileCollision(Direction dir) // JOE: TODO: Is this supposed to be "new"'ed?
    {
        if (World.DoorwayDir != Direction.None) return CheckWorldBounds(dir);
        // Original, but seemingly never triggered: if [$E] < 0, leave

        if (TileOffset != 0) return dir;

        return dir != Direction.None ? FindUnblockedDir(dir) : dir;
    }

    private bool HitsWorldLimit()
    {
        if (Moving != 0)
        {
            var dirOrd = MovingDirection.GetOrdinal();
            var singleMoving = dirOrd.GetOrdDirection();
            var coord = singleMoving.IsVertical() ? Y : X;

            // JOE: I believe it's important for this to be a "==" instead a proper greater/less than check, because
            // I _believe_ this is what allows out of bounds clipping?
            if (coord == PlayerLimits[dirOrd])
            {
                Facing = singleMoving;
                return true;
            }
        }
        return false;
    }

    private void StopPlayer()
    {
        Stop();
    }

    // F14E
    protected override Direction CheckWorldBounds(Direction dir)
    {
        if (World.GetMode() == GameMode.Play
            && World.GetLadder() == null
            && TileOffset == 0)
        {
            if (HitsWorldLimit())
            {
                // JOE: TODO: MAP REWRITE
                // Is LeaveRoom depricated? I wouldn't think so.
                World.LeaveRoom(Facing, World.CurrentRoom);
                dir = Direction.None;
                StopPlayer();
            }
        }

        return dir;
    }

    private Direction FindUnblockedDir(Direction dir)
    {
        // JOE TODO 10/22/2025: This code seems weird?
        var collision = World.CollidesWithTileMoving(X, Y, dir, true);
        if (!collision.Collides)
        {
            dir = CheckWorldBounds(dir);
            return dir;
        }

        PushOWTile(collision);

        dir = Direction.None;
        // ORIGINAL: [$F8] := 0
        return World.IsOverworld() ? CheckWorldBounds(dir) : dir;
    }

    // $01:A223
    private void PushOWTile(TileCollision collision)
    {
        if (TileOffset != 0 || Moving == 0) return;

        // This isn't anologous to the original's code, but the effect is the same.
        World.PushTile(collision.FineRow, collision.FineCol);
    }
}
