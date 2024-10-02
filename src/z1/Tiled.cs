﻿using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.Pkcs;
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
    public string Id => Name; // not sure if these will remain the same.
    public string Name { get; }
    public GameRoom[] Rooms { get; }
    public GameRoom EntryRoom { get; }
    public GameRoom? BossRoom { get; }
    public GameWorldMap GameWorldMap { get; }
    public GameRoom[] TeleportDestinations { get; }
    public WorldInfo Info { get; }
    public string? LevelString { get; }

    public bool IsBossAlive => BossRoom != null && BossRoom.RoomFlags.ObjectCount != 0;

    public GameWorld(Game game, TiledWorld world, string filename, int questId)
    {
        Name = Path.GetFileNameWithoutExtension(filename);
        if (world.Maps == null) throw new Exception($"World {Name} has no maps.");

        var directory = Path.GetDirectoryName(filename);
        Rooms = new GameRoom[world.Maps.Length];
        var worldMaps = new (GameRoom Room, TiledWorldEntry Entry)[world.Maps.Length];
        for (var i = 0; i < world.Maps.Length; ++i)
        {
            var worldEntry = world.Maps![i];
            var asset = new Asset(directory, worldEntry.Filename);
            var tiledmap = asset.ReadJson<TiledMap>();
            var room = new GameRoom(game, this, worldEntry, Path.GetFileName(worldEntry.Filename), tiledmap, questId);
            if (room.RoomInformation.IsEntryRoom) EntryRoom = room;
            if (room.RoomInformation.IsBossRoom) BossRoom = room;
            worldMaps[i] = (room, worldEntry);
            Rooms[i] = room;
        }

        Info = world.GetJsonProperty<WorldInfo>(TiledWorldProperties.WorldInfo);
        if (Info.LevelNumber > 0) LevelString = $"Level-{Info.LevelNumber}";

        EntryRoom ??= Rooms[0];

        TeleportDestinations = Rooms
            .Where(t => t.RecorderDestination != null)
            .OrderBy(t => t.RecorderDestination!.Slot)
            .ToArray();

        var orderedWorldMaps = worldMaps
            .OrderBy(t => t.Entry.Y)
            .ThenBy(t => t.Entry.X)
            .ToArray();

        // This can use a massive optimization.
        for (var i = 0; i < orderedWorldMaps.Length; ++i)
        {
            var (room, entry) = orderedWorldMaps[i];
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

    public static GameWorld Load(Game game, string filename, int questId)
    {
        return new GameWorld(game, new Asset(filename).ReadJson<TiledWorld>(), filename, questId);
    }


    public static GameWorld WorldFromCaveDescription(Game game, string description)
    {
        var questId = game.World.Profile.Quest;
        var enters = description.IReplace("{QuestId}", questId.ToString("D2"));

        if (enters.IStartsWith("Level_"))
        {
            return Load(game, "Maps/" + enters + ".world", questId);
        }
        else if (enters.IStartsWith("Cave_"))
        {
        }

        throw new Exception();
    }

    public void ResetLevelKillCounts()
    {
        foreach (var map in Rooms) map.LevelKillCount = 0;
    }

    public GameRoom GetGameRoom(string id)
    {
        var room = Rooms.FirstOrDefault(t => t.Id == id);
        if (room == null) throw new Exception($"Unable to find room \"{id}\" in world \"{Name}\"");
        return room;
    }
}

[DebuggerDisplay("{World.Id}/{Id} ({Name})")]
internal sealed class GameRoom
{
    public string Id { get; }
    public GameWorld World { get; }
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
    public ImmutableArray<InteractiveGameObject> InteractiveGameObjects { get; set; }
    public bool HasDungeonDoors { get; }
    public Dictionary<Direction, DoorType> DungeonDoors { get; } = [];
    // public SoundEffect? AmbientSound { get; set; }
    public ImmutableArray<MonsterEntry> Monsters { get; set; }
    public int ZoraCount { get; set; }
    public bool MonstersEnter { get; set; }
    public MazeRoom? Maze { get; set; }
    // public Raft? Raft { get; set; }
    public RoomInformation RoomInformation { get; }
    // public bool IsDark { get; }
    // public bool PlaysSecretChime { get; }
    // public bool IsBossRoom { get; }
    // public bool IsEntryRoom { get; set; }
    public RecorderDestination? RecorderDestination { get; }

    public RoomTileMap RoomMap { get; }
    public bool HidePlayerMapCursor { get; set; }
    public bool HiddenFromMap { get; set; }
    public bool IsTriforceRoom { get; set; }
    public bool HasTriforce => IsTriforceRoom && !RoomFlags.ItemState;

    public Dictionary<Direction, GameRoom> Connections { get; } = [];
    public RoomFlags RoomFlags => GetRoomFlags();

    public int LevelKillCount { get; set; }

    // Old zelda stuff that needs to be refactored in action objects and stuff.
    public World.Secret Secret { get; }
    public ItemId ItemId { get; } = ItemId.None;
    public PointXY ItemPosition { get; }
    public int? FireballLayout { get; }
    public PointXY ExitPosition { get; }
    public string? CellarStairsLeftRoomId { get; set; }
    public string? CellarStairsRightRoomId { get; set; }
    // public CaveId? CaveId { get; set; }
    // public ItemId? RoomItemId { get; set; }
    public bool IsLadderAllowed { get; set; }

    private readonly Game _game;
    // private readonly GameMapObject[] _ownedObjects;
    private readonly int _waterTileCount;

    public GameRoom(Game game, GameWorld world, TiledWorldEntry worldEntry, string name, TiledMap map, int questId)
    {
        if (map.Layers == null) throw new Exception();
        if (map.TileSets == null) throw new Exception();

        _game = game;
        World = world;

        WorldEntry = worldEntry;
        Name = name;
        Width = map.Width;
        Height = map.Height;
        TileWidth = map.TileWidth;
        TileHeight = map.TileHeight;
        BlockWidth = map.TileWidth * 2; // This assumption will break at some point.
        BlockHeight = map.TileHeight * 2; // This assumption will break at some point.

        RoomMap = new RoomTileMap(Width, Height);

        Id = map.GetProperty(TiledObjectProperties.Id) ?? throw new Exception("Room has no room id.");
        // AmbientSound = map.GetNullableEnumProperty<SoundEffect>(TiledObjectProperties.AmbientSound);
        Monsters = MonsterEntry.ParseMonsters(map.GetProperty(TiledObjectProperties.Monsters), out var zoraCount);
        ZoraCount = zoraCount;
        MonstersEnter = map.GetBooleanProperty(TiledObjectProperties.MonstersEnter);
        Maze = map.GetClass<MazeRoom>(TiledObjectProperties.Maze);
        // Raft = map.GetClass<Raft>(TiledObjectProperties.Raft);
        RoomInformation = map.ExpectClass<RoomInformation>(TiledObjectProperties.RoomInformation);
        RecorderDestination = map.GetClass<RecorderDestination>(TiledObjectProperties.RecorderDestination);
        // IsDark = map.GetBooleanProperty(TiledObjectProperties.IsDark);
        // PlaysSecretChime = map.GetBooleanProperty(TiledObjectProperties.PlaysSecretChime);
        // IsBossRoom = map.GetBooleanProperty(TiledObjectProperties.IsBossRoom);
        // IsEntryRoom = map.GetBooleanProperty(TiledObjectProperties.IsEntryRoom);
        HiddenFromMap = map.GetBooleanProperty(TiledObjectProperties.HiddenFromMap);

        var dungeonDoors = map.GetProperty(TiledObjectProperties.DungeonDoors);
        if (TryParseDungeonDoors(dungeonDoors, out var doors))
        {
            HasDungeonDoors = true;
            DungeonDoors = doors;
        }

        Secret = map.GetEnumProperty(TiledObjectProperties.Secret, z1.World.Secret.None);
        ItemId = map.GetEnumProperty(TiledObjectProperties.ItemId, ItemId.None);
        ItemPosition = map.GetPoint(TiledObjectProperties.ItemPosition);
        FireballLayout = map.GetIntPropertyOrNull(TiledObjectProperties.FireballLayout);
        // ExitPosition = map.GetPoint(TiledObjectProperties.ExitPosition);
        CellarStairsLeftRoomId = map.GetProperty(TiledObjectProperties.CellarStairsLeft);
        CellarStairsRightRoomId = map.GetProperty(TiledObjectProperties.CellarStairsRight);
        // CaveId = map.GetNullableEnumProperty<CaveId>(TiledObjectProperties.CaveId);
        // RoomItemId = map.GetEnumProperty(TiledObjectProperties.RoomItemId, ItemId.None);
        // IsLadderAllowed = map.GetBooleanProperty(TiledObjectProperties.IsLadderAllowed);

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
            var source = new Asset("Maps/" + filename);
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
        InteractiveGameObjects = ObjectLayer.Objects.OfType<InteractiveGameObject>().ToImmutableArray();

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

        IsTriforceRoom = InteractiveGameObjects.Any(t => t.Interaction.Item?.Item == ItemId.TriforcePiece);
    }

    public RoomFlags GetRoomFlags()
    {
        return _game.World.Profile.GetRoomFlags(_game.World.CurrentWorld, this);
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
        var randomCell = _game.Random.Next(0, _waterTileCount);
        for (var tileY = 0; tileY < Height - 1; tileY++)
        {
            for (var tileX = 0; tileX < Width - 1; tileX++)
            {
                if (!RoomMap.CheckBlockBehavior(tileX, tileY, TileBehavior.Water)) continue;
                if (waterCount == randomCell) return new Cell((byte)(tileY + z1.World.BaseRows), (byte)tileX);
                waterCount++;
            }
        }

        throw new Exception("Unreachable code.");
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

    public IEnumerable<InteractiveGameObject> GetInteractiveGameObjects(Interaction interaction, int x, int y, int distance)
    {
        var minX = x - distance;
        var maxX = x + distance;
        var minY = y - distance;
        var maxY = y + distance;

        foreach (var obj in InteractiveGameObjects)
        {
            if (obj.Interaction.Interaction != interaction) continue;

            if (obj.X >= minX && obj.X <= maxX && obj.Y >= minY && obj.Y <= maxY)
            {
                yield return obj;
            }
        }
    }

    // private IEnumerable<TiledTile> GetInclusiveTiles(GameMapTileLayer layer, GameObjectLayerObject obj)
    // {
    //     var startX = obj.X / TileWidth;
    //     var startY = obj.Y / TileHeight;
    //     var endX = (obj.X + obj.Width - 1) / TileWidth;
    //     var endY = (obj.Y + obj.Height - 1) / TileHeight;
    //
    //     var tiles = layer.Tiles;
    //     for (var y = startY; y <= endY; y++)
    //     {
    //         var tilerow = y * Width;
    //         for (var x = startX; x <= endX; x++)
    //         {
    //             yield return tiles[tilerow + x];
    //         }
    //     }
    // }
    //
    // private IEnumerable<Point> GetInclusiveTileCoordinates(GameObjectLayerObject obj)
    // {
    //     var startX = obj.X / TileWidth;
    //     var startY = obj.Y / TileHeight;
    //     var endX = (obj.X + obj.Width - 1) / TileWidth;
    //     var endY = (obj.Y + obj.Height - 1) / TileHeight;
    //
    //     for (var y = startY; y <= endY; y++)
    //     {
    //         for (var x = startX; x <= endX; x++)
    //         {
    //             yield return new Point(x, y);
    //         }
    //     }
    // }
    //
    // public IEnumerable<GameMapObject> GetObjects(int tileX, int tileY)
    // {
    //     foreach (var obj in ObjectLayer.Objects)
    //     {
    //         obj.GetScreenTileCoordinates(out var objectTileX, out var objectTileY);
    //
    //         if (tileX < objectTileX || tileX >= objectTileX + obj.Width / TileWidth) continue;
    //         if (tileY < objectTileY || tileY >= objectTileY + obj.Height / TileHeight) continue;
    //         yield return obj;
    //     }
    // }
    //
    // public bool TryGetActionObject(TileAction action, out InteractiveGameObject actionObject)
    // {
    //     actionObject = default;
    //     foreach (var obj in ActionMapObjects)
    //     {
    //         if (obj.Action != action) continue;
    //         actionObject = obj;
    //         return true;
    //     }
    //
    //     return false;
    // }
    //
    // public bool TryGetActionObject(TileAction action, int tileX, int tileY, out InteractiveGameObject actionObject)
    // {
    //     actionObject = default;
    //     foreach (var obj in GetObjects(tileX, tileY))
    //     {
    //         if (obj is not InteractiveGameObject currentObject) continue;
    //         if (currentObject.Action != action) continue;
    //         actionObject = currentObject;
    //         return true;
    //     }
    //
    //     return false;
    // }
    //
    // public InteractiveGameObject GetActionObject(TileAction action, int tileX, int tileY)
    // {
    //     if (TryGetActionObject(action, tileX, tileY, out var actionObject)) return actionObject;
    //
    //     throw new Exception($"No object of type {action} found at {tileX}, {tileY}");
    // }

    internal static bool TryParseDungeonDoors(string? s, [MaybeNullWhen(false)] out Dictionary<Direction, DoorType> doors)
    {
        doors = null;

        if (s == null) return false;
        var parser = new StringParser();

        doors = new Dictionary<Direction, DoorType>();
        var directions = TiledObjectProperties.DoorDirectionOrder;
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

    public override string ToString() => $"{World.Name}/{Id} ({Name})";
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

[DebuggerDisplay("{Name} ({X}, {Y})")]
internal abstract class GameMapObject
{
    public string Name { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public ImmutableDictionary<string, string> Properties { get; }

    protected readonly TiledLayerObject LayerObject;

    private readonly GameRoom _room;

    protected GameMapObject(GameRoom room, TiledLayerObject layerObject)
    {
        _room = room;
        LayerObject = layerObject;

        Name = layerObject.Name;
        X = layerObject.X;
        Y = layerObject.Y;
        Width = layerObject.Width;
        Height = layerObject.Height;
        Properties = layerObject.GetPropertyDictionary();
    }

    public void GetScreenTileCoordinates(out int tileX, out int tileY)
    {
        var objtilex = X / _room.TileWidth;
        var objtiley = Y / _room.TileHeight;
        tileX = objtilex;
        tileY = objtiley;
    }

    public void GetTileSize(out int tileWidth, out int tileHeight)
    {
        tileWidth = Width / (_room.TileWidth);
        tileHeight = Height / (_room.TileHeight );
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
    public InteractiveGameObject MapObject { get; }
    public string Name { get; }

    public GameMapReference(InteractiveGameObject mapObject, string name)
    {
        MapObject = mapObject;
        Name = name;
    }
}

[DebuggerDisplay("{Name}")]
internal sealed class InteractiveGameObject : GameMapObject
{
    public string Id { get; }
    private readonly GameRoom _room;

    public InteractableBlock Interaction { get; set; }

    public InteractiveGameObject(GameRoom room, TiledLayerObject layerObject, InteractableBlock interaction) : base(room, layerObject)
    {
        _room = room;
        var idProperty = layerObject.GetProperty(TiledObjectProperties.Id);
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
                    var cavespec = interactable.Entrance?.Cave;
                    if (cavespec != null) cavespec.Items = interactable.CaveItems;

                    list.Add(new InteractiveGameObject(room, obj, interactable));
                    break;
            }
        }

        Objects = list.ToArray();
    }
}