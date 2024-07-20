using System.Runtime.InteropServices;
using z1.Actors;

namespace z1;

internal enum LadderStates { Unknown0, Unknown1, Unknown2 }

internal sealed class LadderActor : Actor
{
    public LadderStates state = LadderStates.Unknown1;
    public Direction origDir = Direction.Down; // JOE: NOTE: This is unused in the original?
    SpriteImage image;

    public LadderActor(Game game, int x, int y) : base(game, ObjType.Ladder, x, y)
    {
        Facing = Game.Link.Facing;
        Decoration = 0;

        image = new()
        {
            Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Ladder)
        };
    }

    public override void Update()
    {
    }

    public override void Draw()
    {
        image.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Player);
    }
}

internal enum BlockObjType
{
    Mob_Cave = 0x0C,
    Mob_Ground = 0x0E,
    Mob_Stairs = 0x12,
    Mob_Rock = 0x13,
    Mob_Headstone = 0x14,

    Mob_Block = 0,
    Mob_Tile = 1,
    Mob_UW_Stairs = 4,

    Tile_Rock = 0xC8,
    Tile_Headstone = 0xBC,
    Tile_Block = 0xB0,
    Tile_WallEdge = 0xF6,
}

internal abstract class BlockObjBase : Actor, IBlocksPlayer
{
    public int timer;
    public int targetPos;
    // public BlockSpec spec;
    public int origX;
    public int origY;

    Action? curUpdate;

    protected BlockObjBase(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
        Decoration = 0;
        curUpdate = UpdateIdle;
    }

    protected abstract byte BlockTile { get; }
    protected abstract BlockObjType BlockMob { get; }
    protected abstract BlockObjType FloorMob1 { get; }
    protected abstract BlockObjType FloorMob2 { get; }
    protected abstract int TimerLimit { get; }
    protected abstract bool AllowHorizontal { get; }

    public override void Draw()
    {
        if (curUpdate == UpdateMoving)
        {
            Graphics.DrawStripSprite16X16(TileSheet.Background, BlockTile, X, Y, Game.World.GetInnerPalette());
        }
    }

    public CollisionResponse CheckCollision()
    {
        if (curUpdate != UpdateMoving) return CollisionResponse.Unknown;

        var player = Game.Link;
        if (player == null) return CollisionResponse.Unknown;

        var playerX = player.X;
        var playerY = player.Y + 3;

        if (Math.Abs(playerX - X) < World.MobTileWidth && Math.Abs(playerY - Y) < World.MobTileHeight)
        {
            return CollisionResponse.Blocked;
        }

        return CollisionResponse.Unknown;
    }

    public override void Update()
    {
        var fun = curUpdate ?? throw new Exception("CurUpdate is null");
        fun();
    }

    private void UpdateIdle()
    {
        if (this is RockObj)
        {
            if (Game.World.GetItem(ItemSlot.Bracelet) == 0)
                return;
        }
        else if (this is BlockObj)
        {
            if (Game.World.HasLivingObjects())
                return;
        }

        var player = Game.Link;
        if (player == null)
            return;

        var bounds = player.GetBounds(); // JOE: IS this a bug that this is unused?
        var dir = player.MovingDirection;

        if (!AllowHorizontal && dir.IsHorizontal())
        {
            timer = 0;
            return;
        }

        var playerX = player.X;
        var playerY = player.Y + 3;

        var pushed = false;
        if (dir.IsVertical())
        {
            pushed = X == playerX && Math.Abs(Y - playerY) <= World.MobTileHeight;
        }
        else
        {
            pushed = Y == playerY && Math.Abs(X - playerX) <= World.MobTileWidth;
        }

        if (!pushed)
        {
            timer = 0;
            return;
        }

        timer++;
        if (timer == TimerLimit)
        {
            targetPos = dir switch
            {
                Direction.Right => X + World.MobTileWidth,
                Direction.Left => X - World.MobTileWidth,
                Direction.Down => Y + World.MobTileHeight,
                Direction.Up => Y - World.MobTileHeight,
                _ => targetPos
            };
            Game.World.SetMobXY(X, Y, FloorMob1);
            Facing = dir;
            origX = X;
            origY = Y;
            curUpdate = UpdateMoving;
        }
    }

