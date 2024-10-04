using System.Diagnostics;
using z1.Render;

namespace z1.Actors;

// The runtime state for InteractableBlock.
[DebuggerDisplay("{GameObject.Name} ({X},{Y})")]
internal sealed class InteractiveGameObjectActor : Actor
{
    private const int MaxSpawnCount = 16;

    public InteractableBlock Interactable => GameObject.Interaction;
    public InteractiveGameObject GameObject { get; }

    private bool HasInteracted => _state.HasInteracted || _hasInteracted || Interactable.Interaction == Interaction.None;

    private readonly ObjectState _state;
    private readonly RaftInteraction? _raft;
    private readonly PushInteraction? _push;
    private bool _hasSoundPlayed;
    private bool _hasInteracted;

    public InteractiveGameObjectActor(Game game, InteractiveGameObject gameObject)
        : base(game, ObjType.Block, gameObject.X, gameObject.Y + World.TileMapBaseY)
    {
        GameObject = gameObject;
        _state = game.World.Profile.GetObjectFlags(game.World.CurrentWorld, game.World.CurrentRoom, gameObject);
        // JOE: NOTE: This not being set makes it make a poof animation when you enter the room. This might be a good
        // way to have a method of revealing secrets to the player?
        Decoration = 0;

        _raft = RaftInteraction.Create(game, this);
        _push = PushInteraction.Create(game, this);

        if (HasInteracted && Interactable.Persisted) SetInteracted(true);
    }

    public override void Update()
    {
        if (!CheckRequirements()) return;
        if (!CheckItemRequirement()) return;

        if (HasInteracted)
        {
            UpdateCaveEntrance();
            _raft?.Update();
            return;
        }

        if (CheckBombable() || CheckBurnable() || CheckCover() || (_push?.Check() ?? false))
        {
            SetInteracted(false);
        }
    }

    private void SetInteracted(bool initializing)
    {
        _hasInteracted = true;

        if (Interactable.Persisted)
        {
            _state.HasInteracted = true;
        }

        void OptionalSound()
        {
            if (initializing || _hasSoundPlayed) return;
            _hasSoundPlayed = true;
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }

        if (Interactable.Entrance.IsValid())
        {
            Game.World.SetMapObjectXY(X, Y, Interactable.Entrance.BlockType);
            if (!initializing)
            {
                OptionalSound();
                switch (Interactable.Interaction)
                {
                    case Interaction.Bomb: Game.World.Profile.Statistics.OWBlocksBombed++; break;
                    case Interaction.Burn: Game.World.Profile.Statistics.TreesBurned++; break;
                }
            }
        }

        if (Interactable.Raft != null)
        {
            Game.World.SetMapObjectXY(X, Y, BlockType.Dock);
            OptionalSound();
        }

        if (Interactable.Item != null && !_state.ItemGot)
        {
            var itemId = Interactable.Item.Item;
            var isRoomItem = Interactable.Item.IsRoomItem;
            var itemActor = new ItemObjActor(Game, itemId, isRoomItem, X, Y);
            itemActor.OnTouched += _ => _state.ItemGot = true;
            Game.World.AddObject(itemActor);
            OptionalSound();
        }

        if (Interactable.SpawnedType != null && Interactable.SpawnedType != ObjType.None)
        {
            var count = Game.World.GetObjects().Count(t => t.ObjType == Interactable.SpawnedType.Value);
            if (count < MaxSpawnCount)
            {
                Game.World.CommonMakeObjectAction(
                    Interactable.SpawnedType.Value,
                    X / World.TileWidth, Y / World.TileHeight - World.BaseRows);
            }
        }

        switch (Interactable.Effect)
        {
            case InteractionEffect.OpenShutterDoors:
                OptionalSound();
                Game.World.OpenShutters();
                break;
        }
    }

    private void UpdateCaveEntrance()
    {
        var caveEntrance = Interactable.Entrance;
        if (!caveEntrance.IsValid()) return;
        if (Game.World.WhirlwindTeleporting != 0) return;
        if (Game.World.GetMode() != GameMode.Play) return;

        var obj = Game.World.GetObjects();
        if (!Game.World.Player.DoesCover(this)) return;
        Game.World.GotoStairs(TileBehavior.Cave, caveEntrance);
    }

    private bool CheckRequirements()
    {
        if (Interactable.Requirements.HasFlag(InteractionRequirements.AllEnemiesDefeated))
        {
            if (Game.World.HasLivingObjects()) return false;
        }

        return true;
    }

    private bool CheckItemRequirement()
    {
        var requirement = GameObject.Interaction.ItemRequirement;
        if (requirement == null) return true;
        var actualValue = Game.World.GetItem(requirement.ItemSlot);
        return actualValue >= requirement.ItemLevel;
    }

    private bool CheckBombable()
    {
        if (Interactable.Interaction != Interaction.Bomb) return false;

        foreach (var bomb in Game.World.GetObjects<BombActor>())
        {
            if (bomb.IsDeleted || bomb.BombState != BombState.Blasting) continue;
            if (!IsWithinDistance(bomb, 16)) continue;

            return true;
        }
        return false;
    }

