using System.Diagnostics;
using System.Drawing;
using System.Reflection;
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
    bool IsOverworld,
    RoomContext RoomContext, RoomCols[] RoomCols, TableResource<byte> ColTable, byte[] TileAttrs,
    TableResource<byte> ObjectList, byte[] PrimaryMobs, byte[]? SecondaryMobs, byte[] TileBehaviors,
    RoomAttr[] RoomAttrs, TableResource<byte> SparseTable, LevelInfoBlock LevelInfoBlock, MapResources? CellarResources,
    CaveSpec[]? CaveSpecs)
{
    public byte[][] GetRoomColsArray() => RoomCols.Select(t => t.Get()).ToArray();
    public RoomAttr GetRoomAttr(Point point) => RoomAttrs[point.Y * World.WorldWidth + point.X];
    public bool IsCellarRoom(RoomId roomId) => !IsOverworld && RoomAttrs[roomId.Id].GetUniqueRoomId() >= 0x3E;

    public bool FindSparseFlag(RoomId roomId, Sparse attrId) => SparseTable.FindSparseAttr<SparsePos>(attrId, roomId.Id).HasValue;
    public SparsePos? FindSparsePos(RoomId roomId, Sparse attrId) => SparseTable.FindSparseAttr<SparsePos>(attrId, roomId.Id);
    public SparsePos2? FindSparsePos2(RoomId roomId, Sparse attrId) => SparseTable.FindSparseAttr<SparsePos2>(attrId, roomId.Id);
    public SparseRoomItem? FindSparseItem(RoomId roomId, Sparse attrId) => SparseTable.FindSparseAttr<SparseRoomItem>(attrId, roomId.Id);
}

