using System.Diagnostics;
using z1.IO;
using z1.Render;

namespace z1.Actors;

// Expectations:
//
// - Both push blocks in level 1 (gel and cellar room) should make secret sound when pushed.
// - Boomerang should make item sound when it appears in level 1.

// The interface is required to allow abstracting between room and block events, which matters in some select scenarios.
internal interface IInteractableActor
{
    bool HasInteracted { get; }
    InteractableBase InteractableBase { get; }
}

[DebuggerDisplay("{Interactable.Name} ({X},{Y})")]
internal abstract partial class InteractableActor<T> : Actor, IInteractableActor
    where T : InteractableBase
{
    protected enum UpdateState { None, HasInteracted, Check }

    public override bool IsMonsterSlot => false;

    public T Interactable { get; }

    bool IInteractableActor.HasInteracted => HasInteracted;
    InteractableBase IInteractableActor.InteractableBase => Interactable;

    protected ObjectState State => _state.Value;
    protected bool HasInteracted => (State.HasInteracted && Interactable.Persisted)
        || _hasInteracted
        || Interactable.Interaction == Interaction.None;

    private bool _hasSoundPlayed;
    private bool _hasInteracted;
    private DeferredEvent? _deferredEvent;
    // This is used for when the code externally sets an interaction. It'll check on the following Update() if all the other criteria are met.
    private bool _hasPerformedInteraction;
    // This is to avoid a virtual call in the initializer.
    // NOTE: This might be better off setting _hasInteracted, and having CheckRequirements pass if that's set.
    // 10/16/2025 NOTE: This appears no longer used?
    private bool _setInteracted;
    // 10/16/2025 NOTE: This, also, appears unused?
    private bool _initializerSetInteracted;
    private bool _hasUpdateRun;

    private bool _checkedForRevealer;
    private IInteractableActor? _revealedBy;

    private readonly Lazy<ObjectState> _state;

    protected InteractableActor(World world, T interactable, int x, int y)
        : base(world, ObjType.Block, x, y + World.TileMapBaseY)
    {
        Interactable = interactable;
        Decoration = 0;

        _state = new Lazy<ObjectState>(() => Interactable.GetObjectState(World));
    }

    public void DebugSetInteracted() => SetInteracted(false);

    public override void Update()
    {
        if (_setInteracted)
        {
            _setInteracted = false;
            SetInteracted(true);
        }

        UpdateCore();
    }

    private bool CheckRevealed()
    {
        if (Interactable.Interaction != Interaction.Revealed) return false;

        if (!_checkedForRevealer)
        {
            _revealedBy = World.GetObjects().OfType<IInteractableActor>()
                .FirstOrDefault(t => t.InteractableBase.Reveals == Interactable.Name)
                ?? throw new Exception($"Unable to locate object to reveal object named \"{Interactable.Name}\" in room \"{World.CurrentRoom.UniqueId}\"");
            _checkedForRevealer = true;
        }

        var revealedBy = _revealedBy ?? throw new Exception($"Revealed object named missing \"{Interactable.Name}\" in room \"{World.CurrentRoom.UniqueId}\"");

        return revealedBy.HasInteracted;
    }

    protected virtual UpdateState UpdateCore()
    {
        if (!CheckRequirements()) return UpdateState.None;
        if (!CheckItemRequirement()) return UpdateState.None;

        if (_deferredEvent != null)
        {
            if (_deferredEvent.IsCompleted)
            {
                _deferredEvent = null;
                SetInteracted(false);
            }
            return UpdateState.None;
        }

        if (!_hasUpdateRun)
        {
            // This used to use _setInteracted, but that failed because things set to None then would not check if the
            // Requirements are met. The idea behind _setInteracted was that it wouldn't check requirements, incase you
            // trigger something, then lose that. IE, later in the quest you lose the item and that's ok. But we can
            // recross this bridge when and if it comes up again.
            if (HasInteracted) SetInteracted(true);
            _hasUpdateRun = true;
        }

        if (_hasPerformedInteraction || _initializerSetInteracted)
        {
            _hasPerformedInteraction = false;
            if (CheckDeferredEvent()) return UpdateState.None;
            SetInteracted(_initializerSetInteracted);
            _initializerSetInteracted = false;
            return UpdateState.HasInteracted;
        }

        if (!HasInteracted)
        {
            if (CheckRevealed())
            {
                SetInteracted(false);
            }
        }

        return HasInteracted ? UpdateState.HasInteracted : UpdateState.Check;
    }

    protected void OptionalSound(bool initializing)
    {
        if (initializing || _hasSoundPlayed) return;
        _hasSoundPlayed = true;
        Game.Sound.PlayEffect(SoundEffect.Secret);
    }

    // Some interactions do not cause the secret to be revealed immediately, and instead are deferred until after a trigger.
    // We have to be careful to keep all of this single threaded and using the normal update loop.
    private bool CheckDeferredEvent()
    {
        if (Interactable.Effect.HasFlag(InteractionEffect.DryoutWater))
        {
            _deferredEvent = World.DryoutWater();
            return true;
        }

        return false;
    }

    protected virtual void SetInteracted(bool initializing)
    {
        _hasInteracted = true;

        if (Interactable.Persisted)
        {
            State.HasInteracted = true;
        }

        if (Interactable.Effect.HasFlag(InteractionEffect.OpenShutterDoors))
        {
            OptionalSound(initializing);
            World.TriggerShutters();
        }
    }

    // The result of this is a bit iffy. At this point, it's designed to know if the recorder should summon the whirlwind.
    public override bool NonTargetedAction(Interaction interaction)
    {
        if (Interactable.Interaction != interaction) return false;
        if (HasInteracted) return true;
        _hasPerformedInteraction = true;
        return true;
    }

    private bool CheckRequirements()
    {
        if (Interactable.Requirements.HasFlag(InteractionRequirements.AllEnemiesDefeated))
        {
            if (World.HasLivingObjects()) return false;
        }

        if (_revealedBy is { HasInteracted: false }) return false;

        return true;
    }

    private bool CheckItemRequirement()
    {
        var requirement = Interactable.ItemRequirement;
        if (requirement == null) return true;
        var actualValue = World.GetItem(requirement.Slot);
        return actualValue >= requirement.Level;
    }
}

