using System.Diagnostics;

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

[Flags]
internal enum ActorAttributes
{
    CustomCollision = 1,
    CustomDraw = 4,
    Unknown10__ = 0x10,
    InvincibleToWeapons = 0x20,
    HalfWidth = 0x40,
    Unknown80__ = 0x80,
    WorldCollision = 0x100,
}

internal interface IDeleteEvent
{
    void OnDelete();
}

internal abstract class Actor
{
    public Game Game { get; }

    // JOE: TODO: Get rid of this.
    public Point Position
    {
        get => new(_x, _y);
        set
        {
            X = value.X;
            Y = value.Y;
        }
    }

    private int _x;
    private int _y;

    public int X
    {
        get => Position.X;
        set => _x = value;
    }
    public int Y
    {
        get => Position.Y;
        set => _y = value;
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            _isDeleted = value;

            // JOE: I don't like properties that have side effects but here we are :)
            if (value && this is IDeleteEvent deleteEvent && !_ranDeletedEvent)
            {
                deleteEvent.OnDelete();
                // JOE: _Presumably_ we only want these to run once.
                _ranDeletedEvent = true;
            }
        }
    }

    private bool _isDeleted;
    private bool _ranDeletedEvent = false;

    public byte Decoration = 1;
    protected byte HP;
    public byte InvincibilityTimer;
    protected byte InvincibilityMask;
    protected Direction ShoveDirection = Direction.None;
    protected byte ShoveDistance = 0;
    public Direction Facing
    {
        get => _facing;
        set
        {
            if (this is Link && value == Direction.None)
            {
                Debugger.Break();
            }
            _facing = value;
        }
    }
    public Direction _facing = Direction.None;
    public sbyte TileOffset = 0;
    protected byte Fraction;
    public byte Moving;
    public Direction MovingDirection
    {
        get => (Direction)Moving;
        set => Moving = (byte)value;
    }
    public byte ObjTimer;
    protected byte StunTimer;
    public ActorFlags Flags;

    public ObjType ObjType { get; set; }
    public ObjectAttr Attributes => Game.World.GetObjectAttrs(ObjType);

    protected bool IsStunned => _isStunned();
    protected bool IsParalyzed { get; set; }

    // JOE: TODO: Consider bringing this back.
    //public ActorColor Color;

    protected Actor(Game game, ObjType type, int x = 0, int y = 0)
    {
        if (type == ObjType.None) throw new ArgumentOutOfRangeException(nameof(type));

        Game = game;
        ObjType = type;
        Position = new Point(x, y);

        if (type < ObjType.PersonEnd
            && type != ObjType.Armos && type != ObjType.FlyingGhini)
        {
            var slot = game.World.curObjSlot;
            var time = slot + 1;
            ObjTimer = (byte)time;
        }

        HP = (byte)game.World.GetObjectMaxHP(ObjType);
    }

    public abstract void Update();
    public abstract void Draw();

    public virtual bool IsPlayer => false;
    public virtual bool ShouldStopAtPersonWall => false;
    public virtual bool CountsAsLiving => true;
    public virtual bool CanHoldRoomItem => false;
    public virtual bool IsReoccuring => true;
    public virtual bool IsUnderworldPerson => true;
    public virtual bool IsAttrackedToMeat => false;

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
            ObjType.Zelda => ZeldaActor.Make(game),
            ObjType.Digdogger1 => DigdoggerActor.Make(game, x, y, 3),
            ObjType.Digdogger2 => DigdoggerActor.Make(game, x, y, 1),
            ObjType.RedLamnola => LamnolaActor.MakeSet(game, ActorColor.Red),
            ObjType.BlueLamnola => LamnolaActor.MakeSet(game, ActorColor.Blue),
            ObjType.Manhandla => ManhandlaActor.Make(game, x, y),
            ObjType.Aquamentus => new AquamentusActor(game, x, y),
            ObjType.Ganon => new GanonActor(game, x, y),
            ObjType.GuardFire => new GuardFireActor(game, x, y),
            ObjType.StandingFire => new StandingFireActor(game, x, y),
            ObjType.Moldorm => MoldormActor.MakeSet(game),
            ObjType.Gleeok1 => GleeokActor.Make(game, 1),
            ObjType.Gleeok2 => GleeokActor.Make(game, 2),
            ObjType.Gleeok3 => GleeokActor.Make(game, 3),
            ObjType.Gleeok4 => GleeokActor.Make(game, 4),
            ObjType.GleeokHead => new GleeokHeadActor(game, x, y),
            ObjType.Patra1 => PatraActor.MakePatra(game, PatraType.Circle),
            ObjType.Patra2 => PatraActor.MakePatra(game, PatraType.Spin),
            ObjType.Trap => TrapActor.MakeSet(game, 6),
            ObjType.TrapSet4 => TrapActor.MakeSet(game, 4),
            _ => throw new NotImplementedException(),
        };
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
        float x = 0, y = 0;
        switch (dir & (Direction.Right | Direction.Left))
        {
            case Direction.Right: x = speed; break;
            case Direction.Left: x = -speed; break;
        }

        switch (dir & (Direction.Down | Direction.Up))
        {
            case Direction.Down: y = speed; break;
            case Direction.Up: y = -speed; break;
        }

        return new(x, y);
    }

    protected void InitCommonFacing()
    {
        InitCommonFacing(X, Y, ref _facing);
    }

    private void InitCommonFacing(int x, int y, ref Direction facing)
    {
        if (facing != Direction.None)
            return;

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

    protected void InitCommonStateTimer(ref byte stateTimer)
    {
        var t = Game.World.curObjSlot;
        t = (t + 2) * 16;
        stateTimer = (byte)t;
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
        if (InvincibilityTimer > 0 && (Game.GetFrameCounter() & 1) == 0)
        {
            InvincibilityTimer--;
        }

        if (Decoration == 0)
        {
            Update();

            if (Decoration == 0)
            {
                var slot = Game.World.curObjectSlot;
                if (slot < ObjectSlot.Buffer && !Attributes.GetCustomCollision())
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
                IsDeleted = true;
                return true;
            }
        }
        return false;
    }

    public void DecoratedDraw()
    {
        if (Decoration == 0)
        {
            Draw();
        }
        else if (Decoration < 0x10)
        {
            var frame = Decoration - 1;
            var animator = Graphics.GetSpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Cloud);
            animator.DrawFrame(TileSheet.PlayerAndItems, X, Y, Palette.BlueFgPalette, frame);
        }
        else
        {
            var counter = (Game.GetFrameCounter() >> 1) & 3;
            var frame = (Decoration + 1) % 2;
            var pal = Palette.Player + (Palettes.ForegroundPalCount - counter - 1);
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
        if (Game.World.GetItem(ItemSlot.Clock) != 0) return true;
        return StunTimer != 0;
    }

    protected bool CheckCollisions()
    {
        if (!Attributes.GetInvincibleToWeapons())
        {
            if (InvincibilityTimer != 0) return false;

            CheckBoomerang(ObjectSlot.Boomerang);
            CheckWave(ObjectSlot.PlayerSwordShot);
            CheckBombAndFire(ObjectSlot.Bomb);
            CheckBombAndFire(ObjectSlot.Bomb2);
            CheckBombAndFire(ObjectSlot.Fire);
            CheckBombAndFire(ObjectSlot.Fire2);
            CheckSword(ObjectSlot.PlayerSword);
            CheckArrow(ObjectSlot.Arrow);
        }

        return CheckPlayerCollision();

        // The original game checked special Wallmaster, Like-Like, and Goriya states here.
        // But, we check instead in each of those classes.
    }

    protected void CheckBoomerang(ObjectSlot slot)
    {
        var box = new Point(0xA, 0xA);
        var weaponCenter = new Point(4, 8);
        var context = new CollisionContext(slot, DamageType.Boomerang, 0, Point.Empty);

        CheckCollisionCustomNoShove(context, box, weaponCenter);
    }

    protected void CheckWave(ObjectSlot slot)
    {
        var weaponObj = Game.World.GetObject(slot);
        if (weaponObj == null) return;

        if (weaponObj is PlayerSwordProjectile wave)
        {
            if (wave.state != ProjectileState.Flying) return;
        }

        var box = new Point(0xC, 0xC);
        var context = new CollisionContext(slot, default, default, default);

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

            if (weaponObj is PlayerSwordProjectile swordShot)
            {
                swordShot.SpreadOut();
            }
            else if (weaponObj is MagicWaveProjectile magicWave)
            {
                magicWave.AddFire();
                magicWave.IsDeleted = true;
            }
        }
    }

    protected void CheckBombAndFire(ObjectSlot slot)
    {
        var obj = Game.World.GetObject(slot);
        if (obj == null) return;

        var context = new CollisionContext(slot, DamageType.Fire, 0x10, Point.Empty);
        short distance = 0xE;

        if (obj is BombActor bomb)
        {
            if (bomb.BombState != BombState.Blasting) return;

            context.DamageType = DamageType.Bomb;
            context.WeaponSlot = slot;
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

    private static ReadOnlySpan<byte> _swordPowers => new byte[] { 0, 0x10, 0x20, 0x40 };

    protected void CheckSword(ObjectSlot slot)
    {
        var sword = Game.World.GetObject<PlayerSwordActor>(slot);
        if (sword == null || sword.state != 1) return;

        var box = new Point();
        var player = Game.Link;

        if (player.Facing.IsVertical())
        {
            box.X = 0xC;
            box.Y = 0x10;
        }
        else
        {
            box.X = 0x10;
            box.Y = 0xC;
        }

        var context = new CollisionContext(slot, DamageType.Sword, 0, Point.Empty);

        if (sword.ObjType == ObjType.PlayerSword)
        {
            var itemValue = Game.World.GetItem(ItemSlot.Sword);
            int power = _swordPowers[itemValue];
            context.Damage = power;
        }
        else if (sword.ObjType == ObjType.Rod)
        {
            context.Damage = 0x20;
        }

        if (CheckCollisionNoShove(context, box))
            ShoveCommon(context);
    }

    private static ReadOnlySpan<int> _arrowPowers => new[] { 0, 0x20, 0x40 };

    protected bool CheckArrow(ObjectSlot slot)
    {
        var arrow = Game.World.GetObject<ArrowProjectile>(slot);
        if (arrow == null) return false;

        if (arrow.state != ProjectileState.Flying)
            return false;

        var itemValue = Game.World.GetItem(ItemSlot.Arrow);
        var box = new Point(0xB, 0xB);

        var context = new CollisionContext(slot, DamageType.Arrow, _arrowPowers[itemValue], Point.Empty);

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
            return new(X + 0x10, Y + 0x10);
        }

        var xOffset = Attributes.GetHalfWidth() ? 4 : 8;
        return new Point(X + xOffset, Y + 8);
    }

    protected bool CheckCollisionNoShove(CollisionContext context, Point box)
    {
        var player = Game.Link;
        Point weaponCenter = new();

        if (player.Facing.IsVertical())
        {
            weaponCenter.X = 6;
            weaponCenter.Y = 8;
        }
        else
        {
            weaponCenter.X = 8;
            weaponCenter.Y = 6;
        }

        return CheckCollisionCustomNoShove(context, box, weaponCenter);
    }

    protected bool CheckCollisionCustomNoShove(CollisionContext context, Point box, Point weaponOffset)
    {
        var weaponObj = Game.World.GetObject(context.WeaponSlot);
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
        distance = new(Math.Abs(obj2.X - obj1.X), 0);
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

        var weaponObj = Game.World.GetObject(context.WeaponSlot) ?? throw new InvalidOperationException("Weapon was null.");

        if (this is GohmaActor gohma)
        {
            if (weaponObj is ArrowProjectile arrow)
            {
                arrow.SetSpark(4);
            }

            // FIXME: This is repeated and could be made a method on gohma.
            if (((gohma.GetCurrentCheckPart() == 3) || (gohma.GetCurrentCheckPart() == 4))
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
        else if (this is ZolActor || this is VireActor)
        {
            if (context.WeaponSlot != ObjectSlot.Boomerang)
            {
                Facing = weaponObj.Facing;
            }
        }
        else if (this is DarknutActor)
        {
            var combinedDir = (int)(Facing | weaponObj.Facing);

            if (combinedDir == 3 || combinedDir == 0xC)
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

        if (HP < context.Damage)
        {
            KillObjectNormally(context);
        }
        else
        {
            HP -= (byte)context.Damage; // TODO: This is sus math and casting.
            if (HP == 0)
            {
                KillObjectNormally(context);
            }
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
            var weaponObj = Game.World.GetObject(context.WeaponSlot) ?? throw new InvalidOperationException("Weapon was null.");
            var combinedDir = (int)Facing | (int)weaponObj.Facing;

            if (combinedDir == 3 || combinedDir == 0xC)
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
        if (context.WeaponSlot == 0) Debugger.Break();
        if (context.WeaponSlot > 0)
        {
            ShoveObject(context);
            return;
        }

        var player = Game.Link;
        if (player.InvincibilityTimer != 0) return;

        var useY = false;
        if (player.TileOffset == 0)
        {
            if (context.Distance.Y >= 4)
                useY = true;
        }
        else
        {
            if (player.Facing.IsVertical())
                useY = true;
        }

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

        if (Game.World.curObjectSlot >= ObjectSlot.Buffer) return;
        if (Attributes.GetUnknown80__() || this is VireActor) return;

        Facing = Facing.GetOppositeDirection();
}

    public void ShoveObject(CollisionContext context)
    {
        if (InvincibilityTimer != 0) return;

        var weaponObj = Game.World.GetObject(context.WeaponSlot) ?? throw new InvalidOperationException("Weapon was null.");
        var dir = weaponObj.Facing;

        if (Attributes.GetUnknown80__())
        {
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

        var player = Game.Link;

        if (IsStunned || player.StunTimer != 0 || player.InvincibilityTimer != 0)
        {
            return new PlayerCollision(false, false);
        }

        return CheckPlayerCollisionDirect();
    }

    protected PlayerCollision CheckPlayerCollisionDirect()
    {
        var player = Game.Link;

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
        var box = new Point(9, 9);

        if (!DoObjectsCollide(objCenter, playerCenter, box, out var distance))
        {
            return new PlayerCollision(false, false);
        }

        // JOE: NOTE: Is this right? Original code did: CollisionContext context = { 0 };
        var context = new CollisionContext(ObjectSlot.NoneFound, 0, 0, distance);

        if (this is PersonActor)
        {
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        if (ObjType is ObjType.Fireball2 || ObjType == (ObjType)0x5A ||
            player.GetState() != PlayerState.Idle)
        {
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        if (((int)(Facing | player.Facing) & 0xC) != 0xC &&
            ((int)(Facing | player.Facing) & 3) != 3)
        {
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        if (this is Projectile projectile &&
            projectile.IsBlockedByMagicSheild &&
            Game.World.GetItem(ItemSlot.MagicShield) == 0)
        {
            Shove(context);
            player.BeHarmed(this);
            return new PlayerCollision(true, true);
        }

        Game.Sound.PlayEffect(SoundEffect.Parry);
        return new PlayerCollision(false, true);
    }

    protected Direction CheckWorldMarginH(int x, Direction dir, bool adjust)
    {
        var curDir = Direction.Left;

        if (adjust)
        {
            x += 0xB;
        }

        if (x > Game.MarginLeft)
        {
            if (adjust)
            {
                x -= 0x17;
            }

            curDir = Direction.Right;

            if (x < Game.MarginRight)
            {
                return dir;
            }
        }

        return (dir & curDir) != 0 ? Direction.None : dir;
    }

    protected static Direction CheckWorldMarginV(int y, Direction dir, bool adjust)
    {
        var curDir = Direction.Up;

        if (adjust)
        {
            y += 0xF;
        }

        if (y > Game.MarginTop)
        {
            if (adjust)
            {
                y -= 0x21;
            }

            curDir = Direction.Down;

            if (y < Game.MarginBottom)
            {
                return dir;
            }
        }

        return (dir & curDir) != 0 ? Direction.None : dir;
    }

    protected Direction CheckWorldMargin(Direction dir)
    {
        var slot = Game.World.curObjectSlot;
        var adjust = slot > ObjectSlot.Buffer || this is LadderActor;

        // ORIGINAL: This isn't needed, because the player is first (slot=0).
        if (slot >= ObjectSlot.Player)
        {
            adjust = false;
        }

        dir = CheckWorldMarginH(X, dir, adjust);
        return CheckWorldMarginV(Y, dir, adjust);
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
                    if (dir != Direction.None)
                        return dir;
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

    private static ReadOnlySpan<Direction> _nextDirections => new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

    private Direction GetNextAltDir(ref int seq, Direction dir)
    {
        switch (seq++)
        {
            // Choose a random direction perpendicular to facing.
            case 0:
                {
                    var index = 0;
                    int r = Random.Shared.GetByte();
                    if ((r & 1) == 0)
                        index++;
                    if (Facing.IsVertical())
                        index += 2;
                    return _nextDirections[index];
                }

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
        if (Y < 0x8E && (dir & Direction.Up) != 0)
        {
            return Direction.None;
        }

        return dir;
    }

    protected Direction StopAtPersonWallUW(Direction dir)
    {
        // ($6E46) if first object is grumble or person, block movement up above $8E.

        var firstObj = Game.World.GetObject(ObjectSlot.Monster1);
        if (firstObj != null)
        {
            if (firstObj.ShouldStopAtPersonWall)
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

    protected void MoveDirection(int speed, Direction dir)
    {
        var align = 0x10;

        if (IsPlayer)
            align = 8;

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

        if (dir.IsGrowing())
        {
            frac += speed;
        }
        else
        {
            frac -= speed;
        }

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
        if ((ShoveDirection & (Direction)0x80) == 0)
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
        }
        else
        {
            ShoveDirection ^= (Direction)0x80;

            var shoveHoriz = ShoveDirection.IsHorizontal(Direction.DirectionMask);
            var facingHoriz = Facing.IsHorizontal();

            if (shoveHoriz != facingHoriz && TileOffset != 0 && !IsPlayer)
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
            }
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

            if (CheckWorldMargin(cleanDir) == Direction.None || StopAtPersonWallUW(cleanDir) == Direction.None)
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
                return;
            }

            var distance = cleanDir.IsGrowing() ? 1 : -1;

            ShoveDistance--;
            TileOffset += (sbyte)distance;

            if ((TileOffset & 0xF) == 0)
            {
                TileOffset &= 0xF;
            }
            else if (IsPlayer && (TileOffset & 7) == 0)
            {
                TileOffset &= 7;
            }

            Position += cleanDir.IsHorizontal() ? new Size(distance, 0) : new Size(0, distance);
        }
    }


    // Are these monster only?
    // -----------------------


    protected Direction GetXDirToPlayer(int x) => Game.World.GetObservedPlayerPos().X < x ? Direction.Left : Direction.Right;
    protected Direction GetYDirToPlayer(int y) => Game.World.GetObservedPlayerPos().Y < y ? Direction.Up : Direction.Down;
    protected Direction GetXDirToTruePlayer(int x) => Game.Link.X < x ? Direction.Left : Direction.Right;
    protected Direction GetYDirToTruePlayer(int y) => Game.Link.Y < y ? Direction.Up : Direction.Down;

    private Direction GetDir8ToPlayer(int x, int y)
    {
        var playerPos = Game.World.GetObservedPlayerPos();
        var dir = Direction.None;

        if (playerPos.Y < y)
            dir |= Direction.Up;
        else if (playerPos.Y > y)
            dir |= Direction.Down;

        if (playerPos.X < x)
            dir |= Direction.Left;
        else if (playerPos.X > x)
            dir |= Direction.Right;

        return dir;
    }

    protected Direction TurnTowardsPlayer8(int x, int y, Direction facing)
    {
        var dirToPlayer = GetDir8ToPlayer(x, y);
        var dirIndex = (uint)facing.GetDirection8Ord(); // uint required.

        dirIndex = (dirIndex + 1) % 8;

        for (var i = 0; i < 3; i++)
        {
            if (dirIndex.GetDirection8() == dirToPlayer)
                return facing;
            dirIndex = (dirIndex - 1) % 8;
        }

        dirIndex = (dirIndex + 1) % 8;

        for (var i = 0; i < 3; i++)
        {
            var dir = dirIndex.GetDirection8();
            if ((dir & dirToPlayer) != 0)
            {
                if ((dir | dirToPlayer) < (Direction)7)
                    return dir;
            }
            dirIndex = (dirIndex + 1) % 8;
        }

        dirIndex = (dirIndex - 1) % 8;
        return dirIndex.GetDirection8();
    }

    protected static Direction TurnRandomly8(Direction facing)
    {
        switch (Random.Shared.GetByte())
        {
            case >= 0xA0:
                ;   // keep going in the same direction
                break;
            case >= 0x50:
                facing = facing.GetNextDirection8();
                break;
            default:
                facing = facing.GetPrevDirection8();
                break;
        }

        return facing;
    }

    protected ObjectSlot Shoot(ObjType shotType, int x, int y, Direction facing)
    {
        var slot = Game.World.FindEmptyMonsterSlot();
        if (slot < 0)
        {
            return ObjectSlot.NoneFound;
        }

        var thisSlot = Game.World.curObjectSlot;
        var oldActiveShots = Game.World.activeShots;
        var thisPtr = Game.World.GetObject(thisSlot);

        var shot = shotType == ObjType.Boomerang
            ? GlobalFunctions.MakeBoomerang(Game, x, y, facing, 0x51, 2.5f, thisPtr, slot)
            : GlobalFunctions.MakeProjectile(Game.World, shotType, x, y, facing, slot);

        var newActiveShots = Game.World.activeShots;
        if (oldActiveShots != newActiveShots && newActiveShots > 4)
        {
            return ObjectSlot.NoneFound;
        }

        Game.World.SetObject(slot, shot);
        // In the original, they start in state $10. But, that was only a way to say that the object exists.
        shot.ObjTimer = 0;
        return slot;
    }

    protected void ShootFireball(ObjType type, int x, int y)
    {
        Game.ShootFireball(type, x, y);
    }
}

internal enum ObjType
{
    None,

    BlueLynel,
    RedLynel,
    BlueMoblin,
    RedMoblin,
    BlueGoriya,
    RedGoriya,
    RedSlowOctorock,
    RedFastOctorock,
    BlueSlowOctorock,
    BlueFastOctorock,
    RedDarknut,
    BlueDarknut,
    BlueTektite,
    RedTektite,
    BlueLeever,
    RedLeever,
    Zora,
    Vire,
    Zol,
    ChildGel,
    Gel,
    PolsVoice,
    LikeLike,
    LittleDigdogger,
    Unknown1__,
    Peahat,
    BlueKeese,
    RedKeese,
    BlackKeese,
    Armos,
    Boulders,
    Boulder,
    Ghini,
    FlyingGhini,
    BlueWizzrobe,
    RedWizzrobe,
    PatraChild1,
    PatraChild2,
    Wallmaster,
    Rope,
    Unknown5__,
    Stalfos,
    Bubble1,
    Bubble2,
    Bubble3,
    Whirlwind,
    PondFairy,
    Gibdo,
    ThreeDodongos,
    OneDodongo,
    BlueGohma,
    RedGohma,
    RupieStash,
    Grumble,
    Zelda,
    Digdogger1,
    Digdogger2,
    RedLamnola,
    BlueLamnola,
    Manhandla,
    Aquamentus,
    Ganon,
    GuardFire,
    StandingFire,
    Moldorm,
    Gleeok1,
    Gleeok2,
    Gleeok3,
    Gleeok4,
    GleeokHead,
    Patra1,
    Patra2,
    Trap,
    TrapSet4,

    Person1,
    Person2,
    Person3,
    Person4,
    Person5,
    Person6,
    Person7,
    Person8,

    FlyingRock,
    Unknown54__,
    Fireball,
    Fireball2,
    PlayerSwordShot,

    OldMan,
    OldWoman,
    Merchant,
    FriendlyMoblin,

    MagicWave = OldMan,
    MagicWave2 = OldWoman,
    Arrow = FriendlyMoblin,

    Boomerang,
    DeadDummy,
    FluteSecret,
    Ladder,
    Item,

    Dock,
    Rock,
    RockWall,
    Tree,
    Headstone,

    Unknown66__,
    Unknown67__,
    Block,
    Unknown69__,

    Cave1,
    Cave2,
    Cave3,
    Cave4,
    Cave5,
    Cave6,
    Cave7,
    Cave8,
    Cave9,
    Cave10,
    Cave11,
    Cave12,
    Cave13,
    Cave14,
    Cave15,
    Cave16,
    Cave17,
    Cave18,
    Cave19,
    Cave20,

    Bomb,
    PlayerSword,
    Fire,
    Rod,
    Food,

    Player,

    PersonEnd = Person8 + 1,
    PersonTypes = PersonEnd - Person1,
    CaveMedicineShop = Cave11,
    CaveShortcut = Cave5,
};