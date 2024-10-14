using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace z1.Common.Data;

[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Class)]
public class TiledClass : Attribute { }

[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Field | AttributeTargets.Property)]
public class TiledIgnoreAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Enum)]
public class TiledSelectableEnumAttribute : Attribute { }

public interface IHasTiledProperties
{
    TiledProperty[]? Properties { get; set; }
}

public interface IHasInitialization
{
    void Initialize();
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

// This is done a bit differently than how Tiled normally does.
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

    public TiledLayer() { }

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

public enum TiledPropertyType { String, Int, Bool, Class, Enum }

public sealed class EnumArrayJsonConverter<T> : JsonConverter<T[]> where T : struct, Enum
{
    public override T[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringvalue = reader.GetString();
        if (string.IsNullOrWhiteSpace(stringvalue)) return [];

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

        return list;
    }

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options)
    {
        if (value == null || value.Length == 0)
        {
            writer.WriteStringValue("");
            return;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(value[i]);
        }
        writer.WriteStringValue(sb.ToString());
    }
}

public sealed class TiledJsonSelectableEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return default;

        var stringSpan = s.AsSpan();

        var parser = new StringParser();
        long finalValue = default;

        while (true)
        {
            if (!parser.TryExpectEnum<T>(stringSpan, out var value))
            {
                throw new Exception($"Invalid TiledJsonSelectableEnumConverter for type \"{typeof(T).Name}\", input: \"{s}\"");
            }
            finalValue |= Convert.ToInt64(value);
            if (!parser.TryExpectChar(stringSpan, ',')) break;
        }

        return (T)Enum.ToObject(typeof(T), finalValue);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var enumValues = Enum.GetValues(value.GetType());
        var sb = new StringBuilder();

        var isFirst = true;
        foreach (T enumValue in enumValues)
        {
            if (value.HasFlag(enumValue) && Convert.ToInt32(enumValue) != 0)
            {
                if (!isFirst) sb.Append(',');
                isFirst = false;
                sb.Append(enumValue);
            }
        }

        writer.WriteStringValue(sb.ToString());
    }
}

[JsonConverter(typeof(Converter))]
[DebuggerDisplay("[{Name}=\"{Value}\"")]
public sealed class TiledProperty
{
    public TiledPropertyType Type { get; set; }
    public string Name { get; set; }
    public string? Value { get; set; }
    public string? PropertyType { get; set; } // The name of the enum or class, if present.

    [JsonIgnore]
    public object? ClassValue { get; set; }

    [JsonIgnore]
    private bool IsClassType { get; set; }

    public TiledProperty() { }

    public TiledProperty(string name, string value)
    {
        Type = TiledPropertyType.String;
        Name = name;
        Value = value;
    }

    public TiledProperty(string name, int value)
    {
        Type = TiledPropertyType.Int;
        Name = name;
        Value = value.ToString();
    }

    public TiledProperty(string name, bool value)
    {
        Type = TiledPropertyType.Bool;
        Name = name;
        Value = value.ToString();
    }

    public TiledProperty(string name, object? value) : this(name, (value ?? "").ToString()) { }
    public TiledProperty(string name, PointXY point) : this(name, $"{point.X},{point.Y}") { }

    public bool TryGetEnum<T>(out T value) where T : struct
    {
        value = default;
        if (Value == null) return false;
        return Enum.TryParse<T>(Value, ignoreCase: true, out value);
    }

    public T GetEnumProperty<T>(T defaultValue = default) where T : struct
    {
        if (Value == null) return defaultValue;
        return Enum.Parse<T>(Value, ignoreCase: true);
    }

    public T? GetNullableEnumProperty<T>(T? defaultValue = null) where T : struct
    {
        if (Value == null) return defaultValue;
        return Enum.Parse<T>(Value, ignoreCase: true);
    }

