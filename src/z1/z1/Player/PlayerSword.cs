using z1.Actors;

namespace z1.Player;

internal sealed class PlayerSword : Actor
{
    const int DirCount = 4;
    const int SwordStates = 5;
    const int LastSwordState = SwordStates - 1;

    private static readonly Point[][] swordOffsets = new Point[][]
    {
        new[] { new Point(-8, -11), new Point(0, -11), new Point(1, -14), new Point(-1, -9) },
        new[] { new Point(11, 3), new Point(-11, 3), new Point(1, 13), new Point(-1, -10) },
        new[] { new Point(7, 3), new Point(-7, 3), new Point(1, 9), new Point(-1, -9) },
        new[] { new Point(3, 3), new Point(-3, 3), new Point(1, 5), new Point(-1, -1) }
    };

    public static readonly AnimationId[] swordAnimMap =
    {
        AnimationId.Sword_Right,
        AnimationId.Sword_Left,
        AnimationId.Sword_Down,
        AnimationId.Sword_Up
    };

    private static readonly AnimationId[] rodAnimMap =
    {
        AnimationId.Wand_Right,
        AnimationId.Wand_Left,
        AnimationId.Wand_Down,
        AnimationId.Wand_Up,
    };

    private static readonly byte[] swordStateDurations = new byte[] { 5, 8, 1, 1, 1 };

    public int state;
    private int timer;
    private SpriteImage image = new();

    public PlayerSword(Game game, ObjType type, int x = 0, int y = 0) : base(game, x, y)
    {
        ObjType = type;

        Put();
        timer = swordStateDurations[state];
        Decoration = 0;
    }

    int GetState()
    {
        return state;
    }

    void Put()
    {
        var player = Game.Link;
        Direction facingDir = player.Facing;
        X = player.X;
        Y = player.Y;

        int dirOrd = facingDir.GetOrdinal();
        var offset = swordOffsets[state][dirOrd];
        X += offset.X;
        Y += offset.Y;
        Facing = facingDir;

        var animMap = ObjType == ObjType.Rod ? rodAnimMap : swordAnimMap;

        var animIndex = animMap[dirOrd];
        image.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, animIndex);
    }

    void TryMakeWave()
    {
        if (state == 2)
        {
            bool makeWave = false;
            var wave = Game.World.GetObject(ObjectSlot.PlayerSwordShot);

            if (ObjType == ObjType.Rod)
            {
                if (wave == null || wave.ObjType != ObjType.MagicWave)
                {
                    makeWave = true;
                    Game.Sound.PlayEffect(SoundEffect.MagicWave);
                }
            }
            else
            {
                if (wave == null)
                {
                    // The original game skips checking hearts, and shoots, if [$529] is set.
                    // But, I haven't found any code that sets it.

                    var profile = Game.World.GetProfile();
                    var neededHeartsValue = (profile.Items[ItemSlot.HeartContainers] << 8) - 0x80;

                    if (profile.Hearts >= neededHeartsValue)
                    {
                        makeWave = true;
                        Game.Sound.PlayEffect(SoundEffect.SwordWave);
                    }
                }
            }

            if (makeWave)
                MakeWave();
        }
    }

    void MakeWave()
    {
        var player = Game.Link;
        int x = player.X;
        int y = player.Y;
        Direction dir = player.Facing;

        MoveSimple(ref x, ref y, dir, 0x10);

        if (dir.IsVertical()  || (x >= 0x14 && x < 0xEC))
        {
            var type = ObjType == ObjType.Rod ? ObjType.MagicWave : ObjType.PlayerSwordShot;
            var wave = GlobalFunctions.MakeProjectile(Game.World, type, x, y, dir, ObjectSlot.PlayerSwordShot);

            Game.World.SetObject(ObjectSlot.PlayerSwordShot, wave);
            wave.TileOffset = player.TileOffset;
        }
    }

    public override void Update()
    {
        timer--;

        if (timer == 0)
        {
            if (state == LastSwordState)
            {
                IsDeleted = true;
                return;
            }
            state++;
            timer = swordStateDurations[state];
            // The original game does this: player.animTimer := timer
            // But, we do it differently. The player handles all of its animation.
        }

        if (state < LastSwordState)
        {
            Put();
            TryMakeWave();
        }
    }

    public override void Draw()
    {
        if (state > 0 && state < LastSwordState)
        {
            var weaponValue = Game.World.GetItem(ItemSlot.Sword);
            var palette = (ObjType == ObjType.Rod) ? Palette.BlueFgPalette : (Palette.Player + weaponValue - 1);
            var xOffset = (16 - image.Animation.Width) / 2;
            image.Draw(TileSheet.PlayerAndItems, X + xOffset, Y, palette);
        }
    }
}