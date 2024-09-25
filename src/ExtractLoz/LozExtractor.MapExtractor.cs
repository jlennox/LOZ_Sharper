using System.Drawing;
using System.Text.Json;
using static ExtractLoz.MapExtractor;
#pragma warning disable CA1416

namespace ExtractLoz;

internal record RoomContext(
    int ColCount, int RowCount, int StartRow, int StartCol, int TileTypeCount,
    int MarginRight, int MarginLeft, int MarginBottom, int MarginTop)
{
    public static readonly RoomContext OpenRoomContext = new(
        32, 22, 0, 0, 56,
        World.OWMarginRight, World.OWMarginLeft, World.OWMarginBottom, World.OWMarginTop);

    public static readonly RoomContext ClosedRoomContext = new(
        24, 14, 4, 4, 9,
        World.UWMarginRight, World.UWMarginLeft, World.UWMarginBottom, World.UWMarginTop);
}

internal record MapResources(
    RoomContext RoomContext, RoomCols[] RoomCols, TableResource<byte> ColTable, byte[] TileAttrs,
    TableResource<byte> ObjectList, byte[] PrimaryMobs, byte[]? SecondaryMobs, byte[] TileBehaviors,
    RoomAttr[] RoomAttrs, TableResource<byte> SparseTable, LevelInfoBlock LevelInfoBlock, MapResources? CellarResources)
{
    // public readonly byte[][] RoomColsArray = GetRoomColsArray(RoomCols);

    public byte[][] GetRoomColsArray() => RoomCols.Select(t => t.Get()).ToArray();

    public RoomAttr GetRoomAttr(Point point) => RoomAttrs[point.Y * World.WorldWidth + point.X];

    public bool IsCellarRoom(bool isOverworld, RoomId roomId) => !isOverworld && RoomAttrs[roomId.Id].GetUniqueRoomId() >= 0x3E;
    public bool IsCellarRoom2(bool isOverworld, RoomId roomId) => !isOverworld && RoomAttrs[roomId.Id].GetUniqueRoomId() >= 0x3E;
}

public partial class LozExtractor
{
    private readonly record struct LevelGroupMap(int QuestId, int LevelNumber);

    private static readonly Dictionary<LevelGroupMap, int> _levelGroupMap = new()
    {
        { new(0, 1), 0 },
        { new(0, 2), 0 },
        { new(0, 3), 0 },
        { new(0, 4), 0 },
        { new(0, 5), 0 },
        { new(0, 6), 0 },
        { new(0, 7), 1 },
        { new(0, 8), 1 },
        { new(0, 9), 1 },
        { new(1, 1), 2 },
        { new(1, 2), 2 },
        { new(1, 3), 2 },
        { new(1, 4), 2 },
        { new(1, 5), 2 },
        { new(1, 6), 2 },
        { new(1, 7), 3 },
        { new(1, 8), 3 },
        { new(1, 9), 3 },
    };

    private static void ExtractTiledMaps(Options options)
    {
        var overworldResources = ExtractOverworldTiledMaps(options);
        var underworldResources = ExtractUnderworldTiledMaps(options);

        MakeTiledTileSets(options, overworldResources, underworldResources);
    }

    private static MapResources ExtractOverworldTiledMaps(Options options)
    {
        using var reader = options.GetBinaryReader();
        var tileAttributes = ExtractOverworldTileAttrs(options);
        var tileBehaviors = ExtractOverworldTileBehaviors(options, options.GetBinaryReader()).ToArray();
        var roomAttributes = ExtractOverworldMapAttrs(options);
        var worldInfo = ExtractOverworldMap(options);
        var objectList = ExtractObjLists(options);
        var sparseTable = ExtractOverworldMapSparseAttrs(options);
        var (primaries, secondaries) = ExtractOverworldMobs(options, reader);
        var levelInfo = ExtractOverworldInfo(options);

        var resources = new MapResources(
            RoomContext: RoomContext.OpenRoomContext,
            RoomCols: worldInfo.RoomCols,
            ColTable: worldInfo.Table,
            TileAttrs: tileAttributes,
            ObjectList: objectList,
            PrimaryMobs: primaries,
            SecondaryMobs: secondaries,
            TileBehaviors: tileBehaviors,
            RoomAttrs: roomAttributes,
            SparseTable: sparseTable,
            LevelInfoBlock: levelInfo,
            CellarResources: null);

        ExtractTiledMap(options, resources, "Overworld", true);

        return resources;
    }

