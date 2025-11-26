using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace z1.Common;

public static class CollectionExtensions
{
    extension<T>(List<T> list)
    {
        public void Shuffle(Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public T Pop()
        {
            if (list.Count == 0) throw new InvalidOperationException("The list is empty.");
            var item = list[^1];
            list.RemoveAt(list.Count - 1);
            return item;
        }

        public bool TryPop([MaybeNullWhen(false)] out T item)
        {
            if (list.Count == 0)
            {
                item = default;
                return false;
            }

            item = list[^1];
            list.RemoveAt(list.Count - 1);
            return true;
        }

        public T PopSingle(Func<T, bool> pred)
        {
            if (list.Count == 0) throw new InvalidOperationException("The list is empty.");
            for (var i = 0; i < list.Count; ++i)
            {
                var item = list[i];
                if (pred(item))
                {
                    list.RemoveAt(i);
                    return item;
                }
            }

            throw new InvalidOperationException("No list items matched pedicate.");
        }

        public IEnumerable<T> PopMany(Func<T, bool> pred)
        {
            if (list.Count == 0) throw new InvalidOperationException("The list is empty.");
            for (var i = list.Count - 1; i >= 0; --i)
            {
                var item = list[i];
                if (pred(item))
                {
                    list.RemoveAt(i);
                    yield return item;
                }
            }
        }

        public IEnumerable<T> PopManyExactly(Func<T, bool> pred, int count)
        {
            var found = 0;
            foreach (var item in PopMany(list, pred))
            {
                ++found;
                yield return item;
            }

            if (found != count) throw new InvalidOperationException($"{found} items matched predicate, but required {count}.");
        }

        public T PopRandomly(Random rng)
        {
            if (list.Count == 0) throw new InvalidOperationException("The list is empty.");

            var index = rng.Next(list.Count);
            var item = list[index];
            list.RemoveAt(index);
            return item;
        }

        public void AddRandomly(T item, Random rng)
        {
            var index = rng.Next(list.Count + 1);
            list.Insert(index, item);
        }

        public void AddRangeRandomly(IList<T> items, Random rng)
        {
            list.EnsureCapacity(list.Count + items.Count);
            foreach (var item in items) list.AddRandomly(item, rng);
        }
    }

    extension<T>(IEnumerable<T> array)
    {
        public IEnumerable<T> Shuffle(Random rng)
        {
            // Kind of awful. Consider fixing at some point.
            var list = array.ToArray();
            list.Shuffle(rng);
            return list;
        }

        public Stack<T> ToStack() => new(array);

        public IEnumerable<T> Add(T direction)
        {
            foreach (var entry in array) yield return entry;
            yield return direction;
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

    public static void Add<T>(this Stack<T> stack, T item) => stack.Push(item);

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

    public static T GetRandomly<T>(this ReadOnlySpan<T> span, Random rng)
    {
        if (span.Length == 0) throw new InvalidOperationException("The span is empty.");
        var index = rng.Next(span.Length);
        return span[index];
    }
}