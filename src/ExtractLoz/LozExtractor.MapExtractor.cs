using System.Drawing;
using System.Text.Json;
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

internal readonly record struct MapResources(
    RoomContext RoomContext, RoomCols[] RoomCols, TableResource<byte> ColTable, byte[] TileAttrs,
    TableResource<byte> ObjectList, byte[] PrimaryMobs, byte[]? SecondaryMobs, byte[] TileBehaviors,
    RoomAttr[] RoomAttrs, TableResource<byte> SparseTable, LevelInfoBlock LevelInfoBlock);

public partial class LozExtractor
{
    private static void ExtractTiledMaps(Options options)
    {
        var overworldResources = ExtractOverworldTiledMaps(options);
        var underworldResources = ExtractUnderworldTiledMaps(options);

        MakeTiledTileSets(options, overworldResources, underworldResources);
    }

    private static MapResources ExtractOverworldTiledMaps(Options options)
    {
        using var reader = options.GetBinaryReader();
        var owTileBehaviors = ExtractOverworldTileBehaviors(options, options.GetBinaryReader()).ToArray();
        var roomAttributes = ExtractOverworldMapAttrs(options);
        var extractedOverworldMap = ExtractOverworldMap(options);
        var columnTables = TableResource<byte>.Load(options.Files["overworldCols.tab"]);
        var tileAttributes = ExtractOverworldTileAttrs(options);
        var objectList = ExtractObjLists(options);
        var (primaries, secondaries) = ExtractOverworldMobs(options, reader);
        var sparseTable = ExtractOverworldMapSparseAttrs(options);
        var infoBlock = ExtractOverworldInfo(options);

        var resources = new MapResources(
            RoomContext.OpenRoomContext, extractedOverworldMap.RoomCols, columnTables, tileAttributes,
            objectList, primaries, secondaries, owTileBehaviors,
            roomAttributes, sparseTable, infoBlock);

        ExtractTiledMap(options, resources, "Overworld", true);

        return resources;
    }

    private readonly record struct LevelGroupMap(int QuestId, int LevelNumber);

    private static MapResources ExtractUnderworldTiledMaps(Options options)
    {
        using var reader = options.GetBinaryReader();
        var columnTables = TableResource<byte>.Load(options.Files["underworldCols.tab"]);
        var tileAttributes = ExtractUnderworldTileAttrs(options);
        var objectList = ExtractObjLists(options);
        var sparseTable = ExtractOverworldMapSparseAttrs(options); // there is no underworld specific one.
        var tileBehaviors = ExtractUnderworldTileBehaviors(options, reader).ToArray();
        var levelInfo = ExtractUnderworldInfo(options);
        var mapObjects = ExtractUnderworldMobs(options, reader);
        var worldInfo = ExtractUnderworldMap(options);
        var roomAttributes = ExtractUnderworldMapAttrs(options).ToDictionary(t => t.LevelGroup);

        var levelGroupMap = new Dictionary<LevelGroupMap, int>
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

        var resources = new MapResources(
            RoomContext.ClosedRoomContext, worldInfo.RoomCols, columnTables, tileAttributes,
            objectList, mapObjects.Backing, null, tileBehaviors,
            roomAttributes[0].RoomAttributes, sparseTable, levelInfo.First().Value);

        for (var questId = 0; questId < 2; questId++)
        {
            for (var i = 1; i <= 9; i++)
            {
                var level = new LevelGroupMap(questId, i);
                var levelGroup = levelGroupMap[level];
                resources = resources with {
                    RoomAttrs = roomAttributes[levelGroup].RoomAttributes,
                    LevelInfoBlock = levelInfo[level]
                };
                ExtractTiledMap(options, resources, $"Level{questId:D2}_{i:D2}", false);
            }
        }

        return resources;
    }

