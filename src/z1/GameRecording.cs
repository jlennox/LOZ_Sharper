using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using z1.Common.IO;

namespace z1;

internal readonly struct RecordingError<T>(T Expected, T Actual)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

internal abstract class GameRecordingObjectAssertion<T>
{
    public abstract bool Assert(T actual, [MaybeNullWhen(true)] out string error);

    // This is awful, but it's only used for assertions in test playbacks.
    protected static bool DeepEquals<TEquals>(TEquals obj1, TEquals obj2, [MaybeNullWhen(true)] out string error)
    {
        error = null;

        if (ReferenceEquals(obj1, obj2)) return true;

        if (obj1 == null || obj2 == null)
        {
            Debugger.Break();
            error = "One of the objects is null:" + new RecordingError<TEquals>(obj1, obj2);
            return false;
        }

        var type = typeof(TEquals);

        if (type.IsValueType || type == typeof(string))
        {
            if (!obj1.Equals(obj2))
            {
                Debugger.Break();
                error = "Objects not equal:" + new RecordingError<TEquals>(obj1, obj2);
                return false;
            }

            return true;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var enum1 = ((IEnumerable)obj1).GetEnumerator();
            var enum2 = ((IEnumerable)obj2).GetEnumerator();

            try
            {
                while (enum1.MoveNext())
                {
                    if (!enum2.MoveNext())
                    {
                        Debugger.Break();
                        error = "Second collection is shorter than the first one:" + new RecordingError<TEquals>(obj1, obj2);
                        return false;
                    }

                    if (!DeepEquals(enum1.Current, enum2.Current, out error))
                    {
                        return false;
                    }
                }

                if (enum2.MoveNext())
                {
                    Debugger.Break();
                    error = "First collection is shorter than the first one:" + new RecordingError<TEquals>(obj1, obj2);
                    return false;
                }

                return true;
            }
            finally
            {
                if (enum1 is IDisposable disposable1) disposable1.Dispose();
                if (enum2 is IDisposable disposable2) disposable2.Dispose();
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value1 = property.GetValue(obj1);
            var value2 = property.GetValue(obj2);

            if (!DeepEquals(value1, value2, out error)) return false;
        }

        return true;
    }
}

internal readonly record struct GameRecordingObjectActorValues
{
    public readonly ObjType ObjType;
    public readonly int X;
    public readonly int Y;

    public GameRecordingObjectActorValues(Actor actor)
    {
        ObjType = actor.ObjType;
        X = actor.X;
        Y = actor.Y;
    }

    public static GameRecordingObjectActorValues[] From(IEnumerable<Actor> actors)
    {
        return actors.Select(t => new GameRecordingObjectActorValues(t)).ToArray();
    }
}

internal sealed class GameRecordingObjectActorAssertion : GameRecordingObjectAssertion<IEnumerable<Actor>>
{
    public GameRecordingObjectActorValues[] Values { get; set; }

    public GameRecordingObjectActorAssertion() { }

    public GameRecordingObjectActorAssertion(IEnumerable<Actor> actors)
    {
        Values = GameRecordingObjectActorValues.From(actors);
    }

    public override bool Assert(IEnumerable<Actor> actuals, [MaybeNullWhen(true)] out string error)
    {
        var actualTypes = GameRecordingObjectActorValues.From(actuals);
        if (!actualTypes.SequenceEqual(Values))
        {
            var errorDescription =  new RecordingError<GameRecordingObjectActorValues[]>(Values, actualTypes);
            error = "Object types do not match:" + errorDescription;
            return false;
        }

        error = null;
        return true;
    }
}

internal sealed class GameRecordingStableObjectPlayerProfileAssertion : GameRecordingObjectAssertion<PlayerProfile>
{
    public sealed class StablePersistedRoomState
    {
        public bool VisitState { get; set; }
        public Dictionary<Direction, PersistedDoorState> DoorState { get; set; } = [];
        public int ObjectCount { get; set; }
        public Dictionary<string, ObjectState> ObjectState { get; set; } = [];

