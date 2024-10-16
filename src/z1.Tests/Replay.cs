using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using z1.GUI;

namespace z1.Tests;

internal static class EmbeddedResource
{
    private static readonly ImmutableArray<string> _resourceNames = [.. Assembly.GetExecutingAssembly().GetManifestResourceNames()];

    private static Stream GetEmbeddedResource(string name)
    {
        var resourceName = _resourceNames.FirstOrDefault(t => t.EndsWith(name))
            ?? throw new FileNotFoundException($"Resource not found: {name}");

        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Resource not found: {name}");

        if (name.EndsWith(".br"))
        {
            stream = new BrotliStream(stream, CompressionMode.Decompress, false);
        }

        return stream;
    }

    public static T ReadJson<T>(string name)
    {
        using var stream = GetEmbeddedResource(name);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new Exception();
    }
}

[TestFixture]
internal class ReplayTests
{
    private static void RunReplay(string filename)
    {
        // Unfortunately, too much of the code expects the GL instance to be present.
        // This could be fixed by refactoring Graphics into an instance class.
        var window = new GLWindow(true);
        var recording = EmbeddedResource.ReadJson<GameRecordingState>(filename);
        window.Game = new Game(new GameIO(), recording, true);
        window.Game.Sound.SetMute(true);
        var timer = Stopwatch.StartNew();
        var framecount = 0;
        while (window.Game.Playback!.Enabled)
        {
            window.Game.Update();
            framecount++;
        }
        var totalTime = timer.Elapsed;
        var ups = framecount / totalTime.TotalSeconds;
        Console.WriteLine($"Ticks: {framecount} in {totalTime}, UPS: {ups}");
    }

    [Test]
    public void BasicTest()
    {
        RunReplay("basic dungeon 1 key tests.recording.br");
    }
}