using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Silk.NET.Input;
using z1.Actors;

namespace z1;

internal sealed class GameCheats
{
    private static bool TryMatchFromEnum<TEnum>(string partial, out TEnum value)
        where TEnum : struct, Enum
    {
        var validTypes = Enum.GetNames<TEnum>()
            .Where(t => !t.Contains("Child"))
            .ToArray();

        var name =
            validTypes.FirstOrDefault(x => x.IStartsWith(partial))
            ?? validTypes.FirstOrDefault(x => x.IContains(partial));

        if (name == null)
        {
            value = default;
            return false;
        }

        value = Enum.Parse<TEnum>(name);
        return true;
    }

    public abstract class Cheat
    {
        public abstract bool OnKeyPressed(char key, [MaybeNullWhen(false)] out string[] args);
        public abstract void RunPayload(Game game, string[] args);
    }

    public abstract class RegexCheat : Cheat
    {
        protected const RegexOptions DefaultRegexOptions =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking;

        protected abstract Regex FullMatch { get; }
        protected abstract Regex PartialMatch { get; }

        private readonly StringBuilder _input = new();

        public override bool OnKeyPressed(char key, [MaybeNullWhen(false)] out string[] args)
        {
            args = null;

            _input.Append(key);
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

    public abstract class SingleWordCheat : RegexCheat
    {
        protected override Regex FullMatch { get; }
        protected override Regex PartialMatch { get; }

        protected SingleWordCheat(string cheat, bool noTerminator = false)
        {
            if (cheat != Regex.Escape(cheat))
            {
                throw new ArgumentException("SingleWordCheat does not support characters that need to be escaped.");
            }

            FullMatch = new Regex($"^{cheat}{(noTerminator ? "" : ";")}$", DefaultRegexOptions);
            var cheatRest = string.Concat(cheat.Skip(1).Select(t => $"{t}?"));
            PartialMatch = new Regex($"^{cheat[0] + cheatRest}{(noTerminator ? "" : ";?")}$", DefaultRegexOptions);
        }
    }

    public sealed class OverworldWarpCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^w(\d+)x(\d+);$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^w(\d*)x?(\d*);?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!int.TryParse(args[0], out var x) || !int.TryParse(args[0], out var y))
            {
                game.Toast("Invalid warp coordinates. " + string.Join(", ", args));
                return;
            }

