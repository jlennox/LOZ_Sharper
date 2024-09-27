using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using DDirection = z1.Common.Direction;

namespace z1.Common.Data;

public interface IHasTiledProperties
{
    TiledProperty[]? Properties { get; set; }
}

public interface IHasCompression
{
    byte[] Data { get; set; }
    string Compression { get; set; }
}

public sealed class TiledMap : IHasTiledProperties
{
    public string Version { get; set; } = "1.10";
    public string Type { get; set; } = "map";
    public int CompressionLevel { get; set; } = -1;
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Infinite { get; set; }
    public TiledLayer[]? Layers { get; set; }
    public int NextLayerId { get; set; }
    public int NextObjectId { get; set; }
    public string Orientation { get; set; } = "orthogonal";
    public string RenderOrder { get; set; } = "right-down";
    public string TiledVersion { get; set; } = "1.11.0";
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public TiledTileSetReference[]? TileSets { get; set; }
    public TiledProperty[]? Properties { get; set; }
}

public sealed class TiledTileSetReference
{
    public int FirstGid { get; set; }
    public string Source { get; set; }
}

public enum TiledLayerType
{
    TileLayer,
    ObjectGroup,
    ImageLayer,
}

// This is done a bit differently than how Tiled normally does this.
// Tiled calls these GIDs. GIDs are unique only to a specific map. Each map defines a series of TiledTileSetReference.
// Say we have 2 tilesets with 256 tiles each. The first TiledTileSetReference will have a FirstGid of 1 and the second
// will have a FirstGid of 257. The "gid" is the tile's offset in its tileset + the tileset's FirstGid. The flags are
// then or'ed into the GID. As done in gidmapper.cpp, there does not appear to be protection against the addition
// overflow or colliding with the flags.
// Instead of this, we treat the tilemaps as their own bitmask on the GID, making retrieval not require traversal of the
// TiledTileSetReference collection.
[DebuggerDisplay("{TileSheet}/{TileId}")]
public readonly record struct TiledTile(int Tile)
{
    public const int FirstGid = 1;

    private const int TileMask = 0x0000FFFF;
    private const int TileSheetMask = 0x00FF0000;
    private const uint FlipXFlag = 0x80000000;
    private const int FlipYFlag = 0x40000000;
    private const int FlipAntiDiagonallyFlag = 0x20000000;
    private const int RotateHexagonal120Flag = 0x10000000;

    private static readonly int _tileSheetMaskShiftCount = BitOperations.TrailingZeroCount(TileSheetMask);
    private static readonly int _tileSheetMaskMaxValue = (1 << BitOperations.PopCount(TileSheetMask)) - 1;

    public static readonly TiledTile Empty = default;

    public static TiledTile Create(int tileId, bool flippedX, bool flippedY)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tileId, nameof(tileId));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tileId, TileMask, nameof(tileId));

        unchecked
        {
            var tile = tileId + FirstGid;
            if (flippedX) tile |= (int)FlipXFlag;
            if (flippedY) tile |= FlipYFlag;
            return new TiledTile(tile);
        }
    }

    public static TiledTile Create(int tileId, int tileSheetId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tileId, nameof(tileId));
        ArgumentOutOfRangeException.ThrowIfNegative(tileSheetId, nameof(tileSheetId));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tileId, TileMask, nameof(tileId));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tileSheetId, _tileSheetMaskMaxValue, nameof(tileSheetId));

        var tile = (tileId + FirstGid) | (tileSheetId << _tileSheetMaskShiftCount);
        return new TiledTile(tile);
    }

    public static TiledTile Create(int tileId) => new(tileId);

    public static int CreateFirstGid(int tileSheetId) => (tileSheetId << _tileSheetMaskShiftCount) + FirstGid;

    // 0 means empty. 1 is the first entry in the tileset.
    public int TileId => Tile & TileMask;
    public int TileSheet => (Tile & TileSheetMask) >> _tileSheetMaskShiftCount;
    public bool IsFlippedX => (Tile & FlipXFlag) != 0;
    public bool IsFlippedY => (Tile & FlipYFlag) != 0;
}

public sealed class TiledLayer : IHasTiledProperties, IHasCompression
{
    public int Id { get; set; }
    public TiledLayerType Type { get; set; }
    public byte[] Data { get; set; }
    public string Compression { get; set; } = "";
    public string Encoding { get; set; } = "base64";
    public string Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public float Opacity { get; set; } = 1f;
    public bool Visible { get; set; } = true;
    public TiledProperty[]? Properties { get; set; }
    public TiledLayerObject[]? Objects { get; set; }

    public TiledLayer()
    {
    }

    public TiledLayer(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public TiledLayer(int width, int height, ReadOnlySpan<TiledTile> tiles) : this(width, height)
    {
        SetTiles(tiles);
    }

    [JsonIgnore]
    public ReadOnlySpan<TiledTile> Tiles => MemoryMarshal.Cast<byte, TiledTile>(Data);

    public void SetTiles(ReadOnlySpan<TiledTile> tiles)
    {
        var bytes = MemoryMarshal.Cast<TiledTile, byte>(tiles);
        Data = new byte[bytes.Length];
        bytes.CopyTo(Data);
    }
}

public sealed class TiledProperty
{
    public string Type { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }

