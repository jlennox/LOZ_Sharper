using System.Collections.Immutable;
using SkiaSharp;
using z1.IO;

namespace z1.UI;

internal abstract class Menu
{
    public abstract void Update();
    public abstract void Draw();
}

internal sealed class ProfileSelectMenu : Menu
{
    private const int MaxProfiles = SaveFolder.MaxProfiles;
    private const int RegisterIndex = MaxProfiles;
    private const int EliminateIndex = RegisterIndex + 1;
    private const int FinalIndex = EliminateIndex + 1;

    private static readonly ImmutableArray<ImmutableArray<byte>> _palettes = [
        [0x0F, 0x30, 0x00, 0x12],
        [0x0F, 0x16, 0x27, 0x36],
        [0x0F, 0x0C, 0x1C, 0x2C],
        [0x0F, 0x12, 0x1C, 0x2C],
        [0x00, 0x29, 0x27, 0x07],
        [0x00, 0x29, 0x27, 0x07],
        [0x00, 0x29, 0x27, 0x07],
        [0x00, 0x15, 0x27, 0x30]
    ];

    private int _selectedIndex;
    private readonly PlayerProfile[] _summaries;
    private readonly Game _game;

    public ProfileSelectMenu(Game game, PlayerProfile[] summaries)
    {
        _game = game;
        _summaries = summaries;

        for (var i = 0; i < _palettes.Length; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, _palettes[i]);
        }

        // So that characters are fully opaque.
        Graphics.SetColor(0, 0, 0xFF000000);
        Graphics.UpdatePalettes();

        SelectFirst();
    }

    private void StartWorld(PlayerProfile profile)
    {
        _game.World.Start(profile);
    }

    public override void Update()
    {
        if (_game.Input.IsButtonPressing(GameButton.Select))
        {
            SelectNext();
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Start))
        {
            switch (_selectedIndex)
            {
                case < MaxProfiles: StartWorld(_summaries[_selectedIndex]); break;
                case RegisterIndex: _game.World.RegisterFile(_summaries); break;
                case EliminateIndex: _game.World.EliminateFile(_summaries); break;
            }
        }
    }

    public override void Draw()
    {
        Graphics.Begin();

        Graphics.Clear(SKColors.Black);
        GlobalFunctions.DrawBox(0x18, 0x40, 0xD0, 0x90);

        // JOE: TODO: Use normal strings.
        GlobalFunctions.DrawString("- s e l e c t  -", 0x40, 0x28, 0);
        GlobalFunctions.DrawString(" name ", 0x50, 0x40, 0);
        GlobalFunctions.DrawString(" life ", 0x98, 0x40, 0);
        GlobalFunctions.DrawString("register your name", 0x30, 0xA8, 0);
        GlobalFunctions.DrawString("elimination mode", 0x30, 0xB8, 0);

        var y = 0x58;
        for (var i = 0; i < 3; i++)
        {
            var summary = _summaries[i];
            if (summary.IsActive())
            {
                var numBuf = new byte[3].AsSpan();
                GlobalFunctions.NumberToStringR(summary.Deaths, NumberSign.None, ref numBuf);
                GlobalFunctions.DrawString(numBuf, 0x48, y + 8, 0);
                GlobalFunctions.DrawString(summary.Name, 0x48, y, 0);
                var totalHearts = summary.GetItem(ItemSlot.HeartContainers);
                var heartsValue = PlayerProfile.GetMaxHeartsValue(totalHearts);
                GlobalFunctions.DrawHearts(heartsValue, totalHearts, 0x90, y + 8);
                GlobalFunctions.DrawFileIcon(0x30, y, summary.Quest);
            }
            GlobalFunctions.DrawChar(Char.Minus, 0x88, y, 0);
            y += 24;
        }

        if (_selectedIndex < 3)
        {
            y = 0x58 + _selectedIndex * 24 + 5;
        }
        else
        {
            y = 0xA8 + (_selectedIndex - MaxProfiles) * 16;
        }
        GlobalFunctions.DrawChar(Char.FullHeart, 0x28, y, (Palette)7);

        Graphics.End();
    }

    private void SelectNext()
    {
        do
        {
            _selectedIndex++;
            if (_selectedIndex >= FinalIndex) _selectedIndex = 0;
        } while (_selectedIndex < MaxProfiles && !_summaries[_selectedIndex].IsActive());
    }

    private void SelectFirst()
    {
        for (var i = 0; i < SaveFolder.MaxProfiles; i++)
        {
            if (_summaries[i].IsActive())
            {
                _selectedIndex = i;
                return;
            }
        }

        _selectedIndex = RegisterIndex;
    }
}

