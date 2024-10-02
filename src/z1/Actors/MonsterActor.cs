namespace z1.Actors;

internal abstract class MonsterActor : Actor
{
    protected MonsterActor(Game game, ObjType type, int x = 0, int y = 0)
        : base(game, type, x, y)
    {
    }

    protected Direction GetXDirToPlayer(int x) => Game.World.GetObservedPlayerPos().X < x ? Direction.Left : Direction.Right;
    protected Direction GetYDirToPlayer(int y) => Game.World.GetObservedPlayerPos().Y < y ? Direction.Up : Direction.Down;
    protected Direction GetXDirToTruePlayer(int x) => Game.Player.X < x ? Direction.Left : Direction.Right;
    protected Direction GetYDirToTruePlayer(int y) => Game.Player.Y < y ? Direction.Up : Direction.Down;

    private Direction GetDir8ToPlayer(int x, int y)
    {
        var playerPos = Game.World.GetObservedPlayerPos();
        var dir = Direction.None;

        if (playerPos.Y < y)
        {
            dir |= Direction.Up;
        }
        else if (playerPos.Y > y)
        {
            dir |= Direction.Down;
        }

        if (playerPos.X < x)
        {
            dir |= Direction.Left;
        }
        else if (playerPos.X > x)
        {
            dir |= Direction.Right;
        }

        return dir;
    }

    protected Direction TurnTowardsPlayer8(int x, int y, Direction facing)
    {
        var dirToPlayer = GetDir8ToPlayer(x, y);
        var dirIndex = (uint)facing.GetDirection8Ord(); // uint required.

        dirIndex = (dirIndex + 1) % 8;

        for (var i = 0; i < 3; i++)
        {
            if (dirIndex.GetDirection8() == dirToPlayer) return facing;
            dirIndex = (dirIndex - 1) % 8;
        }

        dirIndex = (dirIndex + 1) % 8;

        for (var i = 0; i < 3; i++)
        {
            var dir = dirIndex.GetDirection8();
            if ((dir & dirToPlayer) != 0)
            {
                if ((dir | dirToPlayer) < (Direction)7) return dir;
            }
            dirIndex = (dirIndex + 1) % 8;
        }

        dirIndex = (dirIndex - 1) % 8;
        return dirIndex.GetDirection8();
    }

    protected Direction TurnRandomly8(Direction facing)
    {
        return Game.Random.GetByte() switch
        {
            >= 0xA0 => facing, // keep going in the same direction
            >= 0x50 => facing.GetNextDirection8(),
            _ => facing.GetPrevDirection8()
        };
    }
}