    private static MapResources ExtractUnderworldTiledMaps(Options options)
    {
        using var reader = options.GetBinaryReader();
        var tileAttributes = ExtractUnderworldTileAttrs(options);
        var tileBehaviors = ExtractUnderworldTileBehaviors(options, reader).ToArray();
        var roomAttributes = ExtractUnderworldMapAttrs(options).ToDictionary(t => t.LevelGroup);
        var worldInfo = ExtractUnderworldMap(options);
        var objectList = ExtractObjLists(options);
        var sparseTable = ExtractOverworldMapSparseAttrs(options); // there is no underworld specific one.
        var mapObjects = ExtractUnderworldMobs(options, reader);
        var levelInfo = ExtractUnderworldInfo(options);

        var resources = new MapResources(
            RoomContext: RoomContext.ClosedRoomContext,
            RoomCols: worldInfo.RoomCols,
            ColTable: worldInfo.Table,
            TileAttrs: tileAttributes,
            ObjectList: objectList,
            PrimaryMobs: mapObjects.Backing,
            SecondaryMobs: null,
            TileBehaviors: tileBehaviors,
            RoomAttrs: roomAttributes[0].RoomAttributes,
            SparseTable: sparseTable,
            LevelInfoBlock: levelInfo.First().Value,
            CellarResources: null);

        var cellarTileAttributes = ExtractUnderworldCellarTileAttrs(options);
        var (cellarPrimaries, cellarSecondaries) = ExtractUnderworldCellarMobs(options, reader);
        var cellarWorldInfo = ExtractUnderworldCellarMap(options);
        // ExtractUnderworldCellarTiles(options);

        resources = resources with
        {
            CellarResources = resources with
            {
                RoomContext = RoomContext.OpenRoomContext,
                RoomCols = cellarWorldInfo.RoomCols,
                ColTable = cellarWorldInfo.Table,
                TileAttrs = cellarTileAttributes,
                PrimaryMobs = cellarPrimaries,
                SecondaryMobs = cellarSecondaries,
            }
        };

        for (var questId = 0; questId < 2; questId++)
        {
            for (var i = 1; i <= 9; i++)
            {
                var level = new LevelGroupMap(questId, i);
                var levelGroup = _levelGroupMap[level];
                resources = resources with {
                    RoomAttrs = roomAttributes[levelGroup].RoomAttributes,
                    LevelInfoBlock = levelInfo[level],
                    CellarResources = resources.CellarResources with
                    {
                        RoomAttrs = roomAttributes[levelGroup].RoomAttributes,
                        LevelInfoBlock = levelInfo[level],
                    }
                };
                ExtractTiledMap(options, resources, $"Level{questId:D2}_{i:D2}", false);
            }
        }

        return resources;
    }

    private static void ExtractSpecialMaps(Options options, MapResources underworldResources)
    {
    }

    private static readonly TiledTile[] _emptyRoomTiles = Enumerable.Range(0, World.ScreenColumns * World.ScreenRows).Select(t => TiledTile.Empty).ToArray();
    private static readonly TiledTile[] _emptyScreenRow = Enumerable.Range(0, World.ScreenColumns).Select(t => TiledTile.Empty).ToArray();

