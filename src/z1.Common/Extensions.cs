using System.Collections.Immutable;

namespace z1.Common;

public static class Extensions
{
    public static bool IEquals(this string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool IStartsWith(this string? a, string? b) => a?.StartsWith(b ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    public static bool IStartsWith(this string? a, ReadOnlySpan<char> b) => (a ?? "").AsSpan().StartsWith(b, StringComparison.OrdinalIgnoreCase);
    public static bool IContains(this string? a, string? b) => a?.Contains(b ?? "", StringComparison.OrdinalIgnoreCase) ?? false;
    public static string IReplace(this string a, string find, string with) => a.Replace(find, with, StringComparison.OrdinalIgnoreCase);

    private static readonly ImmutableArray<Direction> _doorDirectionOrder = [Direction.Right, Direction.Left, Direction.Down, Direction.Up];

    public static T GetClamped<T>(this IReadOnlyCollection<T> collection, int index)
        where T : notnull
    {
        if (collection.Count == 0) throw new ArgumentException("Collection is empty.", nameof(collection));
        index = Math.Clamp(index, 0, collection.Count - 1);
        return collection.ElementAt(index);
    }


    public static T GetClamped<T>(this ReadOnlySpan<T> collection, int index)
        where T : notnull
    {
        if (collection.Length == 0) throw new ArgumentException("Collection is empty.", nameof(collection));
        index = Math.Clamp(index, 0, collection.Length - 1);
        return collection[index];
    }


    extension(Direction)
    {
        public static ImmutableArray<Direction> DoorDirectionOrder => _doorDirectionOrder;
    }
}