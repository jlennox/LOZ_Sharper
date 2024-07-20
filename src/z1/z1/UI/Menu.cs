using SkiaSharp;

namespace z1.UI;

internal abstract class Menu
{
    public abstract void Update();
    public abstract void Draw();
}

internal sealed class GameMenu : Menu
{
    private readonly Game game;

    public ProfileSummarySnapshot summaries;
    public int selectedIndex;

    public GameMenu(Game game, ProfileSummarySnapshot summaries)
    {
        this.game = game;
        this.summaries = summaries;

        var palettes = new byte[][]
        {
            new byte[] { 0x0F, 0x30, 0x00, 0x12 },
            new byte[] { 0x0F, 0x16, 0x27, 0x36 },
            new byte[] { 0x0F, 0x0C, 0x1C, 0x2C },
            new byte[] { 0x0F, 0x12, 0x1C, 0x2C },
            new byte[] { 0x00, 0x29, 0x27, 0x07 },
            new byte[] { 0x00, 0x29, 0x27, 0x07 },
            new byte[] { 0x00, 0x29, 0x27, 0x07 },
            new byte[] { 0x00, 0x15, 0x27, 0x30 }
        };

        for (var i = 0; i < palettes.Length; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, palettes[i]);
        }

        // So that characters are fully opaque.
        Graphics.SetColor(0, 0, 0xFF000000);
        Graphics.UpdatePalettes();

        SelectNext();
    }

    void StartWorld(int fileIndex)
    {
        var profile = SaveFolder.ReadProfile(fileIndex);
        game.World.Start(fileIndex, profile);
    }

    public override void Update()
    {
        if (game.Input.IsButtonPressing(Button.Select))
        {
            SelectNext();
            game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (game.Input.IsButtonPressing(Button.Start))
        {
            if (selectedIndex < 3)
                StartWorld(selectedIndex);
            else if (selectedIndex == 3)
                game.World.RegisterFile(summaries);
            else if (selectedIndex == 4)
                game.World.EliminateFile(summaries);
        }
    }

    private static readonly byte[] _selectStr = new byte[]
        { 0x62, 0x24, 0x1C, 0x24, 0x0E, 0x24, 0x15, 0x24, 0x0E, 0x24, 0x0C, 0x24, 0x1D, 0x24, 0x62 };

    private static readonly byte[] _nameStr = new byte[]
        { 0x24, 0x17, 0x0A, 0x16, 0x0E, 0x24 };

    private static readonly byte[] _lifeStr = new byte[]
        { 0x24, 0x15, 0x12, 0x0F, 0x0E, 0x24 };

    private static readonly byte[] _registerStr = new byte[]
    {
        0x1B, 0x0E, 0x10, 0x12, 0x1C, 0x1D, 0x0E, 0x1B, 0x24, 0x22, 0x18, 0x1E, 0x1B, 0x24, 0x17, 0x0A,
        0x16, 0x0E
    };

    private static readonly byte[] _eliminateStr = new byte[]
        { 0x0E, 0x15, 0x12, 0x16, 0x12, 0x17, 0x0A, 0x1D, 0x12, 0x18, 0x17, 0x24, 0x16, 0x18, 0x0D, 0x0E };

    public override void Draw()
    {
        Graphics.Begin();

        Graphics.Clear(SKColors.Black);

        GlobalFunctions.DrawBox(0x18, 0x40, 0xD0, 0x90);

        GlobalFunctions.DrawString(_selectStr, 0x40, 0x28, 0);
        GlobalFunctions.DrawString(_nameStr, 0x50, 0x40, 0);
        GlobalFunctions.DrawString(_lifeStr, 0x98, 0x40, 0);
        GlobalFunctions.DrawString(_registerStr, 0x30, 0xA8, 0);
        GlobalFunctions.DrawString(_eliminateStr, 0x30, 0xB8, 0);

        var y = 0x58;
        for (var i = 0; i < 3; i++)
        {
            var summary = summaries.Summaries[i];
            if (summary.IsActive())
            {
                var numBuf = new byte[3].AsSpan();
                GlobalFunctions.NumberToStringR(summary.Deaths, NumberSign.None, ref numBuf);
                GlobalFunctions.DrawString(numBuf, 0x48, y + 8, 0);
                GlobalFunctions.DrawString(summary.Name, 0x48, y, 0);
                var totalHearts = summary.HeartContainers;
                var heartsValue = PlayerProfile.GetMaxHeartsValue(totalHearts);
                GlobalFunctions.DrawHearts(heartsValue, totalHearts, 0x90, y + 8);
                GlobalFunctions.DrawFileIcon(0x30, y, summary.Quest);
            }
            GlobalFunctions.DrawChar(Char.Minus, 0x88, y, 0);
            y += 24;
        }

        if (selectedIndex < 3)
            y = 0x58 + selectedIndex * 24 + 5;
        else
            y = 0xA8 + (selectedIndex - 3) * 16;
        GlobalFunctions.DrawChar(Char.FullHeart, 0x28, y, (Palette)7);

        Graphics.End();
    }

    private void SelectNext()
    {
        do
        {
            selectedIndex++;
            if (selectedIndex >= 5) selectedIndex = 0;
        } while (selectedIndex < 3 && !summaries.Summaries[selectedIndex].IsActive());
    }
}

