using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using z1.IO;
using z1.Render;

namespace z1.Actors;

[Flags]
internal enum ActorFlags { None = 0, DrawAbovePlayer = 1 }

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
internal abstract class Actor
{
    private static readonly DebugLog _log = new(nameof(Actor));
    private static readonly DebugLog _traceLog = new(nameof(Actor), DebugLogDestination.DebugBuildsOnly);
    private static readonly ImmutableArray<byte> _swordPowers = [0, 0x10, 0x20, 0x40];
    private static long _idCounter;

    public Game Game { get; }
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
    public ActorFlags Flags;

    public ObjType ObjType { get; }
    public ObjectAttribute Attributes => Game.World.GetObjectAttribute(ObjType);
    public ObjectStatistics ObjectStatistics => Game.World.Profile.Statistics.GetObjectStatistics(this);
    public Actor? Owner { get; protected set; }

    // Any non-player actors that can pick up an item and count as the player having collected. IE, arrows.
    // This does not apply to room items.
    public virtual bool CanPickupItem => false;

    protected bool IsStunned => _isStunned();

    protected Actor(Game game, ObjType type, int x = 0, int y = 0)
    {
        if (type == ObjType.None) throw new ArgumentOutOfRangeException(nameof(type));

        Game = game;
        ObjType = type;
        Position = new Point(x, y);

        _traceLog.Write($"Created {GetType().Name} at {X:X2},{Y:X2}");

        // JOE: "monsters and persons not armos or flying ghini"
        if (type < ObjType.PersonEnd
            && type is not (ObjType.Armos or ObjType.FlyingGhini))
        {
            // JOE: This might not be entirely correct...
            // var time = game.World.CurObjSlot + 1;
            var time = game.World.CountObjects() + 1;
            ObjTimer = (byte)time;
        }

        HP = (byte)game.World.GetObjectMaxHP(ObjType);
    }

    ~Actor()
    {
        _log.Error($"Finalizer for {GetType().Name}/{ObjType} called.");
    }

    public abstract void Update();
    public abstract void Draw();

    public virtual bool IsPlayer => false;
    public virtual bool ShouldStopAtPersonWall => false;
    public virtual bool CountsAsLiving =>
        ObjType < ObjType.Bubble1
        || (ObjType > ObjType.Bubble3 && ObjType < ObjType.Trap);

    public virtual bool CanHoldRoomItem => false;
    public virtual bool IsReoccuring => true;
    public virtual bool IsAttrackedToMeat => false;

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

    public static Actor AddFromType(ObjType type, Game game, int x, int y)
    {
        // Some object constructors add themselves already, making generic object construction need to not double-add.
        var actor = FromType(type, game, x, y);
        game.World.AddUniqueObject(actor);
        return actor;
    }

