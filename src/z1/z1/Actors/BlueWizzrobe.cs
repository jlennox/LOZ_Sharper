using System.Diagnostics;

namespace z1.Actors;

internal abstract class BlueWizzrobeBase : WizzrobeBase
{
    public static readonly AnimationId[] wizzrobeAnimMap = {
        AnimationId.UW_Wizzrobe_Right,
        AnimationId.UW_Wizzrobe_Left,
        AnimationId.UW_Wizzrobe_Right,
        AnimationId.UW_Wizzrobe_Up
    };

    private static readonly int[] blueWizzrobeXSpeeds = { 0, 1, -1, 0, 0, 1, -1, 0, 0, 1, -1 };
    private static readonly int[] blueWizzrobeYSpeeds = { 0, 0, 0, 0, 1, 1, 1, 0, -1, -1, -1 };

    protected byte flashTimer;
    protected byte turnTimer;

    protected BlueWizzrobeBase(Game game, ObjType type, int x, int y) : base(game, type, x, y)
    {
        Decoration = 0;
    }

    void TruncatePosition()
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
                    turnTimer++;
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

        if (flashTimer == 0)
        {
            var r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);
            TruncatePosition();
            Turn();
            return;
        }

        flashTimer--;
        MoveAndCollide();
    }

    protected void MoveAndCollide()
    {
        Move();

        var collisionResult = CheckWizzrobeTileCollision(X, Y, Facing);

        if (collisionResult == 1)
        {
            if (Facing.IsVertical())
                Facing ^= Direction.VerticalMask;
            if (Facing.IsHorizontal())
                Facing ^= Direction.HorizontalMask;

            Move();
        }
        else if (collisionResult == 2)
        {
            if (flashTimer == 0)
            {
                flashTimer = 0x20;
                turnTimer ^= 0x40;
                ObjTimer = 0;
                TruncatePosition();
            }
        }
    }

    void Move()
    {
        X += blueWizzrobeXSpeeds[(int)Facing];
        Y += blueWizzrobeYSpeeds[(int)Facing];
    }

    protected void TryShooting()
    {
        if (Game.World.GetItem(ItemSlot.Clock) != 0)
            return;
        if (flashTimer != 0)
            return;
        if ((Game.GetFrameCounter() % 0x20) != 0)
            return;

        var player = Game.Link;
        Direction dir;

        if ((player.Y & 0xF0) != (Y & 0xF0))
        {
            if (player.X != (X & 0xF0))
                return;

            dir = GetYDirToTruePlayer(Y);
        }
        else
        {
            dir = GetXDirToTruePlayer(X);
        }

        if (dir != Facing)
            return;

        Game.Sound.PlayEffect(SoundEffect.MagicWave);
        Shoot(ObjType.MagicWave, X, Y, Facing);
    }

    protected void TurnIfNeeded()
    {
        if ((turnTimer & 0x3F) == 0)
            Turn();
    }

    void Turn()
    {
        var dir = (turnTimer & 0x40) != 0
            ? GetYDirToTruePlayer(Y)
            : GetXDirToTruePlayer(X);

        if (dir == Facing)
            return;

        Facing = dir;
        TruncatePosition();
    }

    private static readonly int[] blueWizzrobeTeleportXOffsets = { -0x20, 0x20, -0x20, 0x20 };
    private static readonly int[] blueWizzrobeTeleportYOffsets = { -0x20, -0x20, 0x20, 0x20 };
    private static readonly int[] blueWizzrobeTeleportDirs = { 0xA, 9, 6, 5 };

    void TryTeleporting()
    {
        var index = Random.Shared.Next(4);

        var teleportX = X + blueWizzrobeTeleportXOffsets[index];
        var teleportY = Y + blueWizzrobeTeleportYOffsets[index];
        var dir = (Direction)blueWizzrobeTeleportDirs[index];

        var collisionResult = CheckWizzrobeTileCollision(teleportX, teleportY, dir);

        if (collisionResult != 0)
        {
            var r = Random.Shared.GetByte();
            ObjTimer = (byte)(r | 0x70);
        }
        else
        {
            Facing = dir;

            flashTimer = 0x20;
            turnTimer ^= 0x40;
            ObjTimer = 0;
        }

        TruncatePosition();
    }
}

internal abstract class WizzrobeBase : Actor
{
    private static readonly int[] allWizzrobeCollisionXOffsets = { 0xF, 0, 0, 4, 8, 0, 0, 4, 8, 0 };
    private static readonly int[] allWizzrobeCollisionYOffsets = { 4, 4, 0, 8, 8, 8, 0, -8, 0, 0 };

    protected int CheckWizzrobeTileCollision(int x, int y, Direction dir)
    {
        // JOE: TODO: This can crash.
        var ord = dir - 1;
        x += allWizzrobeCollisionXOffsets[(int)ord];
        y += allWizzrobeCollisionYOffsets[(int)ord];

        var collision = Game.World.CollidesWithTileStill(x, y);
        if (!collision.Collides)
            return 0;

        // This isn't quite the same as the original game, because the original contrasted
        // blocks and water together with everything else.
        return World.CollidesWall(collision.TileBehavior) ? 1 : 2;
    }

    protected WizzrobeBase(Game game, ObjType type, int x, int y) : base(game, type, x, y) { }
}

internal sealed class BlueWizzrobeActor : BlueWizzrobeBase
{
    readonly SpriteAnimator Animator;

    public BlueWizzrobeActor(Game game, int x, int y) : base(game, ObjType.BlueWizzrobe, x, y)
    {
        Debug.WriteLine($"BLUEWIZZ.ctor {game.World.CurObjectSlot} ObjTimer: {ObjTimer}");
        Animator = new SpriteAnimator
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

        if ((flashTimer & 1) == 0)
        {
            AnimateAndCheckCollisions();
        }

        SetFacingAnimation();
    }

    public override void Draw()
    {
        if ((flashTimer & 1) == 0 && Facing != Direction.None)
        {
            var pal = CalcPalette(Palette.Blue);
            Animator.Draw(TileSheet.Npcs, X, Y, pal);
        }
    }

    void SetFacingAnimation()
    {
        var dirOrd = Facing.GetOrdinal();
        Animator.Animation = Graphics.GetAnimation(TileSheet.Npcs, wizzrobeAnimMap[dirOrd]);
    }

    void AnimateAndCheckCollisions()
    {
        Animator.Advance();

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
