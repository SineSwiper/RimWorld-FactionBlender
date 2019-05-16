using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HugsLib;
using HugsLib.Settings;
using Verse;

namespace FactionBlender {
    [StaticConstructorOnStartup]
    public class Base : ModBase {
        public override string ModIdentifier {
            get { return "FactionBlender"; }
        }
        public static Base             Instance    { get; private set; }
        public static DefInjectors     DefInjector { get; private set; }
        public static List<FactionDef> FB_Factions { get; private set; }

        public Base() {
            Instance    = this;
            DefInjector = new FactionBlender.DefInjectors();
            FB_Factions = new List<FactionDef>();
        }

        // Settings
        internal SettingHandle<float>  filterWeakerAnimalsRaids;
        internal SettingHandle<float>  filterSlowPawnsCaravans;
        internal SettingHandle<float>  pawnKindDifficultyLevel;
        internal SettingHandle<string> excludedFactionTypes;

        public SettingHandle<float>  fwarFilterDisplay;
        public SettingHandle<float>  fspcFilterDisplay;
        public SettingHandle<float>  pkdlFilterDisplay;
        public SettingHandle<string> eftFilterDisplay;

        public string lastSettingChanged = "";

        public string[] excludedFactionTypesList;

        public override void DefsLoaded() {
            FB_Factions.RemoveAll(x => true);
            FB_Factions.Add( FactionDef.Named("FactionBlender_Pirate") );
            FB_Factions.Add( FactionDef.Named("FactionBlender_Civil")  );

            Logger.Message("Injecting hair, backstory, and trader kinds to our factions");
            DefInjector.InjectMiscToFactions(FB_Factions);

            ProcessSettings();

            Logger.Message("Injecting pawn groups to our factions");
            DefInjector.InjectPawnKindDefsToFactions(FB_Factions);
        }

        public override void SettingsChanged() {
            lastSettingChanged = "";
            DefInjector.InjectPawnKindDefsToFactions(FB_Factions);
        }

        public void ProcessSettings () {
            // XXX: There is far too much duplication going on here, especially all of the config name strings

            // Read/declare settings
            filterWeakerAnimalsRaids = Settings.GetHandle<float>(
                "FilterWeakerAnimalsRaids", "FB_FilterWeakerAnimalsRaids_Title".Translate(), "FB_FilterWeakerAnimalsRaids_Description".Translate(),
                // Pirates will want stronger animals.  Bears are 200, and we definitely don't want to exclude
                // those.  Muffalos are 100, which is probably something a pirate raid shouldn't have.
                150, Validators.FloatRangeValidator(0, 400)
            );
            filterWeakerAnimalsRaids.DisplayOrder = 1;
            filterWeakerAnimalsRaids.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_Filter(
                    rect, filterWeakerAnimalsRaids, false, 0, 400, 10
                );
            };
            filterWeakerAnimalsRaids.OnValueChanged = x => { lastSettingChanged = "filterWeakerAnimalsRaids"; };

            filterSlowPawnsCaravans = Settings.GetHandle<float>(
                "FilterSlowPawnsCaravans", "FB_FilterSlowPawnsCaravans_Title".Translate(), "FB_FilterSlowPawnsCaravans_Description".Translate(),
                // MoveSpeed of 3.0 is still slower than the 4.6 humanlike pawns, but fast enough for them to not
                // lag too far behind.  Don't want to go beyond 4.0, as that hits stuff like Muffallos.
                3, Validators.FloatRangeValidator(0, 4)
            );
            filterSlowPawnsCaravans.DisplayOrder = 3;
            filterSlowPawnsCaravans.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_Filter(
                    rect, filterSlowPawnsCaravans, false, 0, 4, 0.1f
                );
            };
            filterSlowPawnsCaravans.OnValueChanged = x => { lastSettingChanged = "filterSlowPawnsCaravans"; };

