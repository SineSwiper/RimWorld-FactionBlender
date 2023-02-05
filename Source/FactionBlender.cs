using RimWorld;
using System;
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
        public static Base             Instance     { get; private set; }
        public static DefInjectors     DefInjector  { get; private set; }
        public static List<FactionDef> FB_Factions  { get; private set; }
        public static bool             hasAlienRace { get; private set; }
        public static Dictionary<string, SettingHandle> Config { get; private set; }

        internal HugsLib.Utils.ModLogger ModLogger { get; private set; }

        public Base() {
            Instance    = this;
            DefInjector = new FactionBlender.DefInjectors();
            FB_Factions = new List<FactionDef>();
            ModLogger   = this.Logger;
            Config      = config;
        }

        // Settings
        internal Dictionary<string, SettingHandle> config = new Dictionary<string, SettingHandle>();

        public string lastSettingChanged = "";

        public Regex[] excludedFactionTypesList;
        public Regex[] excludedRacesList;

        public void FillFilterLists(string excludedFactionTypes = "", string excludedRaces = "") {
            // Split out excludedFactionTypes
            if (excludedFactionTypes.Length == 0) excludedFactionTypes = ((SettingHandle<string>)config["ExcludedFactionTypes"]).Value;
            if (excludedRaces       .Length == 0) excludedRaces        = ((SettingHandle<string>)config["ExcludedRaces"       ]).Value;

            // Trim, use * as RegEx wildcard, check for blanks, bind to exact matching
            excludedFactionTypesList =
                Regex.Split(excludedFactionTypes.Trim(), @"[^\w*]+").
                Select(x => x.Trim().Replace("*", @"\w*")).
                Where (x => x.Length >= 1).
                Select(x => new Regex("^" + x + "$")).
                ToArray()
            ;

            excludedRacesList =
                Regex.Split(excludedRaces.Trim(), @"[^\w*]+").
                Select(x => x.Trim().Replace("*", @"\w*")).
                Where (x => x.Length >= 1).
                Select(x => new Regex("^" + x + "$")).
                ToArray()
            ;
        }

        public override void DefsLoaded() {
            hasAlienRace = GenTypes.GetTypeInAnyAssembly("AlienRace.RaceSettings", "AlienRace") != null;

            FB_Factions.RemoveAll(x => true);
            FB_Factions.Add( FactionDef.Named("FactionBlender_Pirate") );
            FB_Factions.Add( FactionDef.Named("FactionBlender_Civil")  );
            if (ModsConfig.IdeologyActive) FB_Factions.Add( FactionDef.Named("FactionBlender_Clan") );

            ProcessSettings();

            DefInjector.InjectMiscToFactions(FB_Factions);

            FillFilterLists();
            if (ModsConfig.BiotechActive) {
                Logger.Message("Injecting xenotypes to our factions");
                DefInjector.InjectXenotypeDefsToFactions(FB_Factions);
            }

            Logger.Message("Injecting pawn groups to our factions");
            DefInjector.InjectPawnKindDefsToFactions(FB_Factions);

            if (hasAlienRace) {
                Logger.Message("Injecting pawn groups to our race settings");
                DefInjector.InjectPawnKindEntriesToRaceSettings();
            }
            else {
                Logger.Message("AlienRace not loaded; no race settings for us!");
            }
        }

        public override void SettingsChanged() {
            lastSettingChanged = "";

            Logger.Message("Re-injecting pawn groups to our factions");
            DefInjector.InjectPawnKindDefsToFactions(FB_Factions);

            if (hasAlienRace) {
                Logger.Message("Re-injecting pawn groups to our race settings");
                DefInjector.InjectPawnKindEntriesToRaceSettings();
            }
        }

        public void ProcessSettings () {
            // Hidden config version entry
            Version currentVer    = Instance.GetVersion();
            string  currentVerStr = currentVer.ToString();

            config["ConfigVersion"] = Settings.GetHandle<string>("ConfigVersion", "", "", currentVerStr);
            var configVerSetting = (SettingHandle<string>)config["ConfigVersion"];
            configVerSetting.DisplayOrder = 0;
            configVerSetting.NeverVisible = true;

            string  configVerStr = configVerSetting.Value;
            Version configVer    = new Version(configVerStr);

            /*
             * Booleans
             */
            var bSettings = new List<string> {};
            if (hasAlienRace) bSettings = new List<string> {
                "EnableMixedStartingColonists",
                "EnableMixedRefugees",
                "EnableMixedSlaves",
                "EnableMixedWanderers",
                "EnableMixedAncients",
            };

            int order = 1;
            foreach (string sName in bSettings) {
                config[sName] = Settings.GetHandle<bool>(
                    sName, ("FB_" + sName + "_Title").Translate(), ("FB_" + sName + "_Description").Translate(), true
                );
                var setting = (SettingHandle<bool>)config[sName];
                setting.DisplayOrder        = order;
                setting.ValueChanged       += sh => { lastSettingChanged = ""; };
                setting.VisibilityPredicate = delegate { return hasAlienRace; };
                order++;
            }

            /*
             * Float sliders
             */
            var fSettings = new List<string> {
                "FilterWeakerAnimalsRaids",
                "FilterSlowPawnsCaravans",
                "PawnKindDifficultyLevel",
            };
            var fDefaults = new Dictionary<string, float> {
                // Pirates will want stronger animals.  Bears are 200, and we definitely don't want to exclude
                // those.  Muffalos are 100, which is probably something a pirate raid shouldn't have.
                { "FilterWeakerAnimalsRaids",  150 },

                // MoveSpeed of 3.0 is still slower than the 4.6 humanlike pawns, but fast enough for them to not
                // lag too far behind.  Don't want to go beyond 4.0, as that hits stuff like Muffallos.
                { "FilterSlowPawnsCaravans",     3 },

                // This should just filter out the Archotech Centipede
                { "PawnKindDifficultyLevel",  5000 },
            };
            var fValidators = new Dictionary<string, SettingHandle.ValueIsValid> {
                { "FilterWeakerAnimalsRaids", Validators.FloatRangeValidator(0, 400)   },
                { "FilterSlowPawnsCaravans",  Validators.FloatRangeValidator(0, 4)     },
                { "PawnKindDifficultyLevel",  Validators.IntRangeValidator(100, 12000) },
            };
            var fDrawerParams = new Dictionary<string, List<float>> {  // ie: min, max, step
                { "FilterWeakerAnimalsRaids", new List<float> {0, 400, 10}      },
                { "FilterSlowPawnsCaravans",  new List<float> {0, 4, 0.1f}      },
                { "PawnKindDifficultyLevel",  new List<float> {100, 12000, 100} },
            };

            foreach (string sName in fSettings) {
                config[sName] = Settings.GetHandle<float>(
                    sName, ("FB_" + sName + "_Title").Translate(), ("FB_" + sName + "_Description").Translate(), fDefaults[sName], fValidators[sName]
                );

                var setting = (SettingHandle<float>)config[sName];
                setting.DisplayOrder = order;

                List<float> p = fDrawerParams[sName];
                float min  = p[0];
                float max  = p[1];
                float step = p[2];

                setting.CustomDrawer = rect => {
                    return DrawUtility.CustomDrawer_Filter(
                        rect, setting, false, min, max, step
                    );
                };
                setting.ValueChanged += sh => { lastSettingChanged = sName; };
                order += 2;
            }

            /*
             * Strings
             */
            var sSettings = new List<string> {
                "ExcludedFactionTypes",
                "ExcludedRaces",
            };
            var sDefaults = new Dictionary<string, string> {
                // No Vampires: Too many post-XML modifications and they tend to burn up on entry, anyway
                // No Star Vampires: They are loners that attack ANYBODY on contact, including their own faction
                // No xenomorphs: Same thing
                { "ExcludedFactionTypes", "ROMV_Sabbat, ROM_StarVampire, RRY_Xenomorph, AvP_Xenomorph" },

                // Better Infestation Queens: They tend to wander around, gathering resources, and ignoring the fight.
                // Boats: Boats are not real pawns
                // TRoM Golems: Just do not function at all as an enemy, fills up with ticking exceptions
                { "ExcludedRaces", "BI_Queen, SPBoats_*, TM_*Golem" },
            };
            var sPrevDefaults = new Dictionary<string, Dictionary<string, Version>> {
                // This is used to change the defaults on mod upgrades.  Version used is the last version before the change.
                { "ExcludedFactionTypes", new Dictionary<string, Version> {
                    { "ROMV_Sabbat, ROM_StarVampire", new Version("1.1.5.1") },
                    { "ROMV_Sabbat, ROM_StarVampire, RRY_Xenomorph", new Version("1.3.5") },
                    { sDefaults["ExcludedFactionTypes"], currentVer },
                } },
                { "ExcludedRaces", new Dictionary<string, Version> {
                    { "",                    new Version("1.1.5.0") },
                    { "BI_Queen",            new Version("1.3.1")  },
                    { "BI_Queen, SPBoats_*", new Version("1.4.1")  },
                    { sDefaults["ExcludedRaces"], currentVer },
                } },
            };

            foreach (string sName in sSettings) {
                config[sName] = Settings.GetHandle<string>(
                    sName, ("FB_" + sName + "_Title").Translate(), ("FB_" + sName + "_Description").Translate(), sDefaults[sName]
                );

                var setting = (SettingHandle<string>)config[sName];
                setting.DisplayOrder = order;
                if (sName == "ExcludedFactionTypes") setting.ValueChanged += sh => { FillFilterLists(sh.StringValue);     lastSettingChanged = sName; };
                else                                 setting.ValueChanged += sh => { FillFilterLists("", sh.StringValue); lastSettingChanged = sName; };
                order += 2;

                setting.CustomDrawer = rect => DrawUtility.CustomDrawer_InputTextbox(rect, setting);
                setting.CustomDrawerHeight = 34f * 4;

                // Force change defaults on mod upgrade
                if (!currentVer.Equals(configVer) && sPrevDefaults[sName].ContainsKey(setting.Value)) {
                    Version lastVerForDefault = sPrevDefaults[sName][setting.Value];
                    if (configVer <= lastVerForDefault) setting.Value = sDefaults[sName];
                }
            }

            // Set the new config value to the current version, now that the above values have been changed
            configVer                             = currentVer;
            configVerStr = configVerSetting.Value = currentVerStr;

            /*
             * Filter Displays
             */
            List<PawnKindDef> fullPawnKindList = DefDatabase<PawnKindDef>.AllDefs.ToList();

            var fltSettings = new List<string> {
                "fwarFilterDisplay",
                "fspcFilterDisplay",
                "pkdlFilterDisplay",
                 "eftFilterDisplay",
                  "erFilterDisplay",
            };
            var fltAffected = new Dictionary<string, string> {
                { "fwarFilterDisplay", "FilterWeakerAnimalsRaids" },
                { "fspcFilterDisplay", "FilterSlowPawnsCaravans"  },
                { "pkdlFilterDisplay", "PawnKindDifficultyLevel"  },
                {  "eftFilterDisplay", "ExcludedFactionTypes"     },
                {   "erFilterDisplay", "ExcludedRaces"            },
            };
            var fltDrawers = new Dictionary<string, SettingHandle.DrawCustomControl> {
                { "fwarFilterDisplay", rect => {
                    return DrawUtility.CustomDrawer_FilteredPawnKinds(
                        rect, config["fwarFilterDisplay"], fullPawnKindList,
                        (pawn => FilterPawnKindDef(pawn, "combat", "FilterWeakerAnimalsRaids", (int)((SettingHandle<float>)config["FilterWeakerAnimalsRaids"]).Value) == null),
                        (list => { list.SortBy(pawn => pawn.combatPower, pawn => pawn.defName); }),
                        (pawn => pawn.combatPower.ToString("N0"))
                    );
                } },
                { "fspcFilterDisplay", rect => {
                    return DrawUtility.CustomDrawer_FilteredPawnKinds(
                        rect, config["fspcFilterDisplay"], fullPawnKindList,
                        (pawn => FilterPawnKindDef(pawn, "trade", "FilterSlowPawnsCaravans") == null),
                        (list => { list.SortBy(pawn => pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed), pawn => pawn.defName); }),
                        (pawn => pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed).ToString("F2"))
                    );
                } },
                { "pkdlFilterDisplay", rect => {
                    return DrawUtility.CustomDrawer_FilteredPawnKinds(
                        rect, config["pkdlFilterDisplay"], fullPawnKindList,
                        (pawn => FilterPawnKindDef(pawn, "global", "PawnKindDifficultyLevel") == null),
                        (list => { list.SortBy(pawn => pawn.combatPower, pawn => pawn.defName); }),
                        (pawn => pawn.combatPower.ToString("N0"))
                    );
                } },
                {  "eftFilterDisplay", rect => {
                    return DrawUtility.CustomDrawer_FilteredPawnKinds(
                        rect, config["eftFilterDisplay"], fullPawnKindList,
                        (pawn => FilterPawnKindDef(pawn, "global", "ExcludedFactionTypes") == null),
                        (list => { list.SortBy(pawn => pawn.defaultFactionType != null ? pawn.defaultFactionType.defName : "", pawn => pawn.defName); }),
                        (pawn => pawn.defaultFactionType != null ? pawn.defaultFactionType.defName : "")
                    );
                } },
                {  "erFilterDisplay", rect => {
                    return DrawUtility.CustomDrawer_FilteredPawnKinds(
                        rect, config["erFilterDisplay"], fullPawnKindList,
                        (pawn => FilterPawnKindDef(pawn, "global", "ExcludedRaces") == null),
                        (list => { list.SortBy(pawn => pawn.race.defName, pawn => pawn.defName); }),
                        (pawn => pawn.race.defName)
                    );
                } },
            };

            order -= (fSettings.Count + sSettings.Count) * 2 - 1;
            foreach (string sName in fltSettings) {
                config[sName] = Settings.GetHandle<float>(sName, "", "", 0);

                var setting = (SettingHandle<float>)config[sName];
                setting.Unsaved = true;
                setting.DisplayOrder = order;
                setting.CustomDrawer = fltDrawers[sName];
                setting.VisibilityPredicate = delegate { return lastSettingChanged == fltAffected[sName]; };
                order += 2;
            }
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

            msg += "Speed: " + pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed);
            if (pawn.defName.StartsWith(...)|| pawn.defName.Contains(...)) Logger.Message(msg);
            */

            // Try to collect more detailed information for errors (which are usually because of weird conditions from the def)
            try {
                // Global filters //

                /* True Story: Sarg Bjornson (Genetic Rim author) added Archotech Centipedes and somebody ended up
                 * fighting one in a FB raid the same day.  Amusing, but, in @Extinction's words, "a fight of
                 * apocalyptic proportions".
                 */
                if (pawn.combatPower > ((SettingHandle<float>)config["PawnKindDifficultyLevel"]).Value)
                    return watchSetting == "PawnKindDifficultyLevel" ? nil : false;

                // Don't include pawns with zero or negative combatPower; they are probably special enough to cause
                // problems.
                if (pawn.combatPower <= 0) return false;

                // Filter by defaultFactionType
                if (pawn.defaultFactionType != null) {
                    foreach (Regex factionDefNameRgx in excludedFactionTypesList) {
                        if (factionDefNameRgx.IsMatch(pawn.defaultFactionType.defName))
                            return watchSetting == "ExcludedFactionTypes" ? nil : false;
                    }
                }

                // Filter by race defName
                if (pawn.race.defName != null) {
                    foreach (Regex raceDefNameRgx in excludedRacesList) {
                        if (raceDefNameRgx.IsMatch(pawn.race.defName))
                            return watchSetting == "ExcludedRaces" ? nil : false;
                    }
                }

                // Combat filters //
                if (filterType == "combat") {
                    // Gotta fight if you're in a combat raid
                    if (!pawn.isFighter && !race.predator) return false;

                    // If it's an animal, make sure Vegeta agrees with the power level
                    if (((SettingHandle<float>)config["FilterWeakerAnimalsRaids"]).Value > 0) {
                        if (race.Animal && pawn.combatPower < minCombatPower)
                            return watchSetting == "FilterWeakerAnimalsRaids" ? nil : false;
                    }
                }
                // Trade filters //
                else if (filterType == "trade") {
                    // Enforce a minimum speed.  Trader pawns shouldn't get left too far behind, especially pack animals.
                    if ((pawn.trader || race.packAnimal || FilterPawnKindDef(pawn, "combat", minCombatPower)) &&
                        pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed) < ((SettingHandle<float>)config["FilterSlowPawnsCaravans"]).Value
                    )
                        return watchSetting == "FilterSlowPawnsCaravans" ? nil : false;
                }
            }
            catch (Exception ex) {
                throw new Exception(
                    string.Format("Triggered exception while filtering for a {0} PawnKindDef from race {1}", pawn.defName, pawn.race?.defName),
                    ex
                );
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