    public static Actor FromType(ObjType type, Game game, int x, int y)
    {
        return type switch
        {
            ObjType.BlueLynel => LynelActor.Make(game, ActorColor.Blue, x, y),
            ObjType.RedLynel => LynelActor.Make(game, ActorColor.Red, x, y),
            ObjType.BlueMoblin => MoblinActor.Make(game, ActorColor.Blue, x, y),
            ObjType.RedMoblin => MoblinActor.Make(game, ActorColor.Red, x, y),
            ObjType.BlueGoriya => GoriyaActor.Make(game, ActorColor.Blue, x, y),
            ObjType.RedGoriya => GoriyaActor.Make(game, ActorColor.Red, x, y),
            ObjType.RedSlowOctorock => OctorokActor.Make(game, ActorColor.Red, false, x, y),
            ObjType.RedFastOctorock => OctorokActor.Make(game, ActorColor.Red, true, x, y),
            ObjType.BlueSlowOctorock => OctorokActor.Make(game, ActorColor.Blue, false, x, y),
            ObjType.BlueFastOctorock => OctorokActor.Make(game, ActorColor.Blue, true, x, y),
            ObjType.RedDarknut => DarknutActor.Make(game, ActorColor.Red, x, y),
            ObjType.BlueDarknut => DarknutActor.Make(game, ActorColor.Blue, x, y),
            ObjType.BlueTektite => TektiteActor.Make(game, ActorColor.Blue, x, y),
            ObjType.RedTektite => TektiteActor.Make(game, ActorColor.Red, x, y),
            ObjType.BlueLeever => new BlueLeeverActor(game, x, y),
            ObjType.RedLeever => new RedLeeverActor(game, x, y),
            ObjType.Zora => new ZoraActor(game, x, y),
            ObjType.Vire => new VireActor(game, x, y),
            ObjType.Zol => new ZolActor(game, x, y),
            ObjType.Gel => new GelActor(game, ObjType.Gel, x, y, Direction.None, 0),
            ObjType.PolsVoice => new PolsVoiceActor(game, x, y),
            ObjType.LikeLike => new LikeLikeActor(game, x, y),
            ObjType.Peahat => new PeahatActor(game, x, y),
            ObjType.BlueKeese => KeeseActor.Make(game, ActorColor.Blue, x, y),
            ObjType.RedKeese => KeeseActor.Make(game, ActorColor.Red, x, y),
            ObjType.BlackKeese => KeeseActor.Make(game, ActorColor.Black, x, y),
            ObjType.Armos => new ArmosActor(game, x, y),
            ObjType.Boulders => new BouldersActor(game, x, y),
            ObjType.Boulder => new BoulderActor(game, x, y),
            ObjType.Ghini => new GhiniActor(game, x, y),
            ObjType.FlyingGhini => new FlyingGhiniActor(game, x, y),
            ObjType.BlueWizzrobe => new BlueWizzrobeActor(game, x, y),
            ObjType.RedWizzrobe => new RedWizzrobeActor(game, x, y),
            ObjType.Wallmaster => new WallmasterActor(game, x, y),
            ObjType.Rope => new RopeActor(game, x, y),
            ObjType.Stalfos => new StalfosActor(game, x, y),
            ObjType.Bubble1 => new BubbleActor(game, ObjType.Bubble1, x, y),
            ObjType.Bubble2 => new BubbleActor(game, ObjType.Bubble2, x, y),
            ObjType.Bubble3 => new BubbleActor(game, ObjType.Bubble3, x, y),
            ObjType.Whirlwind => new WhirlwindActor(game, x, y),
            ObjType.PondFairy => new PondFairyActor(game),
            ObjType.Gibdo => new GibdoActor(game, x, y),
            ObjType.ThreeDodongos => DodongoActor.Make(game, 3, x, y),
            ObjType.OneDodongo => DodongoActor.Make(game, 1, x, y),
            ObjType.BlueGohma => GohmaActor.Make(game, ActorColor.Blue),
            ObjType.RedGohma => GohmaActor.Make(game, ActorColor.Red),
            ObjType.RupieStash => RupeeStashActor.Make(game),
            ObjType.Princess => PrincessActor.Make(game),
            ObjType.Digdogger1 => DigdoggerActor.Make(game, x, y, 3),
            ObjType.Digdogger2 => DigdoggerActor.Make(game, x, y, 1),
            ObjType.RedLamnola => LamnolaActor.MakeSet(game, ActorColor.Red),
            ObjType.BlueLamnola => LamnolaActor.MakeSet(game, ActorColor.Blue),
            ObjType.Manhandla => ManhandlaActor.Make(game, x, y),
            ObjType.Aquamentus => new AquamentusActor(game),
            ObjType.Ganon => new GanonActor(game, x, y),
            ObjType.GuardFire => new GuardFireActor(game, x, y),
            ObjType.StandingFire => new StandingFireActor(game, x, y),
            ObjType.Moldorm => MoldormActor.MakeSet(game),
            ObjType.Gleeok1 => GleeokActor.Make(game, 1),
            ObjType.Gleeok2 => GleeokActor.Make(game, 2),
            ObjType.Gleeok3 => GleeokActor.Make(game, 3),
            ObjType.Gleeok4 => GleeokActor.Make(game, 4),
            ObjType.Patra1 => PatraActor.MakePatra(game, PatraType.Circle),
            ObjType.Patra2 => PatraActor.MakePatra(game, PatraType.Spin),
            ObjType.Trap => TrapActor.MakeSet(game, 6),
            ObjType.TrapSet4 => TrapActor.MakeSet(game, 4),
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

        if (Game.World.GetMode() != GameMode.Play) return false;

        // NOTE: This check is because level 6 is double wide. We need to pass width and height into this.
        if (Game.World.IsOverworld() && false) // JOE: TODO: MAP REWRITE This appears to the level 6 entrance...? Game.World.CurRoomId == 0x22)
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

        var playerPos = Game.World.GetObservedPlayerPos();
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
        // var t = Game.World.CurObjSlot;
        var t = Game.World.CountObjects();
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
                // JOE: CHECK: Not sure this is right. It use to be: slot < ObjectSlot.Buffer
                if (this is MonsterActor && !Attributes.HasCustomCollision)
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

    public void DecoratedDraw()
    {
        if (!Visible) return;

        if (Decoration == 0)
        {
            Draw();
        }
        else if (Decoration < 0x10)
        {
            var frame = Decoration - 1;
            var animator = Graphics.GetSpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Cloud);
            animator.DrawFrame(TileSheet.PlayerAndItems, X, Y, Palette.Blue, frame);
        }
        else
        {
            var counter = (Game.FrameCounter >> 1) & 3;
            var frame = (Decoration + 1) % 2;
            var pal = Palette.Player + (Global.ForegroundPalCount - counter - 1);
            var animator = Graphics.GetSpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Sparkle);
            animator.DrawFrame(TileSheet.PlayerAndItems, X, Y, pal, frame);
        }
    }

