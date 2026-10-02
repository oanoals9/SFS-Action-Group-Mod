using System;
using System.Collections.Generic;
using SFS.Builds;
using SFS.Parts;
using UnityEngine;

namespace SFSActionGroupMod
{
    /// <summary>
    /// Remembers which action groups the parts of the last copy belong to, so that pasting them (or
    /// duplicating the selection) carries the action groups along.
    ///
    /// That is how two blueprints are merged in the build scene: load the first one, select it and copy
    /// it, load the second one and paste the copy - the parts of the first craft, and with them its
    /// action groups, join the parts and groups of the second one. The game itself knows nothing about
    /// our groups, so without this the groups of the copied craft would simply be lost.
    ///
    /// The clipboard stores the parts in one order (PartSave.CreateSaves keeps the order it is given,
    /// and PartsLoader.CreateParts creates one part per save in the same order), so the pasted parts can
    /// be lined up with the recorded ones by position in that list. The part type and the offset between
    /// the two positions are checked for every pair before anything is added, so a stale or unrelated
    /// clipboard can never put parts into the wrong slot.
    /// </summary>
    public static class AgClipboard
    {
        /// <summary>One part of the last copy, in clipboard order.</summary>
        private class CopiedPart
        {
            public string name;      // part type
            public float x;
            public float y;
            public int[] slots = new int[0];
            public string[] slotNames = new string[0];
        }

        private static readonly List<CopiedPart> copied = new List<CopiedPart>();

        /// <summary>Pasted parts whose entries still have to be recalculated once the game has moved
        /// them from the cursor into the build grid (until then they have no index there).</summary>
        private static readonly List<Part> waitingForPlacement = new List<Part>();

        /// <summary>How many parts the hold grid carries right now. Read before the game pastes, so
        /// that the parts it adds can be told apart from the ones already there.</summary>
        public static int CountHeldParts()
        {
            List<Part> held = GetHeldParts();
            return held == null ? 0 : held.Count;
        }

        /// <summary>Records the action groups of the parts the player is about to copy.</summary>
        public static void NoteCopied()
        {
            copied.Clear();

            try
            {
                BuildGrid grid = BuildManager.main != null ? BuildManager.main.buildGrid : null;
                if (grid == null || grid.selector == null || grid.activeGrid == null ||
                    grid.activeGrid.partsHolder == null)
                {
                    return;
                }

                List<Part> gridParts = grid.activeGrid.partsHolder.parts;

                // The game copies the selected parts that are in the active grid, in the order they come
                // out of the selection set; this walks the set the same way.
                foreach (Part part in grid.selector.selected)
                {
                    if (part == null || gridParts == null || !gridParts.Contains(part))
                        continue;

                    copied.Add(Describe(part));
                }

                if (copied.Count > 0)
                    AgLog.Detail("clipboard: remembered the action groups of " + copied.Count + " part(s)");
            }
            catch (Exception e)
            {
                copied.Clear();
                AgLog.Error("could not read the copied parts: " + e);
            }
        }

        /// <summary>Called right after the game pasted the clipboard: gives the new parts the action
        /// groups of the parts they were copied from.</summary>
        public static void NotePasted(int firstNewPartIndex)
        {
            try
            {
                if (copied.Count == 0)
                    return;

                List<Part> held = GetHeldParts();
                if (held == null || firstNewPartIndex < 0 || firstNewPartIndex >= held.Count)
                {
                    AgLog.Detail("clipboard: the pasted parts could not be read, nothing was carried over");
                    copied.Clear();
                    return;
                }

                int count = held.Count - firstNewPartIndex;
                if (count != copied.Count || !LooksLikeTheSameCluster(held, firstNewPartIndex))
                {
                    AgLog.Detail("clipboard: the pasted parts (" + count + ") do not line up with the " +
                                 copied.Count + " part(s) of the last copy, so their action groups were " +
                                 "not carried over");
                    copied.Clear();
                    return;
                }

                int joined = 0;
                for (int i = 0; i < count; i++)
                {
                    Part part = held[firstNewPartIndex + i];
                    CopiedPart record = copied[i];
                    if (part == null)
                        continue;

                    if (AgSlots.AddPastedPart(part, record.slots, record.slotNames))
                    {
                        waitingForPlacement.Add(part);
                        joined++;
                    }
                }

                copied.Clear();

                AgSlots.Status = joined > 0
                    ? joined + " pasted part(s) joined their action groups."
                    : "The pasted parts were not in any action group.";
                AgLog.Detail(AgSlots.Status);
                AgWindow.Refresh();
            }
            catch (Exception e)
            {
                AgLog.Error("paste handling failed: " + e);
            }
        }