    private void UpdateMoving()
    {
        var done = false;

        MoveDirection(0x20, Facing);

        if (Facing.IsHorizontal())
        {
            if (X == targetPos)
                done = true;
        }
        else
        {
            if (Y == targetPos)
                done = true;
        }

        if (done)
        {
            Game.World.OnPushedBlock();
            Game.World.SetMobXY(X, Y, BlockMob);
            Game.World.SetMobXY(origX, origY, FloorMob2);
            IsDeleted = true;
        }
    }
}

internal sealed class RockObj : BlockObjBase
{
    public RockObj(Game game, int x, int y) : base(game, ObjType.Rock, x, y) { }

    protected override byte BlockTile => (byte)BlockObjType.Tile_Rock;
    protected override BlockObjType BlockMob => BlockObjType.Mob_Rock;
    protected override BlockObjType FloorMob1 => BlockObjType.Mob_Ground;
    protected override BlockObjType FloorMob2 => BlockObjType.Mob_Ground;
    protected override int TimerLimit => 1;
    protected override bool AllowHorizontal => false;
}

internal sealed class HeadstoneObj : BlockObjBase
{
    public HeadstoneObj(Game game, int x, int y) : base(game, ObjType.Headstone, x, y) { }

    protected override byte BlockTile => (byte)BlockObjType.Tile_Headstone;
    protected override BlockObjType BlockMob => BlockObjType.Mob_Headstone;
    protected override BlockObjType FloorMob1 => BlockObjType.Mob_Ground;
    protected override BlockObjType FloorMob2 => BlockObjType.Mob_Stairs;
    protected override int TimerLimit => 1;
    protected override bool AllowHorizontal => false;
}

internal sealed class BlockObj : BlockObjBase
{
    public BlockObj(Game game, int x, int y) : base(game, ObjType.Block, x, y) { }

    protected override byte BlockTile => (byte)BlockObjType.Tile_Block;
    protected override BlockObjType BlockMob => (byte)BlockObjType.Mob_Block;
    protected override BlockObjType FloorMob1 => BlockObjType.Mob_Tile;
    protected override BlockObjType FloorMob2 => BlockObjType.Mob_Tile;
    protected override int TimerLimit => 17;
    protected override bool AllowHorizontal => true;
}

internal enum FireState { Moving, Standing }

internal class FireActor : Actor
{
    public FireState state
    {
        get => _state;
        set
        {
            _state = value;
            if (state == FireState.Standing) Game.World.BeginFadeIn();
        }
    }
    SpriteAnimator animator;

    private FireState _state;

    public FireActor(Game game, int x, int y, Direction facing) : base(game, ObjType.Fire, x, y)
    {
        _state = FireState.Moving;
        Facing = facing;

        animator = new()
        {
            Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Fire),
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
        if (state == FireState.Moving)
        {
            var origOffset = TileOffset;
            TileOffset = 0;
            MoveDirection(0x20, Facing);
            TileOffset += origOffset;

            if (Math.Abs(TileOffset) == 0x10)
            {
                ObjTimer = 0x3F;
                state = FireState.Standing;
                Game.World.BeginFadeIn();
            }
        }
        else
        {
            if (ObjTimer == 0)
            {
                IsDeleted = true;
                return;
            }
        }

        animator.Advance();

        CheckCollisionWithPlayer();
    }

    // F8D9
    private void CheckCollisionWithPlayer()
    {
        var player = Game.Link;

        if (player.InvincibilityTimer == 0)
        {
            var objCenter = GetMiddle();
            var playerCenter = player.GetMiddle();
            var box = new Point(0xE, 0xE);

            if (!DoObjectsCollide(objCenter, playerCenter, box, out var distance))
                return;

            // JOE: NOTE: This came out pretty different.
            var context = new CollisionContext(ObjectSlot.NoneFound, DamageType.Fire, 0, distance);

            Shove(context);
            player.BeHarmed(this, 0x0080);
        }
    }

