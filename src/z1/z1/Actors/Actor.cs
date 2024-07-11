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

internal abstract class Actor
{
    public Game Game { get; }

    public Point Position { get; set; }
    public Size Size { get; set; }
    public Direction Dir = Direction.Left;

    public Rectangle Rect => new(Position, Size);
    public int X {
        get => Position.X;
        set => Position += new Size(value, 0);
    }
    public int Y
    {
        get => Position.Y;
        set => Position += new Size(0, value);
    }

    public bool IsDeleted;
    public byte Decoration;
    protected byte HP;
    public byte InvincibilityTimer;
    protected byte InvincibilityMask;
    protected Direction ShoveDirection = Direction.None;
    protected byte ShoveDistance = 0;
    public Direction Facing = Direction.None;
    public byte TileOffset = 0;
    protected byte Fraction;
    public byte Moving;
    public Direction MovingDirection => (Direction)Moving;
    public byte ObjTimer;
    protected byte StunTimer;
    public ActorFlags Flags;

    public ObjType ObjType => throw new Exception(); // TODO

    public ObjectAttr Attributes;

    protected bool IsStunned => _isStunned();
    protected bool IsParalyzed { get; set; }
    public int PlayerDamage => 0; // TODO

    public ActorColor Color;

    public Actor(Game game, int x = 0, int y = 0)
    {
        Game = game;
        Position = new(x, y);

        Attributes = game.World.GetObjectAttrs(ObjType);
    }

    public abstract void Update();
    public abstract void Draw();

    public virtual bool IsPlayer => false;
    public virtual bool ShouldStopAtPersonWall => false;
    public virtual bool CountsAsLiving => true;
    public virtual bool CanHoldRoomItem => false;
    public virtual bool IsReoccuring => true;
    public bool IsRoomItem { get; set; }
    public virtual bool IsUnderworldPerson => true;

