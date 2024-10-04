using System.Collections.Immutable;
using SkiaSharp;
using z1.IO;
using z1.Render;

namespace z1.UI;

internal abstract class Menu
{
    public abstract void Update();
    public abstract void Draw();

    protected static void DrawFileIcon(PlayerProfile profile, int x, int y, int quest)
    {
        if (quest == 1)
        {
            var sword = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.SwordItem);
            sword.Draw(TileSheet.PlayerAndItems, x + 12, y - 3, (Palette)7);
        }

        profile.SetPlayerColor();
        var player = Graphics.GetSpriteImage(TileSheet.PlayerAndItems, AnimationId.PlayerWalk_NoShield_Down);
        player.Draw(TileSheet.PlayerAndItems, x, y, Palette.Player);
    }
}

internal sealed class PregameMenu : Menu
{
    public bool IsActive { get; private set; }

    private readonly Game _game;
    private readonly List<PlayerProfile> _profiles;
    private Menu _currentMenu;

    public PregameMenu(Game game, List<PlayerProfile> profiles)
    {
        IsActive = true;
        _game = game;
        _profiles = profiles;

        _currentMenu = new ProfileSelectMenu(_game, this, _profiles);
    }

    public override void Update() => _currentMenu.Update();
    public override void Draw() => _currentMenu.Draw();

    public bool UpdateIfActive()
    {
        if (IsActive)
        {
            Update();
            return true;
        }

        return false;
    }

    public bool DrawIfActive()
    {
        if (IsActive)
        {
            Draw();
            return true;
        }

        return false;
    }

    public void StartWorld(PlayerProfile profile)
    {
        IsActive = false;
        _game.Start(profile);
    }

    public void GotoFileMenu(int page = 0)
    {
        IsActive = true;
        _currentMenu = new ProfileSelectMenu(_game, this, _profiles, page);
    }

    public void GotoRegisterMenu()
    {
        IsActive = true;
        _currentMenu = new RegisterMenu(_game, this, _profiles);
    }

    public void GotoEliminateMenu(int page)
    {
        IsActive = true;
        _currentMenu = new EliminateMenu(_game, this, _profiles, page);
    }
}

internal sealed class ProfileSelectMenu : Menu
{
    private const int MaxProfiles = SaveFolder.MaxProfiles;
    private const int RegisterIndex = MaxProfiles;
    private const int EliminateIndex = RegisterIndex + 1;
    private const int FinalIndex = EliminateIndex + 1;

    private static readonly Rectangle _mainBox = new(0x18, 0x40, 0xD0, 0x90);

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
    private readonly List<PlayerProfile> _profiles;
    private readonly Game _game;
    private readonly PregameMenu _menu;
    private int _page = 0;
    private int _pageCount = 0;
    private string _pageString = "";
    private readonly string _menuStr = "press alt for menu";

    public ProfileSelectMenu(Game game, PregameMenu menu, List<PlayerProfile> profiles, int page = 0)
    {
        _game = game;
        _menu = menu;
        _profiles = profiles;

        _menuStr = GetCentered(_menuStr);

        for (var i = 0; i < _palettes.Length; i++)
        {
            Graphics.SetPaletteIndexed((Palette)i, _palettes[i]);
        }

        // So that characters are fully opaque.
        Graphics.SetColor(0, 0, 0xFF000000);
        Graphics.UpdatePalettes();

        SetPage(page);
    }

    private static string GetCentered(string s)
    {
        var padding = (int)((_mainBox.Width / 8f) / 2f - s.Length / 2f);
        return new string(' ', padding) + s;
    }

    private void SetPage(int direction)
    {
        var page = _page + direction;
        _pageCount = _profiles.Count / SaveFolder.MaxProfiles + 1;
        _page = (int)((uint)page % _pageCount);
        _pageString = GetCentered($"< Page {_page + 1}/{_pageCount} >");

        if (!_profiles.GetProfile(_page, _selectedIndex).IsActive())
        {
            _selectedIndex = 0;
            SelectFirst();
        }
    }