    public override void Draw()
    {
        animator.Draw(TileSheet.PlayerAndItems, X, Y, Palette.RedFgPalette);
    }
}

internal sealed class TreeActor : Actor
{
    public TreeActor(Game game, int x, int y) : base(game, ObjType.Tree, x, y)
    {
        Decoration = 0;
    }

    public override void Update()
    {
        for (var i = (int)ObjectSlot.FirstFire; i < (int)ObjectSlot.LastFire; i++)
        {
            var gameObj = Game.World.GetObject((ObjectSlot)i);
            if (gameObj is not FireActor fire) continue;
            if (fire.IsDeleted || fire.state != FireState.Standing || fire.ObjTimer != 2) continue;

            // TODO: This is repeated a lot. Make generic.
            if (Math.Abs(fire.X - X) >= 16 || Math.Abs(fire.Y - Y) >= 16) continue;

            Game.World.SetMobXY(X, Y, BlockObjType.Mob_Stairs);
            Game.World.TakeSecret();
            Game.Sound.PlayEffect(SoundEffect.Secret);
            IsDeleted = true;
            return;
        }
    }

    public override void Draw() { }
}

internal enum BombState { Initing, Ticking, Blasting, Fading }

internal sealed class BombActor : Actor
{
    private const int Clouds = 4;
    private const int CloudFrames = 2;

    private static readonly Point[][] cloudPositions = new[]
    {
        new[] {
            new Point(0, 0 ),
            new Point(-13, 0 ),
            new Point(7, -13 ),
            new Point(-7, 14),
        },

        new[] {
            new Point(0, 0 ),
            new Point(13, 0 ),
            new Point(-7, -13 ),
            new Point(7, 14),
        }
    };

    private static readonly byte[] times = new byte[] { 0x30, 0x18, 0xC, 0 };

    public BombState BombState = BombState.Initing;

    SpriteAnimator animator;

    public BombActor(Game game, int x, int y) : base(game, ObjType.Bomb, x, y)
    {
        Facing = Game.Link.Facing;
        Decoration = 0;
        animator = new()
        {
            Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.BombItem),
            Time = 0,
            DurationFrames = 1
        };
    }

    public override void Update()
    {
        if (ObjTimer == 0)
        {
            ObjTimer = times[(int)BombState];
            BombState++;

            if (BombState == BombState.Blasting)
            {
                animator.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, AnimationId.Cloud);
                animator.Time = 0;
                animator.DurationFrames = animator.Animation.Length;
                Game.Sound.PlayEffect(SoundEffect.Bomb);
            }
            else if (BombState == BombState.Fading)
            {
                animator.AdvanceFrame();
            }
            else if (BombState > BombState.Fading)
            {
                IsDeleted = true;
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
            var offset = (16 - animator.Animation.Width) / 2;
            animator.Draw(TileSheet.PlayerAndItems, X + offset, Y, Palette.BlueFgPalette);
        }
        else
        {
            var positions = cloudPositions[ObjTimer % CloudFrames];

            for (var i = 0; i < Clouds; i++)
            {
                animator.Draw(
                    TileSheet.PlayerAndItems,
                    X + positions[i].X, Y + positions[i].Y,
                    Palette.BlueFgPalette);
            }
        }
    }
}

internal sealed class RockWallActor : Actor
{
    public RockWallActor(Game game, int x, int y) : base(game, ObjType.RockWall, x, y)
    {
        Decoration = 0;
    }