internal sealed class RoomInteractionActor : InteractableActor<RoomInteraction>
{
    public RoomInteractionActor(World world, RoomInteraction interactable)
        : base(world, interactable, 0, 0)
    {
    }

    public override void Draw() { }
}

[DebuggerDisplay("{GameObject.Name} ({X},{Y})")]
internal sealed partial class InteractableBlockActor : InteractableActor<InteractableBlock>
{
    private const int _maxSpawnCount = 16;

    public InteractableBlockObject GameObject { get; }

    private readonly RaftInteraction? _raft;
    private readonly PushInteraction? _push;
    private Actor? _stillSpawningActor;

    public InteractableBlockActor(World world, InteractableBlockObject gameObject)
        : base(world, gameObject.Interaction, gameObject.X, gameObject.Y)
    {
        GameObject = gameObject;

        _raft = RaftInteraction.Create(world, this);
        _push = PushInteraction.Create(world, this);
    }

    public static Actor Make(World world, InteractableBlockObject block)
    {
        // This is unfortunately needed so that bad guys as they spawn in can see the items to "hold" them
        // when possible in the underworld. This code could really use a cleanup.
        if (block.Interaction.IsItemOnly())
        {
            var options = block.Interaction.Item!.Options;
            if (block.Interaction.Persisted) options |= ItemObjectOptions.Persisted;
            return new ItemObjActor(world, block.Interaction.Name, block.Interaction.Item.Item, options, block.X, block.Y + World.TileMapBaseY);
        }

        return new InteractableBlockActor(world, block);
    }

    protected override UpdateState UpdateCore()
    {
        switch (base.UpdateCore())
        {
            case UpdateState.None:
                return UpdateState.None;

            case UpdateState.HasInteracted:
                UpdateCaveEntrance();
                UpdateSpawnedActorBlockRemoval();
                _raft?.Update();
                return UpdateState.HasInteracted;

            case UpdateState.Check:
                if (CheckBombable() || CheckBurnable() || CheckCover() || (_push?.Check() ?? false))
                {
                    SetInteracted(false);
                    return UpdateState.HasInteracted;
                }
                return UpdateState.Check;
        }

        throw new UnreachableException();
    }

    private void UpdateSpawnedActorBlockRemoval()
    {
        // I'm not the biggest fan of how this works but it's not actually far off from correct.
        // When an armos is spawned, their block stays present until they finish spawning in.
        if (_push == null) return;
        if (_stillSpawningActor == null) return;
        if (_push.BackgroundRemoval != BackgroundRemoval.Deferred) return;

        if (_stillSpawningActor.ObjTimer == 0)
        {
            _push.RemoveBackground();
            _stillSpawningActor = null; // Stop this from being re-entered.
        }
    }

