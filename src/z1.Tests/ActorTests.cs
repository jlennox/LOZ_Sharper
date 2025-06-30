using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using z1.Common;
using z1.Common.Data;

namespace z1.Tests;

internal static class TestObjects
{
    public static Game Game => new(new GameIO());
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
        // var actor = Actor.FromType(type, TestObjects.Game, 0, 0);
        // Assert.That(actor.ObjTimer, Is.EqualTo(0));
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
        var actual = GameString.NumberToString(number, sign);
        Assert.That(actual, Is.EqualTo(expected));
    }
}

[TestFixture]
internal class ScreenGameMapObjectTests
{
    [Test]
    [TestCase(null, new ObjType[] { })]
    [TestCase("", new ObjType[] { })]
    [TestCase("RedDarknut", new[] { ObjType.RedDarknut })]
    [TestCase("RedDarknut*3", new[] { ObjType.RedDarknut, ObjType.RedDarknut, ObjType.RedDarknut })]
    [TestCase("   RedDarknut  *  3  ", new[] { ObjType.RedDarknut, ObjType.RedDarknut, ObjType.RedDarknut })]
    [TestCase(" RedDarknut*3 , BlueDarknut  *  2 ", new[] { ObjType.RedDarknut, ObjType.RedDarknut, ObjType.RedDarknut, ObjType.BlueDarknut, ObjType.BlueDarknut })]
    public void EnsureScreenMapObjectIsCreated(string? monsterList, ObjType[] expected)
    {
        var actual = MonsterEntry.ParseMonsters(monsterList, out _);
        var expectedcollection = expected.Select(t => new MonsterEntry(t)).ToImmutableArray();
        Assert.That(actual, Is.EqualTo(expectedcollection));
    }

    [Test]
    public void TryParseDungeonDoors()
    {
        var directions = TiledRoomProperties.DoorDirectionOrder;

        foreach (var (input, expected) in new[] {
            ("Wall, Wall, Open, Key", new[] { DoorType.Wall, DoorType.Wall, DoorType.Open, DoorType.Key }),
        })
        {
            Assert.That(GameRoom.TryParseUnderworldDoors(input, out var actual), Is.True);
            var i = 0;
            foreach (var dir in directions)
            {
                Assert.That(actual[dir], Is.EqualTo(expected[i]));
                i++;
            }
        }
    }
}

[TestFixture]
internal class PointsParser
{
    [Test]
    public void EnsurePointListsAreParsedCorrectly()
    {
        foreach (var (s, expected) in new (string, Point[])[] {
            ("(0,0)", [new Point(0, 0)]),
            ("(0,0),(1,1)", [new Point(0, 0), new Point(1, 1)]),
            ("(0,0), (  1  ,  1  ), (2,2)", [new Point(0, 0), new Point(1, 1), new Point(2, 2)]),
            ("   (1,0)  , (1,6)  , (2,77)  , (3,10)   ", [new Point(1, 0), new Point(1, 6), new Point(2, 77), new Point(3, 10)]),
        })
        {
            var actual = GameTileSet.ParsePointsString(s).ToArray();
            Assert.That(actual, Is.EqualTo(expected));
        }
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

public class WhateverPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

public class SimpleClass
{
    public string? One { get; set; }
    public string Two { get; set; }
    public int Three { get; set; }
    public bool Four { get; set; }
    public WhateverPoint Point { get; set; }
}

[TestFixture]
public class TiledPropertyTests
{
    [Test]
    public void Test()
    {
        var simpleClass = new SimpleClass
        {
            One = "hello",
            Two = "world",
            Three = 123,
            Four = true,
            Point = new WhateverPoint { X = 1, Y = 2 }
        };

        var classProp = TiledProperty.ForClass(nameof(TiledPropertyTests), simpleClass);
        var jsoned = JsonSerializer.Serialize(classProp);
        var backagain = JsonSerializer.Deserialize<TiledProperty>(jsoned);
        var actual = (SimpleClass)backagain.ClassValue;
        var expected = simpleClass;
        Assert.That(actual.One, Is.EqualTo(expected.One));
        Assert.That(actual.Two, Is.EqualTo(expected.Two));
        Assert.That(actual.Three, Is.EqualTo(expected.Three));
        Assert.That(actual.Four, Is.EqualTo(expected.Four));
        Assert.That(actual.Point.X, Is.EqualTo(expected.Point.X));
        Assert.That(actual.Point.Y, Is.EqualTo(expected.Point.Y));
    }
}

[TestFixture]
public class ParseMonstersTests
{
    [Test]
    public void Test()
    {
        var tests = new[] {
            ("BlueKeese*2", new[] {
                new MonsterEntry(ObjType.BlueKeese),
                new MonsterEntry(ObjType.BlueKeese)
            }),
            ("BlueKeese, RedKeese", new[] {
                new MonsterEntry(ObjType.BlueKeese),
                new MonsterEntry(ObjType.RedKeese)
            }),
            ("BlueKeese[X=5,Y=2]*2, RedKeese", new[] {
                new MonsterEntry(ObjType.BlueKeese, false, 1, new System.Drawing.Point(5, 2)),
                new MonsterEntry(ObjType.BlueKeese, false, 1, new System.Drawing.Point(5, 2)),
                new MonsterEntry(ObjType.RedKeese)
            }),
            ("BlueKeese[X=5,Ringleader,Y=2]*2, RedKeese", new[] {
                new MonsterEntry(ObjType.BlueKeese, true, 1, new System.Drawing.Point(5, 2)),
                new MonsterEntry(ObjType.BlueKeese, true, 1, new System.Drawing.Point(5, 2)),
                new MonsterEntry(ObjType.RedKeese)
            }),
        };

        foreach (var (input, expected) in tests)
        {
            var actual = MonsterEntry.ParseMonsters(input, out _).ToArray();
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}