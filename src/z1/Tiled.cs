using System.Collections.Immutable;
using System.Diagnostics;
using z1.Common.Data;
using z1.IO;
using z1.Render;

namespace z1;

internal enum GameTileLayerType
{
    Default,
    Background,
    Palette,
    Behavior,
}

// This current system of doing this is allocation heavy and not very versatile. It should be made to be flexible.
internal readonly record struct GameBlockObjectEntry(BlockObjType Type, int TileId, bool HasOffsets, int XTileOffset, int YTileOffset);
// GameBlockObject's are 2x2's of tiles that are interactable in the game world. Push blocks, bombable caves, etc.
internal readonly record struct GameBlockObject(BlockObjType Type, TiledTile TopLeft, TiledTile TopRight, TiledTile BottomLeft, TiledTile BottomRight);

[DebuggerDisplay("{Name}")]
internal sealed class GameTileSet
{
    public string Name { get; set; }
    public GLImage Image { get; set; }
    public TileBehavior[] Behaviors { get; }
    public GameBlockObject[] BlockObjects { get; } = [];

    public GameTileSet(string name, int tileSetId, TiledTileSet tileset)
    {
        Name = name;
        Image = Graphics.CreateImage(new Asset(tileset.Image));
        Behaviors = new TileBehavior[tileset.TileCount + 1];

        if (tileset.Tiles != null)
        {
            var foundBlockObjectEntries = new List<GameBlockObjectEntry>();

            foreach (var tile in tileset.Tiles)
            {
                // The tile ID is 1-based, so we're adding 1 here so TileId's map directly to the index.
                Behaviors[tile.Id + 1] = tile.GetEnumProperty(
                    TiledTileSetTileProperties.Behavior,
                    TiledTileSetTileProperties.DefaultTileBehavior);

                var blockObectName = tile.GetNullableEnumProperty<BlockObjType>(TiledTileSetTileProperties.Object);
                if (blockObectName != null)
                {
                    if (tile.TryGetProperty(TiledTileSetTileProperties.ObjectOffsets, out var offsetsString))
                    {
                        foreach (var point in ParsePointsString(offsetsString))
                        {
                            foundBlockObjectEntries.Add(new GameBlockObjectEntry(blockObectName.Value, tile.Id, true, point.X, point.Y));
                        }
                    }
                    else
                    {
                        foundBlockObjectEntries.Add(new GameBlockObjectEntry(blockObectName.Value, tile.Id, false, 0, 0));
                    }
                }
            }

            var foundBlockObjects = new List<GameBlockObject>();

            foreach (var groupEntry in foundBlockObjectEntries.GroupBy(t => t.Type))
            {
                var first = groupEntry.First();
                if (!first.HasOffsets)
                {
                    if (groupEntry.Count() != 1) throw new Exception();
                    var tile = TiledTile.Create(first.TileId + 1, tileSetId);
                    foundBlockObjects.Add(new GameBlockObject(first.Type, tile, tile, tile, tile));
                    continue;
                }

                var entries = groupEntry.ToArray();
                if (entries.Length != 4) throw new Exception();
                var topLeftTileId = -1;
                var topRightTileId = -1;
                var bottomLeftTileId = -1;
                var bottomRightTileId = -1;
                foreach (var entry in entries)
                {
                    switch (entry)
                    {
                        case { XTileOffset: 0, YTileOffset: 0 }: topLeftTileId = entry.TileId; break;
                        case { XTileOffset: 1, YTileOffset: 0 }: topRightTileId = entry.TileId; break;
                        case { XTileOffset: 0, YTileOffset: 1 }: bottomLeftTileId = entry.TileId; break;
                        case { XTileOffset: 1, YTileOffset: 1 }: bottomRightTileId = entry.TileId; break;
                        default: throw new Exception();
                    }
                }

                if (topLeftTileId == -1 || topRightTileId == -1 || bottomLeftTileId == -1 || bottomRightTileId == -1)
                {
                    throw new Exception();
                }

                foundBlockObjects.Add(new GameBlockObject(
                    first.Type,
                    TiledTile.Create(topLeftTileId + 1, tileSetId),
                    TiledTile.Create(topRightTileId + 1, tileSetId),
                    TiledTile.Create(bottomLeftTileId + 1, tileSetId),
                    TiledTile.Create(bottomRightTileId + 1, tileSetId)
                ));
            }

            BlockObjects = foundBlockObjects.ToArray();
        }
    }

