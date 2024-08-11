using System.Collections.Immutable;

namespace z1.Actors;

internal sealed class PersonActor : Actor
{
    private enum PersonState
    {
        Idle,
        PickedUp,
        WaitingForLetter,
        WaitingForFood,
        WaitingForStairs,
    }

    private enum PersonType
    {
        Shop,
        Grumble,
        MoneyOrLife,
        DoorRepair,
        Gambling,
        EnterLevel9,
        CaveShortcut,
        MoreBombs,
    }

    private const int ItemY = 0x98;
    private const int PriceY = 0xB0;

    private const int MaxItemCount = 3;
    private const int PriceLength = 4;

    private static readonly ImmutableArray<byte> _itemXs = [0x58, 0x78, 0x98];
    private static readonly ImmutableArray<byte> _priceXs = [0x48, 0x68, 0x88];

    private static readonly ImmutableArray<ItemGraphics> _sPersonGraphics = [
        new(AnimationId.OldMan,   Palette.Red),
        new(AnimationId.OldWoman, Palette.Red),
        new(AnimationId.Merchant, Palette.Player),
        new(AnimationId.Moblin,   Palette.Red)
    ];

    private PersonState _state = PersonState.Idle;
    private readonly SpriteImage? _image;

    public CaveSpec Spec; // Do not make readonly to avoid struct copies.
    public readonly TextBox? TextBox;
    public int ChosenIndex;
    public bool ShowNumbers;

    private readonly byte[] _priceStrs = new byte[MaxItemCount * PriceLength];
    private Span<byte> GetPrice(int index) => _priceStrs.AsSpan(index * PriceLength, PriceLength);

    public readonly byte[] GamblingAmounts = new byte[3];
    public readonly byte[] GamblingIndexes = new byte[3];

    public override bool ShouldStopAtPersonWall => true;
    public override bool IsUnderworldPerson => true;

    private readonly PersonType _personType;

    public PersonActor(Game game, ObjType type, CaveSpec spec, int x, int y)
        : base(game, type, x, y)
    {
        Spec = spec;
        HP = 0;
        // This isn't used anymore. The effect is implemented a different way.
        Game.World.SetPersonWallY(0x8D);

        if (!Game.World.IsOverworld())
        {
            Game.Sound.PlayEffect(SoundEffect.Item);
        }

        _personType = GetPersonType();
        var stringId = Spec.GetStringId();

        // Room has been previously paid for. Clear it out.
        if (Game.World.GotItem())
        {
            switch (_personType)
            {
                case PersonType.DoorRepair:
                    IsDeleted = true;
                    return;

                case PersonType.MoneyOrLife:
                    Game.World.OpenShutters();
                    Game.World.SetPersonWallY(0);
                    IsDeleted = true;
                    return;

                case PersonType.Grumble:
                    Game.World.SetPersonWallY(0);
                    IsDeleted = true;
                    return;
            }
        }

        if (_personType == PersonType.EnterLevel9)
        {
            if (Game.World.GetItem(ItemSlot.TriforcePieces) == 0xFF)
            {
                Game.World.OpenShutters();
                Game.World.SetPersonWallY(0);
                IsDeleted = true;
                return;
            }
        }

        if (spec.GetPickUp() && !spec.GetShowPrices())
        {
            if (Game.World.GotItem())
            {
                IsDeleted = true;
                return;
            }
        }

        var animIndex = spec.DwellerType - ObjType.OldMan;
        var animId = _sPersonGraphics[animIndex].AnimId;
        _image = new SpriteImage(TileSheet.PlayerAndItems, animId);
        TextBox = new TextBox(Game, Game.World.GetString(stringId).ToArray());

        Array.Fill(_priceStrs, (byte)Char.Space);

        if (Spec.GetShowPrices() || Spec.GetSpecial())
        {
            var sign = Spec.GetShowNegative() ? NumberSign.Negative : NumberSign.None;

            for (var i = 0; i < 3; i++)
            {
                var price = GetPrice(i);
                GlobalFunctions.NumberToStringR(Spec.GetPrice(i), sign, ref price);
            }
        }

        if (_personType == PersonType.Gambling)
        {
            InitGambling();
        }

        if (type == ObjType.CaveMedicineShop)
        {
            var itemValue = Game.World.GetItem(ItemSlot.Letter);
            _state = itemValue == 2 ? PersonState.Idle : PersonState.WaitingForLetter;
        }

        ShowNumbers = stringId is StringId.MoreBombs or StringId.MoneyOrLife;

        if (_state == PersonState.Idle)
        {
            Game.Link.SetState(PlayerState.Paused);
        }
    }

