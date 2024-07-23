using z1.GUI;

namespace z1;

// Bugs:
// * Traps move slow?
// * No spawn clouds?
// * Doors in dungeons and drawing priority.
// * Likelike's don't hold correctly.

// To check:
// * Check `IsReoccuring` is proper.
// * Consider refactoring monster projectile creation into generic CreateProjectile?
// * Do bubbles work properly?

// TODO:
// * SIMD the palettes?
// * Eliminate parameterless SpriteImage constructor?

// Enhancements:
// * Having the red candle causes dark rooms to auto fade in.
//   Blue candle does not because it can only be used once per room, this would be too strong of a buf as a weapon for it.
// * Reimplement mic kill of pols voice

// Monsters:
// * Manhandla:              W8, u1
// * Gleeok:                 W8, u4, l2
// * Cellar:                 W8, l2
// * Ruppee Boss:            W8, u5, l1
// * Crab:                   W8, u3, l1
// * Moldorm:                W7, r1
// * Lamnola:                W9: u2, l1
// * Patra (expand, type 2): W9: u5, r1

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        using var window = new GLWindow();
        Thread.Sleep(Timeout.Infinite);
    }
}