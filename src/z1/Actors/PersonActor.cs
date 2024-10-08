using System.Collections.Immutable;
using z1.IO;
using z1.Render;

namespace z1.Actors;

internal sealed class PersonActor : Actor
{
    public const int PersonWallY = 0x8E;

    private enum PersonState
    {
        Idle,
        PickedUp,
        WaitingForLetter,
        WaitingForFood,
        WaitingForStairs,
    }

    private static readonly DebugLog _log = new(nameof(PersonActor));

    private readonly record struct ItemLocation(int X, int Y, int PriceX, int PriceY);

    private static readonly ImmutableArray<ItemLocation> _defaultItemLocations = [
        new ItemLocation(0x58, 0x98, 0x48, 0xB0),
        new ItemLocation(0x78, 0x98, 0x68, 0xB0),
        new ItemLocation(0x98, 0x98, 0x88, 0xB0)
    ];

    private static readonly ImmutableArray<ItemGraphics> _personGraphics = [
        new ItemGraphics(AnimationId.OldMan,   Palette.Red),
        new ItemGraphics(AnimationId.OldWoman, Palette.Red),
        new ItemGraphics(AnimationId.Merchant, Palette.Player),
        new ItemGraphics(AnimationId.Moblin,   Palette.Red)
    ];

    private PersonType PersonType => _spec.PersonType;

    private PersonState _state = PersonState.Idle;
    private readonly ObjectState _objectState;
    private readonly SpriteImage? _image;

    private readonly CaveSpec _spec; // Do not make readonly to avoid struct copies.
    private readonly TextBox _textBox;
    private int _chosenIndex;
    private bool _showNumbers;
    private readonly ImmutableArray<ItemLocation> _itemLocations;

    private readonly byte[] _gamblingAmounts = new byte[3];
    private readonly byte[] _gamblingIndexes = new byte[3];
    private readonly string[] _priceStrings = [];

    public override bool ShouldStopAtPersonWall => true;
    public override bool IsUnderworldPerson => true;

    // Arg. Sometimes "CaveId" is an ObjType.Person1-end :/
    // This code got to be a pretty big mess but I'm hoping a mapping format rewrite can clean that up.
    public PersonActor(Game game, ObjectState? state, CaveId type, CaveSpec spec, int x, int y)
        : base(game, (ObjType)type, x, y)
    {
        // We operate on a clone of it because we modify it to keep track of the state of this instance.
        _spec = spec.Clone();
        // If it's not a persisted, create an ephemeral state.
        _objectState = (spec.IsPersisted ? state : null) ?? new ObjectState();
        HP = 0;
        // This isn't used anymore. The effect is implemented a different way.
        // Game.World.SetPersonWallY(0x8D);

        // Room has been previously paid for. Clear it out.
        if (_objectState.ItemGot)
        {
            Delete();
            return;
        }

        if (!Game.World.IsOverworld())
        {
            Game.Sound.PlayEffect(SoundEffect.Item);
        }

        _itemLocations = spec.Items?.Length switch
        {
            1 => [_defaultItemLocations[1]],
            2 => [_defaultItemLocations[0], _defaultItemLocations[^1]],
            _ => _defaultItemLocations,
        };

        if (spec.Items != null)
        {
            const ItemObjActorOptions itemOptions = ItemObjActorOptions.DoNotSpawnIn | ItemObjActorOptions.LiftItem;
            for (var i = 0; i < spec.Items.Length; ++i)
            {
                var caveItem = spec.Items[i];
                var location = _itemLocations[i];
                var item = game.World.AddItem(caveItem.ItemId, location.X, location.Y, itemOptions) as ItemObjActor;
                if (item == null) continue;
                var itemIndex = i;
                item.OnTouched += _ => HandlePlayerHit(caveItem, itemIndex);
            }
        }

        if (spec.HasEntranceCheck)
        {
            var checkItem = spec.EntranceCheckItem ?? throw new Exception("EntranceCheckItem is unset.");
            var checkAmount = spec.EntranceCheckAmount ?? throw new Exception("EntranceCheckAmount is unset.");
            var item = game.World.GetItem(checkItem);
            if (item >= checkAmount)
            {
                _objectState.ItemGot = true;
                return;
            }
        }

        if (spec.HasEntranceCost)
        {
            var checkItem = spec.EntranceCheckItem ?? throw new Exception("EntranceCheckItem is unset.");
            var checkAmount = spec.EntranceCheckAmount ?? throw new Exception("EntranceCheckAmount is unset.");
            if (checkItem == ItemSlot.Rupees)
            {
                game.World.PostRupeeLoss(checkAmount);
                _objectState.ItemGot = true;
                return;
            }

            // JOE: TODO
            throw new Exception($"EntranceCost is not implemented for this item {checkItem}.");
        }

        var animIndex = (ObjType)spec.DwellerType - ObjType.OldMan;
        var animId = _personGraphics[animIndex].AnimId;
        _image = new SpriteImage(TileSheet.PlayerAndItems, animId);
        if (spec.Text != null)
        {
            _textBox = new TextBox(Game, spec.Text);
        }

        if ((spec.ShowNumbers || spec.IsSpecial) && spec.Items != null)
        {
            var negative = spec.ShowNegative;
            _priceStrings = spec.Items.Select(t =>
            {
                var neg = negative || t.ShowNegative ? NumberSign.Negative : NumberSign.None;
                return GlobalFunctions.NumberToString(t.Cost, neg);
            }).ToArray();
        }

        if (PersonType == PersonType.Gambling)
        {
            InitGambling();
        }

        // JOE: TODO: Move this
        if (type == CaveId.Cave11MedicineShop)
        {
            var itemValue = Game.World.GetItem(ItemSlot.Letter);
            _state = itemValue == 2 ? PersonState.Idle : PersonState.WaitingForLetter;
        }

        if (_state == PersonState.Idle)
        {
            Game.Player.SetState(PlayerState.Paused);
        }
    }