    protected Palette CalcPalette(Palette wantedPalette)
    {
        if (InvincibilityTimer == 0) return wantedPalette;
        return Palette.Player + (InvincibilityTimer & 3);
    }

    protected bool _isStunned()
    {
        if (Game.World.HasItem(ItemSlot.Clock)) return true;
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
        foreach (var rang in Game.World.GetObjects<BoomerangProjectile>())
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
        foreach (var projectile in Game.World.GetObjects<Projectile>().ToArray())
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
        var context = new CollisionContext(weaponObj, default, default, default);

        if (weaponObj is MagicWaveProjectile)
        {
            context.DamageType = DamageType.MagicWave;
            context.Damage = 0x20;
        }
        else
        {
            var itemValue = Game.World.GetItem(ItemSlot.Sword);
            context.DamageType = DamageType.Sword;
            context.Damage = _swordPowers[itemValue];
        }

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
        foreach (var fire in Game.World.GetObjects<FireActor>()) CheckBombAndFire(fire);
        foreach (var bomb in Game.World.GetObjects<BombActor>()) CheckBombAndFire(bomb);
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
        var sword = Game.World.GetObject<PlayerSwordActor>();
        if (sword == null || sword.State != 1) return;

        var player = Game.Player;

        var box = player.Facing.IsVertical() ? new Point(0xC, 0x10) : new Point(0x10, 0xC);
        var context = new CollisionContext(sword, DamageType.Sword, 0, Point.Empty);

        switch (sword.ObjType)
        {
            case ObjType.PlayerSword:
                var itemValue = Game.World.GetItem(ItemSlot.Sword);
                var power = _swordPowers[itemValue];
                context.Damage = power;
                break;

            case ObjType.Rod when allowRodDamage:
                context.Damage = 0x20;
                break;
        }

        if (CheckCollisionNoShove(context, box))
        {
            ShoveCommon(context);
        }
    }

    protected bool CheckArrow()
    {
        foreach (var arrow in Game.World.GetObjects<ArrowProjectile>())
        {
            if (!arrow.IsPlayerWeapon) continue;
            if (CheckArrow(arrow)) return true;
        }

        return false;
    }