internal sealed class EliminateMenu : Menu
{
    private readonly Game _game;
    private readonly PlayerProfile[] _summaries;
    private int _selectedIndex;

    public EliminateMenu(Game game, PlayerProfile[] summaries)
    {
        _game = game;
        _summaries = summaries;

        SelectNext();
    }

    private void SelectNext()
    {
        do
        {
            _selectedIndex++;
            if (_selectedIndex >= 4) _selectedIndex = 0;
        } while (_selectedIndex < SaveFolder.MaxProfiles && !_summaries[_selectedIndex].IsActive());
    }

    private void DeleteCurrentProfile()
    {
        _summaries[_selectedIndex].Name = null;
        SaveFolder.SaveProfiles();
        _game.Sound.PlayEffect(SoundEffect.PlayerHit);
    }

    public override void Update()
    {
        if (_game.Input.IsButtonPressing(GameButton.Select))
        {
            SelectNext();
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Start))
        {
            switch (_selectedIndex)
            {
                case < SaveFolder.MaxProfiles: DeleteCurrentProfile(); break;
                case SaveFolder.MaxProfiles: _game.World.ChooseFile(_summaries); break;
            }
        }
    }

    public override void Draw()
    {
        Graphics.Begin();

        Graphics.Clear(SKColors.Black);

        const char hor = (char)StringChar.BoxHorizontal;
        GlobalFunctions.DrawString($"{hor}{hor}{hor}Elimination  Mode{hor}{hor}{hor}", 0x20, 0x18, 0);
        GlobalFunctions.DrawString("Elimination End", 0x50, 0x78, 0);

        var y = 0x30;
        for (var i = 0; i < 3; i++)
        {
            var summary = _summaries[i];
            if (summary.IsActive())
            {
                GlobalFunctions.DrawString(summary.Name, 0x70, y, 0);
                GlobalFunctions.DrawFileIcon(0x50, y, summary.Quest);
            }
            y += 24;
        }

        if (_selectedIndex < 3)
        {
            y = 0x30 + _selectedIndex * 24 + 4;
        }
        else
        {
            y = 0x78 + (_selectedIndex - 3) * 16;
        }
        GlobalFunctions.DrawChar(Char.FullHeart, 0x44, y, Palette.LevelFgPalette);

        Graphics.End();
    }
}

internal sealed class RegisterMenu : Menu
{
    private const string Quest2Name = "zelda";
    private const string RegisterStr = "    register your name";
    private const string RegisterEndStr = "register    end";

    private const string CharSetStrBlank = "                     ";
    private const string CharSetStr0 = "A B C D E F G H I J K";
    private const string CharSetStr1 = "L M N O P Q R S T U V";
    private const string CharSetStr2 = "W X Y Z - . , ! ' & .";
    private const string CharSetStr3 = "0 1 2 3 4 5 6 7 8 9  ";

    private static readonly ImmutableArray<string> _charSetStrs = [
        CharSetStr0,
        CharSetStrBlank,
        CharSetStr1,
        CharSetStrBlank,
        CharSetStr2,
        CharSetStrBlank,
        CharSetStr3,
    ];

    private readonly Game _game;
    private readonly PlayerProfile[] _summaries;
    private int _selectedProfileIndex;
    private int _namePos;
    private int _charPosCol;
    private int _charPosRow;
    private readonly bool[] _origActive = new bool[SaveFolder.MaxProfiles];

    public RegisterMenu(Game game, PlayerProfile[] summaries)
    {
        _game = game;
        _summaries = summaries;
    }

    private void SelectNext()
    {
        do
        {
            _selectedProfileIndex++;
            if (_selectedProfileIndex >= 4)
            {
                _selectedProfileIndex = 0;
            }
        } while (_selectedProfileIndex < SaveFolder.MaxProfiles && _origActive[_selectedProfileIndex]);
    }

    private void MoveNextNamePosition()
    {
        _namePos++;
        if (_namePos >= PlayerProfile.MaxNameLength)
        {
            _namePos = 0;
        }
    }

    private void AddCharToName(char ch)
    {
        var summary = _summaries[_selectedProfileIndex];
        if (summary.Name == null)
        {
            summary.Name = "";
            summary.Hearts = PlayerProfile.DefaultHearts;
        }
        summary.Name += ch;
        MoveNextNamePosition();
    }

    private char GetSelectedChar()
    {
        return _charSetStrs[_charPosRow][_charPosCol];
    }

