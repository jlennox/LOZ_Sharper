global using z1.Common;
global using z1.Common.Data;
using z1.GUI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("z1.Tests")]

namespace z1;

// Sharper bugs (minor):
// * Can't hit the underworld cave people.
// * For one frame when leaving a cave, player appears in the wrong spot.
// * Objects should approximate the original update order. IE, bombs update after monsters.
// * Make AllTriforce an item requirement.
// * Octorok's in room NW of vanilla start seem to twitch when on the bottom row. They try to turn downward, can't,
//   and flicker back to the left immediately.
// * Warp caves appear before pushing rock. They also appear as cave, not stairs.
// * Set caves prior to scrolling, otherwise they only appear after scrolling has finished.
// * Fix potion shop.
// * Track the old spawn in time values and compare to the new. New ones seem longer.
// * Does not lift cellar items overhead.
// * Player spawns in wrong place when leaving dungeon. And does not do the correct animation/sound.

// Dungeon bugs:
// * Wizzrobe's wand attacks go into the walls.

// Know behavior changes:
// * You can spawn multiple armos at the same time at the level 6 entrance where they are next to each other.
//   In the normal game, only one will spawn at a time. This is because it early exits in the "is touching" loop
//   when it finds a match. It is not "only one can spawn in at a time." This might be an acknowledged behavior difference.

// Sharper:
// * Rectify MarginRight.
// * Make replay engine?
// * Figure out where to define what song is playing.
// * Refactor game-space to be (0,0), not (0,status bar height).
// * Make fire shooters and spike traps objects.

// Known tiled map issues:
// * Make recorder destinations spots on the map: ReadOnlySpan<int> teleportYs = [0x8D, 0xAD, 0x8D, 0x8D, 0xAD, 0x8D, 0xAD, 0x5D];
// * Fix _tempShutterRoomId

// Minor:
// * Vire keese seem to spawn too close together. -- but the assembly seems to check out.
// * Dungeon shutters draw closed when moving between rooms.
// * Dungeon walls without doors draw over swords/bombs.
// * Holding the button spams the sword. Is that a problem?
// * Swords and items picked up out of order should not downgrade.

// To check:
// * Boss noises when next to boss room.
// * Recorder does not work in caves/cellars/underworld.
// * If food attracts monsters.

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