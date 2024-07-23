using Silk.NET.Input;

namespace z1;

internal static class KeyExtensions
{
    public static char GetKeyCharacter(this Key key)
    {
        if ((int)key < 32 || (int)key > 126)
        {
            return '\0';
        }
        return (char)key;
    }
}