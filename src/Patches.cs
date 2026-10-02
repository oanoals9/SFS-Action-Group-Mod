using System;
using System.Collections.Generic;
using HarmonyLib;
using SFS.Builds;
using SFS.Parts;
using SFS.World;

namespace SFSActionGroupMod
{
    /// <summary>Captures part clicks in the build scene and hooks the launch, so slots follow the ship.</summary>
    public static class Patches
    {
        [HarmonyPatch(typeof(BuildMenus), nameof(BuildMenus.OnPartClick))]
        public static class BuildMenus_OnPartClick
        {
            public static bool Prefix(PartHit hit)
            {
                if (AgSlots.Selected < 0)
                    return true; // no slot selected: the game behaves as usual

                try
                {
                    if (hit != null && hit.part != null)
                        AgSlots.AddPart(AgSlots.Selected, hit.part);
                }
                catch (Exception e)
                {
                    AgLog.Error("OnPartClick failed: " + e);
                }

                return false; // consume the click so it only means "add to slot"
            }
        }

        [HarmonyPatch(typeof(BuildMenus), nameof(BuildMenus.OnAreaSelect))]
        public static class BuildMenus_OnAreaSelect
        {
            public static bool Prefix(Part[] parts)
            {
                if (AgSlots.Selected < 0 || parts == null)
                    return true;

                try
                {
                    for (int i = 0; i < parts.Length; i++)
                        AgSlots.AddPart(AgSlots.Selected, parts[i]);
                }
                catch (Exception e)
                {
                    AgLog.Error("OnAreaSelect failed: " + e);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(BuildMenus), nameof(BuildMenus.OnEmptyClick))]
        public static class BuildMenus_OnEmptyClick
        {
            public static void Postfix()
            {
                try
                {
                    // Clicking empty space clears the slot selection, so clicking parts goes back to
                    // meaning "select a part" instead of "add to a slot".
                    // The game only routes clicks that are not over UI here (UI panels carry
                    // SFS.UI.SkipUI, which consumes the input in SFS.Input.TouchElements), so this does
                    // not fire when the player clicks this mod's own window.
                    AgSlots.Deselect();
                }
                catch (Exception e)
                {
                    AgLog.Error("OnEmptyClick patch failed: " + e);
                }
            }
        }

        /// <summary>In flight, a click on a part ends up here as a single part "use". While a slot is
        /// selected the click means "add this part to that slot" instead, exactly like in the build
        /// scene; with nothing selected the part is used as usual. The click path of the game
        /// (Rocket.ClickPart) is private, but this method is public and is the only thing it calls with
        /// a single part, so it is the natural place to intercept.</summary>
        [HarmonyPatch(typeof(Rocket), nameof(Rocket.UseParts))]
        public static class Rocket_UseParts
        {
            public static bool Prefix(bool fromStaging,
                                      ValueTuple<Part, SFS.Parts.Modules.PolygonData>[] regions,
                                      ref UsePartData[] __result)
            {
                try
                {
                    if (AgSlots.SuppressClickAdd)
                        return true; // this call comes from the mod itself (a key was pressed)

                    if (fromStaging || !AgScene.IsWorld)
                        return true;

                    if (AgSlots.Selected < 0 || regions == null || regions.Length != 1)
                    {
                        // No slot is being edited, so the click really fires the part: the flight lights
                        // of the groups that contain it get the same flash a key press gives them.
                        if (regions != null && regions.Length == 1)
                            AgSlots.NotePartTriggered(regions[0].Item1);

                        return true;
                    }

                    Part part = regions[0].Item1;
                    if (part == null)
                        return true;

                    AgSlots.AddPartInWorld(AgSlots.Selected, part);
                    __result = new UsePartData[0]; // nothing was used
                    return false;
                }
                catch (Exception e)
                {
                    AgLog.Error("UseParts patch failed: " + e);
                    return true;
                }
            }
        }

