using System.Text.Json.Serialization;

namespace z1.Common.Data;

internal sealed class TiledMap
{
    public string Version { get; set; }
    public string Type { get; set; }
    public int CompressionLevel { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Infinite { get; set; }
    public TiledLayer[] Layers { get; set; }
    public int NextLayerId { get; set; }
    public int NextObjectId { get; set; }
    public string Orientation { get; set; }
    public string RenderOrder { get; set; }
    public string TiledVersion { get; set; }
    public int TileHeight { get; set; }
    public int TileSets { get; set; }
    public int TileWidth { get; set; }
}

internal enum TiledLayerType
{
    TileLayer,
    ObjectGroup,
    ImageLayer,
}

internal sealed class TiledLayer
{
    public int Id { get; set; }
    public string Type { get; set; }
    public byte[] Data { get; set; }
    public string Compression { get; set; }
    public string Encoding { get; set; }
    public string DrawOrder { get; set; }
    public string Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float Opacity { get; set; }
    public bool Visible { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public TiledProperty[] Properties { get; set; }

    [JsonIgnore]
    public TiledLayerType LayerType => Type switch
    {
        "tilelayer" => TiledLayerType.TileLayer,
        "objectgroup" => TiledLayerType.ObjectGroup,
        "imagelayer" => TiledLayerType.ImageLayer,
        _ => throw new System.Exception($"Unknown layer type: {Type}"),
    };
}

internal sealed class TiledProperty
{
    public string Type { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
}

// Need properties like "bomb removes"
internal sealed class TiledLayerObject
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Visible { get; set; }
    public float Rotation { get; set; }
    public TiledProperty[] Properties { get; set; }
}