    private static void ExtractTiledMap(Options options, MapResources resources, string name, bool isOverworld)
    {
        var extractor = new MapExtractor(resources);

        var questObjects = Enumerable.Range(0, 3).Select(_ => new List<TiledLayerObject>()).ToArray();
        var cellarRoomObjects = new List<TiledLayerObject>();
        var allExtractedScreens = new List<TiledTile[]>();
        var cellarExtractedScreens = new List<TiledTile[]>();
        static string GetScreenName(int x, int y) => $"Screen {x},{y}";
        static string GetScreenNameP(Point p) => GetScreenName(p.X, p.Y);

        var startRoomId = resources.LevelInfoBlock.StartRoomId;
        var startingRoom = Extensions.PointFromRoomId(startRoomId);

        var cellarRooms = new List<RoomId>();

        // Walk the dungeon to isolate it from their grouped together maps.
        var visitedRooms = new HashSet<RoomId>();
        var hasVisitedRooms = false;
        if (!isOverworld)
        {
            hasVisitedRooms = true;

            var checks = new (Direction Direction, Point Point)[] {
                (Direction.Up, new Point(0, -1)),
                (Direction.Left, new Point(-1, 0)),
                (Direction.Down, new Point(0, 1)),
                (Direction.Right, new Point(1, 0)),
            };
            var nextRooms = new Stack<Point>([startingRoom]);

            while (nextRooms.Count > 0)
            {
                var currentRoom = nextRooms.Pop();
                var uwRoomAttrs = new UWRoomAttr(resources.GetRoomAttr(currentRoom));
                visitedRooms.Add(currentRoom);
                foreach (var (dir, offset) in checks)
                {
                    if (resources.LevelInfoBlock.FindCellarRoomIds(currentRoom.GetRoomId(), resources.RoomAttrs, out var left, out var right))
                    {
                        var leftp = Extensions.PointFromRoomId(left);
                        var rightp = Extensions.PointFromRoomId(right);

                        if (!visitedRooms.Contains(leftp)) nextRooms.Push(leftp);
                        if (!visitedRooms.Contains(rightp)) nextRooms.Push(rightp);
                    }

                    var door = uwRoomAttrs.GetDoor(dir);
                    if (door == DoorType.None) continue;
                    if (dir == Direction.Down && currentRoom.Y == World.WorldHeight - 1) continue; // entry rooms are always the bottom row, and we can't go lower.
                    var nextRoom = new Point(currentRoom.X + offset.X, currentRoom.Y + offset.Y);
                    if (visitedRooms.Contains(nextRoom)) continue;
                    nextRooms.Push(nextRoom);
                }
            }

            // These are for cropping... which I don't believe is worth while?
            // var leftMostRoom = visitedRooms.Min(t => t.X);
            // var rightMostRoom = visitedRooms.Max(t => t.X);
            // var topMostRoom = visitedRooms.Min(t => t.Y);
            // var bottomMostRoom = visitedRooms.Max(t => t.Y);
        }

        static TiledLayerObject TransformAction(ActionableTiles action, int screenX, int screenY)
        {
            var basex = screenX * World.ScreenColumns * World.TileWidth;
            var basey = screenY * World.ScreenRows * World.TileHeight;

            return new TiledLayerObject
            {
                Id = screenY * World.WorldWidth + screenX,
                X = basex + action.X * World.TileWidth,
                Y = basey + action.Y * World.TileHeight,
                Width = action.Width * World.BlockWidth,
                Height = action.Height * World.BlockHeight,
                Name = $"{action.Action}",
                Visible = true,
                Properties = [
                    new TiledProperty(TiledObjectProperties.Type, GameObjectLayerObjectType.Action),
                    new TiledProperty(TiledObjectProperties.TileAction, action.Action),
                    new TiledProperty(TiledObjectProperties.Owner, GetScreenName(screenX, screenY)),
                    .. (action.Properties ?? [])
                ],
            };
        }

        // Draw each map into allExtractedScreens, and extract all the Actions.
        var currentRoomId = 0;
        for (var screenY = 0; screenY < World.WorldHeight; ++screenY)
        {
            for (var screenX = 0; screenX < World.WorldWidth; screenX++, currentRoomId++)
            {
                var screenPoint = new RoomId(screenX, screenY);
                var isUWCellar = resources.IsCellarRoom(isOverworld, currentRoomId);

                var map = extractor.LoadLayout(currentRoomId, isOverworld, out var actions);
                var tiles = extractor.DrawMap(map, isOverworld, currentRoomId, 0, 0);
                if (isUWCellar)
                {
                    // If it's a cellar, store it in its own list and add an empty room to the main list.
                    cellarRooms.Add(screenPoint);
                    cellarExtractedScreens.Add(tiles);
                    allExtractedScreens.Add(_emptyRoomTiles);
                }
                else
                {
                    // If this is a dungeon, we only want the rooms that are specific to this one dungeon.
                    if (hasVisitedRooms && !visitedRooms.Contains(screenPoint))
                    {
                        allExtractedScreens.Add(_emptyRoomTiles);
                        continue;
                    }
                    allExtractedScreens.Add(tiles);
                }

                foreach (var action in actions)
                {
                    if (isUWCellar)
                    {
                        cellarRoomObjects.Add(TransformAction(action, cellarRoomObjects.Count, 0));
                    }
                    else
                    {
                        questObjects[action.QuestId].Add(TransformAction(action, screenX, screenY));
                    }
                }
            }
        }

        // Build out the list of room screens.
        currentRoomId = 0;
        var cellarRoomId = 0;
        for (var y = 0; y < World.WorldHeight; y++)
        {
            var basey = y * World.ScreenRows * 8;

            for (var x = 0; x < World.WorldWidth; x++, currentRoomId++)
            {
                var basex = x * World.ScreenColumns * 8;
                var screenProperties = new List<TiledProperty>();
                var maze = resources.SparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, currentRoomId);
                var roomAttr = resources.RoomAttrs[currentRoomId];
                var owRoomAttrs = new OWRoomAttr(roomAttr);
                var uwRoomAttrs = new UWRoomAttr(roomAttr);

                var screenPoint = new Point(x, y);
                var isUWCellar = resources.IsCellarRoom(isOverworld, currentRoomId);
                // if (!isUWCellar && hasVisitedRooms && !visitedRooms.Contains(screenPoint))
                // {
                //     continue;
                // }

                isUWCellar = false;

                screenProperties.Add(new TiledProperty("uroomid", roomAttr.GetUniqueRoomId()));

                if (currentRoomId == startRoomId)
                {
                    screenProperties.Add(new TiledProperty(TiledObjectProperties.IsEntryRoom, true));
                }

                if (maze != null)
                {
                    screenProperties.Add(new TiledProperty(TiledObjectProperties.Maze,
                        string.Join(", ", maze.Value.Paths.ToArray().Select(t => t.ToString()))));
                    screenProperties.Add(new TiledProperty(TiledObjectProperties.MazeExit, maze.Value.ExitDirection));
                }

                if (isOverworld)
                {
                    if (owRoomAttrs.DoMonstersEnter())
                    {
                        screenProperties.Add(new TiledProperty(TiledObjectProperties.MonstersEnter, true));
                    }

                    if (owRoomAttrs.HasAmbientSound())
                    {
                        screenProperties.Add(new TiledProperty(TiledObjectProperties.AmbientSound, SoundEffect.Sea));
                    }
                }
                else
                {
                    if (resources.LevelInfoBlock.FindCellarRoomIds(currentRoomId, resources.RoomAttrs, out var left, out var right))
                    {
                        var leftp = Extensions.PointFromRoomId(left);
                        var rightp = Extensions.PointFromRoomId(right);

                        screenProperties.Add(TiledProperty.CreateArgument(TiledObjectArguments.CellarStairsLeft, GetScreenNameP(leftp)));
                        screenProperties.Add(TiledProperty.CreateArgument(TiledObjectArguments.CellarStairsRight, GetScreenNameP(rightp)));
                    }
                }

                // JOE: TODO: Need to handle a lot of other cases here.
                // Build out monster list.
                var monsterCount = roomAttr.GetMonsterCount();
                if (monsterCount > 0)
                {
                    var objId = (ObjType)roomAttr.MonsterListId;
                    var isList = objId >= ObjType.Rock;
                    var monsterList = new List<(int Count, ObjType Type)>();

                    if (isOverworld && owRoomAttrs.HasZora())
                    {
                        monsterList.Add((1, ObjType.Zora));
                    }

                    if (isList)
                    {
                        var listId = objId - ObjType.Rock;
                        var list = resources.ObjectList.GetItem(listId);
                        var monsters = new ObjType[monsterCount];
                        for (var i = 0; i < monsterCount; i++) monsters[i] = (ObjType)list[i];
                        monsterList.AddRange(monsters.GroupBy(t => t).Select(t => (t.Count(), t.Key)));
                    }
                    else
                    {
                        monsterList.Add((monsterCount, objId));
                    }

                    var monsterString = string.Join(", ", monsterList
                        .Where(t => t.Type != ObjType.None)
                        .Select(t => t.Count == 1 ? t.Type.ToString() : $"{t.Type}*{t.Count}"));

                    if (!string.IsNullOrWhiteSpace(monsterString))
                    {
                        screenProperties.Add(new TiledProperty(TiledObjectProperties.Monsters, monsterString));
                    }
                }

                if (!isOverworld)
                {
                    var doors = Enumerable.Range(0, 4).Select(uwRoomAttrs.GetDoor).Select(t => t.ToString()).ToArray();
                    var ambientSound = uwRoomAttrs.GetAmbientSound();
                    screenProperties.Add(new TiledProperty(TiledObjectProperties.DungeonDoors, string.Join(", ", doors)));
                    if (ambientSound != 0)
                    {
                        var soundId = SoundEffect.BossRoar1 + ambientSound - 1;
                        screenProperties.Add(new TiledProperty(TiledObjectProperties.AmbientSound, soundId));
                    }
                    if (uwRoomAttrs.IsDark())
                    {
                        screenProperties.Add(new TiledProperty(TiledObjectProperties.IsDark, true));
                    }
                }


                (isUWCellar ? cellarRoomObjects : questObjects[0]).Add(new TiledLayerObject
                {
                    Id = y * World.WorldWidth + x,
                    X = basex,
                    Y = basey,
                    Width = World.ScreenColumns * 8,
                    Height = World.ScreenRows * 8,
                    Name = GetScreenName(x, y),
                    Visible = true,
                    Properties = [
                        new TiledProperty(TiledObjectProperties.Type, GameObjectLayerObjectType.Screen),
                        new TiledProperty(TiledObjectProperties.InnerPalette, roomAttr.GetInnerPalette()),
                        new TiledProperty(TiledObjectProperties.OuterPalette, roomAttr.GetOuterPalette()),
                        new TiledProperty("Is cellar", resources.IsCellarRoom2(isOverworld, currentRoomId)),
                        .. screenProperties
                    ],
                });
            }
        }

