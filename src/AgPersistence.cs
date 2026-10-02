using System;
using System.Collections.Generic;
using SFS.Builds;
using SFS.Parts;
using SFS.World;
using UnityEngine;

namespace SFSActionGroupMod
{
    /// <summary>
    /// Persistence built on the CustomSaveData mod (https://github.com/AstroTheRabbit/Custom-Save-Data-SFS).
    /// It hands us the game's own save objects:
    ///   * CustomBlueprint  - on blueprint save / load / launch
    ///   * CustomRocketSave - on world save / load
    /// The action groups are stored under one id as a list of SavedSlot.
    ///
    /// Using CustomSaveData also gives us the exact Part[] that a launch created, so the groups are
    /// mapped onto the real parts instead of being guessed from the scene.
    /// </summary>
    public static class AgPersistence
    {
        private const string DataId = "sfsActionGroups";

        private static bool subscribed;
        private static bool giveUp;
        private static int retryFrames;
        private static List<SavedSlot> pendingBuild;
        private static int pendingBuildFrames;

        /// <summary>Snapshot taken the last time the game built a blueprint, used as a launch fallback.</summary>
        private static List<SavedSlot> lastBuilt;

        /// <summary>True once the CustomSaveData events are hooked.</summary>
        public static bool IsHooked
        {
            get { return subscribed; }
        }

        /// <summary>Hooks into CustomSaveData. Called once from Mod.Load(); retried from Tick()
        /// in case CustomSaveData was not ready yet at that moment.</summary>
        public static void Initialise()
        {
            if (subscribed || giveUp)
                return;

            try
            {
                CustomSaveData.CustomBlueprintHelper blueprintHelper =
                    CustomSaveData.Entrypoint.BlueprintHelper;
                blueprintHelper.OnSave += OnBlueprintSave;
                blueprintHelper.OnLoad += OnBlueprintLoad;
                blueprintHelper.OnLaunch += OnBlueprintLaunch;

                CustomSaveData.CustomRocketSaveHelper rocketHelper =
                    CustomSaveData.Entrypoint.RocketSaveHelper;
                rocketHelper.OnSave += OnRocketSave;
                rocketHelper.OnLoad += OnRocketLoad;

                subscribed = true;
                AgLog.Detail("hooked into CustomSaveData " +
                            CustomSaveData.Entrypoint.Main.ModVersion +
                            " (blueprint + world save)");
            }
            catch (Exception e)
            {
                AgLog.Warn("CustomSaveData is not ready yet (" + e.GetType().Name +
                           "), will retry - is the mod installed?");
            }
        }

        // ------------------------------------------------------------------ blueprint

        private static void OnBlueprintSave(CustomSaveData.CustomBlueprint blueprint)
        {
            try
            {
                List<SavedSlot> slots = AgSlots.CaptureBuildSlots();
                int entries = CountEntries(slots);

                // CustomSaveData calls this hook from a postfix on BuildState.GetBlueprint, i.e. every
                // time the game asks for the current blueprint - not only when the player saves a file.
                // Measured in game: a freshly entered build scene produced "action groups stored
                // (0 part(s))" while the blueprint was about to be loaded. Storing zero entries there
                // can wipe the groups an earlier session saved, so when the grid is empty and the
                // blueprint already carries data, leave it alone. (With an empty grid there is nothing
                // to bind to anyway. A player who clears every group on a craft that still has parts
                // is still stored as an empty set, because the grid is not empty in that case.)
                if (entries == 0 && !AgSlots.HasLiveBuildParts())
                {
                    List<SavedSlot> existing;
                    int kept = blueprint.GetCustomData(DataId, out existing) ? CountEntries(existing) : 0;
                    if (kept > 0)
                    {
                        AgLog.Warn("blueprint save: the build grid is empty, keeping the " + kept +
                                   " stor(ies) already in this blueprint");
                        return;
                    }
                }

                // AddCustomData uses Dictionary.Add, which throws on a duplicate key, so remove first.
                blueprint.RemoveCustomData(DataId);
                blueprint.AddCustomData(DataId, slots);

                // Keep a copy as well: when a launch happens the build scene may already be gone,
                // and the blueprint handed to OnLaunch is not always the one we stored into.
                lastBuilt = slots;
                AgLog.Detail("blueprint save: action groups stored (" + CountEntries(slots) + " part(s))");
            }
            catch (Exception e)
            {
                AgLog.Error("blueprint save failed: " + e);
            }
        }