    private bool CheckBurnable()
    {
        if (Interactable.Interaction != Interaction.Burn) return false;

        foreach (var fire in Game.World.GetObjects<FireActor>())
        {
            if (fire.IsDeleted) continue;
            if (fire.State != FireState.Standing || fire.ObjTimer != 2) continue;
            if (!IsWithinDistance(fire, 16)) continue;

            return true;
        }
        return false;
    }

    private bool CheckCover()
    {
        if (Interactable.Interaction != Interaction.Cover) return false;
        if (!Game.World.Player.DoesCover(this)) return false;

        return true;
    }


    public override void Draw()
    {
        _raft?.Draw();
        _push?.Draw();
    }
}

internal sealed class PushInteraction
{
    private int _pushTimer;

    private readonly Game _game;
    private readonly InteractiveGameObjectActor _interactive;
    private readonly InteractiveGameObject _gameObject;
    private readonly int _timerLimit;
    private readonly bool _allowHorizontal;
    private readonly bool _requireAlignment;
    private readonly bool _removeTile;
    private readonly bool _movesBlock;
    private Point _targetPos;
    private readonly Point _originalPosition;
    private bool _isMoving;
    private bool _isDone;
    private MovingBlockActor? _movingActor;

    public PushInteraction(Game game, InteractiveGameObjectActor interactive)
    {
        _game = game;
        _interactive = interactive;
        _gameObject = interactive.GameObject;
        _originalPosition = _interactive.Position;
        _allowHorizontal = true;
        _requireAlignment = true;
        _removeTile = true;
        _movesBlock = true;

        switch (_gameObject.Interaction.Interaction)
        {
            case Interaction.Push:
                _timerLimit = 17;
                break;

            case Interaction.PushVertical:
                _timerLimit = 17;
                _allowHorizontal = false;
                break;

            case Interaction.Touch:
                _timerLimit = 1;
                _requireAlignment = false;
                _removeTile = false;
                _movesBlock = false;
                break;

            case Interaction.TouchOnce:
                _timerLimit = 1;
                _requireAlignment = false;
                _movesBlock = false;
                break;

            default: throw new Exception();
        }
    }

    public static PushInteraction? Create(Game game, InteractiveGameObjectActor interactive)
    {
        if (interactive.GameObject.Interaction.Interaction
            is Interaction.Push or Interaction.PushVertical or Interaction.Touch or Interaction.TouchOnce)
        {
            return new PushInteraction(game, interactive);
        }

        return null;
    }

    public bool Check()
    {
        if (_movingActor != null)
        {
            return _movingActor.HasFinishedMoving;
        }

        var dir = _game.Player.MovingDirection;

        if ((!_allowHorizontal && dir.IsHorizontal()) || dir == Direction.None)
        {
            _pushTimer = 0;
            return false;
        }

        var playerX = _game.Player.X;
        var playerY = _game.Player.Y + 3;
        var pushed = false;

        if (!_requireAlignment)
        {
            pushed = dir.IsVertical()
                ? _interactive.X == playerX && Math.Abs(_interactive.Y - playerY) <= World.BlockHeight
                : _interactive.Y == playerY && Math.Abs(_interactive.X - playerX) <= World.BlockWidth;
        }
        else
        {
            // Not my favorite way to do this, but it's not terrible either.
            if (_interactive.IsWithinBoundsInclusive(playerX, playerY, _gameObject.Width, _gameObject.Height))
            {
                var goingTo = _game.Player.Position + dir.GetOffset();
                var collides = _game.Player.CollidesWithTileMoving(goingTo.X, goingTo.Y, dir);
                pushed = collides;
            }
        }

        if (!pushed)
        {
            _pushTimer = 0;
            return false;
        }

        if (!_interactive.IsMovingToward(_game.Player, dir)) return false;

        _pushTimer++;
        if (_pushTimer == _timerLimit)
        {
            _targetPos = dir switch
            {
                Direction.Right => new Point(_interactive.X + _gameObject.Width, 0),
                Direction.Left => new Point(_interactive.X - _gameObject.Width, 0),
                Direction.Down => new Point(0, _interactive.Y + _gameObject.Height),
                Direction.Up => new Point(0, _interactive.Y - _gameObject.Height),
                _ => _targetPos
            };
            // _game.World.SetMapObjectXY(_interactive.X, _interactive.Y, FloorMob1);
            _interactive.Facing = dir;

            // _log.Write(nameof(UpdateIdle), $"Moving {X:X2},{Y:X2} TargetPos:{_targetPos}, dir:{dir}");
            if (_movesBlock)
            {
                var block = _interactive.Interactable.ApparanceBlock;
                if (block != null)
                {
                    _movingActor = new MovingBlockActor(
                        _game, ObjType.Block, block.Value, _targetPos,
                        _interactive.X, _interactive.Y, _gameObject.Width, _gameObject.Height)
                    {
                        Facing = dir,
                        EnableDraw = true,
                    };
                    _game.World.AddObject(_movingActor);
                }
                _isMoving = true;
                return false;
            }

            if (_removeTile)
            {
                var tile = _game.World.CurrentRoom.RoomInformation.FloorTile;
                _game.World.SetMapObjectXY(_interactive.X, _interactive.Y, tile);
            }

            _pushTimer = 0;
            return true;
        }

        return false;
    }

