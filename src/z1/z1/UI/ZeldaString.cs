namespace z1.UI;

internal static class ZeldaString
{
    public static unsafe string FromBytes(byte[] bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = CharFromByte(bytes[i]);
        }
        return new string(chars);
    }

    public static IEnumerable<char> EnumerateBytes(byte[] bytes)
    {
        foreach (var b in bytes)
        {
            yield return CharFromByte(b);
        }
    }

    public static IEnumerable<byte> ToBytes(string text)
    {
        foreach (var c in text)
        {
            yield return c switch {
                ' ' => 123,
                ',' => 127,
                '!' => 128,
                '\'' => 129,
                '&' => 130,
                '.' => 131,
                '"' => 132,
                '?' => 133,
                '-' => 193,
                _ => (byte)((byte)char.ToLower(c) - (byte)'a' + 0x0A),
            };
        }
    }

    public static char CharFromByte(byte b) => b switch {
        123 => ' ',
        127 => ',',
        128 => '!',
        129 => '\'',
        130 => '&',
        131 => '.',
        132 => '"',
        133 => '?',
        193 => '-',
        _ => (char)(b + 'a' - 0x0A),
    };
}