using System;
using SFS.UI.ModGUI;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Type = SFS.UI.ModGUI.Type;

namespace SFSActionGroupMod
{
    /// <summary>
    /// Adds the mod's page to UITools' "Mods Settings" window (the window the game's settings menu
    /// shows). UITools creates that window during its own Early_Load, and the loader runs every mod's
    /// Early_Load before any mod's Load, so calling Register() from Load always finds it.
    /// </summary>
    public static class AgSettingsPage
    {
        private const string ModTitle = "Action Group";

        private static bool registered;

        public static bool Registered
        {
            get { return registered; }
        }

        public static void Register()
        {
            if (registered)
                return;

            try
            {
                ConfigurationMenu.Add(ModTitle,
                    new ValueTuple<string, Func<Transform, GameObject>>[]
                    {
                        new ValueTuple<string, Func<Transform, GameObject>>(
                            "GUI", new Func<Transform, GameObject>(CreateGuiPage)),
                        new ValueTuple<string, Func<Transform, GameObject>>(
                            "Misc", new Func<Transform, GameObject>(CreateMiscPage))
                    });

                registered = true;
                AgLog.Detail("settings page added to the Mods Settings window");
            }
            catch (Exception e)
            {
                AgLog.Warn("could not add the settings page yet: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ GUI page

        private static GameObject CreateGuiPage(Transform parent)
        {
            Box box = MakePage(parent, "GUI Settings");
            int width = RowWidth();

            AddSection(box, width, "Window");
            AddSlider(box, width, "Window Width",
                      AgConfig.MinWidth, AgConfig.MaxWidth, AgConfig.Width, true,
                      AgConfig.SetWidth, delegate (float value) { return value.ToString("0"); });

            AddSlider(box, width, "Window Height",
                      AgConfig.MinHeight, AgConfig.MaxHeight, AgConfig.Height, true,
                      AgConfig.SetHeight, delegate (float value) { return value.ToString("0"); });

            AddSlider(box, width, "Window Opacity",
                      AgConfig.MinOpacity, AgConfig.MaxOpacity, AgConfig.Opacity, false,
                      AgConfig.SetOpacity, delegate (float value) { return value.ToString("0.00"); });

            Builder.CreateLabel(box, width, 60, 0, 0,
                "The window follows its contents: it is only as tall as it has\n" +
                "to be, and never taller than Window Height.");

            return box.gameObject;
        }

        // ------------------------------------------------------------------ misc page

        private static GameObject CreateMiscPage(Transform parent)
        {
            Box box = MakePage(parent, "Misc Settings");
            int width = RowWidth();

            AddSection(box, width, "Diagnostics");
            AddToggle(box, width, "Debug",
                      delegate { return AgConfig.Debug; },
                      delegate { AgConfig.SetDebug(!AgConfig.Debug); });

            Builder.CreateLabel(box, width, 60, 0, 0,
                "On: every matching step and the state dump are printed.\n" +
                "Off: only the important lines reach the console.");

            return box.gameObject;
        }

        // ------------------------------------------------------------------ building blocks

        /// <summary>Creates the page root: a box with a vertical layout group, the same shape the pages
        /// of the other installed mods use. The width comes from UITools, so it always fits.</summary>
        private static Box MakePage(Transform parent, string title)
        {
            Vector2Int content = ConfigurationMenu.ContentSize;
            Box box = Builder.CreateBox(parent, content.x, content.y, 0, 0, 0.3f);
            box.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperCenter, 20f,
                                  new RectOffset(0, 0, 5, 5), true);

            Builder.CreateLabel(box, RowWidth(), 40, 0, 0, title);
            return box;
        }

        /// <summary>Rows are inset by 50 px, which is what the other mods' pages do.</summary>
        private static int RowWidth()
        {
            int width = ConfigurationMenu.ContentSize.x - 50;
            return width < 100 ? 100 : width;
        }

        private static void AddSection(Box box, int width, string title)
        {
            Builder.CreateSeparator(box, width, 0, 0);
            Label label = Builder.CreateLabel(box, width, 36, 0, 0, title);
            label.TextAlignment = TextAlignmentOptions.Left;
        }

        /// <summary>The third argument of Builder.CreateSlider is the value the slider starts at
        /// (verified against the "GUI Settings" page of the Part Editor mod), the fourth is the range.</summary>
        private static void AddSlider(Box box, int width, string title, float min, float max,
                                      float current, bool wholeNumbers,
                                      Action<float> apply, Func<float, string> show)
        {
            Label label = Builder.CreateLabel(box, width, 30, 0, 0, title);
            label.TextAlignment = TextAlignmentOptions.Left;

            Builder.CreateSlider(box, width, Clamp(current, min, max),
                                 new ValueTuple<float, float>(min, max), wholeNumbers,
                                 new UnityAction<float>(delegate (float changed) { apply(changed); }),
                                 show);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static void AddToggle(Box box, int width, string title, Func<bool> get, Action toggle)
        {
            Builder.CreateToggleWithLabel(box, width, 35, get, toggle, 0, 0, title);
        }
    }
}