    public override bool Delete()
    {
        if (base.Delete())
        {
            if (_spec.DoesControlsBlockingWall) Game.World.SetPersonWallY(0);
            if (_spec.DoesControlsShutters) Game.World.OpenShutters();
            return true;
        }
        return false;
    }

    public override void Update()
    {
        switch (_state)
        {
            case PersonState.Idle:
                UpdateDialog();
                // CheckPlayerHit();

                if (!Game.World.IsOverworld())
                {
                    CheckCollisions();
                    if (Decoration != 0)
                    {
                        Decoration = 0;
                        Game.World.EnablePersonFireballs = true;
                    }
                }
                break;

            case PersonState.PickedUp: UpdatePickUp(); break;
            case PersonState.WaitingForLetter: UpdateWaitForLetter(); break;
            case PersonState.WaitingForFood: UpdateWaitForFood(); break;
            case PersonState.WaitingForStairs: CheckStairsHit(); break;
            default: throw new Exception($"Invalid state: {_state}");
        }
    }

    private void UpdateDialog()
    {
        if (_textBox == null) throw new Exception();
        if (_textBox.IsDone()) return;

        _textBox.Update();

        if (_textBox.IsDone())
        {
            switch (PersonType)
            {
                case PersonType.DoorRepair:
                    Game.World.PostRupeeLoss(20);
                    _objectState.ItemGot = true;
                    break;

                case PersonType.Grumble:
                    _state = PersonState.WaitingForFood;
                    break;

                case PersonType.CaveShortcut:
                    _state = PersonState.WaitingForStairs;
                    break;
            }

            var player = Game.Player;
            if (player.GetState() == PlayerState.Paused)
            {
                player.SetState(PlayerState.Idle);
            }
        }
    }

    // private void CheckPlayerHit()
    // {
    //     if (_spec.Items == null) return;
    //     if (!_spec.IsPay) return;
    //
    //     var player = Game.Player;
    //
    //     for (var i = 0; i < _spec.Items.Length; i++)
    //     {
    //         var item = _spec.Items[i];
    //         var itemId = item.ItemId;
    //         var location = _itemLocations[i];
    //
    //         var distanceY = Math.Abs(location.Y - player.Y);
    //         if (distanceY >= 6) continue;
    //
    //         if (itemId != ItemId.None && player.X == location.X)
    //         {
    //             // HandlePlayerHit(item, i);
    //             break;
    //         }
    //     }
    // }

    private bool HandlePlayerHit(CaveShopItem item, int index)
    {
        if (item.HasOption(CaveShopItemOptions.CheckCost))
        {
            var actual = Game.World.GetItem(item.Costing);
            if (actual < item.Cost)
            {
                _log.Write($"Failed check: {actual} < {item.Costing} for {item.ItemId}.");
                return false;
            }
        }

        if (_spec.IsPay)
        {
            var actual = Game.World.GetItem(item.Costing);
            if (item.Cost > actual)
            {
                _log.Write($"Failed pay: {actual} < {item.Costing} for {item.ItemId}.");
                return false;
            }

            // JOE: TODO: I want to do positives this way too.
            if (item.Costing == ItemSlot.Rupees)
            {
                Game.World.PostRupeeLoss(item.Cost);
            }
            else
            {
                var newValue = actual - item.Cost;
                // This is to emulate the zombie player game behavior.
                if (item.Costing == ItemSlot.HeartContainers && newValue <= PlayerProfile.DefaultHeartCount)
                {
                    Game.World.Profile.Hearts = 0;
                }
                else
                {
                    Game.World.SetItem(item.Costing, actual - item.Cost);
                }
            }
        }

        if (!_spec.ShowNumbers)
        {
            _objectState.ItemGot = true;
        }

        if (HandlePickUpHint(item))
        {
            return true;
        }

        if (_spec.IsSpecial)
        {
            HandlePickUpSpecial(item, index);
        }
        else
        {
            HandlePickUpItem(item);
        }

        return true;
    }