        /// <summary>The blueprint menu's "Import" button: unlike "Load" it does not clear the build grid,
        /// it drops the imported blueprint next to the craft that is already there, which is how two
        /// blueprints are merged by hand. Its action groups come along the same way a paste does.
        ///
        /// The imported parts are a copy of the blueprint's own part list in exactly that order, and the
        /// saved entries store the index of their part in that list, so the two line up one to one.</summary>
        public static void NoteImported(List<SavedSlot> saved, int blueprintPartCount, int firstNewPartIndex)
        {
            try
            {
                if (saved == null)
                {
                    AgLog.Detail("import: the imported blueprint carries no action groups");
                    return;
                }

                List<Part> held = GetHeldParts();
                int count = held == null ? 0 : held.Count - firstNewPartIndex;
                if (held == null || count <= 0 || (blueprintPartCount > 0 && count != blueprintPartCount))
                {
                    AgLog.Detail("import: the imported parts could not be read (" + count + " of " +
                                 blueprintPartCount + "), their action groups were not carried over");
                    return;
                }

                int joined = 0;
                int skipped = 0;
                for (int i = 0; i < saved.Count && i < AgSlots.Count; i++)
                {
                    SavedSlot record = saved[i];
                    if (record == null || record.entries == null)
                        continue;

                    foreach (SlotEntry entry in record.entries)
                    {
                        if (entry == null || entry.shipIndex < 0 || entry.shipIndex >= count)
                        {
                            skipped++;
                            continue;
                        }

                        Part part = held[firstNewPartIndex + entry.shipIndex];
                        if (part == null || part.Name != entry.internalName)
                        {
                            skipped++;
                            continue;
                        }

                        if (AgSlots.AddPastedPart(part, new int[] { i }, new string[] { record.name }))
                        {
                            waitingForPlacement.Add(part);
                            joined++;
                        }
                    }
                }

                AgSlots.Status = joined > 0
                    ? joined + " imported part(s) joined their action groups."
                    : "The imported parts could not be matched to their action groups.";
                AgLog.Detail(AgSlots.Status + (skipped > 0 ? " (" + skipped + " entr(ies) skipped)" : ""));
                AgWindow.Refresh();
            }
            catch (Exception e)
            {
                AgLog.Error("import handling failed: " + e);
            }
        }

        /// <summary>Once the game has put the pasted parts into the build grid, their captured index can
        /// be filled in.</summary>
        public static void Tick()
        {
            if (waitingForPlacement.Count == 0)
                return;

            try
            {
                List<Part> grid = AgSlots.GetBuildParts();
                if (grid == null)
                    return;

                bool placed = false;
                for (int i = waitingForPlacement.Count - 1; i >= 0; i--)
                {
                    Part part = waitingForPlacement[i];
                    if (part == null || grid.Contains(part))
                    {
                        waitingForPlacement.RemoveAt(i);
                        placed = true;
                    }
                }

                if (!placed)
                    return;

                for (int i = 0; i < AgSlots.Count; i++)
                    AgSlots.RebuildEntries(AgSlots.Slots[i]);

                AgWindow.Refresh();
            }
            catch (Exception e)
            {
                AgLog.Error("clipboard update failed: " + e);
            }
        }

        private static CopiedPart Describe(Part part)
        {
            CopiedPart record = new CopiedPart();
            record.name = part.Name;
            record.x = part.Position.x;
            record.y = part.Position.y;

            List<int> slots = new List<int>();
            List<string> names = new List<string>();
            for (int i = 0; i < AgSlots.Count; i++)
            {
                Slot slot = AgSlots.Slots[i];
                if (slot == null || !slot.parts.Contains(part))
                    continue;

                slots.Add(i);
                names.Add(slot.name);
            }

            record.slots = slots.ToArray();
            record.slotNames = names.ToArray();
            return record;
        }

        /// <summary>True when the new parts are the same cluster as the recorded copy: every part has the
        /// same type, and the distance between a new part and the part it was copied from is the same for
        /// all of them (a paste only moves the cluster, i.e. it is a plain translation).</summary>
        private static bool LooksLikeTheSameCluster(List<Part> held, int first)
        {
            bool haveOffset = false;
            float offsetX = 0f;
            float offsetY = 0f;

            for (int i = 0; i < copied.Count; i++)
            {
                Part part = held[first + i];
                CopiedPart record = copied[i];
                if (part == null || part.Name != record.name)
                    return false;

                Vector2 position = part.Position;
                float dx = position.x - record.x;
                float dy = position.y - record.y;

                if (!haveOffset)
                {
                    offsetX = dx;
                    offsetY = dy;
                    haveOffset = true;
                    continue;
                }

                if (Mathf.Abs(dx - offsetX) > 0.05f || Mathf.Abs(dy - offsetY) > 0.05f)
                    return false;
            }

            return true;
        }

        private static List<Part> GetHeldParts()
        {
            if (BuildManager.main == null || BuildManager.main.holdGrid == null ||
                BuildManager.main.holdGrid.holdGrid == null ||
                BuildManager.main.holdGrid.holdGrid.partsHolder == null)
            {
                return null;
            }

            return BuildManager.main.holdGrid.holdGrid.partsHolder.parts;
        }
    }
}
