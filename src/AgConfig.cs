using System;
using SFS.IO;
using SFS.Variables;
using UITools;
using UnityEngine;

namespace SFSActionGroupMod
{
    /// <summary>
    /// The values the player can change in UITools' "Mods Settings" window. Every field is one of the
    /// game's serialisable observable types, so UITools' ModSettings base can load and save the whole
    /// class as JSON in the mod folder.
    /// </summary>
    [Serializable]
    public class AgConfigData
    {
        public Float_Local windowWidth = new Float_Local(AgConfig.DefaultWidth);
        public Float_Local windowHeight = new Float_Local(AgConfig.DefaultHeight);
        public Float_Local windowOpacity = new Float_Local(AgConfig.DefaultOpacity);

        /// <summary>On: every step of the matching and the per-scene state dump are printed.
        /// Off: only milestones, warnings and errors reach the console.</summary>
        public Bool_Local debug = new Bool_Local { Value = false };
    }

    /// <summary>
    /// Settings storage. Derives from UITools' ModSettings, which loads the file on startup, saves it
    /// again, and saves once more whenever one of the observable values changes. The window reads the
    /// values through the static properties below, so a change in the settings window is picked up
    /// immediately (the values also clamp, so a hand edited file cannot break the layout).
    /// </summary>
    public class AgConfig : ModSettings<AgConfigData>
    {
        public const float DefaultWidth = 430f;
        public const float DefaultHeight = 800f;
        public const float DefaultOpacity = 0.9f;

        // The rows are inset by 30 px, and the window grows to fit its content, so the minimum only
        // has to keep the buttons and the name field usable.
        public const float MinWidth = 300f;
        public const float MaxWidth = 900f;
        public const float MinHeight = 300f;
        public const float MaxHeight = 1200f;
        public const float MinOpacity = 0.2f;
        public const float MaxOpacity = 1f;

        private static bool ready;

        public static bool Ready
        {
            get { return ready; }
        }

        /// <summary>Raised after any value changed, so the open window can follow along.</summary>
        public static event Action Changed;

        protected override FilePath SettingsFile
        {
            get
            {
                return new FolderPath(Entrypoint.Instance.ModFolder).ExtendToFile("settings.txt");
            }
        }

        protected override void RegisterOnVariableChange(Action action)
        {
            settings.windowWidth.OnChange += action;
            settings.windowHeight.OnChange += action;
            settings.windowOpacity.OnChange += action;
            settings.debug.OnChange += action;
            Application.quitting += action;
        }

        /// <summary>Call once, from the mod's Load. Never throws: if anything goes wrong the mod
        /// simply keeps working with the built in defaults.</summary>
        public static void Initialise()
        {
            if (ready)
                return;

            try
            {
                AgConfig config = new AgConfig();
                config.Initialize();
                ready = settings != null;

                // Live updates for a window that is already on screen. (Initialize already registered
                // its own handler that saves the file on every change.)
                config.RegisterOnVariableChange(NotifyChanged);

                AgLog.Detail("settings loaded: " + Describe());
            }
            catch (Exception e)
            {
                ready = false;
                AgLog.Warn("settings could not be loaded, the defaults are used: " + e);
            }
        }

        private static void NotifyChanged()
        {
            try
            {
                Action handler = Changed;
                if (handler != null)
                    handler();
            }
            catch (Exception e)
            {
                AgLog.Error("a settings change handler failed: " + e);
            }
        }

        // ------------------------------------------------------------------ values used by the mod

        public static void SetWidth(float value)
        {
            if (settings != null)
                settings.windowWidth.Value = Mathf.Clamp(value, MinWidth, MaxWidth);
        }

        public static void SetHeight(float value)
        {
            if (settings != null)
                settings.windowHeight.Value = Mathf.Clamp(value, MinHeight, MaxHeight);
        }

        public static void SetOpacity(float value)
        {
            if (settings != null)
                settings.windowOpacity.Value = Mathf.Clamp(value, MinOpacity, MaxOpacity);
        }

        public static void SetDebug(bool value)
        {
            if (settings == null)
                return;

            settings.debug.Value = value;

            // Only announce turning it on: while it is off the console stays completely silent.
            if (value)
                AgLog.Write("debug output enabled");
        }

        public static float Width
        {
            get
            {
                if (settings == null)
                    return DefaultWidth;
                return Mathf.Clamp(settings.windowWidth.Value, MinWidth, MaxWidth);
            }
        }

        /// <summary>The smallest height the window may have. The window grows beyond it when the
        /// content needs the room, so this is a floor rather than the exact size.</summary>
        public static float Height
        {
            get
            {
                if (settings == null)
                    return DefaultHeight;
                return Mathf.Clamp(settings.windowHeight.Value, MinHeight, MaxHeight);
            }
        }

        public static float Opacity
        {
            get
            {
                if (settings == null)
                    return DefaultOpacity;
                return Mathf.Clamp(settings.windowOpacity.Value, MinOpacity, MaxOpacity);
            }
        }

        public static bool Debug
        {
            get { return settings != null && settings.debug.Value; }
        }

        private static string Describe()
        {
            return "width " + Width.ToString("0") + ", height " + Height.ToString("0") +
                   ", opacity " + Opacity.ToString("0.00") +
                   ", debug " + (Debug ? "on" : "off");
        }
    }
}
