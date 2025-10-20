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

    public static void Shuffle<T>(this List<T> array, Random rng)
    {
        for (var i = array.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    public static void Shuffle<T>(this T[] array, Random rng)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> array, Random rng)
    {
        // Kind of awful. Consider fixing at some point.
        var list = array.ToArray();
        list.Shuffle(rng);
        return list;
    }

    public static T Pop<T>(this List<T> list)
    {
        if (list.Count == 0) throw new InvalidOperationException("The list is empty.");
        var item = list[^1];
        list.RemoveAt(list.Count - 1);
        return item;
    }

    public static void AddRandomly<T>(this List<T> list, T item, Random rng)
    {
        var index = rng.Next(list.Count + 1);
        list.Insert(index, item);
    }

    public static void AddRangeRandomly<T>(this List<T> list, IList<T> items, Random rng)
    {
        list.EnsureCapacity(list.Count + items.Count);
        foreach (var item in items) list.AddRandomly(item, rng);
    }

    public static Stack<T> ToStack<T>(this IEnumerable<T> source) => new(source);

    public static void Add<T>(this Stack<T> stack, T item) => stack.Push(item);

    public static IEnumerable<T> Add<T>(this IEnumerable<T> enumerable, T direction)
    {
        foreach (var entry in enumerable) yield return entry;
        yield return direction;
    }

    extension(Direction)
    {
        public static ImmutableArray<Direction> DoorDirectionOrder => _doorDirectionOrder;
    }
}