global using z1.Actors;
global using z1.Common;
global using z1.Common.Data;
global using z1.Randomizer;
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
// * Player spawns in wrong place when leaving dungeon. And does not do the correct animation/sound.
// * I think we need to reproduce the original game's draw order? IE, bombs should always be drawn over bad guys.
// * Do bad guys flicker when overlapping each other?
// * Can walk through key doors?!
// * Bomb capacity shop might be broken.
// * Sword beams when shot horizontally next to top row in dungeon hit wall.

// Sharper broken AIs:
// * Muldorm (sand worm) can get stuck, broken AI.
// * The flying monster killed by the recorder is broken.

// Overworld bugs:
// * Top right: Secret does not work.
// * Armos: Stairs under did not visually appear but functioned as if they were there.
// * Money: Goblins give wrong amount of money.

// Dungeon bugs:

// Know behavior changes:
// * You can spawn multiple armos at the same time at the level 6 entrance where they are next to each other.
//   In the normal game, only one will spawn at a time. This is because it early exits in the "is touching" loop
//   when it finds a match. It is not "only one can spawn in at a time." This might be an acknowledged behavior difference.

// Sharper:
// * Rectify MarginRight.
// * Figure out where to define what song is playing.
// * Refactor game-space to be (0,0), not (0,status bar height).
// * Make fire shooters and spike traps objects.
// * Items that upgrade each compass/map you find. Perhaps makes secret unmapped rooms clearer, or the types of room connections.

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
// * "public InteractionRequirements Requirements { get; set; }" should have AllEnemiesDefeated set for dungeon item
//   rooms I believe?
// * "DoorDirectionOrder" says it's in clockwise order but it isn't.
// * Boss noises when next to boss room.
// * Recorder does not work in caves/cellars/underworld.
// * If food attracts monsters.
// * Write code to interact with all objects in all rooms. This ensures there's no runtime exceptions in their reference hierarchy.

// TODO:
// * The dungeon old men use the same sprite as the overworld old men.

// Linting to add:
// * If world says "AllowWhirlwind" but does not have any recorder destinations, error.
// * Make sure all ExitLeft/ExitRight's exist.
// * Make sure no duplicate IDs.

// BUGS TO ADD:
// * Canana key?
// * Screen wrap?
// * Block clip?
// * World wrap?
// * Sword + wand?
// * Dungeon locked doors glitch: https://gamefaqs.gamespot.com/boards/563433-the-legend-of-zelda/63821853
// * Ensure you can sword beam dragon boss from right side door without taking hits.

// Enhancements:
// * Make an abstracted selectable menu. It takes in X/Y's, figures out what left/right/up/down does, perhaps has an onselect callback.
//   Perhaps make it use imgui so we get free mouse support?
// * Cheat: ctrl+arrows (or something) to screen scroll accordingly. Move link to valid square.

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
