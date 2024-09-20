using System.Collections.Immutable;
using NAudio.MediaFoundation;
using z1.Common.Data;
using z1.IO;
using z1.Render;

namespace z1;

internal enum ZeldaTileLayerType
{
    Default,
    Background,
    Palette,
    Behavior,
}

internal sealed class ZeldaTileSet
{
    public GLImage Image { get; set; }
    public TileBehavior[] Behaviors { get; }

    public ZeldaTileSet(TiledTileSet tileset)
    {
        Image = Graphics.CreateImage(new Asset(tileset.Image));
        Behaviors = new TileBehavior[tileset.TileCount];

        if (tileset.Tiles != null)
        {
            foreach (var tile in tileset.Tiles)
            {
                var tileId = tile.Id;
                Behaviors[tileId] = tile.GetEnumProperty("Behavior", TileBehavior.GenericWalkable);
            }
        }
    }
}

internal sealed class ZeldaMap
{
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public ZeldaTileSet[] TileSets { get; } = [];
    public ZeldaMapTileLayer[] Layers { get; } = [];
    public ZeldaMapObjectLayer[] ObjectLayers { get; } = [];
    public ZeldaMapTileLayer BackgroundLayer { get; }
    public Palette[] Palettes { get; } = [];
    public ZeldaMapTileLayer? PaletteLayer { get; }
    public ZeldaMapTileLayer? BehaviorLayer { get; }
    public ZeldaMapObjectLayer ObjectLayer { get; }

    public ZeldaScreen[] Screens { get; }

    private readonly TileBehavior[] _behaviors;

    public ZeldaMap(string name, TiledMap map, int questId)
    {
        Name = name;
        Width = map.Width;
        Height = map.Height;
        TileWidth = map.TileWidth;
        TileHeight = map.TileHeight;

        _behaviors = new TileBehavior[Width * Height];
        Palettes = new Palette[Width * Height];

        if (map.Layers == null) throw new Exception();
        if (map.TileSets == null) throw new Exception();

        // Setup tilesets
        TileSets = new ZeldaTileSet[map.TileSets.Length];
        for (var i = 0; i < map.TileSets.Length; i++)
        {
            // TODO: Better error handling.
            var tileset = map.TileSets[i];
            var source = new Asset(Path.GetFileName(tileset.Source));
            TileSets[i] = new ZeldaTileSet(source.ReadJson<TiledTileSet>());
        }

        // Setup layers
        var layers = new List<ZeldaMapTileLayer>(map.Layers.Length);
        var objectLayers = new List<TiledLayer>(map.Layers.Length);

        foreach (var layer in map.Layers)
        {
            if (!layer.IsInQuest(questId)) continue;

            switch (layer.Type)
            {
                case TiledLayerType.TileLayer:
                    var zlayer = new ZeldaMapTileLayer(layer);
                    switch (zlayer.Type)
                    {
                        case ZeldaTileLayerType.Background: BackgroundLayer = zlayer; break;
                        case ZeldaTileLayerType.Palette: PaletteLayer = zlayer; break;
                        case ZeldaTileLayerType.Behavior: BehaviorLayer = zlayer; break;
                        default: layers.Add(zlayer); break;
                    }
                    break;
                case TiledLayerType.ObjectGroup:
                    objectLayers.Add(layer);
                    break;
                default:
                    throw new Exception();
            }
        }

        Layers = layers.ToArray();
        ObjectLayers = null; // new ZeldaTiledObjectLayer(objectLayers.ToArray();

        BackgroundLayer ??= Layers.FirstOrDefault()
            ?? throw new Exception($"Unable to find background layer for map {name}");

        ObjectLayer ??= ObjectLayers.FirstOrDefault()
            ?? throw new Exception($"Unable to find object layer for map {name}");

        for (var i = 0; i < BackgroundLayer.Tiles.Length; ++i)
        {
            var tile = BackgroundLayer.Tiles[i];
            if (tile.TileId == 0) continue;
            _behaviors[i] = TileSets[tile.TileSheet].Behaviors[tile.TileId];
        }

        // This layer overrides the default behavior when a tile is set.
        // It'll behave as though it's the tile drawn on this layer instead.
        if (BehaviorLayer != null)
        {
            for (var i = 0; i < BehaviorLayer.Tiles.Length; i++)
            {
                var tile = BehaviorLayer.Tiles[i];
                if (tile.TileId == 0) continue;
                _behaviors[i] = TileSets[tile.TileSheet].Behaviors[tile.TileId];
            }
        }

        if (PaletteLayer != null)
        {
            for (var i = 0; i < PaletteLayer.Tiles.Length; i++)
            {
                var tile = PaletteLayer.Tiles[i];
                if (tile.TileId == 0) continue;
                Palettes[i] = (Palette)tile.TileId;
            }
        }

        // JOE: TODO: Pre-count most of the things so you can preallocate arrays instead of using lists.
        var screens = new List<ZeldaScreen>(World.WorldWidth * World.WorldHeight);
        foreach (var obj in ObjectLayer.Objects)
        {
            if (obj.Type != ZeldaObjectLayerObjectType.Screen) continue;

            var startX = obj.X / TileWidth;
            var startY = obj.Y / TileHeight;
            var endX = (obj.X + obj.Width - 1) / TileWidth;
            var endY = (obj.Y + obj.Height - 1) / TileHeight;
            var width = endX - startX;
            var height = endY - startY;

            var screen = new ZeldaScreen(startX, startY, width, height);

            var backgroundTiles = new ReadOnlySpan<TiledTile>(BackgroundLayer.Tiles);
            var behaviors = new ReadOnlySpan<TileBehavior>(_behaviors);
            for (var y = startY; y <= endY; y++)
            {
                var tilerow = backgroundTiles[(y * Width)..];
                var behaviorrow = behaviors[(y * Width)..];
                for (var x = startX; x <= endX; x++)
                {
                    screen.Map[x, y] = tilerow[x];
                    screen.Map.Behaviors(x, y) = behaviorrow[x];
                }
            }

            screens.Add(screen);
        }

        Screens = screens
            .OrderByDescending(t => t.Y)
            .ThenByDescending(t => t.X)
            .ToArray();
    }

