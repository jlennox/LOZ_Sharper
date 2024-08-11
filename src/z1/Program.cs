using z1.GUI;

namespace z1;

// Milestone 1:
// * Music loops wrong.
// * No fairy sound.
// * Cave noises are wrong.
// * A save and replay locked up the game?
// * Screen sides need to be painted black.
// * Add text speed option.
// * Blue leevers are too slow.
// * All triggers press on startup.
// --
// * Blue Wizzrobes crash the game.
// * Red leevers can crash.
// * Dungeon walls without doors draw over swords/bombs.

// To check:
// * Check `IsReoccuring` is proper.
// * Consider refactoring monster projectile creation into generic CreateProjectile?
// * Do bubbles work properly?
// * Ensure magic shield works.

// TODO:
// * The dungeon old men use the same sprite as the overworld old men.
// * Getter/setter OWRoomFlags.
// * Refactor World enough so that Profile can not be null.
// * Move history out of World.
// * SIMD the palettes?
// * Try this for the palettes? https://skia.org/docs/user/sksl/
// * Eliminate parameterless SpriteImage constructor?
// * File select dialog?

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

// Monsters:
// * Manhandla:              W8, u1
// * Gleeok:                 W8, u4, l2
// * Cellar:                 W8, l2
// * Ruppee Boss:            W8, u5, l1
// * Crab:                   W8, u3, l1
// * Moldorm:                W7, r1
// * Lamnola:                W9: u2, l1
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