    public TiledProperty() { }

    public TiledProperty(string name, string value)
    {
        Type = "string";
        Name = name;
        Value = value;
    }

    public TiledProperty(string name, int value)
    {
        Type = "int";
        Name = name;
        Value = value.ToString();
    }

    public TiledProperty(string name, bool value)
    {
        Type = "bool";
        Name = name;
        Value = value.ToString();
    }

    public TiledProperty(string name, object value) : this(name, (value ?? "").ToString()) { }
    public TiledProperty(string name, PointXY point) : this(name, $"{point.X},{point.Y}") { }

    public static TiledProperty CreateArgument(string name, object value)
    {
        return new TiledProperty($"{TiledObjectProperties.Argument}_{name}", $"{value}");
    }
}

// Need properties like "bomb removes"
public sealed class TiledLayerObject : IHasTiledProperties
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Visible { get; set; } = true;
    public float Rotation { get; set; }
    public TiledProperty[]? Properties { get; set; }
}

public sealed class TiledTileSetTile : IHasTiledProperties
{
    public int Id { get; set; }
    public TiledProperty[]? Properties { get; set; }
}

public sealed class TiledTileSet
{
    public string Image { get; set; }
    public string Name { get; set; }
    public string Type { get; set; } = "tileset";
    public int Columns { get; set; }
    public int ImageHeight { get; set; }
    public int ImageWidth { get; set; }
    public int Margin { get; set; }
    public int Spacing { get; set; }
    public int TileCount { get; set; }
    public int TileHeight { get; set; }
    public int TileWidth { get; set; }
    public TiledTileSetTile[]? Tiles { get; set; }

    [JsonConstructor]
    public TiledTileSet() { }

    public TiledTileSet(string filename, byte[] image, int tileWidth, int tileHeight)
    {
        Image = filename;
        Name = Path.GetFileName(filename);
        var bitmap = SKBitmap.Decode(image);
        ImageWidth = bitmap.Width;
        ImageHeight = bitmap.Height;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        Columns = ImageWidth / TileWidth;
        TileCount = Columns * (ImageHeight / TileHeight);
    }
}

public static class TiledExtensions
{
    public static string? GetProperty(this IHasTiledProperties tiled, string name)
    {
        if (tiled.Properties == null) return null;

        foreach (var prop in tiled.Properties)
        {
            if (prop.Name == name)
            {
                return prop.Value;
            }
        }

        return null;
    }

