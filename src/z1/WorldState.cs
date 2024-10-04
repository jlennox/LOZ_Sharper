﻿using System.Diagnostics;
using z1.Actors;

namespace z1;

internal enum RoomType { Regular, Cellar, Cave }

[DebuggerDisplay("{Substate}")]
internal struct PlayState
{
    public enum Substates { Active }

    public Substates Substate;
    public int Timer;

    public bool AnimatingRoomColors;
    public bool AllowWalkOnWater;
    public bool UncoveredRecorderSecret;
    public RoomType RoomType;
    public short LiftItemTimer;
    public ItemId LiftItemId;
    public int PersonWallY;
}

[DebuggerDisplay("{Substate}")]
internal struct PlayCellarState
{
    public enum Substates
    {
        Start,
        FadeOut,
        LoadRoom,
        FadeIn,
        Walk,
    }

    public Substates Substate;
    public SpritePriority PlayerPriority;
    public int TargetY;
    public int FadeTimer;
    public int FadeStep;
}

[DebuggerDisplay("{Substate}")]
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
    }

    public Substates Substate;
    public int FadeTimer;
    public int FadeStep;
    public int Timer;
}

[DebuggerDisplay("{Substate}")]
internal struct PlayCaveState
{
    public enum Substates
    {
        Start,
        Wait,
        LoadRoom,
        Walk,
    }

    public Substates Substate;
    public CaveId CaveId;
    public int Timer;
    public int TargetY;
    // public SpritePriority playerPriority; // JOE: Unused in C++.
}

[DebuggerDisplay("{Substate}")]
internal struct ScrollState
{
    public enum Substates
    {
        Start,
        AnimatingColors,
        FadeOut,
        LoadRoom,
        Scroll,
    }

    public const int StateTime = 32;

    public Substates Substate;
    /// <summary>How long before the screen begins the scroll animation.</summary>
    public int Timer;
    public Direction ScrollDir;
    public GameRoom NextRoom;
    public GameRoom CurrentRoom;

    public int OffsetX;
    public int OffsetY;
    public int SpeedX;
    public int SpeedY;
    public GameRoom OldRoom;
    public int OldMapToNewMapDistX;
    public int OldMapToNewMapDistY;
}

internal struct LeaveState
{
    public const int StateTime = 2;

    public int Timer;
    public Direction ScrollDir;
    public GameRoom CurrentRoom;
}

[DebuggerDisplay("{Substate}")]
internal struct EnterState
{
    public enum Substates
    {
        Start,
        Wait,
        FadeIn,
        Walk,
        WalkCave,
    }

    public const int StateTime = 2;

    public Substates Substate;
    public int Timer;
    public Direction ScrollDir;
    public int TargetX;
    public int TargetY;
    public int PlayerSpeed;
    public int PlayerFraction;
    public SpritePriority PlayerPriority;
    public bool GotoPlay;

    public bool HasReachedTarget(Player player) => player.Position.HasReachedPoint(TargetX, TargetY, ScrollDir);
}

[DebuggerDisplay("{Substate}")]
internal struct LoadLevelState
{
    public enum Substates
    {
        Load,
        Wait,
    }

    public const int StateTime = 18;

    public Substates Substate;
    public int Timer;
    public int Level;
    public bool RestartOW;
}

[DebuggerDisplay("{Substate}")]
internal struct UnfurlState
{
    public enum Substates
    {
        Start,
        Unfurl,
    }

    public const int StateTime = 11;

    public Substates Substate;
    public int Timer;
    public int StepTimer;
    public int Left;
    public int Right;
    public bool RestartOW;
}

[DebuggerDisplay("{Substate}")]
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
    }

    public const int Wait1Time = 0x30;
    public const int FlashTime = 0x30;
    public const int Wait2Time = 0x80;
    public const int Wait3Time = 0x80;

    public Substates Substate;
    public int Timer;
    public int StepTimer;
    public int Left;
    public int Right;
}

[DebuggerDisplay("{Substate}")]
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
    }

    public enum NpcVisualState
    {
        None,
        Stand,
        Lift,
    }

    public const int TextBox2Top = 0xA8;

    public Substates Substate;
    public int Timer;
    public int StepTimer;
    public int Left;
    public int Right;
    public NpcVisualState NpcVisual;
}

[DebuggerDisplay("{Substate}")]
internal struct StairsState
{
    public enum Substates
    {
        Start,
        Walk,
        WalkCave,
    }

    public Substates Substate;
    public Direction ScrollDir;
    public int TargetX;
    public int TargetY;
    public int PlayerSpeed;
    public int PlayerFraction;
    public TileBehavior TileBehavior;
    public SpritePriority PlayerPriority;
    public Entrance Entrance;

    public bool HasReachedTarget(Player player) => player.Position.HasReachedPoint(TargetX, TargetY, ScrollDir);
}

[DebuggerDisplay("{Substate}")]
internal struct DeathState
{
    public enum Substates
    {
        Start,
        Flash,
        Wait1,
        Turn,
        Fade,
        GrayPlayer,
        Spark,
        Wait2,
        GameOver,
    }

    public Substates Substate;
    public int Timer;
    public int Step;
}

[DebuggerDisplay("{Substate}")]
internal struct ContinueState
{
    public enum Substates
    {
        Start,
        Idle,
        Chosen,
    }

    public enum Indexes
    {
        Continue,
        Save,
        Retry,
    }

    public Substates Substate;
    public int Timer;
    public Indexes SelectedIndex;
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

internal enum LevelBlock
{
    Width = 16,
    Height = 8,
    Rooms = 128,
}

internal sealed class RoomHistory
{
    private readonly Game _game;
    private readonly GameRoom[] _roomHistory;
    private int _nextRoomHistorySlot;

    public RoomHistory(Game game, int length)
    {
        _game = game;
        _roomHistory = new GameRoom[length];
    }

    public bool IsRoomInHistory()
    {
        for (var i = 0; i < _roomHistory.Length; i++)
        {
            if (_roomHistory[i] == _game.World.CurrentRoom) return true;
        }
        return false;
    }

    public void AddRoomToHistory()
    {
        var i = 0;

        for (; i < _roomHistory.Length; i++)
        {
            if (_roomHistory[i] == _game.World.CurrentRoom) break;
        }

        if (i == _roomHistory.Length)
        {
            _roomHistory[_nextRoomHistorySlot] = _game.World.CurrentRoom;
            _nextRoomHistorySlot++;
            if (_nextRoomHistorySlot >= _roomHistory.Length)
            {
                _nextRoomHistorySlot = 0;
            }
        }
    }

    public void Clear() => Array.Clear(_roomHistory);
}