using System.Collections.Immutable;
using z1.Actors;
using z1.Render;

namespace z1;

internal enum LadderStates { Unknown0, Unknown1, Unknown2 }

internal sealed class LadderActor : Actor
{
    public LadderStates State = LadderStates.Unknown1;
    private readonly SpriteImage _image;

    public LadderActor(World world, int x, int y)
        : base(world, ObjType.Ladder, x, y)
    {
        Facing = Game.Player.Facing;
        Decoration = 0;

        _image = new SpriteImage(TileSheet.PlayerAndItems, AnimationId.Ladder);
    }

    public override void Update()
    {
    }

    public override void Draw()
    {
        _image.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Player, DrawOrder);
    }
}

internal interface IHasCollision
{
    CollisionResponse CheckCollision(Actor player);
}

internal class BlockActor : Actor, IHasCollision
{
    public bool EnableDraw { get; set; }

    protected readonly TileType Tile;

    private readonly int _width;
    private readonly int _height;
    private readonly TileSheet _tileSheet;
    private readonly TileType _tileType;

    public BlockActor(
        World world, ObjType type, TileType tile, int x, int y,
        int width = World.BlockWidth, int height = World.BlockHeight)
        : base(world, type, x, y)
    {
        Decoration = 0;
        Tile = tile;
        _width = width;
        _height = height;
        _tileSheet = World.CurrentWorld.GetBackgroundTileSheet();
    }

    public CollisionResponse CheckCollision(Actor player)
    {
        var playerX = player.X;
        var playerY = player.Y + 3;

        if (!IsMovingToward(player, player.Facing)) return CollisionResponse.Unknown;

        if (Math.Abs(playerX - X) < _width && Math.Abs(playerY - Y) < _height)
        {
            return CollisionResponse.Blocked;
        }

        return CollisionResponse.Unknown;
    }

    public override void Update() { }

    public override void Draw()
    {
        if (!EnableDraw) return;

        var palette = World.CurrentRoom.Settings.InnerPalette;
        Graphics.DrawStripSprite16X16(_tileSheet, Tile, X, Y, palette, DrawOrder);
    }
}

[Flags]
internal enum MovingBlockActorOptions
{
    None = 0,
    ReplaceWithBackground = 1 << 0,
}

internal class MovingBlockActor : BlockActor
{
    public bool HasFinishedMoving { get; private set; }

    public event Action<MovingBlockActor>? OnFinishedMoving;

    private readonly BlockType _block;
    private readonly Point _moveTo;
    private readonly MovingBlockActorOptions _options;
    private bool _triggerEvent;

    public MovingBlockActor(
        World world, ObjType type, BlockType block, Point moveTo, MovingBlockActorOptions options,
        int x, int y, int width = World.BlockWidth, int height = World.BlockHeight)
        : base(world, type, block.GetTileType(), x, y, width, height)
    {
        Decoration = 0;
        _block = block;
        _moveTo = moveTo;
        _options = options;
    }

    public override void Update()
    {
        MoveDirection(0x20, Facing);

        HasFinishedMoving = Facing.IsHorizontal()
            ? X == _moveTo.X
            : Y == _moveTo.Y;

        if (!_triggerEvent && HasFinishedMoving)
        {
            _triggerEvent = true;
            OnFinishedMoving?.Invoke(this);
            if (_options.HasFlag(MovingBlockActorOptions.ReplaceWithBackground))
            {
                ReplaceWithBackground();
            }
        }
    }

    public void ReplaceWithBackground()
    {
        World.SetMapObjectXY(X, Y, _block);
        Delete();
    }
}

internal enum FireState { Moving, Standing }

internal sealed class FireActor : Actor
{
    public FireState State
    {
        get => _state;
        set
        {
            _state = value;
            if (State == FireState.Standing) World.BeginFadeIn();
        }
    }

    private readonly SpriteAnimator _animator;

    private FireState _state;

