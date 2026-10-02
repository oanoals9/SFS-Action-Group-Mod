using System;
using System.Collections.Generic;
using HarmonyLib;
using ModLoader;
using ModLoader.Helpers;
using UnityEngine;

namespace SFSActionGroupMod
{
    /// <summary>
    /// Mod entry point. The game's built-in loader (ModLoader.Mod in Assembly-CSharp.dll)
    /// creates this class and calls Early_Load() then Load().
    /// </summary>
    public class Entrypoint : Mod
    {
        public static Entrypoint Instance { get; private set; }

        public Entrypoint()
        {
            Instance = this;
        }

        public override string ModNameID => "sfsactiongroupmod";
        public override string DisplayName => "Action Group";
        public override string Author => "S.S9";
        public override string MinimumGameVersionNecessary => "1.6.0.18";
        public override string ModVersion => "0.9.2";
        public override string Description => "KSP-like action groups. Assign parts to 10 slots and trigger them with the number keys 1-0.";
        public override string IconLink => null;
        public override Action LoadKeybindings => null;

        public override Dictionary<string, string> Dependencies
        {
            get
            {
                return new Dictionary<string, string>
                {
                    { "UITools", "1.1.6" },
                    { "customsavedata", "1.4" }
                };
            }
        }

        public override void Early_Load()
        {
            // Patch class by class so a single failing patch (for example after a game update)
            // cannot stop the whole mod from loading.
            int applied = 0;
            int failed = 0;
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            Harmony harmony = new Harmony(ModNameID);

            foreach (Type type in assembly.GetTypes())
            {
                try
                {
                    if (type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length == 0 &&
                        type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatchAll), true).Length == 0)
                    {
                        continue;
                    }

                    harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (Exception e)
                {
                    failed++;
                    AgLog.Error("patch class " + type.FullName + " failed: " + e);
                }
            }

            AgLog.Detail("Early_Load finished (" + applied + " patch class(es) applied, " + failed + " failed)");
        }

        public override void Load()
        {
            AgConfig.Initialise();
            AgSettingsPage.Register();
            AgConfig.Changed += AgWindow.ApplySettings;

            AgPersistence.Initialise();
            SceneHelper.OnBuildSceneLoaded += AgScene.OnBuildLoaded;
            SceneHelper.OnBuildSceneUnloaded += AgScene.OnBuildUnloaded;
            SceneHelper.OnWorldSceneLoaded += AgScene.OnWorldLoaded;
            SceneHelper.OnWorldSceneUnloaded += AgScene.OnWorldUnloaded;
            AgLog.Detail("Load finished (scene hooks registered)");
        }
    }

    /// <summary>Small logging helper so every message is easy to grep in the F1 console.
    /// Write/Warn/Error always print; Detail only prints while the Debug setting is on, which is what
    /// keeps the console quiet during normal play (the per-part matching steps use Detail).</summary>
    public static class AgLog
    {
        private const string Prefix = "[SFSAG] ";

        public static void Write(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Detail(string message)
        {
            if (!AgConfig.Debug)
                return;

            Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
