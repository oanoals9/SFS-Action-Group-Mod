using System;
using System.Collections.Generic;
using System.Text;
using SFS.Input;
using SFS.Parts;
using SFS.UI.ModGUI;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Button = SFS.UI.ModGUI.Button;
using GUIElement = SFS.UI.ModGUI.GUIElement;
using Type = SFS.UI.ModGUI.Type;

namespace SFSActionGroupMod
{
    /// <summary>
    /// The mod's window. It is rebuilt from scratch every time a scene is loaded, using the size and
    /// opacity from the settings page in UITools' "Mods Settings" window.
    ///
    /// The list of parts is scrolled by moving a window of lines through it: only as many lines as fit
    /// the available height are written into the label, and the mouse wheel (or a drag) moves that
    /// window. The label is a normal row of the window, so the text can never escape or disappear.
    /// </summary>
    public static class AgWindow
    {
        private const string Title = "Action Group";

        private const int RowInset = 30;
        private const int SlotRowHeight = 28;
        private const int ActionButtonHeight = 30;
        private const int StatusHeight = 44;
        private const int NameHintHeight = 22;
        private const int NameInputHeight = 34;

        /// <summary>The list of parts never gets less than this, and never asks for more than the
        /// rest of the window can spare.</summary>
        private const int MinListHeight = 60;

        /// <summary>Spacing and padding of the window's layout group, needed to predict how much room
        /// is left for the list of parts.</summary>
        private const float RowSpacing = 4f;
        private const int PaddingVertical = 8;

        /// <summary>How many lines the list shows at least, and the safety limit for a single slot.</summary>
        private const int MinListedLines = 3;
        private const int MaxListedLines = 400;

        /// <summary>Font size of the list of parts, in the units this UI is designed in. It is pinned
        /// instead of being left to TextMeshPro's auto sizing, so the text looks the same whether a slot
        /// holds two parts or forty.</summary>
        private const float PartsFontSize = 14f;

        /// <summary>Only used when the rendered text cannot be measured at all.</summary>
        private const float FallbackLineFactor = 1.35f;

        /// <summary>Blank space between the name field and the list of parts. The name field's prefab
        /// draws a little below the row it is given, so without this the first line of the list would
        /// sit on top of it.</summary>
        private const int ListGap = 18;

        private static GameObject holder;
        private static Window window;
        private static ClosableWindow closable;
        private static Label labelStatus;
        private static Label labelParts;
        private static TextInput inputName;
        private static readonly Button[] slotButtons = new Button[AgSlots.Count];
        private static bool creating;
        private static bool isBuildScene;

        /// <summary>Every element that spans the row width, with its own height, so the width can be
        /// changed while the window is open.</summary>
        private static readonly List<GUIElement> rows = new List<GUIElement>();
        private static readonly List<float> rowHeights = new List<float>();

        /// <summary>Index in <see cref="rows"/> of the list of parts, whose height is computed.</summary>
        private static int partsRow = -1;

        /// <summary>First line of the list that is currently shown (0 = top of the list).</summary>
        private static int scrollLine;

        /// <summary>Index of the last slot the list was built for, used to jump back to the top when
        /// the player switches slots.</summary>
        private static int listedSlot = -2;

        /// <summary>Size of the flight status light that sits beside each slot row, the gap in front of
        /// it, and how often (in frames) its colour is refreshed.</summary>
        private const int LightWidth = 26;
        private const int LightGap = 6;
        private const int LightHeight = 20;
        private const int LightInterval = 8;

        private static readonly Color LightOn = new Color(0.16f, 0.85f, 0.24f, 1f);
        private static readonly Color LightOff = new Color(0.85f, 0.16f, 0.16f, 1f);

        /// <summary>Where the mouse was when the current drag through the list started.</summary>
        private static Vector2? dragAnchor;

        /// <summary>Flight only: the green/red light of each slot, and the row that holds it.</summary>
        private static readonly Box[] slotLights = new Box[AgSlots.Count];
        private static readonly Container[] slotRows = new Container[AgSlots.Count];
        private static int lightTick;

        /// <summary>Height of one rendered line of the list, measured while it is filled.</summary>
        private static float lineHeight = PartsFontSize * FallbackLineFactor;