    private IEnumerable<TiledTile> GetInclusiveTiles(ZeldaMapTileLayer layer, ZeldaObjectLayerObject obj)
    {
        var startX = obj.X / TileWidth;
        var startY = obj.Y / TileHeight;
        var endX = (obj.X + obj.Width - 1) / TileWidth;
        var endY = (obj.Y + obj.Height - 1) / TileHeight;

        var tiles = layer.Tiles;
        for (var y = startY; y <= endY; y++)
        {
            var tilerow = y * Width;
            for (var x = startX; x <= endX; x++)
            {
                yield return tiles[tilerow + x];
            }
        }
    }

    private IEnumerable<Point> GetInclusiveTileCoordinates(ZeldaObjectLayerObject obj)
    {
        var startX = obj.X / TileWidth;
        var startY = obj.Y / TileHeight;
        var endX = (obj.X + obj.Width - 1) / TileWidth;
        var endY = (obj.Y + obj.Height - 1) / TileHeight;

        for (var y = startY; y <= endY; y++)
        {
            for (var x = startX; x <= endX; x++)
            {
                yield return new Point(x, y);
            }
        }
    }
}

internal sealed class ZeldaMapTileLayer
{
    public string Name { get; set; }
    public ZeldaTileLayerType Type { get; set; }
    public TiledTile[] Tiles { get; }

    public ZeldaMapTileLayer(TiledLayer layer)
    {
        Tiles = layer.Tiles.ToArray();
        Name = layer.Name;
        Type = Enum.Parse<ZeldaTileLayerType>(Name, true);
    }
}

internal enum ZeldaObjectLayerObjectType
{
    Unknown,
    Screen,
    Action,
    TileBehavior,
}

internal readonly record struct ZeldaObjectLayerObject(
    int X, int Y, int Width, int Height,
    ZeldaObjectLayerObjectType Type,
    ImmutableDictionary<string, string> Properties);

