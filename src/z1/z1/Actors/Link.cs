﻿using z1.Player;

namespace z1.Actors;

internal enum PlayerState { Idle, Wielding, Paused }

internal sealed class Link : Actor, IThrower
{
    public const int WalkSpeed = 0x60;
    public const int StairsSpeed = 0x30;

    private const int WalkDurationFrames = 12;

    public static ReadOnlySpan<byte> PlayerLimits => new byte[] { 0xF0, 0x00, 0xDD, 0x3D };

    public override bool IsPlayer => true;

    public bool IsParalyzed;

    private int _walkFrame = 0;
    private int _state = 0; // JOE: TODO: Enumify this.
    private byte _speed;
    private TileBehavior _tileBehavior;
    private byte _animTimer;
    private byte _avoidTurningWhenDiag;   // 56
    private byte _keepGoingStraight;      // 57
    private InputButtons _curButtons;

    public readonly SpriteAnimator Animator;

    public Link(Game game, Direction facing = Direction.Up) : base(game, ObjType.Player)
    {
        _speed = WalkSpeed;
        Facing = facing;
        Decoration = 0;

        Animator = new SpriteAnimator() { Time = 0, DurationFrames = WalkDurationFrames };
    }

    public void DecInvincibleTimer()
    {
        if (InvincibilityTimer > 0 && (Game.GetFrameCounter() & 1) == 0)
        {
            InvincibilityTimer--;
        }
    }