    public override void Update()
    {
        for (var i = (int)ObjectSlot.FirstBomb; i < (int)ObjectSlot.LastBomb; i++)
        {
            var gameObj = Game.World.GetObject((ObjectSlot)i);
            if (gameObj is not BombActor bomb) continue;
            if (bomb.IsDeleted || bomb.BombState != BombState.Blasting) continue;

            // TODO: This is repeated a lot. Make generic.
            if (Math.Abs(bomb.X - X) >= 16 || Math.Abs(bomb.Y - Y) >= 16) continue;

            Game.World.SetMobXY(X, Y, BlockObjType.Mob_Cave);
            Game.World.TakeSecret();
            Game.Sound.PlayEffect(SoundEffect.Secret);
            IsDeleted = true;
            return;
        }
    }

    public override void Draw() { }
}

internal sealed class PlayerSwordActor : Actor
{
    private const int DirCount = 4;
    private const int SwordStates = 5;
    private const int LastSwordState = SwordStates - 1;

    private static readonly Point[][] swordOffsets = new Point[][]
    {
        new[] { new Point(-8, -11), new Point(0, -11), new Point(1, -14), new Point(-1, -9) },
        new[] { new Point(11, 3), new Point(-11, 3), new Point(1, 13), new Point(-1, -10) },
        new[] { new Point(7, 3), new Point(-7, 3), new Point(1, 9), new Point(-1, -9) },
        new[] { new Point(3, 3), new Point(-3, 3), new Point(1, 5), new Point(-1, -1) }
    };

    public static readonly AnimationId[] swordAnimMap =
    {
        AnimationId.Sword_Right,
        AnimationId.Sword_Left,
        AnimationId.Sword_Down,
        AnimationId.Sword_Up
    };

    private static readonly AnimationId[] rodAnimMap =
    {
        AnimationId.Wand_Right,
        AnimationId.Wand_Left,
        AnimationId.Wand_Down,
        AnimationId.Wand_Up,
    };

    private static readonly byte[] swordStateDurations = new byte[] { 5, 8, 1, 1, 1 };

    public int state;
    private int timer;
    private SpriteImage image = new();

    public PlayerSwordActor(Game game, ObjType type) : base(game, type, 0, 0)
    {
        Put();
        timer = swordStateDurations[state];
        Decoration = 0;
    }

    int GetState()
    {
        return state;
    }

