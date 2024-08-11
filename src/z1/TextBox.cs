﻿namespace z1;

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
    private byte[] _startCharPtr;
    private int _curCharIndex;

    public TextBox(Game game, byte[] text, int delay)
    {
        _game = game;
        _startCharPtr = text;
        _charDelay = Game.Cheats.SpeedUp ? 1 : delay;
        if (_charDelay < 1) _charDelay = 1;
    }

    public TextBox(Game game, byte[] text)
        : this(game, text, CharDelay - game.Configuration.TextSpeed)
    {
    }

    public void Reset(byte[] text)
    {
        _drawingDialog = true;
        _charTimer = 0;
        _startCharPtr = text;
        _curCharIndex = 0;
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

        byte ch;
        do
        {
            var curCharPtr = _startCharPtr[_curCharIndex];
            ch = (byte)(curCharPtr & 0x3F);
            var attr = (byte)(curCharPtr & 0xC0);
            if (attr == 0xC0)
            {
                _drawingDialog = false;
            }
            else if (attr != 0)
            {
                _height += 8;
            }

            _curCharIndex++;
            if (ch != (int)Char.JustSpace)
            {
                _game.Sound.PlayEffect(SoundEffect.Character);
            }
        } while (_drawingDialog && ch == (int)Char.JustSpace);
        _charTimer = _charDelay - 1;
    }

    public void Draw()
    {
        var x = _left;
        var y = _top;

        for (var i = 0; i < _curCharIndex; i++ )
        {
            var chr = _startCharPtr[i];
            var attr = chr & 0xC0;
            var ch = chr & 0x3F;

            if (ch != (int)Char.JustSpace)
            {
                GlobalFunctions.DrawChar((byte)ch, x, y, 0);
            }

            if (attr == 0)
            {
                x += 8;
            }
            else
            {
                x = StartX;
                y += 8;
            }
        }
    }
}