    public static bool TryGetProperty(this IHasTiledProperties tiled, string name, out string value)
    {
        value = "";
        if (tiled.Properties == null) return false;

        foreach (var prop in tiled.Properties)
        {
            if (prop.Name == name)
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetEnumProperty<T>(this IHasTiledProperties tiled, string name, out T value) where T : struct
    {
        value = default;
        var stringvalue = tiled.GetProperty(name);
        if (stringvalue == null) return false;

        return Enum.TryParse<T>(stringvalue, ignoreCase: true, out value);
    }

    public static T GetEnumProperty<T>(this IHasTiledProperties tiled, string name, T defaultValue = default) where T : struct
    {
        var stringvalue = tiled.GetProperty(name);
        if (stringvalue == null) return defaultValue;
        return Enum.Parse<T>(stringvalue, ignoreCase: true);
    }

    public static T? GetNullableEnumProperty<T>(this IHasTiledProperties tiled, string name, T? defaultValue = null) where T : struct
    {
        var stringvalue = tiled.GetProperty(name);
        if (stringvalue == null) return defaultValue;
        return Enum.Parse<T>(stringvalue, ignoreCase: true);
    }

    public static T[] GetEnumArray<T>(this IHasTiledProperties tiled, string name) where T : struct
    {
        var stringvalue = tiled.GetProperty(name);
        if (string.IsNullOrEmpty(stringvalue)) return [];

        var count = 1;
        foreach (var c in stringvalue) if (c == ',') count++;
        Span<Range> outputs = stackalloc Range[count];
        var stringSpan = stringvalue.AsSpan();
        var nfound = stringSpan.Split(outputs, ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new T[count];

        for (var i = 0; i < nfound; ++i)
        {
            var enumName = stringSpan[outputs[i]];
            list[i] = Enum.Parse<T>(enumName, ignoreCase: true);
        }

        return list.ToArray();
    }

    public static bool GetBooleanProperty(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = tiled.GetProperty(name);
        if (stringvalue == null) return false;
        return bool.Parse(stringvalue);
    }

    public static bool TryGetIntProperty(this IHasTiledProperties tiled, string name, out int value)
    {
        var stringvalue = tiled.GetProperty(name);
        return int.TryParse(stringvalue, out value);
    }

    public static int? GetIntPropertyOrNull(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = tiled.GetProperty(name);
        return int.TryParse(stringvalue, out var value) ? value : null;
    }

    public static PointXY GetPoint(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = tiled.GetProperty(name);
        if (stringvalue == null) return default;

        var index = stringvalue.IndexOf(',');
        var span = stringvalue.AsSpan();
        return new PointXY(int.Parse(span[..index]), int.Parse(span[(index + 1)..]));
    }

    public static T GetJsonProperty<T>(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = tiled.GetProperty(name);
        return JsonSerializer.Deserialize<T>(stringvalue);
    }

    public static ImmutableDictionary<string, string> GetPropertyDictionary(this IHasTiledProperties properties)
    {
        if (properties.Properties == null) return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var prop in properties.Properties)
        {
            builder.Add(prop.Name, prop.Value);
        }

        return builder.ToImmutable();
    }

    public static bool IsInQuest(this IHasTiledProperties tiled, int questId)
    {
        var questIdString = tiled.GetProperty(TiledLayerProperties.QuestId);
        if (questIdString == null) return true;
        return int.TryParse(questIdString, out var id) && id == questId;
    }
}

public sealed class TiledWorld : IHasTiledProperties
{
    public TiledWorldEntry[]? Maps { get; set; }
    [JsonPropertyName("onlyShowAdjacentMaps")]
    public bool OnlyShowAdjacentMaps { get; set; }
    public string Type { get; set; } = "world";
    public TiledProperty[]? Properties { get; set; }
}

public sealed class TiledWorldEntry
{
    [JsonPropertyName("fileName")]
    public string Filename { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore] public int Right => X + Width;
    [JsonIgnore] public int Bottom => Y + Height;
}

public sealed class TiledProject
{
    public string AutomappingRulesFile { get; set; } = "";
    public object[] Commands { get; set; } = [];
    public int CompatibilityVersion { get; set; } = 1100;
    public string ExtensionsPath { get; set; } = "extensions";
    public string[] Folders { get; set; } = ["./"];
    public TiledProperty[]? Properties { get; set; }
    public object[] PropertyTypes { get; set; } = [];
}

public static class TiledLayerProperties
{
    public const string QuestId = nameof(QuestId);
}

public static class TiledWorldProperties
{
    public const string WorldInfo = nameof(WorldInfo);
}

public static class TiledObjectProperties
{
    public const string Type = nameof(Type);

    // Room
    public const string Id = nameof(Id);
    public const string Monsters = nameof(Monsters);
    public const string IsEntryRoom = nameof(IsEntryRoom);
    public const string AmbientSound = nameof(AmbientSound);
    public const string IsLadderAllowed = nameof(IsLadderAllowed);

    // Overworld
    public const string MonstersEnter = nameof(MonstersEnter);
    public const string Maze = nameof(Maze);
    public const string MazeExit = nameof(MazeExit);
    public const string InnerPalette = nameof(InnerPalette);
    public const string OuterPalette = nameof(OuterPalette);
    public const string PlaysSecretChime = nameof(PlaysSecretChime);
    public const string CaveId = nameof(CaveId);
    public const string RoomItemId = nameof(RoomItemId);

    // Underworld
    public const string DungeonDoors = nameof(DungeonDoors);
    public const string IsDark = nameof(IsDark);
    public const string Secret = nameof(Secret);
    public const string ItemId = nameof(ItemId);
    public const string ItemPosition = nameof(ItemPosition);
    public const string FireballLayout = nameof(FireballLayout);
    public const string IsBossRoom = nameof(IsBossRoom);
    public const string CellarItem = nameof(CellarItem);
    public const string CellarStairsLeft = nameof(CellarStairsLeft);
    public const string CellarStairsRight = nameof(CellarStairsRight);

    public static readonly ImmutableArray<DDirection> DoorDirectionOrder = [DDirection.Right, DDirection.Left, DDirection.Down, DDirection.Up];

    // Action
    public const string TileAction = nameof(TileAction);
    public const string Enters = nameof(Enters);
    public const string ExitPosition = nameof(ExitPosition);
    public const string Owner = nameof(Owner);
    public const string Reveals = nameof(Reveals);
    public const string Direction = nameof(Direction); // Used by the raft. Should also have a destination screen id.
    public const string Argument = nameof(Argument);
    public const string RecorderDestinationSlot = nameof(RecorderDestinationSlot); // What sequence the recorder will warp you in.

    // TileBehavior
    public const string TileBehavior = nameof(TileBehavior);
}

public static class TiledObjectArguments
{ }

public static class TiledTileSetTileProperties
{
    public static readonly TileBehavior DefaultTileBehavior = TileBehavior.GenericWalkable;

    public const string Behavior = nameof(Behavior);
    public const string Object = nameof(Object);
    public const string ObjectOffsets = nameof(ObjectOffsets);
}

public enum GameObjectLayerObjectType
{
    Unknown,
    Action,
    TileBehavior,
}
