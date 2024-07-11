namespace z1;

internal struct PlayState
{
    public enum Substates
    {
        Active,
    }

    public enum RoomType
    {
        Regular,
        Cellar,
        Cave,
    }

    public Substates substate;
    public int timer;

    public bool animatingRoomColors;
    public bool allowWalkOnWater;
    public bool uncoveredRecorderSecret;
    public RoomType roomType;
    public short liftItemTimer;
    public ItemId liftItemId;
    public int personWallY;
}

internal struct PlayCellarState
{
    public enum Substates
    {
        Start,
        FadeOut,
        LoadRoom,
        FadeIn,
        Walk,

        MaxSubstate
    }

    public Substates substate;
    public SpritePriority playerPriority;
    public int targetY;
    public int fadeTimer;
    public int fadeStep;
}

internal struct LeaveCellarState
{
    public enum Substates
    {
        Start,
        FadeOut,
        LoadRoom,
        FadeIn,
        Walk,

        Wait,
        LoadOverworldRoom,

        MaxSubstate
    }

    public Substates substate;
    public int fadeTimer;
    public int fadeStep;
    public int timer;
}

internal struct PlayCaveState
{
    public enum Substates
    {
        Start,
        Wait,
        LoadRoom,
        Walk,

        MaxSubstate
    }

    public Substates substate;
    public int timer;
    public int targetY;
    public SpritePriority playerPriority;
}

internal struct ScrollState
{
    public enum Substates
    {
        Start,
        AnimatingColors,
        FadeOut,
        LoadRoom,
        Scroll,

        MaxSubstate
    }

    public const int StateTime = 32;

    public Substates substate;
    public int timer;
    public Direction scrollDir;
    public int nextRoomId;
    public int curRoomId;

    public int offsetX;
    public int offsetY;
    public int speedX;
    public int speedY;
    public int oldTileMapIndex;
    public int oldRoomId;
    public int oldMapToNewMapDistX;
    public int oldMapToNewMapDistY;
}

internal struct LeaveState
{
    public const int StateTime = 2;

    public int timer;
    public Direction scrollDir;
    public int curRoomId;
}

internal struct EnterState
{
    public enum Substates
    {
        Start,
        Wait,
        FadeIn,
        Walk,
        WalkCave,

        MaxSubstate
    }

    public const int StateTime = 2;

    public Substates substate;
    public int timer;
    public Direction scrollDir;
    public int targetX;
    public int targetY;
    public int playerSpeed;
    public int playerFraction;
    public SpritePriority playerPriority;
    public bool gotoPlay;
}

internal struct LoadLevelState
{
    public enum Substates
    {
        Load,
        Wait,
    }

    public const int StateTime = 18;

    public Substates substate;
    public int timer;
    public int level;
    public bool restartOW;
}

internal struct UnfurlState
{
    public enum Substates
    {
        Start,
        Unfurl,
    }

    public const int StateTime = 11;

    public Substates substate;
    public int timer;
    public int stepTimer;
    public int left;
    public int right;
    public bool restartOW;
}

internal struct EndLevelState
{
    public enum Substates
    {
        Start,
        Wait1,
        Flash,
        FillHearts,
        Wait2,
        Furl,
        Wait3,

        MaxSubstate
    }

    public const int Wait1Time = 0x30;
    public const int FlashTime = 0x30;
    public const int Wait2Time = 0x80;
    public const int Wait3Time = 0x80;

    public Substates substate;
    public int timer;
    public int stepTimer;
    public int left;
    public int right;
}

internal struct WinGameState
{
    public enum Substates
    {
        Start,
        Text1,
        Stand,
        Hold1,
        Colors,
        Hold2,
        Text2,
        Hold3,
        NoObjects,
        Credits,

        MaxSubstate
    }

    public enum NpcVisual
    {
        Npc_None,
        Npc_Stand,
        Npc_Lift,
    }

    public const int TextBox2Top = 0xA8;

    public Substates substate;
    public int timer;
    public int stepTimer;
    public int left;
    public int right;
    public NpcVisual npcVisual;
}

internal struct StairsState
{
    public enum Substates
    {
        Start,
        Walk,
        WalkCave,

        MaxSubstate
    }

    public Substates substate;
    public Direction scrollDir;
    public int targetX;
    public int targetY;
    public int playerSpeed;
    public int playerFraction;
    public TileBehavior tileBehavior;
    public SpritePriority playerPriority;
}

internal struct DeathState
{
    public enum Substates
    {
        Start,
        Flash,
        Wait1,
        Turn,
        Fade,
        GrayLink,
        Spark,
        Wait2,
        GameOver,

        MaxSubstate
    };

    public Substates Substate;
    public int Timer;
    public int Step;
}

internal struct ContinueState
{
    public enum Substates
    {
        Start,
        Idle,
        Chosen,

        MaxSubstate
    };

    public Substates Substate;
    public int Timer;
    public int SelectedIndex;
}

internal sealed class WorldState
{
    public PlayState Play = new();
    public PlayCellarState PlayCellar = new();
    public LeaveCellarState LeaveCellar = new();
    public PlayCaveState PlayCave = new();
    public ScrollState Scroll = new();
    public LeaveState Leave = new();
    public EnterState Enter = new();
    public LoadLevelState LoadLevel = new();
    public UnfurlState Unfurl = new();
    public StairsState Stairs = new();
    public EndLevelState EndLevel = new();
    public WinGameState WinGame = new();
    public ContinueState Continue = new();
    public DeathState Death = new();
}