    public override void Update()
    {
        // Do this in order to flash while you have the clock. It doesn't matter if it becomes zero,
        // because foes will check invincibilityTimer AND the clock item.
        // I suspect that the original game did this in the drawing code.
        var profile = Game.World.GetProfile();
        if (profile.GetItem(ItemSlot.Clock) != 0)
        {
            InvincibilityTimer += 0x10;
        }

        _curButtons = Game.Input.GetButtons();

        // It looks like others set player's state to $40. They don't bitwise-and it with $40.
        if ((_state & 0xC0) == 0x40)
            return;

        HandleInput();

        if (IsParalyzed)
        {
            Moving &= 0xF0;
        }

        if ((_state & 0xF0) != 0x10 && (_state & 0xF0) != 0x20)
        {
            Move();
        }

        if (Game.World.GetMode() == GameMode.LeaveCellar) return;

        if (Game.World.WhirlwindTeleporting == 0)
        {
            CheckWater();
            CheckDoorway();
            if (Game.World.GetMode() == GameMode.Play)
            {
                CheckWarp();
            }
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
        if (_state != 0)
        {
            if (_animTimer != 0)
            {
                _animTimer--;
            }

            if (_animTimer == 0)
            {
                if ((_state & 0x30) == 0x10 || (_state & 0x30) == 0x20)
                {
                    Animator.Time = 0;
                    _animTimer = (byte)(_state & 0xF);
                    _state |= 0x30;
                }
                else if ((_state & 0x30) == 0x30)
                {
                    Animator.AdvanceFrame();
                    _state &= 0xC0;
                }
            }
        }
        else if (Moving != 0)
        {
            Animator.Advance();
        }
    }

    private TileCollision CollidesWithTileMovingUW(int x, int y, Direction dir)
    {
        if (dir == Direction.Up && y == 0x5D)
        {
            y -= 8;
        }

        var collision1 = Game.World.CollidesWithTileMoving(x, y, dir, true);

        if (dir.IsHorizontal() && collision1.TileBehavior != TileBehavior.Wall)
        {
            var collision2 = Game.World.CollidesWithTileMoving(x, y - 8, dir, true);

            if (collision2.TileBehavior == TileBehavior.Wall)
            {
                return collision2;
            }
        }

        return collision1;
    }

    private TileCollision CollidesWithTileMoving(int x, int y, Direction dir)
    {
        if (!Game.World.IsUWMain())
        {
            return Game.World.CollidesWithTileMoving(x, y, dir, true);
        }

        var collision = CollidesWithTileMovingUW(x, y, dir);
        if (collision.TileBehavior == TileBehavior.Doorway)
        {
            collision.Collides = false;
        }

        return collision;
    }

    // F23C
    private void CheckWater()
    {
        var mode = Game.World.GetMode();

        if (mode is GameMode.Leave or < GameMode.Play) return;

        if (TileOffset != 0)
        {
            if ((TileOffset & 7) != 0) return;
            TileOffset = 0;
            if (mode != GameMode.Play) return;
            Game.World.fromUnderground = 0;
        }

        if (mode != GameMode.Play) return;

        if (Game.World.IsOverworld())
        {
            if (!Game.World.DoesRoomSupportLadder()) return;
        }

        if (Game.World.doorwayDir != Direction.None
            || Game.World.GetItem(ItemSlot.Ladder) == 0
            || (_state & 0xC0) == 0x40
            || Game.World.GetLadder() != null)
        {
            return;
        }

        var collision = CollidesWithTileMoving(X, Y, Facing);

        // The original game checked for specific water tiles in the OW and UW.
        if (collision.TileBehavior != TileBehavior.Water) return;
        if (Moving == 0) return;
        if (Moving != (int)Facing) return;

        ReadOnlySpan<sbyte> ladderOffsetsX = [ 0x10, -0x10, 0x00, 0x00 ];
        ReadOnlySpan<sbyte> ladderOffsetsY = [ 0x03, 0x03, 0x13, -0x05 ];

        var dirOrd = MovingDirection.GetOrdinal();

        var ladder = new LadderActor(Game, X + ladderOffsetsX[dirOrd], Y + ladderOffsetsY[dirOrd]);
        Game.World.SetLadder(ladder);
    }

    private void CheckWarp()
    {
        if (Game.World.fromUnderground != 0 || TileOffset != 0) return;

        if (Game.World.IsOverworld() && Game.World.curRoomId == 0x22)
        {
            if ((X & 7) != 0) return;
        }
        else
        {
            if ((X & 0xF) != 0) return;
        }

        if ((Y & 0xF) != 0xD) return;

        var fineRow = (Y + 3 - 0x40) / 8;
        var fineCol = X / 8;

        Game.World.CoverTile(fineRow, fineCol);
    }

    private void CheckDoorway()
    {
        var collision = Game.World.PlayerCoversTile(X, Y);

        if (collision.TileBehavior == TileBehavior.Doorway)
        {
            if (Game.World.doorwayDir == Direction.None)
            {
                Game.World.doorwayDir = Facing;
            }
        }
        else
        {
            if (Game.World.doorwayDir != Direction.None)
            {
                Game.World.doorwayDir = Direction.None;
            }
        }
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
        ReadOnlySpan<byte> outerBorderOW = [ 0x07, 0xE9, 0x45, 0xD6 ];
        ReadOnlySpan<byte> outerBorderUW = [ 0x17, 0xD9, 0x55, 0xC6 ];
        ReadOnlySpan<byte> innerBorder = [ 0x1F, 0xD1, 0x54, 0xBE ];

        var coord = Facing.IsHorizontal() ? X : Y;
        var outerBorder = Game.World.IsOverworld() ? outerBorderOW : outerBorderUW;

        if (IsInBorder(coord, Facing, outerBorder))
        {
            _curButtons.Buttons = 0;
            if (!Game.World.IsOverworld())
            {
                var mask = Facing.IsVertical() ? Direction.VerticalMask : Direction.HorizontalMask;
                Moving = (byte)(Moving & (byte)mask);
            }
        }
        else if (IsInBorder(coord, Facing, innerBorder))
        {
            _curButtons.Mask(Button.A);
        }
    }

    private void HandleInput()
    {
        Moving = (byte)(_curButtons.Buttons & Button.MovingMask);

        if (_state == 0)
        {
            FilterBorderInput();

            if (_curButtons.Has(Button.A))
            {
                UseWeapon();
            }

            if (_curButtons.Has(Button.B))
            {
                UseItem();
            }
        }

        if (ShoveDirection != 0) return;

        if (!Game.World.IsOverworld())
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
        if (Game.World.doorwayDir != Direction.None && Moving != 0)
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
        }
    }