internal sealed class EliminateMenu : Menu
{
    private readonly Game game;

    public ProfileSummarySnapshot summaries;
    public int selectedIndex;

    public EliminateMenu(Game game, ProfileSummarySnapshot summaries)
    {
        this.game = game;
        this.summaries = summaries;
    }

    public override void Draw()
    {
        throw new NotImplementedException();
    }

    public override void Update()
    {
        throw new NotImplementedException();
    }
}

internal sealed class RegisterMenu : Menu
{

    private static readonly byte[] _registerStr = new byte[]
    {
        0x6A, 0x6A, 0x6A, 0x6A,
        0x1B, 0x0E, 0x10, 0x12, 0x1C, 0x1D, 0x0E, 0x1B, 0x24, 0x22, 0x18, 0x1E, 0x1B, 0x24, 0x17, 0x0A,
        0x16, 0x0E,
        0x6A, 0x6A, 0x6A,
    };

    private static readonly byte[] _registerEndStr = new byte[]
        { 0x1B, 0x0E, 0x10, 0x12, 0x1C, 0x1D, 0x0E, 0x1B, 0x24, 0x24, 0x24, 0x24, 0x0E, 0x17, 0x0D };

    private static readonly byte[] _charSetStrBlank = new byte[]
    {
        0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60, 0x60,
        0x60, 0x60, 0x60, 0x60, 0x60
    };

    private static readonly byte[] _charSetStr0 = new byte[]
    {
        0x0A, 0x60, 0x0B, 0x60, 0x0C, 0x60, 0x0D, 0x60, 0x0E, 0x60, 0x0F, 0x60, 0x10, 0x60, 0x11, 0x60,
        0x12, 0x60, 0x13, 0x60, 0x14
    };

    private static readonly byte[] _charSetStr1 = new byte[]
    {
        0x15, 0x60, 0x16, 0x60, 0x17, 0x60, 0x18, 0x60, 0x19, 0x60, 0x1A, 0x60, 0x1B, 0x60, 0x1C, 0x60,
        0x1D, 0x60, 0x1E, 0x60, 0x1F
    };

    private static readonly byte[] _charSetStr2 = new byte[]
    {
        0x20, 0x60, 0x21, 0x60, 0x22, 0x60, 0x23, 0x60, 0x62, 0x60, 0x63, 0x60, 0x28, 0x60, 0x29, 0x60,
        0x2A, 0x60, 0x2B, 0x60, 0x2C
    };

    private static readonly byte[] _charSetStr3 = new byte[]
    {
        0x00, 0x60, 0x01, 0x60, 0x02, 0x60, 0x03, 0x60, 0x04, 0x60, 0x05, 0x60, 0x06, 0x60, 0x07, 0x60,
        0x08, 0x60, 0x09, 0x60, 0x24
    };

    private static readonly byte[][] _charSetStrs = new byte[][]
    {
        _charSetStr0,
        _charSetStrBlank,
        _charSetStr1,
        _charSetStrBlank,
        _charSetStr2,
        _charSetStrBlank,
        _charSetStr3,
    };

    private readonly Game _game;
    private ProfileSummarySnapshot _summaries;
    private int _selectedIndex;
    private int _namePos;
    private int _charPosCol;
    private int _charPosRow;
    private bool[] _origActive = new bool[SaveFolder.MaxProfiles];

    public RegisterMenu(Game game, ProfileSummarySnapshot summaries)
    {
        _game = game;
        _summaries = summaries;
    }

    void SelectNext()
    {
        do
        {
            _selectedIndex++;
            if (_selectedIndex >= 4)
                _selectedIndex = 0;
        } while (_selectedIndex < 3 && _origActive[_selectedIndex]);
    }

    void MoveNextNamePosition()
    {
        _namePos++;
        if (_namePos >= PlayerProfile.MaxNameLength)
            _namePos = 0;
    }

    void AddCharToName(byte ch)
    {
        var summary = _summaries.Summaries[_selectedIndex];
        if (summary.NameLength == 0)
        {
            Array.Fill(summary.Name, (byte)Char.Space);
            summary.NameLength = PlayerProfile.MaxNameLength;
            summary.HeartContainers = PlayerProfile.DefaultHearts;
        }
        summary.Name[_namePos] = ch;
        MoveNextNamePosition();
    }

    byte GetSelectedChar()
    {
        var ch = _charSetStrs[_charPosRow][_charPosCol];
        return ch;
    }

