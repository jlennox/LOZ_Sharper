using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
internal readonly record struct GameBlockObjectEntry(BlockType Type, int TileId, bool HasOffsets, int XTileOffset, int YTileOffset);
// GameBlockObject's are 2x2's of tiles that are interactable in the game world. Push blocks, bombable caves, etc.
internal readonly record struct GameBlockObject(BlockType Type, TiledTile TopLeft, TiledTile TopRight, TiledTile BottomLeft, TiledTile BottomRight);

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
        // JOE: TODO: This `Path.GetFileName` makes me want to cry, infact, most things about how assets work now does.
        Image = Graphics.CreateImage(new Asset(Path.GetFileName(tileset.Image)));
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

                var blockObectName = tile.GetNullableEnumProperty<BlockType>(TiledTileSetTileProperties.Object);
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
                    var tile = TiledTile.Create(first.TileId, tileSetId);
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
                    TiledTile.Create(topLeftTileId, tileSetId),
                    TiledTile.Create(topRightTileId, tileSetId),
                    TiledTile.Create(bottomLeftTileId, tileSetId),
                    TiledTile.Create(bottomRightTileId, tileSetId)
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

internal sealed class GameWorldMap
{
    public bool IsValid { get; }
    public GameRoom?[,] RoomGrid { get; }
    public int Width { get; }
    public int Height { get; }
    public bool DrawWithRoomConnections { get; } // JOE: TODO

    public GameWorldMap(GameWorld world)
    {
        // HEY! This has a big issue of not being able to handle rooms that are not connected to the entry room.
        // To fix this, we need to find rooms not seen, then make logical assumptions about where they go. Yay :)
        var roomsToVisit = new Stack<(GameRoom Room, Point Position)>();
        roomsToVisit.Push((world.EntryRoom, new Point(0, 0)));
        var visitedRooms = new HashSet<GameRoom> { world.EntryRoom };
        var visitedPositions = new Dictionary<Point, GameRoom>();
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MaxValue;
        var maxY = int.MaxValue;
        while (roomsToVisit.Count > 0)
        {
            var (room, position) = roomsToVisit.Pop();
            visitedPositions.Add(position, room);

            if (minX == int.MaxValue || position.X < minX) minX = position.X;
            if (minY == int.MaxValue || position.Y < minY) minY = position.Y;
            if (maxX == int.MaxValue || position.X > maxX) maxX = position.X;
            if (maxY == int.MaxValue || position.Y > maxY) maxY = position.Y;

            foreach (var (direction, nextRoom) in room.Connections)
            {
                if (!visitedRooms.Add(nextRoom)) continue;
                var offset = direction.GetOffset();
                roomsToVisit.Push((nextRoom, new Point(position.X + offset.X, position.Y + offset.Y)));
            }
        }

        if (minX == int.MaxValue || minY == int.MaxValue || maxX == int.MaxValue || maxY == int.MaxValue)
        {
            IsValid = false;
            return;
        }

        IsValid = true;

        var width = maxX - minX + 1;
        var height = maxY - minY + 1;

        // This is just a sanity check. The numbers are not significant.
        if (width > 100 || height > 100)
        {
            IsValid = false;
            return;
        }

        Width = width;
        Height = height;

        var xoffset = Math.Abs(minX);
        var yoffset = Math.Abs(minY);

        var grid = new GameRoom?[width, height];
        foreach (var (position, room) in visitedPositions)
        {
            ref var spot = ref grid[position.X + xoffset, position.Y + yoffset];
            spot = room;
        }

        RoomGrid = grid;
    }
}

// An entire map (overworld, a single dungeon), which is broken into rooms.
[DebuggerDisplay("{Name}")]
internal sealed class GameWorld
{
    public string UniqueId => Name; // not sure if these will remain the same.
    public string Name { get; }
    public GameRoom[] Rooms { get; }
    public GameRoom EntryRoom { get; }
    public GameRoom? BossRoom { get; }
    public GameWorldMap GameWorldMap { get; }
    public GameRoom[] TeleportDestinations { get; }
    public WorldSettings Settings { get; }
    public string? LevelString { get; }
    public bool IsOverworld => Settings.WorldType == GameWorldType.Overworld;