    public T[] GetEnumArray<T>() where T : struct
    {
        if (string.IsNullOrEmpty(Value)) return [];

        var count = 1;
        foreach (var c in Value) if (c == ',') count++;
        Span<Range> outputs = stackalloc Range[count];
        var stringSpan = Value.AsSpan();
        var nfound = stringSpan.Split(outputs, ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new T[count];

        for (var i = 0; i < nfound; ++i)
        {
            var enumName = stringSpan[outputs[i]];
            list[i] = Enum.Parse<T>(enumName, ignoreCase: true);
        }

        return list.ToArray();
    }

    public bool GetBoolean()
    {
        if (Value == null) return false;
        return bool.Parse(Value);
    }

    public bool TryGetInt(out int value)
    {
        return int.TryParse(Value, out value);
    }

    public int? GetIntOrNull()
    {
        return int.TryParse(Value, out var value) ? value : null;
    }

    public PointXY GetPoint()
    {
        if (Value == null) return default;
        var index = Value.IndexOf(',');
        if (index == -1) return default;
        var span = Value.AsSpan();
        return new PointXY(int.Parse(span[..index]), int.Parse(span[(index + 1)..]));
    }

    public T GetJson<T>()
    {
        return JsonSerializer.Deserialize<T>(Value);
    }

    public T? GetClass<T>() where T : class
    {
        return ClassValue as T;
    }

    public T ExpectClass<T>() where T : class
    {
        return GetClass<T>() ?? throw new Exception($"Expected class \"{typeof(T).Name}\" at property \"{Name}\".");
    }

    public static TiledProperty ForClass<T>(string name, T obj)
    {
        return new TiledProperty
        {
            Name = name,
            Type = TiledPropertyType.Class,
            PropertyType = typeof(T).Name,
            ClassValue = obj,
            IsClassType = true,
        };
    }

    public static TiledProperty ForClass(string name, Type type, object obj)
    {
        return new TiledProperty
        {
            Name = name,
            Type = TiledPropertyType.Class,
            PropertyType = type.Name,
            ClassValue = obj,
            IsClassType = true,
        };
    }

    // This can't be automatically used by [JsonConverter(typeof(Converter))] because
    // tiled-project files expect the property name for PropertyType as "propertyType" and map types
    // expect "propertytype"
    public class Converter : JsonConverter<TiledProperty>
    {
        private readonly string _propertyTypeString;
        private readonly bool _isReadOnly = false;
        private static readonly Dictionary<string, Type> _typeCache = new();

        public Converter()
        {
            _isReadOnly = true;
        }

        public Converter(bool lowerCasePropertyName)
        {
            _propertyTypeString = lowerCasePropertyName ? "propertytype" : "propertyType";
        }

        private static string ExpectString(ref Utf8JsonReader reader, string name)
        {
            return reader.GetString() ?? throw new Exception($"Tile property \"{name}\" is missing.");
        }

        public override TiledProperty Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var property = new TiledProperty();
            JsonElement valueElement = default;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    // We may not get the "type" property first, so we evaluate at the end of the object when all values
                    // will be present.
                    case JsonTokenType.EndObject:
                        DeserializeValue(property, valueElement, options);
                        return property;

                    case JsonTokenType.PropertyName:
                        var propertyName = reader.GetString();
                        reader.Read();
                        switch (propertyName)
                        {
                            case "name":
                                property.Name = ExpectString(ref reader, "name");
                                break;
                            case "type":
                                var typename = ExpectString(ref reader, "type");
                                property.Type = Enum.Parse<TiledPropertyType>(typename, true);
                                break;
                            case "propertyType":
                            case "propertytype":
                                property.PropertyType = reader.GetString();
                                break;
                            case "value":
                                valueElement = JsonSerializer.Deserialize<JsonElement>(ref reader);
                                break;
                        }
                        break;
                }
            }

