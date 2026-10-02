# Action Group

KSP-style action groups for **Spaceflight Simulator** (Steam / PC, 1.6.x).

Assign any parts of your craft to ten slots and fire them with the number keys `1`–`0` — the same
effect as clicking each part in flight.

* **Author:** S.S9
* **Mod ID:** `sfsactiongroupmod`
* **Version:** 0.9.2
* **License:** MIT
* **Game version:** 1.6.0.18 or newer
* **Requires:** [UITools](https://github.com/cucumber-sp/UITools) 1.1.6 **and**
  [Custom Save Data](https://github.com/AstroTheRabbit/Custom-Save-Data-SFS) 1.4 (both are separate
  mods that must be installed)

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

## Usage

**Build scene**

1. Press a number key `1`–`0` (or click a slot button in the window) to select that slot.
   Click empty space in the build scene to clear the selection again (clicking the selected slot button
   does the same).
2. Click parts on your craft to add them. Use the text field to rename the slot.
3. With no slot selected, part clicks behave like the vanilla game again.
4. Save the blueprint as usual; the groups are stored inside it.
5. Turning **Debug** on in Mods Settings prints everything (scene, slots, every entry and whether it
   part) to the game console — useful when reporting a problem.

**Flight**

* Press `1`–`0` to trigger that slot. This is identical to clicking every part of the group.

## Compatibility

Verified against the mods installed alongside it (Harmony patch targets were compared method by
method). The mod now uses seven small patches: the build-scene part click, box selection, the empty
click (deselect), build clear, the launch button, and the two rocket split/dock hooks.

| Mod | Interaction |
|---|---|
| Custom Save Data 1.4 | **required**; it owns the save/load hooks, this mod only listens to its events |
| UITools 1.1.6 | **required**; used for the closable window (with a fallback to the game's own window) |
| VanillaUpgrades | also postfixes `RocketManager.MergeRockets` — both postfixes run, no conflict |
| PartText | patches `BuildMenus.TryDoubleClick` and `Screen_Game.ProcessInput`; no overlap. Note it disables double-click selection by itself |
| BuildSettings / InfoOverload / PartEditor / DeltaV Calculator / SFS2 | no shared patch targets |
| ActionGroupsMod (the old, broken mod) | do not install both: it uses the same 1-0 keys |

No installed mod binds the number keys `1`-`0`, so the hotkeys do not collide.

## Known limitations

* The window text is English only for now.
* On loading a world with several rockets, the rocket that matches the most saved parts claims the
  group data.

## Publishing checklist

The build output is already a complete release (one DLL, two dependencies). To publish it:

1. Create a repository for `ActionGroup` (a git client is not required to build, only to publish).
2. Push the source, then create a release whose asset is `Action Group.dll` (the file name must match
   the game organises mods into `Mods\<name>\<name>.dll` by itself).
3. Announce it with this README, and mention the two required dependencies (UITools, Custom Save Data).
4. Optional auto-update: the loader supports it through UITools' `IUpdatable` interface. Fill in the
   release download URL in the mod's `UpdatableFiles` and rebuild — users then get new versions
   automatically. This is intentionally left out until the repository exists.

## Building from source

The project compiles with the Roslyn compiler that ships with Visual Studio 2022 — no .NET SDK and
no NuGet packages are needed. Run `build.cmd` (or `build.cmd -Install` to also copy the DLL into the
game's `Mods` folder). Paths to the game, and to the two dependency DLLs (UITools, Custom Save Data),
are configured at the top of `build.ps1`.

## Where the data is stored

Action groups are saved through the **Custom Save Data** mod, which stores mod data inside the game's
own save files:

* your blueprint (`...\Saving\Blueprints\<name>\Blueprint.txt`)
* your world (`...\Saving\Worlds\<world>\Persistent\Rockets.txt`)

Every group belongs to one rocket slot, so it follows that craft around. Players without this mod are
unaffected: the extra data is simply ignored.

The interface settings live in the mod's own folder, as plain JSON:
`Mods\Action Group\settings.txt`. Deleting that file restores every default.

## Changelog

**0.9.2 - renaming a slot in flight works, and the names follow a rocket switch**

**Bug 1: slots could not be renamed in the World scene.** The "empty group goes back to its default name"
rule added in 0.9.0 was **state based**: every 60 frames any group that had no parts and a non-default name
was renamed to `Group N`. So renaming a slot that was **already empty** was undone within a second (the
`action group 6 has no parts left ... so it is called "Group 6" again` lines in the log appear right after
each slot selection). It is now a **one-shot event**, fired only at the moment a group loses its last part:
* a stage separation that takes a group's parts away still renames it immediately (0.9.0 behaviour kept);
* a group that lost its last part to destruction is renamed once as well;
* a slot that was **already empty** - the one the player is renaming by hand - is left alone.
On top of that, `Refresh()` no longer writes the stored name back into the name field while that field is
**being typed into**: it used to restore the old name, and that write itself fires OnChange, which put the
old name back into the slot.

**Bug 2: switching between two rockets did not change the slot names.** The window always shows the groups of
the rocket being flown, but **nothing refreshed it when the player switched rockets**, so it kept showing the
names of the rocket that was left. `AgRuntime.Update` now calls `AgSlots.WatchCurrentRocket()` every frame:
as soon as the current rocket changes, the edit state ends and the window is refreshed (with Debug on it logs
`now flying "..."`), so the names and the part list follow the switch immediately.

**0.9.1 - the "Import" button now merges action groups too, plus a one-click name reset**

**What 0.9.0 missed:** the game has **two** ways to bring a blueprint into the build grid and only one of
them was hooked:

| Button | What it does internally | 0.9.0 | 0.9.1 |
| --- | --- | --- | --- |
| **Load** | `BuildState.LoadBlueprint` - **clears the build grid first**, then adds the blueprint | hooked | unchanged |
| **Import** | `HoldGrid.StartImport` - **does not clear anything**, the blueprint follows the cursor and is dropped next to the craft already there | ❌ not hooked | ✅ hooked |

`Import` is the game's own "merge two blueprints" button (Load deletes the previous craft). The imported
parts are a copy of the blueprint's own part list in the same order, and the saved entries store the index
of their part in that list, so the two line up directly; the part type is checked before anything is added,
and a mismatch is skipped and logged. The rules are the same as for copy/paste: groups on the same slot are
merged, and a slot that still has its default name adopts the name of the imported side.

* **New button `Reset all slot names`** (build scene only, right below `Clear all action groups`): puts the
  names of all ten slots back to `Group 1` … `Group 10` in one click. Nothing else is touched.

**0.9.0 - merging two blueprints merges their action groups, and a stage separation clears empty groups**

* **Merging two blueprints in the build scene now merges their action groups too.** Merging two blueprints
  is done with the blueprint menu's **Import** button (**not** Load - Load clears the build grid first):
  pick a blueprint, hit Import, and drop the parts it brings next to the craft. (Copy and paste works too:
  select, copy, load the other blueprint, paste.) The parts used to belong to no action group at
  all (the game knows nothing about our groups), so the groups of the blueprint that was brought in were
  lost. The mod now reads them either way and **puts those parts back into their slots** (the game's own
  "duplicate selection" works the same way):
  * two groups on the same slot become **one slot** holding both sets of parts (there are only ten slots,
    so they are merged by slot number);
  * the name: a slot that still carries its default name (`Group N`) adopts the name of the group the
    copied parts came from, otherwise the name already in the window is kept;
  * parts that cannot be set to a stage are still refused, exactly like adding a part by clicking it;
  * safety: the pasted parts are checked against the copied ones one by one (part type and offset - a
    paste only translates the cluster) before anything is added, so a stale or unrelated clipboard can
    never put a part into the wrong slot.
* **A stage separation refreshes the groups automatically.** The separation splits the parts between two
  rockets, so a group whose parts all went to the other rocket has nothing left; such a group now goes
  back to its **default name** (`Group N`) and its list is emptied. While flying the groups are also
  tidied up every 60 frames (which covers parts that were destroyed), and the window refreshes itself.

**0.8.3 - the indicator lights now follow the real part state (fixes a 0.8.2 bug)**

0.8.2 read `ActiveModule`, which is **not** an on/off state: it only turns its own GameObject on or off
from a boolean reference (a purely visual thing), most parts do not have one, and its reference is
often unconnected - so every light stayed red. The lights now read the parts' real switches, in this
order (the first module type that exists decides):

| Part module | The light reads "on" when |
| --- | --- |
| `EngineModule` (liquid engines) | `engineOn.Value` is true |
| `BoosterModule` (solid boosters) | `boosterPrimed.Value` is true |
| `ParachuteModule` (parachutes) | `state.Value > 0.05` (deploying or deployed) |
| `ActivationSequenceModule` (one-way steps, e.g. fairings) | `state.Value > 0.5` (past step 0) |
| `MultiStepToggleSequenceModule` (cyclic steps, e.g. landing legs, solar panels) | `state.Value > 0.5` (not at step 0, i.e. deployed) |
| `ActiveModule` (fallback) | `active.Value` (inverted when `invert` is set) |

* **Green as soon as one part of the group is on**, red while none of them is.
* Parts that have **no readable state at all** (separators, plain fuel tanks, RCS) cannot be shown, so
  a group made only of those shows **green for about 1.2 seconds after it fires successfully** and red
  the rest of the time - that way a key press always does something visible. Firing a part by hand
  (clicking it) flashes the same way.
* **Diagnostics:** with Debug on, every key press now also logs a line such as
  `lights: slot N = green/red (...) - K part(s), R with a state (engine/...), C on, flash F`, which
  says exactly why that light has the colour it has. The automatic dump on entering the World scene
  contains those lines for all ten slots as well.

**0.8.2 - flight indicator lights, a name length limit, and no more hotkeys while renaming**

* **Every slot got a small indicator light in the World scene**, sitting to the **right of the slot
  button, outside the row**. It is **green while any part of that group is switched on** and **red
  while none of them is** (an empty slot is red too). "Switched on" is read from the game's own part
  state (`ActiveModule.active`, inverted when `ActiveModule.invert` is set), so lights, solar panels
  and engines show it directly; **parts with no on/off state (stack separators, for instance) always
  read red**. The colour is refreshed every 8 frames (and painted once as soon as the window is
  built, so it never stays white), and the lights exist in the World scene only.
* **Slot names are limited to 23 characters** (for example `SsSeasLevelsEnginesssss`, which is
  exactly 23). Anything past that is cut off **while you type**, and **no "N characters left"
  counter appears** - the truncation is done by the mod, not by the field's built-in character
  limit, so the field shows no counter. Over-long names read from a blueprint or a save are cut down
  the same way.
* **The number keys no longer fire action groups while you rename a slot.** Typing a name such as
  `Engine1` used to trigger the group bound to that digit as you pressed it. Now, whenever the name
  field is **being typed into** (TextMeshPro's own focus flag, or one of the game's text boxes being
  open), the keys 1-0 are left alone completely.

**0.8.1 - hotkeys in flight no longer touch the window selection**

In flight a number key now only *triggers* its group: it no longer moves the `>>` marker and does not change the
list or the name field. That reverses the 0.7.2 "marker follows the hotkey" behaviour for the World scene only,
so triggering another group while editing one cannot interrupt the edit, and a key press can never turn the next
click on a part into "add to group". Pick a slot with the slot buttons and leave editing with
`Deselect (parts behave normally)`. The build scene still selects a slot with the number keys.

**0.8.0 - only stageable parts, and flight can select/edit/deselect groups**

* **Only parts that can be put in a stage are accepted**: before a part joins a group the mod runs the
  game's own test (`StagingDrawer.CanStagePart`: an `onPartUsed` listener exists and there is no
  `CannotStageModule`). Parts that fail it (a plain fuel tank, for instance) are refused with a message in
  the status line, because they could never react to a key.
* **Nothing is selected by default in flight**: entering the world scene clears the selection, so clicking a
  part still means "use that part".
* **Flight can edit too**: clicking a slot button puts the window into editing (status shows `Editing slot
  N (...)`), and clicking a part then adds it to that slot, exactly like in the build scene. The entry stores
  the part's index inside the rocket, so it is saved with the world.
  Pressing a number key (triggering a group) does not change the window selection or display at all, so a
  key press cannot turn the next click into "add to group" and cannot interrupt the slot being edited.
* **A `Deselect (parts behave normally)` button in flight only**: leaves editing and gives clicks back to the
  game.

**0.7.6 - the window follows its contents, the list no longer covers the name field, and a clear-all button**

* **The window is only as tall as it has to be**: with a short list it shrinks and the buttons follow the
  text right away (no large empty area), with a long list it grows up to `Window Height` and then the list
  scrolls. `Window Height` is therefore a maximum.
* **The first line of the list no longer covers the rename field**: the name field's prefab draws a little
  taller than the row it is given, so a fixed gap was added between the two.
* **A `Clear all action groups` button** at the bottom empties all ten slots at once (slot names are kept).

**0.7.5 - the parts list font is a fixed small size**

* The list turns TextMeshPro **auto sizing off** and pins the font to **14**, so a slot with two parts and a
  slot with forty look exactly the same (0.7.4 cured the giant text, but the size still followed how many
  lines were written - few lines meant auto sizing stretched them).
* How many lines fit is now derived from a **measured** line height (render the whole list, divide by its
  line count, so wrapped lines count too): no overflow and no jumping sizes. Text is left and top aligned.
* One constant changes the size: `AgWindow.PartsFontSize` (currently 14).

**0.7.4 - fixed the font size of the parts list, text is left aligned now**

* **The text no longer blows up.** This game's labels use TextMeshPro auto sizing, so a taller box means a
  bigger font. 0.7.3 sized the box from a measurement of the text, but that measurement sees the *large*
  pre-fitting font, so box, font and measurement grew together in a feedback loop - which is the giant
  text you saw. Nothing is measured any more: the box height comes from the window, the number of lines is
  derived from a fixed line height (22), and the lines written always fill the box, so auto sizing settles
  on a normal size.
* **The list is left aligned** (`TextAlignmentOptions.Left`) instead of centred.

**0.7.3 - fixed the parts list scrolling (regression in 0.7.2)**

The 0.7.2 scroll area (a clipped container with hand-positioned text) made the text disappear completely on
the user's machine - the anchor conventions of a nested container cannot be verified outside the game.
The list now writes only the lines that fit and the wheel/drag simply swaps in another slice of text, so
the text is always an ordinary label row and cannot vanish. The heading shows `(1-12 of 42, scroll)`, the
end of the list says how many lines are below it, and the wheel works anywhere over the window.

**0.7.2 - scrollable parts list, marker follows the hotkey, silent debug-off**

* **The parts list is now a scrollable area.** It takes the height the window has left over, its text is
  clipped and scrolled (mouse wheel, or drag inside the list), so a slot with 40+ parts neither overflows
  nor covers the buttons below; the heading says `(scroll to see all)` when there is more to see.
  Switching slots scrolls back to the top.
* (Superseded for the World scene by 0.8.1, which made hotkeys in flight trigger only.)
* **The `>>` marker follows the hotkey**, including in flight - pressing `3` now moves the marker to row 3
  and switches the parts list to that slot (in flight the key used to trigger without moving it).
* **Debug off is now completely silent.** Lines such as `Triggered slot N` and `world save: stored ...`
  used to print anyway; now not a single `[SFSAG]` line is printed unless it is a `WARNING`/`ERROR`.
  Turning Debug on prints one `debug output enabled` line as confirmation.

**0.7.1 - deselect by clicking empty space**

* **Clicking empty space in the build scene now clears the slot selection** (the old `Deselect`
  button): the status line shows `Selection off - parts behave normally.` and part clicks go back to
  the vanilla behaviour. This is a postfix on `BuildMenus.OnEmptyClick`; the game only routes clicks
  that are not over UI there, so clicking this mod's window does not deselect.
* **The number keys now only select.** Pressing the same key again no longer turns the mode off
  (that way a part shared by several groups cannot be deselected by accident). The slot buttons in the
  window still toggle, because the world scene has no empty space to click.

**0.7.0 - adaptive window, leaner UI**

* **The window now adapts to its contents.** The parts list gets as many lines as the window can show
  and the window grows when it has to (up to 1200 px), so a slot with many parts no longer squeezes the
  text; rows still follow the Window Width slider. `Window Height` is now the *smallest* height the
  window may have.
* **Removed two buttons**: `Deselect (parts behave normally)` and `Dump state to console (F1)`.
  Only `Clear selected slot` is left; deselection moved to clicking empty space in 0.7.1. For a state
  dump, turn Debug on.
* **The Misc page is now a single `Debug` switch**: off (default) keeps the console to milestones and
  warnings (scene loads, window creation, the blueprint/world save summaries, the `launch mapping:`
  summary, `Triggered slot N`); on restores the full detail plus the per-scene state dump. Everything
  else that used to be on that page is gone.
* **The GUI page keeps only the `Window` section**: width, height and opacity sliders. The
  "show parts list", "remember window position" and "reset window position" controls were removed
  (the list is always shown, the position always remembered).
* **Renamed to Action Group**, including the file: `Action Group.dll`, installed as
  `Mods\Action Group\Action Group.dll`.

**0.6.0 - adjustable interface**

The game's **Settings → Mods Settings** window (the one that also hosts the pages of Part Editor and
VanillaUpgrades) now has an entry with two pages (called `SFSActionGroupMod` back then, `Action Group` now):

* **GUI** - sliders for window width (300-900), window height (400-1100) and window opacity
  (0.20-1.00), toggles for "show the list of parts" and "remember window position", and a
  "reset window position" button.
* **Misc** - a toggle for the automatic per-scene state dump, plus buttons to dump the state now,
  print the current settings, and restore all defaults.

Changes apply immediately (an open window resizes, fades or hides its list on the spot) and are
written to `Mods\Action Group\settings.txt`. It is built on UITools: `UITools.ModSettings<T>`
handles the JSON file and saves on every change, `UITools.ConfigurationMenu.Add(...)` attaches the
pages to the settings window, and the controls come from the game's own `SFS.UI.ModGUI.Builder`.

**0.5.0 (verified in game)**

* **Fixed: a part that belongs to several action groups was only triggered by one of them.** The
  one-to-one assignment rule was applied across *all* slots, so as soon as a part was claimed by one
  group it was invisible to the others. The rule is now per slot: a part can be in as many groups as
  you like. Measured: one engine in slots 1, 2 and 3, pressing `1` and `2` seven times in alternation,
  every press reported `1 part(s), 1 reacted`.
* **Corrected a wrong assumption about coordinates.** In-game logs showed that the build grid and the
  launched rocket do *not* share a coordinate origin - the same engine is at `(-6.0, -0.5)` in the
  build grid and at `(-2.5, 0.0)` on the rocket, a translation of `(3.50, 0.50)` - so a position match
  never fired and only the blueprint part index worked. The index route now runs first (validated by
  part type), and the pairs it produces derive that translation, which then calibrates the position
  match used for anything left over. Measured on a 7-part craft: `frame calibration (3.50, 0.50) from
  5 pair(s), spread 0.00` and `5 attached (by index 5), 0 unmatched`.
* **The independent "derive the translation from the parent transforms" cross-check was measured and
  falsified.** The two parents sit at the same world position while the part positions still differ by
  `(3.50, 0.50)`, so the parent difference is *not* the frame difference (it was `(0.00, 0.00)`, off by
  3.54). That derivation has been removed from the code; the index pairs are now the only source.
* **Fixed a second silent-failure risk, this time in the restore paths.** Reverse engineering
  `BuildState.LoadBlueprint` showed that loading a blueprint *shifts the part positions*
  (`blueprint.offset`, plus `gridSize.centerX - blueprint.center` through
  `Part_Utility.OffsetPartPosition`), so matching a restored group by coordinates alone would have
  failed after every save/load cycle. Both restore paths (blueprint load and world load) now use the
  same layered rule as the launch mapping - part index first, validated by part type, then position -
  and report how many parts each pass resolved. Measured: `blueprint restore: 5/5 restored (by index 5,
  by position 0)` and, after a world save/reload, `restored action groups on rocket Rocket(Clone)
  (5 part(s), by index 5, by position 0, rocket has 7 part(s))`.
* If the index pairs contradict each other (`spread` above 2 units, which `SpawnBlueprint`'s two
  `AddParts` calls can cause on multi-layer craft) that is only *warned* about now: the index matches
  are kept because they are type-validated (the worst case is picking another part of the same type),
  while dropping them can lose the whole group - in game the index route is what the restore actually
  runs on. The new `restore:` log line per slot states how many entries were resolved by index and by
  position, the offset actually used, and the spread.
* Install note: `Loader.Initialize_EarlyLoad()` calls `MoveIndividualDLLs()` (copies loose
  `Mods\*.dll` into `Mods\<name>\<name>.dll`, overwriting, then deletes the loose file) *before*
  `LoadModList()`, which only scans folders. So dropping the DLL into `Mods\` does upgrade an existing
  folder copy - but the file is mapped while the game runs and cannot be overwritten then.
* **Guard added: an empty build grid no longer overwrites the groups stored in a blueprint.** IL shows
  `CustomSaveData`'s `BlueprintHelper.OnSave` is invoked from a postfix on **`BuildState.GetBlueprint()`**,
  i.e. *every* time the game asks for the current blueprint, not only when the player saves one.
  In game that produced an `action groups stored (0 part(s))` right after entering the build scene, with
  the grid still empty - if that empty set reaches the blueprint file it wipes the groups saved earlier.
  Now, when the grid holds no parts and the blueprint already carries data, the write is skipped and
  `blueprint save: the build grid is empty, keeping the N stor(ies) already in this blueprint` is logged.
  Clearing every group on a craft that still has parts is still stored as an empty set.
* Persistence checked against the Custom Save Data implementation: it wraps every `RocketSave` in a
  `CustomRocketSave` (a subclass carrying a `CustomData` dictionary) and hands that object back as the
  save result, so the data ends up inside `Rockets.txt` - the same mechanism covers the blueprint.
  Its serializer is a plain `new JsonSerializer()` without a contract resolver, so writing and reading
  use identical member names. `AddCustomData` is `Dictionary.Add`, which throws on a duplicate key, so
  the id is removed before writing.
* Fixed the launch path (action groups were lost when entering the world scene) - see the 0.4.1
  entries below, which are all part of this build.
* Added a **self-heal** in the world scene: if the rocket the player actually flies has no groups,
  the frozen launch snapshot is mapped onto it there, so the fix does not depend on the launch
  callback arriving at the right moment or on the right rocket object.
* Diagnostics: the periodic dump now states whether the launch path was entered at all, every
  binding is logged, and the created parts, the unmatched entries (with the nearest same-type part
  and its distance) and the per-rocket group ownership are printed.

**0.4.1**

* **Fixed: action groups were lost when launching.** Two causes, both in this mod: (1) when the
  build scene is torn down - which is what a launch does - every part reports its destruction and
  the launch snapshot was rebuilt from those parts, so it ended up empty; (2) the launch re-read the
  live build slots instead of the groups stored in the blueprint being launched.
  The snapshot is no longer rebuilt when parts are destroyed, and the launch now reads the groups
  from the blueprint itself (the route the original Action Groups mod used), with the last built
  snapshot and the live slots as fallbacks.
* Part mapping is now layered and ordered by trustworthiness: exact position first (both sides are
  `transform.localPosition`, so this is authoritative), then a loose position match reported as a
  warning, and only as a last resort the blueprint part index validated by part type - that one
  assumes the build grid order equals the blueprint order, so with two parts of the same type it
  could pick the wrong one, which is why it runs last and is reported.
  (Superseded in 0.5.0: the two sides turned out *not* to be the same frame, so the order was
  reversed - index first, position as the fallback. See the 0.5.0 entries above.)
* A **self-heal** runs shortly after the world scene is up: if the rocket the player actually flies
  has no action groups, the frozen launch snapshot is mapped onto it there. The launch mapping
  happens while the scene is still loading, so this is the late correction for the case where the
  groups did not land on the flown rocket. It only ever fills in a rocket that has no groups, and
  only with parts it can actually match, so unrelated craft are never touched.
* Every binding is logged (`entry "Hawk Engine" idx=1 @(-3.5, -0.5) -> created[7] ...`), so a wrong
  binding is visible instead of silent.
* Much richer diagnostics: the created parts, every unmatched entry together with the nearest
  part of the same type and its distance, and which rockets actually carry action groups.
* Repaired one source comment that an earlier scripted edit had mangled (sources are ASCII only now).
* The build snapshots are now **frozen when the Launch button is pressed**, because the scene is
  then torn down part by part and refreshing the snapshots from the dying parts would drop them.
  They are refreshed again as soon as the build scene is shown next time.

**0.4.0**

* Persistence moved to the **Custom Save Data** mod (required dependency). The previous hand-rolled
  JSON injection into the save files is gone, along with its backup files.
* Launch mapping now uses the exact `Part[]` that Custom Save Data hands over when a blueprint is
  launched, instead of looking for the rocket in the scene; groups are attached per rocket, so a
  staged craft no longer relies on guessing which rocket owns which part.
* World-save loading receives the rocket directly from Custom Save Data, so the previous
  "guess the rocket by matching the most parts" logic is gone.
* Because the mod no longer waits for the world scene to finish loading, the pending-work machinery
  (timeouts, frame counters) shrank to a single case: the build grid not being ready yet.
* Triggering a slot now reports how many parts actually reacted (`Rocket.UseParts` returns that),
  because a part without a use action (a plain tank, for example) cannot do anything.
* The window falls back to the game's own window builder if UITools' closable window cannot be
  created, so a UITools update cannot take the whole mod down.
* Added a `Dump state to console` button for troubleshooting. The mod also dumps the same state
  automatically once per scene (about 3-4 seconds after a scene loads), so a single test run
  produces a complete log.
* Window layout fixed: the ten slot rows plus the name field, the parts list and the three buttons
  need about 730 px, so the window is 800 px tall (it used to be 640 px, which pushed the buttons
  out of the window) and vertical scrolling is enabled as a safety net.

**0.3.0**

* Part matching is now **exact** instead of a guess. Reverse engineering showed that
  `PartSave.position` is `transform.localPosition` on both sides, so the build grid and the
  launched rocket use the *same* coordinate system. The previous "align the two averages"
  step was wrong whenever a group covered only part of the craft (a regression test shows it
  matching 0 out of 3 parts); it is gone. Only the craft rotation is still applied, exactly the
  way the game does it.
* Matching is now **one-to-one** (closest pair first, no part claimed twice), so stacked
  identical parts no longer steal each other's part.
* Tolerance tightened from 0.6 to 0.2.
* The matching rules moved into `PartMatcher`, a Unity-free source file that is compiled and
  executed by an offline test harness (16 assertions).

**0.2.0**

* Action groups are now stored in the blueprint and in the world save (new `sfsActionGroups`
  JSON property, versioned envelope).
* Per-rocket groups (a rocket carries its own groups once launched).
* Stage separation and docking keep the groups with the parts.
* Fixed a launch-ordering bug: the world scene is loaded asynchronously, so the launch snapshot
  is now taken immediately and mapped once the world scene is really active.
* Patches are applied class by class, so one failing patch no longer prevents the mod from loading.
* A pristine copy of a save file is kept as `*.sfsag-bak` the first time the mod rewrites it.

**0.1.0** — first test build: 10 slots, click-to-add, number-key triggering, window in both scenes.

## License

MIT — see `LICENSE`.

Not affiliated with Team Curiosity. Spaceflight Simulator is a trademark of its respective owners.