public unsafe partial class LozExtractor
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

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(static a => a.GetTypes())
            .Where(static t => t.GetCustomAttribute<TiledClass>() != null)
            .ToArray();

        var propertyTypes = TiledProjectCustomProperty.From(types).ToArray();

        options.AddJson("Maps/Project.tiled-project", new TiledProject
        {
            PropertyTypes = propertyTypes
        }, _tiledJsonOptionsProject);
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
        var levelInfoEx = ExtractOverworldInfoEx(options);

        var resources = new MapResources(
            IsOverworld: true,
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
            CellarResources: null,
            CaveSpecs: levelInfoEx.CaveSpec);

        ExtractTiledMap(options, resources, "Overworld");

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
            IsOverworld: false,
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
            CellarResources: null,
            CaveSpecs: null);

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
                resources = resources with
                {
                    RoomAttrs = roomAttributes[levelGroup].RoomAttributes,
                    LevelInfoBlock = levelInfo[level],
                    CellarResources = resources.CellarResources with
                    {
                        RoomAttrs = roomAttributes[levelGroup].RoomAttributes,
                        LevelInfoBlock = levelInfo[level],
                    }
                };
                ExtractTiledMap(options, resources, $"Level{questId:D2}_{i:D2}");
            }
        }

        return resources;
    }

    [DebuggerDisplay("{RoomId.Id} ({RoomId.X},{RoomId.Y})")]
    private readonly record struct RoomEntry(RoomId RoomId, TiledTile[] Tiles, ActionableTiles[] Actions);

    private static void ExtractTiledMap(Options options, MapResources resources, string name)
    {
        var extractor = new MapExtractor(resources);
        var isOverworld = resources.IsOverworld;

        var questObjects = Enumerable.Range(0, 3).Select(_ => new List<TiledLayerObject>()).ToArray();
        var allExtractedRooms = new List<RoomEntry?>();

        var startRoomId = resources.LevelInfoBlock.StartRoomId;
        var startingRoom = Extensions.PointFromRoomId(startRoomId);

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
        }

        // Draw each map into allExtractedScreens and store the Actions.
        var currentRoomId = 0;
        for (var roomY = 0; roomY < World.WorldHeight; ++roomY)
        {
            for (var roomX = 0; roomX < World.WorldWidth; roomX++, currentRoomId++)
            {
                var roomId = new RoomId(roomX, roomY);

                var map = extractor.LoadLayout(currentRoomId, out var actions);
                var tiles = extractor.DrawMap(map, currentRoomId);

                // If this is a dungeon, we only want the rooms that are specific to this one dungeon.
                if (hasVisitedRooms && !visitedRooms.Contains(roomId))
                {
                    allExtractedRooms.Add(null);
                    continue;
                }
                allExtractedRooms.Add(new RoomEntry(roomId, tiles, actions));
            }
        }

        var minroomX = allExtractedRooms.Where(t => t != null).Min(t => t.Value.RoomId.X);
        var maxroomX = allExtractedRooms.Where(t => t != null).Max(t => t.Value.RoomId.X);

        static TiledProperty[] GetRoomProperties(MapResources resources, RoomId roomId, int minroomX, int maxroomX)
        {
            var properties = new List<TiledProperty>();
            var levelBlock = resources.LevelInfoBlock;
            var startRoomId = resources.LevelInfoBlock.StartRoomId;
            var roomAttr = resources.RoomAttrs[roomId.Id];
            var owRoomAttrs = new OWRoomAttr(roomAttr);
            var uwRoomAttrs = new UWRoomAttr(roomAttr);
            var isCellar = resources.IsCellarRoom(roomId);

            properties.Add(new TiledProperty(TiledObjectProperties.Id, roomId.GetGameRoomId()));
            // properties.AddIf(roomId.Id == startRoomId, new TiledProperty(TiledObjectProperties.IsEntryRoom, true));

            if (resources.IsOverworld)
            {
                properties.AddIf(owRoomAttrs.DoMonstersEnter(), new TiledProperty(TiledObjectProperties.MonstersEnter, true));
                // properties.AddIf(owRoomAttrs.HasAmbientSound(), new TiledProperty(TiledObjectProperties.AmbientSound, SoundEffect.Sea));

                var exitPos = owRoomAttrs.GetExitPosition();
                if (exitPos != 0)
                {
                    var col = exitPos & 0x0F;
                    var row = (exitPos >> 4) + 4;
                    var point = new PointXY(col * World.BlockWidth, row * World.BlockHeight + 0x0D);
                    // properties.Add(new TiledProperty(TiledObjectProperties.ExitPosition, point));
                }

                var maze = resources.SparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, roomId.Id);
                if (maze != null)
                {
                    properties.Add(TiledProperty.ForClass(TiledObjectProperties.Maze, new MazeRoom
                    {
                        Path = maze.Value.Paths.ToArray(),
                        ExitDirection = maze.Value.ExitDirection,
                    }));
                }

                // TopRightOverworldSecret
                // if (roomAttr.GetUniqueRoomId() == 0x0F)
                // {
                //     properties.Add(new TiledProperty(TiledObjectProperties.PlaysSecretChime, true));
                // }
                //
                // if (resources.FindSparseFlag(roomId, Sparse.Ladder))
                // {
                //     properties.Add(new TiledProperty(TiledObjectProperties.IsLadderAllowed, true));
                // }
            }

            if (!resources.IsOverworld && !isCellar)
            {
                if (resources.LevelInfoBlock.FindCellarRoomIds(roomId.Id, resources.RoomAttrs, out var left, out var right))
                {
                    var leftp = Extensions.PointFromRoomId(left).GetRoomIdObj();
                    var rightp = Extensions.PointFromRoomId(right).GetRoomIdObj();

                    properties.Add(TiledProperty.CreateArgument(TiledObjectProperties.CellarStairsLeft, leftp.GetGameRoomId()));
                    properties.Add(TiledProperty.CreateArgument(TiledObjectProperties.CellarStairsRight, rightp.GetGameRoomId()));
                }

                var doors = TiledObjectProperties.DoorDirectionOrder
                    .Select(uwRoomAttrs.GetDoor)
                    .Select(t => t.ToString())
                    .ToArray();
                properties.Add(new TiledProperty(TiledObjectProperties.DungeonDoors, string.Join(", ", doors)));
                // properties.AddIf(uwRoomAttrs.IsDark(), new TiledProperty(TiledObjectProperties.IsDark, true));

                // var ambientSound = uwRoomAttrs.GetAmbientSound();
                // if (ambientSound != 0)
                // {
                //     var soundId = SoundEffect.BossRoar1 + ambientSound - 1;
                //     properties.Add(new TiledProperty(TiledObjectProperties.AmbientSound, soundId));
                // }

                var secret = uwRoomAttrs.GetSecret();
                if (secret != Secret.None)
                {
                    properties.Add(new TiledProperty(TiledObjectProperties.Secret, secret));
                }

                var itemId = uwRoomAttrs.GetItemId();
                if (itemId != 0)
                {
                    properties.Add(new TiledProperty(TiledObjectProperties.ItemId, itemId));
                    var posIndex = uwRoomAttrs.GetItemPositionIndex();
                    var block = resources.LevelInfoBlock;
                    var bytePos = block.ShortcutPosition[posIndex];
                    var pos = new PointXY(bytePos & 0xF0, (byte)(bytePos << 4));
                    properties.Add(new TiledProperty(TiledObjectProperties.ItemPosition, pos));
                }

                var fireballLayoutIndex = Array.IndexOf(new[] { 0x24, 0x23 }, roomAttr.GetUniqueRoomId());
                if (fireballLayoutIndex >= 0)
                {
                    properties.Add(new TiledProperty(TiledObjectProperties.FireballLayout, fireballLayoutIndex));
                }

                // if (resources.LevelInfoBlock.BossRoomId == roomId.Id)
                // {
                //     properties.Add(new TiledProperty(TiledObjectProperties.IsBossRoom, true));
                // }
                //
                // if (resources.FindSparseFlag(roomId, Sparse.Ladder))
                // {
                //     properties.Add(new TiledProperty(TiledObjectProperties.IsLadderAllowed, true));
                // }

                var caveId = owRoomAttrs.GetCaveId();
                if (caveId != 0)
                {
                    // properties.Add(new TiledProperty(TiledObjectProperties.CaveId, caveId));
                }

                // if (levelBlock.LevelNumber == 2 && roomId.X == 0xd && roomId.Y == 0)
                // {
                //     Debugger.Break();
                // }

                // if (levelBlock.LevelNumber == 9) Debugger.Break();

                // We need to do the math to figure out where this room would be drawn in the x/y space
                // of the minimap. So we need to "center" this map.
                const int miniMapWidth = 8;
                const int drawnMapXOffset = 4; // a fixed offset.
                var xoff = (miniMapWidth - (maxroomX - minroomX + 1)) / 2;
                var currentroomX = roomId.X - minroomX;
                var drawnmapX = currentroomX + xoff + drawnMapXOffset;
                var mapMaskByte = levelBlock.DrawnMap[drawnmapX] << roomId.Y;

                if ((mapMaskByte & 0x80) != 0x80)
                {
                    properties.Add(new TiledProperty(TiledObjectProperties.HiddenFromMap, true));
                }
            }

            var innerPalette = roomAttr.GetInnerPalette();
            var outerPalette = roomAttr.GetOuterPalette();

            if (isCellar)
            {
                innerPalette = (Palette)2;
                outerPalette = (Palette)3;

                const int startY = 0x9D;
                var keeseX = new[] { 0x20, 0x60, 0x90, 0xD0 };
                var keese = keeseX.Select(t => new MonsterEntry(ObjType.BlueKeese, 1, new Point(t, startY))).ToArray();
                properties.Add(new TiledProperty(TiledObjectProperties.Monsters, string.Join(", ", keese)));
            }
            else
            {
                if (TryExtractMonsterList(roomAttr, resources, out var monsterString))
                {
                    properties.Add(new TiledProperty(TiledObjectProperties.Monsters, monsterString));
                }
            }

            SoundEffect? ambientSound = null;
            if (resources.IsOverworld && owRoomAttrs.HasAmbientSound())
            {
                ambientSound = SoundEffect.Sea;
            }
            else if (!resources.IsOverworld)
            {
                var ambientSoundInt = uwRoomAttrs.GetAmbientSound();
                if (ambientSoundInt != 0)
                {
                    ambientSound = SoundEffect.BossRoar1 + ambientSoundInt - 1;
                }
            }

            properties.Add(TiledProperty.ForClass(TiledObjectProperties.RoomInformation, new RoomInformation
            {
                InnerPalette = innerPalette,
                OuterPalette = outerPalette,
                IsBossRoom = !resources.IsOverworld && resources.LevelInfoBlock.BossRoomId == roomId.Id,
                IsLadderAllowed = resources.FindSparseFlag(roomId, Sparse.Ladder),
                IsEntryRoom = roomId.Id == startRoomId,
                AmbientSound = ambientSound,
                IsDark = uwRoomAttrs.IsDark(),
                PlaysSecretChime = resources.IsOverworld && roomAttr.GetUniqueRoomId() == 0x0F,
            }));

            return properties.ToArray();
        }

        var tilesets = new[] {
            new TiledTileSetReference {
                FirstGid = TiledTile.CreateFirstGid(0),
                Source = "../overworldTiles.tsj",
            },
            new TiledTileSetReference {
                FirstGid = TiledTile.CreateFirstGid(1),
                Source = "../underworldTiles.tsj",
            },
        };

        var worldEntries = new List<TiledWorldEntry>();

        foreach (var roomEntry in allExtractedRooms)
        {
            if (roomEntry == null)
            {
                continue;
            }

            var room = roomEntry.Value;

            foreach (var q in questObjects) q.Clear();

            foreach (var action in room.Actions)
            {
                questObjects[action.QuestId].Add(action.CreateTiledLayerObject());
            }

            // Walk through wall in upper right of overworld map.
            if (isOverworld && room.RoomId.Id == 0x1F)
            {
                questObjects[0].Add(new TiledLayerObject
                {
                    X = 15 * World.RoomColumns * 8 + 0x80,
                    Y = 1 * World.RoomRows * 8,
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

            var backgroundLayer = new TiledLayer(World.RoomColumns, World.RoomRows, room.Tiles)
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

                return new TiledLayer(World.RoomColumns * World.WorldWidth, World.RoomRows * World.WorldHeight)
                {
                    Name = title,
                    Type = TiledLayerType.ObjectGroup,
                    Visible = true,
                    Opacity = 1.0f,
                    Objects = objects.ToArray(),
                    Properties = properties.Count == 0 ? null : properties.ToArray(),
                };
            }

            var objectLayers = questObjects
                .Where(t => t.Count > 0)
                .Select(TransformObjectsIntoLayer)
                .ToArray();

            var tiledmap = new TiledMap
            {
                Width = backgroundLayer.Width,
                Height = backgroundLayer.Height,
                TileWidth = 8,
                TileHeight = 8,
                Layers = [backgroundLayer, .. objectLayers],
                TileSets = tilesets,
                Properties = GetRoomProperties(resources, room.RoomId, minroomX, maxroomX),
            };

            var filename = $"{name}/Map-{room.RoomId.X:D2}-{room.RoomId.Y:D2}.json";

            options.AddJson($"Maps/{filename}", tiledmap, _tiledJsonOptions);

            worldEntries.Add(new TiledWorldEntry
            {
                Filename = filename,
                X = room.RoomId.X * World.RoomColumns * World.TileWidth,
                Y = room.RoomId.Y * World.RoomRows * World.TileHeight,
                Width = World.RoomColumns * World.TileWidth,
                Height = World.RoomRows * World.TileHeight,
            });
        }

        var worldInfo = resources.LevelInfoBlock.GetWorldInfo();
        var world = new TiledWorld
        {
            Maps = worldEntries.ToArray(),
            Properties = [new TiledProperty(TiledWorldProperties.WorldInfo, JsonSerializer.Serialize(worldInfo))],
        };

        options.AddJson($"Maps/{name}.world", world, _tiledJsonOptions);


        // if (!isOverworld && !options.Files.ContainsKey("Map/Common.world"))
        // {
        //     var cellarRoomIds = new[] { new RoomId(4), new RoomId(7) };
        //
        //     var eachCellarScreen = new List<TiledTile[]>();
        //     var cellarObjects = new List<TiledLayerObject>();
        //     var basex = 0;
        //     foreach (var curCellarRoom in cellarRoomIds)
        //     {
        //         var map = extractor.LoadLayout(curCellarRoom.Id, out var actions);
        //         var tiles = extractor.DrawMap(map, curCellarRoom.Id);
        //         eachCellarScreen.Add(tiles);
        //         cellarObjects.AddRange(actions.Select(t => TransformAction(t, curCellarRoom.Point.X, curCellarRoom.Point.Y)));
        //         cellarObjects.Add(CreateScreenObject(resources, curCellarRoom, basex, 0));
        //         basex += World.RoomColumns * 8;
        //     }
        //
        //     var cellarTilesInMapOrder = GetTilesInMapOrder(eachCellarScreen, false, visitedRooms, cellarRoomIds.Length, 1);
        //
        //     var cellarBackgroundLayer = new TiledLayer(
        //         World.RoomColumns * cellarRoomIds.Length,
        //         World.RoomRows * 1,
        //         cellarTilesInMapOrder.ToArray())
        //     {
        //         Name = "World",
        //         Type = TiledLayerType.TileLayer,
        //         Visible = true,
        //         Opacity = 1.0f,
        //     };
        //
        //     var cellarObjectLayer = TransformObjectsIntoLayer(cellarObjects, 0);
        //
        //     var cellarTiledMap = new TiledMap
        //     {
        //         Width = cellarBackgroundLayer.Width,
        //         Height = cellarBackgroundLayer.Height,
        //         TileWidth = 8,
        //         TileHeight = 8,
        //         Layers = [cellarBackgroundLayer, cellarObjectLayer],
        //         TileSets = tilesets
        //     };
        //
        //     options.AddJson($"CellarMap.json", cellarTiledMap, _tiledJsonOptions);
        // }
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
            var tileSet = new TiledTileSet(imageFilename, options.Files[Path.GetFileName(imageFilename)], World.TileWidth, World.TileHeight)
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
            var tilesetFile = "Maps/" + Path.GetFileName(Path.ChangeExtension(imageFilename, "tsj"));
            options.AddJson(tilesetFile, tileSet, _tiledJsonOptions);
        }

        var owTileBehaviors = owResources.TileBehaviors;
        var uwTileBehaviors = uwResources.TileBehaviors;

        AddTileMap(
            "../overworldTiles.png", owTileBehaviors,
            [BlockObjType.Cave, BlockObjType.Ground, BlockObjType.Stairs, BlockObjType.Rock, BlockObjType.Headstone, BlockObjType.Dock],
            [/*BlockObjType.TileRock, BlockObjType.TileHeadstone*/]);
        AddTileMap(
            "../underworldTiles.png", uwTileBehaviors,
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

    private static bool TryExtractMonsterList(RoomAttr roomAttr, MapResources resources, out string monsterString)
    {
        // JOE: TODO: Need to handle a lot of other cases here.
        var monsterCount = roomAttr.GetMonsterCount();
        monsterString = "";
        if (monsterCount == 0) return false;

        var objId = (ObjType)roomAttr.MonsterListId;
        var isList = objId >= ObjType.Rock;
        var monsterList = new List<MonsterEntry>();

        if (resources.IsOverworld)
        {
            var owRoomAttrs = new OWRoomAttr(roomAttr);
            if (owRoomAttrs.HasZora()) monsterList.Add(new MonsterEntry(ObjType.Zora));
        }

        if (isList)
        {
            var listId = objId - ObjType.Rock;
            var list = resources.ObjectList.GetItem(listId);
            var monsters = new ObjType[monsterCount];
            for (var i = 0; i < monsterCount; i++) monsters[i] = (ObjType)list[i];
            monsterList.AddRange(monsters.GroupBy(t => t).Select(t => new MonsterEntry(t.Key, t.Count())));
        }
        else
        {
            monsterList.Add(new MonsterEntry(objId, monsterCount));
        }

        monsterString = string.Join(", ", monsterList.Where(t => t.ObjType != ObjType.None));

        return !string.IsNullOrWhiteSpace(monsterString);
    }

    private static readonly JsonSerializerOptions _tiledJsonOptions = MakeTiledConverter(false);
    private static readonly JsonSerializerOptions _tiledJsonOptionsProject = MakeTiledConverter(true);

    private static JsonSerializerOptions MakeTiledConverter(bool isProject)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new LowerCaseNamingPolicy(),
            WriteIndented = true,
        };
        jsonOptions.Converters.Add(new LowerCaseEnumConverterFactory());
        jsonOptions.Converters.Add(new TiledProperty.Converter(!isProject));
        return jsonOptions;
    }
}