            throw new JsonException("Unexpected end of JSON");
        }

        private static void DeserializeValue(TiledProperty property, JsonElement valueElement, JsonSerializerOptions options)
        {
            if (property.Type == TiledPropertyType.Class)
            {
                var propertyType = property.PropertyType
                    ?? throw new Exception("TiledProperty specifies class type but does not defined PropertyType.");

                if (!_typeCache.TryGetValue(propertyType, out var classType))
                {
                    classType = AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(static a => a.GetTypes())
                            .FirstOrDefault(t => t.Name == propertyType)
                        ?? throw new Exception($"Unable to find class \"{propertyType}\" associated with TileProperty's PropertyType.");
                    _typeCache[propertyType] = classType;
                }

                property.ClassValue = valueElement.Deserialize(classType); //, options);
                property.IsClassType = true;
                return;
            }

            property.Value = valueElement.GetString();
        }

        public override void Write(Utf8JsonWriter writer, TiledProperty value, JsonSerializerOptions options)
        {
            if (_isReadOnly) throw new Exception();

            writer.WriteStartObject();
            writer.WriteString("name", value.Name);
            writer.WriteString("type", value.Type.ToString().ToLowerInvariant());

            if (value.PropertyType != null)
            {
                writer.WriteString(_propertyTypeString, value.PropertyType);
            }

            if (value.IsClassType)
            {
                var obj = value.ClassValue!;
                writer.WritePropertyName("value");
                // OK. This is kind of awful. We're not passing options down. That's because Tiled uses 3 separate
                // JSON key naming schemes, 2 for enums, and is case-sensitive.
                // We're now decending into the world where our normal naming scheme is correct, so we're dropping the
                // options that lowercase our enums and properties.
                JsonSerializer.Serialize(writer, obj);
            }
            else
            {
                writer.WriteString("value", value.Value);
            }

            writer.WriteEndObject();
        }
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
    public static TiledProperty? GetPropertyEntry(this IHasTiledProperties tiled, string name)
    {
        if (tiled.Properties == null) return null;

        foreach (var prop in tiled.Properties)
        {
            if (prop.Name == name) return prop;
        }

        return null;
    }

    public static string? GetProperty(this IHasTiledProperties tiled, string name)
    {
        return GetPropertyEntry(tiled, name)?.Value;
    }

    public static bool TryGetProperty(this IHasTiledProperties tiled, string name, out string value)
    {
        var entry = GetPropertyEntry(tiled, name);
        if (entry == null)
        {
            value = "";
            return false;
        }

        value = entry.Value ?? "";
        return true;
    }

    public static bool TryGetEnumProperty<T>(this IHasTiledProperties tiled, string name, out T value) where T : struct
    {
        value = default;
        var stringvalue = GetProperty(tiled, name);
        if (stringvalue == null) return false;
        return Enum.TryParse<T>(stringvalue, ignoreCase: true, out value);
    }

    public static T GetEnumProperty<T>(this IHasTiledProperties tiled, string name, T defaultValue = default) where T : struct
    {
        var stringvalue = GetProperty(tiled, name);
        if (stringvalue == null) return defaultValue;
        return Enum.Parse<T>(stringvalue, ignoreCase: true);
    }

    public static T? GetNullableEnumProperty<T>(this IHasTiledProperties tiled, string name, T? defaultValue = null) where T : struct
    {
        var stringvalue = GetProperty(tiled, name);
        if (stringvalue == null) return defaultValue;
        return Enum.Parse<T>(stringvalue, ignoreCase: true);
    }

    public static T[] GetEnumArray<T>(this IHasTiledProperties tiled, string name) where T : struct
    {
        var stringvalue = GetProperty(tiled, name);
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
        var stringvalue = GetProperty(tiled, name);
        if (stringvalue == null) return false;
        return bool.Parse(stringvalue);
    }

    public static bool TryGetIntProperty(this IHasTiledProperties tiled, string name, out int value)
    {
        var stringvalue = GetProperty(tiled, name);
        return int.TryParse(stringvalue, out value);
    }

    public static int? GetIntPropertyOrNull(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = GetProperty(tiled, name);
        return int.TryParse(stringvalue, out var value) ? value : null;
    }

    public static PointXY GetPoint(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = GetProperty(tiled, name);
        if (stringvalue == null) return default;

        var index = stringvalue.IndexOf(',');
        var span = stringvalue.AsSpan();
        return new PointXY(int.Parse(span[..index]), int.Parse(span[(index + 1)..]));
    }

    public static T GetJsonProperty<T>(this IHasTiledProperties tiled, string name)
    {
        var stringvalue = GetProperty(tiled, name);
        return JsonSerializer.Deserialize<T>(stringvalue);
    }

    public static ImmutableDictionary<string, string> GetPropertyDictionary(this IHasTiledProperties properties)
    {
        if (properties.Properties == null) return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var prop in properties.Properties)
        {
            if (prop.Value == null) continue;
            builder.Add(prop.Name, prop.Value);
        }

        return builder.ToImmutable();
    }

    public static bool IsInQuest(this IHasTiledProperties tiled, int questId)
    {
        var questIdString = GetProperty(tiled, TiledLayerProperties.QuestId);
        if (questIdString == null) return true;
        return int.TryParse(questIdString, out var id) && id == questId;
    }

    public static T? GetClass<T>(this IHasTiledProperties tiled, string name) where T : class
    {
        var entry = GetPropertyEntry(tiled, name);
        if (entry == null) return null;
        return entry.ClassValue as T;
    }

    public static T ExpectClass<T>(this IHasTiledProperties tiled, string name) where T : class
    {
        return tiled.GetClass<T>(name) ?? throw new Exception($"Expected class \"{typeof(T).Name}\" at property \"{name}\".");
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

[DebuggerDisplay("{Filename}")]
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
    [JsonPropertyName("propertyTypes")]
    public TiledProjectCustomProperty[] PropertyTypes { get; set; } = [];
}

