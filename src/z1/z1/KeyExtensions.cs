using System.Runtime.InteropServices;

namespace z1;

internal static unsafe partial class KeyExtensions
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetKeyboardState(byte* lpKeyState);

    [LibraryImport("user32.dll")]
    private static partial int ToUnicode(
        uint wVirtKey,
        uint wScanCode,
        byte* lpKeyState,
        char* pwszBuff,
        int cchBuff,
        uint wFlags);

    //... Really?
    public static string GetKeyString(this Keys key)
    {
        var keyboardState = stackalloc byte[256];
        GetKeyboardState(keyboardState);

        var buffer = stackalloc char[64];
        var scancode = (int)(key & Keys.KeyCode);
        var result = ToUnicode((uint)key, (uint)scancode, keyboardState, buffer, 64, 0);
        return new string(buffer, 0, result);
    }

    public static char GetKeyCharacter(this Keys key)
    {
        var s = GetKeyString(key);
        return s.Length == 0 ? default : s[0];
    }
}