using z1.Player;

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

    internal enum PersonType
    {
        Shop,
        Grumble,
        MoneyOrLife,
        DoorRepair,
        Gambling,
        Level9,
        CaveShortcut,
        MoreBombs,
    }

    private const int ItemY = 0x98;
    private const int PriceY = 0xB0;

    private const int MaxItemCount = 3;
    private const int PriceLength = 4;

    private static readonly byte[] _itemXs = { 0x58, 0x78, 0x98 };
    private static readonly byte[] _priceXs = { 0x48, 0x68, 0x88 };

    private static readonly ItemGraphics[] _sPersonGraphics = {
        new(AnimationId.OldMan,       Palette.Red),
        new(AnimationId.OldWoman,     Palette.Red),
        new(AnimationId.Merchant,     Palette.Player),
        new(AnimationId.Moblin,       Palette.Red),
    };

    private PersonState _state = PersonState.Idle;
    private readonly SpriteImage? _image;

    public CaveSpec Spec;
    public TextBox? TextBox;
    public int ChosenIndex;
    public bool ShowNumbers;

    private readonly byte[] _priceStrs = new byte[MaxItemCount * PriceLength];

    private Span<byte> GetPrice(int index) => _priceStrs.AsSpan(index * PriceLength, PriceLength);

    public byte[] GamblingAmounts = new byte[3];
    public byte[] GamblingIndexes = new byte[3];

    public override bool ShouldStopAtPersonWall => true;
    public override bool IsUnderworldPerson => true;

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

        var stringId = spec.GetStringId();

        if (Game.World.GotItem())
        {
            if (stringId == StringId.DoorRepair)
            {
                IsDeleted = true;
                return;
            }

            if (stringId == StringId.MoneyOrLife)
            {
                Game.World.OpenShutters();
                Game.World.SetPersonWallY(0);
                IsDeleted = true;
                return;
            }

            if (type == ObjType.Grumble)
            {
                Game.World.SetPersonWallY(0);
                IsDeleted = true;
                return;
            }
        }

        if (stringId == StringId.EnterLevel9)
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
        _image = new SpriteImage(Graphics.GetAnimation(TileSheet.PlayerAndItems, animId));
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

        if (IsGambling())
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

    // JOE: TODO: Start using this. I think "Shop" might be an boolean out argument?
    private PersonType GetPersonType()
    {
        if (IsGambling()) return PersonType.Gambling;
        var stringId = Spec.GetStringId();
        if (stringId == StringId.DoorRepair) return PersonType.DoorRepair;
        if (stringId == StringId.MoneyOrLife) return PersonType.MoneyOrLife;
        if (stringId == StringId.EnterLevel9) return PersonType.Level9;
        if (stringId == StringId.MoreBombs) return PersonType.MoreBombs;
        if (ObjType == ObjType.CaveShortcut) return PersonType.CaveShortcut;
        if (ObjType == ObjType.Grumble) return PersonType.Grumble;
        return PersonType.Shop;
    }

    public override void Update()
    {
        switch (_state)
        {
            case PersonState.Idle:
            {
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
            }
            case PersonState.PickedUp: UpdatePickUp(); break;
            case PersonState.WaitingForLetter: UpdateWaitForLetter(); break;
            case PersonState.WaitingForFood: UpdateWaitForFood(); break;
            case PersonState.WaitingForStairs: CheckStairsHit(); break;
        }
    }

    private void UpdateDialog()
    {
        if (TextBox == null) throw new Exception();
        if (TextBox.IsDone()) return;

        TextBox.Update();

        if (TextBox.IsDone())
        {
            if (Spec.GetStringId() == StringId.DoorRepair)
            {
                Game.World.PostRupeeLoss(20);
                Game.World.MarkItem();
            }
            else if (ObjType == ObjType.Grumble)
            {
                _state = PersonState.WaitingForFood;
            }
            else if (ObjType == ObjType.CaveShortcut)
            {
                _state = PersonState.WaitingForStairs;
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
                ObjType.Cave3 => 5,
                ObjType.Cave4 => 12,
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

        var stringId = StringId.AintEnough;

        if (index == 2)
        {
            stringId = ObjType switch
            {
                ObjType.Cave12 => StringId.LostHillsHint,
                ObjType.Cave13 => StringId.LostWoodsHint,
                _ => throw new Exception()
            };
        }

        TextBox.Reset(Game.World.GetString(stringId).ToArray());

        Spec.ClearShowPrices();
        Spec.ClearPickUp();
    }

    private void HandlePickUpSpecial(int index)
    {
        if (IsGambling())
        {
            var price = Spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees))
                return;

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

        if (Spec.GetStringId() == StringId.MoreBombs)
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

        if (Spec.GetStringId() == StringId.MoneyOrLife)
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
                // JOE: TODO: This needs to emulate the original "zombie link" game behavior.
                if (profile.Items[ItemSlot.HeartContainers] > 1)
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
        ReadOnlySpan<byte> stairsXs = [ 0x50, 0x80, 0xB0 ];

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
        if (_state == PersonState.PickedUp)
        {
            if ((Game.GetFrameCounter() & 1) == 0) return;
        }
        else if (_state is PersonState.Idle or PersonState.WaitingForFood or PersonState.WaitingForStairs)
        {
            DrawDialog();
        }

        var animIndex = Spec.DwellerType - ObjType.OldMan;
        var palette = _sPersonGraphics[animIndex].PaletteAttrs;
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