public enum TiledProjectCustomPropertyType { Enum, Class }
public enum TiledProjectCustomPropertyUseAs { Property, Map, Layer, Object, Tile, Tileset, Wangcolor, Wangset, Project }

[DebuggerDisplay("{Name} ({Type})")]
public sealed class TiledProjectCustomProperty
{
    public int Id { get; set; }
    public required string Name { get; set; }
    [JsonPropertyName("storageType")]
    public string StorageType { get; set; } = "string";
    public TiledProjectCustomPropertyType Type { get; set; }
    public string[]? Values { get; set; }
    [JsonPropertyName("valuesAsFlags")]
    public bool ValuesAsFlags { get; set; }
    public TiledProperty[]? Members { get; set; }
    [JsonPropertyName("useAs")]
    public TiledProjectCustomPropertyUseAs[] UseAs { get; set; } = _defaultUseAs;

    // Since we're passing this by reference, it should be immutable. So... don't break it :_)
    private static readonly TiledProjectCustomPropertyUseAs[] _defaultUseAs = [TiledProjectCustomPropertyUseAs.Property, TiledProjectCustomPropertyUseAs.Map, TiledProjectCustomPropertyUseAs.Layer, TiledProjectCustomPropertyUseAs.Object, TiledProjectCustomPropertyUseAs.Tile, TiledProjectCustomPropertyUseAs.Tileset, TiledProjectCustomPropertyUseAs.Wangcolor, TiledProjectCustomPropertyUseAs.Wangset, TiledProjectCustomPropertyUseAs.Project];

    public static IEnumerable<TiledProjectCustomProperty> From(params Type[] types)
    {
        var hasSeen = new HashSet<Type>();
        var toevaluate = new Stack<Type>(types);

        while (toevaluate.Count > 0)
        {
            var type = toevaluate.Pop();

            if (!hasSeen.Add(type)) continue;

            var innerType = type.GetInnerType();
            if (innerType.IsEnum)
            {
                yield return FromEnum(type);
                continue;
            }

            if (type == typeof(string)) continue;

            yield return FromClass(innerType, out var nestedTypes);

            foreach (var nestedType in nestedTypes)
            {
                toevaluate.Push(nestedType);
            }
        }
    }

    private static TiledProjectCustomProperty FromEnum(Type type)
    {
        var names = Enum.GetNames(type)
            .Where(name => !(type.GetField(name) ?? throw new Exception())
                .GetCustomAttributes(typeof(TiledIgnoreAttribute), false)
                .Any())
            .ToArray();

        return new TiledProjectCustomProperty
        {
            Name = type.Name,
            StorageType = "string",
            Type = TiledProjectCustomPropertyType.Enum,
            ValuesAsFlags = type.GetCustomAttributes(typeof(TiledSelectableEnumAttribute), false).Any(),
            Values = names,
        };
    }

