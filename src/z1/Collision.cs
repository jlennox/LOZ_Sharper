using z1.Actors;

namespace z1;

internal sealed class CollisionContext(Actor? weapon, DamageType damageType, int damage, Point distance)
{
    public Actor? Weapon { get; set; } = weapon;
    public DamageType DamageType { get; set; } = damageType;
    public int Damage { get; set; } = damage;
    public Point Distance = distance;
}

internal enum TileCollisionStep { CheckTile, NextDir }
internal enum CollisionResponse { Unknown, Free, Blocked }

internal readonly record struct PlayerCollision(bool Collides, bool ShotCollides)
{
    public static implicit operator bool(PlayerCollision b) => b.Collides;
}

internal record struct TileCollision(bool Collides, TileBehavior TileBehavior, int FineCol, int FineRow)
{
    public static implicit operator bool(TileCollision b) => b.Collides;

    public bool CollidesWall => TileBehavior.CollidesWall();

    public readonly override string ToString() => $"Collides:{Collides}, TileBehavior:{TileBehavior}, FineCol:{FineCol}, FineRow:{FineRow}";
}