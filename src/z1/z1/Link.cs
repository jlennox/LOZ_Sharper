using SkiaSharp;

namespace z1;

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

        public static readonly SKBitmap[] WalkDown = [WalkDown1, WalkDown2];
        public static readonly SKBitmap[] WalkUp = [WalkUp1, WalkUp2];
        public static readonly SKBitmap[] WalkLeft = [WalkVertical1, WalkVertical2];
        public static readonly SKBitmap[] WalkRight = [WalkVertical1.Mirror(), WalkVertical2.Mirror()];
    }

    private int _walkFrame = 0;

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

    public override void Draw(Game game, SKCanvas canvas)
    {
        var sprites = Dir switch
        {
            Direction.Down => Images.WalkDown,
            Direction.Up => Images.WalkUp,
            Direction.Left => Images.WalkLeft,
            Direction.Right => Images.WalkRight,
            _ => throw new InvalidOperationException("Invalid direction")
        };

        var sprite = sprites[_walkFrame];
        game.DrawBitmap(sprite, Rect);
    }

    public override void Tick(Game game)
    {
    }

    public void DoMove(Direction dir, int amount)
    {
        Dir = dir;
        Position += new SizeF(dir switch
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