    private static TiledProjectCustomProperty FromClass(Type type, out Type[] nestedTypes)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(t => t.GetCustomAttribute<TiledIgnoreAttribute>() == null)
            .ToArray();

        static TiledProperty CreateTiledProperty(string name, TiledPropertyType type)
        {
            return new TiledProperty
            {
                Name = name,
                Type = type,
            };
        }

        var members = new TiledProperty[properties.Length];
        // var instance = Activator.CreateInstance(type);
        var nestedTypeList = new List<Type>();
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            // var value = property.GetValue(instance);
            var innerType = property.PropertyType.GetInnerType(out var isArray, out _);
            if (innerType.Name == "PointXY")
            {
            }
            members[i] = innerType switch
            {
                _ when innerType == typeof(int) => CreateTiledProperty(property.Name, TiledPropertyType.Int),
                _ when innerType == typeof(bool) => CreateTiledProperty(property.Name, TiledPropertyType.Bool),
                _ when innerType == typeof(string) => CreateTiledProperty(property.Name, TiledPropertyType.String),
                _ when innerType.IsEnum => new TiledProperty
                {
                    Name = property.Name,
                    // Value = value?.ToString(),
                    Type = TiledPropertyType.Enum,
                    PropertyType = innerType.Name,
                },
                _ when innerType is { IsClass: true, IsArray: false } => new TiledProperty
                {
                    Name = property.Name,
                    // Value = value,
                    Type = TiledPropertyType.Class,
                    PropertyType = innerType.Name,
                },
                // PointXY typed => new TiledProperty(property.Name, typed),
                // _ => new TiledProperty(property.Name, value)
                _ => throw new Exception()
            };

            if (innerType.IsClass || innerType.IsEnum) nestedTypeList.Add(innerType);
        }

        nestedTypes = nestedTypeList.ToArray();

        return new TiledProjectCustomProperty
        {
            Name = type.Name,
            Type = TiledProjectCustomPropertyType.Class,
            Members = members,
        };
    }
}