    public bool IsBossAlive => BossRoom != null && BossRoom.PersistedRoomState.ObjectCount != 0;

    public GameWorld(World world, TiledWorld tiledWorld, string filename, int questId)
    {
        Name = Path.GetFileNameWithoutExtension(filename);
        if (tiledWorld.Maps == null) throw new Exception($"World {Name} has no maps.");

        var directory = Path.GetDirectoryName(filename);
        Rooms = new GameRoom[tiledWorld.Maps.Length];
        var worldMaps = new (GameRoom Room, TiledWorldEntry Entry)[tiledWorld.Maps.Length];
        for (var i = 0; i < tiledWorld.Maps.Length; ++i)
        {
            var worldEntry = tiledWorld.Maps![i];
            var asset = new Asset(directory, worldEntry.Filename);
            var tiledmap = asset.ReadJson<TiledMap>();
            var entryName = Path.GetFileNameWithoutExtension(worldEntry.Filename);
            var room = new GameRoom(world, this, worldEntry, entryName, tiledmap, questId);
            if (room.Settings.IsEntryRoom) EntryRoom = room;
            if (room.Settings.IsBossRoom) BossRoom = room;
            worldMaps[i] = (room, worldEntry);
            Rooms[i] = room;
        }

        Settings = tiledWorld.GetJsonProperty<WorldSettings>(TiledWorldProperties.WorldSettings);
        if (Settings.LevelNumber > 0) LevelString = $"Level-{Settings.LevelNumber}";

        EntryRoom ??= Rooms[0];

        TeleportDestinations = Rooms
            .Where(t => t.RecorderDestination != null)
            .OrderBy(t => t.RecorderDestination!.Slot)
            .ToArray();

        var orderedWorldMaps = worldMaps
            .OrderBy(t => t.Entry.Y)
            .ThenBy(t => t.Entry.X)
            .ToArray();

        // Build out how rooms connect to each other.
        // This can use a massive optimization.
        foreach (var (room, entry) in orderedWorldMaps)
        {
            if (entry.X > 0)
            {
                var leftroom = orderedWorldMaps.FirstOrDefault(t => t.Entry.Right == entry.X && t.Entry.Y == entry.Y);
                if (leftroom != default)
                {
                    room.Connections[Direction.Left] = leftroom.Room;
                    leftroom.Room.Connections[Direction.Right] = room;
                }
            }

            if (entry.Y > 0)
            {
                var aboveroom = orderedWorldMaps.FirstOrDefault(t => t.Entry.X == entry.X && t.Entry.Bottom == entry.Y);
                if (aboveroom != default)
                {
                    room.Connections[Direction.Up] = aboveroom.Room;
                    aboveroom.Room.Connections[Direction.Down] = room;
                }
            }
        }

        GameWorldMap = new GameWorldMap(this);
    }

    public static GameWorld Load(World world, string filename, int questId)
    {
        return new GameWorld(world, new Asset(filename).ReadJson<TiledWorld>(), filename, questId);
    }

    public void ResetLevelKillCounts()
    {
        foreach (var map in Rooms) map.LevelKillCount = 0;
    }

    public GameRoom GetRoomByName(string roomName)
    {
        return Rooms.FirstOrDefault(t => roomName.IEquals(t.Name))
            ?? throw new Exception($"Unable to find room name \"{roomName}\" in world \"{Name}\".");
    }

    public GameRoom GetRoomById(string id)
    {
        return Rooms.FirstOrDefault(t => id.IEquals(t.UniqueId))
            ?? throw new Exception($"Unable to find room id \"{id}\" in world \"{Name}\"");
    }
}