    public FireActor(World world, Actor owner, int x, int y, Direction facing)
        : base(world, ObjType.Fire, x, y)
    {
        Owner = owner;
        State = FireState.Moving;
        Facing = facing;

        _animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.Fire)
        {
            Time = 0,
            DurationFrames = 8
        };

        Decoration = 0;
    }

    private Point GetMiddle()
    {
        return new Point(X + 8, Y + 8);
    }

    public override void Update()
    {
        if (State == FireState.Moving)
        {
            var origOffset = TileOffset;
            TileOffset = 0;
            MoveDirection(0x20, Facing);
            TileOffset += origOffset;

            if (Math.Abs(TileOffset) == 0x10)
            {
                ObjTimer = 0x3F;
                State = FireState.Standing;
                World.BeginFadeIn();
            }
        }
        else
        {
            if (ObjTimer == 0)
            {
                Delete();
                return;
            }
        }

        _animator.Advance();

        CheckCollisionWithPlayer();
    }

    // F8D9
    private void CheckCollisionWithPlayer()
    {
        var player = Game.Player;

        if (player.InvincibilityTimer == 0)
        {
            var objCenter = GetMiddle();
            var playerCenter = player.GetMiddle();
            var box = new Point(0xE, 0xE);

            if (!DoObjectsCollide(objCenter, playerCenter, box, out var distance))
            {
                return;
            }

            // JOE: NOTE: This came out pretty different.
            var context = new CollisionContext(null, DamageType.Fire, 0, distance);

            Shove(context);
            player.BeHarmed(this, 0x0080);
        }
    }

    public override void Draw()
    {
        _animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Red, DrawOrder);
    }
}

internal enum BombState { Initing, Ticking, Blasting, Fading }

internal sealed class BombActor : Actor
{
    private const int Clouds = 4;
    private const int CloudFrames = 2;

    private static readonly ImmutableArray<ImmutableArray<Point>> _cloudPositions = [
        [new Point(0, 0), new Point(-13, 0), new Point(7, -13), new Point(-7, 14)],
        [new Point(0, 0), new Point(13, 0), new Point(-7, -13), new Point(7, 14)]
    ];

    public BombState BombState = BombState.Initing;

    private readonly SpriteAnimator _animator;

    public BombActor(World world, Actor owner, int x, int y)
        : base(world, ObjType.Bomb, x, y)
    {
        Owner = owner;
        Facing = Game.Player.Facing;
        Decoration = 0;
        _animator = new SpriteAnimator(TileSheet.PlayerAndItems, AnimationId.BombItem)
        {
            Time = 0,
            DurationFrames = 1
        };
    }

    public override void Update()
    {
        ReadOnlySpan<byte> times = [0x30, 0x18, 0xC, 0];

        if (ObjTimer == 0)
        {
            ObjTimer = times[(int)BombState];
            BombState++;

            switch (BombState)
            {
                case BombState.Blasting:
                    _animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Cloud);
                    _animator.Time = 0;
                    _animator.DurationFrames = _animator.Animation.Length;
                    Game.Sound.PlayEffect(SoundEffect.Bomb);
                    break;

                case BombState.Fading:
                    _animator.AdvanceFrame();
                    break;

                case > BombState.Fading:
                    Delete();
                    ObjTimer = 0;
                    return;
            }
        }

        if (BombState == BombState.Blasting)
        {
            switch (ObjTimer)
            {
                case 0x16: Graphics.EnableGrayscale(); break;
                case 0x12: Graphics.DisableGrayscale(); break;
                case 0x11: Graphics.EnableGrayscale(); break;
                case 0x0D: Graphics.DisableGrayscale(); break;
            }
        }
    }

    public override void Draw()
    {
        if (BombState == BombState.Ticking)
        {
            var offset = (16 - _animator.Animation.Width) / 2;
            _animator.Draw(TileSheet.PlayerAndItems, X + offset, Y, Palette.Blue, DrawOrder);
            return;
        }

        var positions = _cloudPositions[ObjTimer % CloudFrames];

        for (var i = 0; i < Clouds; i++)
        {
            _animator.Draw(
                TileSheet.PlayerAndItems,
                X + positions[i].X, Y + positions[i].Y,
                Palette.Blue, DrawOrder);
        }
    }
}

