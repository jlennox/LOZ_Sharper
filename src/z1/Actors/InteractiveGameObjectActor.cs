using z1.Render;

namespace z1.Actors;

// The runtime state for InteractableBlock.
internal sealed class InteractiveGameObjectActor : Actor
{
    public InteractableBlock Interactable => _obj.Interaction;

    private bool HasInteracted => _state.HasInteracted || Interactable.Interaction == Interaction.None;

    private readonly InteractiveGameObject _obj;
    private readonly ObjectState _state;
    private readonly SpriteImage _raftImage;

    public InteractiveGameObjectActor(Game game, InteractiveGameObject obj)
        : base(game, ObjType.Item, obj.X, obj.Y + World.TileMapBaseY)
    {
        _obj = obj;
        _state = game.World.Profile.GetObjectFlags(game.World.CurrentWorld, game.World.CurrentRoom, obj);
        Decoration = 0;

        _raftImage ??= Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.Raft);

        if (HasInteracted) SetInteracted(true);
    }

    public override void Update()
    {
        if (HasInteracted)
        {
            UpdateCaveEntrance();
            UpdateRaft();
            return;
        }

        if (UpdateBombable() || UpdateBurnable() || UpdateRaft())
        {
            _state.HasInteracted = true;
        }
    }

    private void SetInteracted(bool initializing)
    {
        _state.HasInteracted = true;

        if (Interactable.Entrance.IsValid())
        {
            Game.World.SetMapObjectXY(X, Y, BlockObjType.Cave);
            if (!initializing)
            {
                Game.Sound.PlayEffect(SoundEffect.Secret);
                Game.World.Profile.Statistics.OWBlocksBombed++;
            }
        }

        if (Interactable.Raft != null)
        {
            Game.World.SetMapObjectXY(X, Y, BlockObjType.Dock);
            if (!initializing)
            {
                Game.Sound.PlayEffect(SoundEffect.Secret);
                Game.World.Profile.Statistics.OWBlocksBombed++;
            }
        }

        if (Interactable.Item != null && !_state.ItemGot)
        {
            var itemId = Interactable.Item.Item;
            if (!initializing)
            {
                Game.Sound.PlayEffect(SoundEffect.Secret);
            }
        }
    }

    private void UpdateCaveEntrance()
    {
        var caveEntrance = Interactable.Entrance;
        if (!caveEntrance.IsValid()) return;
        if (Game.World.WhirlwindTeleporting != 0) return;
        if (Game.World.GetMode() != GameMode.Play) return;

        if (!Game.World.Player.DoesCover(this)) return;
        if (!Game.World.Player.DoesCover(this)) return;
        Game.World.GotoStairs(TileBehavior.Cave, caveEntrance);
    }

    private bool UpdateBombable()
    {
        if (Interactable.Interaction != Interaction.Bomb) return false;

        foreach (var bomb in Game.World.GetObjects<BombActor>())
        {
            if (bomb.IsDeleted || bomb.BombState != BombState.Blasting) continue;
            if (!IsWithinDistance(bomb, 16)) continue;

            SetInteracted(false);
            return true;
        }
        return false;
    }

    private bool UpdateBurnable()
    {
        foreach (var fire in Game.World.GetObjects<FireActor>())
        {
            if (fire.IsDeleted) continue;
            if (fire.State != FireState.Standing || fire.ObjTimer != 2) continue;
            if (!IsWithinDistance(fire, 16)) continue;

            SetInteracted(false);
            return true;
        }
        return false;
    }

    private Direction? _raftDirection;

    private bool UpdateRaft()
    {
        if (Interactable.Raft == null) return false;
        if (Game.World.GetItem(ItemSlot.Raft) == 0) return false;
        if (_raftDirection == null && !Game.World.Player.DoesCover(this)) return false;

        var player = Game.Link;

        switch (_raftDirection)
        {
            case null:
                _raftDirection = Interactable.Raft.Direction;

                Y = player.Y + 6;
                X = player.X;

                player.SetState(PlayerState.Paused);
                player.Facing = Interactable.Raft.Direction;
                Game.Sound.PlayEffect(SoundEffect.Secret);
                break;

            case Direction.Down:
                Y++;
                player.Y++;

                if (player.Y == _obj.Y)
                {
                    player.TileOffset = 2;
                    player.SetState(PlayerState.Idle);
                    _raftDirection = null;
                }

                player.Animator.Advance();
                break;

            case Direction.Up:
                Y--;
                player.Y--;

                if (player.Y == World.TileMapBaseY - 3)
                {
                    Game.World.LeaveRoom(player.Facing, Game.World.CurrentRoom);
                    player.SetState(PlayerState.Idle);
                    _raftDirection = null;
                }

                player.Animator.Advance();
                break;
        }

        return true;
    }

    public override void Draw()
    {
        if (_state.HasInteracted)
        {
            if (Interactable.Item != null && !_state.ItemGot)
            {
                var itemId = Interactable.Item.Item;
                GlobalFunctions.DrawItem(Game, itemId, X, Y, 16); // 16 isn't right here.
            }
        }

        if (_raftDirection != null)
        {
            _raftImage.Draw(TileSheet.PlayerAndItems, X, Y, Palette.Player);
        }
    }
}