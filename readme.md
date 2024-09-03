About
===
This is a C# port of Aldo Nunez's excellent Legend of Zelda port based on their disassembly project.
- [C++ port](https://github.com/aldonunez/Loz_enhanced/tree/master)
- [Disassembly of NES code](https://github.com/aldonunez/zelda1-disassembly)

How to use:
===
- Place the American PRG0 Legend of Zelda ROM (`Legend of Zelda, The (U) (PRG0) [!].nes`) in the same directory as the .exe
- Run the .exe

Project goals:
===
- Implement a complete vanilla port in C#.
- Stay mostly bug compatible with the original version.
- Fork into an enhanced game with expanded mapping, monster, etc, capabilities, and build a new "second quest" off of this.

Current enhancements:
===
These are enabled by default.

- Red candle will automatically light dark rooms.
- "Select" switches secondary item, as does trigger L and trigger R when using a game controller.
- You can press up or down in the item select screen, as with most menu screens.
- You can type your name in the name entry screen.
- "Select" + "Start" brings up the "Save, Continue, Retry" screen.

Cheat codes:
===
Note that some cheats have `;` as a required terminator. Many can do what's not usually possible, ie, go out of bounds or spawn a specific monster where they should never be, causing game crashes.

- `iddqd`: God mode.
- `idkfa`: All items/hearts.
- `idfa`: Full health and bombs.
- `idclip`: Link can walk through walls/etc.
- `idsu`: Toggle speed up move. Link walks faster and animations are sped up.
- `wXxY;`: Warp to map tile of X/Y. eg, `w5x7;`
- `wX;`: Warps to dungeon X. eg, `w9;` warps to dungeon 9.
- `sNNNN;`: does a partial name match on `NNNN` and spawns said monster. String is not fixed length. eg, `sbluetek;` spawns a blue tektite.
- `idka`: Kills all objects on the screen.
- `idbeholdNNNN;`: replace NNNN with an item's name. eg, `idbeholdclock`
- `idmypos`: Show Link's map and room positions.
- `idpos`: Show the room position of all objects.
- `clearhis`: Clear room history.

Status:
===
- `Program.cs` contains my todo list.
- Still early, expect some wild refactorings/renamings/etc still to come.