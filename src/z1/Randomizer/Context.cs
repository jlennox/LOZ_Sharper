using System;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;

namespace z1.Randomizer;

internal interface IContext : IDisposable
{
    ContextSource Source { get; init; }

    Random CreateRng();

    // Creates a "sub context." It nests the original inside of this context.
    public IContext Create([CallerMemberName] string name = "") => Source.CreateNamed(name, this);
    public IContext Create<T>(T val, [CallerMemberName] string name = "") => Source.CreateNamed(name, this, val);
    public IContext Create<T, T2>(T val, T2 val2, [CallerMemberName] string name = "") => Source.CreateNamed(name, this, val, val2);
    public IContext Create<T, T2, T3>(T val, T2 val2, T3 val3, [CallerMemberName] string name = "") => Source.CreateNamed(name, this, val, val2, val3);

    public IContext CreateNamed(string name) => Source.CreateNamed(name);
    public IContext CreateNamed<T>(string name, T val) => Source.CreateNamed(name, this, val);
    public IContext CreateNamed<T, T2>(string name, T val, T2 val2) => Source.CreateNamed(name, this, val, val2);
    public IContext CreateNamed<T, T2, T3>(string name, T val, T2 val2, T3 val3) => Source.CreateNamed(name, this, val, val2, val3);

    protected static int GetStringHash(string s)
    {
        // TODO: Fix allocations.
        return unchecked((int)XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(s)));
    }

    // TODO: Consider making this exception if it's ever called twice with the same value. That would indicate a failure to cache.
    public bool TryGetCached<T>([MaybeNullWhen(false)] out T val) => ContextCache.TryGet(this, out val);

    public T SetCache<T>(T val)
    {
        ContextCache.Set(this, val);
        return val;
    }
}

internal sealed class ContextSource(int seed)
{
    // To help with seed stability, if more arguments are ever needed, make a separate class.
    private readonly record struct ValContext<T, T2, T3, T4, T5, T6>(ContextSource Source, string Name, int Seed, T Val, T2 Val2, T3 Val3, T4 Val4, T5 Val5, T6 Val6) : IContext
    {
        public Random CreateRng()
        {
            var hash = HashCode.Combine(Seed, IContext.GetStringHash(Name), Val, Val2, Val3, Val4, Val5, Val6);
            return new Random(hash);
        }

        public void Dispose()
        {
            if (!ContextCache.Contains(this)) throw new Exception($"{this} was not cached.");
        }

    }

    public IContext Create([CallerMemberName] string name = "") => CreateNamed(name);
    public IContext Create<T>(T val, [CallerMemberName] string name = "") => CreateNamed(name, val);
    public IContext Create<T, T2>(T val, T2 val2, [CallerMemberName] string name = "") => CreateNamed(name, val, val2);
    public IContext Create<T, T2, T3>(T val, T2 val2, T3 val3, [CallerMemberName] string name = "") => CreateNamed(name, val, val2, val3);
    public IContext Create<T, T2, T3, T4>(T val, T2 val2, T3 val3, T4 val4, [CallerMemberName] string name = "") => CreateNamed(name, val, val2, val3, val4);

    public IContext CreateNamed(string name) => new ValContext<byte, byte, byte, byte, byte, byte>(this, name, seed, 0, 0, 0, 0, 0, 0);
    public IContext CreateNamed<T>(string name, T val) => new ValContext<T, byte, byte, byte, byte, byte>(this, name, seed, val, 0, 0, 0, 0, 0);
    public IContext CreateNamed<T, T2>(string name, T val, T2 val2) => new ValContext<T, T2, byte, byte, byte, byte>(this, name, seed, val, val2, 0, 0, 0, 0);
    public IContext CreateNamed<T, T2, T3>(string name, T val, T2 val2, T3 val3) => new ValContext<T, T2, T3, byte, byte, byte>(this, name, seed, val, val2, val3, 0, 0, 0);
    public IContext CreateNamed<T, T2, T3, T4>(string name, T val, T2 val2, T3 val3, T4 val4) => new ValContext<T, T2, T3, T4, byte, byte>(this, name, seed, val, val2, val3, val4, 0, 0);
}

internal static class ContextCache
{
    private static readonly Dictionary<IContext, object?> _cache = new();

    public static bool TryGet<T>(IContext context, [MaybeNullWhen(false)] out T result)
    {
        if (!_cache.TryGetValue(context, out var cached))
        {
            result = default;
            return false;
        }

        if (cached is T value)
        {
            result = value;
            return true;
        }

        throw new Exception($"{context} tried to access type of {typeof(T).Name}, but got {cached?.GetType().Name ?? "{NULL}"}");
    }

    public static bool Contains(IContext context) => _cache.ContainsKey(context);

    public static void Set<T>(IContext context, T value) => _cache.Add(context, value);
}