internal sealed class RockWallActor : Actor
{
    public RockWallActor(World world, int x, int y)
        : base(world, ObjType.RockWall, x, y)
    {
        Decoration = 0;
    }

    public override void Update()
    {
        foreach (var bomb in World.GetObjects<BombActor>())
        {
            if (bomb.IsDeleted || bomb.BombState != BombState.Blasting) continue;

            // TODO: This is repeated a lot. Make generic.
            if (Math.Abs(bomb.X - X) >= 16 || Math.Abs(bomb.Y - Y) >= 16) continue;

            World.SetMapObjectXY(X, Y, BlockType.Cave);
            Game.Sound.PlayEffect(SoundEffect.Secret);
            World.Profile.Statistics.OWBlocksBombed++;
            Delete();
            return;
        }
    }

    public override void Draw() { }
}

internal sealed class PlayerSwordActor : Actor
{
    private const int SwordStates = 5;
    private const int LastSwordState = SwordStates - 1;

    private static readonly ImmutableArray<ImmutableArray<Point>> _swordOffsets = [
        [new Point(-8, -11), new Point(0, -11), new Point(1, -14), new Point(-1, -9)],
        [new Point(11, 3), new Point(-11, 3), new Point(1, 13), new Point(-1, -10)],
        [new Point(7, 3), new Point(-7, 3), new Point(1, 9), new Point(-1, -9)],
        [new Point(3, 3), new Point(-3, 3), new Point(1, 5), new Point(-1, -1)]
    ];

    public static readonly ImmutableArray<AnimationId> SwordAnimMap = [
        AnimationId.Sword_Right,
        AnimationId.Sword_Left,
        AnimationId.Sword_Down,
        AnimationId.Sword_Up
    ];

    private static readonly ImmutableArray<AnimationId> _rodAnimMap = [
        AnimationId.Wand_Right,
        AnimationId.Wand_Left,
        AnimationId.Wand_Down,
        AnimationId.Wand_Up
    ];

    private static readonly ImmutableArray<byte> _swordStateDurations = [5, 8, 1, 1, 1];

    public int State;
    private int _timer;
    private readonly SpriteImage _image = new();

    public PlayerSwordActor(World world, ObjType type, Player owner)
        : base(world, type)
    {
        if (type is not (ObjType.PlayerSword or ObjType.Rod))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Invalid type for {nameof(PlayerSwordActor)}.");
        }