[DebuggerDisplay("{GameWorld.UniqueId}/{UniqueId} ({Name})")]
internal sealed class GameRoom
{
    public string UniqueId { get; }
    public string Id { get; }
    public GameWorld GameWorld { get; }
    public TiledWorldEntry WorldEntry { get; }
    public string Name { get; }
    public int WorldX => WorldEntry.X; // The world position, in pixels.
    public int WorldY => WorldEntry.Y;
    public int Width { get; } // The width in tiles (ie, 32)
    public int Height { get; }
    public int TileWidth { get; set; } // The width of a tile (ie, 8)
    public int TileHeight { get; set; }
    public int BlockWidth { get; set; } // The width of a tile (ie, 8)
    public int BlockHeight { get; set; }
    private GameTileSet[] TileSets { get; } = [];
    public GameMapTileLayer[] Layers { get; } = [];
    private GameMapTileLayer BackgroundLayer { get; }
    public Palette[] Palettes { get; } = []; // this is for palette overrides, not used yet.
    private GameMapTileLayer? PaletteLayer { get; }
    private GameMapTileLayer? BehaviorLayer { get; }
    public GameMapObjectLayer ObjectLayer { get; }
    public ImmutableArray<InteractableBlockObject> InteractableBlockObjects { get; set; }
    public ImmutableArray<RoomInteraction> RoomInteractions { get; set; }
    public bool HasUnderworldDoors { get; }
    public Dictionary<Direction, DoorType> UnderworldDoors { get; } = [];
    public ImmutableArray<MonsterEntry> Monsters { get; set; }
    public ShopSpec? CaveSpec { get; set; }
    public int ZoraCount { get; set; }
    public bool MonstersEnter { get; set; }
    public MazeRoom? Maze { get; set; }
    public RoomSettings Settings { get; }
    public RecorderDestination? RecorderDestination { get; }
    public EntryPosition? EntryPosition { get; }

    public RoomTileMap RoomMap { get; }
    private readonly RoomTileMap _unmodifiedRoomMap;
    public bool HidePlayerMapCursor { get; set; }
    public bool IsTriforceRoom { get; set; }
    public bool HasTriforce => DoesContainTriforce();

    public Dictionary<Direction, GameRoom> Connections { get; } = [];
    public PersistedRoomState PersistedRoomState => _roomState.Value;

    // JOE: TODO: Uh, this feels wrong...?
    public int LevelKillCount { get; set; }

    // Old zelda stuff that needs to be refactored in action objects and stuff.
    public int? FireballLayout { get; }

    // I'm not super fond of how these checks work. These should likely be moved over to flags.
    public bool IsCellar => GameWorld.Settings.WorldType == GameWorldType.UnderworldCommon;
    public bool IsCave => GameWorld.Settings.WorldType == GameWorldType.OverworldCommon;

    private readonly World _world;
    private readonly int _waterTileCount;
    private readonly Lazy<PersistedRoomState> _roomState;