    private void MoveCharSetCursorH(int dir)
    {
        var fullSize = CharSetStr0.Length * _charSetStrs.Length;

        for (var i = 0; i < fullSize; i++)
        {
            _charPosCol += dir;

            if (_charPosCol < 0)
            {
                _charPosCol = CharSetStr0.Length - 1;
                MoveCharSetCursorV(-1, false);
            }
            else if (_charPosCol >= CharSetStr0.Length)
            {
                _charPosCol = 0;
                MoveCharSetCursorV(1, false);
            }

            if (GetSelectedChar() != ' ') break;
        }
    }

    private void MoveCharSetCursorV(int dir, bool skip = true)
    {
        foreach (var _ in _charSetStrs)
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

            if (GetSelectedChar() != ' ' || !skip) break;
        }
    }

    private void CommitFiles()
    {
        for (var i = 0; i < SaveFolder.MaxProfiles; i++)
        {
            if (!_origActive[i] && _summaries[i].IsActive())
            {
                var profile = _summaries[i];
                // JOE: TODO: Move to be profile method, make it case insensitive.
                if (profile.Name.IEquals(Quest2Name))
                {
                    profile.Quest = 1;
                }

                profile.Name = profile.Name ?? throw new Exception("name missing."); // JOE: TODO: Uhhh :)
                profile.Quest = profile.Quest;
                profile.Items[ItemSlot.HeartContainers] = PlayerProfile.DefaultHearts;
                profile.Items[ItemSlot.MaxBombs] = PlayerProfile.DefaultBombs;
                // Leave deaths set 0.
                SaveFolder.SaveProfiles();
            }
        }
    }

    public override void Update()
    {
        var inTextEntry = _selectedProfileIndex < SaveFolder.MaxProfiles;

        if (_game.Input.IsButtonPressing(GameButton.Select))
        {
            SelectNext();
            _namePos = 0;
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Start))
        {
            if (_selectedProfileIndex == SaveFolder.MaxProfiles)
            {
                CommitFiles();
                _game.World.ChooseFile(_summaries);
            }
        }
        else if (_game.Input.IsButtonPressing(GameButton.A))
        {
            if (inTextEntry)
            {
                AddCharToName(GetSelectedChar());
                _game.Sound.PlayEffect(SoundEffect.PutBomb);
            }
        }
        else if (_game.Input.IsButtonPressing(GameButton.B))
        {
            if (inTextEntry)
            {
                MoveNextNamePosition();
            }
        }
        else if (_game.Input.IsButtonPressing(GameButton.Right))
        {
            MoveCharSetCursorH(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Left))
        {
            MoveCharSetCursorH(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Down))
        {
            MoveCharSetCursorV(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Up))
        {
            MoveCharSetCursorV(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }

        if (_game.Enhancements && inTextEntry)
        {
            foreach (var c in _game.Input.GetCharactersPressing())
            {
                AddCharToName(c);
                _game.Sound.PlayEffect(SoundEffect.PutBomb);
            }
        }
    }

    public override void Draw()
    {
        Graphics.Begin();
        Graphics.Clear(SKColors.Black);

        int y;

        if (_selectedProfileIndex < 3)
        {
            var showCursor = ((_game.GetFrameCounter() >> 3) & 1) != 0;
            if (showCursor)
            {
                var x = 0x70 + (_namePos * 8);
                y = 0x30 + (_selectedProfileIndex * 24);
                GlobalFunctions.DrawChar(0x25, x, y, (Palette)7);

                x = 0x30 + (_charPosCol * 8);
                y = 0x88 + (_charPosRow * 8);
                GlobalFunctions.DrawChar(0x25, x, y, (Palette)7);
            }
        }

        GlobalFunctions.DrawBox(0x28, 0x80, 0xB8, 0x48);
        GlobalFunctions.DrawString(RegisterStr, 0x20, 0x18, 0);
        GlobalFunctions.DrawString(RegisterEndStr, 0x50, 0x78, 0);

        y = 0x88;
        for (var i = 0; i < _charSetStrs.Length; i++, y += 8)
        {
            GlobalFunctions.DrawString(_charSetStrs[i], 0x30, y, 0, DrawingFlags.None);
        }

        y = 0x30;
        foreach (var summary in _summaries)
        {
            GlobalFunctions.DrawString(summary.Name, 0x70, y, 0);
            GlobalFunctions.DrawFileIcon(0x50, y, 0);
            y += 24;
        }

        if (_selectedProfileIndex < SaveFolder.MaxProfiles)
        {
            y = 0x30 + _selectedProfileIndex * 24 + 4;
        }
        else
        {
            y = 0x78 + (_selectedProfileIndex - SaveFolder.MaxProfiles) * 16;
        }
        GlobalFunctions.DrawChar(Char.FullHeart, 0x44, y, (Palette)7);

        Graphics.End();
    }
}
