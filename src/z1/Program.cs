using z1.GUI;

namespace z1;

// Milestone 1:
// * Textbox and ruppee sounds are wrong.
// * Screen sides need to be painted black.
// * Swords and items picked up out of order should not downgrade.
// * Wall masters got shield blocked, then boss sound kept going after getting brought back.
//   And then I kept walking north and got stuck.
// * Aquamentis blocks his own fireballs.
// * Some how between saves my max hearts got reset?

// Minor:
// * Vire keese seem to spawn too close together.
// * Dungeon shutters draw closed when moving between rooms.
// * Dungeon walls without doors draw over swords/bombs.
// * Holding the button spams the sword.

// To check:
// * Check `IsReoccuring` is proper.
// * Do bubbles work properly? (normal kind: yes)
// * Ensure magic shield works.
// * Boss noises when next to boss room.

// TODO:
// * The dungeon old men use the same sprite as the overworld old men.
// * Rewrite the RoomAttrs. Make them a single type and mimic the RoomFlags.
// * Refactor World enough so that Profile can not be null.
// * Move history out of World.
// * Try this for the palettes? https://skia.org/docs/user/sksl/
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