    void MoveCharSetCursorH(int dir)
    {
        var fullSize = _charSetStr0.Length * _charSetStrs.Length;

        for (var i = 0; i < fullSize; i++)
        {
            _charPosCol += dir;

            if (_charPosCol < 0)
            {
                _charPosCol = _charSetStr0.Length - 1;
                MoveCharSetCursorV(-1, false);
            }
            else if (_charPosCol >= _charSetStr0.Length)
            {
                _charPosCol = 0;
                MoveCharSetCursorV(1, false);
            }

            if (GetSelectedChar() != 0x60)
                break;
        }
    }

    void MoveCharSetCursorV(int dir, bool skip = true)
    {
        for (var i = 0; i < _charSetStrs.Length; i++)
        {
            _charPosRow += dir;

            if (_charPosRow < 0)
            {
                _charPosRow = _charSetStrs.Length - 1;
            }
            else if (_charPosRow >= _charSetStrs.Length)
            {
                _charPosRow = 0;
            }

            if (GetSelectedChar() != 0x60 || !skip)
                break;
        }
    }

    private static readonly byte[] _quest2Name = new byte[] { 0x23, 0x0E, 0x15, 0x0D, 0x0A, 0x24, 0x24, 0x24 };

    void CommitFiles()
    {
        for (var i = 0; i < SaveFolder.MaxProfiles; i++)
        {
            if (!_origActive[i] && _summaries.Summaries[i].IsActive())
            {
                var summary = _summaries.Summaries[i];
                PlayerProfile profile = new();

                if (summary.Name.SequenceEqual(_quest2Name))
                {
                    summary.Quest = 1;
                }

                profile.NameLength = summary.NameLength;
                Buffer.BlockCopy(summary.Name, 0, profile.Name, 0, summary.NameLength);
                profile.Quest = summary.Quest;
                profile.Items[ItemSlot.HeartContainers] = summary.HeartContainers;
                profile.Items[ItemSlot.MaxBombs] = PlayerProfile.DefaultBombs;
                // Leave deaths set 0.
                SaveFolder.WriteProfile(i, profile);
            }
        }
    }

    public override void Update()
    {
        var inTextEntry = _selectedIndex < 3;

        if (_game.Input.IsButtonPressing(Button.Select))
        {
            SelectNext();
            _namePos = 0;
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(Button.Start))
        {
            if (_selectedIndex == 3)
            {
                CommitFiles();
                _game.World.ChooseFile(_summaries);
            }
        }
        else if (_game.Input.IsButtonPressing(Button.A))
        {
            if (inTextEntry)
            {
                AddCharToName(GetSelectedChar());
                _game.Sound.PlayEffect(SoundEffect.PutBomb);
            }
        }
        else if (_game.Input.IsButtonPressing(Button.B))
        {
            if (inTextEntry)
            {
                MoveNextNamePosition();
            }
        }
        else if (_game.Input.IsButtonPressing(Button.Right))
        {
            MoveCharSetCursorH(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(Button.Left))
        {
            MoveCharSetCursorH(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(Button.Down))
        {
            MoveCharSetCursorV(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(Button.Up))
        {
            MoveCharSetCursorV(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }

        if (_game.Enhancements && inTextEntry)
        {
            foreach (var c in _game.Input.GetCharactersPressing())
            {
                AddCharToName((byte)(char.ToLower(c) - (byte)'a' + 0x0A));
                _game.Sound.PlayEffect(SoundEffect.PutBomb);
            }
        }
    }

    public override void Draw()
    {
        Graphics.Begin();
        Graphics.Clear(SKColors.Black);

        int y;

        if (_selectedIndex < 3)
        {
            var showCursor = ((_game.GetFrameCounter() >> 3) & 1) != 0;
            if (showCursor)
            {
                var x = 0x70 + (_namePos * 8);
                y = 0x30 + (_selectedIndex * 24);
                GlobalFunctions.DrawChar((byte)0x25, x, y, (Palette)7);

                x = 0x30 + (_charPosCol * 8);
                y = 0x88 + (_charPosRow * 8);
                GlobalFunctions.DrawChar((byte)0x25, x, y, (Palette)7);
            }
        }

        GlobalFunctions.DrawBox(0x28, 0x80, 0xB8, 0x48);
        GlobalFunctions.DrawString(_registerStr, 0x20, 0x18, 0);
        GlobalFunctions.DrawString(_registerEndStr, 0x50, 0x78, 0);

        y = 0x88;
        for (var i = 0; i < _charSetStrs.Length; i++, y += 8)
        {
            GlobalFunctions.DrawString(_charSetStrs[i], 0x30, y, 0);
        }

        y = 0x30;
        for (var i = 0; i < 3; i++)
        {
            var summary = _summaries.Summaries[i];
            GlobalFunctions.DrawString(summary.Name[..summary.NameLength], 0x70, y, 0);
            GlobalFunctions.DrawFileIcon(0x50, y, 0);
            y += 24;
        }

        if (_selectedIndex < 3)
            y = 0x30 + _selectedIndex * 24 + 4;
        else
            y = 0x78 + (_selectedIndex - 3) * 16;
        GlobalFunctions.DrawChar(Char.FullHeart, 0x44, y, (Palette)7);

        Graphics.End();
    }

}