    protected override void SetInteracted(bool initializing)
    {
        base.SetInteracted(initializing);

        if (Interactable.Entrance.IsValid())
        {
            if (Interactable.Entrance.BlockType == BlockType.None)
            {
                World.CurrentRoom.RoomMap.SetBlockBehaviorXY(X, Y, TileBehavior.GenericWalkable);
            }
            else
            {
                World.SetMapObjectXY(X, Y, Interactable.Entrance.BlockType);
            }

            // Move out of entrance check?
            if (!initializing)
            {
                OptionalSound(initializing);
                switch (Interactable.Interaction)
                {
                    case Interaction.Bomb: World.Profile.Statistics.OWBlocksBombed++; break;
                    case Interaction.Burn: World.Profile.Statistics.TreesBurned++; break;
                }
            }
        }

        if (Interactable.Raft != null)
        {
            World.SetMapObjectXY(X, Y, BlockType.Dock);
            OptionalSound(initializing);
        }

        if (Interactable.Item != null && !State.ItemGot)
        {
            var itemId = Interactable.Item.Item;
            var options = Interactable.Item.Options;
            var itemActor = new ItemObjActor(World, itemId, options, X, Y);
            State.ItemId = itemId;
            itemActor.OnTouched += _ => State.ItemGot = true;
            World.AddObject(itemActor);
        }

        if (Interactable.SpawnedType != null && Interactable.SpawnedType != ObjType.None)
        {
            var count = World.GetObjects().Count(t => t.ObjType == Interactable.SpawnedType.Value);
            if (count < _maxSpawnCount)
            {
                _stillSpawningActor = World.MakeActivatedObject(
                    Interactable.SpawnedType.Value,
                    X / World.TileWidth, Y / World.TileHeight - World.BaseRows);
            }
        }
    }

    private void UpdateCaveEntrance()
    {
        var caveEntrance = Interactable.Entrance;
        if (!caveEntrance.IsValid()) return;
        if (World.WhirlwindTeleporting != 0) return;
        if (World.GetMode() != GameMode.Play) return;
        // JOE: Arg. I don't like the FromUnderground check too much. The value
        // is unset inside CheckWater, which is not at all intuitive.
        if (World.Player.FromUnderground) return;

        if (!World.Player.DoesCover(this)) return;
        World.GotoStairs(caveEntrance, State);
    }

    private bool CheckBombable()
    {
        if (Interactable.Interaction != Interaction.Bomb) return false;

        foreach (var bomb in World.GetObjects<BombActor>())
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

        foreach (var fire in World.GetObjects<FireActor>())
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
        if (!World.Player.DoesCover(this)) return false;

        return true;
    }

    public override void Draw()
    {
        _raft?.Draw();
    }
}

public enum BackgroundRemoval { None, Immediate, Deferred }

internal sealed class PushInteraction
{
    private int _pushTimer;

    private readonly World _world;
    private readonly InteractableBlockActor _interactive;
    private readonly int _width;
    private readonly int _height;
    private readonly int _timerLimit;
    private readonly bool _allowHorizontal;
    private readonly bool _requireAlignment;
    public readonly BackgroundRemoval BackgroundRemoval;
    private readonly bool _movesBlock;
    private MovingBlockActor? _movingActor;
    private bool _hasRemovedBackground;

