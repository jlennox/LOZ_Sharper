﻿using System.Diagnostics;
using z1.Render;

namespace z1.Actors;

[DebuggerDisplay("{GameObject.Name} ({X},{Y})")]
internal sealed class InteractiveGameObjectActor : Actor
{
    private const int MaxSpawnCount = 16;

    public InteractableBlock Interactable => GameObject.Interaction;
    public InteractiveGameObject GameObject { get; }

    private bool HasInteracted => (_state.HasInteracted && Interactable.Persisted)
        || _hasInteracted
        || Interactable.Interaction == Interaction.None;

    private readonly ObjectState _state;
    private readonly RaftInteraction? _raft;
    private readonly PushInteraction? _push;
    private bool _hasSoundPlayed;
    private bool _hasInteracted;

    public InteractiveGameObjectActor(Game game, InteractiveGameObject gameObject)
        : base(game, ObjType.Block, gameObject.X, gameObject.Y + World.TileMapBaseY)
    {
        GameObject = gameObject;
        _state = game.World.Profile.GetObjectFlags(game.World.CurrentRoom, gameObject);
        Decoration = 0;

        _raft = RaftInteraction.Create(game, this);
        _push = PushInteraction.Create(game, this);

        if (HasInteracted) SetInteracted(true);
    }

    public void DebugSetInteracted() => SetInteracted(false);

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
            var flags = Interactable.Item.IsRoomItem ? ItemObjActorOptions.IsRoomItem : ItemObjActorOptions.None;
            var itemActor = new ItemObjActor(Game, itemId, flags, X, Y);
            itemActor.OnTouched += _ => _state.ItemGot = true;
            Game.World.AddObject(itemActor);
            OptionalSound();
        }

        if (Interactable.SpawnedType != null && Interactable.SpawnedType != ObjType.None)
        {
            var count = Game.World.GetObjects().Count(t => t.ObjType == Interactable.SpawnedType.Value);
            if (count < MaxSpawnCount)
            {
                Game.World.MakeActivatedObject(
                    Interactable.SpawnedType.Value,
                    X / World.TileWidth, Y / World.TileHeight - World.BaseRows);
            }
        }

        if (Interactable.Effect.HasFlag(InteractionEffect.OpenShutterDoors))
        {
            OptionalSound();
            Game.World.OpenShutters();
        }
    }

    private void UpdateCaveEntrance()
    {
        var caveEntrance = Interactable.Entrance;
        if (!caveEntrance.IsValid()) return;
        if (Game.World.WhirlwindTeleporting != 0) return;
        if (Game.World.GetMode() != GameMode.Play) return;
        // JOE: Arg. I don't like the FromUnderground check too much. The value
        // is unset inside CheckWater, which is not at all intuitive.
        if (Game.World.FromUnderground != 0) return;

        if (!Game.World.Player.DoesCover(this)) return;
        Game.World.GotoStairs(TileBehavior.Cave, caveEntrance, _state);
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
        var requirement = Interactable.ItemRequirement;
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
    }
}

internal sealed class PushInteraction
{
    private int _pushTimer;

    private readonly Game _game;
    private readonly InteractiveGameObjectActor _interactive;
    private readonly int _width;
    private readonly int _height;
    private readonly int _timerLimit;
    private readonly bool _allowHorizontal;
    private readonly bool _requireAlignment;
    private readonly bool _removeBackground;
    private readonly bool _movesBlock;
    private MovingBlockActor? _movingActor;

    public PushInteraction(
        Game game, InteractiveGameObjectActor interactive,
        Interaction interaction, int width, int height)
    {
        _game = game;
        _interactive = interactive;
        _width = width;
        _height = height;
        _allowHorizontal = true;
        _requireAlignment = true;
        _removeBackground = true;
        _movesBlock = true;

        switch (interaction)
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
                _removeBackground = false;
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
        var gameobj = interactive.GameObject;
        if (gameobj.Interaction.Interaction
            is Interaction.Push or Interaction.PushVertical or Interaction.Touch or Interaction.TouchOnce)
        {
            return new PushInteraction(game, interactive, gameobj.Interaction.Interaction, gameobj.Width, gameobj.Height);
        }

        return null;
    }

    public bool Check()
    {
        if (_movingActor != null) return _movingActor.HasFinishedMoving;

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
            if (_interactive.IsWithinBoundsInclusive(playerX, playerY, _width, _height))
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
            // This is all kind of complicated but here's how the original game behaves:
            //
            // - When the object begins to move:
            //  - Nothing shows on the ground below it (_removeBackground).
            //  - The moving object is displayed with color 0 being transparent (MovingBlockActor).
            // - Once it's completed moving:
            //  - It's displayed once again as a background with no transparency (ReplaceWithBackground).
            //  - Now what's under it appears (return _movingActor.HasFinishedMoving).

            _interactive.Facing = dir;

            if (_removeBackground)
            {
                var tile = _game.World.CurrentRoom.RoomInformation.FloorTile;
                _game.World.SetMapObjectXY(_interactive.X, _interactive.Y, tile);
            }

            if (_movesBlock)
            {
                var targetPos = dir switch
                {
                    Direction.Right => new Point(_interactive.X + _width, 0),
                    Direction.Left => new Point(_interactive.X - _width, 0),
                    Direction.Down => new Point(0, _interactive.Y + _height),
                    Direction.Up => new Point(0, _interactive.Y - _height),
                    _ => new Point(_interactive.X, _interactive.Y)
                };

                var block = _interactive.Interactable.ApparanceBlock;
                if (block == null) return true;

                _movingActor = new MovingBlockActor(
                    _game, ObjType.Block, block.Value, targetPos, MovingBlockActorOptions.ReplaceWithBackground,
                    _interactive.X, _interactive.Y, _width, _height)
                {
                    Facing = dir,
                    EnableDraw = true,
                };
                _game.World.AddObject(_movingActor);
                return false;
            }

            _pushTimer = 0;
            return true;
        }

        return false;
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

                // Player has reached the dock.
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

                // Player has reached the top of the map.
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