    // JOE: TODO: Start using this. I think "Shop" might be a boolean out argument?
    private PersonType GetPersonType()
    {
        if (IsGambling()) return PersonType.Gambling;
        var stringId = Spec.GetStringId();
        switch (stringId)
        {
            case StringId.DoorRepair: return PersonType.DoorRepair;
            case StringId.MoneyOrLife: return PersonType.MoneyOrLife;
            case StringId.EnterLevel9: return PersonType.EnterLevel9;
            case StringId.MoreBombs: return PersonType.MoreBombs;
        }

        switch (ObjType)
        {
            case ObjType.CaveShortcut: return PersonType.CaveShortcut;
            case ObjType.Grumble: return PersonType.Grumble;
        }

        return PersonType.Shop;
    }

    public override void Update()
    {
        switch (_state)
        {
            case PersonState.Idle:
                UpdateDialog();
                CheckPlayerHit();

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
        if (TextBox == null) throw new Exception();
        if (TextBox.IsDone()) return;

        TextBox.Update();

        if (TextBox.IsDone())
        {
            switch (_personType)
            {
                case PersonType.DoorRepair:
                    Game.World.PostRupeeLoss(20);
                    Game.World.MarkItem();
                    break;

                case PersonType.Grumble:
                    _state = PersonState.WaitingForFood;
                    break;

                case PersonType.CaveShortcut:
                    _state = PersonState.WaitingForStairs;
                    break;
            }

            var player = Game.Link;
            if (player.GetState() == PlayerState.Paused)
            {
                player.SetState(PlayerState.Idle);
            }
        }
    }

    private void CheckPlayerHit()
    {
        if (!Spec.GetPickUp()) return;

        var player = Game.Link;

        var distanceY = Math.Abs(ItemY - player.Y);
        if (distanceY >= 6) return;

        for (var i = 0; i < CaveSpec.Count; i++)
        {
            var itemId = Spec.GetItemId(i);
            if (itemId != ItemId.None && player.X == _itemXs[i])
            {
                HandlePlayerHit(i);
                break;
            }
        }
    }

    private void HandlePlayerHit(int index)
    {
        if (Spec.GetCheckHearts())
        {
            var expectedCount = ObjType switch
            {
                ObjType.Cave3WhiteSword => 5,
                ObjType.Cave4MagicSword => 12,
                _ => throw new Exception()
            };

            if (Game.World.GetItem(ItemSlot.HeartContainers) < expectedCount) return;
        }

        if (Spec.GetPay())
        {
            var price = Spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees)) return;
            Game.World.PostRupeeLoss(price);
        }

        if (!Spec.GetShowPrices())
        {
            Game.World.MarkItem();
        }

