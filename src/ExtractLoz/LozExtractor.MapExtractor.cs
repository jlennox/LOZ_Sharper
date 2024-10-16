using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using System.Text;
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
    LevelInfoEx LevelInfoEx, string[] TextTable, int QuestId)
{
    public ShopSpec[] CaveSpec => LevelInfoEx.CaveSpec;

    public byte[][] GetRoomColsArray() => RoomCols.Select(t => t.Get()).ToArray();
    public RoomAttr GetRoomAttr(Point point) => RoomAttrs[point.Y * World.WorldWidth + point.X];
    public RoomAttr GetRoomAttr(RoomId point) => RoomAttrs[point.Id];
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
        { new LevelGroupMap(0, 1), 0 },
        { new LevelGroupMap(0, 2), 0 },
        { new LevelGroupMap(0, 3), 0 },
        { new LevelGroupMap(0, 4), 0 },
        { new LevelGroupMap(0, 5), 0 },
        { new LevelGroupMap(0, 6), 0 },
        { new LevelGroupMap(0, 7), 1 },
        { new LevelGroupMap(0, 8), 1 },
        { new LevelGroupMap(0, 9), 1 },
        { new LevelGroupMap(1, 1), 2 },
        { new LevelGroupMap(1, 2), 2 },
        { new LevelGroupMap(1, 3), 2 },
        { new LevelGroupMap(1, 4), 2 },
        { new LevelGroupMap(1, 5), 2 },
        { new LevelGroupMap(1, 6), 2 },
        { new LevelGroupMap(1, 7), 3 },
        { new LevelGroupMap(1, 8), 3 },
        { new LevelGroupMap(1, 9), 3 },
    };
    private static byte[,] _wallTileMap = null;
    private static string[] _textTable = null;
    private static DoorTileIndex _doorTileMaps;

    private static void ExtractTiledMaps(Options options)
    {
        using var reader = options.GetBinaryReader();
        _wallTileMap ??= ExtractUnderworldWalls(reader, new Bitmap(300, 300), out _doorTileMaps);
        _textTable ??= ExtractText(options);

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
            LevelInfoEx: levelInfoEx,
            TextTable: _textTable ?? throw new Exception(),
            QuestId: 0);

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
        var levelInfoEx = ExtractOverworldInfoEx(options);

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
            LevelInfoEx: levelInfoEx,
            TextTable: _textTable ?? throw new Exception(),
            QuestId: 0);

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
                    QuestId = questId,
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
        var extractor = new MapExtractor(resources, _wallTileMap, _doorTileMaps);
        var isOverworld = resources.IsOverworld;

        var questObjects = Enumerable.Range(0, 3).Select(_ => new List<TiledLayerObject>()).ToArray();
        var allExtractedRooms = new List<RoomEntry?>();

        var startRoomId = resources.LevelInfoBlock.StartRoomId;
        var startingRoom = new RoomId(startRoomId);

        // There's multiple dungeons on a single map. To know which rooms belong to this dungeon,
        // we walk the map from the entrance, following transport cellars, to isolate a single map.
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
            var nextRooms = new Stack<RoomId>([startingRoom]);

            while (nextRooms.Count > 0)
            {
                var currentRoom = nextRooms.Pop();
                var uwRoomAttrs = new UWRoomAttr(resources.GetRoomAttr(currentRoom));
                visitedRooms.Add(currentRoom);
                foreach (var (dir, offset) in checks)
                {
                    if (resources.LevelInfoBlock.FindCellarRoomIds(currentRoom, resources.RoomAttrs, out var left, out var right, out _))
                    {
                        if (!visitedRooms.Contains(left)) nextRooms.Push(left);
                        if (!visitedRooms.Contains(right)) nextRooms.Push(right);
                    }

                    var door = uwRoomAttrs.GetDoor(dir);
                    if (door == DoorType.Wall) continue;
                    if (dir == Direction.Down && currentRoom.Y == World.WorldHeight - 1) continue; // entry rooms are always the bottom row, and we can't go lower.
                    var nextRoom = new RoomId(currentRoom.X + offset.X, currentRoom.Y + offset.Y);
                    if (visitedRooms.Contains(nextRoom)) continue;
                    nextRooms.Push(nextRoom);
                }
            }
        }

        // Draw each map into allExtractedScreens and store the Actions.
        for (var roomY = 0; roomY < World.WorldHeight; ++roomY)
        {
            for (var roomX = 0; roomX < World.WorldWidth; roomX++)
            {
                var roomId = new RoomId(roomX, roomY);

                var map = extractor.LoadLayout(roomId, out var actions);
                var tiles = extractor.DrawMap(map, roomId);

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

        static TiledProperty[] GetRoomProperties(
            MapResources resources, RoomId roomId,
            int minroomX, int maxroomX,
            string? name = null, RoomFlags roomOptions = RoomFlags.None)
        {
            var properties = new List<TiledProperty>();
            var levelBlock = resources.LevelInfoBlock;
            var startRoomId = resources.LevelInfoBlock.StartRoomId;
            var roomAttr = resources.RoomAttrs[roomId.Id];
            var owRoomAttrs = new OWRoomAttr(roomAttr);
            var uwRoomAttrs = new UWRoomAttr(roomAttr);
            var isCellar = resources.IsCellarRoom(roomId);
            var roomInteractions = new List<RoomInteraction>();

            properties.Add(new TiledProperty(TiledRoomProperties.Id, name ?? roomId.GetGameRoomId()));

            if (resources.IsOverworld)
            {
                properties.AddIf(owRoomAttrs.DoMonstersEnter(), new TiledProperty(TiledRoomProperties.MonstersEnter, true));

                var maze = resources.SparseTable.FindSparseAttr<SparseMaze>(Sparse.Maze, roomId.Id);
                if (maze != null)
                {
                    properties.Add(TiledProperty.ForClass(TiledRoomProperties.Maze, new MazeRoom
                    {
                        Path = maze.Value.Paths.ToArray(),
                        ExitDirection = maze.Value.ExitDirection,
                    }));
                }
            }

            if (!resources.IsOverworld && !isCellar)
            {
                var doors = TiledRoomProperties.DoorDirectionOrder
                    .Select(uwRoomAttrs.GetDoor)
                    .Select(t => t.ToString())
                    .ToArray();
                properties.Add(new TiledProperty(TiledRoomProperties.UnderworldDoors, string.Join(", ", doors)));

                var fireballLayoutIndex = Array.IndexOf([0x24, 0x23], roomAttr.GetUniqueRoomId(roomId));
                if (fireballLayoutIndex >= 0)
                {
                    properties.Add(new TiledProperty(TiledRoomProperties.FireballLayout, fireballLayoutIndex));
                }

                if (maxroomX != 0 && minroomX != 0)
                {
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
                        roomOptions |= RoomFlags.HiddenFromMap;
                    }
                }
            }

            var innerPalette = roomAttr.GetInnerPalette();
            var outerPalette = roomAttr.GetOuterPalette();

            ShopSpec UnderworldPersonSpec(ObjType personType)
            {
                if (personType == ObjType.Grumble) return resources.CaveSpec.First(t => t.PersonType == PersonType.Grumble);

                var secret = uwRoomAttrs.GetSecret();
                if (secret == Secret.MoneyOrLife) return resources.CaveSpec.First(t => t.PersonType == PersonType.MoneyOrLife);

                var levelGroups = new byte[] { 0, 0, 1, 1, 0, 1, 0, 1, 2 };
                var levelIndex = resources.LevelInfoBlock.EffectiveLevelNumber - 1;
                int levelTableIndex = levelGroups[levelIndex];
                var stringSlot = personType - ObjType.Person1;
                var stringId = (StringId)resources.LevelInfoEx.LevelPersonStringIds[levelTableIndex][stringSlot];
                if (stringId == StringId.MoreBombs) return resources.CaveSpec.First(t => t.PersonType == PersonType.MoreBombs);

                return new ShopSpec
                {
                    DwellerType = DwellerType.OldMan,
                    PersonType = PersonType.Text,
                    Text = _textTable[(int)stringId],
                };
            }

            if (isCellar)
            {
                innerPalette = (Palette)2;
                outerPalette = (Palette)3;

                const int startY = 0x9D;
                var keeseX = new[] { 0x20, 0x60, 0x90, 0xD0 };
                var keese = keeseX.Select(t => new MonsterEntry(ObjType.BlueKeese, false, 1, new Point(t, startY))).ToArray();
                properties.Add(new TiledProperty(TiledRoomProperties.Monsters, string.Join(", ", keese)));
            }
            else
            {
                var hasRingleader = !resources.IsOverworld && uwRoomAttrs.GetSecret() == Secret.Ringleader;
                if (TryExtractMonsterList(roomAttr, resources, hasRingleader, out var monsterString, out var monsterList))
                {
                    var personType = monsterList.FirstOrDefault(t => t.ObjType is >= ObjType.Person1 and < ObjType.PersonEnd or ObjType.Grumble);

                    if (personType != default && !resources.IsOverworld)
                    {
                        var caveSpec = UnderworldPersonSpec(personType.ObjType);
                        properties.Add(TiledProperty.ForClass(TiledRoomProperties.CaveSpec, caveSpec));
                    }
                    else
                    {
                        properties.Add(new TiledProperty(TiledRoomProperties.Monsters, monsterString));
                    }
                }
            }

            SoundEffect? ambientSound = null;
            if (resources.IsOverworld)
            {
                if (owRoomAttrs.HasAmbientSound()) ambientSound = SoundEffect.Sea;
                if (roomAttr.GetUniqueRoomId(roomId) == 0x0F) roomOptions |= RoomFlags.PlaysSecretChime;
            }
            else
            {
                var ambientSoundInt = uwRoomAttrs.GetAmbientSound();
                if (ambientSoundInt != 0) ambientSound = SoundEffect.BossRoar1 + ambientSoundInt - 1;
                if (resources.LevelInfoBlock.BossRoomId == roomId.Id) roomOptions |= RoomFlags.IsBossRoom;
                if (uwRoomAttrs.IsDark()) roomOptions |= RoomFlags.IsDark;

                if (uwRoomAttrs.GetSecret() == Secret.FoesDoor)
                {
                    roomInteractions.Add(new RoomInteraction
                    {
                        Name = "FoesDoor",
                        Interaction = Interaction.None,
                        Requirements = InteractionRequirements.AllEnemiesDefeated,
                        Effect = InteractionEffect.OpenShutterDoors,
                    });
                }
            }

            if (resources.FindSparseFlag(roomId, Sparse.Ladder)) roomOptions |= RoomFlags.IsLadderAllowed;
            if (roomId.Id == startRoomId)
            {
                properties.Add(TiledProperty.ForClass(TiledRoomProperties.EntryPosition, new EntryPosition(World.StartX, resources.LevelInfoBlock.StartY, Direction.Up)));
                roomOptions |= RoomFlags.IsEntryRoom;
            }

            if (roomInteractions.Count > 0)
            {
                var interactions = new RoomInteractions
                {
                    Interactions = roomInteractions.ToArray(),
                };
                properties.AddRange(TiledPropertySerializer<RoomInteractions>.Serialize(interactions));
            }

            properties.Add(TiledProperty.ForClass(TiledRoomProperties.RoomSettings, new RoomSettings
            {
                InnerPalette = innerPalette,
                OuterPalette = outerPalette,
                Options = roomOptions,
                AmbientSound = ambientSound,
                FloorTile = resources.IsOverworld ? BlockType.Ground : BlockType.Tile
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

        static TiledLayer TransformObjectsIntoLayer(IEnumerable<TiledLayerObject> objects, int i)
        {
            var properties = new List<TiledProperty>();
            var title = "Object";
            if (i > 0)
            {
                properties.Add(new TiledProperty(TiledLayerProperties.QuestId, i));
                title = $"Object (Quest {i})";
            }

            return new TiledLayer(World.RoomTileWidth * World.WorldWidth, World.RoomTileHeight * World.WorldHeight)
            {
                Name = title,
                Type = TiledLayerType.ObjectGroup,
                Visible = true,
                Opacity = 1.0f,
                Objects = objects.ToArray(),
                Properties = properties.Count == 0 ? null : properties.ToArray(),
            };
        }

        var worldEntries = new List<TiledWorldEntry>();

        foreach (var roomEntry in allExtractedRooms)
        {
            if (roomEntry == null) continue;
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
                    X = 15 * World.RoomTileWidth * 8 + 0x80,
                    Y = 1 * World.RoomTileHeight * 8,
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

            var backgroundLayer = new TiledLayer(World.RoomTileWidth, World.RoomTileHeight, room.Tiles)
            {
                Name = "World",
                Type = TiledLayerType.TileLayer,
                Visible = true,
                Opacity = 1.0f,
            };

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
                X = room.RoomId.X * World.RoomTileWidth * World.TileWidth,
                Y = room.RoomId.Y * World.RoomTileHeight * World.TileHeight,
                Width = World.RoomTileWidth * World.TileWidth,
                Height = World.RoomTileHeight * World.TileHeight,
            });
        }

        var worldtype = resources.IsOverworld ? GameWorldType.Overworld : GameWorldType.Underworld;
        var worldInfo = resources.LevelInfoBlock.GetWorldInfo(worldtype);
        var world = new TiledWorld
        {
            Maps = worldEntries.ToArray(),
            Properties = [new TiledProperty(TiledWorldProperties.WorldSettings, JsonSerializer.Serialize(worldInfo))],
        };

        options.AddJson($"Maps/{name}.world", world, _tiledJsonOptions);

        var isUnderworldCommon = !isOverworld && resources.LevelInfoBlock.LevelNumber == 1 && resources.QuestId == 0;

        if (isOverworld || isUnderworldCommon)
        {
            var commonName = isOverworld ? "OverworldCommon" : "UnderworldCommon";
            var commonRooms = new List<TiledWorldEntry>();
            (RoomId Room, string Name)[] cellarRoomIds = isUnderworldCommon
                ? [
                    (new RoomId(4), CommonUnderworldRoomName.ItemCellar),
                    (new RoomId(7), CommonUnderworldRoomName.Transport)
                ] : [
                    (RoomId.FromUniqueRoomId(0x79), CommonOverworldRoomName.Cave),
                    (RoomId.FromUniqueRoomId(0x7A), CommonOverworldRoomName.Shortcut)
                ];


            foreach (var (commonRoomId, commonRoomName) in cellarRoomIds)
            {
                var map = extractor.LoadLayout(commonRoomId, out var actions);
                var tiles = extractor.DrawMap(map, commonRoomId);

                foreach (var q in questObjects) q.Clear();
                foreach (var action in actions)
                {
                    questObjects[action.QuestId].Add(action.CreateTiledLayerObject());
                }

                var commonBackgroundLayer = new TiledLayer(World.RoomTileWidth, World.RoomTileHeight, tiles)
                {
                    Name = "World",
                    Type = TiledLayerType.TileLayer,
                };

                var objectLayers = questObjects
                    .Where(t => t.Count > 0)
                    .Select(TransformObjectsIntoLayer)
                    .ToArray();

                if (commonRoomName == CommonUnderworldRoomName.ItemCellar)
                {
                    foreach (var layer in objectLayers)
                    {
                        foreach (var obj in layer.Objects ?? [])
                        {
                            if (obj.GetClass<RoomItem>(nameof(InteractableBlock.Item)) is { } roomItem)
                            {
                                roomItem.Item = ItemId.ArgumentItemId;
                                roomItem.Options |= ItemObjectOptions.LiftOverhead;
                            }
                        }
                    }
                }

                var cellarTiledMap = new TiledMap
                {
                    Width = commonBackgroundLayer.Width,
                    Height = commonBackgroundLayer.Height,
                    TileWidth = World.TileWidth,
                    TileHeight = World.TileHeight,
                    Layers = [commonBackgroundLayer, .. objectLayers],
                    TileSets = tilesets,
                    Properties = GetRoomProperties(resources, commonRoomId, 0, 0, commonRoomName, RoomFlags.ShowPreviousMap)
                };

                var filename = $"{commonName}/{commonRoomName}.json";

                options.AddJson($"Maps/{filename}", cellarTiledMap, _tiledJsonOptions);

                commonRooms.Add(new TiledWorldEntry
                {
                    Filename = filename,
                    X = commonRooms.Count * World.RoomTileWidth * World.TileWidth,
                    Y = 0,
                    Width = World.RoomTileWidth * World.TileWidth,
                    Height = World.RoomTileHeight * World.TileHeight,
                });
            }

            worldInfo.WorldType = resources.IsOverworld ? GameWorldType.OverworldCommon : GameWorldType.UnderworldCommon;

            var commonWorld = new TiledWorld
            {
                Maps = commonRooms.ToArray(),
                Properties = [new TiledProperty(TiledWorldProperties.WorldSettings, JsonSerializer.Serialize(worldInfo))],
            };

            options.AddJson($"Maps/{commonName}.world", commonWorld, _tiledJsonOptions);
        }
    }

    private static void MakeTiledTileSets(Options options, MapResources owResources, MapResources uwResources)
    {
        var offsets = new Dictionary<TiledTileSetTile, List<Point>>();

        void AddTileMap(
            string imageFilename,
            ReadOnlySpan<byte> tileBehavior,
            ReadOnlySpan<BlockType> objectTypes,
            ReadOnlySpan<TileType> tiles)
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

            void AddProps(TiledTileSetTile tile, BlockType obj, int x, int y)
            {
                var newprops = new List<TiledProperty>();
                if (!offsets.TryGetValue(tile, out var points))
                {
                    points = new List<Point>();
                    offsets[tile] = points;
                }

                points.Add(new Point(x, y));

                if (string.IsNullOrEmpty(tile.GetProperty(TiledTileSetTileProperties.Object)))
                {
                    newprops.Add(new TiledProperty(TiledTileSetTileProperties.Object, obj.ToString()));
                }

                tile.Properties = [
                    .. newprops,
                    .. (tile.Properties ?? [])
                ];
            }

            void SetTile(int a, int b, int c, int d, BlockType obj)
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
                    AddProps(tileSet.Tiles[index], (BlockType)tile, offsetsX[i], offsetsY[i]);
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
            [BlockType.Cave, BlockType.Ground, BlockType.Stairs, BlockType.Rock, BlockType.Headstone, BlockType.Dock],
            [/*TileType.TileRock, TileType.TileHeadstone*/]);

        AddTileMap(
            "../underworldTiles.png", uwTileBehaviors,
            [BlockType.Block, BlockType.Tile, BlockType.UnderworldStairs],
            [/*BlockObjType.TileBlock, */TileType.WallEdge]);
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

    private static bool TryExtractMonsterList(
        RoomAttr roomAttr, MapResources resources, bool hasRingleader,
        out string monsterString, out List<MonsterEntry> monsterList)
    {
        // JOE: TODO: Need to handle a lot of other cases here.
        var monsterCount = roomAttr.GetMonsterCount();
        monsterString = "";
        monsterList = new List<MonsterEntry>();
        if (monsterCount == 0) return false;

        var objId = (ObjType)roomAttr.MonsterListId;
        var isList = objId >= ObjType.Rock;

        if (objId is >= ObjType.OneDodongo and < ObjType.Rock)
        {
            monsterCount = 1;
        }

        if (resources.IsOverworld)
        {
            var owRoomAttrs = new OWRoomAttr(roomAttr);
            if (owRoomAttrs.HasZora()) monsterList.Add(new MonsterEntry(ObjType.Zora));
        }

        static void AddMonster(ObjType objId, ref bool hasRingleader, int count, List<MonsterEntry> monsterList)
        {
            if (!hasRingleader)
            {
                monsterList.Add(new MonsterEntry(objId, false, count));
                return;
            }

            monsterList.Add(new MonsterEntry(objId, true, 1));
            hasRingleader = false;
            count--;

            if (count > 0)
            {
                monsterList.Add(new MonsterEntry(objId, false, count));
            }
        }

        if (isList)
        {
            var listId = objId - ObjType.Rock;
            var list = resources.ObjectList.GetItem(listId);
            var monsters = new ObjType[monsterCount];
            for (var i = 0; i < monsterCount; i++) monsters[i] = (ObjType)list[i];
            // monsterList.AddRange(monsters.GroupBy(t => t).Select(t => new MonsterEntry(t.Key, false, t.Count())));

            foreach (var group in monsters.GroupBy(t => t))
            {
                AddMonster(group.Key, ref hasRingleader, group.Count(), monsterList);
            }
        }
        else
        {
            AddMonster(objId, ref hasRingleader, monsterCount, monsterList);
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
    public static bool AddIf<T>(this List<T> list, bool condition, T item)
    {
        if (condition)
        {
            list.Add(item);
            return true;
        }
        return false;
    }

    public static byte[] ReadBytesFrom(this BinaryReader reader, int location, int bytecount)
    {
        reader.BaseStream.Position = location;
        return reader.ReadBytes(bytecount);
    }

    public static string HexDump(this byte[,] bin)
    {
        var xlength = bin.GetLength(0);
        var ylength = bin.GetLength(1);
        var sb = new StringBuilder(xlength & ylength * 3 + 5);
        for (var y = 0; y < ylength; y++)
        {
            for (var x = 0; x < xlength; x++)
            {
                if (x > 0) sb.Append(',');
                sb.Append(bin[x, y].ToString("X2"));
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }
}

[DebuggerDisplay("{Id} ({X},{Y})")]
internal readonly struct RoomId
{
    public int Id { get; }
    public int? UniqueRoomId { get; init; }

    public int X => Id % World.WorldWidth;
    public int Y => Id / World.WorldWidth;

    public RoomId(int id) => Id = id;
    public RoomId(Point p) : this(p.Y * World.WorldWidth + p.X) { }
    public RoomId(int x, int y) : this(y * World.WorldWidth + x) { }

    public static RoomId FromUniqueRoomId(int id) => new() { UniqueRoomId = id };

    public string GetGameRoomId() => $"{X},{Y}";

    // public static implicit operator RoomId(int b) => new(b);
    // public static implicit operator RoomId(Point b) => new(b);

    public override bool Equals([NotNullWhen(true)] object obj) => obj is RoomId other && Equals(other);
    public bool Equals(RoomId other) => Id == other.Id && UniqueRoomId == other.UniqueRoomId;
    public override int GetHashCode() => HashCode.Combine(Id, UniqueRoomId);
    public static bool operator ==(RoomId left, RoomId right) => left.Equals(right);
    public static bool operator !=(RoomId left, RoomId right) => !left.Equals(right);
}