using SkiaSharp;

namespace z1;

internal abstract class ShootingType
{
}

internal sealed class FlyingRockShootingType : ShootingType
{
}

internal abstract class Monster : Actor
{
    protected const int StandardSpeed = 0x20;
    protected const int FastSpeed = 0x40;

    protected abstract MonsterSprites MonsterSprites { get; }
    protected abstract int AnimationTime { get; }
    protected abstract int Speed { get; }
    protected abstract ShootingType? ShotType { get; }

    protected static SKBitmap SpriteFromIndex(int y, int index) => Sprites.FromSheet(Sprites.BadguysOverworld, 8 + index * 17, y);
}

internal readonly struct MonsterSprites
{
    public readonly Palette PaletteA;
    public readonly Palette PaletteB;
    public readonly SKBitmap[] Left;
    public readonly SKBitmap[] Right;
    public readonly SKBitmap[] Up;
    public readonly SKBitmap[] Down;

    private MonsterSprites(Palette paletteA, Palette paletteB)
    {
        PaletteA = paletteA;
        PaletteB = paletteB;
    }

    public MonsterSprites(Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] down) : this(paletteA, paletteB)
    {
        Left = left;
        Right = left.Mirror();
        Up = down.Flip();
        Down = down;
    }

    public MonsterSprites(Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] down, SKBitmap[] up) : this(paletteA, paletteB)
    {
        Left = left;
        Right = left.Mirror();
        Up = up;
        Down = down;
    }

    public MonsterSprites(Palette paletteA, Palette paletteB, SKBitmap[] left, SKBitmap[] right, SKBitmap[] down, SKBitmap[] up) : this(paletteA, paletteB)
    {
        Left = left;
        Right = right;
        Up = up;
        Down = down;
    }

    // public SKBitmap AsPaletteB(SKBitmap bitmap) => bitmap.ChangePalette(PaletteA, PaletteB);
}

internal sealed class Octorok : Monster
{
    private static class Images
    {
        private const int _y = 19;

        public static readonly SKBitmap Down1 = SpriteFromIndex(0, _y);
        public static readonly SKBitmap Down2 = SpriteFromIndex(1, _y);

        public static readonly SKBitmap Left1 = SpriteFromIndex(2, _y);
        public static readonly SKBitmap Left2 = SpriteFromIndex(3, _y);

        public static readonly SKBitmap[] Left = [Left1, Left2];
        public static readonly SKBitmap[] Down = [Down1, Down2];
    }

    protected override MonsterSprites  MonsterSprites => new(Palette.Red, Palette.Blue, Images.Left, Images.Down);
    protected override int AnimationTime => 12;
    protected override int Speed => StandardSpeed;
    protected override ShootingType? ShotType => new FlyingRockShootingType();

    public bool IsBlue { get; }

    private int TurnTimer = 0;
    private int ShoveDir = 0;
    private Direction InputDir = 0;
    private int InvClock = 0;
    private int StunTimer = 0;
    private int GridOffset = 0;
    private int QSpeedFrac = 0;
    private int State = 0;
    private const int TurnRate = 0x41;

    public Octorok(bool isBlue = false)
    {
        var image = Images.Left[0];
        Size = new SizeF(image.Width, image.Height);
        IsBlue = isBlue;
    }

    public override void Tick(Game game)
    {
        var turnRate = IsBlue ? 0xA0 : 0x70;

        Wanderer_TargetPlayer(game);

        var speed = 0x20 * (IsBlue ? 2 : 1);
    }

    private void Wanderer_TargetPlayer(Game game)
    {
        // If turn timer != 0, then decrement it.
        if (TurnTimer != 0) TurnTimer--;

        Walker_Move();

        if (ShoveDir == 0)
        {
            // If speed = 0, or the object is between squares;
            // then go set input direction to facing direction.
            if (QSpeedFrac == 0 || (GridOffset & 0x0F) != 0)
            {
                // TODO SetInputDir();
            }

            // Set truncated grid offset.
            GridOffset &= 0xF0;

            // If turn rate < a random value, or Link's state = 0xFF;
            // then go turn if turn timer has expired.
            // TODO if (TurnRate < Random1 || State == 0xFF)
            // TODO {
            // TODO     TurnIfTime();
            // TODO }

            // Get the absolute horizontal distance between
            // the monster and the chase target.
            int distance = (int)Math.Abs(game.ChaseTarget.Position.X - Position.X);

            // If distance >= 9, then go check the vertical distance.
            if (distance >= 9)
            {
                // TODO CheckVerticalDistance();
            }
        }
    }

    private void Walker_Move()
    {
        if (ShoveDir != 0)
        {
            Obj_Shove();
            return;
        }

        // Choose object direction or input direction for movement as appropriate.
        if (GridOffset != 0)
        {
            if (InputDir == 0)
            {
                Dir = 0;
            }
            else
            {
                Dir = Dir;
            }
        }
        else
        {
            if (InvClock != 0 || StunTimer != 0)
            {
                return;
            }

            if (InputDir == 0)
            {
                Dir = 0;
            }
            else
            {
                Dir = GetOppositeDir(InputDir);
            }
        }

        // Mask off everything but directions.
        Dir &= Direction.Mask;

        // If object is Link and using or catching an item then reset movement direction.
        // if (Type == ObjType.Type07 && (State & 0xF0) == 0x10 || (State & 0xF0) == 0x20)
        // {
        //     Dir = 0;
        // }
    }

    private static Direction GetOppositeDir(Direction dir) => (Direction)(((int)dir + 2) & 0x03);

    public void Obj_Shove()
    {
        // If this is not the first call to this routine for this instance of shoving (high bit is clear),
        // then go handle it separately.
        if ((ShoveDir & 0x80) == 0)
        {
            // MoveIfNotDone();
            return;
        }

        // Clear the high bit, so we don't repeat this initialization.
        ShoveDir &= 0x7F;

        // If the object faces horizontally, go check which axis shove direction is on.
        if ((int)Dir < 0x03)
        {
            // FacingHorizontally();
            return;
        }

        // The object faces vertically.
        // If the shove direction is vertical, return. We're OK to shove.
        if ((ShoveDir & 0x03) == 0)
        {
            return;
        }

        // Exit();
    }

    public override void Draw(Game game, SKCanvas canvas)
    {
        var image = Images.Left[0];
        game.DrawBitmap(image, Rect);
    }
}
