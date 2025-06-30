namespace z1.Actors;

internal abstract class MonsterActor : Actor
{
    public override bool IsMonsterSlot => true;

    public bool IsRingleader { get; set; }

    protected MonsterActor(World world, ObjType type, int x = 0, int y = 0)
        : base(world, type, x, y)
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

    public override bool Delete()
    {
        if (base.Delete())
        {
            // I'm not sure about doing this in delete... because it does trigger on the room change.
            if (IsRingleader)
            {
                Game.World.KillAllObjects();
            }
            return true;
        }

        return false;
    }

    protected Actor? Shoot(ObjType shotType, int x, int y, Direction facing, ProjectileOptions options = ProjectileOptions.None)
    {
        Ensure.ThrowIfPlayer(this);

        var oldActiveShots = World.ActiveMonsterShots;

        // JOE NOTE: I hate that this exists while " Game.Data.GetObjectAttribute(collider.ObjType).Damage" still exists.
        // One complication is Boomerang is not a projectile. We need a lower base to handle all of that, I assume?
        // SECOND NOTE: Ya... these damage values go unused :) Projectile.Damage is _only_ used when it's from Player.
        var damage = shotType switch
        {
            ObjType.FlyingRock => FlyingRockProjectile.MonsterBaseDamage,
            ObjType.Arrow => ArrowProjectile.MonsterBaseDamage,
            ObjType.MagicWave => MagicWaveProjectile.MonsterBaseDamage,
            ObjType.MagicWave2 => MagicWaveProjectile.MonsterBaseDamage,
            ObjType.Boomerang => BoomerangProjectile.MonsterBaseDamage,
            ObjType.PlayerSwordShot => PlayerSwordProjectile.MonsterBaseDamage,
            _ => throw new ArgumentOutOfRangeException(nameof(shotType), shotType, "Invalid ObjType.")
        };

        var shot = shotType == ObjType.Boomerang
            ? Projectile.MakeBoomerang(World, x, y, facing, 0x51, 2.5f, this)
            : (Actor)Projectile.MakeProjectile(World, shotType, x, y, facing, damage, options, this);

        var newActiveShots = World.ActiveMonsterShots;
        if (oldActiveShots != newActiveShots && newActiveShots > 4)
        {
            shot.Delete();
            return null;
        }

        World.AddObject(shot);
        // In the original, they start in state $10. But, that was only a way to say that the object exists.
        shot.ObjTimer = 0;
        return shot;
    }

    protected FireballProjectile? ShootFireball(ObjType type, int x, int y, int? offset = null)
    {
        Ensure.ThrowIfPlayer(this);

        return Game.ShootFireball(type, x, y, offset);
    }
}