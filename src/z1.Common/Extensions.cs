namespace z1.Common;

public static class Extensions
{
    public static bool IEquals(this string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool IStartsWith(this string? a, string? b) => a?.StartsWith(b ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    public static bool IContains(this string? a, string? b) => a?.Contains(b ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    public static string IReplace(this string a, string find, string with) => a.Replace(find, with, StringComparison.OrdinalIgnoreCase);
}