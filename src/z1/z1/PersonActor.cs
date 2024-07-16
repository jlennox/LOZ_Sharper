using z1.Actors;
using z1.Player;

namespace z1;

internal sealed class PersonActor : Actor
{
    public enum PersonState
    {
        Idle,
        PickedUp,
        WaitingForLetter,
        WaitingForFood,
        WaitingForStairs,
    };

    const int ItemY = 0x98;
    const int PriceY = 0xB0;
    private static readonly byte[] itemXs = new byte[] { 0x58, 0x78, 0x98 };
    private static readonly byte[] priceXs = new byte[] { 0x48, 0x68, 0x88 };

    private static readonly ItemGraphics[] sPersonGraphics = new ItemGraphics[]
    {
        new ItemGraphics(AnimationId.OldMan,       Palette.Red),
        new ItemGraphics(AnimationId.OldWoman,     Palette.Red),
        new ItemGraphics(AnimationId.Merchant,     Palette.Player),
        new ItemGraphics(AnimationId.Moblin,       Palette.Red),
    };

    public PersonState _state = PersonState.Idle;
    SpriteImage image = new SpriteImage();

    public CaveSpec spec;
    public TextBox textBox;
    public int chosenIndex;
    public bool showNumbers;

    private const int _maxItemCount = 3;
    private const int _priceLength = 4;
    byte[] priceStrs = new byte[_maxItemCount * _priceLength];

    private Span<byte> GetPrice(int index) => priceStrs.AsSpan()[(index * _priceLength)..];

    public byte[] gamblingAmounts = new byte[3];
    public byte[] gamblingIndexes = new byte[3];

    public override bool ShouldStopAtPersonWall => true;
    public override bool IsUnderworldPerson => true;

    public PersonActor(Game game, ObjType type, CaveSpec spec, int x, int y) : base(game, type, x, y)
    {
        this.spec = spec;
        HP = 0;
        // This isn't used anymore. The effect is implemented a different way.
        Game.World.SetPersonWallY(0x8D);

        if (!Game.World.IsOverworld())
            Game.Sound.PlayEffect(SoundEffect.Item);

        var stringId = spec.GetStringId();

        if (stringId == StringId.DoorRepair && Game.World.GotItem())
        {
            IsDeleted = true;
            return;
        }

        if (stringId == StringId.MoneyOrLife && Game.World.GotItem())
        {
            Game.World.OpenShutters();
            Game.World.SetPersonWallY(0);
            IsDeleted = true;
            return;
        }

        if (type == ObjType.Grumble && Game.World.GotItem())
        {
            Game.World.SetPersonWallY(0);
            IsDeleted = true;
            return;
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

        int animIndex = spec.DwellerType - ObjType.OldMan;
        var animId = sPersonGraphics[animIndex].AnimId;
        image.Animation = Graphics.GetAnimation(TileSheet.PlayerAndItems, animId);

        textBox = new TextBox(Game, Game.World.GetString(stringId).ToArray());

        Array.Fill(priceStrs, (byte)Char.Space);

        if (this.spec.GetShowPrices() || this.spec.GetSpecial())
        {
            var sign = this.spec.GetShowNegative() ? NumberSign.Negative : NumberSign.None;

            for (int i = 0; i < 3; i++)
            {
                var price = GetPrice(i);
                GlobalFunctions.NumberToStringR(this.spec.GetPrice(i), sign, ref price);
            }
        }

        if (IsGambling())
            InitGambling();

        if (type == ObjType.CaveMedicineShop)
        {
            int itemValue = Game.World.GetItem(ItemSlot.Letter);
            if (itemValue == 2)
                _state = PersonState.Idle;
            else
                _state = PersonState.WaitingForLetter;
        }

        if (stringId == StringId.MoreBombs)
        {
            showNumbers = true;
        }
        else if (stringId == StringId.MoneyOrLife)
        {
            showNumbers = true;
        }

        if (_state == PersonState.Idle)
            Game.Link.SetState(PlayerState.Paused);
    }

    public override void Update()
    {
        if (_state == PersonState.Idle)
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
        }
        else if (_state == PersonState.PickedUp)
        {
            UpdatePickUp();
        }
        else if (_state == PersonState.WaitingForLetter)
        {
            UpdateWaitForLetter();
        }
        else if (_state == PersonState.WaitingForFood)
        {
            UpdateWaitForFood();
        }
        else if (_state == PersonState.WaitingForStairs)
        {
            CheckStairsHit();
        }
    }

