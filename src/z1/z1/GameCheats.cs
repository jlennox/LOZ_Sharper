using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace z1;

internal class GameCheats
{
    private abstract class Cheat
    {
        public abstract bool OnKeyPressed(Keys key, [MaybeNullWhen(false)] out string[] args);
        public abstract void RunPayload(Game game, string[] args);
    }

    private abstract class RegexCheat : Cheat
    {
        protected const RegexOptions DefaultRegexOptions =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking;

        protected abstract Regex FullMatch { get; }
        protected abstract Regex PartialMatch { get; }

        private readonly StringBuilder _input = new();

        public override bool OnKeyPressed(Keys key, [MaybeNullWhen(false)] out string[] args)
        {
            args = null;

            var val = key.GetKeyString();
            _input.Append(val);
            var inputString = _input.ToString();
            if (!PartialMatch.IsMatch(inputString))
            {
                _input.Clear();
                return false;
            }

            var result = FullMatch.Match(inputString);
            if (result.Success)
            {
                _input.Clear();
                args = result.Groups.Values.Skip(1).Select(x => x.Value).ToArray();
                return true;
            }

            return false;
        }
    }

    private abstract class SingleWordCheat : RegexCheat
    {
        protected override Regex FullMatch { get; }
        protected override Regex PartialMatch { get; }

        protected SingleWordCheat(string cheat)
        {
            if (cheat != Regex.Escape(cheat))
            {
                throw new ArgumentException("SingleWordCheat does not support characters that need to be escaped.");
            }

            FullMatch = new Regex($"^{cheat};$", DefaultRegexOptions);
            var cheatRest = string.Concat(cheat.Skip(1).Select(t => $"{t}?"));
            PartialMatch = new Regex($"^{cheat[0] + cheatRest};?$", DefaultRegexOptions);
        }
    }

    private sealed class OverworldWarpCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^w(\d+)x(\d+);$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^w(\d*)x?(\d*);?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!int.TryParse(args[0], out var x) || !int.TryParse(args[0], out var y))
            {
                Debug.WriteLine("Invalid warp coordinates. " + string.Join(", ", args));
                return;
            }

            // game.World.GotoLoadLevel(
        }
    }

    private sealed class DungeonWarpCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^w(\d+);$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^w(\d*);?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!int.TryParse(args[0], out var levelNumber))
            {
                Debug.WriteLine("Invalid warp coordinates. " + string.Join(", ", args));
                return;
            }

            game.World.GotoLoadLevel(levelNumber);
        }
    }

    private sealed class GodModeCheat : SingleWordCheat
    {
        public GodModeCheat() : base("iddqd") { }

        public override void RunPayload(Game game, string[] args)
        {
            Game.GodMode = !Game.GodMode;
        }
    }

    private sealed class ItemsCheat : SingleWordCheat
    {
        public ItemsCheat() : base("idkfa") { }

        public override void RunPayload(Game game, string[] args)
        {
            var profile = game.World.profile;
            if (profile == null) return;
            profile.Items[ItemSlot.MagicShield] = 1;
            profile.Items[ItemSlot.Food] = 1;
            profile.Items[ItemSlot.Bombs] = 99;
            profile.Items[ItemSlot.Keys] = 99;
            profile.Items[ItemSlot.Sword] = 3;
            profile.Items[ItemSlot.HeartContainers] = 16;
            profile.Items[ItemSlot.Raft] = 1;
            profile.Items[ItemSlot.Ladder] = 1;
            profile.Items[ItemSlot.Ring] = 2;
            profile.Items[ItemSlot.RupeesToAdd] = 0xff;
            profile.Items[ItemSlot.Bow] = 1;
            profile.Items[ItemSlot.Arrow] = 2;
            profile.Items[ItemSlot.Bracelet] = 1;
            profile.Items[ItemSlot.Candle] = 2;
            profile.Items[ItemSlot.Rod] = 1;
            game.World.SetItem(ItemSlot.TriforcePieces, 0xFF);
        }
    }

    private readonly Game _game;
    private readonly Cheat[] _cheats = new Cheat[]
    {
        new OverworldWarpCheat(),
        new DungeonWarpCheat(),
        new GodModeCheat(),
        new ItemsCheat(),
    };

    public GameCheats(Game game)
    {
        _game = game;
    }

    public void OnKeyPressed(Keys key)
    {
        foreach (var cheat in _cheats)
        {
            if (cheat.OnKeyPressed(key, out var args))
            {
                cheat.RunPayload(_game, args);
            }
        }
    }
}