        Put();
        _timer = _swordStateDurations[State];
        Owner = owner;
        Decoration = 0;
    }

    public static PlayerSwordActor MakeSword(World world, Player owner) => new(world, ObjType.PlayerSword, owner);
    public static PlayerSwordActor MakeRod(World world, Player owner) => new(world, ObjType.Rod, owner);

    private void Put()
    {
        var player = Game.Player;
        var facingDir = player.Facing;
        X = player.X;
        Y = player.Y;

        var dirOrd = facingDir.GetOrdinal();
        var offset = _swordOffsets[State][dirOrd];
        X += offset.X;
        Y += offset.Y;
        Facing = facingDir;

        var animMap = ObjType == ObjType.Rod ? _rodAnimMap : SwordAnimMap;
        var animIndex = animMap[dirOrd];
        _image.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, animIndex);
    }

    private void TryMakeWave()
    {
        if (State != 2) return;

        var makeWave = true;

        if (ObjType == ObjType.PlayerSword)
        {
            var profile = World.Profile;
            makeWave = profile.IsFullHealth();
        }

        if (makeWave)
        {
            MakeProjectile();
        }
    }

    private void MakeProjectile()
    {
        var x = Owner.X;
        var y = Owner.Y;
        var dir = Owner.Facing;

        MoveSimple(ref x, ref y, dir, 0x10);

        // Second check is to disallow shooting from doors?
        if (dir.IsVertical() || (x >= 0x14 && x < 0xEC))
        {
            var type = ObjType == ObjType.Rod ? ObjType.MagicWave : ObjType.PlayerSwordShot;
            var (count, allowed, effect) = type switch
            {
                ObjType.MagicWave => (
                    MagicWaveProjectile.PlayerCount(World),
                    World.GetItem(ItemSlot.MaxConcurrentProjectiles),
                    SoundEffect.MagicWave),
                ObjType.PlayerSwordShot => (
                    PlayerSwordProjectile.PlayerCount(World),
                    World.GetItem(ItemSlot.MaxConcurrentProjectiles),
                    SoundEffect.SwordWave),
                _ => throw new Exception(type.ToString())
            };

            if (count >= allowed) return;

            Game.Sound.PlayEffect(effect);

            var shot = Projectile.MakeProjectile(World, type, x, y, dir, Owner);
            World.AddObject(shot);
            shot.TileOffset = Owner.TileOffset;
        }
    }

    public override void Update()
    {
        _timer--;

        if (_timer == 0)
        {
            if (State == LastSwordState)
            {
                Delete();
                return;
            }
            State++;
            _timer = _swordStateDurations[State];
            // The original game does this: player.animTimer := timer
            // But, we do it differently. The player handles all of its animation.
        }

        if (State < LastSwordState)
        {
            Put();
            TryMakeWave();
        }
    }

    public override void Draw()
    {
        if (State is <= 0 or >= LastSwordState) return;

        var weaponValue = World.GetItem(ItemSlot.Sword);
        var palette = ObjType == ObjType.Rod ? Palette.Blue : (Palette.Player + weaponValue - 1);
        var xOffset = (16 - _image.Animation.Width) / 2;
        _image.Draw(TileSheet.PlayerAndItems, X + xOffset, Y, palette, DrawOrder);
    }
}

internal sealed class ItemObjActor : Actor
{
    // How long items are on the floor before they disappear.
    private const int DespawnTime = 0x1FF;
    // How long the item blinks when it first appears.
    private const int SpawnInTime = 0x1F;
    private const int SpawnInTimeLastTimer = 0x1FF - SpawnInTime;

    public bool TouchEnabled { get; set; } = true;

    private readonly ItemId _itemId;
    private readonly ItemObjectOptions _options;
    private int _timer;

    // Can return false to prevent the item from being picked up. IE, if the player couldn't afford it in a shop.
    public event Func<ItemObjActor, bool>? OnTouched;

    private readonly ObjectState? _state;

    public ItemObjActor(World world, ItemId itemId, ItemObjectOptions options, int x, int y)
        : this(world, null, itemId, options, x, y)
    {
    }

    public ItemObjActor(World world, string? name, ItemId itemId, ItemObjectOptions options, int x, int y)
        : base(world, ObjType.Item, x, y)
    {
        Decoration = 0;

        _itemId = itemId;
        _options = options;

        if (!options.HasFlag(ItemObjectOptions.IsRoomItem))
        {
            _timer = DespawnTime;
        }

        if (options.HasFlag(ItemObjectOptions.Persisted))
        {
            if (name == null) throw new ArgumentNullException(nameof(name), "Object decleared as persisted but requires a name argument.");

            _state = world.Player.Profile.GetObjectFlags(world.CurrentRoom, name);
            _state.ItemId = itemId;
            if (_state.ItemGot) Delete();
        }
    }

    private bool TouchesObject(Actor obj)
    {
        if (!TouchEnabled) return false;

        var distanceX = Math.Abs(obj.X + 0 - X);
        var distanceY = Math.Abs(obj.Y + 3 - Y);

        return distanceX <= 8
            && distanceY <= 8;
    }

    private void OptionalDelete()
    {
        if (_options.HasFlag(ItemObjectOptions.BecomesInactive))
        {
            TouchEnabled = false;
            return;
        }

        Delete();
    }