            game.World.LoadOverworldRoom(x, y);
            game.Toast($"Warping to room {x}x{y}");
        }
    }

    public sealed class ItemCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^idbehold(\w+);$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^id?b?e?h?o?l?d?\w*;", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!TryMatchFromEnum<ItemId>(args[0], out var itemId))
            {
                game.Toast("Invalid item type. " + args[0]);
                return;
            }

            game.World.DebugSpawnItem(itemId);
        }
    }

    public sealed class DungeonWarpCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^w(w|\d+);$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^w(w?|\d*);?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            var target = args[0];
            switch (target)
            {
                case "w":
                    game.World.ShowShortcutStairs(7 * 16 + 9, 1);
                    game.Toast("Warping to stairs.");
                    return;
            }

            if (!int.TryParse(target, out var levelNumber))
            {
                game.Toast("Invalid warp coordinates. " + string.Join(", ", args));
                return;
            }

            game.World.GotoLoadLevel(levelNumber);
            game.Toast($"Warping to dungeon {levelNumber}");
        }
    }

    public sealed class SpawnCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^s(\w+);$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^s\w*;?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!TryMatchFromEnum<ObjType>(args[0], out var objType))
            {
                game.Toast("Invalid object type. " + string.Join(", ", args));
                return;
            }

            // if (!game.World.TryFindEmptyMonsterSlot(out var slot))
            // {
            //     game.Toast("No empty slots found.");
            //     return;
            // }

            // Ultimately, we want to support TryFindEmptyMonsterSlot but some monsters demand to be spawned
            // into the first slot, making it a problem.
            const ObjectSlot slot = ObjectSlot.Monster1;
            game.World.CurObjectSlot = slot;

            var obj = Actor.FromType(objType, game, 80, 80);
            game.World.SetObject(slot, obj);
            game.Toast($"Spawned {objType} at slot {slot}");
        }
    }

    public sealed class GodModeCheat : SingleWordCheat
    {
        public GodModeCheat() : base("iddqd", true) { }
        public override void RunPayload(Game game, string[] args)
        {
            Game.Cheats.GodMode = !Game.Cheats.GodMode;
            game.Toast("God mode " + (Game.Cheats.GodMode ? "enabled" : "disabled"));
        }
    }

    public sealed class KillAllCheat : SingleWordCheat
    {
        public KillAllCheat() : base("ka", true) { }
        public override void RunPayload(Game game, string[] args)
        {
            game.World.KillAllObjects();
            game.Toast("Killed all objects.");
        }
    }

    public sealed class SpeedUpCheat : SingleWordCheat
    {
        public SpeedUpCheat() : base("su", true) { }
        public override void RunPayload(Game game, string[] args)
        {
            Game.Cheats.SpeedUp = !Game.Cheats.SpeedUp;
            game.Toast("Speed up " + (Game.Cheats.SpeedUp ? "enabled" : "disabled"));
        }
    }

    public sealed class WalkThroughWallsCheat : SingleWordCheat
    {
        public WalkThroughWallsCheat() : base("idclip", true) { }
        public override void RunPayload(Game game, string[] args)
        {
            Game.Cheats.NoClip = !Game.Cheats.NoClip;
            game.Toast("Walk through walls " + (Game.Cheats.NoClip ? "enabled" : "disabled"));
        }
    }

    public sealed class ItemsCheat : SingleWordCheat
    {
        public ItemsCheat() : base("idkfa", true) { }

        public override void RunPayload(Game game, string[] args)
        {
            var profile = game.World.Profile;
            if (profile == null) return;
            game.World.AddItem(ItemId.MagicShield);
            game.World.AddItem(ItemId.Food);
            game.World.AddItem(ItemId.Raft);
            game.World.AddItem(ItemId.Ladder);
            game.World.AddItem(ItemId.RedRing);
            game.World.AddItem(ItemId.Bow);
            game.World.AddItem(ItemId.SilverArrow);
            game.World.AddItem(ItemId.Bracelet);
            game.World.AddItem(ItemId.Letter);
            game.World.AddItem(ItemId.Recorder);
            game.World.AddItem(ItemId.WoodBoomerang);
            game.World.AddItem(ItemId.Book);
            game.World.AddItem(ItemId.RedCandle);
            game.World.AddItem(ItemId.Rod);
            game.World.AddItem(ItemId.MagicSword);
            game.World.AddItem(ItemId.HeartContainer);
            game.World.SetItem(ItemSlot.TriforcePieces, 0xFF);
            game.World.PostRupeeWin(0xFF);
            profile.Items[ItemSlot.Bombs] = 98;
            profile.Items[ItemSlot.Keys] = 98;
            profile.Items[ItemSlot.HeartContainers] = 16;
            profile.Items[ItemSlot.Map] = 0xFF;
            profile.Items[ItemSlot.Compass] = 0xFF;
            profile.Items[ItemSlot.Map9] = 0xFF;
            profile.Items[ItemSlot.Compass9] = 0xFF;
            profile.Hearts = PlayerProfile.GetMaxHeartsValue(16);
            profile.SelectedItem = ItemSlot.Bombs;

            game.Toast("All items added.");
        }
    }

    private readonly Game _game;
    private readonly Input _input;

    private readonly Cheat[] _cheats =
    {
        new OverworldWarpCheat(),
        new DungeonWarpCheat(),
        new GodModeCheat(),
        new ItemCheat(),
        new ItemsCheat(),
        new KillAllCheat(),
        new SpeedUpCheat(),
        new WalkThroughWallsCheat(),
        new SpawnCheat(),
    };

    public GameCheats(Game game, Input input)
    {
        _game = game;
        _input = input;
    }

    public void OnKeyPressed(Key key)
    {
        var chr = key.GetKeyCharacter();
        if (chr == '\0') return;

        foreach (var cheat in _cheats)
        {
            if (cheat.OnKeyPressed(chr, out var args))
            {
                cheat.RunPayload(_game, args);
            }
        }
    }

    public void Update()
    {
        if (_input.IsButtonPressing(GameButton.CheatKillAll)) TriggerCheat<KillAllCheat>();
        if (_input.IsButtonPressing(GameButton.CheatSpeedUp)) TriggerCheat<SpeedUpCheat>();
    }

    public void TriggerCheat<T>() where T : Cheat
    {
        foreach (var cheat in _cheats)
        {
            if (cheat is T t)
            {
                t.RunPayload(_game, Array.Empty<string>());
            }
        }
    }
}