        /// <summary>Copying a cluster in the build scene is how two blueprints are combined: load the
        /// first one, select it, copy, load the second one and paste. The copied parts remember which
        /// action groups they were in, so the paste can put the pasted copies back into those groups and
        /// the two sets of groups end up merged (see AgClipboard).</summary>
        // "Copy"/"Paste" are private in BuildMenus, so they are named as strings (Harmony can patch a
        // private method; only the nameof(...) form would not compile).
        [HarmonyPatch(typeof(BuildMenus), "Copy")]
        public static class BuildMenus_Copy
        {
            public static void Prefix()
            {
                try
                {
                    AgClipboard.NoteCopied();
                }
                catch (Exception e)
                {
                    AgLog.Error("Copy patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(BuildMenus), "Paste")]
        public static class BuildMenus_Paste
        {
            private static int heldParts;

            public static void Prefix()
            {
                try
                {
                    heldParts = AgClipboard.CountHeldParts();
                }
                catch (Exception e)
                {
                    AgLog.Error("Paste patch failed: " + e);
                }
            }

            public static void Postfix()
            {
                try
                {
                    AgClipboard.NotePasted(heldParts);
                }
                catch (Exception e)
                {
                    AgLog.Error("Paste patch failed: " + e);
                }
            }
        }

        /// <summary>The game's own "duplicate the selection" works like a copy and a paste in one go, so
        /// the duplicated parts join the groups of the parts they were made from.</summary>
        [HarmonyPatch(typeof(BuildGrid), nameof(BuildGrid.Duplicate))]
        public static class BuildGrid_Duplicate
        {
            private static int heldParts;

            public static void Prefix()
            {
                try
                {
                    AgClipboard.NoteCopied();
                    heldParts = AgClipboard.CountHeldParts();
                }
                catch (Exception e)
                {
                    AgLog.Error("Duplicate patch failed: " + e);
                }
            }

            public static void Postfix()
            {
                try
                {
                    AgClipboard.NotePasted(heldParts);
                }
                catch (Exception e)
                {
                    AgLog.Error("Duplicate patch failed: " + e);
                }
            }
        }

        /// <summary>The blueprint menu's "Import" button brings a blueprint into the build grid without
        /// clearing it (that is the game's own way of merging two blueprints, as opposed to "Load"). The
        /// parts it creates are a copy of the blueprint's part list in the same order, so the action
        /// groups stored in that blueprint can be handed to them.</summary>
        [HarmonyPatch(typeof(HoldGrid), nameof(HoldGrid.StartImport))]
        public static class HoldGrid_StartImport
        {
            private static int heldParts;
            private static int blueprintParts;
            private static List<SavedSlot> imported;

            public static void Prefix(Blueprint blueprint)
            {
                try
                {
                    heldParts = AgClipboard.CountHeldParts();
                    blueprintParts = blueprint != null && blueprint.parts != null
                        ? blueprint.parts.Length
                        : 0;
                    imported = AgPersistence.ReadFromBlueprint(blueprint);
                }
                catch (Exception e)
                {
                    imported = null;
                    AgLog.Error("Import patch failed: " + e);
                }
            }

            public static void Postfix()
            {
                try
                {
                    AgClipboard.NoteImported(imported, blueprintParts, heldParts);
                }
                catch (Exception e)
                {
                    AgLog.Error("Import patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(BuildState), nameof(BuildState.Clear))]
        public static class BuildState_Clear
        {
            public static void Postfix()
            {
                try
                {
                    AgSlots.ResetBuildState();
                }
                catch (Exception e)
                {
                    AgLog.Error("BuildState.Clear patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(BuildManager), nameof(BuildManager.Launch))]
        public static class BuildManager_Launch
        {
            public static void Postfix()
            {
                try
                {
                    // From now on the build scene is going to be torn down part by part, so the
                    // action-group snapshots must not be refreshed from the (dying) parts any more.
                    AgSlots.FreezeBuild();
                }
                catch (Exception e)
                {
                    AgLog.Error("Launch patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(RocketManager), nameof(RocketManager.CreateRocket_Child))]
        public static class RocketManager_CreateRocket_Child
        {
            public static void Postfix(Rocket parentRocket, ref Rocket __result)
            {
                try
                {
                    AgSlots.SplitGroupsOnDecouple(parentRocket, __result);
                }
                catch (Exception e)
                {
                    AgLog.Error("CreateRocket_Child patch failed: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(RocketManager), nameof(RocketManager.MergeRockets))]
        public static class RocketManager_MergeRockets
        {
            public static void Postfix(Rocket rocket_A, Rocket rocket_B)
            {
                try
                {
                    AgSlots.MergeGroupsInto(rocket_A, rocket_B);
                }
                catch (Exception e)
                {
                    AgLog.Error("MergeRockets patch failed: " + e);
                }
            }
        }
    }
}
