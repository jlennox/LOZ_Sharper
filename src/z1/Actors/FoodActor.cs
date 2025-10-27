using z1.Render;

namespace z1.Actors;

internal sealed class FoodActor : Actor
{
    public override bool IsMonsterSlot => false;

    private int _periods;

    public FoodActor(World world, int x, int y) : base(world, ObjType.Food, x, y)
    {
        Decoration = 0;
        ObjTimer = 0xFF;
    }

    public override void Update()
    {
        if (ObjTimer == 0)
        {
            _periods--;
            if (_periods == 0)
            {
                Delete();
                return;
            }
            ObjTimer = 0xFF;
        }

        // This is how food attracts some monsters.
        // var roomObjId = Game.World.RoomObj?.ObjType ?? ObjType.None;
        var roomObjId = Game.World.GetObject<MonsterActor>()?.ObjType ?? ObjType.None;

        // JOE: TODO: Wire up to actor.IsAttrackedToMeat

        if (roomObjId >= ObjType.BlueMoblin && roomObjId <= ObjType.BlueFastOctorock
            || roomObjId == ObjType.Vire
            || roomObjId == ObjType.BlueKeese
            || roomObjId == ObjType.RedKeese)
        {
            Game.World.SetObservedPlayerPos(X, Y);
        }
    }

    public override void Draw(Graphics graphics)
    {
        graphics.DrawItemWide(Game, ItemId.Food, X, Y);
    }
}