    public override void Update()
    {
        if (_game.Input.IsAnyButtonPressing(GameButton.Select, GameButton.Down))
        {
            SelectNext(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Up))
        {
            SelectNext(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        if (_game.Input.IsButtonPressing(GameButton.Left))
        {
            SetPage(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Right))
        {
            SetPage(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Start))
        {
            switch (_selectedIndex)
            {
                case < MaxProfiles: _menu.StartWorld(_profiles.GetProfile(_page, _selectedIndex)); break;
                case RegisterIndex: _menu.GotoRegisterMenu(); break;
                case EliminateIndex: _menu.GotoEliminateMenu(_page); break;
            }
        }
    }

    public override void Draw()
    {
        Graphics.Begin();

        Graphics.Clear(SKColors.Black);
        GlobalFunctions.DrawBox(_mainBox);

        GlobalFunctions.DrawString("- s e l e c t -", 0x40, 0x28, 0);
        GlobalFunctions.DrawString(_pageString, _mainBox.X, _mainBox.Bottom + 4, 0);
        GlobalFunctions.DrawString(_menuStr, _mainBox.X, _mainBox.Bottom + 16, 0);
        GlobalFunctions.DrawString(" name ", 0x50, 0x40, 0);
        GlobalFunctions.DrawString(" life ", 0x98, 0x40, 0);
        GlobalFunctions.DrawString("register your name", 0x30, 0xA8, 0);
        GlobalFunctions.DrawString("elimination mode", 0x30, 0xB8, 0);

        var y = 0x58;
        for (var i = 0; i < 3; i++)
        {
            var profile = _profiles.GetProfile(_page, i);
            if (profile != null && profile.IsActive())
            {
                GlobalFunctions.DrawString($"{profile.Deaths,3}", 0x48, y + 8, 0);
                GlobalFunctions.DrawString(profile.Name, 0x48, y, 0);
                var totalHearts = profile.GetItem(ItemSlot.HeartContainers);
                var heartsValue = PlayerProfile.GetMaxHeartsValue(totalHearts);
                GlobalFunctions.DrawHearts(heartsValue, totalHearts, 0x90, y + 8);
                DrawFileIcon(profile, 0x30, y, profile.Quest);
            }
            GlobalFunctions.DrawChar(Chars.Minus, 0x88, y, 0);
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
        GlobalFunctions.DrawChar(Chars.FullHeart, 0x28, y, (Palette)7);

        Graphics.End();
    }

    private void SelectNext(int direction)
    {
        do
        {
            _selectedIndex += direction;
            if (_selectedIndex >= FinalIndex) _selectedIndex = 0;
            if (_selectedIndex < 0) _selectedIndex = FinalIndex - 1;
        } while (_selectedIndex < MaxProfiles && !_profiles.GetProfile(_page, _selectedIndex).IsActive());
    }

    private void SelectFirst()
    {
        for (var i = 0; i < SaveFolder.MaxProfiles; i++)
        {
            if (_profiles.GetProfile(_page, _selectedIndex).IsActive())
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
    private readonly PregameMenu _menu;
    private readonly List<PlayerProfile> _profiles;
    private readonly int _page;
    private int _selectedIndex = -1; // Account for first SelectNext. JOE: TODO: Recode all menu's into generic selection API.

    public EliminateMenu(Game game, PregameMenu menu, List<PlayerProfile> profiles, int page)
    {
        _game = game;
        _menu = menu;
        _profiles = profiles;
        _page = page;

        SelectNext(1);
    }

    private void SelectNext(int direction)
    {
        do
        {
            _selectedIndex += direction;
            if (_selectedIndex >= 4) _selectedIndex = 0;
            if (_selectedIndex < 0) _selectedIndex = 3;
        } while (_selectedIndex < SaveFolder.MaxProfiles && !_profiles[_selectedIndex].IsActive());
    }

    private void DeleteCurrentProfile()
    {
        var index = _profiles.GetIndex(_page, _selectedIndex);
        _profiles.RemoveAt(index);
        SaveFolder.SaveProfiles();
        _game.Sound.PlayEffect(SoundEffect.PlayerHit);
    }

    public override void Update()
    {
        if (_game.Input.IsAnyButtonPressing(GameButton.Select, GameButton.Down))
        {
            SelectNext(1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Up))
        {
            SelectNext(-1);
            _game.Sound.PlayEffect(SoundEffect.Cursor);
        }
        else if (_game.Input.IsButtonPressing(GameButton.Start))
        {
            switch (_selectedIndex)
            {
                case < SaveFolder.MaxProfiles: DeleteCurrentProfile(); break;
                case SaveFolder.MaxProfiles: _menu.GotoFileMenu(_page); break;
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
            var profile = _profiles.GetProfile(_page, i);
            if (profile != null && profile.IsActive())
            {
                GlobalFunctions.DrawString(profile.Name, 0x70, y, 0);
                DrawFileIcon(profile, 0x50, y, profile.Quest);
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
        GlobalFunctions.DrawChar(Chars.FullHeart, 0x44, y, Palette.SeaPal);

        Graphics.End();
    }
}

internal sealed class RegisterMenu : Menu
{
    private const string Quest2Name = "zelda";
    private const string RegisterEndStr = "Press Start To Register";

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
    private readonly PregameMenu _menu;
    private readonly List<PlayerProfile> _profiles;
    private readonly PlayerProfile _profile;
    private int _namePos;
    private int _charPosCol;
    private int _charPosRow;

    public RegisterMenu(Game game, PregameMenu menu, List<PlayerProfile> profiles)
    {
        _game = game;
        _menu = menu;
        _profiles = profiles;

        _profile = new PlayerProfile();
        _profile.Initialize();
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
        if (_profile.Name == null)
        {
            _profile.Name = "";
            _profile.Hearts = PlayerProfile.DefaultHeartCount;
        }
        _profile.Name += ch;
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
        _profiles.Add(_profile);
        // JOE: TODO: Move to be profile method.
        if (_profile.Name.IEquals(Quest2Name))
        {
            _profile.Quest = 1;
        }

        _profile.Name = _profile.Name ?? throw new Exception("name missing."); // JOE: TODO: Uhhh :)
        _profile.Quest = _profile.Quest;
        SaveFolder.SaveProfiles();
    }

    public override void Update()
    {
        if (_game.Input.IsButtonPressing(GameButton.Start))
        {
            CommitFiles();
            _menu.GotoFileMenu();
        }

        if (_game.Input.IsButtonPressing(GameButton.A))
        {
            AddCharToName(GetSelectedChar());
            _game.Sound.PlayEffect(SoundEffect.PutBomb);
        }
        else if (_game.Input.IsButtonPressing(GameButton.B))
        {
            // JOE: I hate this :)
            // MoveNextNamePosition();
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

        if (_game.Enhancements.ImprovedMenus)
        {
            foreach (var c in _game.Input.GetCharactersPressing())
            {
                if (c != '\0')
                {
                    AddCharToName(c);
                    _game.Sound.PlayEffect(SoundEffect.PutBomb);
                }
            }
        }
    }

    public override void Draw()
    {
        Graphics.Begin();
        Graphics.Clear(SKColors.Black);

        int y;
        const int nameX = 0x28 + 8 + 16;

        var showCursor = ((_game.FrameCounter >> 3) & 1) != 0;
        if (showCursor)
        {
            var x = nameX + (_namePos * 8);
            y = 0x30 + (0 * 24);
            GlobalFunctions.DrawChar(Chars.JustSpace, x, y, (Palette)7);

            x = 0x30 + (_charPosCol * 8);
            y = 0x88 + (_charPosRow * 8);
            GlobalFunctions.DrawChar(Chars.JustSpace, x, y, (Palette)7);
        }

        GlobalFunctions.DrawBox(0x28, 0x80, 0xB8, 0x48);
        if (_game.Enhancements.ImprovedMenus)
        {
            GlobalFunctions.DrawString("Type or input name", 0x28, 0x68, 0);
        }
        GlobalFunctions.DrawString(RegisterEndStr, 0x28, 0x78, 0);

        y = 0x88;
        for (var i = 0; i < _charSetStrs.Length; i++, y += 8)
        {
            GlobalFunctions.DrawString(_charSetStrs[i], 0x30, y, 0, DrawingFlags.None);
        }

        y = 0x30;
        GlobalFunctions.DrawString(_profile.Name, nameX, y, 0);
        GlobalFunctions.DrawChar(Chars.FullHeart, nameX - 16, y, (Palette)7);

        Graphics.End();
    }
}
