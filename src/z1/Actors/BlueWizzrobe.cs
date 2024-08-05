using System.Diagnostics;

namespace z1.Actors;

internal abstract class BlueWizzrobeBase : WizzrobeBase
{
    public static readonly AnimationId[] WizzrobeAnimMap = {
        AnimationId.UW_Wizzrobe_Right,
        AnimationId.UW_Wizzrobe_Left,
        AnimationId.UW_Wizzrobe_Right,
        AnimationId.UW_Wizzrobe_Up
    };

    protected byte FlashTimer;
    protected byte TurnTimer;

    protected BlueWizzrobeBase(Game game, ObjType type, int x, int y)
        : base(game, type, x, y)
    {
        Decoration = 0;
    }

    private void TruncatePosition()
    {
        X = (X + 8) & 0xF0;
        Y = (Y + 8) & 0xF0;
        Y -= 3;
    }

    protected void MoveOrTeleport()
    {
        if (ObjTimer != 0)
        {
            if (ObjTimer >= 0x10)
            {
                if ((Game.GetFrameCounter() & 1) == 1)
                {
                    TurnIfNeeded();
                }
                else
                {
                    TurnTimer++;
                    TurnIfNeeded();
                    MoveAndCollide();
                }
            }
            else if (ObjTimer == 1)
            {
                TryTeleporting();
            }

            return;
        }

        if (FlashTimer == 0)
        {
            var r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);
            TruncatePosition();
            Turn();
            return;
        }

        FlashTimer--;
        MoveAndCollide();
    }

    protected void MoveAndCollide()
    {
        Move();

        var collisionResult = CheckWizzrobeTileCollision(X, Y, Facing);

        if (collisionResult == 1)
        {
            if (Facing.IsVertical()) Facing ^= Direction.VerticalMask;
            if (Facing.IsHorizontal()) Facing ^= Direction.HorizontalMask;

            Move();
        }
        else if (collisionResult == 2)
        {
            if (FlashTimer == 0)
            {
                FlashTimer = 0x20;
                TurnTimer ^= 0x40;
                ObjTimer = 0;
                TruncatePosition();
            }
        }
    }

    private void Move()
    {
        ReadOnlySpan<int> blueWizzrobeXSpeeds = [0, 1, -1, 0, 0, 1, -1, 0, 0, 1, -1];
        ReadOnlySpan<int> blueWizzrobeYSpeeds = [0, 0, 0, 0, 1, 1, 1, 0, -1, -1, -1];

        X += blueWizzrobeXSpeeds[(int)Facing];
        Y += blueWizzrobeYSpeeds[(int)Facing];
    }

    protected void TryShooting()
    {
        if (Game.World.GetItem(ItemSlot.Clock) != 0) return;
        if (FlashTimer != 0) return;
        if ((Game.GetFrameCounter() % 0x20) != 0) return;

        var player = Game.Link;
        Direction dir;

        if ((player.Y & 0xF0) != (Y & 0xF0))
        {
            if (player.X != (X & 0xF0)) return;
            dir = GetYDirToTruePlayer(Y);
        }
        else
        {
            dir = GetXDirToTruePlayer(X);
        }

        if (dir != Facing) return;

        Game.Sound.PlayEffect(SoundEffect.MagicWave);
        Shoot(ObjType.MagicWave, X, Y, Facing);
    }

    protected void TurnIfNeeded()
    {
        if ((TurnTimer & 0x3F) == 0)
        {
            Turn();
        }
    }

    private void Turn()
    {
        var dir = (TurnTimer & 0x40) != 0
            ? GetYDirToTruePlayer(Y)
            : GetXDirToTruePlayer(X);

        if (dir == Facing) return;

        Facing = dir;
        TruncatePosition();
    }

    private static readonly int[] _blueWizzrobeTeleportXOffsets = { -0x20, 0x20, -0x20, 0x20 };
    private static readonly int[] _blueWizzrobeTeleportYOffsets = { -0x20, -0x20, 0x20, 0x20 };
    private static readonly int[] _blueWizzrobeTeleportDirs = { 0xA, 9, 6, 5 };

    private void TryTeleporting()
    {
        var index = Random.Shared.Next(4);

        var teleportX = X + _blueWizzrobeTeleportXOffsets[index];
        var teleportY = Y + _blueWizzrobeTeleportYOffsets[index];
        var dir = (Direction)_blueWizzrobeTeleportDirs[index];

        var collisionResult = CheckWizzrobeTileCollision(teleportX, teleportY, dir);

        if (collisionResult != 0)
        {
            var r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);
        }
        else
        {
            Facing = dir;

            FlashTimer = 0x20;
            TurnTimer ^= 0x40;
            ObjTimer = 0;
        }

        TruncatePosition();
    }
}