        if (isOverworld)
        {
            // Walk through wall in upper right of overworld map.
            questObjects[0].Add(new TiledLayerObject
            {
                X = 15 * World.ScreenColumns * 8 + 0x80,
                Y = 1 * World.ScreenRows * 8,
                Width = 16,
                Height = 16 * 2,
                Name = "TileBehavior",
                Visible = true,
                Properties = [
                    new TiledProperty(TiledObjectProperties.Type, GameObjectLayerObjectType.TileBehavior),
                    new TiledProperty(TiledObjectProperties.TileBehavior, TileBehavior.GenericWalkable),
                ],
            });
        }

        // Build out an array that's the tiles from each screen, but are in the order they appear in the map.
        // IE, row 0 from screen 0 comes first, row 0 from screen 1 comes next, etc.
        var tilesInMapOrder = new List<TiledTile>();
        var tilesInMapOrderCellar = new List<TiledTile>();
        for (var screenY = 0; screenY < World.WorldHeight; screenY++)
        {
            for (var y = 0; y < World.ScreenRows; y++)
            {
                for (var screenX = 0; screenX < World.WorldWidth; screenX++)
                {
                    var roomId = screenY * World.WorldWidth + screenX;
                    var screenPoint = new Point(screenX, screenY);
                    var isCellar = cellarRooms.Contains(screenPoint);

                    var tiles = allExtractedScreens[roomId];
                    var tileRow = new ReadOnlySpan<TiledTile>(tiles,
                        y * World.ScreenColumns,
                        World.ScreenColumns);

                    if (isCellar)
                    {
                        tilesInMapOrderCellar.AddRange(tileRow);
                        tilesInMapOrder.AddRange(_emptyScreenRow);
                        continue;
                    }

                    if (hasVisitedRooms && !visitedRooms.Contains(new Point(screenX, screenY)))
                    {
                        tilesInMapOrder.AddRange(_emptyScreenRow);
                        continue;
                    }

                    tilesInMapOrder.AddRange(tileRow);
                }
            }
        }