internal static class Extensions
{
    public static int GetRoomId(this Point point) => point.Y * World.WorldWidth + point.X;
    public static RoomId GetRoomIdObj(this Point point) => new RoomId(point.X, point.Y);

    public static Point PointFromRoomId(int roomId) => new(roomId % World.WorldWidth, roomId / World.WorldWidth);

    public static bool AddIf<T>(this List<T> list, bool condition, T item)
    {
        if (condition)
        {
            list.Add(item);
            return true;
        }
        return false;
    }
}

[DebuggerDisplay("{Id} ({X},{Y})")]
internal readonly struct RoomId
{
    public int Id { get; }
    public Point Point
    {
        get => new(Id % World.WorldWidth, Id / World.WorldWidth);
        // set => Id = value.Y * World.WorldWidth + value.X;
    }

    public int X => Id % World.WorldWidth;
    public int Y => Id / World.WorldWidth;

    public RoomId(int id) => Id = id;
    public RoomId(Point p) : this(p.Y * World.WorldWidth + p.X) { }
    public RoomId(int x, int y) : this(y * World.WorldWidth + x) { }

    public string GetGameRoomId() => $"{X},{Y}";

    public static implicit operator RoomId(int b) => new(b);
    public static implicit operator RoomId(Point b) => new(b);
}