using System.Collections.Immutable;

namespace z1;

internal partial class World
{
    public readonly record struct EquipValue(ItemSlot Slot, byte Level, ItemSlot? Max = null, int? MaxValue = null);

    // The item ID to item slot map is at $6B14, and copied to RAM at $72A4.
    // The item ID to item value map is at $6B38, and copied to RAM at $72C8.
    // They're combined here.
    public static readonly ImmutableDictionary<ItemId, EquipValue> ItemToEquipment = new Dictionary<ItemId, EquipValue> {
        { ItemId.Bomb,           new EquipValue(ItemSlot.Bombs,           4, ItemSlot.MaxBombs) },
        { ItemId.WoodSword,      new EquipValue(ItemSlot.Sword,           1) },
        { ItemId.WhiteSword,     new EquipValue(ItemSlot.Sword,           2) },
        { ItemId.MagicSword,     new EquipValue(ItemSlot.Sword,           3) },
        { ItemId.Food,           new EquipValue(ItemSlot.Food,            1) },
        { ItemId.Recorder,       new EquipValue(ItemSlot.Recorder,        1) },
        { ItemId.BlueCandle,     new EquipValue(ItemSlot.Candle,          1) },
        { ItemId.RedCandle,      new EquipValue(ItemSlot.Candle,          2) },
        { ItemId.WoodArrow,      new EquipValue(ItemSlot.Arrow,           1) },
        { ItemId.SilverArrow,    new EquipValue(ItemSlot.Arrow,           2) },
        { ItemId.Bow,            new EquipValue(ItemSlot.Bow,             1) },
        { ItemId.SilverBow,      new EquipValue(ItemSlot.Bow,             2) },
        { ItemId.MagicKey,       new EquipValue(ItemSlot.MagicKey,        1) },
        { ItemId.Raft,           new EquipValue(ItemSlot.Raft,            1) },
        { ItemId.Ladder,         new EquipValue(ItemSlot.Ladder,          1) },
        { ItemId.PowerTriforce,  new EquipValue(ItemSlot.PowerTriforce,   1) },
        { ItemId.FiveRupees,     new EquipValue(ItemSlot.RupeesToAdd,     5, ItemSlot.MaxRupees) },
        { ItemId.Rod,            new EquipValue(ItemSlot.Rod,             1) },
        { ItemId.Book,           new EquipValue(ItemSlot.Book,            1) },
        { ItemId.BlueRing,       new EquipValue(ItemSlot.Ring,            1) },
        { ItemId.RedRing,        new EquipValue(ItemSlot.Ring,            2) },
        { ItemId.Bracelet,       new EquipValue(ItemSlot.Bracelet,        1) },
        { ItemId.Letter,         new EquipValue(ItemSlot.Letter,          1) },
        { ItemId.Rupee,          new EquipValue(ItemSlot.RupeesToAdd,     1, ItemSlot.MaxRupees) },
        { ItemId.Key,            new EquipValue(ItemSlot.Keys,            1) },
        { ItemId.HeartContainer, new EquipValue(ItemSlot.HeartContainers, 1) },
        { ItemId.TriforcePiece,  new EquipValue(ItemSlot.TriforcePieces,  1) },
        { ItemId.MagicShield,    new EquipValue(ItemSlot.MagicShield,     1) },
        { ItemId.WoodBoomerang,  new EquipValue(ItemSlot.Boomerang,       1) },
        { ItemId.MagicBoomerang, new EquipValue(ItemSlot.Boomerang,       2) },
        { ItemId.BluePotion,     new EquipValue(ItemSlot.Potion,          1, null, 2) },
        { ItemId.RedPotion,      new EquipValue(ItemSlot.Potion,          2, null, 2) },
        { ItemId.Clock,          new EquipValue(ItemSlot.Clock,           1) },
        { ItemId.Heart,          new EquipValue(ItemSlot.None,            1) },
        { ItemId.Fairy,          new EquipValue(ItemSlot.None,            3) },
        { ItemId.MaxBombs,       new EquipValue(ItemSlot.MaxBombs,        4) },
    }.ToImmutableDictionary();

    public static ItemId GetItemFromEquipment(ItemSlot slot, int itemLevel)
    {
        foreach (var item in ItemToEquipment)
        {
            if (item.Value.Slot == slot && item.Value.Level == itemLevel)
            {
                return item.Key;
            }
        }

        throw new ArgumentException($"No item found for slot {slot} with level {itemLevel}.");
    }

