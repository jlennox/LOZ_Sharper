using z1.GUI;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("z1.Tests")]

namespace z1;

// Milestone 1:
// * Textbox and rupee sounds are wrong.
// * Darknuts should make parry sound when shot with wave.
// * Coming out of stairs into spiral room spawns you in a bad spot: Example in 9, up 2, left 2.
//   I might have been stuck in the wall?
// * Red Ring Room Problems:
//   - w9; w7x1;
//   - You get stuck in dungeon push blocks super easy now. -- they appear to enter their "moving" state but don't move.
//     - This is Moving state issue: I can't push the push block in the room with the red ring. Everything is normal again on renter...
//   - Blue wizzrobes still faze through the floor/ceiling.
//   - Baubles are acting awful? They're moving through the pushblock and
//     they're able to shove link hard enough to go through it.
// * Name entry can go on forever, and can't be backed over. Add backspace support. It skips initial spaces?
// * Too many wallmasters can make it out. I think?
//   - And stopwatch doesn't stop them from appearing.
// * Do second quest skeletons fire swords?
// * Add cheat to unparalyze link incase someone gets stuck.
// * Make it not run too fast on higher refresh rates.

// Minor:
// * Vire keese seem to spawn too close together. -- but the assembly seems to check out.
// * Dungeon shutters draw closed when moving between rooms.
// * Dungeon walls without doors draw over swords/bombs.
// * Holding the button spams the sword.
// * Swords and items picked up out of order should not downgrade.

// To check:
// * Check `IsReoccuring` is proper.
// * Do bubbles work properly? (normal kind: yes)
// * Boss noises when next to boss room.

// TODO:
// * The dungeon old men use the same sprite as the overworld old men.
// * Rewrite the RoomAttrs. Make them a single type and mimic the RoomFlags.
// * Refactor World enough so that Profile can not be null.
// * Move history out of World.
// * Try this for the palettes? https://skia.org/docs/user/sksl/
// * File select dialog?
// * Add levels to DebugLog.
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