    void UpdateDialog()
    {
        if (textBox.IsDone())
            return;

        textBox.Update();

        if (textBox.IsDone())
        {
            if (spec.GetStringId() == StringId.DoorRepair)
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
                player.SetState(PlayerState.Idle);
        }
    }

    void CheckPlayerHit()
    {
        if (!spec.GetPickUp())
            return;

        var player = Game.Link;

        int distanceY = Math.Abs(ItemY - player.Y);
        if (distanceY >= 6)
            return;

        for (int i = 0; i < CaveSpec.Count; i++)
        {
            var itemId = spec.GetItemId(i);
            if (itemId != ItemId.None && player.X == itemXs[i])
            {
                HandlePlayerHit(i);
                break;
            }
        }
    }

    void HandlePlayerHit(int index)
    {
        if (spec.GetCheckHearts())
        {
            var expectedCount = ObjType switch {
                ObjType.Cave3 => 5,
                ObjType.Cave4 => 12,
                _ => throw new Exception()
            };

            if (Game.World.GetItem(ItemSlot.HeartContainers) < expectedCount)
                return;
        }

        if (spec.GetPay())
        {
            var price = spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees))
                return;
            Game.World.PostRupeeLoss(price);
        }

        if (!spec.GetShowPrices())
            Game.World.MarkItem();

        if (spec.GetHint())
            HandlePickUpHint(index);
        else if (spec.GetSpecial())
            HandlePickUpSpecial(index);
        else
            HandlePickUpItem(index);
    }

    void HandlePickUpItem(int index)
    {
        var itemId = spec.GetItemId(index);
        Game.World.AddItem(itemId);
        chosenIndex = index;
        _state = PersonState.PickedUp;
        ObjTimer = 0x40;
        Game.World.LiftItem(itemId);
        Game.Sound.PushSong(SongId.ItemLift);
        spec.ClearShowPrices();
    }

    void HandlePickUpHint(int index)
    {
        var stringId = StringId.AintEnough;

        if (index == 2)
        {
            stringId = ObjType switch {
                ObjType.Cave12 => StringId.LostHillsHint,
                ObjType.Cave13 => StringId.LostWoodsHint,
                _ => throw new Exception()
            };
        }

        textBox.Reset(Game.World.GetString(stringId).ToArray());

        spec.ClearShowPrices();
        spec.ClearPickUp();
    }

    void HandlePickUpSpecial(int index)
    {
        if (IsGambling())
        {
            var price = spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees))
                return;

            int finalIndex;

            for (int i = 0; i < CaveSpec.Count; i++)
            {
                finalIndex = gamblingIndexes[i];
                var sign = finalIndex != 2 ? NumberSign.Negative : NumberSign.Positive;
                var pricex = GetPrice(i);
                GlobalFunctions.NumberToStringR(gamblingAmounts[finalIndex], sign, ref pricex);
            }

            spec.ClearPickUp();
            finalIndex = gamblingIndexes[index];

            if (finalIndex == 2)
                Game.World.PostRupeeWin(gamblingAmounts[finalIndex]);
            else
                Game.World.PostRupeeLoss(gamblingAmounts[finalIndex]);
        }
        else if (spec.GetStringId() == StringId.MoreBombs)
        {
            var price = spec.GetPrice(index);
            if (price > Game.World.GetItem(ItemSlot.Rupees))
                return;

            Game.World.PostRupeeLoss(price);
            Game.World.GetProfile().Items[ItemSlot.MaxBombs] += 4;
            Game.World.GetProfile().Items[ItemSlot.Bombs] = Game.World.GetProfile().Items[ItemSlot.MaxBombs];

            showNumbers = false;
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
        }
        else if (spec.GetStringId() == StringId.MoneyOrLife)
        {
            var price = spec.GetPrice(index);
            var itemId = spec.GetItemId(index);

            if (itemId == ItemId.Rupee)
            {
                if (price > Game.World.GetItem(ItemSlot.Rupees))
                    return;

                Game.World.PostRupeeLoss(price);
            }
            else if (itemId == ItemId.HeartContainer)
            {
                if (price > Game.World.GetItem(ItemSlot.HeartContainers))
                    return;

                var profile = Game.World.GetProfile();
                if (profile.Items[ItemSlot.HeartContainers] > 1)
                {
                    profile.Items[ItemSlot.HeartContainers]--;
                    if (profile.Hearts > 0x100)
                        profile.Hearts -= 0x100;
                    Game.Sound.PlayEffect(SoundEffect.KeyHeart);
                }
            }
            else
            {
                return;
            }

            Game.World.MarkItem();
            Game.World.OpenShutters();

            showNumbers = false;
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
        }
        else  // Give money
        {
            byte amount = spec.GetPrice(index);

            Game.World.PostRupeeWin(amount);
            Game.World.MarkItem();
            spec.ClearPickUp();
            showNumbers = true;
        }
    }

    bool IsGambling()
    {
        return spec.GetSpecial() && ObjType >= ObjType.Cave1 && ObjType < ObjType.Cave18;
    }

    void InitGambling()
    {
        for (var i = 0; i < gamblingIndexes.Length; i++)
            gamblingIndexes[i] = (byte)i;

        gamblingAmounts[0] = (byte)((Random.Shared.Next(2) == 0) ? 10 : 40);
        gamblingAmounts[1] = 10;
        gamblingAmounts[2] = (byte)((Random.Shared.Next(2) == 0) ? 20 : 50);

        gamblingIndexes.Shuffle();
    }

    void UpdatePickUp()
    {
        if (ObjTimer == 0)
        {
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
    }

    void UpdateWaitForLetter()
    {
        int itemValue = Game.World.GetItem(ItemSlot.Letter);
        if (itemValue == 2)
        {
            _state = PersonState.Idle;
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
    }

    void UpdateWaitForFood()
    {
        var food = Game.World.GetObject(ObjectSlot.Food);
        if (food is FoodActor)
        {
            _state = PersonState.PickedUp;
            ObjTimer = 0x40;
            Game.Sound.PlayEffect(SoundEffect.Secret);
        }
    }

    void CheckStairsHit()
    {
        var stairsXs = new byte[] { 0x50, 0x80, 0xB0 };

        if (Game.Link.Y != 0x9D)
            return;

        var rooms = Game.World.GetShortcutRooms();
        int playerX = Game.Link.X;
        int stairsIndex = -1;

        for (int i = 0; i < stairsXs.Length; i++)
        {
            if (playerX == stairsXs[i])
            {
                stairsIndex = i;
                break;
            }
        }

        if (stairsIndex < 0)
            return;


        // JOE: I made a lot of sus changes here.
        for (var j = 0; j < rooms.Length; j++)
        {
            if (rooms[j] == Game.World.curRoomId)
            {
                var index = j + 1 + stairsIndex;
                if (index >= rooms.Length)
                    index -= rooms.Length;

                Game.World.LeaveCellarByShortcut(rooms[index]);
                break;
            }
        }
    }

    public override void Draw()
    {
        if (_state == PersonState.PickedUp)
        {
            if ((Game.GetFrameCounter() & 1) == 0)
                return;
        }
        else if (_state == PersonState.Idle || _state == PersonState.WaitingForFood || _state == PersonState.WaitingForStairs)
        {
            DrawDialog();
        }

        int animIndex = spec.DwellerType - ObjType.OldMan;
        var palette = sPersonGraphics[animIndex].PaletteAttrs;
        palette = CalcPalette(palette);
        image.Draw(TileSheet.PlayerAndItems, X, Y, palette);

        if (_state == PersonState.WaitingForLetter)
            return;

        for (int i = 0; i < 3; i++)
        {
            var itemId = spec.GetItemId(i);

            if ((itemId < ItemId.MAX) && (_state != PersonState.PickedUp || i != chosenIndex))
            {
                if (spec.GetShowItems())
                    GlobalFunctions.DrawItemWide(Game, itemId, itemXs[i], ItemY);
                if (spec.GetShowPrices() || showNumbers)
                    GlobalFunctions.DrawString(GetPrice(i), priceXs[i], PriceY, 0);
            }
        }

        if (spec.GetShowPrices())
        {
            GlobalFunctions.DrawItemWide(Game, ItemId.Rupee, 0x30, 0xAC);
            GlobalFunctions.DrawChar(Char.X, 0x40, 0xB0, 0);
        }
    }

    void DrawDialog()
    {
        textBox.Draw();
    }
}