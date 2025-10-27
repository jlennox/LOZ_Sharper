using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using z1.IO;
using z1.Render;

namespace z1.Actors;

[Flags]
internal enum DamageType
{
    Sword = 1,
    Boomerang = 2,
    Arrow = 4,
    Bomb = 8,
    MagicWave = 0x10,
    Fire = 0x20,
}

[DebuggerDisplay("{ObjType} ({X},{Y})")]
internal abstract partial class Actor
{
    private static readonly DebugLog _log = new(nameof(Actor));
    private static readonly DebugLog _traceLog = new(nameof(Actor), DebugLogDestination.DebugBuildsOnly);
    private static long _idCounter;

    public Game Game => World.Game;
    public World World { get; }
    public readonly long Id = _idCounter++;

    public Point Position
    {
        get => new(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    public int X { get; set; }
    public int Y { get; set; }

    public bool Visible { get; set; } = true;
    public DrawOrder DrawOrder { get; set; } = DrawOrder.Sprites;

    public bool IsDeleted => _isDeleted;

    private bool _isDeleted;
    private bool _ranDeletedEvent;

    public byte Decoration = 1;
    protected byte HP;
    public byte InvincibilityTimer;
    protected byte InvincibilityMask;
    protected Direction ShoveDirection = Direction.None;
    protected byte ShoveDistance;
    public Direction Facing = Direction.None;
    public sbyte TileOffset;
    protected byte Fraction;
    public byte Moving;
    public Direction MovingDirection
    {
        get => (Direction)Moving;
        set => Moving = (byte)value;
    }

    public byte ObjTimer { get; set; }

    protected byte StunTimer;

    public ObjType ObjType { get; }
    public ObjectAttribute Attributes => Game.Data.GetObjectAttribute(ObjType);
    public ObjectStatistics ObjectStatistics => World.Profile.Statistics.GetObjectStatistics(this);
    public Actor? Owner { get; protected set; }

    // Any non-player actors that can pick up an item and count as the player having collected. IE, arrows.
    // This does not apply to room items.
    public virtual bool CanPickupItem => false;

    protected bool IsStunned => _isStunned();

    protected Actor(World world, ObjType type, int x = 0, int y = 0)
    {
        if (type == ObjType.None) throw new ArgumentOutOfRangeException(nameof(type));

        World = world;
        ObjType = type;
        Position = new Point(x, y);

        _traceLog.Write($"Created {GetType().Name} at {X:X2},{Y:X2}");

        // JOE: "monsters and persons not armos or flying ghini"
        if (type < ObjType.PersonEnd
            && type is not (ObjType.Armos or ObjType.FlyingGhini))
        {
            // JOE: This might not be entirely correct...
            // var time = World.CurObjSlot + 1;
            var time = World.CountObjects() + 1;
            ObjTimer = (byte)time;
        }

        HP = (byte)Game.Data.GetObjectAttribute(ObjType).HitPoints;
    }

    ~Actor()
    {
        _log.Error($"Finalizer for {GetType().Name}/{ObjType} called.");
    }

    public abstract void Update();
    public abstract void Draw(Graphics graphics);

    public virtual bool IsPlayer => false;
    public virtual bool ShouldStopAtPersonWall => false;
    public virtual bool CountsAsLiving =>
        ObjType < ObjType.Bubble1
        || (ObjType > ObjType.Bubble3 && ObjType < ObjType.Trap);

    public virtual bool CanHoldRoomItem => false;
    public virtual bool IsReoccuring => true;
    public virtual bool IsAttrackedToMeat => false;

    // The intent here might not be immediately clear, because it really indicates if this object
    // would have been in a monster slot in the original code.
    public abstract bool IsMonsterSlot { get; }

    public ItemObjActor? HoldingItem { get; set; }

    // Returns true if the child's delete method should run.
    public virtual bool Delete()
    {
        if (!_ranDeletedEvent)
        {
            GC.SuppressFinalize(this);
            _ranDeletedEvent = true;
            _isDeleted = true;
            return true;
        }

        return false;
    }

    public static Actor AddFromType(ObjType type, World world, int x, int y)
    {
        // Some object constructors add themselves already, making generic object construction need to not double-add.
        var actor = FromType(type, world, x, y);
        world.AddUniqueObject(actor);
        return actor;
    }

    public static Actor FromType(ObjType type, World world, int x, int y)
    {
        return type switch
        {
            ObjType.BlueLynel => LynelActor.Make(world, ActorColor.Blue, x, y),
            ObjType.RedLynel => LynelActor.Make(world, ActorColor.Red, x, y),
            ObjType.BlueMoblin => MoblinActor.Make(world, ActorColor.Blue, x, y),
            ObjType.RedMoblin => MoblinActor.Make(world, ActorColor.Red, x, y),
            ObjType.BlueGoriya => GoriyaActor.Make(world, ActorColor.Blue, x, y),
            ObjType.RedGoriya => GoriyaActor.Make(world, ActorColor.Red, x, y),
            ObjType.RedSlowOctorock => OctorokActor.Make(world, ActorColor.Red, false, x, y),
            ObjType.RedFastOctorock => OctorokActor.Make(world, ActorColor.Red, true, x, y),
            ObjType.BlueSlowOctorock => OctorokActor.Make(world, ActorColor.Blue, false, x, y),
            ObjType.BlueFastOctorock => OctorokActor.Make(world, ActorColor.Blue, true, x, y),
            ObjType.RedDarknut => DarknutActor.Make(world, ActorColor.Red, x, y),
            ObjType.BlueDarknut => DarknutActor.Make(world, ActorColor.Blue, x, y),
            ObjType.BlueTektite => TektiteActor.Make(world, ActorColor.Blue, x, y),
            ObjType.RedTektite => TektiteActor.Make(world, ActorColor.Red, x, y),
            ObjType.BlueLeever => new BlueLeeverActor(world, x, y),
            ObjType.RedLeever => new RedLeeverActor(world, x, y),
            ObjType.Zora => new ZoraActor(world, x, y),
            ObjType.Vire => new VireActor(world, x, y),
            ObjType.Zol => new ZolActor(world, x, y),
            ObjType.Gel => new GelActor(world, ObjType.Gel, x, y, Direction.None, 0),
            ObjType.PolsVoice => new PolsVoiceActor(world, x, y),
            ObjType.LikeLike => new LikeLikeActor(world, x, y),
            ObjType.Peahat => new PeahatActor(world, x, y),
            ObjType.BlueKeese => KeeseActor.Make(world, ActorColor.Blue, x, y),
            ObjType.RedKeese => KeeseActor.Make(world, ActorColor.Red, x, y),
            ObjType.BlackKeese => KeeseActor.Make(world, ActorColor.Black, x, y),
            ObjType.Armos => new ArmosActor(world, x, y),
            ObjType.Boulders => new BouldersActor(world, x, y),
            ObjType.Boulder => new BoulderActor(world, x, y),
            ObjType.Ghini => new GhiniActor(world, x, y),
            ObjType.FlyingGhini => new FlyingGhiniActor(world, x, y),
            ObjType.BlueWizzrobe => new BlueWizzrobeActor(world, x, y),
            ObjType.RedWizzrobe => new RedWizzrobeActor(world, x, y),
            ObjType.Wallmaster => new WallmasterActor(world, x, y),
            ObjType.Rope => new RopeActor(world, x, y),
            ObjType.Stalfos => new StalfosActor(world, x, y),
            ObjType.Bubble1 => new BubbleActor(world, ObjType.Bubble1, x, y),
            ObjType.Bubble2 => new BubbleActor(world, ObjType.Bubble2, x, y),
            ObjType.Bubble3 => new BubbleActor(world, ObjType.Bubble3, x, y),
            ObjType.Whirlwind => new WhirlwindActor(world, x, y),
            ObjType.PondFairy => new PondFairyActor(world),
            ObjType.Gibdo => new GibdoActor(world, x, y),
            ObjType.ThreeDodongos => DodongoActor.Make(world, 3, x, y),
            ObjType.OneDodongo => DodongoActor.Make(world, 1, x, y),
            ObjType.BlueGohma => GohmaActor.Make(world, ActorColor.Blue),
            ObjType.RedGohma => GohmaActor.Make(world, ActorColor.Red),
            ObjType.RupieStash => RupeeStashActor.Make(world),
            ObjType.Princess => PrincessActor.Make(world),
            ObjType.Digdogger1 => DigdoggerActor.Make(world, x, y, 3),
            ObjType.Digdogger2 => DigdoggerActor.Make(world, x, y, 1),
            ObjType.RedLamnola => LamnolaActor.MakeSet(world, ActorColor.Red),
            ObjType.BlueLamnola => LamnolaActor.MakeSet(world, ActorColor.Blue),
            ObjType.Manhandla => ManhandlaActor.Make(world, x, y),
            ObjType.Aquamentus => new AquamentusActor(world),
            ObjType.Ganon => new GanonActor(world, x, y),
            ObjType.GuardFire => new GuardFireActor(world, x, y),
            ObjType.StandingFire => new StandingFireActor(world, x, y),
            ObjType.Moldorm => MoldormActor.MakeSet(world),
            ObjType.Gleeok1 => GleeokActor.Make(world, 1),
            ObjType.Gleeok2 => GleeokActor.Make(world, 2),
            ObjType.Gleeok3 => GleeokActor.Make(world, 3),
            ObjType.Gleeok4 => GleeokActor.Make(world, 4),
            ObjType.Patra1 => PatraActor.MakePatra(world, PatraType.Circle),
            ObjType.Patra2 => PatraActor.MakePatra(world, PatraType.Spin),
            ObjType.Trap => TrapActor.MakeSet(world, 6),
            ObjType.TrapSet4 => TrapActor.MakeSet(world, 4),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid type."),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinDistance(Actor otherActor, int distance)
    {
        var dx = otherActor.X - X;
        var dy = otherActor.Y - Y;
        return dx * dx + dy * dy < distance * distance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinDistance(int x, int y, int distance)
    {
        var dx = x - X;
        var dy = y - Y;
        return dx * dx + dy * dy < distance * distance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsWithinBoundsInclusive(int x, int y, int width, int height)
    {
        var dx = Math.Abs(x - X);
        if (dx > width) return false;

        var dy = Math.Abs(y - Y);
        if (dy > height) return false;

        return true;
    }

    public bool IsMovingToward(Actor movingActor, Direction direction)
    {
        if (direction.HasFlag(Direction.Right)) return X > movingActor.X;
        if (direction.HasFlag(Direction.Left)) return X < movingActor.X;
        if (direction.HasFlag(Direction.Down)) return Y > movingActor.Y;
        if (direction.HasFlag(Direction.Up)) return Y < movingActor.Y;
        return false;
    }

    public bool DoesCover(Actor actorBeingCovered) => DoesCover(actorBeingCovered.X, actorBeingCovered.Y);
    public bool DoesCover(int x, int y)
    {
        // JOE: TODO: Should this be logically identical to PlayerCoversTile?
        // This logic is adapted from CheckWarp

        if (World.GetMode() != GameMode.Play) return false;

        // NOTE: This check is because level 6 is double wide. We need to pass width and height into this.
        if (World.IsOverworld() && false) // JOE: TODO: MAP REWRITE This appears to the level 6 entrance...? World.CurRoomId == 0x22)
        {
            if ((X & 7) != 0) return false;
        }
        else
        {
            if ((X & 0x0F) != 0) return false;
        }

        if ((Y & 0x0F) != 0x0D) return false;

        var fineCol = X / World.TileWidth;
        var fineCol2 = x / World.TileWidth;
        if (fineCol != fineCol2) return false;

        var fineRow = (Y + 3 - 0x40) / World.TileHeight;
        var fineRow2 = (y + 3 - 0x40) / World.TileHeight;
        return fineRow == fineRow2;
    }


    public Actor GetRootOwner()
    {
        var cur = this;
        for (; cur.Owner != null; cur = cur.Owner) { }
        return cur;
    }

    public static void MoveSimple(ref int x, ref int y, Direction dir, int speed)
    {
        switch (dir)
        {
            case Direction.Right: x += speed; break;
            case Direction.Left: x -= speed; break;
            case Direction.Down: y += speed; break;
            case Direction.Up: y -= speed; break;
        }
    }

    public void MoveSimple(Direction dir, int speed)
    {
        var x = X;
        var y = Y;
        MoveSimple(ref x, ref y, dir, speed);
        X = x;
        Y = y;
    }

    public static void MoveSimple8(ref float x, ref float y, Direction dir, float speed)
    {
        switch (dir & (Direction.Right | Direction.Left))
        {
            case Direction.Right: x += speed; break;
            case Direction.Left: x -= speed; break;
        }

        switch (dir & (Direction.Down | Direction.Up))
        {
            case Direction.Down: y += speed; break;
            case Direction.Up: y -= speed; break;
        }
    }

    public static void MoveSimple8(ref int x, ref int y, Direction dir, int speed)
    {
        switch (dir & (Direction.Right | Direction.Left))
        {
            case Direction.Right: x += speed; break;
            case Direction.Left: x -= speed; break;
        }

        switch (dir & (Direction.Down | Direction.Up))
        {
            case Direction.Down: y += speed; break;
            case Direction.Up: y -= speed; break;
        }
    }

    public void MoveSimple8(Direction dir, int speed)
    {
        var x = X;
        var y = Y;
        MoveSimple8(ref x, ref y, dir, speed);
        X = x;
        Y = y;
    }

    public static SizeF MoveSimple8(Direction dir, float speed)
    {
        var x = (dir & (Direction.Right | Direction.Left)) switch {
            Direction.Right => speed,
            Direction.Left => -speed,
            _ => 0f
        };

        var y = (dir & (Direction.Down | Direction.Up)) switch {
            Direction.Down => speed,
            Direction.Up => -speed,
            _ => 0f
        };

        return new SizeF(x, y);
    }

    protected void InitCommonFacing()
    {
        InitCommonFacing(X, Y, ref Facing);
    }

    private void InitCommonFacing(int x, int y, ref Direction facing)
    {
        if (facing != Direction.None) return;

        var playerPos = World.GetObservedPlayerPos();
        // Why did the original game test these distances as unsigned?
        var xDist = playerPos.X - x;
        var yDist = playerPos.Y - y;

        if (xDist <= yDist)
        {
            // Why is this away from the player, while for Y it's toward the player?
            facing = playerPos.X > x ? Direction.Left : Direction.Right;
        }
        else
        {
            facing = playerPos.Y > y ? Direction.Down : Direction.Up;
        }
    }

    protected void InitCommonStateTimer()
    {
        // JOE: This might not be entirely correct...
        // var t = World.CurObjSlot;
        var t = World.CountObjects();
        t = (t + 2) * 16;
        ObjTimer = (byte)t;
    }

    public void DecrementObjectTimer()
    {
        if (ObjTimer != 0) ObjTimer--;
    }

    public void DecrementStunTimer()
    {
        if (StunTimer != 0) StunTimer--;
    }

    public bool DecoratedUpdate()
    {
        if (_isDeleted) return true;

        if (InvincibilityTimer > 0 && (Game.FrameCounter & 1) == 0)
        {
            InvincibilityTimer--;
        }

        if (HoldingItem != null && !HoldingItem.IsDeleted)
        {
            HoldingItem.X = X;
            HoldingItem.Y = Y;
        }

        if (Decoration == 0)
        {
            Update();

            if (Decoration == 0)
            {
                if (IsMonsterSlot && !Attributes.HasCustomCollision)
                {
                    // ORIGINAL: flag 4 if custom draw. If not set, then call $77D4.
                    // But, this stock drawing code is used for very few objects. This includes
                    // lynel, moblin, goriya, and some env objects like block. See $ECFE.

                    CheckCollisions();
                }
            }
        }
        else if (Decoration == 0x10)
        {
            ObjTimer = 6;
            Decoration++;
        }
        else
        {
            if (ObjTimer == 0)
            {
                ObjTimer = 6;
                Decoration++;
            }

            if (Decoration == 4)
            {
                Decoration = 0;
            }
            else if (Decoration == 0x14)
            {
                Delete();
                return true;
            }
        }
        return false;
    }

    public void DecoratedDraw(Graphics graphics)
    {
        if (!Visible) return;

        if (Decoration == 0)
        {
            Draw(graphics);
        }
        else if (Decoration < 0x10)
        {
            var frame = Decoration - 1;
            var animator = Graphics.GetSpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Cloud);
            animator.DrawFrame(graphics, TileSheet.PlayerAndItems, X, Y, Palette.Blue, frame, DrawOrder);
        }
        else
        {
            var counter = (Game.FrameCounter >> 1) & 3;
            var frame = (Decoration + 1) % 2;
            var pal = Palette.Player + (Global.ForegroundPalCount - counter - 1);
            var animator = Graphics.GetSpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Sparkle);
            animator.DrawFrame(graphics, TileSheet.PlayerAndItems, X, Y, pal, frame, DrawOrder);
        }
    }

    protected Palette CalcPalette(Palette wantedPalette)
    {
        if (InvincibilityTimer == 0) return wantedPalette;
        return Palette.Player + (InvincibilityTimer & 3);
    }

    protected bool _isStunned()
    {
        if (World.HasItem(ItemSlot.Clock)) return true;
        return StunTimer != 0;
    }

    protected bool CheckCollisions()
    {
        if (!Attributes.IsInvincibleToWeapons)
        {
            if (InvincibilityTimer != 0) return false;

            CheckBoomerang();
            CheckWave();
            CheckBombAndFire();
            CheckSword();
            CheckArrow();
        }

        return CheckPlayerCollision();

        // The original game checked special Wallmaster, Like-Like, and Goriya states here.
        // But, we check instead in each of those classes.
    }

    protected void CheckBoomerang()
    {
        foreach (var rang in World.GetObjects<BoomerangProjectile>())
        {
            if (!rang.IsPlayerWeapon) continue;
            CheckBoomerang(rang);
        }
    }

    private void CheckBoomerang(BoomerangProjectile rang)
    {
        var box = new Point(0xA, 0xA);
        var weaponCenter = new Point(4, 8);
        var context = new CollisionContext(rang, DamageType.Boomerang, 0, Point.Empty);

        CheckCollisionCustomNoShove(context, box, weaponCenter);
    }

    protected void CheckWave()
    {
        // ToArray() to allow enumeration changes.
        foreach (var projectile in World.GetObjects<Projectile>().ToArray())
        {
            if (!projectile.IsPlayerWeapon) continue;
            CheckWave(projectile);
        }
    }

    private void CheckWave(Projectile weaponObj)
    {
        if (weaponObj is PlayerSwordProjectile wave)
        {
            if (wave.State != ProjectileState.Flying) return;
        }

        var box = new Point(0xC, 0xC);
        var damageType = weaponObj is MagicWaveProjectile ? DamageType.MagicWave : DamageType.Sword;
        var context = new CollisionContext(weaponObj, damageType, weaponObj.Damage, default);

        if (CheckCollisionNoShove(context, box))
        {
            ShoveCommon(context);

            switch (weaponObj)
            {
                case PlayerSwordProjectile swordShot:
                    swordShot.SpreadOut();
                    break;

                case MagicWaveProjectile magicWave:
                    magicWave.AddFire();
                    magicWave.Delete();
                    break;
            }
        }
    }

    protected void CheckBombAndFire()
    {
        foreach (var fire in World.GetObjects<FireActor>()) CheckBombAndFire(fire);
        foreach (var bomb in World.GetObjects<BombActor>()) CheckBombAndFire(bomb);
    }

    private void CheckBombAndFire(Actor obj)
    {
        var context = new CollisionContext(obj, DamageType.Fire, 0x10, Point.Empty);
        short distance = 0xE;

        if (obj is BombActor bomb)
        {
            if (bomb.BombState != BombState.Blasting) return;

            context.DamageType = DamageType.Bomb;
            context.Damage = 0x40;
            distance = 0x18;
        }

        var box = new Point(distance, distance);
        var weaponCenter = new Point(8, 8);

        if (CheckCollisionCustomNoShove(context, box, weaponCenter))
        {
            if ((InvincibilityMask & (int)context.DamageType) == 0)
            {
                Shove(context);
            }
        }
    }

    protected void CheckSword(bool allowRodDamage = true)
    {
        var sword = World.GetObject<PlayerSwordActor>();
        if (sword == null || sword.State != 1) return;

        var player = Game.Player;

        var box = player.Facing.IsVertical() ? new Point(0xC, 0x10) : new Point(0x10, 0xC);
        var damage = sword.ObjType switch
        {
            ObjType.PlayerSword => PlayerSwordActor.GetSwordDamage(World),
            ObjType.Rod when allowRodDamage => 0x20,
            _ => 0
        };
        var context = new CollisionContext(sword, DamageType.Sword, damage, Point.Empty);

        if (CheckCollisionNoShove(context, box))
        {
            ShoveCommon(context);
        }
    }

    protected bool CheckArrow()
    {
        foreach (var arrow in World.GetObjects<ArrowProjectile>())
        {
            if (!arrow.IsPlayerWeapon) continue;
            if (CheckArrow(arrow)) return true;
        }

        return false;
    }

    private bool CheckArrow(ArrowProjectile arrow)
    {
        if (arrow.State != ProjectileState.Flying) return false;

        var box = new Point(0xB, 0xB);
        var context = new CollisionContext(arrow, DamageType.Arrow, arrow.Damage, Point.Empty);

        if (CheckCollisionNoShove(context, box, out var damagedAmount))
        {
            ShoveCommon(context);

            if (this is PolsVoiceActor)
            {
                HP = 0;
                DealDamage(context);
            }
            else
            {
                arrow.Damage = arrow.IsPiercing
                    ? Math.Max(0, arrow.Damage - damagedAmount)
                    : 0;

                if (arrow.Damage <= 0) arrow.SetSpark();
            }
            return true;
        }
        return false;
    }

    protected Point CalcObjMiddle()
    {
        if (this is GanonActor)
        {
            return new Point(X + 0x10, Y + 0x10);
        }

        var xOffset = Attributes.IsHalfWidth ? 4 : 8;
        return new Point(X + xOffset, Y + 8);
    }

    protected bool CheckCollisionNoShove(CollisionContext context, Point box)
    {
        return CheckCollisionNoShove(context, box, out _);
    }

    protected bool CheckCollisionNoShove(CollisionContext context, Point box, out int damageAmount)
    {
        var player = Game.Player;
        var weaponCenter = player.Facing.IsVertical() ? new Point(6, 8) : new Point(8, 6);

        return CheckCollisionCustomNoShove(context, box, weaponCenter, out damageAmount);
    }

    protected bool CheckCollisionCustomNoShove(CollisionContext context, Point box, Point weaponOffset)
    {
        return CheckCollisionCustomNoShove(context, box, weaponOffset, out _);
    }

    protected bool CheckCollisionCustomNoShove(CollisionContext context, Point box, Point weaponOffset, out int damageAmount)
    {
        damageAmount = 0;
        var weaponObj = context.Weapon;
        if (weaponObj == null) return false;

        var objCenter = CalcObjMiddle();
        weaponOffset.X += weaponObj.X;
        weaponOffset.Y += weaponObj.Y;

        if (!DoObjectsCollide(objCenter, weaponOffset, box, out context.Distance)) return false;

        if (weaponObj is BoomerangProjectile boomerang)
        {
            boomerang.SetState(BoomerangState.Unknown5);

            if ((InvincibilityMask & (int)context.DamageType) != 0)
            {
                PlayParrySound();
                return true;
            }

            StunTimer = 0x10;
        }

        damageAmount = HandleCommonHit(context);
        return true;
    }

    protected static bool DoObjectsCollide(Point obj1, Point obj2, Point box, out Point distance)
    {
        distance = new Point(Math.Abs(obj2.X - obj1.X), 0);
        if (distance.X < box.X)
        {
            distance.Y = Math.Abs(obj2.Y - obj1.Y);
            if (distance.Y < box.Y)
            {
                return true;
            }
        }

        return false;
    }

    protected int HandleCommonHit(CollisionContext context)
    {
        if ((InvincibilityMask & (int)context.DamageType) != 0)
        {
            PlayParrySoundIfSupported(context.DamageType);
            return 0;
        }

        var weaponObj = context.Weapon ?? throw new InvalidOperationException("Weapon was null.");

        if (this is GohmaActor gohma)
        {
            if (weaponObj is ArrowProjectile arrow)
            {
                arrow.SetSpark(4);
            }

            // JOE: FIXME: This is repeated and could be made a method on gohma.
            if (gohma.GetCurrentCheckPart() is 3 or 4
                && gohma.GetEyeFrame() == 3
                && weaponObj.Facing == Direction.Up)
            {
                Game.Sound.PlayEffect(SoundEffect.BossHit);
                Game.Sound.StopEffect(StopEffect.AmbientInstance);
                DealDamage(context);
                // The original game plays sounds again. But why, if we already played boss hit effect?
            }

            Game.Sound.PlayEffect(SoundEffect.Parry);
            return 0;
        }

        if (this is ZolActor || this is VireActor)
        {
            if (context.Weapon is not BoomerangProjectile)
            {
                Facing = weaponObj.Facing;
            }
        }
        else if (this is DarknutActor)
        {
            var combinedDir = Facing | weaponObj.Facing;

            // If the hitter is facing the darknut's front, then parry.
            if (combinedDir is Direction.HorizontalMask or Direction.VerticalMask)
            {
                PlayParrySoundIfSupported(context.DamageType);
                return 0;
            }
        }

        return DealDamage(context);
    }

    protected int DealDamage(CollisionContext context)
    {
        Game.Sound.PlayEffect(SoundEffect.MonsterHit);
        World.Profile.Statistics.DealDamage(context);

        if (HP < context.Damage)
        {
            KillObjectNormally(context);
            return HP;
        }

        HP -= (byte)context.Damage;
        if (HP == 0)
        {
            KillObjectNormally(context);
        }

        return context.Damage;
    }

    protected void KillObjectNormally(CollisionContext context)
    {
        var allowBombDrop = context.DamageType == DamageType.Bomb;

        World.IncrementKilledObjectCount(allowBombDrop);

        Decoration = 0x10;
        Game.Sound.PlayEffect(SoundEffect.MonsterDie);

        StunTimer = 0;
        ShoveDirection = 0;
        ShoveDistance = 0;
        InvincibilityTimer = 0;

        ObjectStatistics.Kills++;
    }

    protected void PlayParrySoundIfSupported(DamageType damageType)
    {
        // TODO: This looks like a bug and damageType isn't used?
        if ((InvincibilityMask & (byte)DamageType.Fire) == 0
            && (InvincibilityMask & (byte)DamageType.Bomb) == 0)
        {
            PlayParrySound();
        }
    }

    protected void PlayParrySound() => Game.Sound.PlayEffect(SoundEffect.Parry);

    protected void PlayBossHitSoundIfHit()
    {
        if (InvincibilityTimer == 0x10)
        {
            Game.Sound.PlayEffect(SoundEffect.BossHit);
        }
    }

    protected void PlayBossHitSoundIfDied()
    {
        if (Decoration != 0)
        {
            Game.Sound.PlayEffect(SoundEffect.BossHit);
            Game.Sound.StopEffect(StopEffect.AmbientInstance);
        }
    }

    protected void ShoveCommon(CollisionContext context)
    {
        if (this is DarknutActor)
        {
            var weaponObj = context.Weapon ?? throw new InvalidOperationException("Weapon was null.");
            var combinedDir = (int)Facing | (int)weaponObj.Facing;

            if (combinedDir is 3 or 0xC)
            {
                Game.Sound.PlayEffect(SoundEffect.Parry);
                return;
            }
        }

        Shove(context);
    }

    protected void Shove(CollisionContext context)
    {
        if ((InvincibilityMask & (int)context.DamageType) != 0) return;

        // JOE: NOTE: This should irl be `> ObjectSlot.NoneFound` but verifying that becomes a bit difficult.
        if (context.Weapon != null)
        {
            ShoveObject(context);
            return;
        }

        var player = Game.Player;
        if (player.InvincibilityTimer != 0) return;

        var useY = player.TileOffset == 0 ? context.Distance.Y >= 4 : player.Facing.IsVertical();

        Direction dir;
        if (useY)
        {
            dir = Y < player.Y ? Direction.Down : Direction.Up;
        }
        else
        {
            dir = X < player.X ? Direction.Right : Direction.Left;
        }

        player.ShoveDirection = (Direction)((int)dir | 0x80);
        player.InvincibilityTimer = 0x18;
        player.ShoveDistance = 0x20;

        if (!IsMonsterSlot) return;
        if (Attributes.Unknown80 || this is VireActor) return;

        Facing = Facing.GetOppositeDirection();
    }

    public void ShoveObject(CollisionContext context)
    {
        if (InvincibilityTimer != 0) return;

        var weaponObj = context.Weapon ?? throw new InvalidOperationException("Weapon was null.");
        var dir = weaponObj.Facing;

        if (Attributes.Unknown80)
        {
            // Debugger.Break();
            dir |= (Direction)0x40;
        }

        if (this is GohmaActor gohma)
        {
            if (gohma.GetCurrentCheckPart() is not (3 or 4)
                || gohma.GetEyeFrame() != 3
                || weaponObj.Facing != Direction.Up)
            {
                return;
            }
        }

        ShoveDirection = dir | (Direction)0x80;
        ShoveDistance = 0x40;
        InvincibilityTimer = 0x10;
    }

    protected PlayerCollision CheckPlayerCollision()
    {
        // The original resets [$C] and [6] here. [6] gets the result of DoObjectsCollide.
        // [$C] takes on the same values as [6], so I don't know why it was needed.

        var player = Game.Player;

        if (IsStunned || player.StunTimer != 0 || player.InvincibilityTimer != 0)
        {
            return new PlayerCollision(false, false);
        }

        return CheckPlayerCollisionDirect();
    }

    protected PlayerCollision CheckPlayerCollisionDirect()
    {
        var player = Game.Player;
        var fnlog = _log.CreateFunctionLog();

        if (player.GetState() == PlayerState.Paused || player.IsParalyzed)
        {
            return new PlayerCollision(false, false);
        }

        if (this is IProjectile shot && !shot.IsInShotStartState())
        {
            return new PlayerCollision(false, false);
        }

        var objCenter = CalcObjMiddle();
        var playerCenter = player.GetMiddle();
        var box = new Point(9, 9); // JOE: TODO: Why 9?

        if (!DoObjectsCollide(objCenter, playerCenter, box, out var distance))
        {
            return new PlayerCollision(false, false);
        }

        var context = new CollisionContext(null, 0, 0, distance);

        if (ObjType < ObjType.PersonEnd)
        {
            fnlog.Write("💥 this is PersonActor");
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, false);
        }

        if (ObjType is ObjType.Fireball2 or ObjType.Merchant // Merchant seems to be a reused object id?
            || player.GetState() != PlayerState.Idle)
        {
            fnlog.Write($"💥 {ObjType}, {player.GetState()}");
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        if (((int)(Facing | player.Facing) & 0xC) != 0xC
            && ((int)(Facing | player.Facing) & 3) != 3)
        {
            fnlog.Write("💥 Facing | player.Facing");
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        // JOE: NOTE: This might be wrong. Original was:
        // GetType() >= Obj_Fireball && GetType() < Obj_Arrow
        if (this is IBlockableProjectile projectile
            && projectile.RequiresMagicShield
            && !World.HasItem(ItemSlot.MagicShield))
        {
            fnlog.Write("💥 !ItemSlot.MagicShield");
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        fnlog.Write("🛡️ Parry.");
        Game.Sound.PlayEffect(SoundEffect.Parry);
        return new PlayerCollision(false, true);
    }

    protected Direction CheckWorldMarginH(int x, int y, Direction dir, bool adjust)
    {
        return CheckWorldMarginH(x, y, dir, adjust, out _);
    }

    protected Direction CheckWorldMarginH(int x, int y, Direction dir, bool adjust, out CheckWorldReason reason)
    {
        reason = CheckWorldReason.None;

        var offsetLeft = x;
        var offsetRight = x;

        if (adjust)
        {
            offsetLeft = x + 0x0B;
            offsetRight = x - 0x0C;
        }

        if (offsetLeft <= World.MarginLeft)
        {
            reason = CheckWorldReason.OutOfBounds;
            return (dir & Direction.Left) != 0 ? Direction.None : dir;
        }

        if (offsetRight >= World.MarginRight)
        {
            reason = CheckWorldReason.OutOfBounds;
            return (dir & Direction.Right) != 0 ? Direction.None : dir;
        }

        if (dir.HasFlag(Direction.Left) && World.TouchesWall(offsetLeft, y, TileOffset))
        {
            reason = CheckWorldReason.Wall;
            return Direction.None;
        }

        if (dir.HasFlag(Direction.Right) && World.TouchesWall(offsetRight + 8, y, TileOffset))
        {
            reason = CheckWorldReason.Wall;
            return Direction.None;
        }

        reason = CheckWorldReason.InBounds;
        return dir;
    }

    protected Direction CheckWorldMarginV(int x, int y, Direction dir, bool adjust)
    {
        return CheckWorldMarginV(x, y, dir, adjust, out _);
    }

    protected Direction CheckWorldMarginV(int x, int y, Direction dir, bool adjust, out CheckWorldReason reason)
    {
        var offsetUp = y;
        var offsetDown = y;

        if (adjust)
        {
            offsetUp = y + 0x0F;
            offsetDown = y - 0x12;
        }

        if (offsetUp <= World.MarginTop)
        {
            reason = CheckWorldReason.OutOfBounds;
            return (dir & Direction.Up) != 0 ? Direction.None : dir;
        }

        if (offsetDown >= World.MarginBottom)
        {
            reason = CheckWorldReason.OutOfBounds;
            return (dir & Direction.Down) != 0 ? Direction.None : dir;
        }

        if (dir.HasFlag(Direction.Up) && World.TouchesWall(x, offsetUp, TileOffset))
        {
            reason = CheckWorldReason.Wall;
            return Direction.None;
        }

        if (dir.HasFlag(Direction.Down) && World.TouchesWall(x, y + 8, TileOffset))
        {
            reason = CheckWorldReason.Wall;
            return Direction.None;
        }

        reason = CheckWorldReason.InBounds;
        return dir;
    }

    protected enum CheckWorldReason
    {
        None, InBounds, OutOfBounds, Wall
    }

    protected Direction CheckWorldMargin(Direction dir)
    {
        return CheckWorldMargin(dir, out _);
    }

    protected Direction CheckWorldMargin(Direction dir, out CheckWorldReason reason)
    {
        // JOE: This isn't exactly correct... the original would exclude the buffer slot from
        // this. I'm not sure if that matters, however. Buffer slots were used for push blocks.
        // var adjust = slot > ObjectSlot.Buffer || this is LadderActor;
        var adjust = !IsMonsterSlot || this is LadderActor;

        if (IsPlayer)
        {
            adjust = false;
        }

        dir = CheckWorldMarginH(X, Y, dir, adjust, out reason);
        return CheckWorldMarginV(X, Y, dir, adjust, out reason);
    }

    protected Direction CheckTileCollision(Direction dir)
    {
        if (TileOffset != 0) return dir;

        if (dir != Direction.None)
        {
            return FindUnblockedDir(dir, TileCollisionStep.CheckTile);
        }

        // The original handles objAttr $10 here, but no object seems to have it.

        dir = (Direction)Moving;
        return FindUnblockedDir(dir, TileCollisionStep.NextDir);
    }

    // F14E
    protected virtual Direction CheckWorldBounds(Direction dir)
    {
        dir = CheckWorldMargin(dir);
        if (dir != Direction.None)
        {
            Facing = dir;
            return dir;
        }

        return dir;
    }

    public virtual bool NonTargetedAction(Interaction interaction) => true;

    protected Direction GetSingleMoving()
    {
        var dirOrd = ((Direction)Moving).GetOrdinal();
        return dirOrd.GetOrdDirection();
    }

    protected Direction FindUnblockedDir(Direction dir, TileCollisionStep firstStep)
    {
        var i = 0;

        // JOE: This was a goto statement before. My change is a bit sus.
        var startOnNext = firstStep == TileCollisionStep.NextDir;

        do
        {
            if (!startOnNext)
            {
                var collision = World.CollidesWithTileMoving(X, Y, dir, false);
                if (!collision.Collides)
                {
                    dir = CheckWorldBounds(dir);
                    if (dir != Direction.None) return dir;
                }

                // The original handles objAttr $10 here, but no object seems to have it.
            }

            startOnNext = false;
            dir = GetNextAltDir(ref i, dir);
        } while (i != 0);

        return dir;
    }

    protected void TurnToUnblockedDir()
    {
        if (TileOffset != 0) return;

        var dir = Direction.None;
        var i = 0;

        while (true)
        {
            dir = GetNextAltDir(ref i, dir);
            if (dir == Direction.None) return;

            if (!World.CollidesWithTileMoving(X, Y, dir, IsPlayer))
            {
                dir = CheckWorldMargin(dir);
                if (dir != Direction.None)
                {
                    Facing = dir;
                    return;
                }
            }
        }
    }

    private Direction GetNextAltDir(ref int seq, Direction dir)
    {
        ReadOnlySpan<Direction> nextDirections = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

        switch (seq++)
        {
            // Choose a random direction perpendicular to facing.
            case 0:
                var index = 0;
                var r = Game.Random.GetByte();

                if ((r & 1) == 0) index++;
                if (Facing.IsVertical()) index += 2;

                return nextDirections[index];

            case 1:
                return dir.GetOppositeDirection();

            case 2:
                Facing = Facing.GetOppositeDirection();
                return Facing;

            default:
                seq = 0;
                return Direction.None;
        }
    }

    protected Direction StopAtPersonWall(Direction dir)
    {
        if (Y < PersonActor.PersonWallY && dir.HasFlag(Direction.Up))
        {
            return Direction.None;
        }

        return dir;
    }

    protected Direction StopAtPersonWallUW(Direction dir)
    {
        if (Game.Cheats.NoClip) return dir;

        foreach (var obj in World.GetObjects())
        {
            if (!obj.IsDeleted && obj.ShouldStopAtPersonWall)
            {
                return StopAtPersonWall(dir);
            }
        }

        return dir;
    }

    protected void ObjMove(int speed)
    {
        if (ShoveDirection != Direction.None)
        {
            ObjShove();
            return;
        }

        if (IsStunned) return;

        var dir = Direction.None;

        if (Moving != 0)
        {
            var dirOrd = ((Direction)Moving).GetOrdinal();
            dir = dirOrd.GetOrdDirection();
        }

        dir &= Direction.DirectionMask;

        // Original: [$E] := 0
        // Maybe it's only done to set up the call to FindUnblockedDir in CheckTileCollision?

        dir = CheckWorldMargin(dir);
        dir = CheckTileCollision(dir);

        MoveDirection(speed, dir);
    }

    public void MoveDirection(int speed, Direction dir)
    {
        var align = IsPlayer ? 8 : 0x10;
        MoveWhole(speed, dir, align);
    }

    private void MoveWhole(int speed, Direction dir, int align)
    {
        if (dir == Direction.None) return;

        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
    }

    private void MoveFourth(int speed, Direction dir, int align)
    {
        int frac = Fraction;

        frac += dir.IsGrowing() ? speed : -speed;

        var carry = frac >> 8;
        Fraction = (byte)(frac & 0xFF);

        if (TileOffset != align && TileOffset != -align)
        {
            TileOffset += (sbyte)carry;
            Position += dir.IsHorizontal() ? new Size(carry, 0) : new Size(0, carry);
        }
    }

    protected void ObjShove()
    {
        if (!ShoveDirection.HasFlag(Direction.ShoveMask))
        {
            if (ShoveDistance != 0)
            {
                MoveShoveWhole();
            }
            else
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
            }

            return;
        }

        ShoveDirection ^= Direction.ShoveMask;

        var shoveHoriz = ShoveDirection.IsHorizontal(Direction.DirectionMask);
        var facingHoriz = Facing.IsHorizontal();

        if (shoveHoriz != facingHoriz
            && TileOffset != 0
            && !IsPlayer)
        {
            ShoveDirection = 0;
            ShoveDistance = 0;
        }
    }

    protected void MoveShoveWhole()
    {
        var cleanDir = ShoveDirection & Direction.DirectionMask;

        for (var i = 0; i < 4; i++)
        {
            if (TileOffset == 0)
            {
                Position = new Point(X & 0xF8, (Y & 0xF8) | 5);

                if (World.CollidesWithTileMoving(X, Y, cleanDir, IsPlayer))
                {
                    ShoveDirection = 0;
                    ShoveDistance = 0;
                    return;
                }
            }

            if (CheckWorldMargin(cleanDir) == Direction.None
                || StopAtPersonWallUW(cleanDir) == Direction.None)
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
                return;
            }

            var distance = cleanDir.IsGrowing() ? 1 : -1;

            ShoveDistance--;
            TileOffset += (sbyte)distance;

            if ((TileOffset & 0x0F) == 0)
            {
                TileOffset &= 0x0F;
            }
            else if (IsPlayer && (TileOffset & 7) == 0)
            {
                TileOffset &= 7;
            }

            Position += cleanDir.IsHorizontal()
                ? new Size(distance, 0)
                : new Size(0, distance);
        }
    }
}