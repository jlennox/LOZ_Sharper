namespace z1.Common;

public enum Chars
{
    FullHeart = 0xF2,
    HalfHeart = 0x65,
    EmptyHeart = 0x66,

    BoxTL = 0x69,
    BoxTR = 0x6B,
    BoxBL = 0x6E,
    BoxBR = 0x6D,
    BoxHorizontal = 0x6A,
    BoxVertical = 0x6C,

    X = 0x21,
    Space = 0x24,
    JustSpace = 0x25,
    Minus = 0x62,
    Plus = 0x64,
}

public enum NumberSign
{
    None,
    Negative,
    Positive,
}

public static class GameString
{
    public static unsafe string FromBytes(byte[] bytes)
    {
        var length = 0;
        foreach (var b in bytes)
        {
            var attr = b & 0xC0;
            if (attr != 0)
            {
                length++; // newline or last character.
                if (attr == 0xC0) break;
            }
            length++;
        }

        Span<char> chars = stackalloc char[length];
        var outputIndex = 0;
        for (var inputIndex = 0; outputIndex < length; inputIndex++, outputIndex++)
        {
            var cur = bytes[inputIndex];
            var nextIsNewLine = (cur & 0xC0) is not (0 or 0xC0);
            var chr = cur & 0x3F;
            chars[outputIndex] = CharFromByte((byte)chr);

            if (nextIsNewLine)
            {
                outputIndex++;
                chars[outputIndex] = '\n';
            }
        }
        return new string(chars);
    }

    public static IEnumerable<char> EnumerateBytes(IEnumerable<byte> bytes) => bytes.Select(CharFromByte);
    public static IEnumerable<int> EnumerateText(IEnumerable<char> text) => text.Select(ByteFromChar);

    // The top bit is used to indicate we should pass the rest of the bytes through untouched.
    public static int ByteFromChar(char c) => c switch
    {
        '❤' => (byte)Chars.FullHeart,
        '\u2661' => (byte)Chars.EmptyHeart,
        >= (char)0x80 => (byte)(c & ~0x80),
        ' ' => 0x24,
        ',' => 0x28,
        '!' => 0x29,
        '\'' => 0x2A,
        '&' => 0x2B,
        '.' => 0x2C, // 0xec?
        '"' => 0x2D,
        '?' => 0x2E,
        '-' => (byte)Chars.Minus,
        '+' => (byte)Chars.Plus,
        >= '0' and <= '9' => (byte)(c - '0'),
        >= 'a' and <= 'z' => (byte)(c - 'a' + 0x0A),
        >= 'A' and <= 'Z' => (byte)(char.ToLower(c) - 'a' + 0x0A),
        // Addendum
        '(' => 16 * 16 + 0,
        ')' => 16 * 16 + 1,
        ':' => 16 * 16 + 2,
        '%' => 16 * 16 + 3,
        '/' => 16 * 16 + 4,
        '<' => 16 * 16 + 5,
        '>' => 16 * 16 + 6,
        _ => 0x2E,
    };

    public static char CharFromByte(byte b) => b switch
    {
        0x60 => ' ',
        0x24 => ' ',
        (byte)Chars.JustSpace => ' ',
        0x28 => ',',
        0x29 => '!',
        0x2A => '\'',
        0x2B => '&',
        0x2C => '.',
        0x2D => '"',
        0x2E => '?',
        (byte)Chars.FullHeart => '❤',
        (byte)Chars.Minus => '-',
        (byte)Chars.Plus => '+',
        <= 0x09 => (char)(b + '0'),
        _ => (char)(b + 'a' - 0x0A),
    };

    public static string NumberToString(int number, NumberSign sign)
    {
        Span<char> buffer = stackalloc char[16];
        var actual = NumberToString(number, sign, buffer);
        return new string(actual);
    }

    public static ReadOnlySpan<char> NumberToString(int number, NumberSign sign, Span<char> output, int paddedSize = 4)
    {
        if (!number.TryFormat(output, out var size)) throw new Exception();
        if (!number.TryFormat(output[^size..], out _)) throw new Exception();

        var signChar = sign switch
        {
            NumberSign.Negative => '-',
            NumberSign.Positive => '+',
            _ => '\0',
        };

        if (signChar != '\0')
        {
            size++;
            output[^size] = signChar;
        }

        // Left pad to 4 because that's how the game does it.
        while (size < paddedSize)
        {
            size++;
            output[^size] = ' ';
        }

        return output[^size..];
    }
}