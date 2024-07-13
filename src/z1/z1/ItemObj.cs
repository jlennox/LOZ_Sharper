using System.Runtime.InteropServices;
using z1.Actors;

namespace z1;

internal sealed class LadderActor : TODOActor
{
    public int state;
    public Direction origDir;
    // TODO SpriteImage image;

    public LadderActor(Game game, int x = 0, int y = 0) : base(game, x, y) { }
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

internal sealed class RockObj : BlockObjBase
{
    public RockObj(Game game, int x = 0, int y = 0) : base(game, x, y) { }

    protected override byte BlockTile => (byte)BlockObjType.Tile_Rock;
    protected override BlockObjType BlockMob => (BlockObjType)BlockObjType.Mob_Rock;
    protected override BlockObjType FloorMob1 => (BlockObjType)BlockObjType.Mob_Ground;
    protected override BlockObjType FloorMob2 => (BlockObjType)BlockObjType.Mob_Ground;
    protected override int TimerLimit => 1;
    protected override bool AllowHorizontal => false;

    public override void Draw()
    {
        throw new NotImplementedException();
    }
}

internal sealed class HeadstoneObj : BlockObjBase
{
    public HeadstoneObj(Game game, int x = 0, int y = 0) : base(game, x, y) { }

    protected override byte BlockTile => (byte)BlockObjType.Tile_Headstone;
    protected override BlockObjType BlockMob => (BlockObjType)BlockObjType.Mob_Headstone;
    protected override BlockObjType FloorMob1 => (BlockObjType)BlockObjType.Mob_Ground;
    protected override BlockObjType FloorMob2 => (BlockObjType)BlockObjType.Mob_Stairs;
    protected override int TimerLimit => 1;
    protected override bool AllowHorizontal => false;
}

internal sealed class BlockObj : BlockObjBase
{
    public BlockObj(Game game, int x = 0, int y = 0) : base(game, x, y) { }

    protected override byte BlockTile => (byte)BlockObjType.Tile_Block;
    protected override BlockObjType BlockMob => (byte)BlockObjType.Mob_Block;
    protected override BlockObjType FloorMob1 => (BlockObjType)BlockObjType.Mob_Tile;
    protected override BlockObjType FloorMob2 => (BlockObjType)BlockObjType.Mob_Tile;
    protected override int TimerLimit => 17;
    protected override bool AllowHorizontal => true;
}

internal abstract class BlockObjBase : Actor, IBlocksPlayer
{
    public int timer;
    public int targetPos;
    // public BlockSpec spec;
    public int origX;
    public int origY;

    Action? CurUpdate;

    protected BlockObjBase(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }

    protected abstract byte BlockTile { get; }
    protected abstract BlockObjType BlockMob { get; }
    protected abstract BlockObjType FloorMob1 { get; }
    protected abstract BlockObjType FloorMob2 { get; }
    protected abstract int TimerLimit { get; }
    protected abstract bool AllowHorizontal { get; }

    public override void Draw()
    {
        throw new NotImplementedException();
    }

    public CollisionResponse CheckCollision()
    {
        if (CurUpdate != UpdateMoving) return CollisionResponse.Unknown;

        var player = Game.Link;
        if (player == null) return CollisionResponse.Unknown;

        int playerX = player.X;
        int playerY = player.Y + 3;

        if (Math.Abs(playerX - X) < World.MobTileWidth && Math.Abs(playerY - Y) < World.MobTileHeight)
        {
            return CollisionResponse.Blocked;
        }

        return CollisionResponse.Unknown;
    }

    public override void Update()
    {
        var fun = CurUpdate ?? throw new Exception("CurUpdate is null");
        fun();
    }