        [JsonConstructor]
        public StablePersistedRoomState()
        {
        }

        public StablePersistedRoomState(PersistedRoomState state)
        {
            VisitState = state.VisitState;
            DoorState = state.DoorState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            ObjectCount = state.ObjectCount;
            ObjectState = state.ObjectState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    public int Version { get; set; } = 1;
    public Dictionary<ItemSlot, int> InventoryItems { get; set; } = [];
    public Dictionary<string, StablePersistedRoomState> RoomStates { get; set; } = [];

    public GameRecordingStableObjectPlayerProfileAssertion(PlayerProfile profile)
    {
        var stableEntries = new[] {
            ItemSlot.Sword,
            ItemSlot.Bombs,
            ItemSlot.Arrow,
            ItemSlot.Bow,
            ItemSlot.Candle,
            ItemSlot.Recorder,
            ItemSlot.Food,
            ItemSlot.Potion,
            ItemSlot.Rod,
            ItemSlot.Raft,
            ItemSlot.Book,
            ItemSlot.Ring,
            ItemSlot.Ladder,
            ItemSlot.MagicKey,
            ItemSlot.Bracelet,
            ItemSlot.Letter,
            ItemSlot.Clock,
            ItemSlot.Rupees,
            ItemSlot.Keys,
            ItemSlot.HeartContainers,
            ItemSlot.PowerTriforce,
            ItemSlot.Boomerang,
            ItemSlot.MagicShield,
        };

        foreach (var slot in stableEntries)
        {
            InventoryItems[slot] = profile.Items.Get(slot);
        }

        RoomStates = profile.RoomState.ToDictionary(kvp => kvp.Key, kvp => new StablePersistedRoomState(kvp.Value));
    }

    public override bool Assert(PlayerProfile actual, [MaybeNullWhen(true)] out string error)
    {
        var stable = new GameRecordingStableObjectPlayerProfileAssertion(actual);
        return DeepEquals(this, stable, out error);
    }
}

internal sealed class GameRecordingObjectPlayerProfileAssertion : GameRecordingObjectAssertion<PlayerProfile>
{
    // This is inherently serializable but it might not always be as stable as we'd want...
    public PlayerProfile Profile { get; set; }

    public GameRecordingObjectPlayerProfileAssertion() { }

    public GameRecordingObjectPlayerProfileAssertion(PlayerProfile profile)
    {
        Profile = profile;
    }

    public override bool Assert(PlayerProfile actual, [MaybeNullWhen(true)] out string error)
    {
        return DeepEquals(Profile, actual, out error);
    }
}

internal readonly record struct GameRecordingInput(int Frame, GameButton[] Buttons);
internal readonly record struct GameRecordingCheat(int Frame, string TypeName, string[] Arguments);
internal readonly record struct GameRecordingAssertion(int Frame)
{
    public required GameRecordingObjectActorAssertion Actors { get; init; }
    public GameRecordingObjectPlayerProfileAssertion? Profile { get; init; }
    public GameRecordingStableObjectPlayerProfileAssertion? StableProfile { get; init; }

    public static GameRecordingAssertion Create(Game game)
    {
        return new GameRecordingAssertion(game.FrameCounter)
        {
            Actors = new GameRecordingObjectActorAssertion(game.World.GetObjects()),
            Profile = default,
            StableProfile = new GameRecordingStableObjectPlayerProfileAssertion(game.World.Profile)
        };
    }

    public bool Assert(Game game, [MaybeNullWhen(true)] out string error)
    {
        if (!Actors.Assert(game.World.GetObjects(), out error)) return false;
        if (!Profile?.Assert(game.World.Profile, out error) ?? true) return false;
        if (!StableProfile?.Assert(game.World.Profile, out error) ?? true) return false;

        error = null;
        return true;
    }
}

internal sealed class GameRecordingState(int seed)
{
    public int Seed { get; } = seed;
    public Queue<GameRecordingInput> Input { get; set; } = [];
    public Queue<GameRecordingCheat> Cheats { get; set; } = [];
    public Queue<GameRecordingAssertion> Assertions { get; set; } = [];
}

internal sealed class GameRecording
{
    public Game Game { get; }
    public bool Enabled { get; set; }