    private bool IsItemTouched()
    {
        if (TouchesObject(Game.Player))
        {
            return true;
        }

        if (!_options.HasFlag(ItemObjectOptions.IsRoomItem))
        {
            var weapons = World.GetObjects(static t => t is PlayerSwordActor or BoomerangProjectile or ArrowProjectile);
            foreach (var obj in weapons)
            {
                if (!obj.IsDeleted && TouchesObject(obj))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public override void Update()
    {
        if (!_options.HasFlag(ItemObjectOptions.IsRoomItem))
        {
            _timer--;
            switch (_timer)
            {
                // Pickup time has run out.
                case 0:
                    Delete();
                    return;

                // It's still in the "blinking in" phase.
                case >= SpawnInTimeLastTimer:
                    return;
            }
        }

        if (!IsItemTouched()) return;
        if (OnTouched?.Invoke(this) == false) return;

        OptionalDelete();

        if (_options.HasFlag(ItemObjectOptions.Persisted))
        {
            var state = _state ?? throw new Exception();
            state.ItemGot = true;
        }

        if (_itemId == ItemId.PowerTriforce)
        {
            World.OnTouchedPowerTriforce();
            Game.Sound.PlayEffect(SoundEffect.RoomItem);
            return;
        }

        World.AddItem(_itemId);

        if (_itemId == ItemId.TriforcePiece)
        {
            World.GotoEndLevel();
        }
        else if (_options.HasFlag(ItemObjectOptions.LiftOverhead))
        {
            World.LiftItem(_itemId);
            Game.Sound.PushSong(SongId.ItemLift);
        }

        // Auto-save if it's an underworld item (IE, triforce or bow)
        if (!World.IsOverworld())
        {
            Game.AutoSave();
        }
    }

    public override void Draw()
    {
        if (_options.HasFlag(ItemObjectOptions.IsRoomItem) || _timer < SpawnInTimeLastTimer || (_timer & 2) != 0)
        {
            GlobalFunctions.DrawItemWide(Game, _itemId, X, Y);
        }
    }
}

internal sealed class WhirlwindActor : Actor
{
    private GameRoom _prevRoom;
    private readonly SpriteAnimator _animator;

    public WhirlwindActor(World world, int x, int y)
        : base(world, ObjType.Whirlwind, x, y)
    {
        Facing = Direction.Right;

        _animator = new SpriteAnimator(TileSheet.NpcsOverworld, AnimationId.OW_Whirlwind)
        {
            DurationFrames = 2,
            Time = 0
        };
    }

    public void SetTeleportPrevRoomId(GameRoom room)
    {
        _prevRoom = room;
    }

    public override void Update()
    {
        X += 2;

        var player = Game.Player;

        if (player.GetState() != PlayerState.Paused || World.WhirlwindTeleporting == 0)
        {
            var thisMiddle = new Point(X + 8, Y + 5);
            var playerMiddle = player.GetMiddle();

            if (Math.Abs(thisMiddle.X - playerMiddle.X) < 14
                && Math.Abs(thisMiddle.Y - playerMiddle.Y) < 14)
            {
                player.Facing = Direction.Right;
                player.Stop();
                player.SetState(PlayerState.Paused);
                World.WhirlwindTeleporting = 1;

                player.Y = 0xF8;
            }
        }
        else
        {
            player.X = X;

            if (World.WhirlwindTeleporting == 2 && X == 0x80)
            {
                player.SetState(PlayerState.Idle);
                player.Y = Y;
                World.WhirlwindTeleporting = 0;
                Delete();
            }
        }

        if (X >= 0xF0)
        {
            Delete();
            if (World.WhirlwindTeleporting != 0)
            {
                World.LeaveRoom(Direction.Right, _prevRoom);
            }
        }

        _animator.Advance();
    }

    public override void Draw()
    {
        var pal = Palette.Player + (Game.FrameCounter & 3);
        _animator.Draw(TileSheet.NpcsOverworld, X, Y, pal, DrawOrder);
    }
}
