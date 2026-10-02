## Features

* 10 fixed slots, bound to the number keys `1` `2` `3` `4` `5` `6` `7` `8` `9` `0` (top row only).
* Every slot can be renamed (23 characters maximum; anything longer is cut off as you type, with no
  character counter).
* In the build scene, clicking a part adds it to the selected slot. The list shows
  `#ordinal  part name  @(x, y)  ship#index` — text only, no part thumbnails, so identical
  parts can still be told apart.
* Box selection works too.
* In flight, pressing a number key uses every part of that slot once — exactly like clicking them.
* **In flight each slot has a green/red indicator light** on its right: green while something in the
  group is switched on, red otherwise (World scene only).
* The window works in **both** the build scene and the world scene, is draggable, and remembers its
  position (UITools).
* The window lays itself out to fit its contents (the parts list gets as many lines as the window can
  show, and the window grows when it has to), and its width/height/opacity are adjustable from the
  game's **Mods Settings** window. Settings are saved in `Mods\Action Group\settings.txt`; a
  **Debug** switch there decides whether the detailed console output is printed.
* Action groups are saved **with your blueprint** and **with your world save**, so they survive
  saving/loading and travelling to other players who also use the mod.
* Stage separation and docking are handled: parts keep their groups on the piece they end up on.
* No other dependencies than UITools.

## Installation

1. Install [UITools](https://github.com/cucumber-sp/UITools) and
   [Custom Save Data](https://github.com/AstroTheRabbit/Custom-Save-Data-SFS) if you do not have them.
2. Put `Action Group.dll` into the game's `Mods` folder
   (next to `Spaceflight Simulator.exe`, e.g. `...\Steam\steamapps\common\Spaceflight Simulator\Mods`).
   On startup the loader moves a loose DLL into `Mods\<name>\<name>.dll` (so it ends up as`r`n   `Mods\Action Group\Action Group.dll`), overwriting any copy already
   there (it does this *before* it scans the folders), so dropping a new version into `Mods\` is enough
   to upgrade. While the game is running that file is mapped and cannot be overwritten, so close the
   game before replacing it.
3. Start the game. "Action Group" should appear in the mod list, and the window should show up
   in the build scene.

## License

MIT — see `LICENSE`.

Not affiliated with Team Curiosity. Spaceflight Simulator is a trademark of its respective owners.