    private static void ExtractTiledMap(Options options, MapResources resources, string name, bool isOverworld)
    {
        var roomColumns = resources.RoomCols.Select(t => t.Get()).ToArray();
        var extractor = new MapExtractor(
            resources.SparseTable, resources.RoomAttrs, resources.LevelInfoBlock, resources.PrimaryMobs, resources.SecondaryMobs,
            roomColumns, resources.TileAttrs);

        var questObjects = Enumerable.Range(0, 3).Select(_ => new List<TiledLayerObject>()).ToArray();
        var allTiles = new List<TiledTile[]>();
        static string GetScreenName(int x, int y) => $"Screen {x},{y}";

        var currentRoomId = 0;
        for (var y = 0; y < World.WorldHeight; ++y)
        {
            for (var x = 0; x < World.WorldWidth; x++)
            {
                var map = extractor.LoadLayout(currentRoomId, isOverworld, resources.ColTable, out var actions);
                var tiles = extractor.DrawMap(map, isOverworld, currentRoomId, 0, 0);
                allTiles.Add(tiles);

                var basex = x * World.ScreenColumns * World.TileWidth;
                var basey = y * World.ScreenRows * World.TileHeight;

                foreach (var action in actions)
                {
                    questObjects[action.QuestId].Add(new TiledLayerObject
                    {
                        Id = y * World.WorldWidth + x,
                        X = basex + action.X * World.TileWidth,
                        Y = basey + action.Y * World.TileHeight,
                        Width = World.TileWidth * 2 * action.Width,
                        Height = World.TileHeight * 2 * action.Height,
                        Name = $"{action.Action}",
                        Visible = true,
                        Properties = [
                            new TiledProperty(TiledObjectProperties.Type, GameObjectLayerObjectType.Action),
                            new TiledProperty(TiledObjectProperties.TileAction, action.Action),
                            new TiledProperty(TiledObjectProperties.Owner, GetScreenName(x, y)),
                            .. (action.Properties ?? [])
                        ],
                    });
                }
                currentRoomId++;
            }
        }

        var context = resources.RoomContext;

        var orderedTiles = new List<TiledTile>();

        for (var y = 0; y < World.ScreenRows * World.WorldHeight; y++)
        {
            var screenY = y / World.ScreenRows;
            var mapY = y % World.ScreenRows;
            for (var x = 0; x < World.ScreenColumns * World.WorldWidth; x++)
            {
                var screenX = x / World.ScreenColumns;
                var mapX = x % World.ScreenColumns;

                var roomId = screenY * World.WorldWidth + screenX;
                var tiles = allTiles[roomId];

                var tile = tiles[mapY * World.ScreenColumns + mapX];
                orderedTiles.Add(tile);
            }
        }

        currentRoomId = 0;
        for (var y = 0; y < World.WorldHeight; y++)
        {
            var basey = y * World.ScreenRows * 8;

            for (var x = 0; x < World.WorldWidth; x++)
            {
                var basex = x * World.ScreenColumns * 8;
                var screenProperties = new List<TiledProperty>();
                var maze = resources.SparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, currentRoomId);
                var roomAttr = resources.RoomAttrs[currentRoomId];
                var owRoomAttrs = new OWRoomAttr(roomAttr);

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

                // JOE: TODO: Need to handle a lot of other cases here.
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

                questObjects[0].Add(new TiledLayerObject
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
                        .. screenProperties
                    ],
                });