    // $B38D
    private void Align()
    {
        if (Moving == 0) return;

        var singleMoving = GetSingleMoving();

        if (singleMoving == Facing)
        {
            SetSpeed();
            return;
        }

        var dir = singleMoving | Facing;
        if (dir != Direction.OppositeHorizontals && dir != Direction.OppositeVerticals)
        {
            if (_keepGoingStraight != 0)
            {
                SetSpeed();
                return;
            }

            if (Math.Abs(TileOffset) >= 4)
                return;

            if (Facing.IsGrowing())
            {
                if (TileOffset < 0) return;
            }
            else
            {
                if (TileOffset >= 0) return;
            }

            Facing = Facing.GetOppositeDirection();

            if (TileOffset >= 0)
                TileOffset -= 8;
            else
                TileOffset += 8;
        }
        else
        {
            Facing = singleMoving;
            Moving = (byte)singleMoving;
        }
    }

    // $B2CF
    private void CalcAlignedMoving()
    {
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

                var collision = CollidesWithTileMoving(X, Y, dir);
                _tileBehavior = collision.TileBehavior;
                if (!collision.Collides)
                {
                    lastClearDir = dir;
                    clearDirCount++;
                }
            }
        }

        if (dirCount == 0) return;

        if (dirCount == 1)
        {
            _avoidTurningWhenDiag = 0;
            Facing = lastDir;
            Moving = (byte)lastDir;
            SetSpeed();
            return;
        }

        if (clearDirCount == 0)
        {
            Moving = 0;
            return;
        }

        _keepGoingStraight++;

        if (clearDirCount == 1 || Game.World.IsOverworld())
        {
            _avoidTurningWhenDiag = 0;
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

        if (Game.World.IsOverworld() || X != 0x78 || Y != 0x5D)
        {
            Moving = (byte)Facing;
            SetSpeed();
            return;
        }

    // B34D
    TakeFacingPerpDir:
        // Moving dir is diagonal. Take the dir component that's perpendicular to facing.
        _avoidTurningWhenDiag++;

        ReadOnlySpan<byte> axisMasks = [ 3, 3, 0xC, 0xC ];

        var dirOrd = Facing.GetOrdinal();
        uint movingInFacingAxis = (uint)(Moving & axisMasks[dirOrd]);
        uint perpMoving = Moving ^ movingInFacingAxis;
        Facing = (Direction)perpMoving;
        Moving = (byte)perpMoving;
        SetSpeed();
    }

    // $B366
    private void SetSpeed()
    {
        byte newSpeed = WalkSpeed;

        if (Game.World.IsOverworld())
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

    private static readonly AnimationId[] _thrustAnimMap = new[]
    {
        AnimationId.LinkThrust_Right,
        AnimationId.LinkThrust_Left,
        AnimationId.LinkThrust_Down,
        AnimationId.LinkThrust_Up
    };

    private static readonly AnimationId[][] _animMap = new[]
    {
        new[] {
            AnimationId.LinkWalk_NoShield_Right,
            AnimationId.LinkWalk_NoShield_Left,
            AnimationId.LinkWalk_NoShield_Down,
            AnimationId.LinkWalk_NoShield_Up,
        },

        new[] {
            AnimationId.LinkWalk_LittleShield_Right,
            AnimationId.LinkWalk_LittleShield_Left,
            AnimationId.LinkWalk_LittleShield_Down,
            AnimationId.LinkWalk_LittleShield_Up,
        },

        new[] {
            AnimationId.LinkWalk_BigShield_Right,
            AnimationId.LinkWalk_BigShield_Left,
            AnimationId.LinkWalk_BigShield_Down,
            AnimationId.LinkWalk_BigShield_Up,
        }
    };

    private void SetFacingAnim()
    {
        var shieldState = Game.World.GetItem(ItemSlot.MagicShield) + 1;
        var dirOrd = Facing.GetOrdinal();
        var map = (_state & 0x30) == 0x10 || (_state & 0x30) == 0x20 ? _thrustAnimMap : _animMap[shieldState];
        Animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, map[dirOrd]);
    }

    public override void Draw()
    {
        var palette = CalcPalette(Palette.Player);
        var y = Y;

        if (Game.World.IsOverworld() || Game.World.GetMode() == GameMode.PlayCellar)
        {
            y += 2;
        }

        SetFacingAnim();
        Animator.Draw(TileSheet.PlayerAndItems, X, y, palette);
    }

    public PlayerState GetState()
    {
        if ((_state & 0xC0) == 0x40) return PlayerState.Paused;
        if ((_state & 0xF0) != 0) return PlayerState.Wielding;
        return PlayerState.Idle;
    }

    public void SetState(PlayerState state)
    {
        _state = state switch {
            PlayerState.Paused => 0x40,
            PlayerState.Idle => 0,
            _ => _state
        };
    }

    public Rectangle GetBounds() => new(X, Y + 8, 16, 8);

    public Point GetMiddle()
    {
        // JOE: This seems silly?
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
        var damage = Game.World.GetPlayerDamage(collider.ObjType);
        BeHarmed(collider, damage);
    }

    // JOE: NOTE: This used to have a `Point& otherMiddle` argument that was unused?
    public void BeHarmed(Actor collider, int damage)
    {
        if (Game.Cheats.GodMode) return;

        if (collider is not WhirlwindActor)
        {
            Game.Sound.PlayEffect(SoundEffect.PlayerHit);
        }

        var ringValue = Game.Profile.Items.GetValueOrDefault(ItemSlot.Ring, 0);

        damage >>= ringValue;

        Game.World.ResetKilledObjectCount();

        if (Game.Profile.Hearts <= damage)
        {
            Game.Profile.Hearts = 0;
            _state = 0;
            Facing = Direction.Down;
            Game.World.GotoDie();
        }
        else
        {
            Game.Profile.Hearts -= damage;
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
        var itemValue = Game.World.GetItem(ItemSlot.Candle);
        if (itemValue == 1 && Game.World.candleUsed)
            return 0;

        Game.World.candleUsed = true;

        // Rewrite findfreeslot to allow this to work.
        for (var i = ObjectSlot.FirstFire; i < ObjectSlot.LastFire; i++)
        {
            if (Game.World.HasObject(i)) continue;

            MoveSimple(ref x, ref y, facingDir, 0x10);

            Game.Sound.PlayEffect(SoundEffect.Fire);

            var fire = new FireActor(Game, x, y, facingDir);
            Game.World.SetObject(i, fire);
            return 12;
        }

        return 0;
    }

    private int UseBomb(int x, int y, Direction facingDir)
    {
        ObjectSlot i;

        for (i = ObjectSlot.FirstBomb; i < ObjectSlot.LastBomb; i++)
        {
            var obj = Game.World.GetObject(i);
            if (obj == null || obj is not BombActor) break;
        }

        if (i == ObjectSlot.LastBomb) return 0;

        var freeSlot = i;
        var otherSlot = ObjectSlot.FirstBomb;

        if (freeSlot == ObjectSlot.FirstBomb)
            otherSlot++;

        var otherBomb = Game.World.GetObject<BombActor>(otherSlot);
        if (otherBomb != null && otherBomb.BombState < BombState.Blasting)
        {
            return 0;
        }

        MoveSimple(ref x, ref y, facingDir, 0x10);

        var bomb = new BombActor(Game, x, y);
        Game.World.SetObject(freeSlot, bomb);
        Game.World.DecrementItem(ItemSlot.Bombs);
        Game.Sound.PlayEffect(SoundEffect.PutBomb);
        return 7;
    }

    private int UseBoomerang(int x, int y, Direction facingDir)
    {
        // ORIGINAL: Trumps food. Look at $05:8E40. The behavior is tied to the statement below.
        //           Skip throw, if there's already a boomerang in the slot. But overwrite Food.
        if (Game.World.HasObject(ObjectSlot.Boomerang)) return 0;

        var itemValue = Game.World.GetItem(ItemSlot.Boomerang);

        MoveSimple(ref x, ref y, facingDir, 0x10);

        if (MovingDirection != Direction.None)
        {
            facingDir = MovingDirection;
        }

        var distance = itemValue == 2 ? BoomerangProjectile.RedsDistance : BoomerangProjectile.YellowsDistance;
        var boomerang = GlobalFunctions.MakeBoomerang(Game, x, y, facingDir, distance, 3.0f, this, ObjectSlot.Boomerang);
        Game.World.SetObject(ObjectSlot.Boomerang, boomerang);
        return 6;
    }

    private int UseArrow(int x, int y, Direction facingDir)
    {
        if (Game.World.GetObject(ObjectSlot.Arrow) != null) return 0;
        if (Game.World.GetItem(ItemSlot.Rupees) == 0) return 0;

        Game.World.PostRupeeLoss(1);

        MoveSimple(ref x, ref y, facingDir, 0x10);

        if (facingDir.IsVertical())
        {
            x += 3;
        }

        var arrow = GlobalFunctions.MakeProjectile(Game.World, ObjType.Arrow, x, y, facingDir, ObjectSlot.Arrow);
        Game.World.SetObject(ObjectSlot.Arrow, arrow);
        Game.Sound.PlayEffect(SoundEffect.Boomerang);
        return 6;
    }

    private int UseFood(int x, int y, Direction facingDir)
    {
        if (Game.World.GetObject(ObjectSlot.Food) != null) return 0;

        MoveSimple(ref x, ref y, facingDir, 0x10);

        var food = new FoodActor(Game, x, y);
        Game.World.SetObject(ObjectSlot.Food, food);
        return 6;
    }

    private int UsePotion(int _x, int _y, Direction _facingDir)
    {
        Game.World.DecrementItem(ItemSlot.Potion);
        Game.World.PauseFillHearts();
        return 0;
    }

    private int UseRecorder(int _x, int _y, Direction _facingDir)
    {
        Game.World.UseRecorder();
        return 0;
    }

    private int UseLetter(int _x, int _y, Direction _facingDir)
    {
        var itemValue = Game.World.GetItem(ItemSlot.Letter);
        if (itemValue != 1) return 0;

        var obj = Game.World.GetObject(ObjectSlot.Monster1);
        if (obj == null || obj.ObjType != ObjType.CaveMedicineShop) return 0;

        Game.World.SetItem(ItemSlot.Letter, 2);
        return 0;
    }

    // JOE: NOTE: Return value is properly unused?
    private int UseItem()
    {
        var profile = Game.World.GetProfile();
        if (profile.SelectedItem == 0) return 0;

        var itemValue = profile.Items[profile.SelectedItem];
        if (itemValue == 0) return 0;

        if (profile.SelectedItem == ItemSlot.Rod)
        {
            return UseWeapon(ObjType.Rod, ItemSlot.Rod);
        }

        var waitFrames = 0;

        switch (profile.SelectedItem)
        {
            case ItemSlot.Bombs: waitFrames = UseBomb(X, Y, Facing); break;
            case ItemSlot.Arrow: waitFrames = UseArrow(X, Y, Facing); break;
            case ItemSlot.Candle: waitFrames = UseCandle(X, Y, Facing); break;
            case ItemSlot.Recorder: waitFrames = UseRecorder(X, Y, Facing); break;
            case ItemSlot.Food: waitFrames = UseFood(X, Y, Facing); break;
            case ItemSlot.Potion: waitFrames = UsePotion(X, Y, Facing); break;
            case ItemSlot.Letter: waitFrames = UseLetter(X, Y, Facing); break;
            case ItemSlot.Boomerang: waitFrames = UseBoomerang(X, Y, Facing); break;
        }

        if (waitFrames == 0)
        {
            return 0;
        }
        Animator.Time = 0;
        _animTimer = 6;
        _state = 0x16;
        return waitFrames + 6;
    }

    private int UseWeapon()
    {
        if (Game.World.SwordBlocked || Game.World.GetStunTimer(ObjectSlot.NoSwordTimer) != 0)
        {
            return 0;
        }

        return UseWeapon(ObjType.PlayerSword, ItemSlot.Sword);
    }

    private int UseWeapon(ObjType type, ItemSlot itemSlot)
    {
        if (Game.World.GetItem(itemSlot) == 0) return 0;
        if (Game.World.GetObject(ObjectSlot.PlayerSword) != null) return 0;

        // The original game did this:
        //   player.animTimer := 1
        //   player.state := $10
        Animator.Time = 0;
        _animTimer = 12;
        _state = 0x11;

        var sword = new PlayerSwordActor(Game, type);
        Game.World.SetObject(ObjectSlot.PlayerSword, sword);
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

        if (Game.World.doorwayDir == Direction.None)
        {
            var mode = Game.World.GetMode();

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
        var mode = Game.World.GetMode();

        if (mode == GameMode.PlayCellar)
        {
            if (Y >= 0x40 || (MovingDirection & Direction.Up) == 0)
            {
                return dir;
            }

            Game.World.LeaveCellar();
            dir = Direction.None;
            StopPlayer();
        }
        else    // Cave
        {
            dir = StopAtPersonWall(dir);

            // Handling 3 shortcut stairs in shortcut cave is handled by the Person obj, instead of here.

            if (HitsWorldLimit())
            {
                Game.World.LeaveCellar();
                dir = Direction.None;
                StopPlayer();
            }
        }

        return dir;
    }

    // 8F7B
    private Direction HandleLadder(Direction dir)
    {
        var ladder = Game.World.GetLadder();
        if (ladder == null) return dir;

        // Original: if ladder.GetState() = 0, destroy it. But, I don't see how it can get in that state.

        var distance = 0;

        if (ladder.Facing.IsVertical())
        {
            if (X != ladder.X)
            {
                Game.World.SetLadder(null);
                return dir;
            }
            distance = (Y + 3) - ladder.Y;
        }
        else
        {
            if ((Y + 3) != ladder.Y)
            {
                Game.World.SetLadder(null);
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
            Game.World.SetLadder(null);
        }
        else if (ladder.State == LadderStates.Unknown1)
        {
            dir = MoveOnLadder(dir, distance);
        }
        else
        {
            Game.World.SetLadder(null);
        }

        return dir;
    }

    // $05:8FCD
    private Direction MoveOnLadder(Direction dir, int distance)
    {
        if (Moving == 0) return Direction.None;

        var ladder = Game.World.GetLadder() ?? throw new Exception();
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

        if (CollidesWithTileMoving(X, Y - 8, dir)) return Direction.None;

        // ORIGINAL: The routine will run again. It'll finish, because now (ladder.facing = dir),
        //           which is one of the conditions that ends this function.
        //           But, why not return dir right here?
        return MoveOnLadder(dir, distance);
    }

    // $01:A13E  stop object, if too close to a block
    private Direction StopAtBlock(Direction dir)
    {
        for (var i = ObjectSlot.Buffer; i >= ObjectSlot.Monster1; i--)
        {
            var obj = Game.World.GetObject(i);
            if (obj is IBlocksPlayer block && block.CheckCollision() == CollisionResponse.Blocked)
            {
                return Direction.None;
            }
        }
        return dir;
    }

    private new Direction CheckTileCollision(Direction dir) // JOE: TODO: Is this supposed to be "new"'ed?
    {
        if (Game.World.doorwayDir != Direction.None) return CheckWorldBounds(dir);
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
        if (Game.World.GetMode() == GameMode.Play
            && Game.World.GetLadder() == null
            && TileOffset == 0)
        {
            if (HitsWorldLimit())
            {
                Game.World.LeaveRoom(Facing, Game.World.curRoomId);
                dir = Direction.None;
                StopPlayer();
            }
        }

        return dir;
    }

    private Direction FindUnblockedDir(Direction dir)
    {
        var collision = CollidesWithTileMoving(X, Y, dir);
        if (!collision.Collides)
        {
            dir = CheckWorldBounds(dir);
            return dir;
        }

        PushOWTile(collision);

        dir = Direction.None;
        // ORIGINAL: [$F8] := 0
        return Game.World.IsOverworld() ? CheckWorldBounds(dir) : dir;
    }

    // $01:A223
    private void PushOWTile(TileCollision collision )
    {
        if (TileOffset != 0 || Moving == 0) return;

        // This isn't anologous to the original's code, but the effect is the same.
        Game.World.PushTile(collision.FineRow, collision.FineCol);
    }
}