        if (Spec.GetHint())
        {
            HandlePickUpHint(index);
        }
        else if (Spec.GetSpecial())
        {
            HandlePickUpSpecial(index);
        }
        else
        {
            HandlePickUpItem(index);
        }
    }

    private void HandlePickUpItem(int index)
    {
        var itemId = Spec.GetItemId(index);
        Game.World.AddItem(itemId);
        ChosenIndex = index;
        _state = PersonState.PickedUp;
        ObjTimer = 0x40;
        Game.World.LiftItem(itemId);
        Game.Sound.PushSong(SongId.ItemLift);
        Spec.ClearShowPrices();
    }

    private void HandlePickUpHint(int index)
    {
        if (TextBox == null) throw new Exception();

        var stringId = (index, ObjType) switch
        {
            (2, ObjType.Cave12LostHillsHint) => StringId.LostHillsHint,
            (2, ObjType.Cave13LostWoodsHint) => StringId.LostWoodsHint,
            (2, _) => throw new Exception(),
            _ => StringId.AintEnough,
        };

        TextBox.Reset(Game.World.GetString(stringId).ToArray());

        Spec.ClearShowPrices();
        Spec.ClearPickUp();
    }

    private void HandlePickUpSpecial(int index)
    {
        if (_personType == PersonType.Gambling)
        {
            var price = Spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees)) return;

            int finalIndex;

            for (var i = 0; i < CaveSpec.Count; i++)
            {
                finalIndex = GamblingIndexes[i];
                var sign = finalIndex != 2 ? NumberSign.Negative : NumberSign.Positive;
                var pricex = GetPrice(i);
                GlobalFunctions.NumberToStringR(GamblingAmounts[finalIndex], sign, ref pricex);
            }

            Spec.ClearPickUp();
            finalIndex = GamblingIndexes[index];

            if (finalIndex == 2)
            {
                Game.World.PostRupeeWin(GamblingAmounts[finalIndex]);
            }
            else
            {
                Game.World.PostRupeeLoss(GamblingAmounts[finalIndex]);
            }

            return;
        }

        if (_personType == PersonType.MoreBombs)
        {
            var price = Spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees)) return;

            Game.World.PostRupeeLoss(price);
            var profile = Game.World.Profile;
            profile.Items[ItemSlot.MaxBombs] += 4;
            profile.Items[ItemSlot.Bombs] = profile.Items[ItemSlot.MaxBombs];

            ShowNumbers = false;
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
            return;
        }

        if (_personType == PersonType.MoneyOrLife)
        {
            var price = Spec.GetPrice(index);
            var itemId = Spec.GetItemId(index);

            if (itemId == ItemId.Rupee)
            {
                if (price > Game.World.GetItem(ItemSlot.Rupees)) return;

                Game.World.PostRupeeLoss(price);
            }
            else if (itemId == ItemId.HeartContainer)
            {
                if (price > Game.World.GetItem(ItemSlot.HeartContainers)) return;

                var profile = Game.World.Profile;
                if (profile.Items[ItemSlot.HeartContainers] <= PlayerProfile.DefaultHearts)
                {
                    // This is to emulate the zombie link game behavior.
                    profile.Hearts = 0;
                }
                else
                {
                    profile.Items[ItemSlot.HeartContainers]--;
                    if (profile.Hearts > 0x100)
                    {
                        profile.Hearts -= 0x100;
                    }
                    Game.Sound.PlayEffect(SoundEffect.KeyHeart);
                }
            }
            else
            {
                return;
            }

            Game.World.MarkItem();
            Game.World.OpenShutters();

            ShowNumbers = false;
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
            return;
        }

        // Give money
        var amount = Spec.GetPrice(index);

        Game.World.PostRupeeWin(amount);
        Game.World.MarkItem();
        Spec.ClearPickUp();
        ShowNumbers = true;
    }

    private bool IsGambling()
    {
        return Spec.GetSpecial() && ObjType >= ObjType.Cave1 && ObjType < ObjType.Cave18;
    }

    private void InitGambling()
    {
        for (var i = 0; i < GamblingIndexes.Length; i++)
        {
            GamblingIndexes[i] = (byte)i;
        }

        GamblingAmounts[0] = (byte)(Random.Shared.Next(2) == 0 ? 10 : 40);
        GamblingAmounts[1] = 10;
        GamblingAmounts[2] = (byte)(Random.Shared.Next(2) == 0 ? 20 : 50);

        GamblingIndexes.Shuffle();
    }

    private void UpdatePickUp()
    {
        if (ObjTimer != 0) return;

        IsDeleted = true;

        if (ObjType == ObjType.Grumble)
        {
            Game.World.MarkItem();
            Game.World.SetItem(ItemSlot.Food, 0);
            Game.World.SetPersonWallY(0);

            var food = Game.World.GetObject(ObjectSlot.Food);
            if (food is FoodActor)
            {
                food.IsDeleted = true;
            }
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
        var food = Game.World.GetObject(ObjectSlot.Food);
        if (food is FoodActor)
        {
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
    }

    private void CheckStairsHit()
    {
        ReadOnlySpan<byte> stairsXs = [0x50, 0x80, 0xB0];

        if (Game.Link.Y != 0x9D) return;

        var rooms = Game.World.GetShortcutRooms();
        var playerX = Game.Link.X;
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
            if (rooms[j] == Game.World.CurRoomId)
            {
                var index = j + 1 + stairsIndex;
                if (index >= rooms.Length)
                {
                    index -= rooms.Length;
                }

                Game.World.LeaveCellarByShortcut(rooms[index]);
                break;
            }
        }
    }

    public override void Draw()
    {
        switch (_state)
        {
            case PersonState.PickedUp when (Game.GetFrameCounter() & 1) == 0:
                return;

            case PersonState.Idle:
            case PersonState.WaitingForFood:
            case PersonState.WaitingForStairs:
                DrawDialog();
                break;
        }

        if (_image == null) throw new NullReferenceException("_image is null.");

        var animIndex = Spec.DwellerType - ObjType.OldMan;
        var palette = _sPersonGraphics[animIndex].Palette;
        palette = CalcPalette(palette);
        _image.Draw(TileSheet.PlayerAndItems, X, Y, palette);

        if (_state == PersonState.WaitingForLetter) return;

        for (var i = 0; i < 3; i++)
        {
            var itemId = Spec.GetItemId(i);

            if (itemId < ItemId.MAX && (_state != PersonState.PickedUp || i != ChosenIndex))
            {
                if (Spec.GetShowItems())
                {
                    GlobalFunctions.DrawItemWide(Game, itemId, _itemXs[i], ItemY);
                }
                if (Spec.GetShowPrices() || ShowNumbers)
                {
                    GlobalFunctions.DrawString(GetPrice(i), _priceXs[i], PriceY, 0);
                }
            }
        }

        if (Spec.GetShowPrices())
        {
            GlobalFunctions.DrawItemWide(Game, ItemId.Rupee, 0x30, 0xAC);
            GlobalFunctions.DrawChar(Char.X, 0x40, 0xB0, 0);
        }
    }

    private void DrawDialog()
    {
        if (TextBox == null) throw new Exception();
        TextBox.Draw();
    }
}