                currentRoomId++;
            }
        }

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

        var backgroundLayer = new TiledLayer(World.ScreenColumns * World.WorldWidth, World.ScreenRows * World.WorldHeight, orderedTiles.ToArray())
        {
            Name = "World",
            Type = TiledLayerType.TileLayer,
            Visible = true,
            Opacity = 1.0f,
        };

        var objectLayers = questObjects
            .Where(t => t.Count > 0)
            .Select((objects, i) =>
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
            }
            ).ToArray();

        var tiledmap = new TiledMap
        {
            Width = World.ScreenColumns * World.WorldWidth,
            Height = World.ScreenRows * World.WorldHeight,
            TileWidth = 8,
            TileHeight = 8,
            Layers = [backgroundLayer, .. objectLayers],
            TileSets = [
                new TiledTileSetReference {
                    FirstGid = 1,
                    Source = "overworldTiles.tsj",
                },
                new TiledTileSetReference {
                    FirstGid = 257,
                    Source = "underworldTiles.tsj",
                },
            ]
        };

        options.AddJson($"{name}Map.json", tiledmap, _jsonSerializerOptions);
    }

    private static void ExtractTiledMapsOLD(Options options)
    {
        using var reader = options.GetBinaryReader();

        static string GetScreenName(int x, int y) => $"Screen {x},{y}";

        var questObjects = Enumerable.Range(0, 3).Select(_ => new List<TiledLayerObject>()).ToArray();

        var isOverworld = true;

        ExtractOverworldTiles(options);
        ExtractUnderworldTiles(options);
        var extractedOverworldMap = ExtractOverworldMap(options);

        var underworldInfo = ExtractUnderworldMap(options);
        var infoBlock = ExtractOverworldInfo(options);
        var tileAttributes = ExtractOverworldTileAttrs(options);
        var (primaries, secondaries) = ExtractOverworldMobs(options, reader);
        var underworldMapObjects = ExtractUnderworldMobs(options, reader);
        var columnTables = TableResource<byte>.Load(options.Files["overworldCols.tab"]); // (overworldMap.colTablePtrs.Length, overworldMap.pointers.ToArray(), colTables);
        var sparseTable = ExtractOverworldMapSparseAttrs(options);
        var roomAttributes = ExtractOverworldMapAttrs(options);
        var objectList = ExtractObjLists(options);
        var owTileBehaviors = ExtractOverworldTileBehaviors(options, options.GetBinaryReader());
        var uwTileBehaviors = ExtractUnderworldTileBehaviors(options, options.GetBinaryReader());
        var extractor = new MapExtractor(
            sparseTable, roomAttributes,
            infoBlock, primaries, secondaries,
            extractedOverworldMap.roomCols2, tileAttributes);

        var allTiles = new List<TiledTile[]>();

        // Overworld map
        var currentRoomId = 0;
        for (var y = 0; y < World.WorldHeight; ++y)
        {
            for (var x = 0; x < World.WorldWidth; x++)
            {
                var map = extractor.LoadLayout(currentRoomId, true, columnTables, out var actions);
                var tiles = extractor.DrawMap(map, true, currentRoomId, 0, 0);
                allTiles.Add(tiles);

                var basex = x * World.ScreenColumns * World.TileWidth;
                var basey = y * World.ScreenRows * World.TileHeight;

                foreach (var action in actions)
                {
                    questObjects[action.QuestId].Add(new TiledLayerObject
                    {
                        Id = y * World.WorldWidth + x,
                        X = basex + action.X * World.TileWidth,
                        Y = basey + action.Y * World.TileHeight,
                        Width = World.TileWidth * 2 * action.Width,
                        Height = World.TileHeight * 2 * action.Height,
                        Name = $"{action.Action}",
                        Visible = true,
                        Properties = [
                            new TiledProperty(TiledObjectProperties.Type, GameObjectLayerObjectType.Action),
                            new TiledProperty(TiledObjectProperties.TileAction, action.Action),
                            new TiledProperty(TiledObjectProperties.Owner, GetScreenName(x, y)),
                            .. (action.Properties ?? [])
                        ],
                    });
                }
                currentRoomId++;
            }
        }

        var orderedTiles = new List<TiledTile>();

        for (var y = 0; y < World.ScreenRows * World.WorldHeight; y++)
        {
            var screenY = y / World.ScreenRows;
            var mapY = y % World.ScreenRows;
            for (var x = 0; x < World.ScreenColumns * World.WorldWidth; x++)
            {
                var screenX = x / World.ScreenColumns;
                var mapX = x % World.ScreenColumns;

                var roomId = screenY * World.WorldWidth + screenX;
                var tiles = allTiles[roomId];

                var tile = tiles[mapY * World.ScreenColumns + mapX];
                orderedTiles.Add(tile);
            }
        }

        currentRoomId = 0;
        for (var y = 0; y < World.WorldHeight; y++)
        {
            var basey = y * World.ScreenRows * 8;

            for (var x = 0; x < World.WorldWidth; x++)
            {
                var basex = x * World.ScreenColumns * 8;
                var screenProperties = new List<TiledProperty>();
                var maze = sparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, currentRoomId);
                var roomAttr = roomAttributes[currentRoomId];
                var owRoomAttrs = new OWRoomAttr(roomAttr);

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

                // JOE: TODO: Need to handle a lot of other cases here.
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
                        var list = objectList.GetItem(listId);
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

                questObjects[0].Add(new TiledLayerObject
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
                        .. screenProperties
                    ],
                });

                currentRoomId++;
            }
        }

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

        var backgroundLayer = new TiledLayer(World.ScreenColumns * World.WorldWidth, World.ScreenRows * World.WorldHeight, orderedTiles.ToArray())
        {
            Name = "World",
            Type = TiledLayerType.TileLayer,
            Visible = true,
            Opacity = 1.0f,
        };

        var objectLayers = questObjects
            .Where(t => t.Count > 0)
            .Select((objects, i) =>
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
            }
            ).ToArray();

        var tiledmap = new TiledMap
        {
            Width = World.ScreenColumns * World.WorldWidth,
            Height = World.ScreenRows * World.WorldHeight,
            TileWidth = 8,
            TileHeight = 8,
            Layers = [backgroundLayer, .. objectLayers],
            TileSets = [
                new TiledTileSetReference {
                    FirstGid = 1,
                    Source = "overworldTiles.tsj",
                },
                new TiledTileSetReference {
                    FirstGid = 257,
                    Source = "underworldTiles.tsj",
                },
            ]
        };

        options.AddJson("overworldMap.json", tiledmap, _jsonSerializerOptions);
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

        return new MapLayout
        {
            uniqueRoomCount = 124,
            columnsInRoom = 16,
            rowsInRoom = 11,
            owLayoutFormat = true,
            roomCols = roomCols,
            colTablePtrs = colTablePtrs,
            colTables = colTables,
            roomCols2 = roomCols2,
            RoomCols = ListResource<RoomCols>.LoadList(options.Files["overworldRoomCols.dat"], 124).ToArray()
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
