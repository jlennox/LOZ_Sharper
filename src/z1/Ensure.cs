using System.Runtime.CompilerServices;

namespace z1;

internal static class Ensure
{
    internal static void ThrowIfPlayer<T>(
        T obj,
        [CallerArgumentExpression(nameof(obj))]
        string? paramName = null,
        [CallerMemberName] string? callerName = null) where T : Actor
    {
        if (obj is Player) throw new ArgumentException($"Cannot use {nameof(Player)} as a parameter in {callerName}.", paramName);
    }
}
