namespace z1.Common.Data;

public ref struct StringParser
{
    public int Index { get; set; }

    public void SkipOptionalWhiteSpace(ReadOnlySpan<char> s)
    {
        for (; Index < s.Length && s[Index] == ' ';) Index++;
    }

    public int ExpectInt(ReadOnlySpan<char> s)
    {
        if (Index >= s.Length) throw new Exception($"Unexpected end of string at position in \"{s}\", expected number.");
        if (!TryExpectInt(s, out var i)) throw new Exception($"Expected number at position {Index} ('{s[Index]}') in \"{s}\"");
        SkipOptionalWhiteSpace(s);
        return i;
    }

    public bool TryExpectInt(ReadOnlySpan<char> s, out int value)
    {
        value = 0;
        if (Index >= s.Length) return false;
        var start = Index;
        var length = 0;

        if (s[Index] is '+' or '-')
        {
            Index++;
            length++;
        }

        while (Index < s.Length && char.IsAsciiDigit(s[Index]))
        {
            Index++;
            length++;
        }

        if (length == 0) return false;
        SkipOptionalWhiteSpace(s);
        return int.TryParse(s.Slice(start, length), out value);
    }

    public void ExpectChar(ReadOnlySpan<char> s, char chr)
    {
        if (Index >= s.Length) throw new Exception($"Unexpected end of string at position in \"{s}\", expected character \"{chr}\".");
        var actual = s[Index];
        if (actual != chr) throw new Exception($"Unexpected char \"{actual}\" at position {Index} in \"{s}\"");
        Index++;
        SkipOptionalWhiteSpace(s);
    }

    public bool TryExpectChar(ReadOnlySpan<char> s, char chr)
    {
        if (Index >= s.Length) return false;
        if (s[Index] != chr) return false;
        Index++;
        SkipOptionalWhiteSpace(s);
        return true;
    }

    public bool TryExpectWord(ReadOnlySpan<char> s, out ReadOnlySpan<char> word)
    {
        if (Index >= s.Length)
        {
            word = default;
            return false;
        }

        var start = Index;
        var length = 0;
        while (Index < s.Length && char.IsLetter(s[Index]))
        {
            Index++;
            length++;
        }

        if (length == 0)
        {
            word = default;
            return false;
        }

        SkipOptionalWhiteSpace(s);
        word = s.Slice(start, length);
        return true;
    }


    public ReadOnlySpan<char> ExpectWord(ReadOnlySpan<char> s)
    {
        if (Index >= s.Length) throw new Exception($"Unexpected end of string at position in \"{s}\", expected word.");
        var start = Index;
        var length = 0;
        while (Index < s.Length && char.IsAsciiLetterOrDigit(s[Index]))
        {
            Index++;
            length++;
        }

        if (length == 0) throw new Exception($"Expected word at position {Index} in \"{s}\"");
        SkipOptionalWhiteSpace(s);
        return s.Slice(start, length);
    }

    public ReadOnlySpan<char> ReadUntil(ReadOnlySpan<char> s, char terminator)
    {
        if (Index >= s.Length) throw new Exception($"Unexpected end of string at position in \"{s}\", expected word.");
        var start = Index;
        var length = 0;
        while (Index < s.Length && s[Index] != terminator)
        {
            Index++;
            length++;
        }

        if (length == 0) throw new Exception($"Expected word at position {Index} in \"{s}\"");
        SkipOptionalWhiteSpace(s);
        return s.Slice(start, length);
    }

    public bool TryExpectEnum<T>(ReadOnlySpan<char> s, out T value) where T : struct, Enum
    {
        if (TryExpectWord(s, out var word) && Enum.TryParse(word, true, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public T ExpectEnum<T>(ReadOnlySpan<char> s) where T : struct, Enum
    {
        if (!TryExpectEnum<T>(s, out var value)) throw new Exception($"Expected enum type {typeof(T).Name} at position {Index} in \"{s}\"");
        return value;
    }

    public ReadOnlySpan<char> ReadRemaining(ReadOnlySpan<char> s)
    {
        var start = Index;
        Index = s.Length;
        return s[start..];
    }
}
