using System.Collections.Immutable;
using z1.IO;
using z1.Render;

namespace z1.Actors;

// Cave specifications:
//
// Hint cave:
// - Shows "{Rupee} X"
// - Prices with negative signs.
// - Rupees show in the 3 item slots.
// - When interacted with:
//   - Item is not lifted overhead.
//   - The prices and the "{Rupee} X" disappear.
//   - The rupee's in the item slots remain present, but can't be interacted with.
//   - The shopkeeper remains.
//
// Shop:
// - Shows "{Rupee} X"
// - Prices have no signage prefix.
// - Bottom of key item aligns to center of two 00's in 100 price tag.
//   IE, the third digit of the text does not center to the item.
// - When interacted with:
//   - Item is lifted overhead.
//   - Shopkeeper and items disappear and "blink" as they do.
//   - Prices/"{Rupee} X" disappear immediately upon purchase.
//
// Gambling/Money Making Game:
// - Shows "{Rupee} X"
// - Prices with negative signs.
// - Rupees show in the 3 item slots.
// - When interacted with:
//   - Item is not lifted overhead.
//   - The prices are updated to reflect the potential winning/losses. All numbers of signed, negative and positive.
//   - The rupee's in the item slots remain present, but can't be interacted with.
//   - The shopkeeper remains.
internal sealed class PersonActor : Actor
{
    public const int PersonWallY = 0x8E;
    private const int DisappearTime = 0x40;

    private enum PersonState
    {
        Idle,
        PickedUp,
        WaitingForItem,
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
    private readonly ImmutableArray<ItemLocation> _itemLocations;

    private readonly byte[] _gamblingAmounts = new byte[3];
    private readonly byte[] _gamblingIndexes = new byte[3];
    private readonly string[] _priceStrings = [];
    private readonly List<ItemObjActor> _itemActors = [];

    public override bool ShouldStopAtPersonWall => true;

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
            var itemOptions = ItemObjectOptions.IsRoomItem;
            if (_spec.IsPickUp) itemOptions |= ItemObjectOptions.LiftOverhead;

            for (var i = 0; i < spec.Items.Length; ++i)
            {
                var caveItem = spec.Items[i];
                var location = _itemLocations[i];
                var item = game.World.AddItemActor(caveItem.ItemId, location.X, location.Y, itemOptions) as ItemObjActor;
                if (item == null) continue;

                _itemActors.Add(item);
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
            }
            else
            {
                // JOE: TODO
                throw new Exception($"EntranceCost is not implemented for this item {checkItem}.");
            }
        }

        var animIndex = (ObjType)spec.DwellerType - ObjType.OldMan;
        var animId = _personGraphics[animIndex].AnimId;
        _image = new SpriteImage(TileSheet.PlayerAndItems, animId);
        if (spec.Text != null)
        {
            _textBox = new TextBox(Game, spec.Text);
        }

        if (spec.ShowNumbers && spec.Items != null)
        {
            var showNegative = spec.ShowNegative;
            _priceStrings = spec.Items.Select(t =>
            {
                var neg = showNegative || t.ShowNegative ? NumberSign.Negative : NumberSign.None;
                return GameString.NumberToString(t.Cost, neg);
            }).ToArray();
        }

        if (PersonType == PersonType.Gambling)
        {
            InitGambling();
        }

        // JOE: TODO: Move this
        // if (type == CaveId.Cave11MedicineShop)
        // {
        //     var itemValue = Game.World.GetItem(ItemSlot.Letter);
        //     _state = itemValue == 2 ? PersonState.Idle : PersonState.WaitingForItem;
        // }
        if (spec.RequiredItem != null)
        {
            var itemValue = Game.World.GetItem(spec.RequiredItem.Item);
            _state = itemValue == spec.RequiredItem.RequiredLevel ? PersonState.Idle : PersonState.WaitingForItem;
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
            if (_spec.DoesControlsShutters) Game.World.TriggerShutters(); // JOE: NOTE: In the original code, this was OpenShutters.
            foreach (var item in _itemActors) item.Delete();
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

            case PersonState.PickedUp:
                UpdatePickUp();
                // Make them "blink" as they disappear.
                Visible = (Game.FrameCounter & 1) == 0;
                foreach (var item in _itemActors) item.Visible = Visible;
                break;
            case PersonState.WaitingForItem: UpdateWaitForLetter(); break;
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

    private bool HandlePlayerHit(CaveShopItem caveItem, int index)
    {
        if (caveItem.HasOption(CaveShopItemOptions.CheckCost))
        {
            var actual = Game.World.GetItem(caveItem.Costing);
            if (actual < caveItem.Cost)
            {
                _log.Write($"Failed check: {actual} < {caveItem.Costing} for {caveItem.ItemId}.");
                return false;
            }
        }

        if (_spec.IsPay)
        {
            var actual = Game.World.GetItem(caveItem.Costing);
            if (caveItem.Cost > actual)
            {
                _log.Write($"Failed pay: {actual} < {caveItem.Costing} for {caveItem.ItemId}.");
                return false;
            }

            // JOE: TODO: I want to do positives this way too.
            if (caveItem.Costing == ItemSlot.Rupees)
            {
                Game.World.PostRupeeLoss(caveItem.Cost);
            }
            else
            {
                var newValue = actual - caveItem.Cost;
                // This is to emulate the zombie player game behavior.
                if (caveItem.Costing == ItemSlot.HeartContainers && newValue <= PlayerProfile.DefaultHeartCount)
                {
                    Game.World.Profile.Hearts = 0;
                }
                else
                {
                    Game.World.SetItem(caveItem.Costing, actual - caveItem.Cost);
                }
            }
        }

        // Here we've successfully committed commerce.
        _objectState.ItemGot = true;
        foreach (var itemActor in _itemActors) itemActor.TouchEnabled = false;

        if (PersonType == PersonType.Gambling)
        {
            HandleGamblingPickup(caveItem, index);
        }
        else
        {
            HandlePickUpItem(caveItem);
        }

        return true;
    }

    private void HandlePickUpItem(CaveShopItem item)
    {
        HandlePickUpHint(item);

        // JOE: NOTE: This should ultimately go...? Or it needs to handle all the conditions.
        // This was causing double pickups from shops, but I am not certain it's not needed.
        // Game.World.AddItem(item.ItemId, item.ItemAmount);

        if (item.FillItem != null)
        {
            var max = Game.World.Profile.GetMax(item.FillItem.Value);
            Game.World.SetItem(item.FillItem.Value, max);
        }
        // ChosenIndex = index;
        if (_spec.IsPickUp)
        {
            _state = PersonState.PickedUp;
            ObjTimer = DisappearTime;
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

    private void HandleGamblingPickup(CaveShopItem item, int index)
    {
        var price = item.Cost;
        if (price > Game.World.GetItem(ItemSlot.Rupees)) return;

        int finalIndex;

        for (var i = 0; i < _gamblingIndexes.Length; i++)
        {
            finalIndex = _gamblingIndexes[i];
            var sign = finalIndex != 2 ? NumberSign.Negative : NumberSign.Positive;
            _priceStrings[i] = GameString.NumberToString(_gamblingAmounts[finalIndex], sign);
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
            ObjTimer = DisappearTime;
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

        if (_state == PersonState.WaitingForItem) return;

        for (var i = 0; _spec.Items != null && i < _spec.Items.Length; i++)
        {
            var location = _itemLocations[i];

            if (_state != PersonState.PickedUp)
            {
                if (_spec.ShowNumbers)
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