    public GameRoom(World world, GameWorld gameWorld, TiledWorldEntry worldEntry, string name, TiledMap map, int questId)
    {
        if (map.Layers == null) throw new Exception();
        if (map.TileSets == null) throw new Exception();

        _world = world;
        GameWorld = gameWorld;

        _roomState = new Lazy<PersistedRoomState>(() => world.Profile.GetRoomFlags(this));

        WorldEntry = worldEntry;
        Name = name;
        Width = map.Width;
        Height = map.Height;
        TileWidth = map.TileWidth;
        TileHeight = map.TileHeight;
        BlockWidth = map.TileWidth * 2; // This assumption will break at some point.
        BlockHeight = map.TileHeight * 2; // This assumption will break at some point.

        RoomMap = new RoomTileMap(Width, Height);

        Id = map.GetProperty(TiledRoomProperties.Id) ?? throw new Exception("Room has no room id.");
        UniqueId = $"{gameWorld.UniqueId}/{Id}";
        Monsters = MonsterEntry.ParseMonsters(map.GetProperty(TiledRoomProperties.Monsters), out var zoraCount);
        ZoraCount = zoraCount;
        MonstersEnter = map.GetBooleanProperty(TiledRoomProperties.MonstersEnter);
        CaveSpec = map.GetClass<ShopSpec>(TiledRoomProperties.CaveSpec);
        Maze = map.GetClass<MazeRoom>(TiledRoomProperties.Maze);
        Settings = map.ExpectClass<RoomSettings>(TiledRoomProperties.RoomSettings);
        RecorderDestination = map.GetClass<RecorderDestination>(TiledObjectProperties.RecorderDestination);
        EntryPosition = map.GetClass<EntryPosition>(TiledRoomProperties.EntryPosition);

        var dungeonDoors = map.GetProperty(TiledRoomProperties.UnderworldDoors);
        if (TryParseUnderworldDoors(dungeonDoors, out var doors))
        {
            HasUnderworldDoors = true;
            UnderworldDoors = doors;
        }

        FireballLayout = map.GetIntPropertyOrNull(TiledRoomProperties.FireballLayout);

        // Stores the entire map's worth of behaviors merged from all sources, with coinciding indexes to the tiles.
        var behaviors = new TileBehavior[Width * Height];
        Palettes = new Palette[Width * Height];

        // Setup tilesets
        // JOE: TODO: MAP REWRITE
        // This is several tears of bad. For every map, of which many are loaded at once, predominately the same
        // graphics are sent over to the GPU again and again. This needs to be managed by the world, but id mappings
        // must remain local to the map. Also, fix this "Maps/" literal crap.
        TileSets = new GameTileSet[map.TileSets.Length];
        for (var i = 0; i < map.TileSets.Length; i++)
        {
            // TODO: Better error handling.
            var tileset = map.TileSets[i];
            var filename = Path.GetFileName(tileset.Source);
            var tilesetName = Path.GetFileNameWithoutExtension(filename);
            var source = new Asset("Maps", filename);
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
        ObjectLayer = new GameMapObjectLayer(this, objectLayers.SelectMany(t => t.Objects ?? []));
        InteractableBlockObjects = ObjectLayer.Objects.OfType<InteractableBlockObject>().ToImmutableArray();
        var roomInteractions = TiledPropertySerializer<RoomInteractions>.Deserialize(map);
        RoomInteractions = roomInteractions.Interactions.ToImmutableArray();

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

        var backgroundTiles = new ReadOnlySpan<TiledTile>(BackgroundLayer.Tiles);
        var behaviorsSpan = new ReadOnlySpan<TileBehavior>(behaviors);
        for (var mapY = 0; mapY < Height; mapY++)
        {
            var rowStartIndex = mapY * Width;
            var tilerow = backgroundTiles.Slice(rowStartIndex, Width);
            var behaviorrow = behaviorsSpan.Slice(rowStartIndex, Width);

            tilerow.CopyTo(RoomMap.Tiles.AsSpan(mapY * Width, Width));
            behaviorrow.CopyTo(RoomMap.Behaviors.AsSpan(mapY * Width, Width));
        }

        _waterTileCount = CountWaterTiles();
        _unmodifiedRoomMap = RoomMap.Clone();

        IsTriforceRoom = InteractableBlockObjects.Any(static t => t.Interaction.Item?.Item == ItemId.TriforcePiece);
    }

    public void InitializeInteractiveGameObjects(RoomArguments arguments)
    {
        foreach (var obj in InteractableBlockObjects)
        {
            obj.Interaction.Initialize(arguments);
        }

        foreach (var obj in RoomInteractions)
        {
            obj.Initialize(arguments);
        }
    }

    public void Reset()
    {
        // I'm not fond of this. We're mixing/confusing ephemeral state with static data.

        // The room map is edited at playtime. Any persisted changes need to be re-applied as the room loads.
        _unmodifiedRoomMap.CopyTo(RoomMap);
    }

    private int CountWaterTiles()
    {
        var waterCount = 0;
        for (var tileY = 0; tileY < Height - 1; tileY++)
        {
            for (var tileX = 0; tileX < Width - 1; tileX++)
            {
                if (!RoomMap.CheckBlockBehavior(tileX, tileY, TileBehavior.Water)) continue;
                waterCount++;
            }
        }

        return waterCount;
    }

    public Cell GetRandomWaterTile()
    {
        if (_waterTileCount == 0) throw new Exception($"No water found for Zora's in map \"{Name}\"");

        var waterCount = 0;
        var randomCell = _world.Game.Random.Next(0, _waterTileCount);
        for (var tileY = 0; tileY < Height - 1; tileY++)
        {
            for (var tileX = 0; tileX < Width - 1; tileX++)
            {
                if (!RoomMap.CheckBlockBehavior(tileX, tileY, TileBehavior.Water)) continue;
                if (waterCount == randomCell) return new Cell((byte)(tileY + World.BaseRows), (byte)tileX);
                waterCount++;
            }
        }

        throw new Exception("Unreachable code.");
    }

    public TileBehavior GetBehavior(TiledTile tile)
    {
        return TileSets[tile.TileSheet].Behaviors[tile.TileId];
    }

    public bool TryGetBlockObjectTiles(BlockType blockObjectType, out GameBlockObject blockObject)
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

    internal static bool TryParseUnderworldDoors(string? s, [MaybeNullWhen(false)] out Dictionary<Direction, DoorType> doors)
    {
        doors = null;

        if (s == null) return false;
        var parser = new StringParser();

        doors = new Dictionary<Direction, DoorType>();
        var directions = TiledRoomProperties.DoorDirectionOrder;
        for (var i = 0; i < directions.Length; i++)
        {
            if (!parser.TryExpectEnum<DoorType>(s, out var doorType)) return false;
            doors[directions[i]] = doorType;

            if (i < directions.Length - 1)
            {
                if (!parser.TryExpectChar(s, ',')) return false;
            }
        }

        return true;
    }

    private bool DoesContainTriforce()
    {
        if (!IsTriforceRoom) return false;

        var triforceState = PersistedRoomState.ObjectState.FirstOrDefault(static t => t.Value.ItemId == ItemId.TriforcePiece);
        if (triforceState.Value == null) return false;

        return !triforceState.Value.ItemGot;
    }

    public override string ToString() => $"{GameWorld.Name}/{UniqueId} ({Name})";
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

[DebuggerDisplay("{Name} ({X}, {Y})")]
internal abstract class GameMapObject
{
    public string Name { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public ImmutableDictionary<string, string> Properties { get; }

    protected GameRoom Room { get; }
    protected TiledLayerObject LayerObject { get; }

    protected GameMapObject(GameRoom room, TiledLayerObject layerObject)
    {
        Room = room;
        LayerObject = layerObject;

        Name = layerObject.Name;
        X = layerObject.X;
        Y = layerObject.Y;
        Width = layerObject.Width;
        Height = layerObject.Height;
        Properties = layerObject.GetPropertyDictionary();
    }
}

internal sealed class RoomTileMap
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

    public RoomTileMap(int width, int height)
    {
        Width = width;
        Height = height;
        var size = width * height;
        Tiles = new TiledTile[size];
        Behaviors = new TileBehavior[size];
    }

    public RoomTileMap Clone()
    {
        var clone = new RoomTileMap(Width, Height);
        CopyTo(clone);
        return clone;
    }

    public void CopyTo(RoomTileMap clone)
    {
        Tiles.CopyTo((Span<TiledTile>)clone.Tiles);
        Behaviors.CopyTo((Span<TileBehavior>)clone.Behaviors);
    }

    public void Blit(TiledTile[,] source, int destX, int destY)
    {
        for (var y = 0; y < source.GetLength(1); y++)
        {
            for (var x = 0; x < source.GetLength(0); x++)
            {
                this[destX + x, destY + y] = source[x, y];
            }
        }
    }

    public void Blit(TiledTile[] source, int srcWidth, int srcHeight, int destX, int destY)
    {
        var srcIndex = 0;
        for (var y = 0; y < srcHeight; y++)
        {
            for (var x = 0; x < srcWidth; x++)
            {
                this[destX + x, destY + y] = source[srcIndex];
                srcIndex++;
            }
        }
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

    public void UpdateTileBehavior(TileBehavior oldBehavior, TileBehavior newBehavior)
    {
        for (var i = 0; i < Behaviors.Length; i++)
        {
            ref var existing = ref Behaviors[i];
            if (existing == oldBehavior) existing = newBehavior;
        }
    }

    public bool CheckBlockBehavior(int tileX, int tileY, TileBehavior behavior)
    {
        return Behavior(tileX, tileY) == behavior
            && Behavior(tileX + 1, tileY) == behavior
            && Behavior(tileX, tileY + 1) == behavior
            && Behavior(tileX + 1, tileY + 1) == behavior;
    }

    public void SetBlockBehavior(Point point, TileBehavior behavior) => SetBlockBehavior(point.X, point.Y, behavior);

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
    public InteractableBlockObject MapObject { get; }
    public string Name { get; }

    public GameMapReference(InteractableBlockObject mapObject, string name)
    {
        MapObject = mapObject;
        Name = name;
    }
}

[DebuggerDisplay("{Name}")]
internal sealed class InteractableBlockObject : GameMapObject
{
    public string Id { get; }

    public InteractableBlock Interaction { get; set; }

    public InteractableBlockObject(GameRoom room, TiledLayerObject layerObject, InteractableBlock interaction) : base(room, layerObject)
    {
        var idProperty = layerObject.GetProperty(TiledRoomProperties.Id);
        Interaction = interaction;
        Id = !string.IsNullOrEmpty(idProperty)
            ? idProperty
            : $"{layerObject.X},{layerObject.Y},{Interaction.Interaction}";
    }
}

[DebuggerDisplay("{Name} ({TileBehavior})")]
internal sealed class TileBehaviorGameMapObject : GameMapObject
{
    public TileBehavior TileBehavior { get; set; }

    public TileBehaviorGameMapObject(GameRoom room, TiledLayerObject layerObject) : base(room, layerObject)
    {
        TileBehavior = layerObject.GetEnumProperty<TileBehavior>(TiledObjectProperties.TileBehavior);
    }
}

internal sealed class GameMapObjectLayer
{
    private readonly GameRoom _room;
    public GameMapObject[] Objects { get; } = [];

    public GameMapObjectLayer(GameRoom room, IEnumerable<TiledLayerObject> objects)
    {
        _room = room;
        var list = new List<GameMapObject>(500);
        foreach (var obj in objects)
        {
            var type = obj.GetEnumProperty<GameObjectLayerObjectType>(TiledObjectProperties.Type);
            switch (type)
            {
                case GameObjectLayerObjectType.TileBehavior:
                    list.Add(new TileBehaviorGameMapObject(room, obj));
                    break;

                case GameObjectLayerObjectType.Interactive:
                default:
                    var interactable = TiledPropertySerializer<InteractableBlock>.Deserialize(obj);
                    // Allow some interactions to be implied.
                    if (interactable.Interaction == Interaction.Unknown)
                    {
                        interactable.Interaction = interactable switch {
                            { Entrance: not null } => Interaction.None,
                            { Raft: not null } => Interaction.None,
                            { SpawnedType: not null } => Interaction.Touch,
                            { Item: not null } => Interaction.Touch,
                            _ => throw new Exception($"Interaction block in room {room} did not have Interaction set, nor could one be assumed.")
                        };
                    }

                    // The items are only root level so that they can be an array.
                    var cavespec = interactable.Entrance?.Shop;
                    if (cavespec != null)
                    {
                        cavespec.Items = interactable.CaveItems;
                    }

                    list.Add(new InteractableBlockObject(room, obj, interactable));
                    break;
            }
        }

        Objects = list.ToArray();
    }
}