    public static Actor FromType(ObjType type, Game game, int x, int y, bool isRoomItem)
    {
        return type switch {
            ObjType.BlueLynel => new LynelActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedLynel => new LynelActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueMoblin => new MoblinActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedMoblin => new MoblinActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueGoriya => new GoriyaActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedGoriya => new GoriyaActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedSlowOctorock => new OctorokActor(game, ActorColor.Red, false, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedFastOctorock => new OctorokActor(game, ActorColor.Red, true, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueSlowOctorock => new OctorokActor(game, ActorColor.Blue, false, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueFastOctorock => new OctorokActor(game, ActorColor.Blue, true, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedDarknut => new RedDarknutActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueDarknut => new BlueDarknutActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueTektite => new TektiteActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedTektite => new TektiteActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueLeever => new LeeverActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedLeever => new LeeverActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.Zora => new ZoraActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Vire => new VireActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Zol => new ZolActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.ChildGel => new ChildGelActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Gel => new GelActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.PolsVoice => new PolsVoiceActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.LikeLike => new LikeLikeActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.LittleDigdogger => new DigdoggerActor(game, DigdoggerType.Little, x, y) { IsRoomItem = isRoomItem },
            // ObjType.Unknown1__ => new Unknown1__Actor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Peahat => new PeahatActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueKeese => new KeeseActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedKeese => new KeeseActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlackKeese => new KeeseActor(game, ActorColor.Black, x, y) { IsRoomItem = isRoomItem },
            ObjType.Armos => new ArmosActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Boulders => new BouldersActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Boulder => new BoulderActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Ghini => new GhiniActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.FlyingGhini => new FlyingGhiniActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueWizzrobe => new WizzrobeActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedWizzrobe => new WizzrobeActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.PatraChild1 => new PatraChildActor(game, PatraType.Circle, x, y) { IsRoomItem = isRoomItem },
            ObjType.PatraChild2 => new PatraChildActor(game, PatraType.Spin, x, y) { IsRoomItem = isRoomItem },
            ObjType.Wallmaster => new WallmasterActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Rope => new RopeActor(game, x, y) { IsRoomItem = isRoomItem },
            // ObjType.Unknown5__ => new Unknown5__Actor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Stalfos => new StalfosActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Bubble1 => new Bubble1Actor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Bubble2 => new Bubble2Actor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Bubble3 => new Bubble3Actor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Whirlwind => new WhirlwindActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.PondFairy => new PondFairyActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Gibdo => new GibdoActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.ThreeDodongos => new DodongosActor(game, 3, x, y) { IsRoomItem = isRoomItem },
            ObjType.OneDodongo => new DodongosActor(game, 1, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueGohma => new BlueGohmaActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedGohma => new RedGohmaActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.RupieStash => new RupieStashActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Grumble => new GrumbleActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Zelda => new ZeldaActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Digdogger1 => new DigdoggerActor(game, DigdoggerType.One, x, y) { IsRoomItem = isRoomItem },
            ObjType.Digdogger2 => new DigdoggerActor(game, DigdoggerType.Two, x, y) { IsRoomItem = isRoomItem },
            ObjType.RedLamnola => new LamnolaActor(game, ActorColor.Red, x, y) { IsRoomItem = isRoomItem },
            ObjType.BlueLamnola => new LamnolaActor(game, ActorColor.Blue, x, y) { IsRoomItem = isRoomItem },
            ObjType.Manhandla => new ManhandlaActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Aquamentus => new AquamentusActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Ganon => new GanonActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.GuardFire => new GuardFireActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.StandingFire => new StandingFireActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Moldorm => new MoldormActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Gleeok1 => new GleeokActor(game, 1, x, y) { IsRoomItem = isRoomItem },
            ObjType.Gleeok2 => new GleeokActor(game, 2, x, y) { IsRoomItem = isRoomItem },
            ObjType.Gleeok3 => new GleeokActor(game, 3, x, y) { IsRoomItem = isRoomItem },
            ObjType.Gleeok4 => new GleeokActor(game, 4, x, y) { IsRoomItem = isRoomItem },
            ObjType.GleeokHead => new GleeokHeadActor(game, x, y) { IsRoomItem = isRoomItem },
            ObjType.Patra1 => new PatraActor(game, PatraType.Circle, x, y) { IsRoomItem = isRoomItem },
            ObjType.Patra2 => new PatraActor(game, PatraType.Spin, x, y) { IsRoomItem = isRoomItem },
            ObjType.Trap => new TrapActor(game, x, y) { IsRoomItem = isRoomItem },
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

    public static void MoveSimple8(ref float x, ref float y, Direction dir, int speed)
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

    public static Size MoveSimple8(Direction dir, int speed)
    {
        int x = 0, y = 0;
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
                var slot = Game.CurrentObjectSlot;
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
        if (Game.GetItem(ItemSlot.Clock) != 0) return true;
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
        var weaponObj = Game.GetObject(slot);
        if (weaponObj == null) return;

        if (weaponObj is PlayerSwordProjectile wave)
        {
            if (wave.State != ProjectileState.Flying) return;
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
            var itemValue = Game.GetItem(ItemSlot.Sword);
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
        var obj = Game.GetObject(slot);
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

    private static Span<byte> _swordPowers => new byte[] { 0, 0x10, 0x20, 0x40 };

    protected void CheckSword(ObjectSlot slot)
    {
        var sword = Game.GetObject<PlayerWeapon>(slot);
        if (sword == null || sword.State != 1) return;

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

        if (this is SwordPlayerWeapon)
        {
            var itemValue = Game.GetItem(ItemSlot.Sword);
            int power = _swordPowers[itemValue];
            context.Damage = power;
        }
        else if (this is RodPlayerWeapon)
        {
            context.Damage = 0x20;
        }

        if (CheckCollisionNoShove(context, box))
            ShoveCommon(context);
    }

    private static Span<int> _arrowPowers => new[] { 0, 0x20, 0x40 };

    protected bool CheckArrow(ObjectSlot slot)
    {
        var arrow = Game.GetObject<ArrowProjectile>(slot);
        if (arrow == null) return false;

        if (arrow.State != ProjectileState.Flying)
            return false;

        var itemValue = Game.GetItem(ItemSlot.Arrow);
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
        var weaponObj = Game.GetObject(context.WeaponSlot);
        if (weaponObj == null) return false;

        Point objCenter = CalcObjMiddle();
        weaponOffset.X += weaponObj.X;
        weaponOffset.Y += weaponObj.Y;

        if (!DoObjectsCollide(objCenter, weaponOffset, box, out context.Distance)) return false;

        if (weaponObj is BoomerangProjectile boomerang)
        {
            boomerang.State = ProjectileState.Unknown5;

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

        var weaponObj = Game.GetObject(context.WeaponSlot) ?? throw new InvalidOperationException("Weapon was null.");

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
                Game.Sound.Play(SoundEffect.BossHit);
                Game.Sound.StopEffect(StopEffect.AmbientInstance);
                DealDamage(context);
                // The original game plays sounds again. But why, if we already played boss hit effect?
            }

            Game.Sound.Play(SoundEffect.Parry);
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
        Game.Sound.Play(SoundEffect.MonsterHit);

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

        Game.IncrementKilledObjectCount(allowBombDrop);

        Decoration = 0x10;
        Game.Sound.Play(SoundEffect.MonsterDie);

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

    protected void PlayParrySound() => Game.Sound.Play(SoundEffect.Parry);

    protected void PlayBossHitSoundIfHit()
    {
        if (InvincibilityTimer == 0x10)
        {
            Game.Sound.Play(SoundEffect.BossHit);
        }
    }

    protected void PlayBossHitSoundIfDied()
    {
        if (Decoration != 0)
        {
            Game.Sound.Play(SoundEffect.BossHit);
            Game.Sound.StopEffect(StopEffect.AmbientInstance);
        }
    }

    protected void ShoveCommon(CollisionContext context)
    {
        if (this is RedDarknutActor || this is BlueDarknutActor)
        {
            var weaponObj = Game.GetObject(context.WeaponSlot) ?? throw new InvalidOperationException("Weapon was null.");
            var combinedDir = (int)Facing | (int)weaponObj.Facing;

            if (combinedDir == 3 || combinedDir == 0xC)
            {
                Game.Sound.Play(SoundEffect.Parry);
                return;
            }
        }

        Shove(context);
    }

    protected void Shove(CollisionContext context)
    {
        if ((InvincibilityMask & (int)context.DamageType) != 0) return;

        if (context.WeaponSlot != 0)
        {
            ShoveObject(context);
        }
        else
        {
            var player = Game.Link;

            if (player.InvincibilityTimer != 0) return;

            bool useY = false;

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

            Direction dir = Direction.None;

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

            if (Game.CurrentObjectSlot >= ObjectSlot.Buffer) return;
            if (Attributes.GetUnknown80__() || this is VireActor) return;

            Facing = Facing.GetOppositeDirection();
        }
    }

    public void ShoveObject(CollisionContext context)
    {
        if (InvincibilityTimer != 0)
            return;

        var weaponObj = Game.GetObject(context.WeaponSlot) ?? throw new InvalidOperationException("Weapon was null.");
        var dir = weaponObj.Facing;

        if (Attributes.GetUnknown80__())
            dir |= (Direction)0x40;

        if (this is GohmaActor gohma)
        {
            if (((gohma.GetCurrentCheckPart() != 3) && (gohma.GetCurrentCheckPart() != 4))
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
            return new(false, false);
        }

        return CheckPlayerCollisionDirect();
    }

    protected PlayerCollision CheckPlayerCollisionDirect()
    {
        var player = Game.Link;

        if (player.GetState() == PlayerState.Paused
            || player.IsParalyzed)
        {
            return new(false, false);
        }

        if (this is Projectile shot && !shot.IsInShotStartState())
        {
            return new(false, false);
        }

        var objCenter = CalcObjMiddle();
        var playerCenter = player.GetMiddle();
        var box = new Point(9, 9);

        if (!DoObjectsCollide(objCenter, playerCenter, box, out var distance))
        {
            return new(false, false);
        }

        var context = new CollisionContext(0, 0, 0, Point.Empty);
        context.Distance = distance;

        if (this is PersonActor)
        {
            Shove(context);
            player.BeHarmed(this);
            return new(true, true);
        }

        if (this is Fireball2Projectile || // TODO: GetType() == 0x5A ||
            player.GetState() != PlayerState.Idle)
        {
            Shove(context);
            player.BeHarmed(this);
            return new(true, true);
        }

        if (((int)(Facing | player.Facing) & 0xC) != 0xC &&
            ((int)(Facing | player.Facing) & 3) != 3)
        {
            Shove(context);
            player.BeHarmed(this);
            return new(true, true);
        }

        if (this is Projectile projectile &&
            projectile.IsBlockedByMagicSheild &&
            Game.GetItem(ItemSlot.MagicShield) == 0)
        {
            Shove(context);
            player.BeHarmed(this);
            return new(true, true);
        }

        Game.Sound.Play(SoundEffect.Parry);
        return new(false, true);
    }

    protected Direction CheckWorldMarginH(int x, Direction dir, bool adjust)
    {
        Direction curDir = Direction.Left;

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

    protected Direction CheckWorldMarginV(int y, Direction dir, bool adjust)
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
        var slot = Game.CurrentObjectSlot;
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
    protected Direction CheckWorldBounds(Direction dir)
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
        int dirOrd = ((Direction)Moving).GetOrdinal();
        return dirOrd.GetOrdDirection();
    }

    protected Direction FindUnblockedDir(Direction dir, TileCollisionStep firstStep)
    {
        TileCollision collision;
        int i = 0;

        // JOE: This was a goto statement before. My change is a bit sus.
        var startOnNext = firstStep == TileCollisionStep.NextDir;

        do
        {
            if (!startOnNext)
            {
                collision = Game.CollidesWithTileMoving(X, Y, dir, false);
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

            if (!Game.CollidesWithTileMoving(X, Y, dir, IsPlayer))
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

    private static Span<Direction> _nextDirections => new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

    private Direction GetNextAltDir(ref int seq, Direction dir)
    {
        switch (seq++)
        {
            // Choose a random direction perpendicular to facing.
            case 0:
                {
                    int index = 0;
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

        var firstObj = Game.GetObject(ObjectSlot.Monster1);
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
            int dirOrd = ((Direction)Moving).GetOrdinal();
            dir = dirOrd.GetOrdDirection();
        }

        dir = dir & Direction.Mask;

        // Original: [$E] := 0
        // Maybe it's only done to set up the call to FindUnblockedDir in CheckTileCollision?

        dir = CheckWorldMargin(dir);
        dir = CheckTileCollision(dir);

        MoveDirection(speed, dir);
    }

    protected void MoveDirection(int speed, Direction dir)
    {
        int align = 0x10;

        if (IsPlayer)
            align = 8;

        MoveWhole(speed, dir, align);
    }

    void MoveWhole(int speed, Direction dir, int align)
    {
        if (dir == Direction.None) return;

        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
    }

    void MoveFourth(int speed, Direction dir, int align)
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

        int carry = frac >> 8;
        Fraction = (byte)(frac & 0xFF);

        if (TileOffset != align && TileOffset != -align)
        {
            TileOffset += (byte)carry;
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

            var shoveHoriz = ShoveDirection.IsHorizontal(Direction.Mask);
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
        var cleanDir = ShoveDirection & Direction.Mask;

        for (int i = 0; i < 4; i++)
        {
            if (TileOffset == 0)
            {
                Position = new Point(Position.X & 0xF8, Position.Y & 0xF8 | 5);

                if (Game.CollidesWithTileMoving(X, Y, cleanDir, IsPlayer))
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
            TileOffset += (byte)distance;

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