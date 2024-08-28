using z1.Actors;

namespace z1;

internal sealed class CollisionContext(ObjectSlot weaponSlot, DamageType damageType, int damage, Point distance)
{
    public ObjectSlot WeaponSlot { get; set; } = weaponSlot;
    public DamageType DamageType { get; set; } = damageType;
    public int Damage { get; set; } = damage;
    public Point Distance = distance;
}

internal enum TileCollisionStep { CheckTile, NextDir }
internal enum CollisionResponse { Unknown, Free, Blocked }

internal interface IBlocksPlayer
{
    public CollisionResponse CheckCollision();
}

internal readonly record struct PlayerCollision(bool Collides, bool ShotCollides)
{
    public static implicit operator bool(PlayerCollision b) => b.Collides;
}

internal record struct TileCollision(bool Collides, TileBehavior TileBehavior, int FineCol, int FineRow)
{
    public static implicit operator bool(TileCollision b) => b.Collides;

    public readonly override string ToString() => $"Collides:{Collides}, TileBehavior:{TileBehavior}, FineCol:{FineCol}, FineRow:{FineRow}";
}

internal enum TileBehavior : byte
{
    GenericWalkable,
    Sand,
    SlowStairs,
    Stairs,

    Doorway,
    Water,
    GenericSolid,
    Cave,
    Ghost0,
    Ghost1,
    Ghost2,
    Ghost3,
    Ghost4,
    Ghost5,
    Ghost6,
    Ghost7,
    Ghost8,
    Ghost9,
    GhostA,
    GhostB,
    GhostC,
    GhostD,
    GhostE,
    GhostF,
    Armos0,
    Armos1,
    Armos2,
    Armos3,
    Armos4,
    Armos5,
    Armos6,
    Armos7,
    Armos8,
    Armos9,
    ArmosA,
    ArmosB,
    ArmosC,
    ArmosD,
    ArmosE,
    ArmosF,
    Door,
    Wall,

    Max,

    FirstWalkable = GenericWalkable,
    FirstSolid = Doorway,
}