internal abstract class ZeldaMapObject
{
    public string Name { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public ZeldaObjectLayerObjectType Type { get; }
    public ImmutableDictionary<string, string> Properties { get; }

    protected ZeldaMapObject(TiledLayerObject layerObject)
    {
        Name = layerObject.Name;
        X = layerObject.X;
        Y = layerObject.Y;
        Width = layerObject.Width;
        Height = layerObject.Height;
        Properties = layerObject.GetPropertyDictionary();
    }
}

internal sealed class ActionZeldaMapObject : ZeldaMapObject
{
    public TileAction Action { get; set; }
    public string? Enters { get; set; }
    public Point ExitPosition { get; set; }
    // The Name of the object which this reveals. Used by overworld push blocks that reveal the shortcut stairs.
    public string? Reveals { get; set; }
    public string? Owner { get; set; }

    public ActionZeldaMapObject(TiledLayerObject layerObject) : base(layerObject)
    {
        Action = layerObject.GetEnumProperty<TileAction>(TiledObjectProperties.TileAction);
        Enters = layerObject.GetProperty(TiledObjectProperties.Enters);
        ExitPosition = layerObject.GetPoint(TiledObjectProperties.ExitPosition).ToPoint();
        Reveals = layerObject.GetProperty(TiledObjectProperties.Reveals);
        Owner = layerObject.GetProperty(TiledObjectProperties.Owner);
    }
}

internal sealed class TileBehaviorZeldaMapObject : ZeldaMapObject
{
    public TileBehavior TileBehavior { get; set; }

    public TileBehaviorZeldaMapObject(TiledLayerObject layerObject) : base(layerObject)
    {
        TileBehavior = layerObject.GetEnumProperty<TileBehavior>(TiledObjectProperties.TileBehavior);
    }
}

internal sealed class ScreenZeldaMapObject : ZeldaMapObject
{
    public SoundEffect? AmbientSound { get; set; }
    public ObjType[] Monsters { get; set; }
    public bool MonstersEnter { get; set; }

    [ThreadStatic]
    private static List<ObjType>? _temporaryList;

    public ScreenZeldaMapObject(TiledLayerObject layerObject) : base(layerObject)
    {
        AmbientSound = layerObject.GetNullableEnumProperty<SoundEffect>(TiledObjectProperties.AmbientSound);
        Monsters = ParseMonsters(layerObject.GetProperty(TiledObjectProperties.Monsters));
        MonstersEnter = layerObject.GetBooleanProperty(TiledObjectProperties.MonstersEnter);
    }

    internal static ObjType[] ParseMonsters(string monsterList)
    {
        if (string.IsNullOrEmpty(monsterList)) return [];

        var count = 1;
        foreach (var c in monsterList) if (c == ',') count++;
        Span<Range> outputs = stackalloc Range[count];
        var monsterListSpan = monsterList.AsSpan();
        var found = monsterListSpan.Split(outputs, ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = _temporaryList ??= new List<ObjType>(count);
        list.Clear();

        for (var i = 0; i < found; ++i)
        {
            var monster = monsterListSpan[outputs[i]];
            var split = monster.IndexOf('*');
            var monsterName = (split == -1 ? monster : monster[..split]).Trim();
            var monsterCount = split == -1 ? 1 : int.Parse(monster[(split + 1)..].Trim());
            if (!Enum.TryParse<ObjType>(monsterName, true, out var type))
            {
                throw new Exception($"Unknown monster type: {monsterName}");
            }

            for (var j = 0; j < monsterCount; j++) list.Add(type);
        }

        return list.ToArray();
    }
}

internal sealed class ZeldaMapObjectLayer
{
    public ZeldaMapObject[] Objects { get; } = [];

    public ZeldaMapObjectLayer(ReadOnlySpan<TiledLayerObject> objects)
    {
        var list = new List<ZeldaMapObject>(objects.Length);
        foreach (var obj in objects)
        {
            var type = obj.GetEnumProperty<ZeldaObjectLayerObjectType>(TiledObjectProperties.Type);
            list.Add(type switch
            {
                ZeldaObjectLayerObjectType.Screen => new ScreenZeldaMapObject(obj),
                ZeldaObjectLayerObjectType.Action => new ActionZeldaMapObject(obj),
                ZeldaObjectLayerObjectType.TileBehavior => new TileBehaviorZeldaMapObject(obj),
                _ => throw new Exception(),
            });
        }

        Objects = list.ToArray();
    }
}