    private bool CheckArrow(ArrowProjectile arrow)
    {
        if (arrow.State != ProjectileState.Flying) return false;

        var itemValue = Game.World.GetItem(ItemSlot.Arrow);
        var box = new Point(0xB, 0xB);

        ReadOnlySpan<int> arrowPowers = [0, 0x20, 0x40];
        var context = new CollisionContext(arrow, DamageType.Arrow, arrowPowers[itemValue], Point.Empty);

        if (CheckCollisionNoShove(context, box))
        {
            ShoveCommon(context);

            if (this is PolsVoiceActor)
            {
                HP = 0;
                DealDamage(context);
            }
            else
            {
                arrow.SetSpark();
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
        var player = Game.Player;
        var weaponCenter = player.Facing.IsVertical() ? new Point(6, 8) : new Point(8, 6);

        return CheckCollisionCustomNoShove(context, box, weaponCenter);
    }

    protected bool CheckCollisionCustomNoShove(CollisionContext context, Point box, Point weaponOffset)
    {
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

        HandleCommonHit(context);
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

    protected void HandleCommonHit(CollisionContext context)
    {
        if ((InvincibilityMask & (int)context.DamageType) != 0)
        {
            PlayParrySoundIfSupported(context.DamageType);
            return;
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
            return;
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
                return;
            }
        }

        DealDamage(context);
    }

    protected void DealDamage(CollisionContext context)
    {
        Game.Sound.PlayEffect(SoundEffect.MonsterHit);
        Game.World.Profile.Statistics.DealDamage(context);

        if (HP < context.Damage)
        {
            KillObjectNormally(context);
            return;
        }

        HP -= (byte)context.Damage;
        if (HP == 0)
        {
            KillObjectNormally(context);
        }
    }

    protected void KillObjectNormally(CollisionContext context)
    {
        var allowBombDrop = context.DamageType == DamageType.Bomb;

        Game.World.IncrementKilledObjectCount(allowBombDrop);

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

        // JOE: Old code was: if (Game.World.CurObjectSlot >= ObjectSlot.Buffer) return;
        if (this is not MonsterActor) return;
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
            && !Game.World.HasItem(ItemSlot.MagicShield))
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

    protected Direction CheckWorldMarginH(int x, Direction dir, bool adjust)
    {
        return CheckWorldMarginH(x, dir, adjust, out _);
    }

    protected Direction CheckWorldMarginH(int x, Direction dir, bool adjust, out CheckWorldReason reason)
    {
        var curDir = Direction.Left;
        reason = CheckWorldReason.None;

        if (adjust)
        {
            x += 0xB;
        }

        if (x > Game.World.MarginLeft)
        {
            if (adjust)
            {
                x -= 0x17;
            }

            curDir = Direction.Right;

            if (x < Game.World.MarginRight)
            {
                reason = CheckWorldReason.InBounds;
                return dir;
            }
        }

        reason = CheckWorldReason.OutOfBounds;
        return (dir & curDir) != 0 ? Direction.None : dir;
    }

    protected Direction CheckWorldMarginV(int y, Direction dir, bool adjust)
    {
        return CheckWorldMarginV(y, dir, adjust, out _);
    }

    protected Direction CheckWorldMarginV(int y, Direction dir, bool adjust, out CheckWorldReason reason)
    {
        var curDir = Direction.Up;
        reason = CheckWorldReason.None;

        if (adjust)
        {
            y += 0x0F;
        }

        if (y > Game.World.MarginTop)
        {
            if (adjust)
            {
                y -= 0x21;
            }

            curDir = Direction.Down;

            if (y < Game.World.MarginBottom)
            {
                reason = CheckWorldReason.InBounds;
                return dir;
            }
        }

        reason = CheckWorldReason.OutOfBounds;
        return (dir & curDir) != 0 ? Direction.None : dir;
    }

    protected enum CheckWorldReason
    {
        None, InBounds, OutOfBounds
    }

    protected Direction CheckWorldMargin(Direction dir)
    {
        return CheckWorldMargin(dir, out _);
    }

    protected Direction CheckWorldMargin(Direction dir, out CheckWorldReason reason)
    {
        // JOE: Original: var adjust = slot > ObjectSlot.Buffer || this is LadderActor;
        var adjust = this is not MonsterActor || this is LadderActor;

        // ORIGINAL: This isn't needed, because the player is first (slot=0).
        // JOE: Original: if (slot >= ObjectSlot.Player)
        if (IsPlayer)
        {
            adjust = false;
        }

        dir = CheckWorldMarginH(X, dir, adjust, out reason);
        return CheckWorldMarginV(Y, dir, adjust, out reason);
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

    public virtual bool NontargetedAction(Interaction interaction) => true;

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
                var collision = Game.World.CollidesWithTileMoving(X, Y, dir, false);
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

            if (!Game.World.CollidesWithTileMoving(X, Y, dir, IsPlayer))
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
        // ($6E46) if first object is grumble or person, block movement up above $8E.

        foreach (var obj in Game.World.GetObjects())
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

                if (Game.World.CollidesWithTileMoving(X, Y, cleanDir, IsPlayer))
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

    // Are these monster only?
    // -----------------------

    protected Actor? Shoot(ObjType shotType, int x, int y, Direction facing)
    {
        var oldActiveShots = Game.World.ActiveShots;

        var shot = shotType == ObjType.Boomerang
            ? GlobalFunctions.MakeBoomerang(Game, x, y, facing, 0x51, 2.5f, this)
            : GlobalFunctions.MakeProjectile(Game, shotType, x, y, facing, this);

        var newActiveShots = Game.World.ActiveShots;
        if (oldActiveShots != newActiveShots && newActiveShots > 4)
        {
            shot.Delete();
            return null;
        }

        Game.World.AddObject(shot);
        // In the original, they start in state $10. But, that was only a way to say that the object exists.
        shot.ObjTimer = 0;
        return shot;
    }

    protected FireballProjectile? ShootFireball(ObjType type, int x, int y, int? offset = null)
    {
        return Game.ShootFireball(type, x, y, offset);
    }
}