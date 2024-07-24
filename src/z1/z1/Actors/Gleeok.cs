﻿namespace z1.Actors;

internal sealed class GleeokHeadActor : FlyingActor
{
    private static readonly AnimationId[] _gleeokHeadAnimMap = new[]
    {
        AnimationId.B2_Gleeok_Head2,
        AnimationId.B2_Gleeok_Head2,
        AnimationId.B2_Gleeok_Head2,
        AnimationId.B2_Gleeok_Head2,
    };

    private static readonly FlyerSpec _gleeokHeadSpec = new(_gleeokHeadAnimMap, TileSheet.Boss, Palette.Red, 0xE0);

    public override bool IsReoccuring => false;
    public GleeokHeadActor(Game game, int x, int y)
        : base(game, ObjType.GleeokHead, _gleeokHeadSpec, x, y)
    {
        Facing = Random.Shared.GetDirection8();

        CurSpeed = 0xBF;
        InvincibilityMask = 0xFF;
    }

    public override void Update()
    {
        UpdateStateAndMove();

        var r = Random.Shared.GetByte();

        if (r < 0x20
            && (MoveCounter & 1) == 0
            && Game.World.GetObject(ObjectSlot.LastMonster) == null)
        {
            ShootFireball(ObjType.Fireball2, X, Y);
        }

        CheckCollisions();
        Decoration = 0;
        ShoveDirection = 0;
        ShoveDistance = 0;
        InvincibilityTimer = 0;
    }

    protected override void UpdateFullSpeedImpl()
    {
        var nextState = 2;
        var r = Random.Shared.GetByte();

        if (r >= 0xD0)
        {
            nextState++;
        }

        GoToState(nextState, 6);
    }
}

internal sealed class GleeokNeck
{
    private readonly Game _game;
    public const int MaxParts = 5;
    public const int HeadIndex = MaxParts - 1;
    public const int ShooterIndex = HeadIndex;

    private struct Limits
    {
        public int Value0;
        public int Value1;
        public int Value2;
    }

    private static readonly byte[] _startYs = new byte[] { 0x6F, 0x74, 0x79, 0x7E, 0x83 };

    public int HP;

    private readonly Point[] _parts = new Point[MaxParts];
    private readonly SpriteImage _neckImage;
    private readonly SpriteImage _headImage;

    private int _startHeadTimer;
    private int _xSpeed;
    private int _ySpeed;
    private int _changeXDirTimer;
    private int _changeYDirTimer;
    private int _changeDirsTimer;
    private bool _isAlive;

    public GleeokNeck(Game game, int index)
    {
        _game = game;

        for (var i = 0; i < MaxParts; i++)
        {
            _parts[i].X = 0x7C;
            _parts[i].Y = _startYs[i];
        }

        HP = 0xA0;
        _isAlive = true;
        _changeXDirTimer = 6;
        _changeYDirTimer = 3;
        _xSpeed = 1;
        _ySpeed = 1;
        _changeDirsTimer = 0;
        _startHeadTimer = 0;

        if (index is 0 or 2)
        {
            _xSpeed = -1;
        }
        else
        {
            _ySpeed = -1;
        }

        _startHeadTimer = index switch
        {
            1 => 12,
            2 => 24,
            3 => 36,
            _ => _startHeadTimer
        };

        _neckImage = new SpriteImage
        {
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gleeok_Neck)
        };

