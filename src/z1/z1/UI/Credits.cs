using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace z1.UI;

internal sealed class CreditsType
{
    public const int StartY = Global.StdViewHeight;

    private const int AllLines = 96;
    private const int AllLineBytes = 12;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct LineStruct : ILoadVariableLengthData<Line>
    {
        public byte Length;
        public byte Col;

        public readonly Line LoadVariableData(ReadOnlySpan<byte> buf)
        {
            var text = buf[..Length].ToArray();
            return new Line(Length, Col, text);
        }
    }

    private record Line(byte Length, byte Col, byte[] Text);

    private readonly Game _game;

    private TableResource<LineStruct> textTable;
    private byte[] lineBmp;
    private int fraction;
    private int tileOffset;
    private int top = StartY;
    private int windowTop = StartY;
    private int windowTopLine;
    private int windowBottomLine = 1;
    private int windowFirstMappedLine;
    private byte[]? playerLine;
    private bool madePlayerLine;

    public CreditsType(Game game)
    {
        _game = game;

        textTable = TableResource<LineStruct>.Load("credits.tab");
        lineBmp = Assets.ReadAllBytes("creditsLinesBmp.dat");
    }

    public bool IsDone() => windowTopLine == GetTopLineAtEnd();
    public int GetTop() => top;
    private int GetTopLineAtEnd() => _game.World.Profile.Quest == 0 ? 46 : 61;

    public void Update()
    {
        if (windowTopLine == GetTopLineAtEnd()) return;

        fraction++;
        if (fraction == 2)
        {
            fraction = 0;
            tileOffset++;
            windowTop--;
            top--;

            if (tileOffset >= 8)
            {
                tileOffset -= 8;

                if (windowTop < 0)
                {
                    windowTop += 8;
                    if (windowTopLine < (AllLines - 1))
                    {
                        var b = windowTopLine / 8;
                        var bit = windowTopLine % 8;
                        var show = lineBmp[b] & (0x80 >> bit);
                        if (show != 0)
                        {
                            windowFirstMappedLine++;
                        }
                        windowTopLine++;
                    }
                }

                if (windowBottomLine < AllLines)
                {
                    windowBottomLine++;
                }
            }
        }
    }

    private byte[] GetPlayerLine(Line line)
    {
        var profile = _game.World.Profile;
        playerLine ??= ZeldaString.ToBytes($"{ZeldaString.FromBytes(line.Text)} {profile.Name} {profile.Deaths}").ToArray();
        return playerLine;
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
        var mappedLine = windowFirstMappedLine;
        var y = windowTop;

        for (var i = windowTopLine; i < windowBottomLine; i++)
        {
            if ((mappedLine >= (int)textTable.Length)
                || (_game.World.Profile.Quest == 0 && mappedLine >= 0x10))
                break;

            var b = i / 8;
            var bit = i % 8;
            var show = lineBmp[b] & (0x80 >> bit);
            var pal = 0;

            if (i > 1 && i < 44)
            {
                GlobalFunctions.DrawChar(0xFA, 24, y, 0);
                GlobalFunctions.DrawChar(0xFA, 224, y, 0);
                pal = ((i + 6) / 7) % 3 + 1;
            }
            else if (_game.World.Profile.Quest == 1)
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
                if (_game.World.Profile.Quest == 1 && mappedLine >= 12)
                {
                    effMappedLine += 4;
                }
                var line = textTable.LoadVariableLengthData<LineStruct, Line>(effMappedLine);
                byte[] text = line.Text;
                var x = line.Col * 8;
                if (_game.World.Profile.Quest == 1 && mappedLine == 13)
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

            var pile = new SpriteImage(Graphics.GetAnimation(TileSheet.Boss, AnimationId.B3_Pile));
            pile.Draw(TileSheet.Boss, 0x78, y + 0, (Palette)7);
        }
    }
}
