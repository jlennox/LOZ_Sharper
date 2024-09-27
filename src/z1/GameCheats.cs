using System.Collections.Immutable;
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
        private static readonly Regex _full = new(@"^w(\d{1,3})[x/](\d{1,3});$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^w(\d{0,3})[x/]?(\d{0,3});?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!int.TryParse(args[0], out var x) || !int.TryParse(args[1], out var y))
            {
                game.Toast("Invalid warp coordinates. " + string.Join(", ", args));
                return;
            }

            x = Math.Clamp(x, 0, World.WorldWidth - 1);
            y = Math.Clamp(y, 0, World.WorldHeight - 1);

            // Need to be sure all the objects are flushed.
            game.World.KillAllObjects();
            // JOE: TODO: MAP REWRITE game.World.LoadOverworldRoom(x, y);
            game.Toast($"Warping to room {x}x{y}");
        }
    }

    public sealed class ItemCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^idbehold(\w{1,10});$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^id?b?e?h?o?l?d?\w{0,10};?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!TryMatchFromEnum<ItemId>(args[0], out var itemId))
            {
                game.Toast("Invalid item type. " + args[0]);
                return;
            }

            Spawn(game, itemId);
        }

        public static void Spawn(Game game, ItemId itemId)
        {
            var slot = game.World.DebugSpawnItem(itemId);
            game.Toast($"Spawned {itemId} at slot {slot}");
        }
    }

    public sealed class DungeonWarpCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^w(w|\d{1,2});$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^w(w?|\d{0,2})?;?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            var target = args[0];
            if (!int.TryParse(target, out var levelNumber))
            {
                game.Toast("Invalid warp coordinates. " + string.Join(", ", args));
                return;
            }

            levelNumber = Math.Clamp(levelNumber, 1, 9);

            // Need to be sure all the objects are flushed.
            game.World.KillAllObjects();
            game.World.GotoLoadLevel(levelNumber);
            game.Toast($"Warping to dungeon {levelNumber}");
        }
    }

    public sealed class CaveSpawnCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^c([\w|\d]{1,10});$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^c([\w|\d]{0,10})?;?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            var target = args[0];
            CaveId? caveId = target.ToLowerInvariant() switch
            {
                "wood" => CaveId.Cave1,
                "any" => CaveId.Cave2,
                "white" => CaveId.Cave3WhiteSword,
                "magic" => CaveId.Cave4MagicSword,
                "sc" => CaveId.Cave5Shortcut,
                "gam" => CaveId.Cave7,
                "mug" => CaveId.Cave8,
                "med" => CaveId.Cave11MedicineShop,
                "hint" => CaveId.Cave12LostHillsHint,
                "hint2" => CaveId.Cave13LostWoodsHint,
                "shop" => CaveId.Cave15,
                "shop2" => CaveId.Cave14,
                "shop3" => CaveId.Cave16,
                "shop4" => CaveId.Cave17,
                _ => null,
            };

            if (int.TryParse(target, out var num))
            {
                caveId = CaveId.Cave1 + num - 1;
            }

            if (caveId == null)
            {
                game.Toast("Invalid cave. " + string.Join(", ", args));
                return;
            }

            // Need to be sure all the objects are flushed.
            game.World.KillAllObjects();
            game.World.MakeCaveObjects(caveId.Value);
        }
    }

    public sealed class SpawnCheat : RegexCheat
    {
        private static readonly Regex _full = new(@"^s(\w{1,15});$", DefaultRegexOptions);
        private static readonly Regex _partial = new(@"^s\w{0,15};?$", DefaultRegexOptions);

        protected override Regex FullMatch => _full;
        protected override Regex PartialMatch => _partial;

        public override void RunPayload(Game game, string[] args)
        {
            if (!TryMatchFromEnum<ObjType>(args[0], out var objType))
            {
                game.Toast("Invalid object type. " + string.Join(", ", args));
                return;
            }

            try
            {
                var obj = Actor.AddFromType(objType, game, 80, 80);
                game.Toast($"Spawned {objType}");
            }
            catch (ArgumentOutOfRangeException e) when (e.ParamName == "type")
            {
                game.Toast($"Invalid object type {objType}");
            }
        }
    }

    public sealed class GodModeCheat() : SingleWordCheat("iddqd", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            Game.Cheats.GodMode = !Game.Cheats.GodMode;
            game.Toast("God mode " + (Game.Cheats.GodMode ? "enabled" : "disabled"));
        }
    }

    public sealed class KillAllCheat() : SingleWordCheat("idka", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            game.World.KillAllObjects();
            game.Toast("Killed all objects.");
        }
    }

    public sealed class SpeedUpCheat() : SingleWordCheat("idsu", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            Game.Cheats.SpeedUp = !Game.Cheats.SpeedUp;
            game.Toast("Speed up " + (Game.Cheats.SpeedUp ? "enabled" : "disabled"));
        }
    }

    public sealed class ClipCheat() : SingleWordCheat("idclip", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            Game.Cheats.NoClip = !Game.Cheats.NoClip;
            game.Toast("Walk through walls " + (Game.Cheats.NoClip ? "enabled" : "disabled"));
        }
    }

    public sealed class FullHealthCheat() : SingleWordCheat("idfa", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            var profile = game.World.Profile;
            if (profile == null) return;
            var containers = profile.GetItem(ItemSlot.HeartContainers);
            profile.Hearts = PlayerProfile.GetMaxHeartsValue(containers);
            profile.Items[ItemSlot.Bombs] = profile.GetItem(ItemSlot.MaxBombs);
            game.Toast("Health refilled.");
        }
    }

    public sealed class ClearHistoryCheat() : SingleWordCheat("idclearhis", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            game.World.DebugClearHistory();
            game.Toast("History cleared.");
        }
    }

    public sealed class ClearSecretsCheat() : SingleWordCheat("idclearsec", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            foreach (var (_, flag) in game.World.Profile.RoomFlags)
            {
                flag.SecretState = false;
                flag.ShortcutState = false;
                flag.ItemState = false;
            }
            game.Toast("Secrets cleared.");
        }
    }

    public sealed class ItemsCheat() : SingleWordCheat("idkfa", true)
    {
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
            profile.Hearts = PlayerProfile.GetMaxHeartsValue(16);
            profile.SelectedItem = ItemSlot.Bombs;

            for (var i = 0; i < 10; ++i)
            {
                var items = profile.GetDungeonItems(i);
                items.Set(ItemId.Compass);
                items.Set(ItemId.Map);
            }

            game.Toast("All items added.");
        }
    }

    public sealed class MyPosCheat() : SingleWordCheat("idmypos", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            var room = game.World.CurrentRoom;
            var link = game.Link;
            // game.Toast($"Room: {room.X},{room.Y} ({room.X:X2},{room.Y:X2}) {game.World.CurRoomId}");
            game.Toast($"Link: {link.X},{link.Y} ({link.X:X2},{link.Y:X2})");
        }
    }

    public sealed class PosAllCheat() : SingleWordCheat("idpos", true)
    {
        public override void RunPayload(Game game, string[] args)
        {
            foreach (var actor in game.World.GetObjects())
            {
                game.Toast($"{actor.ObjType}: {actor.X},{actor.Y} ({actor.X:X2},{actor.Y:X2})");
            }
        }
    }

    private readonly Game _game;
    private readonly Input _input;

    private readonly ImmutableArray<Cheat> _cheats = [
        new OverworldWarpCheat(),
        new DungeonWarpCheat(),
        new CaveSpawnCheat(),
        new GodModeCheat(),
        new ItemCheat(),
        new ClearSecretsCheat(),
        new ClearHistoryCheat(),
        new ItemsCheat(),
        new MyPosCheat(),
        new PosAllCheat(),
        new KillAllCheat(),
        new SpeedUpCheat(),
        new ClipCheat(),
        new FullHealthCheat(),
        new SpawnCheat()
    ];

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
        if (_input.IsButtonPressing(GameButton.CheatBeHoldClock)) ItemCheat.Spawn(_game, ItemId.Clock);
        if (_input.IsButtonPressing(GameButton.CheatFullHealth)) TriggerCheat<FullHealthCheat>();
        if (_input.IsButtonPressing(GameButton.CheatGodMode)) TriggerCheat<GodModeCheat>();
        if (_input.IsButtonPressing(GameButton.CheatClip)) TriggerCheat<ClipCheat>();
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