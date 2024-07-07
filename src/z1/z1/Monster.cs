using SkiaSharp;

namespace z1;

internal abstract class Projectile
{
}

internal sealed class FlyingRockProjectile : Projectile
{
}

internal abstract class Monster : Actor
{
    protected const int StandardSpeed = 0x20;
    protected const int FastSpeed = 0x40;

    protected abstract MonsterSprites MonsterSprites { get; }
    protected abstract int AnimationTime { get; }
    protected abstract int Speed { get; }
    protected abstract bool HasProjectile { get; }
    protected abstract bool IsBlue { get; }

    protected int CurrentSpeed;
    protected int ShootTimer = 0;
    protected bool WantToShoot = false;
    protected int InvincibilityTimer = 0;

    protected bool IsStunned => false;

    protected static SKBitmap SpriteFromIndex(int index, int y) => Sprites.FromSheet(Sprites.BadguysOverworld, 8 + index * 17, y);

    public Monster()
    {
        CurrentSpeed = Speed;
    }

    protected void TryShooting()
    {
        if (!HasProjectile) return;

        if (IsBlue || ShootTimer != 0 || Random.Shared.Next(0xFF) >= 0xF8)
        {
            if (InvincibilityTimer > 0)
            {
                ShootTimer = 0;
            }
            else
            {
                if (ShootTimer > 0)
                {
                    ShootTimer--;
                }
                else if (WantToShoot)
                {
                    ShootTimer = 0x30;
                }
            }
        }

        if (ShootTimer == 0)
        {
            CurrentSpeed = Speed;
            return;
        }

        if (ShootTimer != 0x10 || IsStunned)
        {
            CurrentSpeed = 0;
            return;
        }

        if (WantToShoot && Game.AddProjectile(CreateProjectile()))
        {
            CurrentSpeed = 0;
            WantToShoot = false;
        }
        else
        {
            CurrentSpeed = Speed;
        }
    }

    protected Direction ShoveDirection = Direction.None;
    protected Direction Facing = Direction.None;
    protected byte ShoveDistance = 0;
    protected byte TileOffset = 0;
    protected byte Moving;
    protected byte Fraction;

    protected void Move(int speed)
    {
        if (ShoveDirection != Direction.None)
        {
            Shove();
            return;
        }

        var dir = Direction.None;

        if (IsStunned)
            return;

        if (Moving != 0)
        {
            int dirOrd = ((Direction)Moving).GetOrdinal();
            dir = dirOrd.GetOrdDirection();
        }

        dir = dir & Direction.Mask;

        // Original: [$E] := 0
        // Maybe it's only done to set up the call to FindUnblockedDir in CheckTileCollision?

        dir = CheckWorldMargin(dir);
        // TODO: dir = CheckTileCollision(dir);

        MoveDir(speed, dir);
    }

    protected void MoveDir(int speed, Direction dir)
    {
        int align = 0x10;

        if (IsPlayer)
            align = 8;

        MoveWhole(speed, dir, align);
    }

    void MoveWhole(int speed, Direction dir, int align)
    {
        if (dir == Direction.None)
            return;

        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
        MoveFourth(speed, dir, align);
    }

    void MoveFourth(int speed, Direction dir, int align)
    {
        int frac = Fraction;

        if (dir == Direction.Down || dir == Direction.Right)
        {
            frac += speed;
        }
        else
        {
            frac -= speed;
        }

        int carry = frac >> 8;
        Fraction = (byte)(frac & 0xFF);

        if ((TileOffset != align) && (TileOffset != -align))
        {
            TileOffset += (byte)carry;
            Position += dir.IsHorizontal() ? new Size(carry, 0) : new Size(0, carry);
        }
    }