public static class TiledPropertySerializer<T>
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly PropertyInfo[] _properties;

    static TiledPropertySerializer()
    {
        _properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(t => t.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .Where(t => t.GetCustomAttribute<TiledIgnoreAttribute>() == null)
            .ToArray();
    }

    public static TiledProperty[] Serialize(T obj)
    {
        static TiledProperty CreateProperty(Type type, string name, object value)
        {
            if (value is int i) return new TiledProperty(name, i);
            if (value is bool b) return new TiledProperty(name, b);
            if (value is string s) return new TiledProperty(name, s);
            if (value is TiledProperty p) return p;
            if (type.IsEnum) return new TiledProperty(name, value.ToString()!);
            if (type.IsClass) return TiledProperty.ForClass(name, type, value);

            throw new Exception($"Unable to serialize property \"{name}\" of type \"{type.Name}\".");
        }

        var tiledProperties = new List<TiledProperty>();
        foreach (var prop in _properties)
        {
            var value = prop.GetValue(obj);
            if (value == null) continue;

            var propertyName = prop.Name;
            var innerType = prop.PropertyType.GetInnerType(out var isArray, out _);
            if (isArray)
            {
                var asArray = (Array)value;
                for (var i = 0; i < asArray.Length; i++)
                {
                    var arrayValue = asArray.GetValue(i);
                    if (arrayValue == null) continue;
                    tiledProperties.Add(CreateProperty(innerType, $"{propertyName}[{i}]", arrayValue));
                }
                continue;
            }

            var defaultValue = prop.PropertyType.GetDefaultValue();
            if (value == defaultValue) continue;
            tiledProperties.Add(CreateProperty(innerType, propertyName, value));
        }

        return tiledProperties.ToArray();
    }

    private readonly record struct ArrayEntry(TiledProperty TiledProperty, int Index);

    public static T Deserialize(IHasTiledProperties source)
    {
        var obj = Activator.CreateInstance<T>();
        if (source.Properties == null) return obj;

        static object? DeserializeProperty(Type type, string name, TiledProperty? tiledProperty)
        {
            var innerType = type.GetInnerType(out _, out var isNullable);

            if (innerType.IsClass && innerType != typeof(string))
            {
                return tiledProperty?.ClassValue;
            }

            if (tiledProperty == null || tiledProperty.Value == null
                || (string.IsNullOrWhiteSpace(tiledProperty.Value) && innerType != typeof(string)))
            {
                if (isNullable) return null;
                if (innerType == typeof(int)) return 0;
                if (innerType == typeof(bool)) return false;
                if (innerType == typeof(string)) return null;
                if (innerType.IsEnum) return Enum.ToObject(innerType, 0);
                return null;
            }
            {
                if (innerType == typeof(int)) return int.Parse(tiledProperty.Value!);
                if (innerType == typeof(bool)) return bool.Parse(tiledProperty.Value!);
                if (innerType == typeof(string)) return tiledProperty.Value;
                if (innerType.IsEnum) return Enum.Parse(innerType, tiledProperty.Value!, ignoreCase: true);
                if (innerType.IsClass) return tiledProperty.ClassValue;
            }

            throw new Exception($"Unable to deserialize property \"{name}\" of type \"{type.Name}\".");
        }

        foreach (var prop in _properties)
        {
            var propertyName = prop.Name;

            if (prop.PropertyType.IsArray && prop.PropertyType != typeof(string))
            {
                var arrayEntries = new List<ArrayEntry>();
                foreach (var tiledProperty in source.Properties)
                {
                    var parser = new StringParser();
                    var nameSpan = tiledProperty.Name.AsSpan();
                    if (!parser.TryExpectWord(nameSpan, out var word)) continue;
                    if (!propertyName.IStartsWith(word)) continue;

                    if (parser.TryExpectChar(nameSpan, '[')
                        && parser.TryExpectInt(nameSpan, out var index)
                        && parser.TryExpectChar(nameSpan, ']'))
                    {
                        arrayEntries.Add(new ArrayEntry(tiledProperty, index));
                    }
                    else
                    {
                        arrayEntries.Add(new ArrayEntry(tiledProperty, -1));
                    }
                }

                var orderedEntries = arrayEntries.OrderBy(e => e.Index).ToArray();
                var array = Array.CreateInstance(prop.PropertyType.GetElementType()!, orderedEntries.Length);
                for (var i = 0; i < orderedEntries.Length; ++i)
                {
                    var arrayProperty = orderedEntries[i].TiledProperty;
                    var arrayValue = DeserializeProperty(prop.PropertyType, propertyName, arrayProperty);
                    if (arrayValue != null) array.SetValue(arrayValue, i);
                }
                prop.SetValue(obj, array);
                continue;
            }

            var propertyValue = source.GetPropertyEntry(propertyName);
            var value = DeserializeProperty(prop.PropertyType, propertyName, propertyValue);
            if (value != null) prop.SetValue(obj, value);
        }

        if (obj is IHasInitialization init) init.Initialize();

        return obj;
    }
}

internal static class TypeExtensions
{
    private static readonly Dictionary<Type, object?> _defaultValues = new();

    public static Type GetInnerType(this Type type) => GetInnerType(type, out _, out _);
    public static Type GetInnerType(this Type type, out bool isArray, out bool isNullable)
    {
        isArray = false;
        isNullable = false;
        if (type == typeof(string)) return type;

        var innerType = type;

        if (innerType.IsArray && innerType != typeof(string))
        {
            isArray = true;
            innerType = innerType.GetElementType()
                ?? throw new Exception($"\"{innerType.Name}\" array lacked an element type.");
        }

        var nullableType = Nullable.GetUnderlyingType(innerType);
        if (nullableType != null)
        {
            isNullable = true;
            innerType = nullableType;
        }

        return innerType;
    }

    // Not thread safe.
    public static object? GetDefaultValue(this Type type)
    {
        if (!type.IsValueType) return null;

        if (!_defaultValues.TryGetValue(type, out var value))
        {
            value = Activator.CreateInstance(type);
            _defaultValues[type] = value;
        }
        return value;
    }
}