using SkiaSharp;
using z1.IO;
using z1.Render;
using z1.UI;

namespace z1;

internal partial class World
{
    public void WinGame()
    {
        GotoWinGame();
    }

    private void GotoWinGame()
    {
        _state.WinGame.Substate = WinGameState.Substates.Start;
        _state.WinGame.Timer = 162;
        _state.WinGame.Left = 0;
        _state.WinGame.Right = TileMapWidth;
        _state.WinGame.StepTimer = 0;
        _state.WinGame.NpcVisual = WinGameState.NpcVisualState.Stand;

        _curMode = GameMode.WinGame;
    }

    private static readonly byte[] _winGameStr1 = [
        0x1d, 0x11, 0x0a, 0x17, 0x14, 0x1c, 0x24, 0x15, 0x12, 0x17, 0x14, 0x28, 0x22, 0x18, 0x1e, 0x2a,
        0x1b, 0x8e, 0x64, 0x1d, 0x11, 0x0e, 0x24, 0x11, 0x0e, 0x1b, 0x18, 0x24, 0x18, 0x0f, 0x24, 0x11,
        0x22, 0x1b, 0x1e, 0x15, 0x0e, 0xec
    ];

    private static ReadOnlySpan<byte> WinGameStr2 => [
        0x25, 0x25, 0x25, 0x25, 0x25, 0x25, 0x25, 0x25,
        0x0f, 0x12, 0x17, 0x0a, 0x15, 0x15, 0x22, 0x28,
        0xa5, 0x65,
        0x19, 0x0e, 0x0a, 0x0c, 0x0e, 0x24, 0x1b, 0x0e,
        0x1d, 0x1e, 0x1b, 0x17, 0x1c, 0x24, 0x1d, 0x18, 0x24, 0x11, 0x22, 0x1b, 0x1e, 0x15, 0x0e, 0x2c,
        0xa5, 0x65, 0x65, 0x25, 0x25,
        0x1d, 0x11, 0x12, 0x1c, 0x24, 0x0e, 0x17, 0x0d, 0x1c, 0x24, 0x1d, 0x11, 0x0e, 0x24, 0x1c, 0x1d,
        0x18, 0x1b, 0x22, 0x2c, 0xe5
    ];

    private void UpdateWinGame()
    {
        switch (_state.WinGame.Substate)
        {
            case WinGameState.Substates.Start: WinGameStart(); break;
            case WinGameState.Substates.Text1: WinGameText1(); break;
            case WinGameState.Substates.Stand: WinGameStand(); break;
            case WinGameState.Substates.Hold1: WinGameHold1(); break;
            case WinGameState.Substates.Colors: WinGameColors(); break;
            case WinGameState.Substates.Hold2: WinGameHold2(); break;
            case WinGameState.Substates.Text2: WinGameText2(); break;
            case WinGameState.Substates.Hold3: WinGameHold3(); break;
            case WinGameState.Substates.NoObjects: WinGameNoObjects(); break;
            case WinGameState.Substates.Credits: WinGameCredits(); break;
        }
        return;

        void WinGameStart()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_state.WinGame.Timer);

            if (_state.WinGame.Timer > 0)
            {
                _state.WinGame.Timer--;
                return;
            }