    // private bool UpdateMove()
    // {
    //     if (_isDone || _movingActor == null) return true;
    //     _movingActor.MoveDirection(0x20, _movingActor.Facing);
    //
    //     _isDone = _movingActor.Facing.IsHorizontal()
    //         ? _movingActor.X == _targetPos
    //         : _movingActor.Y == _targetPos;
    //
    //     // _log.Write(nameof(UpdateMoving), $"{X:X2},{Y:X2} done:{done}");
    //
    //     return _isDone;
    //
    //     // if (done)
    //     {
    //         return true;
    //         // _game.World.OnPushedBlock();
    //         // _game.World.SetMapObjectXY(_interactive.X, _interactive.Y, BlockMob);
    //         // _game.World.SetMapObjectXY(_originalPosition.X, _originalPosition.Y, FloorMob2);
    //         // Delete();
    //     }
    // }

    public void Draw()
    {
        // if (!_isMoving) return;
        //
        // var block = _interactive.Interactable.ApparanceBlock;
        // if (block == null) return;
        //
        // var sheet = _game.World.IsOverworld() ? TileSheet.BackgroundOverworld : TileSheet.BackgroundUnderworld;
        // var palette = _game.World.CurrentRoom.RoomInformation.InnerPalette;
        // Graphics.DrawStripSprite16X16(sheet, block.Value, _interactive.X, _interactive.Y, palette);
    }
}

internal sealed class RaftInteraction
{
    private readonly Game _game;
    private readonly InteractiveGameObjectActor _interactive;
    private readonly Raft _raft;
    private readonly InteractiveGameObject _gameObject;
    private readonly SpriteImage _raftImage;
    private readonly Point _raftOpposite;

    private Direction? _raftDirection;

    public RaftInteraction(Game game, InteractiveGameObjectActor interactive)
    {
        _game = game;
        _interactive = interactive;
        _raft = interactive.GameObject.Interaction.Raft ?? throw new Exception();
        _gameObject = interactive.GameObject;
        _raftImage = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.Raft);

        _raftOpposite = _raft.Direction switch
        {
            Direction.Up => new Point(_gameObject.X, _game.World.PlayAreaRect.Y),
            Direction.Down => new Point(_gameObject.X, _game.World.PlayAreaRect.Bottom - World.BlockHeight),
            Direction.Left => new Point(_game.World.PlayAreaRect.X, _gameObject.Y),
            Direction.Right => new Point(_game.World.PlayAreaRect.Right - World.BlockHeight, _gameObject.Y),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static RaftInteraction? Create(Game game, InteractiveGameObjectActor interactive)
    {
        if (interactive.GameObject.Interaction.Raft != null)
        {
            return new RaftInteraction(game, interactive);
        }
        return null;
    }

    public bool Update()
    {
        var goOpposite = false;
        if (_raftDirection == null)
        {
            var doesOppositeCover = _game.World.Player.DoesCover(_raftOpposite.X, _raftOpposite.Y);

            if (!_game.World.Player.DoesCover(_interactive) && !doesOppositeCover)
            {
                return false;
            }

            goOpposite = doesOppositeCover;
        }

        var player = _game.Player;

        // JOE: TODO: This still always assumes up == from dock, down == back to dock.
        switch (_raftDirection)
        {
            case null:
                _raftDirection = goOpposite ? _raft.Direction.GetOppositeDirection() : _raft.Direction;

                _interactive.Y = player.Y + 6;
                _interactive.X = player.X;

                player.SetState(PlayerState.Paused);
                player.Facing = _raftDirection.Value;
                _game.Sound.PlayEffect(SoundEffect.Secret);
                break;

            case Direction.Down:
                _interactive.Y++;
                player.Y++;

                if (player.Y == _gameObject.Y + World.TileMapBaseY)
                {
                    player.TileOffset = 2;
                    player.SetState(PlayerState.Idle);
                    _raftDirection = null;
                }

                player.Animator.Advance();
                break;

            case Direction.Up:
                _interactive.Y--;
                player.Y--;

                if (player.Y == World.TileMapBaseY - 3)
                {
                    _game.World.LeaveRoom(player.Facing, _game.World.CurrentRoom);
                    player.SetState(PlayerState.Idle);
                    _raftDirection = null;
                }

                player.Animator.Advance();
                break;
        }

        return true;
    }

    public void Draw()
    {
        if (_raftDirection != null)
        {
            _raftImage.Draw(TileSheet.PlayerAndItems, _interactive.X, _interactive.Y, Palette.Player);
        }
    }
}