    public PushInteraction(
        World world, InteractableBlockActor interactive,
        Interaction interaction, int width, int height)
    {
        _world = world;
        _interactive = interactive;
        _width = width;
        _height = height;
        _allowHorizontal = true;
        _requireAlignment = true;
        _movesBlock = true;
        BackgroundRemoval = BackgroundRemoval.Immediate;

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
                _movesBlock = false;
                BackgroundRemoval = BackgroundRemoval.None;
                break;

            case Interaction.TouchOnce:
                _timerLimit = 1;
                _requireAlignment = false;
                _movesBlock = false;
                BackgroundRemoval = interactive.Interactable.Repeatable
                    ? BackgroundRemoval.Immediate
                    : BackgroundRemoval.Deferred;
                break;

            default: throw new Exception();
        }
    }

    public static PushInteraction? Create(World world, InteractableBlockActor interactive)
    {
        var gameobj = interactive.GameObject;
        if (gameobj.Interaction.Interaction
            is Interaction.Push or Interaction.PushVertical or Interaction.Touch or Interaction.TouchOnce)
        {
            return new PushInteraction(world, interactive, gameobj.Interaction.Interaction, gameobj.Width, gameobj.Height);
        }

        return null;
    }

    public void RemoveBackground()
    {
        if (_hasRemovedBackground) throw new Exception();
        _hasRemovedBackground = true;

        var tile = _world.CurrentRoom.Settings.FloorTile;
        _world.SetMapObjectXY(_interactive.X, _interactive.Y, tile);
    }

    public bool Check()
    {
        if (_movingActor != null) return _movingActor.HasFinishedMoving;

        var dir = _world.Player.MovingDirection;

        if ((!_allowHorizontal && dir.IsHorizontal()) || dir == Direction.None)
        {
            _pushTimer = 0;
            return false;
        }

        var playerX = _world.Player.X;
        var playerY = _world.Player.Y + 3;
        var pushed = false;

        if (_requireAlignment)
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
                var goingTo = _world.Player.Position + dir.GetOffset();
                var collides = _world.CollidesWithTileMoving(goingTo.X, goingTo.Y, dir, true);
                pushed = collides;
            }
        }

        if (!pushed)
        {
            _pushTimer = 0;
            return false;
        }

        if (!_interactive.IsMovingToward(_world.Player, dir)) return false;

        _pushTimer++;
        if (_pushTimer == _timerLimit)
        {
            // This is all kind of complicated but here's how the original game behaves:
            //
            // When the object begins to move:
            // - Nothing shows on the ground below it (_removeBackground).
            // - The moving object is displayed with color 0 being transparent (MovingBlockActor).
            // Once it's completed moving:
            // - It's displayed once again as a background with no transparency (ReplaceWithBackground).
            // - Now what's under it appears (return _movingActor.HasFinishedMoving).

            _interactive.Facing = dir;

            if (BackgroundRemoval == BackgroundRemoval.Immediate)
            {
                RemoveBackground();
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
                    _world, ObjType.Block, block.Value, targetPos,
                    MovingBlockActorOptions.ReplaceWithBackground,
                    _interactive.X, _interactive.Y, _width, _height)
                {
                    Facing = dir,
                    EnableDraw = true,
                };
                _world.AddObject(_movingActor);
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
    private readonly World _world;
    private readonly InteractableBlockActor _interactive;
    private readonly Raft _raft;
    private readonly InteractableBlockObject _gameObject;
    private readonly SpriteImage _raftImage;
    private readonly Point _raftOpposite;

    private Direction? _raftDirection;

    public RaftInteraction(World world, InteractableBlockActor interactive)
    {
        _world = world;
        _interactive = interactive;
        _raft = interactive.GameObject.Interaction.Raft ?? throw new Exception();
        _gameObject = interactive.GameObject;
        _raftImage = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.Raft);

        _raftOpposite = _raft.Direction switch
        {
            Direction.Up => new Point(_gameObject.X, _world.PlayAreaRect.Y),
            Direction.Down => new Point(_gameObject.X, _world.PlayAreaRect.Bottom - World.BlockHeight),
            Direction.Left => new Point(_world.PlayAreaRect.X, _gameObject.Y),
            Direction.Right => new Point(_world.PlayAreaRect.Right - World.BlockHeight, _gameObject.Y),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static RaftInteraction? Create(World world, InteractableBlockActor interactive)
    {
        if (interactive.GameObject.Interaction.Raft != null)
        {
            return new RaftInteraction(world, interactive);
        }
        return null;
    }

    public bool Update()
    {
        var goOpposite = false;
        if (_raftDirection == null)
        {
            var doesOppositeCover = _world.Player.DoesCover(_raftOpposite.X, _raftOpposite.Y);

            if (!_world.Player.DoesCover(_interactive) && !doesOppositeCover)
            {
                return false;
            }

            goOpposite = doesOppositeCover;
        }

        var player = _world.Player;

        // JOE: TODO: This still always assumes up == from dock, down == back to dock.
        switch (_raftDirection)
        {
            case null:
                _raftDirection = goOpposite ? _raft.Direction.GetOppositeDirection() : _raft.Direction;

                _interactive.Y = player.Y + 6;
                _interactive.X = player.X;

                player.SetState(PlayerState.Paused);
                player.Facing = _raftDirection.Value;
                _world.Game.Sound.PlayEffect(SoundEffect.Secret);
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
                    _world.LeaveRoom(player.Facing, _world.CurrentRoom);
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
            _raftImage.Draw(TileSheet.PlayerAndItems, _interactive.X, _interactive.Y, Palette.Player, DrawOrder.Sprites);
        }
    }
}

internal static class InteractableExtensions
{
    public static ObjectState GetObjectState(this InteractableBase interactable, World world)
    {
        return world.Profile.GetObjectFlags(world.CurrentRoom, interactable.Name);
    }
}