        private static void OnBlueprintLaunch(CustomSaveData.CustomBlueprint blueprint, Rocket[] rockets, Part[] parts)
        {
            try
            {
                float rotation = blueprint != null ? blueprint.rotation : 0f;

                List<SavedSlot> saved = null;

                // 1. the data stored inside the blueprint that is being launched (the classic route)
                if (blueprint != null)
                {
                    List<SavedSlot> fromBlueprint;
                    if (blueprint.GetCustomData(DataId, out fromBlueprint) && CountEntries(fromBlueprint) > 0)
                    {
                        saved = fromBlueprint;
                        AgLog.Detail("launch: action groups read from the blueprint");
                    }
                }

                // 2. the snapshot taken the last time a blueprint was built
                if (saved == null && CountEntries(lastBuilt) > 0)
                {
                    saved = lastBuilt;
                    AgLog.Detail("launch: blueprint had no data, using the last captured snapshot");
                }

                // 3. whatever the build slots still hold
                if (saved == null)
                {
                    saved = AgSlots.CaptureBuildSlots();
                    AgLog.Detail("launch: falling back to the live build slots (" +
                                CountEntries(saved) + " part(s))");
                }

                AgLog.Detail("launch: " + (rockets == null ? 0 : rockets.Length) + " rocket(s), " +
                            (parts == null ? 0 : parts.Length) + " part(s)");

                AgSlots.MapLaunch(saved, parts, rotation);
                AgWindow.Refresh();
            }
            catch (Exception e)
            {
                AgLog.Error("launch mapping failed: " + e);
            }
        }

        private static int CountEntries(List<SavedSlot> slots)
        {
            if (slots == null)
                return 0;

            int count = 0;
            foreach (SavedSlot slot in slots)
            {
                if (slot != null && slot.entries != null)
                    count += slot.entries.Count;
            }

            return count;
        }

        /// <summary>The action groups stored inside a blueprint, or null when it carries none.
        ///
        /// A blueprint read from a file is deserialized as a CustomSaveData.CustomBlueprint (that mod
        /// patches Blueprint.TryLoad for exactly that reason), which is where the custom data lives; a
        /// blueprint built in this session is a plain Blueprint and simply has none.
        ///
        /// Used for the blueprint menu's "Import" button: unlike "Load", Import does not clear the build
        /// grid, it drops the blueprint next to the craft that is already there, which is how two
        /// blueprints are merged by hand.</summary>
        public static List<SavedSlot> ReadFromBlueprint(Blueprint blueprint)
        {
            try
            {
                CustomSaveData.CustomBlueprint custom = blueprint as CustomSaveData.CustomBlueprint;
                if (custom == null)
                    return null;

                List<SavedSlot> saved;
                if (!custom.GetCustomData(DataId, out saved))
                    return null;

                return saved;
            }
            catch (Exception e)
            {
                AgLog.Error("could not read the action groups of a blueprint: " + e);
                return null;
            }
        }

        private static void OnBlueprintLoad(CustomSaveData.CustomBlueprint blueprint)
        {
            try
            {
                List<SavedSlot> saved;
                if (!blueprint.GetCustomData(DataId, out saved) || saved == null)
                {
                    AgLog.Detail("blueprint load: no action group data in this blueprint");
                    return;
                }

                pendingBuild = saved;
                pendingBuildFrames = 0;
                AgLog.Detail("blueprint load: action groups found, waiting for the build grid");
            }
            catch (Exception e)
            {
                AgLog.Error("blueprint load failed: " + e);
            }
        }

        // ------------------------------------------------------------------ world

        private static void OnRocketSave(CustomSaveData.CustomRocketSave save, Rocket rocket)
        {
            try
            {
                // AddCustomData uses Dictionary.Add, which throws on a duplicate key, so remove first.
                save.RemoveCustomData(DataId);
                save.AddCustomData(DataId, AgSlots.CaptureRocketGroups(rocket));
                AgLog.Detail("world save: stored the action groups of rocket " + rocket.name);
            }
            catch (Exception e)
            {
                AgLog.Error("rocket save failed: " + e);
            }
        }

        private static void OnRocketLoad(CustomSaveData.CustomRocketSave save, Rocket rocket)
        {
            try
            {
                List<SavedSlot> saved;
                if (!save.GetCustomData(DataId, out saved) || saved == null)
                    return;

                AgSlots.AttachGroupsToRocket(rocket, saved);
                AgWindow.Refresh();
            }
            catch (Exception e)
            {
                AgLog.Error("rocket load failed: " + e);
            }
        }

        // ------------------------------------------------------------------ pending work

        /// <summary>Called every frame from AgRuntime.</summary>
        public static void Tick()
        {
            if (!subscribed && !giveUp)
            {
                retryFrames++;
                if (retryFrames % 60 == 0)
                {
                    Initialise();
                    if (!subscribed && retryFrames > 600)
                    {
                        giveUp = true;
                        AgLog.Error("gave up hooking into CustomSaveData - saving will not work");
                    }
                }
            }

            TickPendingBuild();
        }

        /// <summary>
        /// CustomSaveData's blueprint load callback can fire before the build grid is filled, so the
        /// restore waits a few frames for the parts to exist.
        /// </summary>
        private static void TickPendingBuild()
        {
            if (pendingBuild == null)
                return;

            if (!AgScene.IsBuild)
                return;

            pendingBuildFrames++;

            List<Part> buildParts = AgSlots.GetBuildParts();
            if (buildParts == null || buildParts.Count == 0)
            {
                if (pendingBuildFrames > 1800)
                {
                    AgLog.Warn("blueprint restore timed out (no parts in the build grid)");
                    pendingBuild = null;
                }
                return;
            }

            if (pendingBuildFrames < 4)
                return;

            List<SavedSlot> data = pendingBuild;
            pendingBuild = null;
            AgSlots.ApplyBuildRestore(data);
        }
    }
}