    void Put()
    {
        var player = Game.Link;
        var facingDir = player.Facing;
        X = player.X;
        Y = player.Y;

        var dirOrd = facingDir.GetOrdinal();
        var offset = swordOffsets[state][dirOrd];
        X += offset.X;
        Y += offset.Y;
        Facing = facingDir;

        var animMap = ObjType == ObjType.Rod ? rodAnimMap : swordAnimMap;

        var animIndex = animMap[dirOrd];
        image.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, animIndex);
    }

    void TryMakeWave()
    {
        if (state == 2)
        {
            var makeWave = false;
            var wave = Game.World.GetObject(ObjectSlot.PlayerSwordShot);

            if (ObjType == ObjType.Rod)
            {
                if (wave == null || wave.ObjType != ObjType.MagicWave)
                {
                    makeWave = true;
                    Game.Sound.PlayEffect(SoundEffect.MagicWave);
                }
            }
            else
            {
                if (wave == null)
                {
                    // The original game skips checking hearts, and shoots, if [$529] is set.
                    // But, I haven't found any code that sets it.

                    var profile = Game.World.GetProfile();
                    var neededHeartsValue = (profile.Items[ItemSlot.HeartContainers] << 8) - 0x80;

                    if (profile.Hearts >= neededHeartsValue)
                    {
                        makeWave = true;
                        Game.Sound.PlayEffect(SoundEffect.SwordWave);
                    }
                }
            }

            if (makeWave)
                MakeWave();
        }
    }

    void MakeWave()
    {
        var player = Game.Link;
        var x = player.X;
        var y = player.Y;
        var dir = player.Facing;

        MoveSimple(ref x, ref y, dir, 0x10);

        if (dir.IsVertical() || (x >= 0x14 && x < 0xEC))
        {
            var type = ObjType == ObjType.Rod ? ObjType.MagicWave : ObjType.PlayerSwordShot;
            var wave = GlobalFunctions.MakeProjectile(Game.World, type, x, y, dir, ObjectSlot.PlayerSwordShot);

            Game.World.SetObject(ObjectSlot.PlayerSwordShot, wave);
            wave.TileOffset = player.TileOffset;
        }
    }

    public override void Update()
    {
        timer--;

        if (timer == 0)
        {
            if (state == LastSwordState)
            {
                IsDeleted = true;
                return;
            }
            state++;
            timer = swordStateDurations[state];
            // The original game does this: player.animTimer := timer
            // But, we do it differently. The player handles all of its animation.
        }

        if (state < LastSwordState)
        {
            Put();
            TryMakeWave();
        }
    }

    public override void Draw()
    {
        if (state > 0 && state < LastSwordState)
        {
            var weaponValue = Game.World.GetItem(ItemSlot.Sword);
            var palette = (ObjType == ObjType.Rod) ? Palette.BlueFgPalette : (Palette.Player + weaponValue - 1);
            var xOffset = (16 - image.Animation.Width) / 2;
            image.Draw(TileSheet.PlayerAndItems, X + xOffset, Y, palette);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct CaveSpec
{
    public const int Count = 3;

    public byte Dweller;
    public byte StringId;
    public byte ItemA;
    public byte ItemB;
    public byte ItemC;
    public byte PriceA;
    public byte PriceB;
    public byte PriceC;

    public readonly ItemId GetItemId(int i) => i switch
    {
        0 => (ItemId)(ItemA & 0x3F),
        1 => (ItemId)(ItemB & 0x3F),
        2 => (ItemId)(ItemC & 0x3F),
        _ => throw new ArgumentOutOfRangeException(nameof(i)),
    };

    public readonly byte GetPrice(int i) => i switch
    {
        0 => PriceA,
        1 => PriceB,
        2 => PriceC,
        _ => throw new ArgumentOutOfRangeException(nameof(i)),
    };

    public ObjType DwellerType
    {
        get => (ObjType)Dweller;
        set => Dweller = (byte)value;
    }

    public readonly StringId GetStringId() => (StringId)(StringId & 0x3F);
    public readonly bool GetPay() => (StringId & 0x80) != 0;
    public readonly bool GetPickUp() => (StringId & 0x40) != 0;
    public readonly bool GetShowNegative() => ((int)ItemA & 0x80) != 0;
    public readonly bool GetCheckHearts() => ((int)ItemA & 0x40) != 0;
    public readonly bool GetSpecial() => ((int)ItemB & 0x80) != 0;
    public readonly bool GetHint() => ((int)ItemB & 0x40) != 0;
    public readonly bool GetShowPrices() => ((int)ItemC & 0x80) != 0;
    public readonly bool GetShowItems() => ((int)ItemC & 0x40) != 0;

    public void ClearPickUp() { unchecked { StringId &= (byte)~0x40; } }
    public void ClearShowPrices() { unchecked { ItemC &= (byte)~0x80; } }
    public void SetPickUp() { StringId |= 0x40; }
    public void SetShowNegative() { ItemA |= 0x80; }
    public void SetSpecial() { ItemB |= 0x80; }
    public void SetShowPrices() { ItemC |= 0x80; }
    public void SetShowItems() { ItemC |= 0x40; }
}

internal sealed class ItemObjActor : Actor
{
    private static readonly ObjectSlot[] weaponSlots = new[] { ObjectSlot.PlayerSword, ObjectSlot.Boomerang, ObjectSlot.Arrow };

    ItemId itemId;
    bool isRoomItem;
    int timer;

    public ItemObjActor(Game game, ItemId itemId, bool isRoomItem, int x, int y) : base(game, ObjType.Item, x, y)
    {
        Decoration = 0;

        this.itemId = itemId;
        this.isRoomItem = isRoomItem;

        if (!isRoomItem)
            timer = 0x1FF;
    }

    bool TouchesObject(Actor obj)
    {
        int distanceX = Math.Abs(obj.X + 0 - X);
        int distanceY = Math.Abs(obj.Y + 3 - Y);

        return distanceX <= 8
            && distanceY <= 8;
    }

    public override void Update()
    {
        if (!isRoomItem)
        {
            timer--;
            if (timer == 0)
            {
                IsDeleted = true;
                return;
            }
            else if (timer >= 0x1E0)
            {
                return;
            }
        }

        bool touchedItem = false;

        if (TouchesObject(Game.Link))
        {
            touchedItem = true;
        }
        else if (!isRoomItem)
        {

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                var slot = weaponSlots[i];
                var obj = Game.World.GetObject(slot);
                if (obj != null && !obj.IsDeleted && TouchesObject(obj))
                {
                    touchedItem = true;
                    break;
                }
            }
        }

        if (touchedItem)
        {
            if (isRoomItem)
                Game.World.MarkItem();

            IsDeleted = true;

            if (itemId == ItemId.PowerTriforce)
            {
                Game.World.OnTouchedPowerTriforce();
                Game.Sound.PlayEffect(SoundEffect.RoomItem);
            }
            else
            {
                Game.World.AddItem(itemId);

                if (itemId == ItemId.TriforcePiece)
                {
                    Game.World.EndLevel();
                }
                else if (Game.World.IsUWCellar())
                {
                    Game.World.LiftItem(itemId);
                    Game.Sound.PushSong(SongId.ItemLift);
                }
            }
        }
    }

    public override void Draw()
    {
        if (isRoomItem
            || timer < 0x1E0
            || (timer & 2) != 0)
        {
            GlobalFunctions.DrawItemWide(Game, itemId, X, Y);
        }
    }
}

internal sealed class WhirlwindActor : Actor
{
    private byte _prevRoomId;
    private readonly SpriteAnimator _animator;

    public WhirlwindActor(Game game, int x, int y) : base(game, ObjType.Whirlwind, x, y)
    {
        Facing = Direction.Right;

        _animator = new()
        {
            Animation = Graphics.GetAnimation(TileSheet.Npcs, AnimationId.OW_Whirlwind),
            DurationFrames = 2,
            Time = 0
        };
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

internal sealed class DockActor : Actor
{
    private int _state = 0;
    private SpriteImage _raftImage;

    public DockActor(Game game, int x, int y) : base(game, ObjType.Dock, x, y)
    {
        Decoration = 0;

        _raftImage = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.Raft);
    }

    public override void Update()
    {
        if (Game.World.GetItem(ItemSlot.Raft) == 0)
            return;

        var player = Game.Link;

        if (_state == 0)
        {
            int x;

            if (Game.World.curRoomId == 0x55)
                x = 0x80;
            else
                x = 0x60;

            if (x != player.X)
                return;

            X = x;

            if (player.Y == 0x3D)
                _state = 1;
            else if (player.Y == 0x7D)
                _state = 2;
            else
                return;

            Y = player.Y + 6;

            var facings = new[] { Direction.None, Direction.Down, Direction.Up };

            player.SetState(PlayerState.Paused);
            player.Facing = facings[_state];
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
        else if (_state == 1)
        {
            // $8FB0
            Y++;
            player.Y++;

            if (player.Y == 0x7F)
            {
                player.TileOffset = 2;
                player.SetState(PlayerState.Idle);
                _state = 0;
            }

            // Not exactly the same as the original, but close enough.
            player.Animator.Advance();
        }
        else if (_state == 2)
        {
            Y--;
            player.Y--;

            if (player.Y == 0x3D)
            {
                Game.World.LeaveRoom(player.Facing, Game.World.curRoomId);
                player.SetState(PlayerState.Idle);
                _state = 0;
            }

            // Not exactly the same as the original, but close enough.
            player.Animator.Advance();
        }
    }

    public override void Draw()
    {
        if (_state != 0)
        {
            _raftImage.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Player);
        }
    }
}