        var backgroundLayer = new TiledLayer(
            World.ScreenColumns * World.WorldWidth,
            World.ScreenRows * World.WorldHeight,
            tilesInMapOrder.ToArray())
        {
            Name = "World",
            Type = TiledLayerType.TileLayer,
            Visible = true,
            Opacity = 1.0f,
        };

        static TiledLayer TransformObjectsIntoLayer(IEnumerable<TiledLayerObject> objects, int i)
        {
            var properties = new List<TiledProperty>();
            var title = "Object";
            if (i > 0)
            {
                properties.Add(new TiledProperty(TiledLayerProperties.QuestId, i));
                title = $"Object (Quest {i})";
            }

            return new TiledLayer(World.ScreenColumns * World.WorldWidth, World.ScreenRows * World.WorldHeight)
            {
                Name = title,
                Type = TiledLayerType.ObjectGroup,
                Visible = true,
                Opacity = 1.0f,
                Objects = objects.ToArray(),
                Properties = properties.Count == 0 ? null : properties.ToArray(),
            };
        };

        var objectLayers = questObjects
            .Where(t => t.Count > 0)
            .Select(TransformObjectsIntoLayer)
            .ToArray();

        var tilesets = new[] {
            new TiledTileSetReference {
                FirstGid = TiledTile.CreateFirstGid(0),
                Source = "overworldTiles.tsj",
            },
            new TiledTileSetReference {
                FirstGid = TiledTile.CreateFirstGid(1),
                Source = "underworldTiles.tsj",
            },
        };