    protected void Shove()
    {
        if ((ShoveDirection & (Direction)0x80) == 0)
        {
            if (ShoveDistance != 0)
            {
                MoveShoveWhole();
            }
            else
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
            }
        }
        else
        {
            ShoveDirection ^= (Direction)0x80;

            var shoveHoriz = ShoveDirection.IsHorizontal(Direction.Mask);
            var facingHoriz = Facing.IsHorizontal();

            if ((shoveHoriz != facingHoriz) && (TileOffset != 0) && !IsPlayer)
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
            }
        }
    }

    protected void MoveShoveWhole()
    {
        var cleanDir = ShoveDirection & Direction.Mask;

        for (int i = 0; i < 4; i++)
        {
            if (TileOffset == 0)
            {
                Position = new Point(Position.X & 0xF8, (Position.Y & 0xF8) | 5);

                // TODO:
                // if (World::CollidesWithTileMoving(objX, objY, cleanDir, IsPlayer))
                // {
                //     shoveDir = 0;
                //     shoveDistance = 0;
                //     return;
                // }
            }

            if (CheckWorldMargin(cleanDir) == Direction.None || StopAtPersonWallUW(cleanDir) == Direction.None)
            {
                ShoveDirection = 0;
                ShoveDistance = 0;
                return;
            }

            var distance = cleanDir.IsGrowing() ? 1 : -1;

            ShoveDistance--;
            TileOffset += (byte)distance;

            if ((TileOffset & 0xF) == 0)
            {
                TileOffset &= 0xF;
            }
            else if (IsPlayer && (TileOffset & 7) == 0)
            {
                TileOffset &= 7;
            }

            Position += cleanDir.IsHorizontal() ? new Size(distance, 0) : new Size(0, distance);
        }
    }

    protected Direction CheckWorldMarginH(int x, Direction dir, bool adjust)
    {
        Direction curDir = Direction.Left;

        if (adjust)
            x += 0xB;

        if (x > Game.MarginLeft)
        {
            if (adjust)
                x -= 0x17;

            curDir = Direction.Right;

            if (x < Game.MarginRight)
                return dir;
        }

        if ((dir & curDir) != 0)
            return Direction.None;

        return dir;
    }

    protected Direction CheckWorldMarginV(int y, Direction dir, bool adjust)
    {
        var curDir = Direction.Up;

        if (adjust)
            y += 0xF;

        if (y > Game.MarginTop)
        {
            if (adjust)
                y -= 0x21;

            curDir = Direction.Down;

            if (y < Game.MarginBottom)
                return dir;
        }

        if ((dir & curDir) != 0)
            return Direction.None;

        return dir;
    }

    Direction CheckWorldMargin(Direction dir)
    {
        // TODO:
        // int slot = World::GetCurrentObjectSlot();
        // bool adjust = (slot > BufferSlot) || (GetType() == Obj_Ladder);
        //
        // // ORIGINAL: This isn't needed, because the player is first (slot=0).
        // if (slot >= PlayerSlot)
        //     adjust = false;

        var adjust = false;
        dir = CheckWorldMarginH(Position.X, dir, adjust);
        return CheckWorldMarginV(Position.Y, dir, adjust);
    }


    Direction StopAtPersonWall(Direction dir)
    {
        if (Position.Y < 0x8E && (dir & Direction.Up) != 0)
        {
            return Direction.None;
        }

        return dir;
    }

    Direction StopAtPersonWallUW(Direction dir)
    {
        // ($6E46) if first object is grumble or person, block movement up above $8E.

        // TODO:
        // Object* firstObj = World::GetObject(MonsterSlot1);
        //
        // if (firstObj != nullptr)
        // {
        //     ObjType type = firstObj->GetType();
        //     if (type == Obj_Grumble
        //         || (type >= Obj_Person1 && type < Obj_Person_End))
        //     {
        //         return StopAtPersonWall(dir);
        //     }
        // }

        return dir;
    }

    protected virtual Projectile CreateProjectile() => throw new NotImplementedException();
}

internal abstract class WandererMonster : Monster
{
    protected byte TurnTimer;
    protected byte TurnRate;

    public override void Tick(Game game)
    {
        // animator.Advance();
        Move();
        TryShooting();
        // TODO: CheckCollisions();
    }

    protected void Move()
    {
        Move(CurrentSpeed);
        TargetPlayer();
    }

    void TargetPlayer()
    {
        if (TurnTimer > 0)
            TurnTimer--;

        if (ShoveDirection != 0)
            return;

        if (CurrentSpeed == 0 || (TileOffset & 0xF) != 0)
        {
            Moving = (byte)Facing;
            return;
        }

        TileOffset &= 0xF;

        int r = Random.Shared.Next(255);

        // ORIGINAL: If (r > turnRate) or (player.state = $FF), then ...
        //           But, I don't see how the player can get in that state.

        if (r > TurnRate)
        {
            TurnIfTime();
        }
        else
        {
            var playerPos = Game.GetObserverPlayer.Position;

            if (Math.Abs(Position.X - playerPos.X) < 9)
                TurnY();
            else if (Math.Abs(Position.Y - playerPos.Y) < 9)
                TurnX();
            else
                TurnIfTime();
        }

        Moving = (byte)Facing;
    }

    void TurnIfTime()
    {
        WantToShoot = false;

        if (TurnTimer != 0)
            return;

        if (Facing.IsVertical())
            TurnX();
        else
            TurnY();
    }

    void TurnX()
    {
        Facing = Game.GetXDirToPlayer(Position.X);
        TurnTimer = Random.Shared.GetByte();
        WantToShoot = true;
    }

    void TurnY()
    {
        Facing = Game.GetYDirToPlayer(Position.X);
        TurnTimer = Random.Shared.GetByte();
        WantToShoot = true;
    }
}

internal sealed class Octorok : WandererMonster
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
    protected override bool HasProjectile => true;

    protected override bool IsBlue => false;

    public Octorok()
    {
        var image = Images.Left[0];
        Size = new SizeF(image.Width, image.Height);
    }

    protected override Projectile CreateProjectile()
    {
        return new FlyingRockProjectile();
    }

    private static Direction GetOppositeDir(Direction dir) => (Direction)(((int)dir + 2) & 0x03);

    public override void Draw(Game game, SKCanvas canvas)
    {
        var image = Images.Left[0];
        game.DrawBitmap(image, Rect);
    }
}
