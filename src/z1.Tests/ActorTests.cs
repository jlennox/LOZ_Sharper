using System.Text.RegularExpressions;
using z1.Actors;
using z1.Common;

namespace z1.Tests;

internal static class TestObjects
{
    public static Game Game => new();
}

[TestFixture]
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

[TestFixture]
internal class NumberToStringTests
{
    [Test]
    [TestCase(1232, NumberSign.Positive, "+1232")]
    [TestCase(1232, NumberSign.Negative, "-1232")]
    [TestCase(1232, NumberSign.None, "1232")]
    [TestCase(1232, NumberSign.None, "1232")]
    [TestCase(2, NumberSign.None, "   2")]
    [TestCase(12, NumberSign.None, "  12")]
    [TestCase(12, NumberSign.Negative, " -12")]
    [TestCase(123, NumberSign.Negative, "-123")]
    [TestCase(1234, NumberSign.Negative, "-1234")]
    public void EnsureNumberToStringWorks(int number, NumberSign sign, string expected)
    {
        var actual = GlobalFunctions.NumberToString(number, sign);
        Assert.That(actual, Is.EqualTo(expected));
    }
}

[TestFixture, Explicit]
public class OffsetExtractor
{
    private record class Offset
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public int Size { get; set; }
    }

    [Test]
    public void ExtractOffsets()
    {
        const string file = @"C:\Users\joe\Dropbox\_code\z1\src\ExtractLoz\LozExtractor.cs";
        var text = File.ReadAllText(file);

        var offsets = new Dictionary<string, Offset>();
        var offsetExpr = new Regex(@"([\w\d]+)\s*=\s*([x\dA-Fa-f]+)\s*\+\s*(?:0x10|16)\s*;");
        foreach (Match match in offsetExpr.Matches(text))
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            offsets[name] = new Offset { Name = name, Value = Convert.ToInt32(value, 16) };
        }
    }
}