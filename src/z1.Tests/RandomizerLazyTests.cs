using System;
using z1.Randomizer;
using z1.Randomizer.Lazy;

namespace z1.Tests;

[TestFixture]
internal class RandomizerLazyTests
{
    [Test]
    public void Testeroo()
    {
        RandomizerLazy.Create(new RandomizerState(1234, new RandomizerFlags()));
    }
}
