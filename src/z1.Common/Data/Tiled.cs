﻿using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using SkiaSharp;

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

public sealed class TiledMap
{
    public string Version { get; set; }
    public string Type { get; set; }
    public int CompressionLevel { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Infinite { get; set; }
    public TiledLayer[]? Layers { get; set; }
    public int NextLayerId { get; set; }
    public int NextObjectId { get; set; }
    public string Orientation { get; set; } = "orthogonal";
    public string RenderOrder { get; set; } = "right-down";
    public string TiledVersion { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public TiledTileSetReference[]? TileSets { get; set; }
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

public readonly record struct TiledTile(int Tile)
{
    private const int TileMask = 0x0000FFFF;
    private const int TileSheetMask = 0x00FF0000;
    private const uint FlipXFlag = 0x80000000;
    private const int FlipYFlag = 0x40000000;

    public static TiledTile Create(int tileId, bool flippedX, bool flippedY)
    {
        unchecked
        {
            var tile = tileId;
            if (flippedX) tile |= (int)FlipXFlag;
            if (flippedY) tile |= FlipYFlag;
            return new TiledTile(tile);
        }
    }

    public static TiledTile Create(int tileId, int tileSheetId)
    {
        var tile = tileId | (tileSheetId << 16);
        return new TiledTile(tile);
    }

    public static TiledTile Create(int tileId) => new(tileId);

    // 0 means empty. 1 is the first entry in the tileset.
    public int TileId => Tile & TileMask;
    public int TileSheet => (Tile & TileSheetMask) >> 16;
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
    public string DrawOrder { get; set; }
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

    // [JsonIgnore]
    // public TiledLayerType LayerType => Type switch
    // {
    //     "tilelayer" => TiledLayerType.TileLayer,
    //     "objectgroup" => TiledLayerType.ObjectGroup,
    //     "imagelayer" => TiledLayerType.ImageLayer,
    //     _ => throw new Exception($"Unknown layer type: {Type}"),
    // };
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

    public static PointXY GetPoint(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = tiled.GetProperty(name);
        if (stringvalue == null) return default;

        var index = stringvalue.IndexOf(',');
        var span = stringvalue.AsSpan();
        return new PointXY(int.Parse(span[..index]), int.Parse(span[(index + 1)..]));
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

public static class TiledLayerProperties
{
    public const string QuestId = nameof(QuestId);
}

public static class TiledObjectProperties
{
    public const string Type = nameof(Type);

    // Screen
    public const string Monsters = nameof(Monsters);
    public const string MonstersEnter = nameof(MonstersEnter);
    public const string AmbientSound = nameof(AmbientSound);
    public const string Maze = nameof(Maze);
    public const string MazeExit = nameof(MazeExit);
    public const string InnerPalette = nameof(InnerPalette);
    public const string OuterPalette = nameof(OuterPalette);

    // Action
    public const string TileAction = nameof(TileAction);
    public const string Enters = nameof(Enters);
    public const string ExitPosition = nameof(ExitPosition);
    public const string Owner = nameof(Owner);
    public const string Reveals = nameof(Reveals);
    public const string Direction = nameof(Direction); // Used by the raft. Should also have a destination screen id.

    // TileBehavior
    public const string TileBehavior = nameof(TileBehavior);
}

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
    Screen,
    Action,
    TileBehavior,
}