        /// <summary>True while the layout is being applied, so row resizing does not recurse.</summary>
        private static bool layingOut;

        /// <summary>True while the player is typing in the name field (or the game has one of its own text
        /// boxes open), so the number keys are left alone - otherwise renaming a slot would trigger the
        /// groups behind the field.</summary>
        public static bool IsTyping
        {
            get
            {
                try
                {
                    // The mod's own field: TextMeshPro knows whether it has the caret.
                    if (inputName != null && inputName.field != null && inputName.field.isFocused)
                        return true;
                }
                catch (Exception e)
                {
                    AgLog.Detail("could not read the name field state: " + e.Message);
                }

                try
                {
                    // One of the game's own text boxes is open (it sits on top of the input screen stack).
                    ScreenManager manager = ScreenManager.main;
                    if (manager != null && manager.CurrentScreen is TextBox)
                        return true;
                }
                catch (Exception e)
                {
                    AgLog.Detail("could not read the input screen state: " + e.Message);
                }

                EventSystem system = EventSystem.current;
                if (system == null)
                    return false;

                GameObject selected = system.currentSelectedGameObject;
                if (selected == null)
                    return false;

                return selected.GetComponent("TMP_InputField") != null
                    || selected.GetComponent("InputField") != null;
            }
        }

        public static void CreateInScene(bool isBuild)
        {
            if (creating)
                return;

            creating = true;
            try
            {
                Destroy();

                isBuildScene = isBuild;
                int width = (int)AgConfig.Width;
                int height = (int)AgConfig.Height;
                float opacity = AgConfig.Opacity;

                holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSAG Holder");
                holder.AddComponent<AgRuntime>();

                int id = Builder.GetRandomID();

                // Prefer UITools' closable window, but do not depend on it: if its API ever changes,
                // fall back to the game's own window builder.
                try
                {
                    closable = UIToolsBuilder.CreateClosableWindow(
                        holder.transform, id,
                        width, height,
                        0, 0, true, true, opacity, Title);
                    window = closable;
                }
                catch (Exception e)
                {
                    AgLog.Warn("UITools' window failed (" + e.Message + "), using the game's own window");
                    closable = null;
                    window = null;
                }

                if (window == null)
                {
                    window = Builder.CreateWindow(
                        holder.transform, id,
                        width, height,
                        0, 0, true, true, opacity, Title);
                }

                if (window == null)
                {
                    AgLog.Error("could not create the window");
                    return;
                }

                try
                {
                    PositionSaver.RegisterPermanentSaving(window, PositionKey());
                }
                catch (Exception e)
                {
                    AgLog.Warn("window position will not be remembered: " + e.Message);
                }

                window.CreateLayoutGroup(Type.Vertical, TextAnchor.UpperCenter, RowSpacing,
                    new RectOffset(10, 10, PaddingVertical, PaddingVertical), true);

                // Safety net: if the content is still taller than the window, the window itself can be
                // scrolled as well (this only works when the window prefab carries a ScrollElement).
                try
                {
                    window.EnableScrolling(Type.Vertical);
                }
                catch (Exception e)
                {
                    AgLog.Detail("the window itself cannot be scrolled: " + e.Message);
                }

                rows.Clear();
                rowHeights.Clear();
                int rowWidth = RowWidth();

                labelStatus = (Label)AddRow(Builder.CreateLabel(window, rowWidth, StatusHeight, 0, 0, ""),
                                            StatusHeight);
                AddRow(Builder.CreateSeparator(window, rowWidth, 0, 0), -1);

                for (int i = 0; i < AgSlots.Count; i++)
                {
                    int index = i; // capture for the closure

                    if (!isBuild)
                    {
                        // In flight the row is [button][light]: the light sits beside the row, not in it.
                        Container row = Builder.CreateContainer(window, rowWidth, SlotRowHeight);
                        if (row != null)
                        {
                            row.CreateLayoutGroup(Type.Horizontal, TextAnchor.MiddleRight, LightGap,
                                                  new RectOffset(0, 0, 0, 0), true);
                            slotRows[i] = row;
                        }

                        Transform parent = row != null ? row : (Transform)window;
                        int buttonWidth = rowWidth - LightWidth - LightGap;

                        slotButtons[i] = Builder.CreateButton(parent, buttonWidth, SlotRowHeight, 0, 0,
                            delegate { AgSlots.Select(index); }, "");
                        slotLights[i] = Builder.CreateBox(parent, LightWidth, LightHeight, 0, 0, 1f);

                        AddRow(row, SlotRowHeight);
                    }
                    else
                    {
                        slotButtons[i] = (Button)AddRow(Builder.CreateButton(
                            window, rowWidth, SlotRowHeight, 0, 0,
                            delegate { AgSlots.Select(index); },
                            ""), SlotRowHeight);
                    }
                }

                AddRow(Builder.CreateSeparator(window, rowWidth, 0, 0), -1);
                AddRow(Builder.CreateLabel(window, rowWidth, NameHintHeight, 0, 0,
                    isBuild
                        ? "Slot name (the selected slot only):"
                        : "Slot name (writes to this rocket only):"), NameHintHeight);
                inputName = (TextInput)AddRow(
                    Builder.CreateTextInput(window, rowWidth, NameInputHeight, 0, 0, "", OnNameChanged),
                    NameInputHeight);

                // The list of parts is an ordinary row of the window: the scrolling window of lines is
                // written into it, so there is nothing that could clip or hide the text. The text is
                // left aligned, which is the only way a list of parts is readable.
                // The spacer keeps the first line clear of the name field, whose prefab is a little
                // taller than the row it is given.
                AddRow(Builder.CreateLabel(window, rowWidth, ListGap, 0, 0, ""), ListGap);

                partsRow = rows.Count;
                labelParts = (Label)AddRow(
                    Builder.CreateLabel(window, rowWidth, MinListHeight, 0, 0, ""), MinListHeight);
                if (labelParts != null)
                {
                    try
                    {
                        // Left aligned (and anchored to the top, like a list), and with a pinned font:
                        // auto sizing would grow the text to fill the box, which made the font depend on
                        // how many parts the slot has.
                        labelParts.TextAlignment = TextAlignmentOptions.TopLeft;
                        labelParts.AutoFontResize = false;
                        labelParts.FontSize = PartsFontSize;
                    }
                    catch (Exception e)
                    {
                        AgLog.Detail("could not set up the list of parts text: " + e.Message);
                    }
                }

                // In flight there is no empty space to click, so the Deselect button only exists there.
                if (!isBuild)
                {
                    AddRow(Builder.CreateButton(window, rowWidth, ActionButtonHeight, 0, 0,
                        delegate { AgSlots.Deselect(); }, "Deselect (parts behave normally)"),
                        ActionButtonHeight);
                }

                AddRow(Builder.CreateButton(window, rowWidth, ActionButtonHeight, 0, 0,
                    OnClearClicked, "Clear selected slot"), ActionButtonHeight);
                AddRow(Builder.CreateButton(window, rowWidth, ActionButtonHeight, 0, 0,
                    OnClearAllClicked, "Clear all action groups"), ActionButtonHeight);

                // Only the build grid has slot names the player can rename in bulk; in flight the names
                // belong to the rocket and are edited one at a time.
                if (isBuild)
                {
                    AddRow(Builder.CreateButton(window, rowWidth, ActionButtonHeight, 0, 0,
                        OnResetNamesClicked, "Reset all slot names"), ActionButtonHeight);
                }

                AgLog.Detail("window created (" + (isBuild ? "build" : "world") + " scene, " +
                             width + "x" + height + ")");
                listedSlot = -2; // force a scroll back to the top on the first fill

                // Paint the flight lights on the very next frame instead of after the usual delay:
                // a fresh Box is white, which would look like a third state.
                lightTick = LightInterval;

                Refresh();
            }
            catch (Exception e)
            {
                AgLog.Error("CreateInScene failed: " + e);
            }
            finally
            {
                creating = false;
            }
        }

