using System;

namespace z1.Tests;

[TestFixture]
internal class ProgramOptionsTests
{
    [Test]
    public void StringReparsing()
    {
        foreach (var (input, expected) in new (string[], string[])[]
        {
            (["--temp-profile" ], ["--temp-profile" ]),
            (["--profile=MyProfile"], ["--profile", "MyProfile"]),
            (["--randomize", "12345"], ["--randomize", "12345"]),
            (["--randomize=67890"], ["--randomize", "67890"]),
            ([ "--profile", "User", "--randomize=42", "--temp-profile" ],
                [ "--profile", "User", "--randomize", "42", "--temp-profile" ]),
        })
        {
            var reparsed = ProgramOptions.ReparseArgs(input).ToArray();
            Assert.That(reparsed, Is.EqualTo(expected), $"Input: {string.Join(" ", input)}");
        }
    }
}