            pawnKindDifficultyLevel = Settings.GetHandle<float>(
                "PawnKindDifficultyLevel", "FB_PawnKindDifficultyLevel_Title".Translate(), "FB_PawnKindDifficultyLevel_Description".Translate(),
                // This should just filter out the Archotech Centipede
                5000, Validators.IntRangeValidator(100, 12000)
            );
            pawnKindDifficultyLevel.DisplayOrder = 5;
            pawnKindDifficultyLevel.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_Filter(
                    rect, pawnKindDifficultyLevel, false, 100, 12000, 100
                );
            };
            pawnKindDifficultyLevel.OnValueChanged = x => { lastSettingChanged = "pawnKindDifficultyLevel"; };

            excludedFactionTypes = Settings.GetHandle<string>(
                "ExcludedFactionTypes", "FB_ExcludedFactionTypes_Title".Translate(), "FB_ExcludedFactionTypes_Description".Translate(),
                // No Vampires: Too many post-XML modifications and they tend to burn up on entry, anyway
                // No Star Vampires: They are loners that attack ANYBODY on contact, including their own faction
                "ROMV_Sabbat, ROM_StarVampire"
            );
            excludedFactionTypes.DisplayOrder = 7;
            // XXX: You need to actually hit Enter to see the filtered list.  Need an onClick here somehow.
            excludedFactionTypes.OnValueChanged = x => { lastSettingChanged = "excludedFactionTypes"; };

            // Add filter displays
            List<PawnKindDef> fullPawnKindList = DefDatabase<PawnKindDef>.AllDefs.ToList();

            fwarFilterDisplay = Settings.GetHandle<float>("fwarFilterDisplay", "", "", filterWeakerAnimalsRaids.Value);
            fwarFilterDisplay.Unsaved = true;
            fwarFilterDisplay.DisplayOrder = 2;
            fwarFilterDisplay.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_FilteredPawnKinds(
                    rect, fwarFilterDisplay, fullPawnKindList,
                    (pawn => FilterPawnKindDef(pawn, "combat", "filterWeakerAnimalsRaids", (int)filterWeakerAnimalsRaids.Value) == null),
                    (list => { list.SortBy(pawn => pawn.combatPower, pawn => pawn.defName); }),
                    (pawn => pawn.combatPower.ToString("N0"))
                );
            };
            fwarFilterDisplay.VisibilityPredicate = delegate { return lastSettingChanged == "filterWeakerAnimalsRaids"; };

            fspcFilterDisplay = Settings.GetHandle<float>("fspcFilterDisplay", "", "", filterSlowPawnsCaravans.Value);
            fspcFilterDisplay.Unsaved = true;
            fspcFilterDisplay.DisplayOrder = 4;
            fspcFilterDisplay.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_FilteredPawnKinds(
                    rect, fspcFilterDisplay, fullPawnKindList,
                    (pawn => FilterPawnKindDef(pawn, "trade", "filterSlowPawnsCaravans") == null),
                    (list => { list.SortBy(pawn => pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed), pawn => pawn.defName); }),
                    (pawn => pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed).ToString("F2"))
                );
            };
            fspcFilterDisplay.VisibilityPredicate = delegate { return lastSettingChanged == "filterSlowPawnsCaravans"; };

            pkdlFilterDisplay = Settings.GetHandle<float>("pkdlFilterDisplay", "", "", pawnKindDifficultyLevel.Value);
            pkdlFilterDisplay.Unsaved = true;
            pkdlFilterDisplay.DisplayOrder = 8;
            pkdlFilterDisplay.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_FilteredPawnKinds(
                    rect, pkdlFilterDisplay, fullPawnKindList,
                    (pawn => FilterPawnKindDef(pawn, "global", "pawnKindDifficultyLevel") == null),
                    (list => { list.SortBy(pawn => pawn.combatPower, pawn => pawn.defName); }),
                    (pawn => pawn.combatPower.ToString("N0"))
                );
            };
            pkdlFilterDisplay.VisibilityPredicate = delegate { return lastSettingChanged == "pawnKindDifficultyLevel"; };

            eftFilterDisplay = Settings.GetHandle<string>("eftFilterDisplay", "", "", excludedFactionTypes.Value);
            eftFilterDisplay.Unsaved = true;
            eftFilterDisplay.DisplayOrder = 10;
            eftFilterDisplay.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_FilteredPawnKinds(
                    rect, eftFilterDisplay, fullPawnKindList,
                    (pawn => FilterPawnKindDef(pawn, "global", "excludedFactionTypes") == null),
                    (list => { list.SortBy(pawn => pawn.defaultFactionType != null ? pawn.defaultFactionType.defName : "", pawn => pawn.defName); }),
                    (pawn => pawn.defaultFactionType != null ? pawn.defaultFactionType.defName : "")
                );
            };
            eftFilterDisplay.VisibilityPredicate = delegate { return lastSettingChanged == "excludedFactionTypes"; };
        }

        public bool? FilterPawnKindDef(PawnKindDef pawn, string filterType, string watchSetting, int minCombatPower = 50) {
            bool? nil = new bool?();
            RaceProperties race = pawn.RaceProps;

            /*
             * DEBUG
             *
            string msg = pawn.defName;
            msg += " (" + pawn.combatPower + "/" + race.baseBodySize + ") --> ";
            if (race.Animal)     msg += "Animal, ";
            if (race.ToolUser)   msg += "ToolUser, ";
            if (race.Humanlike)  msg += "Humanlike, ";
            if (pawn.isFighter)  msg += "Fighter, ";
            if (pawn.trader)     msg += "Trader, ";
            if (race.packAnimal) msg += "PackAnimal, ";
            if (race.predator)   msg += "Predator, ";
            if (isRanged)        msg += "Ranged, ";
            if (!isRanged)       msg += "Melee, ";
            if (isSniper)        msg += "Sniper, ";
            if (isHeavyWeapons)  msg += "Heavy Weapons, ";

            msg += "Speed: " + pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed);
            if (pawn.combatPower > 1000) Logger.Message(msg);
             */

            // Global filters //

            /* True Story: Sarg Bjornson (Genetic Rim author) added Archotech Centipedes and somebody ended up
             * fighting one in a FB raid the same day.  Amusing, but, in @Extinction's words, "a fight of
             * apocalyptic proportions".
             */
            if (pawn.combatPower > pawnKindDifficultyLevel.Value)
                return watchSetting == "pawnKindDifficultyLevel" ? nil : false;

            // Filter by defaultFactionType
            if (pawn.defaultFactionType != null) {
                foreach (string factionDefName in excludedFactionTypesList) {
                    if (pawn.defaultFactionType.defName == factionDefName)
                        return watchSetting == "excludedFactionTypes" ? nil : false;
                }
            }

            // Combat filters //
            if (filterType == "combat") {
                // Gotta fight if you're in a combat raid
                if (!pawn.isFighter && !race.predator) return false;

                // If it's an animal, make sure Vegeta agrees with the power level
                if (filterWeakerAnimalsRaids.Value > 0) {
                    if (race.Animal && pawn.combatPower < minCombatPower)
                        return watchSetting == "filterWeakerAnimalsRaids" ? nil : false;
                }
            }
            // Trade filters //
            else if (filterType == "trade") {
                // Enforce a minimum speed.  Trader pawns shouldn't get left too far behind, especially pack animals.
                if ((pawn.trader || race.packAnimal || FilterPawnKindDef(pawn, "combat", minCombatPower)) &&
                    pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed) < filterSlowPawnsCaravans.Value
                )
                    return watchSetting == "filterSlowPawnsCaravans" ? nil : false;
            }
            
            return true;
        }

        // If we're not watching a setting, the three-way return (true, null, false) simplifies to just true/false
        public bool FilterPawnKindDef(PawnKindDef pawn, string filterType, int minCombatPower = 50) {
            bool? ret = FilterPawnKindDef(pawn, filterType, "", minCombatPower);
            return ret != null && ret == true;
        }
    }
}
