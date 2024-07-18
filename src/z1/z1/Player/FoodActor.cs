using z1.Actors;

namespace z1.Player;

internal sealed class FoodActor : Actor
{
    private int periods;

    public FoodActor(Game game, int x = 0, int y = 0) : base(game, ObjType.Food, x, y)
    {
        Decoration = 0;
        ObjTimer = 0xFF;
    }

    public override void Update()
    {
        if (ObjTimer == 0)
        {
            periods--;
            if (periods == 0)
            {
                IsDeleted = true;
                return;
            }
            ObjTimer = 0xFF;
        }

        // This is how food attracts some monsters.
        var roomObjId = Game.World.RoomObj?.ObjType ?? ObjType.None;

        // TODO: Wire up to actor.IsAttrackedToMeat

        if ((roomObjId >= ObjType.BlueMoblin && roomObjId <= ObjType.BlueFastOctorock)
            || roomObjId == ObjType.Vire
            || roomObjId == ObjType.BlueKeese
            || roomObjId == ObjType.RedKeese)
        {
            Game.World.SetObservedPlayerPos(X, Y);
        }
    }

    public override void Draw()
    {
        GlobalFunctions.DrawItemWide(Game, ItemId.Food, X, Y);
    }
}