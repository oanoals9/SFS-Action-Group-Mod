using UnityEngine;

namespace SFSActionGroupMod
{
    /// <summary>
    /// Polls the ten number keys. In the build scene a key selects that slot,
    /// in the world scene a key triggers that slot (same result as clicking the parts).
    ///
    /// It also dumps the full state to the console once per scene per game session, so a single test
    /// run produces all the information needed to diagnose a problem.
    /// </summary>
    public class AgRuntime : MonoBehaviour
    {
        private const int DumpAfterFramesBuild = 150;
        private const int DumpAfterFramesWorld = 240;
        private const int SelfHealAfterFrames = 120;

        private static bool dumpedBuild;
        private static bool dumpedWorld;

        private int framesInScene;

        private void Update()
        {
            try
            {
                AgPersistence.Tick();
                AgWindow.Tick();
                AgClipboard.Tick();
                AgWindow.HandleScrollInput();
                framesInScene++;

                if (AgScene.IsWorld && framesInScene >= SelfHealAfterFrames)
                    AgSlots.TrySelfHeal();

                // In flight the groups are refreshed now and then: a stage that separated takes its
                // parts with it, and a group that has nothing left goes back to its default name.
                if (AgScene.IsWorld && framesInScene % 60 == 0)
                    AgSlots.PruneCurrentRocket();

                // Switching to another rocket must show that rocket's groups right away.
                AgSlots.WatchCurrentRocket();

                MaybeAutoDump();

                if (AgWindow.IsTyping)
                    return;

                for (int i = 0; i < AgSlots.Count; i++)
                {
                    if (!Input.GetKeyDown(KeyFor(i)))
                        continue;

                    if (AgScene.IsWorld)
                        AgSlots.Activate(i);
                    else if (AgScene.IsBuild)
                        AgSlots.SelectOnly(i);
                }
            }
            catch (System.Exception e)
            {
                AgLog.Error("AgRuntime.Update failed: " + e);
            }
        }

        private void MaybeAutoDump()
        {
            // The state dump is a diagnostic, so the player is allowed to turn it off in the settings.
            if (!AgConfig.Debug)
                return;

            if (AgScene.IsBuild)
            {
                if (dumpedBuild || framesInScene < DumpAfterFramesBuild)
                    return;

                dumpedBuild = true;
                AgLog.Detail("automatic state dump (build scene) - see below");
                AgSlots.DumpState();
            }
            else if (AgScene.IsWorld)
            {
                if (dumpedWorld || framesInScene < DumpAfterFramesWorld)
                    return;

                dumpedWorld = true;
                AgLog.Detail("automatic state dump (world scene) - see below");
                AgSlots.DumpState();
            }
        }

        /// <summary>Slot 0..8 are the keys 1..9, slot 9 is the key 0 (top row only).</summary>
        private static KeyCode KeyFor(int slotIndex)
        {
            if (slotIndex == 9)
                return KeyCode.Alpha0;
            return KeyCode.Alpha1 + slotIndex;
        }
    }
}
