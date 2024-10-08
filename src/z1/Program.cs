global using z1.Common;
global using z1.Common.Data;

using z1.GUI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("z1.Tests")]

namespace z1;

// Sharper bugs (minor):
// * Can't hit the underworld cave people.

// Sharper:
// * Rectify MarginRight.
// * Refactor out ActiveShots.
// * Make replay engine?
// * Figure out where to define what song is playing.
// * Refactor game-space to be (0,0), not (0,status bar height).
// * Make fire shooters and spike traps objects.

// Known tiled map issues:
// * Q1 level 7 recorder spot isn't marked as first quest only.
// * Somethings that shouldn't merge are still merging. Notably, overworld q6 entrance.
// * Make recorder destinations spots on the map: ReadOnlySpan<int> teleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];
// * Add IsEntrance for main overworld spot and dungeon spots. Add EntersTo and a way to add parameters.
// * Move over to custom properties instead of having multiple prefixed ones.
// * Fix _tempShutterRoomId
// * Need to pass along entryroom start x/y's
// * Codify the starting location used by UpdateUnfurl.

// Milestone 1:
// * Coming out of stairs into spiral room spawns you in a bad spot: Example in 9, up 2, left 2.
//   I might have been stuck in the wall?
// * Red Ring Room Problems:
//   - w9; w7x1;
//   - You get stuck in dungeon push blocks super easy now. -- they appear to enter their "moving" state but don't move.
//     - This is Moving state issue: I can't push the push block in the room with the red ring. Everything is normal again on renter...
//   - Blue wizzrobes still faze through the floor/ceiling.
//   - Baubles are acting awful? They're moving through the pushblock and
//     they're able to shove Player hard enough to go through it.
// * Name entry can go on forever, and can't be backed over. Add backspace support. It skips initial spaces?
// * Too many wallmasters can make it out. I think?
//   - And stopwatch doesn't stop them from appearing.
// * Do second quest skeletons fire swords?
// * Add cheat to unparalyze Player incase someone gets stuck.

// Minor:
// * Vire keese seem to spawn too close together. -- but the assembly seems to check out.
// * Dungeon shutters draw closed when moving between rooms.
// * Dungeon walls without doors draw over swords/bombs.
// * Holding the button spams the sword. Is that a problem?
// * Swords and items picked up out of order should not downgrade.

// To check:
// * Boss noises when next to boss room.

// TODO:
// * The dungeon old men use the same sprite as the overworld old men.
// * Rewrite the RoomAttrs. Make them a single type and mimic the RoomFlags.
// * Make a RoomID object.

// BUGS TO ADD:
// * Canana key?
// * Screen wrap?
// * Block clip?
// * World wrap?
// * Dungeon locked doors glitch: https://gamefaqs.gamespot.com/boards/563433-the-legend-of-zelda/63821853

// Enhancements:
// * Ten count indicator.
// * Having the red candle causes dark rooms to auto fade in.
//   Blue candle does not because it can only be used once per room, this would be too strong of a buf as a weapon for it.
// * Reimplement mic kill of pols voice
// * Make an abstracted selectable menu. It takes in X/Y's, figures out what left/right/up/down does, perhaps has an onselect callback.
//   Perhaps make it use imgui so we get free mouse support?
// * Make magic sword clink if wall is bombable?

// Monsters:
// * Manhandla:              W8, u1
// * Gleeok:                 W8, u4, l2
// * Cellar:                 W8, l2
// * Ruppee Boss:            W8, u5, l1
// * Crab:                   W8, u3, l1
// * Moldorm (fireworm):     W7, r1
// * Lamnola (centipede):    W9: u2, l1
// * Pols:                   W8: u2, r1
// * Patra (expand, type 2): W9: u5, r1
// * LikeLike:               W9: u2

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        using var window = new GLWindow();
        Thread.Sleep(Timeout.Infinite);
    }
}