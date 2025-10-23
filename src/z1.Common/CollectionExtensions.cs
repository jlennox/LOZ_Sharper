using System;
using System.Runtime.CompilerServices;

namespace z1.Common;

public static class CollectionExtensions
{
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

    public static T PopRandomly<T>(this List<T> list, Random rng)
    {
        if (list.Count == 0) throw new InvalidOperationException("The list is empty.");

        var index = rng.Next(list.Count);
        var item = list[index];
        list.RemoveAt(index);
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

    public static T BitwiseOr<T>(this IEnumerable<T> enumerable)
        where T : struct, Enum
    {
        var product = 0;
        foreach (var entry in enumerable) product |= Unsafe.As<T, int>(ref Unsafe.AsRef(in entry));
        return Unsafe.As<int, T>(ref product);
    }

    public static T BitwiseOr<TIn, T>(this IEnumerable<TIn> enumerable, Func<TIn, T> predicate)
        where T : unmanaged, Enum
    {
        var product = 0;
        foreach (var entry in enumerable)
        {
            var val = predicate(entry);
            product |= Unsafe.As<T, int>(ref Unsafe.AsRef(in val));
        }
        return Unsafe.As<int, T>(ref product);
    }

}