internal abstract class WizzrobeBase : Actor
{
    protected WizzrobeBase(Game game, ObjType type, int x, int y)
        : base(game, type, x, y) { }

    protected int CheckWizzrobeTileCollision(int x, int y, Direction dir)
    {
        ReadOnlySpan<int> allWizzrobeCollisionXOffsets = [0xF, 0, 0, 4, 8, 0, 0, 4, 8, 0];
        ReadOnlySpan<int> allWizzrobeCollisionYOffsets = [4, 4, 0, 8, 8, 8, 0, -8, 0, 0];

        // JOE: TODO: Research and fix this hacky workaround.
        if (dir == Direction.None) return 0;

        // JOE: TODO: This can crash.
        var ord = dir - 1;
        x += allWizzrobeCollisionXOffsets[(int)ord];
        y += allWizzrobeCollisionYOffsets[(int)ord];

        var collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides) return 0;

        // This isn't quite the same as the original game, because the original contrasted
        // blocks and water together with everything else.
        return World.CollidesWall(collision.TileBehavior) ? 1 : 2;
    }
}

internal sealed class BlueWizzrobeActor : BlueWizzrobeBase
{
    private readonly SpriteAnimator _animator;

    public BlueWizzrobeActor(Game game, int x, int y)
        : base(game, ObjType.BlueWizzrobe, x, y)
    {
        Debug.WriteLine($"BLUEWIZZ.ctor {game.World.CurObjectSlot} ObjTimer: {ObjTimer}");
        _animator = new SpriteAnimator
        {
            DurationFrames = 16,
            Time = 0
        };
    }

    public override void Update()
    {
        Debug.WriteLine($"BLUEWIZZ.update {Game.World.CurObjectSlot} ObjTimer: {ObjTimer}");
        if (Game.World.GetItem(ItemSlot.Clock) != 0)
        {
            AnimateAndCheckCollisions();
            return;
        }

        var origFacing = Facing; // JOE: TODO: IS this intentionally unused?

        MoveOrTeleport();
        TryShooting();

        if ((FlashTimer & 1) == 0)
        {
            AnimateAndCheckCollisions();
        }

        SetFacingAnimation();
    }

    public override void Draw()
    {
        if ((FlashTimer & 1) == 0 && Facing != Direction.None)
        {
            var pal = CalcPalette(Palette.Blue);
            _animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    private void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        _animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, WizzrobeAnimMap[dirOrd]);
    }

    private void AnimateAndCheckCollisions()
    {
        _animator.Advance();

        // If I really wanted, I could make a friend function or class to do this, which is the same
        // as in RedWizzrobe.

        InvincibilityMask = 0xF6;
        if (InvincibilityTimer == 0)
        {
            CheckWave(ObjectSlot.PlayerSwordShot);
            CheckBombAndFire(ObjectSlot.Bomb);
            CheckBombAndFire(ObjectSlot.Bomb2);
            CheckBombAndFire(ObjectSlot.Fire);
            CheckBombAndFire(ObjectSlot.Fire2);
            CheckSword(ObjectSlot.PlayerSword);
        }
        CheckPlayerCollision();
    }
}