    private void HandlePickUpItem(CaveShopItem item)
    {
        HandlePickUpHint(item);

        // JOE: NOTE: This should ultimately go...? Or it needs to handle all the conditions.
        Game.World.AddItem(item.ItemId, item.ItemAmount);

        if (item.FillItem != null)
        {
            var max = Game.World.Profile.GetMax(item.FillItem.Value);
            Game.World.SetItem(item.FillItem.Value, max);
        }
        // ChosenIndex = index;
        if (_spec.IsPickUp)
        {
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
            Game.World.LiftItem(item.ItemId);
            Game.Sound.PushSong(SongId.ItemLift);
        }
        _spec.ClearOptions(CaveSpecOptions.ShowNumbers);
        Game.AutoSave();
    }

    private bool HandlePickUpHint(CaveShopItem item)
    {
        if (item.Hint == null) return false;

        _textBox.Reset(item.Hint);

        _spec.ClearOptions(CaveSpecOptions.ShowNumbers);
        _spec.ClearOptions(CaveSpecOptions.PickUp);
        return true;
    }

    private void HandlePickUpSpecial(CaveShopItem item, int index)
    {
        if (PersonType == PersonType.Gambling)
        {
            var price = item.Cost;
            if (price > Game.World.GetItem(ItemSlot.Rupees)) return;

            int finalIndex;

            for (var i = 0; i < _gamblingIndexes.Length; i++)
            {
                finalIndex = _gamblingIndexes[i];
                var sign = finalIndex != 2 ? NumberSign.Negative : NumberSign.Positive;
                _priceStrings[i] = GlobalFunctions.NumberToString(_gamblingAmounts[finalIndex], sign);
            }

            _spec.ClearOptions(CaveSpecOptions.PickUp);
            finalIndex = _gamblingIndexes[index];

            if (finalIndex == 2)
            {
                Game.World.PostRupeeWin(_gamblingAmounts[finalIndex]);
            }
            else
            {
                Game.World.PostRupeeLoss(_gamblingAmounts[finalIndex]);
            }

            return;
        }
        //
        // if (PersonType == PersonType.MoreBombs)
        // {
        //     var price = item.Cost;
        //     if (price > Game.World.GetItem(ItemSlot.Rupees)) return;
        //
        //     Game.World.PostRupeeLoss(price);
        //     var profile = Game.World.Profile;
        //     profile.Items[ItemSlot.MaxBombs] += 4;
        //     profile.Items[ItemSlot.Bombs] = profile.Items[ItemSlot.MaxBombs];
        //
        //     _showNumbers = false;
        //     _state = PersonState.PickedUp;
        //     ObjTimer = 0x40;
        //     return;
        // }
        //
        // if (PersonType == PersonType.MoneyOrLife)
        // {
        //     var price = item.Cost;
        //     var itemId = item.ItemId;
        //
        //     if (itemId == ItemId.Rupee)
        //     {
        //         if (price > Game.World.GetItem(ItemSlot.Rupees)) return;
        //
        //         Game.World.PostRupeeLoss(price);
        //     }
        //     else if (itemId == ItemId.HeartContainer)
        //     {
        //         if (price > Game.World.GetItem(ItemSlot.HeartContainers)) return;
        //
        //         var profile = Game.World.Profile;
        //         if (profile.Items[ItemSlot.HeartContainers] <= PlayerProfile.DefaultHeartCount)
        //         {
        //             // This is to emulate the zombie player game behavior.
        //             profile.Hearts = 0;
        //         }
        //         else
        //         {
        //             profile.Items[ItemSlot.HeartContainers]--;
        //             if (profile.Hearts > 0x100)
        //             {
        //                 profile.Hearts -= 0x100;
        //             }
        //             Game.Sound.PlayEffect(SoundEffect.KeyHeart);
        //         }
        //     }
        //     else
        //     {
        //         return;
        //     }
        //
        //     _objectState.ItemGot = true;
        //     Game.World.OpenShutters();
        //
        //     _showNumbers = false;
        //     _state = PersonState.PickedUp;
        //     ObjTimer = 0x40;
        //     return;
        // }

        // Give money
        var amount = item.Cost;

        Game.World.PostRupeeWin(amount);
        _objectState.ItemGot = true;
        _spec.ClearOptions(CaveSpecOptions.PickUp);
        _showNumbers = true;
    }