    private readonly record struct DoorStateBehaviors(TileBehavior Closed, TileBehavior Open)
    {
        public TileBehavior GetBehavior(bool isOpen) => isOpen ? Open : Closed;

        public static DoorStateBehaviors Get(DoorType type) => type switch
        {
            DoorType.Open => new DoorStateBehaviors(TileBehavior.Doorway, TileBehavior.Doorway),
            DoorType.Wall => new DoorStateBehaviors(TileBehavior.Wall, TileBehavior.Wall),
            DoorType.FalseWall => new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Door),
            DoorType.FalseWall2 => new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Door),
            DoorType.Bombable => new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Door),
            DoorType.Key => new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Doorway),
            DoorType.Key2 => new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Doorway),
            DoorType.Shutter => new DoorStateBehaviors(TileBehavior.Door, TileBehavior.Doorway),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported door type.")
        };
    }

    private static readonly Dictionary<Direction, Point> _doorMiddles = new() {
        { Direction.Right, new Point(0xE0, 0x98) },
        { Direction.Left, new Point(0x20, 0x98) },
        { Direction.Down, new Point(0x80, 0xD0) },
        { Direction.Up, new Point(0x80, 0x60) },
    };

    private readonly record struct DoorStateFaces(DoorState Closed, DoorState Open)
    {
        private static DoorStateFaces GetDoorFace(DoorType type) => type switch
        {
            DoorType.Open => new DoorStateFaces(DoorState.Open, DoorState.Open),
            DoorType.Wall => new DoorStateFaces(DoorState.Wall, DoorState.Wall),
            DoorType.FalseWall => new DoorStateFaces(DoorState.Wall, DoorState.Wall),
            DoorType.FalseWall2 => new DoorStateFaces(DoorState.Wall, DoorState.Wall),
            DoorType.Bombable => new DoorStateFaces(DoorState.Wall, DoorState.Bombed),
            DoorType.Key => new DoorStateFaces(DoorState.Locked, DoorState.Open),
            DoorType.Key2 => new DoorStateFaces(DoorState.Locked, DoorState.Open),
            DoorType.Shutter => new DoorStateFaces(DoorState.Shutter, DoorState.Open),
            DoorType.None => new DoorStateFaces(DoorState.None, DoorState.None),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported door type.")
        };

        public static DoorState GetState(DoorType type, bool isOpen)
        {
            var face = GetDoorFace(type);
            return isOpen ? face.Open : face.Closed;
        }
    }

    // These points are as tiles.
    internal readonly record struct DoorCorner(Point EntranceCorner, Point Behind, Point TileCornerOffset)
    {
        public static DoorCorner Get(Direction dir) => dir switch
        {
            Direction.Right => new DoorCorner(new Point(0x1C, 0x0A), new Point(0x1E, 0x0A), new Point(0, -1)),
            Direction.Left => new DoorCorner(new Point(0x02, 0x0A), new Point(0x00, 0x0A), new Point(-1, -1)),
            Direction.Down => new DoorCorner(new Point(0x0F, 0x12), new Point(0x0F, 0x14), new Point(-1, 0)),
            Direction.Up => new DoorCorner(new Point(0x0F, 0x02), new Point(0x0F, 0x00), new Point(-1, -1)),
            _ => throw new ArgumentOutOfRangeException(nameof(dir), dir, "Unsupported direction.")
        };
    }

    private void RunTileBehavior(TileBehavior behavior, int tileY, int tileX, TileInteraction interaction)
    {
        switch (behavior)
        {
            case TileBehavior.GenericWalkable: /* no-op */ break;
            case TileBehavior.Sand: /* no-op */ break;
            case TileBehavior.SlowStairs: /* no-op */ break;
            case TileBehavior.Stairs: /* no-op */ break;

            case TileBehavior.Doorway: /* no-op */ break;
            case TileBehavior.Water: /* no-op */ break;
            case TileBehavior.GenericSolid: /* no-op */ break;
            case TileBehavior.Cave: /* no-op */ break;
            case TileBehavior.Door: DoorTileAction(tileY, tileX, interaction); break;
            case TileBehavior.Wall: /* no-op */ break;
            default: throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unsupported tile behavior.");
        }
    }
}