        /// <summary>Applies the settings to a window that is already on screen, so the sliders act live.
        /// Does nothing when no window exists (main menu, or before a scene loaded).</summary>
        public static void ApplySettings()
        {
            try
            {
                if (window == null)
                    return;

                window.WindowOpacity = AgConfig.Opacity;
                Refresh();
            }
            catch (Exception e)
            {
                AgLog.Warn("could not apply the settings to the open window: " + e.Message);
            }
        }

        private static string PositionKey()
        {
            return Entrypoint.Instance.ModNameID + "." + (isBuildScene ? "build" : "world");
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Gives every row the window's width, sizes the list of parts so that it fits what
        /// the rest of the window leaves over, and sizes the window itself.</summary>
        private static void Layout()
        {
            if (window == null || layingOut)
                return;

            layingOut = true;
            try
            {
                int width = RowWidth();
                float otherRows = SumRowHeightsExceptPartsList();
                float otherSpacing = RowSpacing * Mathf.Max(0, rows.Count - 1);

                // Room the list may have if the window is at the height the player allows.
                float room = Mathf.Max(AgConfig.Height, AgConfig.MinHeight)
                             - otherRows - otherSpacing - (PaddingVertical * 2f);
                if (room < MinListHeight)
                    room = MinListHeight;

                float listHeight = FillPartsList(width, room);

                if (partsRow >= 0 && partsRow < rows.Count)
                    rowHeights[partsRow] = listHeight;

                // The window follows the content: a slot with two parts gets a short window and the
                // buttons sit right below the text, a long list makes it grow up to the height the
                // player set (and the list scrolls if even that is not enough).
                float contentWindow = otherRows + listHeight + otherSpacing + (PaddingVertical * 2f);
                float height = Mathf.Clamp(contentWindow, AgConfig.MinHeight, AgConfig.MaxHeight);

                Vector2 size = new Vector2(AgConfig.Width, height);
                if (closable != null)
                    closable.Size = size;
                else
                    window.Size = size;

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i] == null)
                        continue;

                    float rowHeight = i < rowHeights.Count ? rowHeights[i] : -1f;
                    if (rowHeight <= 0f)
                        rowHeight = rows[i].Size.y;

                    rows[i].Size = new Vector2(width, rowHeight);
                }