    internal static ImmutableArray<Point> ParsePointsString(ReadOnlySpan<char> offsetsString)
    {
        var parser = new StringParser();
        var points = new List<Point>();

        for (; parser.Index < offsetsString.Length;)
        {
            parser.SkipOptionalWhiteSpace(offsetsString);
            parser.ExpectChar(offsetsString, '(');
            var x = parser.ExpectInt(offsetsString);
            parser.ExpectChar(offsetsString, ',');
            var y = parser.ExpectInt(offsetsString);
            parser.ExpectChar(offsetsString, ')');
            parser.SkipOptionalWhiteSpace(offsetsString);
            if (parser.Index < offsetsString.Length) parser.ExpectChar(offsetsString, ',');
            points.Add(new Point(x, y));
        }

        return points.ToImmutableArray();
    }
}

// An entire map (overworld, a single dungeon), which is broken into screens.
internal sealed class GameMap
{
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public GameTileSet[] TileSets { get; } = [];
    public GameMapTileLayer[] Layers { get; } = [];
    public GameMapTileLayer BackgroundLayer { get; }
    public Palette[] Palettes { get; } = [];
    public GameMapTileLayer? PaletteLayer { get; }
    public GameMapTileLayer? BehaviorLayer { get; }
    public GameMapObjectLayer ObjectLayer { get; }

    // TODO: public MapType ...

    public GameScreen[] Screens { get; }

