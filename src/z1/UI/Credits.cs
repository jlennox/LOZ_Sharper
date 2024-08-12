using System.Runtime.InteropServices;
using z1.IO;

namespace z1.UI;

internal sealed class CreditsType
{
    public const int StartY = Global.StdViewHeight;
    private const int AllLines = 96;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct LineStruct : ILoadVariableLengthData<Line>
    {
        public byte Length;
        public byte Col;

        public readonly Line LoadVariableData(ReadOnlySpan<byte> buf)
        {
            var text = buf[..Length].ToArray();
            return new Line(Col, text);
        }
    }

    private record Line(byte Col, byte[] Text);

    private readonly Game _game;
    private readonly TableResource<LineStruct> _textTable;
    private readonly byte[] _lineBmp;
    private int _fraction;
    private int _tileOffset;
    private int _top = StartY;
    private int _windowTop = StartY;
    private int _windowTopLine;
    private int _windowBottomLine = 1;
    private int _windowFirstMappedLine;
    private byte[]? _playerLine;

    public CreditsType(Game game)
    {
        _game = game;
        _textTable = TableResource<LineStruct>.Load("credits.tab");
        _lineBmp = new Asset("creditsLinesBmp.dat").ReadAllBytes();
    }

    public bool IsDone() => _windowTopLine == GetTopLineAtEnd();
    public int GetTop() => _top;
    private int GetTopLineAtEnd() => _game.World.GetProfile().Quest == 0 ? 46 : 61;

    public void Update()
    {
        if (_windowTopLine == GetTopLineAtEnd()) return;

        _fraction++;
        if (_fraction == 2)
        {
            _fraction = 0;
            _tileOffset++;
            _windowTop--;
            _top--;

            if (_tileOffset >= 8)
            {
                _tileOffset -= 8;

                if (_windowTop < 0)
                {
                    _windowTop += 8;
                    if (_windowTopLine < (AllLines - 1))
                    {
                        var b = _windowTopLine / 8;
                        var bit = _windowTopLine % 8;
                        var show = _lineBmp[b] & (0x80 >> bit);
                        if (show != 0)
                        {
                            _windowFirstMappedLine++;
                        }
                        _windowTopLine++;
                    }
                }

                if (_windowBottomLine < AllLines)
                {
                    _windowBottomLine++;
                }
            }
        }
    }

    private byte[] GetPlayerLine(Line line)
    {
        var profile = _game.World.GetProfile();
        // JOE: TODO: I think you messed this up.
        _playerLine ??= ZeldaString.EnumerateText($"{ZeldaString.FromBytes(line.Text)} {profile.Name} {profile.Deaths}").ToArray();
        return _playerLine;
    }

    private static void DrawHorizWallLine(int x, int y, int length)
    {
        for (var i = 0; i < length; i++)
        {
            GlobalFunctions.DrawChar(0xFA, x, y, 0);
            x += 8;
        }
    }

    public void Draw()
    {
        var mappedLine = _windowFirstMappedLine;
        var y = _windowTop;
        var profile = _game.World.GetProfile();

        for (var i = _windowTopLine; i < _windowBottomLine; i++)
        {
            if (mappedLine >= _textTable.Length
                || (profile.Quest == 0 && mappedLine >= 0x10))
            {
                break;
            }

            var b = i / 8;
            var bit = i % 8;
            var show = _lineBmp[b] & (0x80 >> bit);
            var pal = 0;

            if (i is > 1 and < 44)
            {
                GlobalFunctions.DrawChar(0xFA, 24, y, 0);
                GlobalFunctions.DrawChar(0xFA, 224, y, 0);
                pal = ((i + 6) / 7) % 3 + 1;
            }
            else if (profile.Quest == 1)
            {
                pal = mappedLine switch {
                    13 => 1,
                    18 => 2,
                    _ => pal
                };
            }
            if (show != 0)
            {
                var effMappedLine = mappedLine;
                if (profile.Quest == 1 && mappedLine >= 12)
                {
                    effMappedLine += 4;
                }
                var line = _textTable.LoadVariableLengthData<LineStruct, Line>(effMappedLine);
                var text = line.Text;
                var x = line.Col * 8;
                if (profile.Quest == 1 && mappedLine == 13)
                {
                    text = GetPlayerLine(line);
                }
                GlobalFunctions.DrawString(text, x, y, (Palette)pal);
                mappedLine++;
            }
            if (i == 1)
            {
                DrawHorizWallLine(24, y, 10);
                DrawHorizWallLine(160, y, 9);
            }
            else if (i == 44)
            {
                DrawHorizWallLine(24, y, 26);
            }
            y += 8;
        }

        if (IsDone())
        {
            y = 0x80;
            GlobalFunctions.DrawItem(_game, ItemId.PowerTriforce, 0x78, y, 0);

            var pile = new SpriteImage(TileSheet.Boss, AnimationId.B3_Pile);
            pile.Draw(TileSheet.Boss, 0x78, y + 0, (Palette)7);
        }
    }
}