            if (_state.WinGame.Left == WorldMidX)
            {
                _state.WinGame.Substate = WinGameState.Substates.Text1;
                _statusBar.EnableFeatures(StatusBarFeatures.EquipmentAndMap, false);

                // A959
                _textBox1 = new TextBox(Game, _winGameStr1);
            }
            else if (_state.WinGame.StepTimer == 0)
            {
                _state.WinGame.Left += 8;
                _state.WinGame.Right -= 8;
                _state.WinGame.StepTimer = 4;
            }
            else
            {
                _state.WinGame.StepTimer--;
            }
        }

        void WinGameText1()
        {
            _textBox1.Update();
            if (_textBox1.IsDone())
            {
                _state.WinGame.Substate = WinGameState.Substates.Stand;
                _state.WinGame.Timer = 76;
            }
        }

        void WinGameStand()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_state.WinGame.Timer);

            _state.WinGame.Timer--;
            if (_state.WinGame.Timer == 0)
            {
                _state.WinGame.Substate = WinGameState.Substates.Hold1;
                _state.WinGame.Timer = 64;
            }
        }

        void WinGameHold1()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_state.WinGame.Timer);

            _state.WinGame.NpcVisual = WinGameState.NpcVisualState.Lift;
            _state.WinGame.Timer--;
            if (_state.WinGame.Timer == 0)
            {
                _state.WinGame.Substate = WinGameState.Substates.Colors;
                _state.WinGame.Timer = 127;
            }
        }

        void WinGameColors()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_state.WinGame.Timer);

            _state.WinGame.Timer--;
            if (_state.WinGame.Timer == 0)
            {
                _state.WinGame.Substate = WinGameState.Substates.Hold2;
                _state.WinGame.Timer = 131;
                Game.Sound.PlaySong(SongId.Ending, SongStream.MainSong, true);
            }
        }

        void WinGameHold2()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_state.WinGame.Timer);

            _state.WinGame.Timer--;
            if (_state.WinGame.Timer == 0)
            {
                _state.WinGame.Substate = WinGameState.Substates.Text2;
                _textBox2 = new TextBox(Game, WinGameStr2.ToArray(), 8); // TODO
                _textBox2.SetY(WinGameState.TextBox2Top);
            }
        }

        void WinGameText2()
        {
            if (_textBox2 == null) throw new Exception();

            _textBox2.Update();
            if (_textBox2.IsDone())
            {
                _state.WinGame.Substate = WinGameState.Substates.Hold3;
                _state.WinGame.Timer = 129;
            }
        }

        void WinGameHold3()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(_state.WinGame.Timer);

            _state.WinGame.Timer--;
            if (_state.WinGame.Timer == 0)
            {
                _state.WinGame.Substate = WinGameState.Substates.NoObjects;
                _state.WinGame.Timer = 32;
            }
        }

        void WinGameNoObjects()
        {
            _state.WinGame.NpcVisual = WinGameState.NpcVisualState.None;
            _state.WinGame.Timer--;
            if (_state.WinGame.Timer == 0)
            {
                _credits = new CreditsType(Game);
                _state.WinGame.Substate = WinGameState.Substates.Credits;
            }
        }

        void WinGameCredits()
        {
            if (_credits == null) throw new Exception();

            ReadOnlySpan<TextBox?> boxes = [_textBox1, _textBox2];
            ReadOnlySpan<int> startYs = [TextBox.StartY, WinGameState.TextBox2Top];

            for (var i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                if (box != null)
                {
                    var textToCreditsY = CreditsType.StartY - startYs[i];
                    box.SetY(_credits.GetTop() - textToCreditsY);
                    var bottom = box.GetY() + box.GetHeight();
                    if (bottom <= 0)
                    {
                        switch (i)
                        {
                            case 0: _textBox1 = null; break;
                            case 1: _textBox2 = null; break;
                        }
                    }
                }
            }

            _credits.Update();
            if (_credits.IsDone())
            {
                if (IsButtonPressing(GameButton.Start))
                {
                    _credits = null;
                    Game.Player = null;
                    DeleteObjects();
                    _submenuOffsetY = 0;
                    _statusBarVisible = false;
                    _statusBar.EnableFeatures(StatusBarFeatures.All, true);

                    // JOE: TODO: I think this conversion is ok...
                    // JOE: TODO: QUEST Profile.Quest = 1;
                    Profile.Items.Set(ItemSlot.HeartContainers, PersistedItems.DefaultHeartCount);
                    Profile.Items.Set(ItemSlot.MaxBombs, PersistedItems.DefaultMaxBombCount);
                    SaveFolder.SaveProfiles();

                    Game.Sound.StopAll();
                    Game.Menu.GotoFileMenu();
                }
            }
            else
            {
                var statusTop = _credits.GetTop() - CreditsType.StartY;
                var statusBottom = statusTop + StatusBar.StatusBarHeight;
                _submenuOffsetY = statusBottom > 0 ? statusTop : -StatusBar.StatusBarHeight;
            }
        }
    }

    private void DrawWinGame(Graphics graphics)
    {
        SKColor backColor;

        using (var _ = graphics.SetClip(0, 0, Global.StdViewWidth, Global.StdViewHeight))
        {
            if (_state.WinGame.Substate == WinGameState.Substates.Colors)
            {
                ReadOnlySpan<int> sysColors = [0x0F, 0x2A, 0x16, 0x12];
                var frame = _state.WinGame.Timer & 3;
                var sysColor = sysColors[frame];
                ClearScreen(graphics, sysColor);
                backColor = GraphicPalettes.GetSystemColor(sysColor);
            }
            else
            {
                ClearScreen(graphics);
                backColor = SKColors.Black;
            }
        }

        _statusBar.Draw(graphics, _submenuOffsetY, backColor);

        if (_state.WinGame.Substate == WinGameState.Substates.Start)
        {
            var left = _state.WinGame.Left;
            var width = _state.WinGame.Right - _state.WinGame.Left;

            using (var _ = graphics.SetClip(left, TileMapBaseY, width, TileMapHeight))
            {
                DrawRoomNoObjects(graphics, true);
            }

            Game.Player.Draw(graphics);
            DrawObjects(graphics);
        }
        else
        {
            var princess = GetObject<PrincessActor>() ?? throw new Exception();

            switch (_state.WinGame.NpcVisual)
            {
                case WinGameState.NpcVisualState.Stand:
                    princess.Draw(graphics);
                    Game.Player.Draw(graphics);
                    break;

                case WinGameState.NpcVisualState.Lift:
                    DrawPrincessLiftingTriforce(graphics, princess.X, princess.Y);
                    DrawPlayerLiftingItem(graphics, ItemId.TriforcePiece);
                    break;
            }

            _credits?.Draw(graphics);
            _textBox1?.Draw(graphics);
            _textBox2?.Draw(graphics);
        }
    }
}
