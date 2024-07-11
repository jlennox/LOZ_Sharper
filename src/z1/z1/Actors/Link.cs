using SkiaSharp;

namespace z1.Actors;

internal enum PlayerState { Idle, Wielding, Paused }

internal class Link : Actor
{
    private static class Images
    {
        public static readonly SKBitmap WalkDown1 = Assets.Root.GetSKBitmap("link_walk_down1.png");
        public static readonly SKBitmap WalkDown2 = Assets.Root.GetSKBitmap("link_walk_down_2.png");
        public static readonly SKBitmap WalkUp1 = Assets.Root.GetSKBitmap("link_walk_up_1.png");
        public static readonly SKBitmap WalkUp2 = Assets.Root.GetSKBitmap("link_walk_up_2.png");
        public static readonly SKBitmap WalkVertical1 = Assets.Root.GetSKBitmap("link_walk_vertical_1.png");
        public static readonly SKBitmap WalkVertical2 = Assets.Root.GetSKBitmap("link_walk_vertical_2.png");

        // public static readonly SKBitmap[] WalkDown = [WalkDown1, WalkDown2];
        // public static readonly SKBitmap[] WalkUp = [WalkUp1, WalkUp2];
        // public static readonly SKBitmap[] WalkLeft = [WalkVertical1, WalkVertical2];
        // public static readonly SKBitmap[] WalkRight = [WalkVertical1.Mirror(), WalkVertical2.Mirror()];
    }

    public override bool IsPlayer => true;

    public const int WalkSpeed = 0x60;
    public const int StairsSpeed = 0x30;

    private int _walkFrame = 0;
    private int _state = 0;

    private byte _speed;
    private TileBehavior _tileBehavior;
    private bool _paralyzed;
    private byte _animTimer;
    private byte _avoidTurningWhenDiag;   // 56
    private byte _keepGoingStraight;      // 57
    // private InputButtons _curButtons;

    public SpriteAnimator Animator;

    public static Span<byte> PlayerLimits => new byte[] { 0xF0, 0x00, 0xDD, 0x3D };

    public Link(Game game) : base(game)
    {
        // TODO Animator = new(game, Palette.Red, Palette.Blue, Images.WalkLeft, Images.WalkDown, Images.WalkUp);
        Facing = Direction.Left; // TODO
    }

    public void DecInvincibleTimer()
    {
        if (InvincibilityTimer > 0 && (Game.GetFrameCounter() & 1) == 0)
            InvincibilityTimer--;
    }

    public PlayerState GetState()
    {
        if ((_state & 0xC0) == 0x40)
            return PlayerState.Paused;
        if ((_state & 0xF0) != 0)
            return PlayerState.Wielding;
        return PlayerState.Idle;
    }

    public Rectangle GetBounds() => new(X, Y + 8, 16, 8);

    public Point GetMiddle()
    {
        // JOE: This seems silly?
        return new (X + 8, Y + 8);
    }

    public void SetState(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Paused:
                _state = 0x40;
                break;
            case PlayerState.Idle:
                _state = 0;
                break;
        }
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

        var damage = collider.PlayerDamage;
        BeHarmed(collider, damage);
    }

    public void BeHarmed(Actor collider, int damage)
    {
        if (collider is not WhirlwindActor)
            Game.Sound.Play(SoundEffect.PlayerHit);

        var ringValue = Game.Profile.Items.GetValueOrDefault(ItemSlot.Ring, 0);

        damage >>= ringValue;

        Game.ResetKilledObjectCount();

        if (Game.Profile.Hearts <= damage)
        {
            Game.Profile.Hearts = 0;
            _state = 0;
            Facing = Direction.Down;
            Game.GotoDie();
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
            TileOffset = 0;
        MoveDirection(speed, dir);
    }


    //====================================================================================
    //  UseItem
    //====================================================================================

    public int UseCandle(int x, int y, Direction facingDir)
    {
        int itemValue = Game.GetItem(ItemSlot.Candle);
        if (itemValue == 1 && Game.World.candleUsed)
            return 0;

        Game.World.candleUsed = true;

        for (int i = (int)ObjectSlot.FirstFire; i < (int)ObjectSlot.LastFire; i++)
        {
            if (Game.GetObject((ObjectSlot)i) != null) continue;

            MoveSimple(ref x, ref y, facingDir, 0x10);

            Game.Sound.Play(SoundEffect.Fire);

            var fire = new FireActor(Game, x, y)
            {
                Moving = (byte)facingDir
            };
            Game.SetObject((ObjectSlot)i, fire);
            return 12;
        }
        return 0;
    }

    public void Move(Game game)
    {
        Direction? direction = game.KeyCode switch
        {
            Keys.Left => Direction.Left,
            Keys.Right => Direction.Right,
            Keys.Up => Direction.Up,
            Keys.Down => Direction.Down,
            _ => null
        };

        if (direction != null)
        {
            DoMove(direction.Value, 1);
        }
    }

    // $B366
    public void SetSpeed()
    {
        byte newSpeed = WalkSpeed;

        if (Game.IsOverworld)
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

    private void SetFacingAnimation()
    {
        int shieldState = Game.GetItem(ItemSlot.MagicShield) + 1;
        int dirOrd = Facing.GetOrdinal();
        // TODO: if ((_state & 0x30) == 0x10 || (_state & 0x30) == 0x20)
        // TODO:     animator.anim = Graphics::GetAnimation(Sheet_PlayerAndItems, thrustAnimMap[dirOrd]);
        // TODO: else
        // Animator.GetFrame( = Graphics::GetAnimation(Sheet_PlayerAndItems, animMap[shieldState][dirOrd]);
    }

    public override void Draw()
    {
        // var sprites = Dir switch
        // {
        //     Direction.Down => Images.WalkDown,
        //     Direction.Up => Images.WalkUp,
        //     Direction.Left => Images.WalkLeft,
        //     // Direction.Right => Images.WalkRight,
        //     _ => throw new InvalidOperationException("Invalid direction")
        // };
        //
        // var sprite = sprites[_walkFrame];
        // Game.DrawBitmap(TileSheet.PlayerAndItems, sprite, Position);
        var palette = CalcPalette(Palette.Player);
        int y = Y;

        if (Game.IsOverworld || Game.CurrentMode == GameMode.PlayCellar)
        {
            y += 2;
        }

        SetFacingAnimation();
        Animator.Draw(TileSheet.PlayerAndItems, X, y, palette);
        // var frame = Animator.GetFrame();
        // if (frame != null)
        // {
        //     Game.DrawBitmap(TileSheet.PlayerAndItems, frame, X, y);
        // }
    }

    public override void Update()
    {
    }

    public void DoMove(Direction dir, int amount)
    {
        Dir = dir;
        Position += new Size(dir switch
        {
            Direction.Left => -amount,
            Direction.Right => amount,
            _ => 0
        }, dir switch
        {
            Direction.Up => -amount,
            Direction.Down => amount,
            _ => 0
        });

        _walkFrame = (_walkFrame + 1) % 2;
    }
}
