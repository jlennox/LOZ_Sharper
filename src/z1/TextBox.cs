namespace z1;

internal sealed class TextBox
{
    public const int StartX = 0x20;
    public const int StartY = 0x68;
    public const int CharDelay = 6;

    private int _left = StartX;
    private int _top = StartY;
    private int _height = 8;
    private readonly int _charDelay;
    private int _charTimer;
    private bool _drawingDialog = true;
    private readonly Game _game;
    private string _text;
    private int _currentTextIndex;

    public TextBox(Game game, string text, int delay)
    {
        _game = game;
        _text = text;
        _charDelay = Game.Cheats.SpeedUp ? 1 : delay;
        if (_charDelay < 1) _charDelay = 1;
    }

    public TextBox(Game game, byte[] text, int delay)
        : this(game, GameString.FromBytes(text), delay)
    {
        _game = game;
        _text = GameString.FromBytes(text);
        _charDelay = Game.Cheats.SpeedUp ? 1 : delay;
        if (_charDelay < 1) _charDelay = 1;
    }

    public TextBox(Game game, string text)
        : this(game, text, CharDelay - game.Enhancements.TextSpeed + 1)
    {
    }

    public TextBox(Game game, byte[] text)
        : this(game, text, CharDelay - game.Enhancements.TextSpeed + 1)
    {
    }

    public void Reset(string text)
    {
        _drawingDialog = true;
        _charTimer = 0;
        _text = text;
        _currentTextIndex = 0;
    }

    public void Reset(byte[] text)
    {
        Reset(GameString.FromBytes(text));
    }

    public bool IsDone() => !_drawingDialog;
    public int GetHeight() => _height;
    public int GetX() => _left;
    public int GetY() => _top;
    public void SetX(int x) => _left = x;
    public void SetY(int y) => _top = y;

    public void Update()
    {
        if (!_drawingDialog) return;

        if (_charTimer != 0)
        {
            _charTimer--;
            return;
        }

        // Skip over the space characters.
        for (; _currentTextIndex < _text.Length; _currentTextIndex++)
        {
            var curCharPtr = _text[_currentTextIndex];
            switch (curCharPtr)
            {
                case '\n': _height += 8; continue;
                case ' ': continue;
            }
            _currentTextIndex++;
            _game.Sound.PlayEffect(SoundEffect.Character);
            break;
        }
        _drawingDialog = _currentTextIndex != _text.Length;
        _charTimer = _charDelay - 1;
    }

    public void Draw()
    {
        var x = _left;
        var y = _top;

        for (var i = 0; i < _currentTextIndex; i++ )
        {
            var chr = _text[i];

            if (chr == '\n')
            {
                x = StartX;
                y += 8;
                continue;
            }

            if (chr != (int)Chars.JustSpace)
            {
                GlobalFunctions.DrawChar(chr, x, y, 0);
            }

            x += 8;
        }
    }
}