        _headImage = new SpriteImage
        {
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gleeok_Head)
        };
    }

    public bool IsAlive() => _isAlive;
    public void SetDead() => _isAlive = false;
    public void SetHP(int value) => HP = value;
    public Point GetPartLocation(int partIndex) => new(_parts[partIndex].X, _parts[partIndex].Y);

    public void Update()
    {
        MoveNeck();
        MoveHead();
        TryShooting();
    }

    public void Draw()
    {
        for (var i = 0; i < HeadIndex; i++)
        {
            _neckImage.Draw(TileSheet.Boss, _parts[i].X, _parts[i].Y, Palette.SeaPal);
        }

        _headImage.Draw(TileSheet.Boss, _parts[HeadIndex].X, _parts[HeadIndex].Y, Palette.SeaPal);
    }

    private void MoveHead()
    {
        if (_startHeadTimer != 0)
        {
            _startHeadTimer--;
            return;
        }

        _parts[HeadIndex].X += _xSpeed;
        _parts[HeadIndex].Y += _ySpeed;

        _changeDirsTimer++;
        if (_changeDirsTimer < 4) return;
        _changeDirsTimer = 0;

        _changeXDirTimer++;
        if (_changeXDirTimer >= 0xC)
        {
            _changeXDirTimer = 0;
            _xSpeed = -_xSpeed;
        }

        _changeYDirTimer++;
        if (_changeYDirTimer >= 6)
        {
            _changeYDirTimer = 0;
            _ySpeed = -_ySpeed;
        }
    }

    private void TryShooting()
    {
        var r = Random.Shared.GetByte();
        if (r < 0x20 && _game.World.GetObject(ObjectSlot.LastMonster) == null)
        {
            _game.ShootFireball(ObjType.Fireball2, _parts[ShooterIndex].X, _parts[ShooterIndex].Y);
        }
    }

    private void MoveNeck()
    {
        Limits xLimits = new();
        Limits yLimits = new();

        var headToEndXDiv4 = (_parts[4].X - _parts[0].X) / 4;
        var headToEndXDiv4Abs = Math.Abs(headToEndXDiv4);
        GetLimits(headToEndXDiv4Abs, ref xLimits);

        var headToEndYDiv4Abs = Math.Abs(_parts[4].Y - _parts[0].Y) / 4;
        GetLimits(headToEndYDiv4Abs, ref yLimits);

        // If passed the capped high limit X or Y from previous part, then bring it back in. (1..4)
        for (var i = 0; i < 4; i++)
        {
            var distance = Math.Abs(_parts[i].X - _parts[i + 1].X);
            if (distance >= xLimits.Value2)
            {
                var oldX = _parts[i + 1].X;
                var x = oldX + 2;
                if (oldX >= _parts[i].X)
                {
                    x -= 4;
                }
                _parts[i + 1].X = x;
            }
            distance = Math.Abs(_parts[i].Y - _parts[i + 1].Y);
            if (distance >= yLimits.Value2)
            {
                var oldY = _parts[i + 1].Y;
                var y = oldY + 2;
                if (oldY >= _parts[i].Y)
                {
                    y -= 4;
                }
                _parts[i + 1].Y = y;
            }
        }

        // Stretch, depending on distance to the next part. (1..3)
        for (var i = 0; i < 3; i++)
        {
            Stretch(i, ref xLimits, ref yLimits);
        }

        // If passed the X limit, then bring it back in. (3..1)
        for (var i = 2; i >= 0; i--)
        {
            var xLimit = _parts[0].X;
            for (var j = i; j >= 0; j--)
            {
                xLimit += headToEndXDiv4;
            }
            var x = _parts[i + 1].X + 1;
            if (xLimit < _parts[i + 1].X)
            {
                x -= 2;
            }
            _parts[i + 1].X = x;
        }

        // If part's Y is not in between surrounding parts, then bring it back in. (3..2)
        for (var i = 1; i >= 0; i--)
        {
            var y2 = _parts[i + 2].Y;
            if (y2 < _parts[i + 1].Y)
            {
                if (y2 < _parts[i + 3].Y)
                {
                    _parts[i + 2].Y++;
                }
            }
            else
            {
                if (y2 >= _parts[i + 3].Y)
                {
                    _parts[i + 2].Y--;
                }
            }
        }
    }

    private static void GetLimits(int distance, ref Limits limits)
    {
        if (distance > 4)
        {
            distance = 4;
        }
        limits.Value0 = distance;

        distance += 4;
        if (distance > 8) // JOE: NOTE: Impossible value.
        {
            distance = 8;
        }
        limits.Value1 = distance;

        distance += 4;
        if (distance > 11)
        {
            distance = 11;
        }
        limits.Value2 = distance;
    }

    private void Stretch(int index, ref Limits xLimits, ref Limits yLimits)
    {
        var funcIndex = 0;

        // The original was [index+2] - [index+2]
        var distance = Math.Abs(_parts[index + 2].X - _parts[index + 1].X);
        if (distance >= xLimits.Value0) funcIndex++;
        if (distance >= xLimits.Value1) funcIndex++;

        distance = Math.Abs(_parts[index + 2].Y - _parts[index + 1].Y);
        if (distance >= yLimits.Value0) funcIndex += 3;
        if (distance >= yLimits.Value1) funcIndex += 3;

        var funcs = new Action<int>[]
        {
            CrossedNoLimits,
            CrossedLowLimit,
            CrossedMidXLimit,
            CrossedLowLimit,
            CrossedLowLimit,
            CrossedMidXLimit,
            CrossedMidYLimit,
            CrossedMidYLimit,
            CrossedBothMidLimits,
        };

        Debug.Assert(funcIndex >= 0 && funcIndex < funcs.Length);

        funcs[funcIndex](index);
    }

    private void CrossedNoLimits(int index)
    {
        var r = Random.Shared.Next(2);
        if (r == 0)
        {
            var oldX = _parts[index + 1].X;
            var x = oldX + 2;
            if (oldX < _parts[index + 2].X)
            {
                x -= 4;
            }
            _parts[index + 1].X = x;
        }
        else
        {
            var oldY = _parts[index + 1].Y;
            var y = oldY + 2;
            if (oldY <= _parts[index + 2].Y)
            {
                y -= 4;
            }
            _parts[index + 1].Y = y;
        }
    }

    private void CrossedLowLimit(int index)
    {
        // Nothing to do
    }

    private void CrossedMidYLimit(int index)
    {
        var oldY = _parts[index + 1].Y;
        var y = oldY + 2;
        if (oldY > _parts[index + 2].Y)
        {
            y -= 4;
        }
        _parts[index + 1].Y = y;
    }

    private void CrossedMidXLimit(int index)
    {
        var oldX = _parts[index + 1].X;
        var x = oldX + 2;
        if (oldX >= _parts[index + 2].X)
        {
            x -= 4;
        }
        _parts[index + 1].X = x;
    }

    private void CrossedBothMidLimits(int index)
    {
        var r = Random.Shared.Next(2);
        if (r == 0)
        {
            CrossedMidXLimit(index);
        }
        else
        {
            CrossedMidYLimit(index);
        }
    }
}

