using System;

namespace z1.Randomizer;

internal sealed class RecoverableRandomizerException : Exception
{
    public RecoverableRandomizerException(string message) : base(message) { }
}