        var tiledmap = new TiledMap
        {
            Width = backgroundLayer.Width,
            Height = backgroundLayer.Height,
            TileWidth = 8,
            TileHeight = 8,
            Layers = [backgroundLayer, .. objectLayers],
            TileSets = tilesets
        };

        options.AddJson($"{name}Map.json", tiledmap, _jsonSerializerOptions);

        if (cellarRooms.Count > 0)
        {
            var cellarBackgroundLayer = new TiledLayer(
                World.ScreenColumns * cellarRooms.Count,
                World.ScreenRows * 1,
                tilesInMapOrderCellar.ToArray())
            {
                Name = "World",
                Type = TiledLayerType.TileLayer,
                Visible = true,
                Opacity = 1.0f,
            };

            var cellarObjectLayer = TransformObjectsIntoLayer(cellarRoomObjects, 0);

            var cellarTiledMap = new TiledMap
            {
                Width = cellarBackgroundLayer.Width,
                Height = cellarBackgroundLayer.Height,
                TileWidth = 8,
                TileHeight = 8,
                Layers = [cellarBackgroundLayer, cellarObjectLayer],
                TileSets = tilesets
            };

            options.AddJson($"CellarMap.json", cellarTiledMap, _jsonSerializerOptions);
        }
    }

    private static void MakeTiledTileSets(Options options, MapResources owResources, MapResources uwResources)
    {
        var offsets = new Dictionary<TiledTileSetTile, List<Point>>();

        void AddTileMap(
            string imageFilename,
            ReadOnlySpan<byte> tileBehavior,
            ReadOnlySpan<BlockObjType> objectTypes,
            ReadOnlySpan<BlockObjType> tiles)
        {
            var tileSet = new TiledTileSet(imageFilename, options.Files[imageFilename], World.TileWidth, World.TileHeight)
            {
                Tiles = new TiledTileSetTile[tileBehavior.Length]
            };

            for (var i = 0; i < tileBehavior.Length; ++i)
            {
                var behavior = (TileBehavior)tileBehavior[i];
                tileSet.Tiles[i] = new TiledTileSetTile
                {
                    Id = i,
                    Properties = behavior == TiledTileSetTileProperties.DefaultTileBehavior
                    ? []
                    : [new TiledProperty(TiledTileSetTileProperties.Behavior, behavior.ToString())]
                };
            }

            void AddProps(TiledTileSetTile tile, BlockObjType obj, int x, int y)
            {
                var newprops = new List<TiledProperty>();
                if (!offsets.TryGetValue(tile, out var points))
                {
                    points = new List<Point>();
                    offsets[tile] = points;
                }

                points.Add(new Point(x, y));

                if (string.IsNullOrEmpty(tile.GetProperty(TiledTileSetTileProperties.Object))) newprops.Add(new TiledProperty(TiledTileSetTileProperties.Object, obj.ToString()));
                tile.Properties = [
                    .. newprops,
                    .. (tile.Properties ?? [])
                ];
            }

            void SetTile(int a, int b, int c, int d, BlockObjType obj)
            {
                AddProps(tileSet.Tiles[a], obj, 0, 0);
                AddProps(tileSet.Tiles[b], obj, 1, 0);
                AddProps(tileSet.Tiles[c], obj, 0, 1);
                AddProps(tileSet.Tiles[d], obj, 1, 1);
            }

            if (imageFilename.Contains("overworld", StringComparison.InvariantCultureIgnoreCase))
            {
                var primaries = owResources.PrimaryMobs;
                var secondaries = owResources.SecondaryMobs;
                foreach (var type in objectTypes)
                {
                    var mobIndex = (int)type;
                    var primary = primaries[mobIndex];

                    if (primary == 0xFF)
                    {
                        var index = mobIndex * 4;
                        SetTile(secondaries[index + 0], secondaries[index + 2], secondaries[index + 1], secondaries[index + 3], type);
                    }
                    else
                    {
                        SetTile(primary, primary + 2, primary + 1, primary + 3, type);
                    }
                }
            }
            else
            {
                var underworldMapObjects = uwResources.PrimaryMobs;
                foreach (var type in objectTypes)
                {
                    var mobIndex = (int)type;
                    var primary = underworldMapObjects[mobIndex];

                    if (primary is < 0x70 or > 0xF2)
                    {
                        var tile = tileSet.Tiles[primary];
                        tile.Properties = [
                            new TiledProperty(TiledTileSetTileProperties.Object, type),
                            .. (tile.Properties ?? [])
                        ];
                    }
                    else
                    {
                        SetTile(primary, primary + 2, primary + 1, primary + 3, type);
                    }
                }
            }

            foreach (var tile in tiles)
            {
                ReadOnlySpan<byte> offsetsX = [0, 0, 1, 1];
                ReadOnlySpan<byte> offsetsY = [0, 1, 0, 1];

                var tileRef = (int)tile;

                for (var i = 0; i < 4; i++)
                {
                    var srcX = (tileRef & 0x0F) * World.TileWidth;
                    var srcY = ((tileRef & 0xF0) >> 4) * World.TileHeight;
                    tileRef++;

                    var tileX = srcX / World.TileWidth;
                    var tileY = srcY / World.TileHeight;

                    var index = tileY * (tileSet.ImageWidth / tileSet.TileWidth) + tileX;
                    AddProps(tileSet.Tiles[index], tile, offsetsX[i], offsetsY[i]);
                }
            }

            foreach (var (tile, points) in offsets)
            {
                tile.Properties = [
                    new TiledProperty(TiledTileSetTileProperties.ObjectOffsets, string.Join(", ", points.Select(t => $"({t.X},{t.Y})"))),
                    .. (tile.Properties ?? [])
                ];
            }

            tileSet.Tiles = tileSet.Tiles.Where(t => t.Properties.Length > 0).ToArray();
            options.AddJson(Path.ChangeExtension(imageFilename, "tsj"), tileSet, _jsonSerializerOptions);
        }

        var owTileBehaviors = owResources.TileBehaviors;
        var uwTileBehaviors = uwResources.TileBehaviors;

        AddTileMap(
            "overworldTiles.png", owTileBehaviors,
            [BlockObjType.Cave, BlockObjType.Ground, BlockObjType.Stairs, BlockObjType.Rock, BlockObjType.Headstone],
            [/*BlockObjType.TileRock, BlockObjType.TileHeadstone*/]);
        AddTileMap(
            "underworldTiles.png", uwTileBehaviors,
            [BlockObjType.Block, BlockObjType.Tile, BlockObjType.UnderworldStairs],
            [/*BlockObjType.TileBlock, */BlockObjType.TileWallEdge]);
    }

    private static MapLayout ExtractOverworldMap(Options options)
    {
        byte[] roomCols = null;
        byte[][] roomCols2 = new byte[World.UniqueRooms][];
        ushort[] colTablePtrs = null;
        byte[] colTables = null;

        using var reader = options.GetBinaryReader();
        reader.BaseStream.Position = OWRoomCols;
        roomCols = reader.ReadBytes(124 * 16);
        reader.BaseStream.Position = OWRoomCols;
        for (var i = 0; i < roomCols2.Length; i++)
        {
            roomCols2[i] = reader.ReadBytes(16);
        }

        reader.BaseStream.Position = OWColDir;
        colTablePtrs = new ushort[16];
        for (int i = 0; i < 16; i++)
        {
            colTablePtrs[i] = (ushort)(reader.ReadUInt16() - colTablePtrs[0]);
        }
        colTablePtrs[0] = 0;

        // There are only 10 columns in the last table
        reader.BaseStream.Position = OWColTables;
        colTables = reader.ReadBytes(964);

        var filePath = "overworldRoomCols.dat";
        options.AddFile(filePath, roomCols);

        filePath = "overworldCols.tab";
        var pointers = new List<short>();
        using (var writer = options.AddBinaryWriter(filePath))
        {
            writer.Write((ushort)colTablePtrs.Length);
            for (int i = 0; i < colTablePtrs.Length; i++)
            {
                ushort ptr = (ushort)(colTablePtrs[i] - colTablePtrs[0]);
                pointers.Add((short)ptr);
                writer.Write(ptr);
            }

            writer.Write(colTables);

            Utility.PadStream(writer.BaseStream);
        }

        const int unqiueRoomCount = 124;

        return new MapLayout
        {
            uniqueRoomCount = unqiueRoomCount,
            columnsInRoom = 16,
            rowsInRoom = 11,
            owLayoutFormat = true,
            roomCols = roomCols,
            colTablePtrs = colTablePtrs,
            colTables = colTables,
            roomCols2 = roomCols2,
            Table = TableResource<byte>.Load(options.Files[filePath]),
            RoomCols = ListResource<RoomCols>.LoadList(options.Files["overworldRoomCols.dat"], unqiueRoomCount).ToArray()
        };
    }

    private static readonly JsonSerializerOptions _jsonSerializerOptions = MakeTiledConverter();

    private static JsonSerializerOptions MakeTiledConverter()
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new LowerCaseNamingPolicy(),
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new LowerCaseEnumConverterFactory());
        return jsonOptions;
    }
}

internal static class Extensions
{
    public static int GetRoomId(this Point point) => point.Y * World.WorldWidth + point.X;

    public static Point PointFromRoomId(int roomId) => new(roomId % World.WorldWidth, roomId / World.WorldWidth);
}

internal readonly struct RoomId
{
    public int Id { get; }
    public Point Point
    {
        get => new(Id % World.WorldWidth, Id / World.WorldWidth);
        // set => Id = value.Y * World.WorldWidth + value.X;
    }

    public RoomId(int id) => Id = id;
    public RoomId(Point p) : this(p.Y * World.WorldWidth + p.X) { }
    public RoomId(int x, int y) : this(y * World.WorldWidth + x) { }

    public static implicit operator RoomId(int b) => new(b);
    public static implicit operator RoomId(Point b) => new(b);
}