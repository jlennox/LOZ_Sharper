using System;
using z1.Common.IO;
using z1.IO;

namespace z1;

internal abstract class WorldStore
{
    public abstract GameWorld GetWorld(GameWorldType type, string destination, int questId);

    protected static string GetWorldAssetName(GameWorldType type, string destination)
    {
        Filenames.ExpectSafe(destination);

        return type switch
        {
            GameWorldType.Underworld => $"Level{destination}",
            GameWorldType.Overworld => "Overworld",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"World type not support with destination \"{destination}\"")
        };
    }
}

internal sealed class AssetWorldStore : WorldStore
{
    public override GameWorld GetWorld(GameWorldType type, string destination, int questId)
    {
        Filenames.ExpectSafe(destination);

        var assetName = GetWorldAssetName(type, destination);

        var asset = new Asset("Maps", $"{assetName}.world");
        var tiledWorld = asset.ReadJson<TiledWorld>();
        return new GameWorld(tiledWorld, asset.Filename, questId); // JOE: TODO: QUEST  Profile.Quest);
    }
}

internal sealed class MemoryWorldStore : WorldStore
{
    private readonly Dictionary<string, GameWorld> _worlds = new();

    public override GameWorld GetWorld(GameWorldType type, string destination, int questId)
    {
        var assetName = GetWorldAssetName(type, destination);

        if (!_worlds.TryGetValue(assetName, out var overriddenWorld))
        {
            throw new Exception($"No runtime override found for world \"{destination}\".");
        }

        return overriddenWorld;
    }

    public void SetWorld(GameWorld world, string destination)
    {
        _worlds[destination] = world;
    }
}