    public GameMap(Game game, string name, TiledMap map, int questId)
    {
        Name = name;
        Width = map.Width;
        Height = map.Height;
        TileWidth = map.TileWidth;
        TileHeight = map.TileHeight;

        // Stores the entire map's worth of behaviors merged from all sources, with coinciding indexes to the tiles.
        var behaviors = new TileBehavior[Width * Height];
        Palettes = new Palette[Width * Height];

        if (map.Layers == null) throw new Exception();
        if (map.TileSets == null) throw new Exception();

        // Setup tilesets
        TileSets = new GameTileSet[map.TileSets.Length];
        for (var i = 0; i < map.TileSets.Length; i++)
        {
            // TODO: Better error handling.
            var tileset = map.TileSets[i];
            var filename = Path.GetFileName(tileset.Source);
            var tilesetName = Path.GetFileNameWithoutExtension(filename);
            var source = new Asset(filename);
            TileSets[i] = new GameTileSet(tilesetName, i, source.ReadJson<TiledTileSet>());
        }

        // Setup layers
        var layers = new List<GameMapTileLayer>(map.Layers.Length);
        var objectLayers = new List<TiledLayer>(map.Layers.Length);

        foreach (var layer in map.Layers)
        {
            if (!layer.IsInQuest(questId)) continue;

            switch (layer.Type)
            {
                case TiledLayerType.TileLayer:
                    var zlayer = new GameMapTileLayer(layer);
                    switch (zlayer.Type)
                    {
                        case GameTileLayerType.Background: BackgroundLayer = zlayer; break;
                        case GameTileLayerType.Palette: PaletteLayer = zlayer; break;
                        case GameTileLayerType.Behavior: BehaviorLayer = zlayer; break;
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
        ObjectLayer = new GameMapObjectLayer(objectLayers.SelectMany(t => t.Objects ?? []));

        BackgroundLayer ??= Layers.FirstOrDefault()
            ?? throw new Exception($"Unable to find background layer for map {name}");

        for (var i = 0; i < BackgroundLayer.Tiles.Length; ++i)
        {
            var tile = BackgroundLayer.Tiles[i];
            if (tile.TileId == 0) continue;
            behaviors[i] = TileSets[tile.TileSheet].Behaviors[tile.TileId];
        }

        // This layer overrides the default behavior when a tile is set.
        // It'll behave as though it's the tile drawn on this layer instead.
        if (BehaviorLayer != null)
        {
            for (var i = 0; i < BehaviorLayer.Tiles.Length; i++)
            {
                var tile = BehaviorLayer.Tiles[i];
                if (tile.TileId == 0) continue;
                behaviors[i] = TileSets[tile.TileSheet].Behaviors[tile.TileId];
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

        var objectsByOwner = ObjectLayer.Objects
            .Where(t => !string.IsNullOrWhiteSpace(t.OwnerName))
            .GroupBy(t => t.OwnerName)
            .ToDictionary(t => t.Key, t => t.ToArray());

        // JOE: TODO: Pre-count most of the things so you can preallocate arrays instead of using lists.
        // Setup screens
        var screens = new List<GameScreen>(World.WorldWidth * World.WorldHeight);
        foreach (var obj in ObjectLayer.Objects)
        {
            if (obj is not ScreenGameMapObject screenObject) continue;

            var startX = obj.MapX / TileWidth;
            var startY = obj.MapY / TileHeight;
            var endX = (obj.MapX + obj.Width - 1) / TileWidth;
            var endY = (obj.MapY + obj.Height - 1) / TileHeight;
            var width = endX - startX + 1;
            var height = endY - startY + 1;

            var owned = screenObject.Name != null && objectsByOwner.TryGetValue(screenObject.Name, out var o) ? o : [];
            var screen = new GameScreen(game, screenObject, owned, startX, startY, width, height);

            var backgroundTiles = new ReadOnlySpan<TiledTile>(BackgroundLayer.Tiles);
            var behaviorsSpan = new ReadOnlySpan<TileBehavior>(behaviors);
            for (var y = 0; y < height; y++)
            {
                var rowStartIndex = (startY + y) * Width + startX;
                var tilerow = backgroundTiles.Slice(rowStartIndex, width);
                var behaviorrow = behaviorsSpan.Slice(rowStartIndex, width);

                tilerow.CopyTo(screen.Map.Tiles.AsSpan(y * width, width));
                behaviorrow.CopyTo(screen.Map.Behaviors.AsSpan(y * width, width));
            }

            screens.Add(screen);
        }

        Screens = screens
            .OrderBy(t => t.TileY)
            .ThenBy(t => t.TileX)
            .ToArray();
    }

    public TileBehavior GetBehavior(TiledTile tile)
    {
        return TileSets[tile.TileSheet].Behaviors[tile.TileId];
    }

    public bool TryGetBlockObjectTiles(BlockObjType blockObjectType, out GameBlockObject blockObject)
    {
        foreach (var tileset in TileSets)
        {
            foreach (var currentObject in tileset.BlockObjects)
            {
                if (currentObject.Type == blockObjectType)
                {
                    blockObject = currentObject;
                    return true;
                }
            }
        }

        blockObject = default;
        return false;
    }

    public void DrawTile(TiledTile tile, int x, int y, Palette palette)
    {
        // JOE: TODO: Hey yo. Lots of repeated work here. Make a table for it.
        var tileset = TileSets[tile.TileSheet];
        var image = tileset.Image;
        var tileId = tile.TileId - 1; // 0 means no tile.
        if (tileId < 0) return;
        var srcx = tileId % (image.Width / TileWidth) * TileWidth;
        var srcy = tileId / (image.Width / TileWidth) * TileHeight;
        Graphics.DrawImage(image, srcx, srcy, TileWidth, TileHeight, x, y, palette, tile.GetDrawingFlags());
    }

    private IEnumerable<TiledTile> GetInclusiveTiles(GameMapTileLayer layer, GameObjectLayerObject obj)
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

    private IEnumerable<Point> GetInclusiveTileCoordinates(GameObjectLayerObject obj)
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

internal sealed class GameMapTileLayer
{
    public string Name { get; set; }
    public GameTileLayerType Type { get; set; }
    public TiledTile[] Tiles { get; }

    public GameMapTileLayer(TiledLayer layer)
    {
        Tiles = layer.Tiles.ToArray();
        Name = layer.Name;
        Type = Enum.TryParse<GameTileLayerType>(layer.Name, true, out var type) ? type : default;
    }
}

internal readonly record struct GameObjectLayerObject(
    int X, int Y, int Width, int Height,
    GameObjectLayerObjectType Type,
    ImmutableDictionary<string, string> Properties);

[DebuggerDisplay("{Name} ({MapX}, {MapY})")]
internal abstract class GameMapObject
{
    public string Name { get; }
    public int MapX { get; }
    public int MapY { get; }
    public int Width { get; }
    public int Height { get; }
    // public GameObjectLayerObjectType Type { get; }
    public string OwnerName { get; }
    public GameScreen? Owner { get; set; }
    public ImmutableDictionary<string, string> Properties { get; }

    protected GameMapObject(TiledLayerObject layerObject)
    {
        Name = layerObject.Name;
        MapX = layerObject.X;
        MapY = layerObject.Y;
        Width = layerObject.Width;
        Height = layerObject.Height;
        OwnerName = layerObject.GetProperty(TiledObjectProperties.Owner) ?? "";
        Properties = layerObject.GetPropertyDictionary();
    }

    public void GetScreenTileCoordinates(GameMap map, out int tileX, out int tileY)
    {
        if (Owner == null) throw new Exception($"Can not get screen coordinates of unknowned object \"{Name}\"");

        var objtilex = MapX / map.TileWidth;
        var objtiley = MapY / map.TileHeight;
        tileX = objtilex - Owner.TileX;
        tileY = objtiley - Owner.TileY;
    }

    public void GetTileSize(GameMap map, out int tileWidth, out int tileHeight)
    {
        tileWidth = Width / (map.TileWidth);
        tileHeight = Height / (map.TileHeight );
    }
}

[DebuggerDisplay("{Name}")]
internal sealed class GameScreen
{
    public GameScreenMap Map { get; }
    public SoundEffect? AmbientSound => _mapObject.AmbientSound;
    public ImmutableArray<ObjType> Monsters => _mapObject.Monsters;
    public string Name => _mapObject.Name;
    public int ZoraCount => _mapObject.ZoraCount;
    public bool MonstersEnter => _mapObject.MonstersEnter;
    public ImmutableArray<Direction> Maze => _mapObject.Maze;
    public Direction MazeExit => _mapObject.MazeExit;
    public ImmutableArray<ActionGameMapObject> ActionMapObjects { get; set; }
    public int TileX { get; }
    public int TileY { get; }
    public int ScreenTileWidth { get; }
    public int ScreenTileHeight { get; }
    public Palette InnerPalette => _mapObject.InnerPalette;
    public Palette OuterPalette => _mapObject.OuterPalette;

    private readonly Game _game;
    private readonly ScreenGameMapObject _mapObject;
    private readonly GameMapObject[] _ownedObjects;
    private readonly int _waterTileCount;

    public GameScreen(
        Game game, ScreenGameMapObject mapObject, GameMapObject[] ownedObjects,
        int tileX, int tileY, int screenTileWidth, int screenTileHeight)
    {
        _game = game;
        _mapObject = mapObject;
        _ownedObjects = ownedObjects;
        Map = new GameScreenMap(screenTileWidth, screenTileHeight);
        ActionMapObjects = ownedObjects.OfType<ActionGameMapObject>().ToImmutableArray();
        TileX = tileX;
        TileY = tileY;
        ScreenTileWidth = screenTileWidth;
        ScreenTileHeight = screenTileHeight;
        _waterTileCount = CountWaterTiles();

        foreach (var obj in ActionMapObjects) obj.Owner = this;
    }

    private int CountWaterTiles()
    {
        var waterCount = 0;
        for (var tileY = 0; tileY < ScreenTileHeight - 1; tileY++)
        {
            for (var tileX = 0; tileX < ScreenTileWidth - 1; tileX++)
            {
                if (!Map.CheckBlockBehavior(tileX, tileY, TileBehavior.Water)) continue;
                waterCount++;
            }
        }

        return waterCount;
    }

    public IEnumerable<GameMapObject> GetObjects(GameMap map, int tileX, int tileY)
    {
        foreach (var obj in _ownedObjects)
        {
            obj.GetScreenTileCoordinates(map, out var objectTileX, out var objectTileY);

            if (tileX < objectTileX || tileX >= objectTileX + obj.Width / map.TileWidth) continue;
            if (tileY < objectTileY || tileY >= objectTileY + obj.Height / map.TileHeight) continue;
            yield return obj;
        }
    }

    public bool TryGetActionObject(GameMap map, TileAction action, int tileX, int tileY, out ActionGameMapObject actionObject)
    {
        actionObject = default;
        foreach (var obj in GetObjects(map, tileX, tileY))
        {
            if (obj is not ActionGameMapObject currentObject) continue;
            if (currentObject.Action != action) continue;
            actionObject = currentObject;
            return true;
        }

        return false;
    }

    public ActionGameMapObject GetActionObject(GameMap map, TileAction action, int tileX, int tileY)
    {
        if (TryGetActionObject(map, action, tileX, tileY, out var actionObject)) return actionObject;

        throw new Exception($"No object of type {action} found at {tileX}, {tileY}");
    }

    public Cell GetRandomWaterTile()
    {
        if (_waterTileCount == 0) throw new Exception($"No water found for Zora's in map \"{Name}\"");

        var waterCount = 0;
        var randomCell = _game.Random.Next(0, _waterTileCount);
        for (var tileY = 0; tileY < ScreenTileHeight - 1; tileY++)
        {
            for (var tileX = 0; tileX < ScreenTileWidth - 1; tileX++)
            {
                if (!Map.CheckBlockBehavior(tileX, tileY, TileBehavior.Water)) continue;
                if (waterCount == randomCell) return new Cell((byte)(tileY + World.BaseRows), (byte)tileX);
                waterCount++;
            }
        }

        throw new Exception("Unreachable code.");
    }
}

internal sealed class GameScreenMap
{
    public int Width { get; }
    public int Height { get; }

    public readonly TiledTile[] Tiles;
    public readonly TileBehavior[] Behaviors;

    public TiledTile this[int x, int y]
    {
        get => Tiles[y * Width + x];
        set => Tiles[y * Width + x] = value;
    }

    public GameScreenMap(int width, int height)
    {
        Width = width;
        Height = height;
        var size = width * height;
        Tiles = new TiledTile[size];
        Behaviors = new TileBehavior[size];
    }

    public ref TiledTile Tile(int index) => ref Tiles[index];
    public ref TiledTile Tile(int tileX, int tileY) => ref Tiles[tileY * Width + tileX];
    public ref TileBehavior Behavior(int tileX, int tileY) => ref Behaviors[tileY * Width + tileX];
    public ref TileBehavior Behavior(int index) => ref Behaviors[index];
    public TileBehavior AsBehaviors(int tileX, int tileY)
    {
        tileY = Math.Max(0, Math.Min(tileY, Height - 1));
        tileX = Math.Max(0, Math.Min(tileX, Width - 1));

        return Behaviors[tileY * Width + tileX];
    }

    public bool CheckBlockBehavior(int tileX, int tileY, TileBehavior behavior)
    {
        return Behavior(tileX, tileY) == behavior
            && Behavior(tileX + 1, tileY) == behavior
            && Behavior(tileX, tileY + 1) == behavior
            && Behavior(tileX + 1, tileY + 1) == behavior;
    }

    public void SetBlockBehavior(int tileX, int tileY, TileBehavior behavior)
    {
        Behavior(tileX, tileY) = behavior;
        Behavior(tileX + 1, tileY) = behavior;
        Behavior(tileX, tileY + 1) = behavior;
        Behavior(tileX + 1, tileY + 1) = behavior;
    }

    public void SetBlock(int tileX, int tileY, TiledTile tile)
    {
        this[tileX, tileY] = tile;
        this[tileX + 1, tileY] = tile;
        this[tileX, tileY + 1] = tile;
        this[tileX + 1, tileY + 1] = tile;
    }

    public void SetBlock(int tileX, int tileY, GameBlockObject blockObject)
    {
        this[tileX, tileY] = blockObject.TopLeft;
        this[tileX + 1, tileY] = blockObject.TopRight;
        this[tileX, tileY + 1] = blockObject.BottomLeft;
        this[tileX + 1, tileY + 1] = blockObject.BottomRight;
    }
}

[DebuggerDisplay("{Name}")]
internal sealed class GameMapReference
{
    public ActionGameMapObject MapObject { get; }
    public string Name { get; }

    public GameMapReference(ActionGameMapObject mapObject, string name)
    {
        MapObject = mapObject;
        Name = name;
    }
}

[DebuggerDisplay("{Name} ({Action})")]
internal sealed class ActionGameMapObject : GameMapObject
{
    public TileAction Action { get; set; }
    public GameMapReference? Enters { get; set; }
    public Point ExitPosition { get; set; }
    // The Name of the object which this reveals. Used by overworld push blocks that reveal the shortcut stairs.
    public string? Reveals { get; set; }

    public ActionGameMapObject(TiledLayerObject layerObject) : base(layerObject)
    {
        Action = layerObject.GetEnumProperty<TileAction>(TiledObjectProperties.TileAction);
        var enters = layerObject.GetProperty(TiledObjectProperties.Enters);
        Enters = enters == null ? null : new GameMapReference(this, enters);
        ExitPosition = layerObject.GetPoint(TiledObjectProperties.ExitPosition).ToPoint();
        Reveals = layerObject.GetProperty(TiledObjectProperties.Reveals);
    }
}

[DebuggerDisplay("{Name} ({TileBehavior})")]
internal sealed class TileBehaviorGameMapObject : GameMapObject
{
    public TileBehavior TileBehavior { get; set; }

    public TileBehaviorGameMapObject(TiledLayerObject layerObject) : base(layerObject)
    {
        TileBehavior = layerObject.GetEnumProperty<TileBehavior>(TiledObjectProperties.TileBehavior);
    }
}

internal readonly record struct MonsterEntry(ObjType ObjType, int X, int Y);

internal sealed class ScreenGameMapObject : GameMapObject
{
    public SoundEffect? AmbientSound { get; set; }
    public ImmutableArray<ObjType> Monsters { get; set; }
    public int ZoraCount { get; set; }
    public bool MonstersEnter { get; set; }
    public ImmutableArray<Direction> Maze { get; set; } = [];
    public Direction MazeExit { get; set; }
    public Palette InnerPalette { get; }
    public Palette OuterPalette { get; }

    [ThreadStatic]
    private static List<ObjType>? _temporaryList;

    public ScreenGameMapObject(TiledLayerObject layerObject) : base(layerObject)
    {
        AmbientSound = layerObject.GetNullableEnumProperty<SoundEffect>(TiledObjectProperties.AmbientSound);
        Monsters = ParseMonsters(layerObject.GetProperty(TiledObjectProperties.Monsters), out var zoraCount);
        ZoraCount = zoraCount;
        MonstersEnter = layerObject.GetBooleanProperty(TiledObjectProperties.MonstersEnter);
        Maze = layerObject.GetEnumArray<Direction>(TiledObjectProperties.Maze).ToImmutableArray();
        MazeExit = layerObject.GetEnumProperty<Direction>(TiledObjectProperties.MazeExit);
        InnerPalette = layerObject.GetEnumProperty<Palette>(TiledObjectProperties.InnerPalette);
        OuterPalette = layerObject.GetEnumProperty<Palette>(TiledObjectProperties.OuterPalette);

        // JOE: TODO: Move over to MonsterEntry and allow monsters to be in defined positions.
        // Also allow those to have AlwaysSpawn and stuff defined.
    }

    internal static ImmutableArray<ObjType> ParseMonsters(string? monsterList, out int zoraCount)
    {
        zoraCount = 0;
        if (string.IsNullOrEmpty(monsterList)) return [];

        var parser = new StringParser();
        var list = _temporaryList ??= [];
        list.Clear();

        var monsterListSpan = monsterList.AsSpan();
        for (; parser.Index < monsterListSpan.Length;)
        {
            parser.SkipOptionalWhiteSpace(monsterListSpan);
            var monsterName = parser.ExpectWord(monsterListSpan);
            var count = parser.TryExpectChar(monsterListSpan, '*')
                ? parser.ExpectInt(monsterListSpan)
                : 1;

            if (!Enum.TryParse<ObjType>(monsterName, true, out var type))
            {
                throw new Exception($"Unknown monster type: {monsterName}");
            }

            if (type == ObjType.Zora)
            {
                zoraCount += count;
            }
            else
            {
                for (var j = 0; j < count; j++) list.Add(type);
            }

            if (!parser.TryExpectChar(monsterListSpan, ',')) break;
        }

        return list.ToImmutableArray();
    }
}

internal sealed class GameMapObjectLayer
{
    public GameMapObject[] Objects { get; } = [];

    public GameMapObjectLayer(IEnumerable<TiledLayerObject> objects)
    {
        var list = new List<GameMapObject>(500);
        foreach (var obj in objects)
        {
            var type = obj.GetEnumProperty<GameObjectLayerObjectType>(TiledObjectProperties.Type);
            list.Add(type switch
            {
                GameObjectLayerObjectType.Screen => new ScreenGameMapObject(obj),
                GameObjectLayerObjectType.Action => new ActionGameMapObject(obj),
                GameObjectLayerObjectType.TileBehavior => new TileBehaviorGameMapObject(obj),
                _ => throw new Exception(),
            });
        }

        Objects = list.ToArray();
    }
}