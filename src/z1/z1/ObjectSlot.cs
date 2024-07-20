namespace z1;

internal enum ObjectSlot
{
    NoneFound = -1,
    Monster1, // 0
    Monster2,
    Monster3,
    Monster4,
    Monster5,
    Monster6, // 5
    Monster7,
    Monster8,
    Monster9,
    Monster10, // 9
    Monster11 = Monster1 + 10,
    Buffer,
    PlayerSword,
    PlayerSwordShot,
    Boomerang,
    // ORIGINAL: There are two s that bombs and fires share.
    Bomb,
    Bomb2,
    Fire,
    Fire2,
    Ladder,
    Food,
    Arrow,
    Item,
    FluteMusic,
    // ORIGINAL: The player is first.
    Player,
    Door,
    MaxObjects,

    FirstSlot = Monster1,

    FirstBomb = Bomb,
    LastBomb = Bomb2 + 1,

    FirstFire = Fire,
    LastFire = Fire2 + 1,

    LastMonster = Monster11,
    MonsterEnd = Monster11 + 1,
    MaxMonsters = MonsterEnd - Monster1,
    MaxObjListSize = 9,

    // Simple synonyms
    Whirlwind = Monster1 + 8,
    Block = Buffer,

    // Object timers
    FadeTimer = Buffer,

    // Long timers
    EdgeObjTimer = Buffer + 2,
    RedLeeverClassTimer = Arrow,
    NoSwordTimer = Boomerang,
    ObservedPlayerTimer = PlayerSword,
}

internal enum StringId
{
    DoorRepair = 5,
    AintEnough = 10,
    LostHillsHint = 11,
    LostWoodsHint = 12,
    Grumble = 18,
    MoreBombs = 25,
    MoneyOrLife = 27,
    EnterLevel9 = 34,
}
