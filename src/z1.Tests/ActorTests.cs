using z1.Actors;

namespace z1.Tests;

internal static class TestObjects
{
    public static Game Game => new();
}

internal class ActorTests
{
    [Test]
    [TestCase(ObjType.Zora)]
    [TestCase(ObjType.BlueWizzrobe)]
    [TestCase(ObjType.RedWizzrobe)]
    [TestCase(ObjType.PatraChild1)]
    [TestCase(ObjType.Wallmaster)]
    [TestCase(ObjType.Ganon)]
    public void EnsureProperObjTimer(ObjType type)
    {
        var actor = Actor.FromType(type, TestObjects.Game, 0, 0);
        Assert.That(actor.ObjTimer, Is.EqualTo(0));
    }
}