    private void AddItem(CaveShopItem item)
    {
        // JOE: TODO: This has a lot more work needed.
        var set = item.HasOption(CaveShopItemOptions.SetItem);
        if (!World.ItemToEquipment.TryGetValue(item.ItemId, out var val)) return;
        if (set)
        {
            Game.World.SetItem(val.Slot, item.ItemAmount);
            return;
        }

        Game.World.AddItem(item.ItemId);
    }

    private void InitGambling()
    {
        for (var i = 0; i < _gamblingIndexes.Length; i++)
        {
            _gamblingIndexes[i] = (byte)i;
        }

        _gamblingAmounts[0] = (byte)(Game.Random.Next(2) == 0 ? 10 : 40);
        _gamblingAmounts[1] = 10;
        _gamblingAmounts[2] = (byte)(Game.Random.Next(2) == 0 ? 20 : 50);

        Game.Random.Shuffle(_gamblingIndexes);
    }

    private void UpdatePickUp()
    {
        if (ObjTimer != 0) return;

        Delete();

        if (ObjType == ObjType.Grumble)
        {
            _objectState.ItemGot = true;
            Game.World.SetItem(ItemSlot.Food, 0);
            Game.World.SetPersonWallY(0);

            var food = Game.World.GetObject<FoodActor>();
            food?.Delete();
        }
    }

    private void UpdateWaitForLetter()
    {
        var itemValue = Game.World.GetItem(ItemSlot.Letter);
        if (itemValue == 2)
        {
            _state = PersonState.Idle;
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
    }

    private void UpdateWaitForFood()
    {
        var food = Game.World.GetObject<FoodActor>();
        if (food != null)
        {
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
    }

    private void CheckStairsHit()
    {
        ReadOnlySpan<byte> stairsXs = [0x50, 0x80, 0xB0];

        if (Game.Player.Y != 0x9D) return;

        var rooms = Game.World.GetShortcutRooms();
        var playerX = Game.Player.X;
        var stairsIndex = -1;

        for (var i = 0; i < stairsXs.Length; i++)
        {
            if (playerX == stairsXs[i])
            {
                stairsIndex = i;
                break;
            }
        }

        if (stairsIndex < 0) return;

        // JOE: I made a lot of sus changes here.
        for (var j = 0; j < rooms.Length; j++)
        {
            if (rooms[j] == Game.World.CurrentRoom)
            {
                var index = j + 1 + stairsIndex;
                if (index >= rooms.Length)
                {
                    index -= rooms.Length;
                }

                Game.World.LeaveCellarByShortcut(rooms[index]);
                return;
            }
        }

        throw new Exception($"CheckStairsHit: Unable to locate {Game.World.CurrentRoom.Name} in "); // {string.Join(", ", rooms.ToArray())}");
    }

    public override void Draw()
    {
        switch (_state)
        {
            case PersonState.PickedUp when (Game.FrameCounter & 1) == 0:
                return;

            case PersonState.Idle:
            case PersonState.WaitingForFood:
            case PersonState.WaitingForStairs:
                DrawDialog();
                break;
        }

        if (_image == null) throw new NullReferenceException("_image is null.");

        var animIndex = (ObjType)_spec.DwellerType - ObjType.OldMan;
        var palette = _personGraphics[animIndex].Palette;
        palette = CalcPalette(palette);
        _image.Draw(TileSheet.PlayerAndItems, X, Y, palette);

        if (_state == PersonState.WaitingForLetter) return;

        for (var i = 0; _spec.Items != null && i < _spec.Items.Length; i++)
        {
            var item = _spec.Items[i];
            var location = _itemLocations[i];

            if (_state != PersonState.PickedUp || i != _chosenIndex)
            {
                if (_spec.ShowItems)
                {
                    // GlobalFunctions.DrawItemWide(Game, item.ItemId, location.X, location.Y);
                }
                if (_spec.ShowNumbers || _showNumbers)
                {
                    GlobalFunctions.DrawString(_priceStrings[i], location.PriceX, location.PriceY, 0);
                }
            }
        }

        if (_spec.ShowNumbers)
        {
            GlobalFunctions.DrawItemWide(Game, ItemId.Rupee, 0x30, 0xAC);
            GlobalFunctions.DrawChar(Chars.X, 0x40, 0xB0, 0);
        }
    }

    private void DrawDialog()
    {
        if (_textBox == null) throw new Exception();
        _textBox.Draw();
    }
}