    private void UpdateIdle()
    {
        if (this is RockObj)
        {
            if (Game.GetItem(ItemSlot.Bracelet) == 0)
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
        var pushed = false;

        if (!AllowHorizontal && (dir == Direction.Left || dir == Direction.Right))
            dir = Direction.None;

        if (dir != Direction.None)
        {
            var playerX = player.X;
            var playerY = player.Y + 3;

            if (dir.IsVertical())
            {
                if (X == playerX && Math.Abs(Y - playerY) <= World.MobTileHeight)
                    pushed = true;
            }
            else
            {
                if (Y == playerY && Math.Abs(X - playerX) <= World.MobTileWidth)
                    pushed = true;
            }
        }

        if (pushed)
        {
            timer++;
            if (timer == TimerLimit)
            {
                switch (dir)
                {
                    case Direction.Right: targetPos = X + World.MobTileWidth; break;
                    case Direction.Left: targetPos = X - World.MobTileWidth; break;
                    case Direction.Down: targetPos = Y + World.MobTileHeight; break;
                    case Direction.Up: targetPos = Y - World.MobTileHeight; break;
                }
                Game.World.SetMobXY(X, Y, FloorMob1);
                Facing = dir;
                origX = X;
                origY = Y;
                CurUpdate = UpdateMoving;
            }
        }
        else
        {
            timer = 0;
        }
    }

    private void UpdateMoving()
    {
        bool done = false;

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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct CaveSpec
{
    public const int Count = 3;

    public byte Dweller;
    public byte StringId;
    public string String;
    public byte ItemA;
    public byte ItemB;
    public byte ItemC;
    public byte PriceA;
    public byte PriceB;
    public byte PriceC;

    public readonly ItemId GetItemId(int i) => i switch
    {
        0 => (ItemId)ItemA,
        1 => (ItemId)ItemB,
        2 => (ItemId)ItemC,
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

internal sealed class TextBox
{
    public const int StartX = 0x20;
    public const int StartY = 0x68;
    public const int CharDelay = 6;

    private int _left = StartX;
    private int _top = StartY;
    private int _height = 8;
    private readonly int _charDelay;
    private int _charTimer = 0;
    private bool _drawingDialog = true;
    private readonly Game _game;
    private string _text;
    public int _currentIndex = 0;
    private byte[] startCharPtr;
    private int curCharIndex;

    public TextBox(Game game, byte[] text, int delay = CharDelay) {
        _game = game;
        startCharPtr = text;
        _charDelay = delay;
    }

    public void Reset(byte[] text)
    {
        _drawingDialog = true;
        _charTimer = 0;
        startCharPtr = text;
        _currentIndex = 0;
    }

    public bool IsDone() => !_drawingDialog;
    public int GetHeight() => _height;
    public int GetX() => _left;
    public int GetY() => _top;
    public void SetX(int x) => _left = x;
    public void SetY(int y) => _top = y;

    public void Update()
    {
        if (!_drawingDialog)
            return;

        if (_charTimer == 0)
        {
            byte attr;
            byte ch;

            do
            {
                var curCharPtr = startCharPtr[curCharIndex];
                ch = (byte)(curCharPtr & 0x3F);
                attr = (byte)(curCharPtr & 0xC0);
                if (attr == 0xC0)
                    _drawingDialog = false;
                else if (attr != 0)
                    _height += 8;

                curCharIndex++;
                if (ch != (int)Char.JustSpace)
                    _game.Sound.Play(SoundEffect.Character);
            } while (_drawingDialog && ch == (int)Char.JustSpace);
            _charTimer = _charDelay - 1;
        }
        else
        {
            _charTimer--;
        }
    }

    public void Draw()
    {
        int x = _left;
        int y = _top;

        for (var i = 0; i < curCharIndex; i++ )
        {
            var chr = startCharPtr[i];
            var attr = chr & 0xC0;
            var ch = chr & 0x3F;

            if (ch != (int)Char.JustSpace)
                GlobalFunctions.DrawChar((byte)ch, x, y, 0);

            if (attr == 0)
            {
                x += 8;
            }
            else
            {
                x = StartX;
                y += 8;
            }
        }
    }
}

internal sealed class DockActor : Actor
{
    private int _state = 0;
    private SpriteImage _raftImage;

    public DockActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
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
            Game.Sound.Play(SoundEffect.Secret);
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


internal sealed class TreeActor : Actor
{
    public TreeActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }

    public override void Update()
    {
        for (var i = (int)ObjectSlot.FirstFire; i < (int)ObjectSlot.LastFire; i++)
        {
            var gameObj = Game.GetObject((ObjectSlot)i);
            if (gameObj is not FireActor fire) continue;
            if (fire.IsDeleted || fire.FireState != FireState.Standing || fire.ObjTimer != 2) continue;

            // TODO: This is repeated a lot. Make generic.
            if (Math.Abs(fire.X - X) >= 16 || Math.Abs(fire.Y - Y) >= 16) continue;

            Game.World.SetMobXY(X, Y, BlockObjType.Mob_Stairs);
            Game.World.TakeSecret();
            Game.Sound.Play(SoundEffect.Secret);
            IsDeleted = true;
            return;
        }
    }

    public override void Draw() { }
}

internal sealed class RockWallActor : TODOActor
{
    public RockWallActor(Game game, int x = 0, int y = 0) : base(game, x, y)
    {
    }

    public override void Update()
    {
        for (var i = (int)ObjectSlot.FirstBomb; i < (int)ObjectSlot.LastBomb; i++)
        {
            var gameObj = Game.GetObject((ObjectSlot)i);
            if (gameObj is not BombActor bomb) continue;
            if (bomb.IsDeleted || bomb.BombState != BombState.Blasting) continue;

            // TODO: This is repeated a lot. Make generic.
            if (Math.Abs(bomb.X - X) >= 16 || Math.Abs(bomb.Y - Y) >= 16) continue;

            Game.World.SetMobXY(X, Y, BlockObjType.Mob_Cave);
            Game.World.TakeSecret();
            Game.Sound.Play(SoundEffect.Secret);
            IsDeleted = true;
            return;
        }
    }
}