                // The buttons inside the flight rows follow the row width, so the lights stay outside.
                for (int i = 0; i < AgSlots.Count; i++)
                {
                    if (slotRows[i] != null && slotButtons[i] != null)
                        slotButtons[i].Size = new Vector2(width - LightWidth - LightGap, SlotRowHeight);
                }
            }
            catch (Exception e)
            {
                AgLog.Warn("could not lay out the window: " + e.Message);
            }
            finally
            {
                layingOut = false;
            }
        }

        private static float SumRowHeightsExceptPartsList()
        {
            float total = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null || i == partsRow)
                    continue;

                float height = i < rowHeights.Count ? rowHeights[i] : -1f;
                if (height <= 0f)
                    height = rows[i].Size.y;

                total += height;
            }
            return total;
        }

        /// <summary>Height of one rendered line of the list. The font is pinned when the window is built,
        /// so this measurement is stable: it depends only on the font and on how many lines the text
        /// wrapped into (that is why the whole list is measured and divided by its line count).</summary>
        private static float MeasureLineHeight(int lineCount)
        {
            try
            {
                if (labelParts != null)
                {
                    TMP_Text text = labelParts.gameObject.GetComponentInChildren<TMP_Text>();
                    if (text != null)
                    {
                        text.ForceMeshUpdate();
                        float height = text.preferredHeight;
                        if (height > 1f && lineCount > 0)
                            return height / lineCount;
                    }
                }
            }
            catch (Exception e)
            {
                AgLog.Detail("could not measure the list of parts: " + e.Message);
            }

            return PartsFontSize * FallbackLineFactor;
        }

        /// <summary>Writes the visible part of the list of parts and returns the height the label got.
        /// The entries that do not fit are not written at all; the wheel or a drag moves the window of
        /// lines instead.</summary>
        private static float FillPartsList(int width, float room)
        {
            if (labelParts == null)
                return MinListHeight;

            int selected = AgSlots.Highlighted;
            if (selected < 0)
            {
                labelParts.Size = new Vector2(width, MinListHeight);
                labelParts.Text = AgScene.IsWorld
                    ? "Select a slot to see its parts."
                    : "Select a slot to see (or edit) its parts.";
                return MinListHeight;
            }

            Slot slot = AgSlots.ViewSlot(selected);
            if (slot == null)
            {
                labelParts.Size = new Vector2(width, MinListHeight);
                labelParts.Text = AgScene.IsWorld
                    ? "This rocket has no action groups yet."
                    : "Slot not available.";
                return MinListHeight;
            }

            List<string> lines = new List<string>();
            if (AgScene.IsWorld)
            {
                for (int i = 0; i < slot.entries.Count && lines.Count < MaxListedLines; i++)
                {
                    string line = slot.entries[i].Format(i + 1);
                    if (i >= slot.parts.Count || slot.parts[i] == null)
                        line += "   [not found]";
                    lines.Add(line);
                }
            }
            else
            {
                foreach (Part part in slot.parts)
                {
                    if (lines.Count >= MaxListedLines)
                        break;
                    if (part == null)
                        continue;

                    lines.Add(AgSlots.MakeEntry(part).Format(lines.Count + 1));
                }
            }

            // The font is pinned (auto sizing is switched off when the window is built), so the height
            // of the box has no effect on the text. That makes it safe to measure the rendered text,
            // which is what gives the exact height of one line - including wrapped lines.
            labelParts.Size = new Vector2(width, room);
            labelParts.Text = "Parts in this slot: " + lines.Count + "\n" +
                              string.Join("\n", lines.ToArray());
            float measuredLine = MeasureLineHeight(lines.Count + 1);
            lineHeight = measuredLine;

            // One line for the heading and one for the "more below" note, so the entries that fit are
            // what is left over.
            int perView = Mathf.FloorToInt((room - 4f) / measuredLine) - 2;
            if (perView < MinListedLines)
                perView = MinListedLines;
            if (perView > MaxListedLines)
                perView = MaxListedLines;

            int maxFirst = Mathf.Max(0, lines.Count - perView);
            scrollable = maxFirst;
            if (scrollLine > maxFirst)
                scrollLine = maxFirst;
            if (scrollLine < 0)
                scrollLine = 0;

            int last = Mathf.Min(lines.Count, scrollLine + perView);
            bool shortened = last < lines.Count || scrollLine > 0;

            StringBuilder builder = new StringBuilder();
            builder.Append("Parts in this slot: ").Append(lines.Count);
            if (lines.Count > perView)
            {
                builder.Append("   (").Append(scrollLine + 1).Append('-').Append(last)
                       .Append(" of ").Append(lines.Count).Append(", scroll)");
            }
            builder.Append('\n');

            for (int i = scrollLine; i < last; i++)
                builder.Append(lines[i]).Append('\n');

            if (shortened)
            {
                builder.Append("... ");
                if (last < lines.Count)
                    builder.Append(lines.Count - last).Append(" more below");
                if (last < lines.Count && scrollLine > 0)
                    builder.Append(", ");
                if (scrollLine > 0)
                    builder.Append(scrollLine).Append(" above");
                builder.Append('\n');
            }

            labelParts.Text = builder.ToString();

            // The box hugs the text it holds (plus a little padding), so the buttons below follow right
            // after the last line instead of leaving a large empty area. It only grows up to the room
            // the window allows, and then the list scrolls.
            int usedLines = 1 + (last - scrollLine) + (shortened ? 1 : 0);
            float wanted = (usedLines * measuredLine) + 4f;
            float height = Mathf.Clamp(wanted, MinListHeight, room);
            labelParts.Size = new Vector2(width, height);
            return height;
        }
        private static int RowWidth()
        {
            float width = AgConfig.Width - RowInset;
            return width < 120f ? 120 : (int)width;
        }

        /// <summary>Remembers an element so its width and height can follow the window, and gives it
        /// back for the caller to keep a typed reference to.</summary>
        private static GUIElement AddRow(GUIElement element, float height)
        {
            if (element != null)
            {
                rows.Add(element);
                rowHeights.Add(height);
            }
            return element;
        }

        // ------------------------------------------------------------------ scrolling

        /// <summary>Moves the window of visible lines with the mouse wheel, or by dragging inside the
        /// list. Called every frame from AgRuntime, and does nothing unless the pointer is over the
        /// list and there is something to scroll.</summary>
        public static void HandleScrollInput()
        {
            try
            {
                if (labelParts == null || scrollable <= 0)
                    return;

                if (!PointerOnWindow())
                {
                    dragAnchor = null;
                    return;
                }

                int step = 0;

                float wheel = Input.mouseScrollDelta.y;
                if (wheel >= 0.5f)
                    step -= 1;
                else if (wheel <= -0.5f)
                    step += 1;

                if (Input.GetMouseButton(0))
                    step += DragLines();
                else
                    dragAnchor = null;

                if (step == 0)
                    return;

                int wanted = Mathf.Clamp(scrollLine + step, 0, scrollable);
                if (wanted == scrollLine)
                    return;

                scrollLine = wanted;
                Layout();
            }
            catch (Exception e)
            {
                AgLog.Detail("scrolling failed: " + e.Message);
            }
        }

        /// <summary>How many more lines the list holds below the visible ones (0 = nothing to scroll).</summary>
        private static int scrollable;

        /// <summary>Turns the drag of the mouse into whole lines, so dragging stays in step with the
        /// text that is written into the label.</summary>
        private static int DragLines()
        {
            Vector2 now = Input.mousePosition;
            if (dragAnchor == null)
            {
                dragAnchor = now;
                return 0;
            }

            float delta = now.y - dragAnchor.Value.y;
            float perLine = lineHeight > 4f ? lineHeight : PartsFontSize * FallbackLineFactor;

            if (Mathf.Abs(delta) < perLine)
                return 0;

            int lines = (int)(delta / perLine);
            dragAnchor = new Vector2(now.x, now.y - (delta - (lines * perLine)));
            return lines;
        }

        /// <summary>True when the pointer is over the list, or anywhere else on this window. The whole
        /// window counts, so the wheel keeps working even if the rect of the list itself cannot be
        /// measured (the game's UI layout is not ours to control).</summary>
        private static bool PointerOnWindow()
        {
            if (labelParts != null && PointerInside(labelParts.rectTransform))
                return true;

            return window != null && PointerInside(window.rectTransform);
        }

        private static bool PointerInside(RectTransform rect)
        {
            try
            {
                if (rect == null)
                    return false;

                Canvas canvas = rect.GetComponentInParent<Canvas>();
                Camera camera = canvas != null ? canvas.worldCamera : null;

                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rect, Input.mousePosition, camera, out local))
                {
                    return false;
                }

                return rect.rect.Contains(local);
            }
            catch (Exception e)
            {
                AgLog.Detail("could not test the pointer position: " + e.Message);
                return false;
            }
        }

        public static void Destroy()
        {
            try
            {
                if (holder != null)
                    UnityEngine.Object.Destroy(holder);
            }
            catch (Exception e)
            {
                AgLog.Warn("Destroy failed: " + e.Message);
            }

            holder = null;
            window = null;
            closable = null;
            labelStatus = null;
            labelParts = null;
            inputName = null;
            partsRow = -1;
            scrollLine = 0;
            scrollable = 0;
            dragAnchor = null;
            listedSlot = -2;
            lightTick = 0;
            rows.Clear();
            rowHeights.Clear();
            for (int i = 0; i < slotButtons.Length; i++)
            {
                slotButtons[i] = null;
                slotLights[i] = null;
                slotRows[i] = null;
            }
        }

        /// <summary>Recolours the flight status lights: green while the group is switched on, red while it
        /// is not. Called every frame from AgRuntime, but only does the work a few times per second,
        /// because reading the state of every part of every group is not free.</summary>
        public static void Tick()
        {
            AgSlots.TickFlashes();

            if (isBuildScene || window == null)
                return;

            lightTick++;
            if (lightTick < LightInterval)
                return;

            lightTick = 0;

            for (int i = 0; i < AgSlots.Count; i++)
            {
                Box light = slotLights[i];
                if (light == null)
                    continue;

                try
                {
                    light.Color = AgSlots.IsSlotLit(i) ? LightOn : LightOff;
                }
                catch (Exception e)
                {
                    AgLog.Detail("could not colour an indicator light: " + e.Message);
                }
            }
        }

        private static void OnNameChanged(string newName)
        {
            int selected = AgSlots.Highlighted;
            if (selected < 0 || selected >= AgSlots.Count)
                return;

            Slot slot = AgSlots.ViewSlot(selected);
            if (slot == null)
                return;

            // The name is truncated here instead of through the field's own character limit, so that no
            // "characters left" counter is shown while typing.
            string clamped = AgSlots.ClampName(newName);
            slot.name = clamped;

            if (clamped != newName && inputName != null)
            {
                string shown = inputName.Text;
                if (shown != clamped)
                    inputName.Text = clamped;
            }

            RefreshSlotButtons();
        }

        private static void OnClearClicked()
        {
            int selected = AgSlots.Highlighted;
            if (selected < 0)
            {
                AgSlots.Status = "Select a slot first.";
                RefreshStatus();
                return;
            }

            if (AgScene.IsWorld)
            {
                Slot slot = AgSlots.ViewSlot(selected);
                if (slot == null)
                    return;

                slot.parts.Clear();
                slot.entries.Clear();
                AgSlots.Status = "Cleared slot " + AgSlots.KeyLabel(selected) + " on this rocket.";
                Refresh();
                return;
            }

            AgSlots.ClearSlot(selected);
        }

        private static void OnClearAllClicked()
        {
            try
            {
                AgSlots.ClearAll();
            }
            catch (Exception e)
            {
                AgLog.Error("could not clear all action groups: " + e);
            }
        }

        private static void OnResetNamesClicked()
        {
            try
            {
                AgSlots.ResetAllNames();
            }
            catch (Exception e)
            {
                AgLog.Error("could not reset the slot names: " + e);
            }
        }

        public static void Refresh()
        {
            if (window == null)
                return;

            try
            {
                RefreshStatus();
                RefreshSlotButtons();
                RefreshNameField();

                // Another slot: start its list at the top again.
                if (AgSlots.Highlighted != listedSlot)
                {
                    listedSlot = AgSlots.Highlighted;
                    scrollLine = 0;
                }

                Layout();
            }
            catch (Exception e)
            {
                AgLog.Error("Refresh failed: " + e);
            }
        }

        private static void RefreshStatus()
        {
            if (labelStatus == null)
                return;

            string header = AgSlots.Highlighted < 0
                ? "No slot selected."
                : "Selected: [" + AgSlots.KeyLabel(AgSlots.Highlighted) + "] " +
                  AgSlots.ViewName(AgSlots.Highlighted);

            labelStatus.Text = header + "\n" + AgSlots.Status;
        }

        private static void RefreshSlotButtons()
        {
            for (int i = 0; i < AgSlots.Count; i++)
            {
                if (slotButtons[i] == null)
                    continue;

                Slot slot = AgSlots.ViewSlot(i);
                int count = slot != null ? slot.Count : 0;
                string marker = AgSlots.Highlighted == i ? ">>" : "  ";
                slotButtons[i].Text = marker + " [" + AgSlots.KeyLabel(i) + "]  " +
                                      AgSlots.ViewName(i) + "   (" + count + " parts)";
            }
        }

        private static void RefreshNameField()
        {
            if (inputName == null)
                return;

            // Never fight the player: while the field has the caret, whatever is in it is what they are
            // typing. Writing the stored name back here would undo the rename (and the write itself
            // fires OnChange again, which would put the old name back into the slot).
            if (NameFieldFocused())
                return;

            int selected = AgSlots.Highlighted;
            string wanted = selected < 0 ? "" : AgSlots.ViewName(selected);

            if (inputName.Text != wanted)
                inputName.Text = wanted;
        }

        /// <summary>True while the player is typing in this window's name field. TextMeshPro knows
        /// (isFocused is its own "the caret is in here" flag), so there is nothing to guess.</summary>
        private static bool NameFieldFocused()
        {
            try
            {
                return inputName != null && inputName.field != null && inputName.field.isFocused;
            }
            catch (Exception e)
            {
                AgLog.Detail("could not read the name field state: " + e.Message);
                return false;
            }
        }

    }
}