internal sealed class GleeokActor : Actor
{
    private const int GleeokX = 0x74;
    private const int GleeokY = 0x57;

    private const int MaxNecks = 4;
    private const int NormalAnimFrames = 17 * 4;
    private const int WrithingAnimFrames = 7 * 4;
    private const int TotalWrithingFrames = 7 * 7;

    private static readonly byte[] _palette = new byte[] { 0, 0x2A, 0x1A, 0x0C };

    private readonly SpriteAnimator _animator;
    private int _writhingTimer;
    private readonly int _neckCount;
    private readonly GleeokNeck[] _necks = new GleeokNeck[MaxNecks];

    public override bool IsReoccuring => false;

    private GleeokActor(Game game, ObjType type, int x, int y)
        : base(game, type, x, y)
    {
        _neckCount = type - ObjType.Gleeok1 + 1;

        Decoration = 0;
        InvincibilityMask = 0xFE;

        _animator = new SpriteAnimator
        {
            DurationFrames = NormalAnimFrames,
            Time = 0,
            Animation = Graphics.GetAnimation(TileSheet.Boss, AnimationId.B2_Gleeok_Body)
        };

        for (var i = 0; i < _neckCount; i++)
        {
            _necks[i] = new GleeokNeck(game, i);
        }

        Graphics.SetPaletteIndexed(Palette.LevelFgPalette, _palette);
        Graphics.UpdatePalettes();

        Game.Sound.PlayEffect(SoundEffect.BossRoar1, true, Sound.AmbientInstance);
    }

    public static GleeokActor Make(Game game, int headCount, int x = GleeokX, int y = GleeokY)
    {
        var type = headCount switch
        {
            1 => ObjType.Gleeok1,
            2 => ObjType.Gleeok2,
            3 => ObjType.Gleeok3,
            4 => ObjType.Gleeok4,
            _ => throw new ArgumentOutOfRangeException(),
        };

        return new GleeokActor(game, type, x, y);
    }

    public override void Update()
    {
        Animate();

        for (var i = 0; i < _neckCount; i++)
        {
            if (!_necks[i].IsAlive()) continue;

            if ((Game.GetFrameCounter() % MaxNecks) == i)
            {
                _necks[i].Update();
            }

            CheckNeckCollisions(i);
        }
    }

    public override void Draw()
    {
        var pal = CalcPalette(Palette.SeaPal);
        _animator.Draw(TileSheet.Boss, X, Y, pal);

        for (var i = 0; i < _neckCount; i++)
        {
            if (_necks[i].IsAlive())
            {
                _necks[i].Draw();
            }
        }
    }

    private void Animate()
    {
        _animator.Advance();

        if (_writhingTimer != 0)
        {
            _writhingTimer--;
            if (_writhingTimer == 0)
            {
                _animator.DurationFrames = NormalAnimFrames;
                _animator.Time = 0;
            }
        }
    }

    private void CheckNeckCollisions(int index)
    {
        var neck = _necks[index];
        var partIndexes = new[] { 0, GleeokNeck.HeadIndex };
        var origX = X;
        var origY = Y;
        var bodyDecoration = 0;

        for (var i = 0; i < 2; i++)
        {
            var partIndex = partIndexes[i];
            var loc = neck.GetPartLocation(partIndex);

            X = loc.X;
            Y = loc.Y;
            HP = (byte)neck.HP;

            CheckCollisions();

            neck.SetHP(HP);

            if (ShoveDirection != 0)
            {
                _writhingTimer = TotalWrithingFrames;
                _animator.DurationFrames = WrithingAnimFrames;
                _animator.Time = 0;
            }

            ShoveDirection = 0;
            ShoveDistance = 0;

            if (partIndex != GleeokNeck.HeadIndex)
            {
                Decoration = 0;
            }
            else
            {
                PlayBossHitSoundIfHit();

                if (Decoration != 0)
                {
                    neck.SetDead();

                    var slot = ObjectSlot.Monster1 + index + 6;
                    var head = new GleeokHeadActor(Game, X, Y);
                    Game.World.SetObject(slot, head);

                    var aliveCount = 0;
                    for (var jj = 0; jj < _neckCount; jj++)
                    {
                        if (_necks[jj].IsAlive())
                        {
                            aliveCount++;
                        }
                    }

                    if (aliveCount == 0)
                    {
                        Game.Sound.PlayEffect(SoundEffect.BossHit);
                        Game.Sound.StopEffect(StopEffect.AmbientInstance);

                        bodyDecoration = 0x11;
                        // Don't include the last slot, which is used for fireballs.
                        for (var jj = ObjectSlot.Monster1 + 1; jj < ObjectSlot.LastMonster; jj++)
                        {
                            Game.World.SetObject(jj, null);
                        }
                    }
                }
            }
        }

        Y = origY;
        X = origX;
        Decoration = (byte)bodyDecoration;
    }
}