using HugsLib.Settings;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

// Much of this borrowed from rheirman's excellent Run and Gun mod.
// See also: https://github.com/rheirman/RunAndGun/blob/master/Source/RunAndGun/Utilities/DrawUtility.cs

namespace FactionBlender {
    class DrawUtility {
        private const float IconSize = 32f;
        private const float IconGap = 1f;
        private const float TextMargin = 20f;
        private const float BottomMargin = 2f;

        private static readonly Color iconBaseColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color iconMouseOverColor = new Color(0.6f, 0.6f, 0.4f, 1f);
        private static Color background = new Color(0.5f, 0, 0, 0.1f);

        private static readonly Dictionary<string, Pawn> pawnKindExamples = new Dictionary<string, Pawn>();

        private static void DrawBackground(Rect rect) {
            Color save = GUI.color;
            GUI.color = background;
            GUI.DrawTexture(rect, TexUI.FastFillTex);
            GUI.color = save;
        }

        private static void DrawLabel(string labelText, Rect textRect, float offset) {
            var labelHeight = Text.CalcHeight(labelText, textRect.width);
            labelHeight -= 2f;
            var labelRect = new Rect(textRect.x, textRect.yMin - labelHeight + offset, textRect.width, labelHeight);
            GUI.DrawTexture(labelRect, TexUI.GrayTextBG);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(labelRect, labelText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static bool DrawIconForPawnKind(PawnKindDef pawn, string extraAttr, Rect contentRect, Vector2 iconOffset, int buttonID) {
            var iconRect = new Rect(contentRect.x + iconOffset.x, contentRect.y + iconOffset.y, IconSize, IconSize);

            if (!contentRect.Contains(iconRect)) return false;

            string label = Find.ActiveLanguageWorker.ToTitleCase(pawn.label);
            if (!extraAttr.NullOrEmpty()) label += " (" + extraAttr + ")";
            TooltipHandler.TipRegion(iconRect, label);

            MouseoverSounds.DoRegion(iconRect, SoundDefOf.Mouseover_Command);
            GUI.color = Mouse.IsOver(iconRect) ? iconMouseOverColor : iconBaseColor;
            GUI.DrawTexture(iconRect, ContentFinder<Texture2D>.Get("square", true));

            // Humanlikes don't have textures, so an example pawn needs to be generated.  Of course, this can't happen in
            // the main menu, so it'll be skipped when a world isn't present

            if (pawn.RaceProps.Humanlike && Current.Game != null) {
                string name = pawn.defName;
                Pawn examplePawn = pawnKindExamples.ContainsKey(name) ? pawnKindExamples[name] : PawnGenerator.GeneratePawn(pawn);
                pawnKindExamples[name] = examplePawn;

                Widgets.ThingIcon(iconRect, examplePawn, 1f);
            }
            else {
                Widgets.ThingIcon(iconRect, pawn.race);
            }

            if (Widgets.ButtonInvisible(iconRect, true)) {
                Event.current.button = buttonID;
                return true;
            }
            else
                return false;
        }

        public static bool CustomDrawer_Filter(Rect rect, SettingHandle<float> slider, bool def_isPercentage, float def_min, float def_max, float roundTo = -1) {
            DrawBackground(rect);
            int labelWidth = 50;

            Rect sliderPortion = new Rect(rect);
            sliderPortion.width -= labelWidth;

            Rect labelPortion = new Rect(rect);
            labelPortion.width = labelWidth;
            labelPortion.position = new Vector2(sliderPortion.position.x + sliderPortion.width + 5f, sliderPortion.position.y + 4f);

            sliderPortion = sliderPortion.ContractedBy(2f);

            if (def_isPercentage)
                Widgets.Label(labelPortion, (Mathf.Round(slider.Value * 100f)).ToString("F0") + "%");
            else if (roundTo >= 1)
                Widgets.Label(labelPortion, slider.Value.ToString("N0"));
            else 
                Widgets.Label(labelPortion, slider.Value.ToString("F2"));

            float val = Widgets.HorizontalSlider(sliderPortion, slider.Value, def_min, def_max, true, null, null, null, roundTo);
            bool change = false;

            if (slider.Value != val)
                change = true;

            slider.Value = val;
            return change;
        }

        internal static bool CustomDrawer_FilteredPawnKinds(
            Rect wholeRect, SettingHandle setting, List<PawnKindDef> allPawnKinds, Predicate<PawnKindDef> filter, Action<List<PawnKindDef>> sorter, Func<PawnKindDef, string> pawnAttr
        ) {
            // Reset the Rect back to full size
            wholeRect.Set(wholeRect.x - wholeRect.width, wholeRect.y, wholeRect.width * 2f, wholeRect.height);
        
            DrawBackground(wholeRect);

            GUI.color = Color.white;

            Rect iconRect     = new Rect(wholeRect);
            iconRect.height   = wholeRect.height - TextMargin + BottomMargin;
            iconRect.position = new Vector2(iconRect.position.x, iconRect.position.y);

            DrawLabel("Filtered".Translate(), iconRect, TextMargin);

            iconRect.position = new Vector2(iconRect.position.x, iconRect.position.y + TextMargin);

            int iconsPerRow = (int)(iconRect.width / (IconGap + IconSize));
            bool change = false;
            int numSelected = 0;

            List<PawnKindDef> filteredList = allPawnKinds.FindAll(filter);
            sorter(filteredList);

            int biggerRows = Math.Max(numSelected / iconsPerRow, (filteredList.Count - numSelected) / iconsPerRow) + 1;
            setting.CustomDrawerHeight = (biggerRows * IconSize) + (biggerRows * IconGap) + TextMargin;
            int index = 0;
            foreach (PawnKindDef pawn in filteredList) {
                int column = index % iconsPerRow;
                int row    = index / iconsPerRow;

                DrawIconForPawnKind(
                    pawn, pawnAttr(pawn), iconRect, new Vector2(IconSize * column + column * IconGap, IconSize * row + row * IconGap), index
                );
                index++;
            }
            return change;
        }
    }

    public static class Extensions {
        public static bool Contains(this Rect rect, Rect otherRect) {
            if (!rect.Contains(new Vector2(otherRect.xMin, otherRect.yMin)))
                return false;
            if (!rect.Contains(new Vector2(otherRect.xMin, otherRect.yMax)))
                return false;
            if (!rect.Contains(new Vector2(otherRect.xMax, otherRect.yMax)))
                return false;
            if (!rect.Contains(new Vector2(otherRect.xMax, otherRect.yMin)))
                return false;
            return true;
        }
    }
}