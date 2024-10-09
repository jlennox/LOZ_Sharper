using System.Diagnostics;
using System.Reflection;
using ImGuiNET;
using Silk.NET.SDL;
using z1.Actors;
using z1.IO;
using z1.UI;

namespace z1.GUI;

internal static class GLWindowGui
{
    private static PropertyInfo GetProperty<T>(string name) => GetProperty<T, bool>(name);

    private static PropertyInfo GetProperty<T, TPropType>(string name)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new Exception($"Property {name} not found on {typeof(T).Name}");

        if (property.PropertyType != typeof(TPropType))
        {
            throw new Exception($"Property {name} on {typeof(T).Name} is not a {typeof(TPropType).Name}");
        }

        return property;
    }

    private static class GameEnhancementsProperties
    {
        private static PropertyInfo GetProperty(string name) => GetProperty<GameEnhancements>(name);

        public static readonly PropertyInfo AutoSave = GetProperty(nameof(GameEnhancements.AutoSave));
        public static readonly PropertyInfo ImprovedMenus = GetProperty(nameof(GameEnhancements.ImprovedMenus));
        public static readonly PropertyInfo ReduceFlashing = GetProperty(nameof(GameEnhancements.ReduceFlashing));
        public static readonly PropertyInfo RedCandleLightsDarkRooms = GetProperty(nameof(GameEnhancements.RedCandleLightsDarkRooms));
        public static readonly PropertyInfo DisableLowHealthWarning = GetProperty(nameof(GameEnhancements.DisableLowHealthWarning));
    }

    private static class AudioProperties
    {
        private static PropertyInfo GetProperty(string name) => GetProperty<AudioConfigurationPassthrough>(name);

        public static readonly PropertyInfo Mute = GetProperty(nameof(AudioConfigurationPassthrough.Mute));
        public static readonly PropertyInfo MuteMusic = GetProperty(nameof(AudioConfigurationPassthrough.MuteMusic));
    }

    private static class DebugInfoProperties
    {
        private static PropertyInfo GetProperty(string name) => GetProperty<DebugInfoConfiguration>(name);

        public static readonly PropertyInfo Enabled = GetProperty(nameof(DebugInfoConfiguration.Enabled));
        public static readonly PropertyInfo RoomId = GetProperty(nameof(DebugInfoConfiguration.RoomId));
        public static readonly PropertyInfo ActiveShots = GetProperty(nameof(DebugInfoConfiguration.ActiveShots));
    }

    private readonly struct AudioConfigurationPassthrough
    {
        private readonly AudioConfiguration _config;
        private readonly Game _game;

        public AudioConfigurationPassthrough(Game game)
        {
            _config = game.Configuration.Audio;
            _game = game;
        }

        public bool Mute
        {
            get => _config.Mute;
            set
            {
                _config.Mute = value;
                _game.Sound.SetMute(value);
            }
        }
        public bool MuteMusic
        {
            get => _config.MuteMusic;
            set
            {
                _config.MuteMusic = value;
                _game.Sound.SetMuteSongs(value);
            }
        }
        public int Volume
        {
            get => _config.Volume;
            set
            {
                _config.Volume = value;
                _game.Sound.SetVolume(value);
            }
        }
    }

    static void DrawMenuItem(string name, PropertyInfo property, object target)
    {
        // Not the most efficient way to do it, but this is rarely rendered.
        if (ImGui.MenuItem(name, null, (bool)property.GetValue(target)))
        {
            property.SetValue(target, !(bool)property.GetValue(target));
            SaveFolder.SaveConfiguration();
        }
    }

    public static void DrawMenu(GLWindow window)
    {
        var game = window.Game;

        if (ImGui.BeginMainMenuBar())
        {
            DrawFileMenu(game);
            DrawDisplayMenu(game, window);
            DrawAudioMenu(game);
            DrawEnhancementsMenu(game);
            DrawDebugMenu(game);
            DrawWarpMenu(game);
            DrawSpawnMenu(game);
            DrawPersonMenu(game);
            ImGui.EndMainMenuBar();
        }
    }

    private static void DrawFileMenu(Game game)
    {
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Save", game.World.Profile != null)) game.AutoSave(false);
            if (ImGui.MenuItem("Open save folder")) Directories.OpenSaveFolder();
            ImGui.Separator();
            if (ImGui.MenuItem("Exit Game")) Environment.Exit(0);
            ImGui.EndMenu();
        }
    }

    private static void DrawDisplayMenu(Game game, GLWindow window)
    {
        if (ImGui.BeginMenu("Display"))
        {
            if (ImGui.MenuItem("Fullscreen", null, window.IsFullScreen)) window.ToggleFullscreen();
            if (ImGui.BeginMenu("Debug Info"))
            {
                DrawMenuItem("Enabled", DebugInfoProperties.Enabled, game.Configuration.DebugInfo);
                DrawMenuItem("Room Id", DebugInfoProperties.RoomId, game.Configuration.DebugInfo);
                DrawMenuItem("Active Shots", DebugInfoProperties.ActiveShots, game.Configuration.DebugInfo);
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
    }

    private static void DrawAudioMenu(Game game)
    {
        if (ImGui.BeginMenu("Audio"))
        {
            var config = new AudioConfigurationPassthrough(game);
            DrawMenuItem("Mute", AudioProperties.Mute, config);
            DrawMenuItem("Mute Music", AudioProperties.MuteMusic, config);
            var volume = config.Volume;
            if (ImGui.SliderInt("Volume", ref volume, 0, 100))
            {
                config.Volume = volume;
                SaveFolder.SaveConfiguration();
            }
            ImGui.EndMenu();
        }
    }

    private static void DrawEnhancementsMenu(Game game)
    {
        if (ImGui.BeginMenu("Enhancements"))
        {
            DrawMenuItem("AutoSave", GameEnhancementsProperties.AutoSave, game.Enhancements);
            DrawMenuItem("Red Candle Auto-Lights Darkrooms", GameEnhancementsProperties.RedCandleLightsDarkRooms, game.Enhancements);
            DrawMenuItem("Improved Menus", GameEnhancementsProperties.ImprovedMenus, game.Enhancements);
            DrawMenuItem("Reduce Flashing", GameEnhancementsProperties.ReduceFlashing, game.Enhancements);
            DrawMenuItem("Disable Low Health Warning", GameEnhancementsProperties.DisableLowHealthWarning, game.Enhancements);

            var speed = game.Enhancements.TextSpeed;
            if (ImGui.SliderInt("Text Speed", ref speed,
                    GameEnhancements.TextSpeedMin,
                    GameEnhancements.TextSpeedMax))
            {
                game.Enhancements.TextSpeed = speed;
                SaveFolder.SaveConfiguration();
            }

            ImGui.EndMenu();
        }
    }

    private static void DrawSpawnMenu(Game game)
    {
#if !DEBUG
        return;
#endif
        static void Spawn(Game game, ObjType type)
        {
            const int x = Global.StdViewWidth / 2;
            const int y = Global.StdViewHeight / 2;
            try
            {
                Actor.AddFromType(type, game, x, y);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception " + e);
            }
        }

        if (ImGui.BeginMenu("SpawnOW"))
        {
            if (ImGui.MenuItem("Armos")) Spawn(game, ObjType.Armos);
            if (ImGui.MenuItem("FlyingGhini")) Spawn(game, ObjType.FlyingGhini);
            if (ImGui.MenuItem("Ghini")) Spawn(game, ObjType.Ghini);
            if (ImGui.MenuItem("Leever (Blue)")) Spawn(game, ObjType.BlueLeever);
            if (ImGui.MenuItem("Leever (Red)")) Spawn(game, ObjType.RedLeever);
            if (ImGui.MenuItem("Lynel (Blue)")) Spawn(game, ObjType.BlueLynel);
            if (ImGui.MenuItem("Lynel (Red)")) Spawn(game, ObjType.RedLynel);
            if (ImGui.MenuItem("Moblin (Blue)")) Spawn(game, ObjType.BlueMoblin);
            if (ImGui.MenuItem("Moblin (Red)")) Spawn(game, ObjType.RedMoblin);
            if (ImGui.BeginMenu("Octorock"))
            {
                if (ImGui.MenuItem("Octorock (Blue)")) Spawn(game, ObjType.BlueSlowOctorock);
                if (ImGui.MenuItem("Octorock (Red)")) Spawn(game, ObjType.RedSlowOctorock);
                if (ImGui.MenuItem("Octorock (Fast, Blue)")) Spawn(game, ObjType.BlueFastOctorock);
                if (ImGui.MenuItem("Octorock (Fast, Red)")) Spawn(game, ObjType.RedFastOctorock);
                ImGui.EndMenu();
            }
            if (ImGui.MenuItem("Peahat")) Spawn(game, ObjType.Peahat);
            if (ImGui.MenuItem("Tektite (Blue)")) Spawn(game, ObjType.BlueTektite);
            if (ImGui.MenuItem("Tektite (Red)")) Spawn(game, ObjType.RedTektite);
            if (ImGui.MenuItem("Zora")) Spawn(game, ObjType.Zora);
            ImGui.Separator();
            if (ImGui.MenuItem("Boulder")) Spawn(game, ObjType.Boulder);
            if (ImGui.MenuItem("Boulders")) Spawn(game, ObjType.Boulders);
            // if (ImGui.MenuItem("Merchant")) Spawn(game, ObjType.Merchant);
            // if (ImGui.MenuItem("Moblin (Friendly)")) Spawn(game, ObjType.FriendlyMoblin);
            // if (ImGui.MenuItem("OldMan")) Spawn(game, ObjType.OldMan);
            // if (ImGui.MenuItem("OldWoman")) Spawn(game, ObjType.OldWoman);
            if (ImGui.MenuItem("PondFairy")) Spawn(game, ObjType.PondFairy);
            if (ImGui.MenuItem("Whirlwind")) Spawn(game, ObjType.Whirlwind);

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("SpawnUW"))
        {
            if (ImGui.MenuItem("Bubble1")) Spawn(game, ObjType.Bubble1);
            if (ImGui.MenuItem("Bubble2")) Spawn(game, ObjType.Bubble2);
            if (ImGui.MenuItem("Bubble3")) Spawn(game, ObjType.Bubble3);
            if (ImGui.MenuItem("Darknut (Blue)")) Spawn(game, ObjType.BlueDarknut);
            if (ImGui.MenuItem("Darknut (Red)")) Spawn(game, ObjType.RedDarknut);
            if (ImGui.MenuItem("Gel")) Spawn(game, ObjType.Gel);
            if (ImGui.MenuItem("Gibdo")) Spawn(game, ObjType.Gibdo);
            if (ImGui.MenuItem("Goriya (Blue)")) Spawn(game, ObjType.BlueGoriya);
            if (ImGui.MenuItem("Goriya (Red)")) Spawn(game, ObjType.RedGoriya);
            if (ImGui.MenuItem("Keese (Black)")) Spawn(game, ObjType.BlackKeese);
            if (ImGui.MenuItem("Keese (Blue)")) Spawn(game, ObjType.BlueKeese);
            if (ImGui.MenuItem("Keese (Red)")) Spawn(game, ObjType.RedKeese);
            if (ImGui.MenuItem("LikeLike")) Spawn(game, ObjType.LikeLike);
            if (ImGui.MenuItem("PolsVoice")) Spawn(game, ObjType.PolsVoice);
            if (ImGui.MenuItem("Rope")) Spawn(game, ObjType.Rope);
            if (ImGui.MenuItem("Stalfos")) Spawn(game, ObjType.Stalfos);
            if (ImGui.MenuItem("Vire")) Spawn(game, ObjType.Vire);
            if (ImGui.MenuItem("Wallmaster")) Spawn(game, ObjType.Wallmaster);
            if (ImGui.MenuItem("Wizzrobe (Blue)")) Spawn(game, ObjType.BlueWizzrobe);
            if (ImGui.MenuItem("Wizzrobe (Red)")) Spawn(game, ObjType.RedWizzrobe);
            if (ImGui.MenuItem("Zol")) Spawn(game, ObjType.Zol);
            ImGui.Separator();
            if (ImGui.MenuItem("Grumble")) Spawn(game, ObjType.Grumble);
            if (ImGui.MenuItem("Patra1")) Spawn(game, ObjType.Patra1);
            if (ImGui.MenuItem("Patra2")) Spawn(game, ObjType.Patra2);
            if (ImGui.MenuItem("RupieStash")) Spawn(game, ObjType.RupieStash);
            if (ImGui.MenuItem("StandingFire")) Spawn(game, ObjType.StandingFire);
            if (ImGui.MenuItem("Trap")) Spawn(game, ObjType.Trap);
            if (ImGui.MenuItem("TrapSet4")) Spawn(game, ObjType.TrapSet4);
            ImGui.Separator();
            if (ImGui.MenuItem("Digdogger1")) Spawn(game, ObjType.Digdogger1);
            if (ImGui.MenuItem("Digdogger2")) Spawn(game, ObjType.Digdogger2);
            if (ImGui.MenuItem("Digdogger (Little)")) Spawn(game, ObjType.LittleDigdogger);
            if (ImGui.MenuItem("Lamnola (Blue)")) Spawn(game, ObjType.BlueLamnola);
            if (ImGui.MenuItem("Lamnola (Red)")) Spawn(game, ObjType.RedLamnola);
            if (ImGui.MenuItem("Manhandla")) Spawn(game, ObjType.Manhandla);
            if (ImGui.MenuItem("Moldorm")) Spawn(game, ObjType.Moldorm);
            ImGui.Separator();
            if (ImGui.MenuItem("Aquamentus")) Spawn(game, ObjType.Aquamentus);
            if (ImGui.MenuItem("Dodongo (one)")) Spawn(game, ObjType.OneDodongo);
            if (ImGui.MenuItem("Dodongos (three)")) Spawn(game, ObjType.ThreeDodongos);
            if (ImGui.MenuItem("Gleeok1")) Spawn(game, ObjType.Gleeok1);
            if (ImGui.MenuItem("Gleeok2")) Spawn(game, ObjType.Gleeok2);
            if (ImGui.MenuItem("Gleeok3")) Spawn(game, ObjType.Gleeok3);
            if (ImGui.MenuItem("Gleeok4")) Spawn(game, ObjType.Gleeok4);
            if (ImGui.MenuItem("Gohma (Blue)")) Spawn(game, ObjType.BlueGohma);
            if (ImGui.MenuItem("Gohma (Red)")) Spawn(game, ObjType.RedGohma);
            ImGui.Separator();
            if (ImGui.MenuItem("Ganon")) Spawn(game, ObjType.Ganon);
            if (ImGui.MenuItem("GuardFire")) Spawn(game, ObjType.GuardFire);
            if (ImGui.MenuItem("Princess")) Spawn(game, ObjType.Princess);

            ImGui.EndMenu();
        }
    }

    private static void DrawWarpMenu(Game game)
    {
#if !DEBUG
        return;
#endif

        static void Warp(Game game, int levelNumber)
        {
            game.World.KillAllObjects();
            game.World.GotoLoadLevel(levelNumber);
        }

        static void WarpOW(Game game, int x, int y)
        {
            game.World.KillAllObjects();
            game.World.LoadOverworldRoom(x, y);
        }

        if (ImGui.BeginMenu("Warp"))
        {
            if (ImGui.MenuItem("Level 1")) Warp(game, 1);
            if (ImGui.MenuItem("Level 1 (Entrance)")) WarpOW(game, 7, 3);
            if (ImGui.MenuItem("Level 2")) Warp(game, 2);
            if (ImGui.MenuItem("Level 3")) Warp(game, 3);
            if (ImGui.MenuItem("Level 4")) Warp(game, 4);
            if (ImGui.MenuItem("Level 5")) Warp(game, 5);
            if (ImGui.MenuItem("Level 6")) Warp(game, 6);
            if (ImGui.MenuItem("Level 7")) Warp(game, 7);
            if (ImGui.MenuItem("Level 8")) Warp(game, 8);
            if (ImGui.MenuItem("Level 9")) Warp(game, 9);

            if (game.World.IsOverworld())
            {
                ImGui.Separator();
                if (ImGui.MenuItem("Raft")) WarpOW(game, 5, 5);
                if (ImGui.MenuItem("Ghost")) WarpOW(game, 1, 2);
                if (ImGui.MenuItem("Level 6 Entrance")) WarpOW(game, 2, 2);
                if (ImGui.MenuItem("Armos / Bracelet")) WarpOW(game, 4, 2);
                if (ImGui.MenuItem("Ladder / Heart")) WarpOW(game, 15, 5);
                if (ImGui.MenuItem("Cave 12: Lost hills hint")) WarpOW(game, 0, 7);
                if (ImGui.MenuItem("Cave 15: Shop")) WarpOW(game, 6, 6);
            }

            ImGui.EndMenu();
        }
    }

    private static void DrawPersonMenu(Game game)
    {
#if !DEBUG
        return;
#endif

        static void SpawnCave(Game game, CaveId caveId)
        {
            game.World.DebugKillAllObjects();
            foreach (var (_, o) in game.World.CurrentRoomFlags.ObjectState)
            {
                o.HasInteracted = false;
                o.ItemGot = false;
            }
            game.World.DebugSpawnCave(caves => caves.First(t => t.CaveId == caveId));
        }

        static void SpawnPerson(Game game, PersonType type)
        {
            game.World.DebugKillAllObjects();
            foreach (var (_, o) in game.World.CurrentRoomFlags.ObjectState)
            {
                o.HasInteracted = false;
                o.ItemGot = false;
            }
            game.World.DebugSpawnCave(caves => caves.First(t => t.PersonType == type));
        }

        if (ImGui.BeginMenu("Person"))
        {
            if (ImGui.MenuItem("Cave 1: Wooden")) SpawnCave(game, CaveId.Cave1);
            if (ImGui.MenuItem("Cave 2: Take any")) SpawnCave(game, CaveId.Cave2);
            if (ImGui.MenuItem("Cave 3: White")) SpawnCave(game, CaveId.Cave3WhiteSword);
            if (ImGui.MenuItem("Cave 4: Magic")) SpawnCave(game, CaveId.Cave4MagicSword);
            if (ImGui.MenuItem("Cave 5: Warp")) SpawnCave(game, CaveId.Cave5Shortcut);
            if (ImGui.MenuItem("Cave 6: Hint")) SpawnCave(game, CaveId.Cave6);
            if (ImGui.MenuItem("Cave 7: Gamble")) SpawnCave(game, CaveId.Cave7);
            if (ImGui.MenuItem("Cave 8: Mugger")) SpawnCave(game, CaveId.Cave8);
            if (ImGui.MenuItem("Cave 9: Letter")) SpawnCave(game, CaveId.Cave9);
            if (ImGui.MenuItem("Cave 10: Hint")) SpawnCave(game, CaveId.Cave10);
            if (ImGui.MenuItem("Cave 11: Medicine")) SpawnCave(game, CaveId.Cave11MedicineShop);
            if (ImGui.MenuItem("Cave 12: Lost hills hint")) SpawnCave(game, CaveId.Cave12LostHillsHint);
            if (ImGui.MenuItem("Cave 13: Lost woods hint")) SpawnCave(game, CaveId.Cave13LostWoodsHint);
            if (ImGui.MenuItem("Cave 14: Shop")) SpawnCave(game, CaveId.Cave14);
            if (ImGui.MenuItem("Cave 15: Shop")) SpawnCave(game, CaveId.Cave15);
            if (ImGui.MenuItem("Cave 16: Shop")) SpawnCave(game, CaveId.Cave16);
            if (ImGui.MenuItem("Cave 17: Shop")) SpawnCave(game, CaveId.Cave17);
            if (ImGui.MenuItem("Cave 18: Secret 30")) SpawnCave(game, CaveId.Cave18);
            if (ImGui.MenuItem("Cave 19: Secret 100")) SpawnCave(game, CaveId.Cave19);
            if (ImGui.MenuItem("Cave 20: Secret 10")) SpawnCave(game, CaveId.Cave20);
            ImGui.Separator();
            if (ImGui.MenuItem("DoorRrepair")) SpawnPerson(game, PersonType.DoorRepair);
            if (ImGui.MenuItem("Grumble")) SpawnPerson(game, PersonType.Grumble);
            if (ImGui.MenuItem("Money or life")) SpawnPerson(game, PersonType.MoneyOrLife);
            if (ImGui.MenuItem("More bombs")) SpawnPerson(game, PersonType.MoreBombs);
            if (ImGui.MenuItem("Level 9")) SpawnPerson(game, PersonType.EnterLevel9);

            ImGui.EndMenu();
        }
    }


    private static void DrawDebugMenu(Game game)
    {
#if !DEBUG
        return;
#endif

        if (ImGui.BeginMenu("Debug"))
        {
            if (ImGui.MenuItem("Clear secrets")) game.GameCheats.TriggerCheat<GameCheats.ClearSecretsCheat>();
            if (ImGui.MenuItem("Clear history")) game.GameCheats.TriggerCheat<GameCheats.ClearHistoryCheat>();

            ImGui.EndMenu();
        }
    }
}