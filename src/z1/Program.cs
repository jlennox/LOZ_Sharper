global using z1.Common;
global using z1.Common.Data;

using z1.GUI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("z1.Tests")]

namespace z1;

// Sharper bugs (minor):
// * Can't hit the underworld cave people.
// * For one frame when leaving a cave, player appears in the wrong spot.
// * Compass does not work unless you have the map.
// * FoesDoor isn't a thing yet.

// Sharper:
// * Rectify MarginRight.
// * Refactor out ActiveShots.
// * Make replay engine?
// * Figure out where to define what song is playing.
// * Refactor game-space to be (0,0), not (0,status bar height).
// * Make fire shooters and spike traps objects.

// Known tiled map issues:
// * Q1 level 7 recorder spot isn't marked as first quest only -- there's an array in the original code for this.
// * Somethings that shouldn't merge are still merging. Notably, overworld q6 entrance.
// * Make recorder destinations spots on the map: ReadOnlySpan<int> teleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];
// * Fix _tempShutterRoomId
// * Need to pass along entryroom start x/y's (Add EntersTo?)

// Minor:
// * Vire keese seem to spawn too close together. -- but the assembly seems to check out.
// * Dungeon shutters draw closed when moving between rooms.
// * Dungeon walls without doors draw over swords/bombs.
// * Holding the button spams the sword. Is that a problem?
// * Swords and items picked up out of order should not downgrade.

// To check:
// * Boss noises when next to boss room.
// * Recorder does not work in caves/cellars/underworld.

// TODO:
// * The dungeon old men use the same sprite as the overworld old men.

// Linting to add:
// * If world says "AllowWhirlwind" but does not have any recorder destinations, error.

// BUGS TO ADD:
// * Canana key?
// * Screen wrap?
// * Block clip?
// * World wrap?
// * Dungeon locked doors glitch: https://gamefaqs.gamespot.com/boards/563433-the-legend-of-zelda/63821853

// Enhancements:
// * Make an abstracted selectable menu. It takes in X/Y's, figures out what left/right/up/down does, perhaps has an onselect callback.
//   Perhaps make it use imgui so we get free mouse support?

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