    public GameRecordingState State { get; }

    private GameRecordingInput? _lastInput;

    public GameRecording(Game game, int seed)
    {
        Game = game;
        State = new GameRecordingState(seed);

        game.GameCheats.RanCheat += AddCheat;
    }
    public void BeginRecording()
    {
        Enabled = true;
        Game.Menu.StartWorld(PlayerProfile.CreateForRecording());
    }

    public void WriteRecording(bool compress = true)
    {
        // This isn't the best, it's used while creating test data only.
        var file = Filenames.GetRecordingFilename() + (compress ? ".br" : "");
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(State));
        Stream fs = File.OpenWrite(file);

        if (compress)
        {
            fs = new BrotliStream(fs, CompressionMode.Compress, false);
        }

        stream.CopyTo(fs);
        fs.Dispose();
    }

    public void AddInput(HashSet<GameButton> buttons)
    {
        if (!Enabled) return;

        var newInput = new GameRecordingInput(Game.FrameCounter, buttons.ToArray());
        if (_lastInput != null && newInput.Buttons.SequenceEqual(_lastInput.Value.Buttons)) return;

        _lastInput = newInput;
        State.Input.Enqueue(newInput);
    }

    public void AddCheat(Type type, string[] arguments)
    {
        if (!Enabled) return;

        State.Cheats.Enqueue(new GameRecordingCheat(Game.FrameCounter, type.Name, arguments));
    }

    public void AddAssertion()
    {
        if (!Enabled) return;

        State.Assertions.Enqueue(GameRecordingAssertion.Create(Game));
    }

    public void Record()
    {
        AddInput(Game.Input.GetButtonsUnsafe());
    }
}

internal sealed class GamePlayback
{
    public Game Game { get; }
    public bool Enabled { get; set; }

    public GameRecordingState State { get; }

    private GameRecordingInput? _nextInput;
    private GameRecordingAssertion? _nextAssertion;
    private GameRecordingCheat? _nextCheat;

    public GamePlayback(Game game, GameRecordingState state)
    {
        Enabled = true;
        Game = game;
        State = state;
    }

    private static bool TryDeuque<T>(ref T? value, Queue<T> queue)
        where T : struct
    {
        if (value == null)
        {
            if (queue.Count == 0) return false;
            value = queue.Dequeue();
        }

        return true;
    }

    public bool Playback(Game game)
    {
        TryDeuque(ref _nextInput, State.Input);
        TryDeuque(ref _nextCheat, State.Cheats);
        TryDeuque(ref _nextAssertion, State.Assertions);

        if (_nextInput == null && _nextCheat == null && _nextAssertion == null)
        {
            return false;
        }

        if (_nextInput != null && _nextInput.Value.Frame == game.FrameCounter)
        {
            var buttons = game.Input.GetButtonsUnsafe();
            buttons.Clear();
            foreach (var button in _nextInput.Value.Buttons)
            {
                buttons.Add(button);
            }
            _nextInput = null;
        }

        if (_nextCheat != null && _nextCheat.Value.Frame == game.FrameCounter)
        {
            game.GameCheats.TriggerCheat(_nextCheat.Value.TypeName, _nextCheat.Value.Arguments);
            _nextCheat = null;
        }

        if (_nextAssertion != null && _nextAssertion.Value.Frame == game.FrameCounter)
        {
            if (!_nextAssertion.Value.Assert(game, out var error))
            {
                throw new Exception($"Assertion failed at frame {game.FrameCounter}: {error}");
            }

            Game.Toast("Assertion passed.");

            _nextAssertion = null;
        }

        return true;
    }
}
