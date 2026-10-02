using System;
using System.Collections.Generic;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.World;
using UnityEngine;

namespace SFSActionGroupMod
{
    /// <summary>Which scene we are currently in. Driven by the loader's scene events, not by scene names.</summary>
    public static class AgScene
    {
        public static bool IsBuild { get; private set; }
        public static bool IsWorld { get; private set; }

        public static void OnBuildLoaded()
        {
            IsBuild = true;
            IsWorld = false;
            AgSlots.UnfreezeBuild();
            AgLog.Detail("build scene loaded");
            AgWindow.CreateInScene(true);
        }

        public static void OnBuildUnloaded()
        {
            // Do NOT cancel a pending launch mapping here: the build scene is unloaded as part of
            // the launch itself.
            IsBuild = false;
            AgWindow.Destroy();
        }

        public static void OnWorldLoaded()
        {
            IsWorld = true;
            IsBuild = false;
            AgLog.Detail("world scene loaded");

            // Nothing is selected by default in flight, so clicks keep the vanilla meaning.
            AgSlots.Deselect();
            AgWindow.CreateInScene(false);
        }

        public static void OnWorldUnloaded()
        {
            IsWorld = false;
            AgWindow.Destroy();
        }
    }

    /// <summary>One saved reference to a part. Coordinates are in the space they were captured in:
    /// build grid coordinates for blueprints, rocket local coordinates for world saves.</summary>
    [Serializable]
    public class SlotEntry
    {
        public string displayName;   // name shown to the player
        public string internalName;  // part type name, used to match the real part again
        public float x;
        public float y;
        public int shipIndex;        // part index inside the ship at capture time

        public string Format(int ordinal)
        {
            return "#" + ordinal + "  " + displayName +
                   "  @(" + x.ToString("0.0") + ", " + y.ToString("0.0") + ")" +
                   "  ship#" + shipIndex;
        }
    }

    /// <summary>One of the ten slots. Used for the build scene and, per rocket, in the world scene.</summary>
    public class Slot
    {
        public string name;
        public readonly List<Part> parts = new List<Part>();
        public readonly List<SlotEntry> entries = new List<SlotEntry>();

        public Slot(string name)
        {
            this.name = name;
        }

        public int Count
        {
            get
            {
                int count = 0;
                foreach (Part part in parts)
                {
                    if (part != null)
                        count++;
                }
                return count;
            }
        }
    }

    /// <summary>Serialisable form of one slot.</summary>
    [Serializable]
    public class SavedSlot
    {
        public string name;
        public List<SlotEntry> entries = new List<SlotEntry>();
    }

    /// <summary>Holds the action groups of one rocket in flight.</summary>
    public class AgRocketGroups : MonoBehaviour
    {
        public readonly List<Slot> groups = new List<Slot>();
    }

    public static class AgSlots
    {
        public const int Count = 10;

        /// <summary>Part positions are identical on both sides (transform.localPosition), so the
        /// tolerance only has to absorb floating point noise - 0.2 keeps neighbouring parts apart.</summary>
        private const float MatchTolerance = 0.2f;

        /// <summary>The ten slots of the ship currently in the build scene.</summary>
        public static readonly Slot[] Slots = CreateSlots();

        /// <summary>Index of the slot that part clicks are added to, or -1 when off.</summary>
        /// <summary>True while the mod itself is triggering parts, so the click patch does not mistake
        /// its own call for a click on a part.</summary>
        public static bool SuppressClickAdd;

        public static int Selected = -1;

        /// <summary>The slot the window shows (see the <see cref="Highlighted"/> property below).</summary>
        private static int highlighted = -1;

        public static string Status = "Click a slot, then click parts to add them.";

        /// <summary>Set when the player presses Launch: from then on the build snapshots are frozen,
        /// because the scene is being torn down part by part.</summary>
        private static bool buildFrozen;

        /// <summary>Called when the build scene is shown again.</summary>
        public static void UnfreezeBuild()
        {
            buildFrozen = false;
        }

        /// <summary>Called when the player presses Launch.</summary>
        public static void FreezeBuild()
        {
            buildFrozen = true;
            AgLog.Detail("launch requested - build snapshots frozen");
        }

        public static Slot[] CreateSlots()
        {
            Slot[] result = new Slot[Count];
            for (int i = 0; i < Count; i++)
                result[i] = new Slot("Group " + (i + 1));
            return result;
        }

        public static string KeyLabel(int index)
        {
            return index == 9 ? "0" : (index + 1).ToString();
        }

        /// <summary>Longest slot name the field accepts. Long names made the rows unreadable, and the
        /// game shows no counter, so the text is simply cut off once it reaches this many characters.</summary>
        public const int MaxNameLength = 23;

