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
        public static Base Instance { get; private set; }
        public Base() {
            Instance = this;
        }

        // Settings
        internal static SettingHandle<float>  filterWeakerAnimalsRaids;
        internal static SettingHandle<float>  filterSlowPawnsCaravans;
        internal static SettingHandle<float>  filterSmallerPackAnimalsCaravans;
        internal static SettingHandle<float>  pawnKindDifficultyLevel;
        internal static SettingHandle<string> excludedFactionTypes;

        public static SettingHandle<float>  fwarFilterDisplay;
        public static SettingHandle<float>  fspcFilterDisplay;
        public static SettingHandle<float>  fspacFilterDisplay;
        public static SettingHandle<float>  pkdlFilterDisplay;
        public static SettingHandle<string> eftFilterDisplay;

        public static string lastSettingChanged = "";

        string[] excludedFactionTypesList;

        public override void DefsLoaded() {
            var FB_Factions = new List<FactionDef>();
            FB_Factions.Add( FactionDef.Named("FactionBlender_Pirate") );
            FB_Factions.Add( FactionDef.Named("FactionBlender_Civil")  );

            Logger.Message("Scanning and inserting hair, backstory, and trader kinds");
            foreach (FactionDef FBfac in FB_Factions) {
                // NOTE: RemoveDuplicates only does an object comparison, which isn't good enough for these strings.
            
                // Add all hairTags
                foreach (HairDef hair in DefDatabase<HairDef>.AllDefs) {
                    foreach (var tag in hair.hairTags) {
                        if (!FBfac.hairTags.Contains(tag)) FBfac.hairTags.Add(tag);
                    }
                }
                FBfac.hairTags.RemoveDuplicates();

                // Add all backstoryCategories
                var bscat = FBfac.backstoryCategories;
                foreach (var backstory in BackstoryDatabase.allBackstories) {
                    foreach (var category in backstory.Value.spawnCategories) {
                        if (!bscat.Contains(category)) bscat.Add(category);
                    }
                }
                bscat.RemoveDuplicates();
            }

            // Fix caravanTraderKinds, visitorTraderKinds, baseTraderKinds for the civil faction only
            FactionDef FB_Civil = FB_Factions[1];
            foreach (var faction in DefDatabase<FactionDef>.AllDefs) {
                if (faction == FB_Civil) continue;

                foreach (var caravan in faction.caravanTraderKinds) {
                    // Tribal caravans are just lesser versions of the outlander ones, except for the slavers, which we
                    // don't want here.
                    if (caravan.defName.Contains("Neolithic")) continue;
                    FB_Civil.caravanTraderKinds.Add(caravan);
                }
                FB_Civil.visitorTraderKinds.AddRange(faction.visitorTraderKinds);
                FB_Civil.   baseTraderKinds.AddRange(faction.   baseTraderKinds);
            }
            FB_Civil.caravanTraderKinds.RemoveDuplicates();
            FB_Civil.visitorTraderKinds.RemoveDuplicates();
            FB_Civil.   baseTraderKinds.RemoveDuplicates();

            this.ProcessSettings();
            this.RepopulatePawnKindDefs();
        }

        public override void SettingsChanged() {
            this.RepopulatePawnKindDefs();
        }

        public void ProcessSettings () {
            // XXX: There is far too much duplication going on here, especially all of the config name strings

            // Read/declare settings
            filterWeakerAnimalsRaids = Settings.GetHandle<float>(
                "FilterWeakerAnimalsRaids", "FB_FilterWeakerAnimalsRaids_Title".Translate(), "FB_FilterWeakerAnimalsRaids_Description".Translate(),
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
                3, Validators.FloatRangeValidator(0, 4)
            );
            filterSlowPawnsCaravans.DisplayOrder = 3;
            filterSlowPawnsCaravans.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_Filter(
                    rect, filterSlowPawnsCaravans, false, 0, 4, 0.1f
                );
            };
            filterSlowPawnsCaravans.OnValueChanged = x => { lastSettingChanged = "filterSlowPawnsCaravans"; };

            filterSmallerPackAnimalsCaravans = Settings.GetHandle<float>(
                "FilterSmallerPackAnimalsCaravans", "FB_FilterSmallerPackAnimalsCaravans_Title".Translate(), "FB_FilterSmallerPackAnimalsCaravans_Description".Translate(),
                1, Validators.FloatRangeValidator(0, 1)
            );
            filterSmallerPackAnimalsCaravans.DisplayOrder = 5;
            filterSmallerPackAnimalsCaravans.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_Filter(
                    rect, filterSmallerPackAnimalsCaravans, false, 0, 1, 0.05f
                );
            };
            filterSmallerPackAnimalsCaravans.OnValueChanged = x => { lastSettingChanged = "filterSmallerPackAnimalsCaravans"; };

            pawnKindDifficultyLevel = Settings.GetHandle<float>(
                "PawnKindDifficultyLevel", "FB_PawnKindDifficultyLevel_Title".Translate(), "FB_PawnKindDifficultyLevel_Description".Translate(),
                5000, Validators.IntRangeValidator(100, 12000)
            );
            pawnKindDifficultyLevel.DisplayOrder = 7;
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
            excludedFactionTypes.DisplayOrder = 9;
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

            fspacFilterDisplay = Settings.GetHandle<float>("fspacFilterDisplay", "", "", filterSmallerPackAnimalsCaravans.Value);
            fspacFilterDisplay.Unsaved = true;
            fspacFilterDisplay.DisplayOrder = 6;
            fspacFilterDisplay.CustomDrawer = rect => {
                return DrawUtility.CustomDrawer_FilteredPawnKinds(
                    rect, fspacFilterDisplay, fullPawnKindList,
                    (pawn => FilterPawnKindDef(pawn, "trade", "filterSmallerPackAnimalsCaravans") == null),
                    (list => { list.SortBy(pawn => pawn.RaceProps.baseBodySize, pawn => pawn.defName); }),
                    (pawn => pawn.RaceProps.baseBodySize.ToString("F2"))
                );
            };
            fspacFilterDisplay.VisibilityPredicate = delegate { return lastSettingChanged == "filterSmallerPackAnimalsCaravans"; };

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

        // TODO: Figure out why ARWoM monsters don't show up

        public void RepopulatePawnKindDefs() {
            var FB_Factions = new List<FactionDef>();
            FB_Factions.Add( FactionDef.Named("FactionBlender_Pirate") );
            FB_Factions.Add( FactionDef.Named("FactionBlender_Civil")  );

            // Split out excludedFactionTypes
            excludedFactionTypesList =
                Regex.Split(excludedFactionTypes.Value.Trim(), "[^\\w]+").
                Select(x => x.Trim()).
                Where (x => x.Length >= 1).
                ToArray()
            ;

            Logger.Message("Scanning and inserting pawn groups");

            // Clear out old settings, if any
            foreach (FactionDef FBfac in FB_Factions) {
                foreach (PawnGroupMaker maker in FBfac.pawnGroupMakers) {
                    foreach (var optList in new List<PawnGenOption>[] { maker.options, maker.traders, maker.carriers, maker.guards } ) {
                        optList.RemoveAll(x => true);
                    }
                }
            }

            // Loop through each PawnKindDef
            foreach (PawnKindDef pawn in DefDatabase<PawnKindDef>.AllDefs.Where(pawn => FilterPawnKindDef(pawn, "global"))) {
                RaceProperties race = pawn.RaceProps;

                // Define weapon-like traits
                bool isRanged = race.ToolUser && pawn.weaponTags != null && pawn.weaponTags.Any(t =>
                    !(t.Contains("Melee") || t == "None") &&
                    Regex.IsMatch(t, "Gun|Ranged|Pistol|Rifle|Sniper|Carbine|Revolver|Bowman|Grenade|Artillery|Assault|MageAttack|DefensePylon|GlitterTech|^OC|Federator|Ogrenaut|Hellmaker")
                );
                // Animals can shoot projectiles, too
                if (race.Animal && pawn.race.Verbs != null && pawn.race.Verbs.Any(v =>
                    v.burstShotCount >= 1 && v.range >= 10 && v.commonality >= 0.7 && v.defaultProjectile != null
                )) isRanged = true;

                bool isSniper = false;
                if (isRanged) {
                    // Using All here to be more strict about sniper weapon usage
                    isSniper = race.ToolUser && pawn.weaponTags != null && pawn.weaponTags.All(t =>
                        !(t.Contains("Melee") || t == "None") &&
                        Regex.IsMatch(t, "Sniper|Ranged(Strong|Mighty|Heavy|Chief)|ElderThingGun")
                    );
                    if (race.Animal && pawn.race.Verbs != null && pawn.race.Verbs.Any(v =>
                        v.burstShotCount >= 1 && v.range >= 40 && v.commonality >= 0.7 && v.defaultProjectile != null
                    )) isSniper = true;
                }

                bool isHeavyWeapons = race.ToolUser && pawn.weaponTags != null && pawn.weaponTags.Any(t =>
                    Regex.IsMatch(t, "Grenade|Flame|Demolition|GunHeavy|Turret|Pylon|Artillery|GlitterTech|OC(Heavy|Tank)|Bomb|Sentinel|FedHeavy")
                );
                // Include animals with BFGs and death explodey types
                if (race.Animal) {
                    if (isRanged && pawn.combatPower >= 500) isHeavyWeapons = true;
                    if (
                        race.deathActionWorkerClass != null &&
                        Regex.IsMatch(race.deathActionWorkerClass.Name, "E?xplosion|Bomb")
                    ) isHeavyWeapons = true;
                }

                foreach (FactionDef FBfac in FB_Factions) {
                    foreach (PawnGroupMaker maker in FBfac.pawnGroupMakers) {
                        bool isPirate = FBfac.defName == "FactionBlender_Pirate";
                        bool isCombat = isPirate || (maker.kindDef.defName == "Combat");
                        bool isTrader = maker.kindDef.defName == "Trader";

                        // Allow "combat ready" animals
                        int origCP         = (int)filterWeakerAnimalsRaids.Value;
                        int minCombatPower =
                            isPirate ? origCP :                                           // 100%
                            isCombat ? (int)System.Math.Round( (float)origCP / 3 * 2 ) :  // 66%
                                       (int)System.Math.Round( (float)origCP / 3 )        // 33%
                        ;

                        // Create the pawn option
                        var newOpt = new PawnGenOption();
                        newOpt.kind = pawn;
                        newOpt.selectionWeight =
                            race.Animal ? 1 :
                            race.Humanlike ? 10 : 2
                        ;

                        if (isCombat) {
                            if (!FilterPawnKindDef(pawn, "combat", minCombatPower)) continue;

                            // XXX: Unfortunately, there are no names for these pawnGroupMakers, so we have to use commonality
                            // to identify each type.
                            
                            // Additional filters for specialized categories
                            bool addIt = true;
                            if      (maker.commonality == 65) addIt = isRanged;
                            else if (maker.commonality == 60) addIt = !isRanged;
                            else if (maker.commonality == 40) addIt = isSniper;
                            else if (maker.commonality == 25) addIt = isHeavyWeapons;
                            else if (maker.commonality == 10) newOpt.selectionWeight = race.Humanlike ? 1 : 10;

                            // Add it
                            if (addIt) maker.options.Add(newOpt);
                        }
                        else if (isTrader) {
                            if (!FilterPawnKindDef(pawn, "trade")) continue;
                            
                            // Trader group makers split up their pawns into three buckets.  The pawn will go into one of those
                            // three, or none of them.
                            if (pawn.trader) {
                                maker.traders.Add(newOpt);
                            }
                            else if (race.packAnimal) {
                                maker.carriers.Add(newOpt);
                            }
                            else if (FilterPawnKindDef(pawn, "combat", minCombatPower)) {
                                maker.guards.Add(newOpt);
                            }
                        }
                        else {
                            // Peaceful or Settlement: Accept almost anybody
                            maker.options.Add(newOpt);
                        }

                    }
                }
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
                    bool? rejectVal = watchSetting == "filterWeakerAnimalsRaids" ? nil : false;
                    if (race.Animal     && pawn.combatPower < minCombatPower)      return rejectVal;
                    if (race.herdAnimal && pawn.combatPower < minCombatPower + 50) return rejectVal;
                }
            }
            // Trade filters //
            else if (filterType == "trade") {
                // Enforce a minimum speed.  Trader pawns shouldn't get left too far behind, especially pack animals.
                if ((pawn.trader || race.packAnimal || FilterPawnKindDef(pawn, "combat", minCombatPower)) &&
                    pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed) < filterSlowPawnsCaravans.Value
                )
                    return watchSetting == "filterSlowPawnsCaravans" ? nil : false;
                
                if (race.packAnimal && race.baseBodySize < filterSmallerPackAnimalsCaravans.Value)
                    return watchSetting == "filterSmallerPackAnimalsCaravans" ? nil : false;
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