        /// <summary>Cuts a name down to <see cref="MaxNameLength"/> characters (and folds null to an
        /// empty string), so names from the field and from save files are always safe to display.</summary>
        public static string ClampName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            return name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength);
        }

        /// <summary>The name a slot has before the player renames it. Used when a group loses every
        /// part it had (a stage separating takes its parts to the other rocket).</summary>
        public static string DefaultName(int index)
        {
            return "Group " + (index + 1);
        }

        /// <summary>True when this slot has no live part left, so it does not control anything.</summary>
        private static bool IsEmpty(Slot slot)
        {
            if (slot == null)
                return true;

            foreach (Part part in slot.parts)
            {
                if (part != null)
                    return false;
            }

            return true;
        }

        /// <summary>Gives a slot that has just lost its last part its default name back ("Group N").
        /// Only ever called at the moment a group goes empty, never for a group that was already empty -
        /// otherwise the flight window would undo every rename of an empty slot once a second.</summary>
        private static bool RenameToDefault(Slot slot, int index)
        {
            if (slot == null)
                return false;

            string fallback = DefaultName(index);
            if (slot.name == fallback)
                return false;

            slot.name = fallback;
            AgLog.Detail("action group " + KeyLabel(index) + " has no parts left on this rocket, " +
                         "so it is called \"" + fallback + "\" again");
            return true;
        }

        /// <summary>Drops from these groups every part that no longer exists or no longer belongs to
        /// this rocket (a separated stage takes its parts with it). A group that has just lost its last
        /// part also goes back to its default name; a group that was already empty is left alone, so a
        /// slot the player renamed by hand keeps that name.
        /// Returns true when something changed.</summary>
        public static bool PruneGroups(AgRocketGroups groups, List<Part> rocketParts)
        {
            if (groups == null)
                return false;

            bool changed = false;
            for (int i = 0; i < groups.groups.Count; i++)
            {
                Slot slot = groups.groups[i];
                if (slot == null)
                    continue;

                bool hadParts = slot.parts.Count > 0;

                for (int p = slot.parts.Count - 1; p >= 0; p--)
                {
                    Part part = slot.parts[p];
                    if (part != null && rocketParts != null && rocketParts.Contains(part))
                        continue;

                    slot.parts.RemoveAt(p);
                    changed = true;
                }

                if (slot.parts.Count > 0)
                {
                    if (changed)
                        RebuildEntries(slot, rocketParts);
                    continue;
                }

                if (slot.entries.Count > 0)
                {
                    slot.entries.Clear();
                    changed = true;
                }

                // Only a group that lost its parts just now is renamed. An empty group that was already
                // empty keeps whatever name it has (the player may have just typed it).
                if (hadParts && RenameToDefault(slot, i))
                    changed = true;
            }

            return changed;
        }

        /// <summary>Refreshes the groups of the rocket being flown: parts that separated away or were
        /// destroyed are dropped, and a group with nothing left goes back to its default name.
        /// Called after a stage separation and once in a while while flying.</summary>
        public static bool PruneCurrentRocket()
        {
            if (AgScene.IsBuild)
                return false;

            Rocket rocket = CurrentRocket;
            if (rocket == null || rocket.partHolder == null)
                return false;

            AgRocketGroups groups = GetRocketGroups(rocket, false);
            if (groups == null)
                return false;

            bool changed = PruneGroups(groups, new List<Part>(rocket.partHolder.GetArray()));
            if (changed)
                AgWindow.Refresh();

            return changed;
        }

        /// <summary>The rocket whose groups the window is showing the last time we looked. Switching to
        /// another rocket has to be noticed, because the window shows whoever is being flown: its slot
        /// names and part lists have to be read again, and the slot that was being edited belongs to the
        /// rocket that was left.</summary>
        private static Rocket watchedRocket;

        /// <summary>Called every frame: notices that the player switched to another rocket. Without this
        /// the window keeps showing the names of the rocket that was left until something else happens to
        /// refresh it.</summary>
        public static void WatchCurrentRocket()
        {
            if (!AgScene.IsWorld)
            {
                watchedRocket = null;
                return;
            }

            Rocket rocket = CurrentRocket;
            if (rocket == watchedRocket)
                return;

            watchedRocket = rocket;
            if (rocket == null)
                return;

            Selected = -1; // the edit belonged to the rocket that was left
            AgLog.Detail("now flying \"" + rocket.name + "\", the window shows its action groups");
            AgWindow.Refresh();
        }

        // ------------------------------------------------------- flight indicator lights

        /// <summary>How long a group that has no readable on/off state stays green after being
        /// triggered (75 frames, about 1.2 seconds at 60 fps).</summary>
        public const int TriggerFlashFrames = 75;

        /// <summary>Frames left of the "it just fired" green flash, one counter per slot.</summary>
        private static readonly int[] flashFrames = new int[Count];

        /// <summary>Counts the green flashes down. Called once per frame from the window.</summary>
        public static void TickFlashes()
        {
            for (int i = 0; i < flashFrames.Length; i++)
            {
                if (flashFrames[i] > 0)
                    flashFrames[i] = flashFrames[i] - 1;
            }
        }

        /// <summary>Marks a group as "just fired". Only the groups whose parts have no readable
        /// on/off state use this (see <see cref="IsSlotLit"/>).</summary>
        public static void NoteTriggered(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= flashFrames.Length)
                return;

            flashFrames[slotIndex] = TriggerFlashFrames;
        }

        /// <summary>Marks every group that contains this part as "just fired" - used when the player
        /// clicks a part in flight, which fires it exactly like a hotkey does.</summary>
        public static void NotePartTriggered(Part part)
        {
            if (part == null || !AgScene.IsWorld)
                return;

            for (int i = 0; i < Count; i++)
            {
                Slot slot = ViewSlot(i);
                if (slot == null)
                    continue;

                if (slot.parts.Contains(part))
                    NoteTriggered(i);
            }
        }

        /// <summary>True when the flight light of this slot should be green.
        ///
        /// The light prefers the real state of the parts: an engine that is running, a booster that is
        /// lit, a deployed parachute and a part whose toggle sequence is not at its first step all count
        /// as "on". Parts that have no such state at all (fuel tanks, separators) cannot be read, so a
        /// group made only of those shows green for a moment after it fires and red the rest of the time -
        /// otherwise its light could never react to a key press at all.</summary>
        public static bool IsSlotLit(int slotIndex)
        {
            Slot slot = ViewSlot(slotIndex);
            if (slot == null)
                return false;

            bool readable = false;
            foreach (Part part in slot.parts)
            {
                if (part == null)
                    continue;

                bool on;
                string source;
                if (!TryReadPartState(part, out on, out source))
                    continue;

                readable = true;
                if (on)
                    return true;
            }

            if (readable)
                return false;

            return flashFrames[slotIndex] > 0;
        }

        /// <summary>Reads the on/off state of one part. Returns false when the part has no state that can
        /// be read at all, and then <paramref name="on"/> means nothing.
        /// <paramref name="source"/> names what was read, for the debug dump.</summary>
        private static bool TryReadPartState(Part part, out bool on, out string source)
        {
            on = false;
            source = null;

            try
            {
                EngineModule[] engines = part.GetModules<EngineModule>();
                if (engines != null && engines.Length > 0)
                {
                    source = "engine";
                    foreach (EngineModule engine in engines)
                    {
                        if (engine != null && engine.engineOn != null && engine.engineOn.Value)
                        {
                            on = true;
                            break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read an engine: " + e.Message);
            }

            try
            {
                BoosterModule[] boosters = part.GetModules<BoosterModule>();
                if (boosters != null && boosters.Length > 0)
                {
                    source = "booster";
                    foreach (BoosterModule booster in boosters)
                    {
                        if (booster != null && booster.boosterPrimed != null && booster.boosterPrimed.Value)
                        {
                            on = true;
                            break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read a booster: " + e.Message);
            }

            try
            {
                ParachuteModule[] chutes = part.GetModules<ParachuteModule>();
                if (chutes != null && chutes.Length > 0)
                {
                    source = "parachute";
                    foreach (ParachuteModule chute in chutes)
                    {
                        if (chute != null && chute.state != null && chute.state.Value > 0.05f)
                        {
                            on = true;
                            break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read a parachute: " + e.Message);
            }

            try
            {
                ActivationSequenceModule[] sequences = part.GetModules<ActivationSequenceModule>();
                if (sequences != null && sequences.Length > 0)
                {
                    source = "sequence";
                    foreach (ActivationSequenceModule sequence in sequences)
                    {
                        // This one only ever counts up, so anything past the first step is "used".
                        if (sequence != null && sequence.state != null && sequence.state.Value > 0.5f)
                        {
                            on = true;
                            break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read a sequence module: " + e.Message);
            }

            try
            {
                MultiStepToggleSequenceModule[] toggles = part.GetModules<MultiStepToggleSequenceModule>();
                if (toggles != null && toggles.Length > 0)
                {
                    source = "toggle";
                    foreach (MultiStepToggleSequenceModule toggle in toggles)
                    {
                        // The game walks the steps in a circle: step 0 fires first and counts as "on".
                        if (toggle != null && toggle.state != null && toggle.state.Value > 0.5f)
                        {
                            on = true;
                            break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read a toggle module: " + e.Message);
            }

            try
            {
                ActiveModule[] actives = part.GetModules<ActiveModule>();
                if (actives != null && actives.Length > 0)
                {
                    source = "active";
                    foreach (ActiveModule active in actives)
                    {
                        if (active == null || active.active == null)
                            continue;

                        bool state = active.active.Value;
                        if (active.invert)
                            state = !state;

                        if (state)
                        {
                            on = true;
                            break;
                        }
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read an active module: " + e.Message);
            }

            return false;
        }

        /// <summary>One line per slot describing what the flight light is showing and why. Only printed
        /// when Debug is on, because that is the only way to find out why a light does not react.</summary>
        public static void DumpLightState()
        {
            if (!AgScene.IsWorld)
            {
                AgLog.Detail("lights: the flight lights only exist in the world scene");
                return;
            }

            for (int i = 0; i < Count; i++)
                AgLog.Detail(DescribeLight(i));
        }

        /// <summary>What the light of one slot is showing, and which module that answer came from.</summary>
        public static string DescribeLight(int slotIndex)
        {
            Slot slot = ViewSlot(slotIndex);
            if (slot == null)
                return "lights: slot " + KeyLabel(slotIndex) + " - no such group on this rocket";

            int parts = 0;
            int readable = 0;
            int onCount = 0;
            string sources = "";
            foreach (Part part in slot.parts)
            {
                if (part == null)
                    continue;

                parts++;

                bool on;
                string source;
                if (!TryReadPartState(part, out on, out source))
                    continue;

                readable++;
                if (on)
                    onCount++;
                if (sources.IndexOf(source, StringComparison.Ordinal) < 0)
                    sources = sources.Length == 0 ? source : sources + "/" + source;
            }

            string verdict = onCount > 0
                ? "green"
                : (readable > 0 ? "red (all off)" : "red (nothing to read)");

            return "lights: slot " + KeyLabel(slotIndex) + " = " + verdict +
                   " - " + parts + " part(s), " + readable + " with a state (" +
                   (sources.Length == 0 ? "-" : sources) + "), " + onCount + " on, flash " +
                   flashFrames[slotIndex];
        }
        public static void Select(int index)
        {
            if (index >= 0 && index == Selected)
                index = -1; // clicking the selected slot button again turns the mode off

            Selected = index;
            highlighted = index;
            Status = index < 0
                ? "Selection off - parts behave normally."
                : "Editing slot " + KeyLabel(index) + " (" + Slots[index].name + ")";
            AgLog.Detail("selected slot " + index);
            AgWindow.Refresh();
        }

        /// <summary>Selects a slot without toggling it off, which is what the number keys do in the build
        /// scene: pressing a key twice keeps the slot selected. Clicking empty space clears it.</summary>
        public static void SelectOnly(int index)
        {
            if (index < 0 || index >= Count)
                return;

            Selected = index;
            highlighted = index;
            Status = "Editing slot " + KeyLabel(index) + " (" + Slots[index].name + ")";
            AgLog.Detail("selected slot " + index);
            AgWindow.Refresh();
        }

        /// <summary>Clears the selection, so clicking parts behaves like the vanilla game again.
        /// Called when the player clicks empty space in the build scene, when the world scene is
        /// entered (nothing is selected by default in flight) and by the Deselect button.</summary>
        public static void Deselect()
        {
            if (Selected < 0 && highlighted < 0)
                return;

            Selected = -1;
            highlighted = -1;
            Status = "Selection off - parts behave normally.";
            AgLog.Detail("selection cleared");
            AgWindow.Refresh();
        }

        /// <summary>The slot the window shows: its row carries the ">>" marker and its parts are listed.
        /// In flight the number keys move this one only (they trigger the group and the arrow follows the
        /// key) while <see cref="Selected"/>, which decides whether clicking a part adds it, is left
        /// alone - so a key press in flight can never turn a click into "add to group".</summary>
        public static int Highlighted
        {
            get { return highlighted; }
        }

        // ---------------------------------------------------------------- parts

        public static string GetDisplayName(Part part)
        {
            try
            {
                string display = part.GetDisplayName();
                if (!string.IsNullOrEmpty(display))
                    return display;
            }
            catch (Exception e)
            {
                AgLog.Warn("GetDisplayName failed: " + e.Message);
            }
            return part.Name;
        }

        public static SlotEntry MakeEntry(Part part)
        {
            return MakeEntry(part, GetBuildParts());
        }

        /// <summary>Capture a part together with its index inside the list it belongs to.</summary>
        public static SlotEntry MakeEntry(Part part, List<Part> within)
        {
            SlotEntry entry = new SlotEntry();
            entry.displayName = GetDisplayName(part);
            entry.internalName = part.Name;

            try
            {
                entry.x = part.Position.x;
                entry.y = part.Position.y;
            }
            catch (Exception e)
            {
                // A part that is being torn down with the build scene can still be referenced here.
                AgLog.Warn("could not read the position of " + entry.internalName + ": " + e.Message);
            }

            entry.shipIndex = within != null ? within.IndexOf(part) : -1;
            return entry;
        }

        public static List<Part> GetBuildParts()
        {
            try
            {
                if (BuildState.main != null && BuildState.main.buildGrid != null &&
                    BuildState.main.buildGrid.activeGrid != null &&
                    BuildState.main.buildGrid.activeGrid.partsHolder != null)
                {
                    return BuildState.main.buildGrid.activeGrid.partsHolder.parts;
                }
            }
            catch (Exception e)
            {
                AgLog.Warn("GetBuildParts failed: " + e.Message);
            }
            return null;
        }

        /// <summary>True while the build grid still holds at least one part. Lets the save hook tell
        /// "the player cleared every group" (grid intact, store an empty set) apart from "the grid is
        /// empty, nothing to store" (leave whatever the blueprint already carries untouched).</summary>
        public static bool HasLiveBuildParts()
        {
            List<Part> parts = GetBuildParts();
            if (parts == null)
                return false;

            foreach (Part part in parts)
            {
                if (part != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Layered matching used by the restore paths (blueprint load, world load). Same shape as the
        /// launch mapping:
        ///   1. the part index captured with the entry, validated by part type,
        ///   2. the offset implied by those index pairs, used to calibrate the position match
        ///      (tolerance 0.2, then 1.0),
        ///   3. as a last resort for what is still unmatched, the same position match without the
        ///      calibration, in case the index pairs were bad and the estimate is wrong.
        ///
        /// Both parts of that are needed here: BuildState.LoadBlueprint *shifts* the part positions
        /// (blueprint.offset plus gridSize.centerX - blueprint.center via Part_Utility.OffsetPartPosition,
        /// or Part_Utility.CenterParts), and BuildState.SpawnBlueprint splits the created parts into two
        /// AddParts calls, so the grid list order only matches the blueprint order for crafts that are
        /// all on one layer. The index pairs that are consistent give us the shift; if they disagree
        /// wildly that is warned about, and the shift they imply is then only an estimate.
        /// </summary>
        public static List<Part> AssignEntries(List<SlotEntry> entries, Part[] parts, Vector2 positionOffset,
                                               out int byIndex, out int byPosition)
        {
            List<Part> result = new List<Part>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
                result.Add(null);

            byIndex = 0;
            byPosition = 0;

            if (entries.Count == 0 || parts == null || parts.Length == 0)
                return result;

            bool[] used = new bool[parts.Length];
            List<Vector2> implied = new List<Vector2>();

            for (int e = 0; e < entries.Count; e++)
            {
                int index = entries[e].shipIndex;
                if (index < 0 || index >= parts.Length || used[index])
                    continue;

                Part candidate = parts[index];
                if (candidate == null || candidate.Name != entries[e].internalName)
                    continue;

                result[e] = candidate;
                used[index] = true;
                byIndex++;

                Vector2 position = candidate.Position;
                implied.Add(new Vector2(position.x - entries[e].x - positionOffset.x,
                                        position.y - entries[e].y - positionOffset.y));
            }

            Vector2 derived = Vector2.zero;
            float spread = 0f;

            if (implied.Count > 0)
            {
                foreach (Vector2 value in implied)
                    derived += value;
                derived /= implied.Count;

                foreach (Vector2 value in implied)
                {
                    float distance = Vector2.Distance(value, derived);
                    if (distance > spread)
                        spread = distance;
                }
            }

            // A large spread means the index pairs contradict each other (a reordered list, which
            // BuildState.SpawnBlueprint's two AddParts calls can produce on multi-layer craft). The
            // matches themselves are kept anyway: they are validated by part type, so the worst case
            // is that a same-type part is picked, which is far better than dropping the whole group -
            // in game the index route is what actually carries the restore. The spread is reported
            // so a wrong binding is visible, and the position passes below still cover leftovers.
            if (byIndex > 1 && spread > 2f)
            {
                AgLog.Warn("restore: the " + byIndex + " index match(es) disagree by up to " +
                           spread.ToString("0.00") +
                           " units, the offset below is only an estimate");
            }

            List<Vector2> targets = new List<Vector2>(entries.Count);
            foreach (SlotEntry entry in entries)
                targets.Add(new Vector2(entry.x, entry.y));

            Vector2 offset = positionOffset + derived;

            byPosition = AssignRemaining(entries, targets, parts, result, MatchTolerance, offset);
            byPosition += AssignRemaining(entries, targets, parts, result, 1f, offset);

            // Last resort, only for entries the calibrated passes could not place: the calibration
            // comes from the index pairs and would be wrong on its own if those pairs were bad, so
            // the uncorrected offset gets one loose attempt before the entry is reported unmatched.
            if (derived != Vector2.zero)
                byPosition += AssignRemaining(entries, targets, parts, result, 1f, positionOffset);

            AgLog.Detail("restore: " + entries.Count + " entr(ies) -> " + byIndex + " by index, " +
                        byPosition + " by position, offset (" + offset.x.ToString("0.00") + ", " +
                        offset.y.ToString("0.00") + "), spread " + spread.ToString("0.00"));

            return result;
        }

        /// <summary>True when the game itself would let this part be put in a stage, i.e. the part has
        /// something to trigger and is not marked as unstagedble. It is the same test the game uses in
        /// StagingDrawer.CanStagePart, so a part the player cannot put in a stage is also never added to
        /// an action group - it could not react to anything.</summary>
        public static bool CanStage(Part part)
        {
            if (part == null)
                return false;

            try
            {
                if (part.onPartUsed == null || part.onPartUsed.GetPersistentEventCount() == 0)
                    return false;

                return !part.HasModule<SFS.Parts.Modules.CannotStageModule>();
            }
            catch (Exception e)
            {
                AgLog.Detail("could not test whether a part can be staged: " + e.Message);
                return false;
            }
        }

        /// <summary>Adds a part to a slot of the rocket being flown. In flight there is no build grid, so
        /// the entry stores the part's index inside the rocket - which is exactly what the world save and
        /// the restore path use.</summary>
        public static bool AddPartInWorld(int slotIndex, Part part)
        {
            if (slotIndex < 0 || slotIndex >= Count)
                return false;

            if (!CanStage(part))
            {
                Status = "That part cannot be set to a stage, so it cannot react to a key.";
                AgWindow.Refresh();
                return false;
            }

            Rocket rocket = CurrentRocket;
            if (rocket == null)
            {
                Status = "No rocket to add to.";
                AgWindow.Refresh();
                return false;
            }

            AgRocketGroups groups = EnsureGroups(rocket, null);
            if (groups == null || slotIndex >= groups.groups.Count)
            {
                Status = "This rocket has no action groups.";
                AgWindow.Refresh();
                return false;
            }

            Slot slot = groups.groups[slotIndex];
            if (slot.parts.Contains(part))
            {
                Status = GetDisplayName(part) + " is already in slot " + KeyLabel(slotIndex) + ".";
                AgWindow.Refresh();
                return false;
            }

            List<Part> inRocket = new List<Part>(rocket.partHolder.GetArray());
            slot.parts.Add(part);
            slot.entries.Add(MakeEntry(part, inRocket));
            Status = "Added " + GetDisplayName(part) + " to slot " + KeyLabel(slotIndex) + ".";
            AgLog.Detail(Status);
            AgWindow.Refresh();
            return true;
        }

        public static void AddPart(int slotIndex, Part part)
        {
            if (slotIndex < 0 || slotIndex >= Count || part == null)
                return;

            if (!CanStage(part))
            {
                Status = "That part cannot be set to a stage, so it cannot react to a key.";
                AgWindow.Refresh();
                return;
            }

            Slot slot = Slots[slotIndex];
            if (slot.parts.Contains(part))
            {
                Status = GetDisplayName(part) + " is already in slot " + KeyLabel(slotIndex) + ".";
                AgWindow.Refresh();
                return;
            }

            slot.parts.Add(part);
            part.aboutToDestroy -= OnPartDestroyed;
            part.aboutToDestroy += OnPartDestroyed;
            RebuildEntries(slot);

            SlotEntry entry = MakeEntry(part);
            Status = "Added " + entry.displayName + " to slot " + KeyLabel(slotIndex) + ".";
            AgLog.Detail(Status);
            AgWindow.Refresh();
        }

        /// <summary>Puts a pasted copy of a part into the same action groups the part it was copied from
        /// was in (see <see cref="AgClipboard"/>). A group that still carries its default name adopts the
        /// name of the group the copy came from, which is what merges two blueprints' group names.
        /// Returns true when the part was added somewhere.</summary>
        public static bool AddPastedPart(Part part, int[] slotIndices, string[] slotNames)
        {
            if (part == null || slotIndices == null || !AgScene.IsBuild)
                return false;

            if (!CanStage(part))
                return false; // the same rule as adding a part by clicking it

            bool added = false;
            for (int s = 0; s < slotIndices.Length; s++)
            {
                int index = slotIndices[s];
                if (index < 0 || index >= Count)
                    continue;

                Slot slot = Slots[index];
                if (slot == null || slot.parts.Contains(part))
                    continue;

                slot.parts.Add(part);
                part.aboutToDestroy -= OnPartDestroyed;
                part.aboutToDestroy += OnPartDestroyed;
                added = true;

                string name = slotNames != null && s < slotNames.Length ? slotNames[s] : null;
                if (!string.IsNullOrEmpty(name) && slot.name == DefaultName(index) &&
                    name != DefaultName(index))
                {
                    slot.name = ClampName(name);
                    AgLog.Detail("action group " + KeyLabel(index) + " took the name \"" + slot.name +
                                 "\" from the pasted parts");
                }

                RebuildEntries(slot);
            }

            return added;
        }

        /// <summary>Puts every slot name of the build grid back to the default one ("Group N"), which is
        /// the "one click" version of renaming them all by hand.</summary>
        public static void ResetAllNames()
        {
            int renamed = 0;
            for (int i = 0; i < Count; i++)
            {
                Slot slot = Slots[i];
                if (slot == null || slot.name == DefaultName(i))
                    continue;

                slot.name = DefaultName(i);
                renamed++;
            }

            Status = renamed == 0
                ? "Every slot already has its default name."
                : "Reset " + renamed + " slot name(s) to default.";
            AgLog.Detail(Status);
            AgWindow.Refresh();
        }

        public static void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Count)
                return;

            Slot slot = Slots[slotIndex];
            foreach (Part part in slot.parts)
            {
                if (part != null)
                    part.aboutToDestroy -= OnPartDestroyed;
            }
            slot.parts.Clear();
            slot.entries.Clear();
            Status = "Cleared slot " + KeyLabel(slotIndex) + ".";
            AgWindow.Refresh();
        }

        /// <summary>Empties every slot at once (the "clear all action groups" button). In the build
        /// scene it empties the build slots, in flight the slots of the rocket being flown; the slot
        /// names are kept, they are labels rather than content.</summary>
        public static void ClearAll()
        {
            int parts = 0;
            int groups = 0;

            for (int i = 0; i < Count; i++)
            {
                Slot slot = ViewSlot(i);
                if (slot == null)
                    continue;

                if (slot.parts.Count > 0 || slot.entries.Count > 0)
                    groups++;

                parts += slot.Count;

                if (!AgScene.IsWorld)
                {
                    foreach (Part part in slot.parts)
                    {
                        if (part != null)
                            part.aboutToDestroy -= OnPartDestroyed;
                    }
                }

                slot.parts.Clear();
                slot.entries.Clear();
            }

            Status = "Cleared all action groups (" + parts + " part(s) in " + groups + " group(s)).";
            AgLog.Detail(Status);
            AgWindow.Refresh();
        }

        private static void OnPartDestroyed(Part part)
        {
            bool changed = false;

            for (int i = 0; i < Count; i++)
            {
                if (Slots[i].parts.Remove(part))
                    changed = true;
            }

            // The snapshot (Slot.entries) is deliberately NOT rebuilt here. When the build scene is
            // torn down - which is exactly what happens on launch - every part reports its destruction,
            // and rebuilding would empty the snapshot before the launch could use it. A stale entry is
            // harmless: it simply fails to match any created part and is skipped.
            if (changed)
            {
                Status = "A deleted part was removed from its action group.";
                AgWindow.Refresh();
            }
        }

        public static void ResetBuildState()
        {
            for (int i = 0; i < Count; i++)
            {
                Slots[i].parts.Clear();
                Slots[i].entries.Clear();
            }
            Selected = -1;
            highlighted = -1;
            Status = "Build cleared.";
            AgWindow.Refresh();
        }

        // ---------------------------------------------------------------- rockets

        public static Rocket CurrentRocket
        {
            get
            {
                try
                {
                    PlayerController controller = PlayerController.main;
                    if (controller == null)
                        return null;
                    return controller.player.Value as Rocket;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static AgRocketGroups GetRocketGroups(Rocket rocket, bool create)
        {
            if (rocket == null)
                return null;

            AgRocketGroups groups = rocket.GetComponent<AgRocketGroups>();
            if (groups == null && create)
                groups = rocket.gameObject.AddComponent<AgRocketGroups>();

            return groups;
        }

        /// <summary>The slot to show in the window: build slots in the editor, rocket groups in flight.</summary>
        public static Slot ViewSlot(int index)
        {
            if (index < 0 || index >= Count)
                return null;

            if (AgScene.IsWorld)
            {
                AgRocketGroups groups = GetRocketGroups(CurrentRocket, false);
                if (groups == null || index >= groups.groups.Count)
                    return null;
                return groups.groups[index];
            }

            return Slots[index];
        }

        public static string ViewName(int index)
        {
            Slot slot = ViewSlot(index);
            return slot != null ? slot.name : "Group " + (index + 1);
        }

        /// <summary>Trigger a slot: identical to clicking every part of the group in the world scene.</summary>
        public static void Activate(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Count)
                return;

            // A key press in flight triggers the group and nothing else: it does not touch the slot the
            // window shows, so it can neither disturb the slot being edited nor turn the next click on a
            // part into "add to group". The window's slot is chosen by clicking a slot button (and left
            // again with the Deselect button).

            Rocket rocket = CurrentRocket;
            AgRocketGroups groups = GetRocketGroups(rocket, false);
            if (rocket == null || groups == null || slotIndex >= groups.groups.Count)
            {
                Status = "No action groups on this rocket.";
                AgWindow.Refresh();
                return;
            }

            Slot slot = groups.groups[slotIndex];
            List<Part> usable = new List<Part>();
            foreach (Part part in slot.parts)
            {
                if (part != null && part.Rocket == rocket)
                    usable.Add(part);
            }

            if (usable.Count == 0)
            {
                Status = "Slot " + KeyLabel(slotIndex) + " has no parts on this rocket.";
                AgWindow.Refresh();
                return;
            }

            try
            {
                ValueTuple<Part, SFS.Parts.Modules.PolygonData>[] regions =
                    new ValueTuple<Part, SFS.Parts.Modules.PolygonData>[usable.Count];
                for (int i = 0; i < usable.Count; i++)
                    regions[i] = new ValueTuple<Part, SFS.Parts.Modules.PolygonData>(usable[i], null);

                // UseParts is static; null polygons means "use the whole part", like a normal click.
                // It fires each part's onPartUsed UnityEvent and reports whether that did anything.
                UsePartData[] results;

                SuppressClickAdd = true;
                try
                {
                    UsePartData[] used = Rocket.UseParts(false, regions);
                    results = used;
                }
                finally
                {
                    SuppressClickAdd = false;
                }

                int effective = 0;
                if (results != null)
                {
                    foreach (UsePartData data in results)
                    {
                        if (data != null && data.successfullyUsedPart)
                            effective++;
                    }
                }

                Status = "Triggered slot " + KeyLabel(slotIndex) + ": " + usable.Count +
                         " part(s), " + effective + " reacted.";
                AgLog.Detail(Status + " (a part with no use action, e.g. a plain tank, cannot react)");

                // Groups made of parts that have no on/off state (separators, for instance) get their
                // light from this instead, so that pressing a key always shows something.
                if (effective > 0)
                    NoteTriggered(slotIndex);

                // With Debug on, this one line says whether the light will now be green and what the
                // answer was read from - which is the first thing to look at if a light does not react.
                AgLog.Detail(DescribeLight(slotIndex));
            }
            catch (Exception e)
            {
                Status = "Trigger failed: " + e.Message;
                AgLog.Error("Activate failed: " + e);
            }

            AgWindow.Refresh();
        }

        // ---------------------------------------------------------------- launch mapping

        /// <summary>Rotation of the craft in the build editor. The game rotates the blueprint part
        /// positions by it before creating the launched parts, so saved positions need the same rotation.</summary>
        public static Vector2 RotateByCraft(Vector2 position, float rotation)
        {
            if (Mathf.Abs(rotation) < 0.0001f)
                return position;

            return position * new Orientation(1f, 1f, rotation);
        }

        /// <summary>Get (creating if needed) the ten slots of a rocket, named like the saved record.</summary>
        public static AgRocketGroups EnsureGroups(Rocket rocket, List<SavedSlot> saved)
        {
            AgRocketGroups groups = GetRocketGroups(rocket, true);
            if (groups == null)
                return null;

            while (groups.groups.Count < Count)
            {
                int index = groups.groups.Count;
                string name = "Group " + (index + 1);

                if (saved != null && index < saved.Count && saved[index] != null &&
                    !string.IsNullOrEmpty(saved[index].name))
                {
                    name = saved[index].name;
                }

                groups.groups.Add(new Slot(name));
            }

            return groups;
        }

        /// <summary>
        /// Attach the saved groups to the rockets the game just created from the blueprint.
        /// CustomSaveData hands us the exact Part[] that was spawned, so nothing has to be searched
        /// for in the scene.
        ///
        /// Three mapping passes, from most to least certain:
        ///   1. the part index inside the blueprint (the game creates the parts in array order),
        ///      accepted only when the part type also matches,
        ///   2. exact position match (build and flight share the same local coordinate space,
        ///      except for the craft rotation which is replayed),
        ///   3. the same position match with a loose tolerance, reported as a warning.
        /// </summary>
        public static void MapLaunch(List<SavedSlot> saved, Part[] parts, float rotation)
        {
            if (saved == null || parts == null || parts.Length == 0)
            {
                AgLog.Warn("launch: nothing to map (saved records " +
                           (saved == null ? "null" : saved.Count.ToString()) + ", created parts " +
                           (parts == null ? "null" : parts.Length.ToString()) + ")");
                return;
            }

            lastLaunchRotation = rotation;
            selfHealPending = true;
            selfHealDone = false;
            launchSeen = true;

            // Per slot: the same part may legitimately belong to several action groups, so the
            // one-to-one rule only applies inside one slot.
            List<SlotEntry>[] slotEntries = new List<SlotEntry>[Count];
            List<Vector2>[] slotTargets = new List<Vector2>[Count];
            List<Part>[] slotMatches = new List<Part>[Count];

            int entryCount = 0;

            for (int i = 0; i < Count; i++)
            {
                slotEntries[i] = new List<SlotEntry>();
                slotTargets[i] = new List<Vector2>();

                SavedSlot record = i < saved.Count ? saved[i] : null;
                if (record != null && record.entries != null)
                {
                    foreach (SlotEntry entry in record.entries)
                    {
                        slotEntries[i].Add(entry);
                        slotTargets[i].Add(RotateByCraft(new Vector2(entry.x, entry.y), rotation));
                    }
                }

                slotMatches[i] = new List<Part>(slotEntries[i].Count);
                for (int k = 0; k < slotEntries[i].Count; k++)
                    slotMatches[i].Add(null);

                entryCount += slotEntries[i].Count;
            }

            AgLog.Detail("launch: " + entryCount + " saved entr(ies) in " + Count + " slot(s), " +
                        parts.Length + " created part(s), craft rotation " + rotation.ToString("0.###"));
            LogCreatedParts(parts);

            // Pass 1 - the part index inside the blueprint, validated by part type. Measured in game
            // this is the route that actually works: the build grid and the rocket do NOT share a
            // coordinate origin (a craft was off by about 3.5 units), so position matching alone
            // finds nothing. The index route is what the original Action Groups mod used.
            int byIndex = 0;
            for (int i = 0; i < Count; i++)
                byIndex += AssignByIndex(slotEntries[i], parts, slotMatches[i]);

            // The index matches tell us the translation between the two frames.
            int pairs;
            float spread;
            Vector2 calibration = ComputeCalibration(slotEntries, slotTargets, slotMatches,
                                                     out pairs, out spread);

            AgLog.Detail("launch: frame calibration (" + calibration.x.ToString("0.00") + ", " +
                        calibration.y.ToString("0.00") + ") from " + pairs + " pair(s), spread " +
                        spread.ToString("0.00"));

            // Note: a "derive the frame offset from the parent transforms" cross-check was tried and
            // measured in game (v0.5.0 test run): the two parents sit at the same world position while
            // the part positions still differ by (3.50, 0.50), so that difference is NOT the frame
            // offset and the idea was dropped. The index-based calibration above is the validated one
            // (5 pairs, spread 0.00, and 5/5 parts mapped).

            // Pass 2..4 - position matching with the calibrated offset, then loose, then with no
            // offset, as safety nets for whatever the index pass could not place.
            int byPosition = 0;
            int byLoose = 0;
            int byRaw = 0;

            for (int i = 0; i < Count; i++)
            {
                byPosition += AssignRemaining(slotEntries[i], slotTargets[i], parts, slotMatches[i],
                                              MatchTolerance, calibration);
                byLoose += AssignRemaining(slotEntries[i], slotTargets[i], parts, slotMatches[i],
                                           1f, calibration);
                byRaw += AssignRemaining(slotEntries[i], slotTargets[i], parts, slotMatches[i],
                                         1f, Vector2.zero);
            }

            int attached = 0;
            int unmatched = 0;

            for (int i = 0; i < Count; i++)
            {
                for (int k = 0; k < slotEntries[i].Count; k++)
                {
                    SlotEntry entry = slotEntries[i][k];
                    Part part = slotMatches[i][k];

                    if (part == null)
                    {
                        unmatched++;
                        LogUnmatched(entry, slotTargets[i][k] + calibration, parts);
                        continue;
                    }

                    Rocket rocket = part.Rocket;
                    if (rocket == null)
                    {
                        unmatched++;
                        AgLog.Warn("launch: matched part \"" + entry.internalName +
                                   "\" has no rocket yet");
                        continue;
                    }

                    AgRocketGroups groups = EnsureGroups(rocket, saved);
                    if (groups == null)
                        continue;

                    Slot slot = groups.groups[i];
                    slot.entries.Add(entry);
                    slot.parts.Add(part);
                    attached++;
                }

                LogBindings(i, slotEntries[i], slotTargets[i], slotMatches[i], parts);
            }

            // Even a rocket that received nothing gets its named slots, so the window is never empty.
            Rocket player = CurrentRocket;
            if (player != null)
                EnsureGroups(player, saved);

            AgLog.Detail("launch mapping: " + attached + " attached (by index " + byIndex +
                        ", by position " + byPosition + ", by loose position " + byLoose +
                        ", by raw position " + byRaw + "), " + unmatched + " unmatched");

            LogRocketOwnership();
        }

        /// <summary>
        /// Pass 1: assign by the part index captured at build time, accepted only when the created
        /// part has the same type. One-to-one inside this slot.
        /// </summary>
        private static int AssignByIndex(List<SlotEntry> entries, Part[] parts, List<Part> matches)
        {
            bool[] used = new bool[parts.Length];
            int count = 0;

            for (int e = 0; e < entries.Count; e++)
            {
                int index = entries[e].shipIndex;
                if (index < 0 || index >= parts.Length || used[index])
                    continue;

                Part candidate = parts[index];
                if (candidate == null || candidate.Name != entries[e].internalName)
                    continue;

                matches[e] = candidate;
                used[index] = true;
                count++;
            }

            return count;
        }

        /// <summary>
        /// Derives the translation between the build frame and the rocket frame from the pairs the
        /// index pass produced, plus how consistent those pairs are.
        /// </summary>
        private static Vector2 ComputeCalibration(List<SlotEntry>[] slotEntries, List<Vector2>[] slotTargets,
                                                  List<Part>[] slotMatches, out int pairs, out float spread)
        {
            List<Vector2> offsets = new List<Vector2>();

            for (int i = 0; i < Count; i++)
            {
                for (int k = 0; k < slotEntries[i].Count; k++)
                {
                    Part part = slotMatches[i][k];
                    if (part == null)
                        continue;

                    Vector2 position = part.Position;
                    offsets.Add(position - slotTargets[i][k]);
                }
            }

            pairs = offsets.Count;
            spread = 0f;

            if (pairs == 0)
                return Vector2.zero;

            Vector2 mean = Vector2.zero;
            foreach (Vector2 offset in offsets)
                mean += offset;
            mean /= offsets.Count;

            foreach (Vector2 offset in offsets)
            {
                float distance = Vector2.Distance(offset, mean);
                if (distance > spread)
                    spread = distance;
            }

            return mean;
        }

        /// <summary>
        /// One-to-one assignment of the still unassigned entries of one slot, delegated to the same
        /// <see cref="PartMatcher"/> that the offline test compiles and executes, so there is only
        /// one implementation of the matching rules. Parts already claimed by this slot are excluded,
        /// but a part claimed by a different slot is not - the same part may be in several groups.
        /// </summary>
        private static int AssignRemaining(List<SlotEntry> entries, List<Vector2> targets,
                                           Part[] parts, List<Part> matches, float tolerance,
                                           Vector2 offset)
        {
            List<int> freeEntries = new List<int>();
            for (int e = 0; e < entries.Count; e++)
            {
                if (matches[e] == null)
                    freeEntries.Add(e);
            }

            if (freeEntries.Count == 0)
                return 0;

            bool[] used = new bool[parts.Length];
            for (int e = 0; e < matches.Count; e++)
            {
                Part claimed = matches[e];
                if (claimed == null)
                    continue;

                for (int p = 0; p < parts.Length; p++)
                {
                    if (ReferenceEquals(parts[p], claimed))
                    {
                        used[p] = true;
                        break;
                    }
                }
            }

            List<int> freeParts = new List<int>();
            for (int p = 0; p < parts.Length; p++)
            {
                if (!used[p] && parts[p] != null)
                    freeParts.Add(p);
            }

            if (freeParts.Count == 0)
                return 0;

            string[] entryNames = new string[freeEntries.Count];
            float[] entryX = new float[freeEntries.Count];
            float[] entryY = new float[freeEntries.Count];

            for (int i = 0; i < freeEntries.Count; i++)
            {
                int e = freeEntries[i];
                entryNames[i] = entries[e].internalName;
                entryX[i] = targets[e].x + offset.x;
                entryY[i] = targets[e].y + offset.y;
            }

            string[] partNames = new string[freeParts.Count];
            float[] partX = new float[freeParts.Count];
            float[] partY = new float[freeParts.Count];

            for (int i = 0; i < freeParts.Count; i++)
            {
                Part part = parts[freeParts[i]];
                Vector2 position = part.Position;
                partNames[i] = part.Name;
                partX[i] = position.x;
                partY[i] = position.y;
            }

            int[] assigned = PartMatcher.Assign(entryNames, entryX, entryY,
                                                partNames, partX, partY, tolerance);

            int count = 0;
            for (int i = 0; i < assigned.Length; i++)
            {
                if (assigned[i] < 0)
                    continue;

                matches[freeEntries[i]] = parts[freeParts[assigned[i]]];
                count++;
            }

            return count;
        }

        /// <summary>Set when a launch mapping ran, so the world scene can correct it later if the
        /// groups did not end up on the rocket the player is flying.</summary>
        private static bool selfHealPending;
        private static bool selfHealDone;
        private static float lastLaunchRotation;

        /// <summary>True once a launch mapping ran this session. If it is false in the world scene,
        /// the launch callback from CustomSaveData never reached us.</summary>
        private static bool launchSeen;

        /// <summary>One line describing whether the launch path was entered at all - the empty log is
        /// otherwise indistinguishable from "the callback never fired".</summary>
        public static string LaunchStage
        {
            get
            {
                if (!launchSeen)
                    return "NOT SEEN (CustomSaveData's launch callback did not reach the mod)";
                if (selfHealDone)
                    return "mapped, self-heal finished";
                if (selfHealPending)
                    return "mapped, self-heal pending";
                return "mapped";
            }
        }

        /// <summary>
        /// Late correction, run once shortly after the world scene is up. The launch mapping happens
        /// while the scene is still being loaded, so if the player rocket was not the object that
        /// received the groups, this maps the frozen launch snapshot onto the rocket the player
        /// actually flies. It only ever adds groups when the rocket has none, and it only attaches
        /// parts it can actually match, so an unrelated craft is left alone.
        /// </summary>
        public static void TrySelfHeal()
        {
            if (!selfHealPending || selfHealDone || !AgScene.IsWorld)
                return;

            Rocket rocket = CurrentRocket;
            if (rocket == null || rocket.partHolder == null || rocket.partHolder.GetArray().Length == 0)
                return;

            selfHealDone = true;
            selfHealPending = false;

            AgRocketGroups existing = GetRocketGroups(rocket, false);
            int existingParts = 0;
            if (existing != null)
            {
                foreach (Slot group in existing.groups)
                    existingParts += group.Count;
            }

            if (existingParts > 0)
            {
                AgLog.Detail("world self-heal: the flown rocket already has " + existingParts +
                            " grouped part(s), nothing to do");
                return;
            }

            List<SavedSlot> saved = CaptureBuildSlots();

            int entries = 0;
            foreach (SavedSlot record in saved)
            {
                if (record != null && record.entries != null)
                    entries += record.entries.Count;
            }

            if (entries == 0)
            {
                AgLog.Detail("world self-heal: no launch snapshot left, nothing to do");
                return;
            }

            AgLog.Detail("world self-heal: the flown rocket has no groups, mapping the " + entries +
                        " saved entr(ies) onto its " + rocket.partHolder.GetArray().Length + " parts");

            MapLaunch(saved, rocket.partHolder.GetArray(), lastLaunchRotation);
            AgWindow.Refresh();
        }

        /// <summary>Logs which created part each of the first few entries of a slot was bound to, so a
        /// wrong binding is visible in the log instead of being silent.</summary>
        private static void LogBindings(int slotIndex, List<SlotEntry> entries, List<Vector2> targets,
                                        List<Part> matches, Part[] parts)
        {
            int shown = 0;

            for (int e = 0; e < entries.Count && shown < 6; e++)
            {
                Part part = matches[e];
                if (part == null)
                    continue;

                int index = -1;
                for (int p = 0; p < parts.Length; p++)
                {
                    if (ReferenceEquals(parts[p], part))
                    {
                        index = p;
                        break;
                    }
                }

                Vector2 position = part.Position;
                AgLog.Detail("launch: slot [" + KeyLabel(slotIndex) + "] entry \"" +
                            entries[e].internalName + "\" idx=" + entries[e].shipIndex +
                            " @(" + targets[e].x.ToString("0.0") + ", " + targets[e].y.ToString("0.0") +
                            ") -> created[" + index + "] \"" + part.Name + "\" @(" +
                            position.x.ToString("0.0") + ", " + position.y.ToString("0.0") + ")");

                shown++;
            }
        }

        private static void LogCreatedParts(Part[] parts)
        {
            int limit = parts.Length < 6 ? parts.Length : 6;
            for (int i = 0; i < limit; i++)
            {
                Part part = parts[i];
                if (part == null)
                    continue;
                Vector2 position = part.Position;
                AgLog.Detail("launch: created[" + i + "] \"" + part.Name + "\" @(" +
                            position.x.ToString("0.0") + ", " + position.y.ToString("0.0") + ")");
            }
        }

        private static void LogUnmatched(SlotEntry entry, Vector2 target, Part[] parts)
        {
            Part nearest = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < parts.Length; i++)
            {
                Part part = parts[i];
                if (part == null || part.Name != entry.internalName)
                    continue;

                float distance = Vector2.Distance(part.Position, target);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = part;
                }
            }

            if (nearest == null)
            {
                AgLog.Warn("launch: UNMATCHED \"" + entry.internalName + "\" idx=" + entry.shipIndex +
                           " @(" + target.x.ToString("0.0") + ", " + target.y.ToString("0.0") +
                           ") - no created part has that type");
            }
            else
            {
                Vector2 position = nearest.Position;
                AgLog.Warn("launch: UNMATCHED \"" + entry.internalName + "\" idx=" + entry.shipIndex +
                           " @(" + target.x.ToString("0.0") + ", " + target.y.ToString("0.0") +
                           ") - nearest same type at (" + position.x.ToString("0.0") + ", " +
                           position.y.ToString("0.0") + ") distance " + nearestDistance.ToString("0.00"));
            }
        }

        /// <summary>Logs which rockets exist and which of them carry action groups.</summary>
        public static void LogRocketOwnership()
        {
            Rocket player = CurrentRocket;
            Rocket[] rockets = UnityEngine.Object.FindObjectsByType<Rocket>(FindObjectsSortMode.None);
            if (rockets == null)
                return;

            int withGroups = 0;
            foreach (Rocket rocket in rockets)
            {
                if (rocket == null)
                    continue;

                AgRocketGroups groups = GetRocketGroups(rocket, false);
                int parts = 0;
                if (groups != null)
                {
                    foreach (Slot group in groups.groups)
                        parts += group.Count;
                }

                if (groups != null)
                    withGroups++;

                AgLog.Detail("launch: rocket \"" + rocket.name + "\" sceneParts=" +
                            (rocket.partHolder == null ? -1 : rocket.partHolder.GetArray().Length) +
                            " groups=" + (groups == null ? "none" : parts + " part(s)") +
                            (rocket == player ? "   <- player" : ""));
            }

            AgLog.Detail("launch: " + rockets.Length + " rocket(s) in scene, " + withGroups +
                        " with action groups, player rocket " +
                        (player == null ? "none" : "\"" + player.name + "\""));
        }
        // ---------------------------------------------------------------- persistence helpers

        /// <summary>
        /// Snapshot the build slots into the serialisable form.
        ///
        /// The stored snapshot is refreshed from the live parts whenever the build scene is still
        /// intact (so parts deleted in the editor disappear). While the scene is being torn down -
        /// which is what a launch does - every part reports destruction, so refreshing would empty
        /// the snapshot; in that case the last good snapshot is kept.
        /// </summary>
        public static List<SavedSlot> CaptureBuildSlots()
        {
            RefreshBuildEntries();

            List<SavedSlot> result = new List<SavedSlot>();

            for (int i = 0; i < Count; i++)
            {
                SavedSlot saved = new SavedSlot();
                saved.name = Slots[i].name;
                saved.entries.AddRange(Slots[i].entries);
                result.Add(saved);
            }

            return result;
        }

        /// <summary>Rebuild the snapshots from the live parts, but only while parts are actually alive
        /// and no launch is under way (a launch tears the build scene down part by part, and
        /// refreshing during that would drop the parts that are already gone).</summary>
        private static void RefreshBuildEntries()
        {
            if (buildFrozen)
                return;

            List<Part> buildParts = GetBuildParts();

            bool anyAlive = false;
            if (buildParts != null)
            {
                foreach (Part part in buildParts)
                {
                    if (part != null)
                    {
                        anyAlive = true;
                        break;
                    }
                }
            }

            if (!anyAlive)
                return;

            for (int i = 0; i < Count; i++)
                RebuildEntries(Slots[i]);
        }

        /// <summary>Snapshot one rocket's groups into the serialisable form.</summary>
        public static List<SavedSlot> CaptureRocketGroups(Rocket rocket)
        {
            List<SavedSlot> result = new List<SavedSlot>();
            AgRocketGroups groups = GetRocketGroups(rocket, false);
            if (groups == null)
                return result;

            List<Part> rocketParts = null;
            if (rocket.partHolder != null)
                rocketParts = new List<Part>(rocket.partHolder.GetArray());

            foreach (Slot group in groups.groups)
            {
                SavedSlot saved = new SavedSlot();
                saved.name = group.name;

                foreach (Part part in group.parts)
                {
                    if (part != null)
                        saved.entries.Add(MakeEntry(part, rocketParts));
                }

                result.Add(saved);
            }

            return result;
        }

        /// <summary>Restore the build slots from a blueprint.</summary>
        public static void ApplyBuildRestore(List<SavedSlot> saved)
        {
            if (saved == null)
                return;

            List<Part> buildParts = GetBuildParts();
            if (buildParts == null)
            {
                AgLog.Warn("build parts are not available yet, restore skipped");
                return;
            }

            for (int i = 0; i < Count; i++)
            {
                Slots[i].parts.Clear();
                Slots[i].entries.Clear();
                Slots[i].name = "Group " + (i + 1);
            }

            int restored = 0;
            int requested = 0;
            int restoredByIndex = 0;
            int restoredByPosition = 0;

            for (int i = 0; i < saved.Count && i < Count; i++)
            {
                SavedSlot record = saved[i];
                if (record == null)
                    continue;

                if (!string.IsNullOrEmpty(record.name))
                    Slots[i].name = ClampName(record.name);

                requested += record.entries.Count;

                int byIndex;
                int byPosition;
                List<Part> matches = AssignEntries(record.entries, buildParts.ToArray(), Vector2.zero,
                                                   out byIndex, out byPosition);
                restoredByIndex += byIndex;
                restoredByPosition += byPosition;

                for (int e = 0; e < record.entries.Count; e++)
                {
                    Part part = matches[e];
                    if (part != null)
                    {
                        Slots[i].parts.Add(part);
                        part.aboutToDestroy -= OnPartDestroyed;
                        part.aboutToDestroy += OnPartDestroyed;
                        restored++;
                    }
                    else
                    {
                        SlotEntry entry = record.entries[e];
                        AgLog.Detail("blueprint restore: no part matched " + entry.internalName +
                                   " idx=" + entry.shipIndex +
                                   " at (" + entry.x.ToString("0.0") + ", " + entry.y.ToString("0.0") + ")");
                    }
                }
            }

            AgLog.Detail("blueprint restore: " + restored + "/" + requested + " restored (by index " +
                        restoredByIndex + ", by position " + restoredByPosition + "), build grid has " +
                        buildParts.Count + " part(s)");
            Status = "Blueprint loaded: " + restored + "/" + requested + " parts restored.";
            AgLog.Detail(Status);
            for (int i = 0; i < Count; i++)
                RebuildEntries(Slots[i]);
            AgWindow.Refresh();
        }

        /// <summary>Attach saved groups to a rocket found in the world scene.</summary>
        public static void AttachGroupsToRocket(Rocket rocket, List<SavedSlot> saved)
        {
            if (rocket == null || saved == null)
                return;

            Part[] parts = rocket.partHolder.GetArray();
            AgRocketGroups groups = EnsureGroups(rocket, saved);
            if (groups == null)
                return;

            int restoredByIndex = 0;
            int restoredByPosition = 0;

            for (int i = 0; i < Count; i++)
            {
                Slot group = groups.groups[i];
                SavedSlot record = i < saved.Count ? saved[i] : null;
                if (record == null)
                    continue;

                int byIndex;
                int byPosition;
                List<Part> matches = AssignEntries(record.entries, parts, Vector2.zero,
                                                   out byIndex, out byPosition);
                restoredByIndex += byIndex;
                restoredByPosition += byPosition;

                for (int e = 0; e < record.entries.Count; e++)
                {
                    group.entries.Add(record.entries[e]);
                    group.parts.Add(matches[e]);
                }
            }

            AgLog.Detail("restored action groups on rocket " + rocket.name + " (" +
                        CountMatched(groups) + " part(s), by index " + restoredByIndex +
                        ", by position " + restoredByPosition + ", rocket has " + parts.Length +
                        " part(s))");
        }

        private static int CountMatched(AgRocketGroups groups)
        {
            int count = 0;
            foreach (Slot group in groups.groups)
                count += group.parts.Count;
            return count;
        }

        // ---------------------------------------------------------------- diagnostics

        /// <summary>Writes the complete state to the console, so one test run gives a full picture.</summary>
        public static void DumpState()
        {
            try
            {
                AgLog.Detail("---------- state dump ----------");
                AgLog.Detail("scene: " + (AgScene.IsBuild ? "build" : AgScene.IsWorld ? "world" : "other") +
                            ", selected slot: " + Selected +
                            ", CustomSaveData hooked: " + AgPersistence.IsHooked);
                AgLog.Detail("launch path: " + LaunchStage);

                List<Part> buildParts = GetBuildParts();
                AgLog.Detail("build grid parts: " + (buildParts == null ? "n/a" : buildParts.Count.ToString()));

                Rocket rocket = CurrentRocket;
                if (rocket != null && rocket.partHolder != null)
                {
                    AgLog.Detail("current rocket \"" + rocket.name + "\" with " +
                                rocket.partHolder.GetArray().Length + " parts");
                }
                else
                {
                    AgLog.Detail("current rocket: none");
                }

                if (AgScene.IsWorld)
                    LogRocketOwnership();

                // What the flight lights are showing right now, and which module the answer came from.
                DumpLightState();

                for (int i = 0; i < Count; i++)
                {
                    Slot view = ViewSlot(i);
                    AgLog.Detail("slot [" + KeyLabel(i) + "] \"" + ViewName(i) + "\": " +
                                (view == null ? "no data" : view.Count + " part(s)"));

                    if (view == null)
                        continue;

                    for (int e = 0; e < view.entries.Count; e++)
                    {
                        SlotEntry entry = view.entries[e];
                        string matched = e < view.parts.Count && view.parts[e] != null
                            ? "matched"
                            : "UNMATCHED";
                        AgLog.Detail("    " + entry.Format(e + 1) + "   -> " + matched +
                                    "  type=" + entry.internalName);
                    }
                }

                AgLog.Detail("---------- end of dump ----------");
            }
            catch (Exception e)
            {
                AgLog.Error("DumpState failed: " + e);
            }
        }

        // ---------------------------------------------------------------- staging

        /// <summary>Rebuild a slot's display snapshot from its live parts.</summary>
        public static void RebuildEntries(Slot slot)
        {
            RebuildEntries(slot, GetBuildParts());
        }

        /// <summary>Rebuilds the snapshot of a slot against the part list the slot belongs to: the build
        /// grid while editing, the parts of the rocket while flying (the index means different things in
        /// those two cases).</summary>
        public static void RebuildEntries(Slot slot, List<Part> within)
        {
            if (slot == null)
                return;

            slot.entries.Clear();
            foreach (Part part in slot.parts)
            {
                if (part != null)
                    slot.entries.Add(MakeEntry(part, within));
            }
        }

        /// <summary>Stage separation: give the pieces that moved to the new rocket their own copy
        /// of the affected groups, and remove them from the parent.</summary>
        public static void SplitGroupsOnDecouple(Rocket parent, Rocket child)
        {
            if (parent == null || child == null || child.partHolder == null)
                return;

            AgRocketGroups parentGroups = GetRocketGroups(parent, false);
            if (parentGroups == null)
                return;

            AgRocketGroups childGroups = GetRocketGroups(child, true);
            childGroups.groups.Clear();

            for (int i = 0; i < Count; i++)
            {
                Slot source = i < parentGroups.groups.Count ? parentGroups.groups[i] : null;
                Slot target = new Slot(source != null ? source.name : "Group " + (i + 1));

                if (source != null)
                {
                    List<Part> moved = new List<Part>();
                    foreach (Part part in source.parts)
                    {
                        if (part != null && child.partHolder.ContainsPart(part))
                            moved.Add(part);
                    }

                    foreach (Part part in moved)
                    {
                        source.parts.Remove(part);
                        target.parts.Add(part);
                    }

                    if (moved.Count > 0)
                    {
                        RebuildEntries(source);
                        RebuildEntries(target);
                        AgLog.Detail("stage separation: " + moved.Count + " part(s) moved from slot " +
                                    KeyLabel(i) + " to the new rocket");

                        // This group lost its last part to the other rocket, so it is empty on this one
                        // and goes back to its default name.
                        if (source.parts.Count == 0 && RenameToDefault(source, i))
                            AgLog.Detail("slot " + KeyLabel(i) + " is empty on \"" +
                                         (parent != null ? parent.name : "?") + "\" after the separation");
                    }
                }

                childGroups.groups.Add(target);
            }

            // Both rockets come out of a separation with a full set of ten slots, so drop whatever each
            // of them lost to the other one and give a group that has nothing left its default name.
            PruneGroups(parentGroups, new List<Part>(parent.partHolder.GetArray()));
            PruneGroups(childGroups, new List<Part>(child.partHolder.GetArray()));

            AgWindow.Refresh();
        }

        /// <summary>Docking: fold the groups of the docked rocket into the surviving one.</summary>
        public static void MergeGroupsInto(Rocket survivor, Rocket other)
        {
            if (survivor == null || other == null)
                return;

            AgRocketGroups survivorGroups = GetRocketGroups(survivor, false);
            AgRocketGroups otherGroups = GetRocketGroups(other, false);
            if (survivorGroups == null || otherGroups == null)
                return;

            for (int i = 0; i < Count; i++)
            {
                Slot from = i < otherGroups.groups.Count ? otherGroups.groups[i] : null;
                if (from == null)
                    continue;

                while (survivorGroups.groups.Count <= i)
                    survivorGroups.groups.Add(new Slot("Group " + (i + 1)));

                Slot into = survivorGroups.groups[i];
                foreach (Part part in from.parts)
                {
                    if (part != null && !into.parts.Contains(part))
                        into.parts.Add(part);
                }

                RebuildEntries(into);
            }

            otherGroups.groups.Clear();
            AgLog.Detail("docking: action groups merged into the surviving rocket");
            AgWindow.Refresh();
        }
    }
}
