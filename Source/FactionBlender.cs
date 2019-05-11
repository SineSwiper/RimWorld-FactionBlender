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
        internal static SettingHandle<bool>   filterWeakerAnimalsRaids;
        internal static SettingHandle<bool>   filterSlowPawnsCaravans;
        internal static SettingHandle<bool>   filterSmallerPackAnimalsCaravans;
        internal static SettingHandle<int>    pawnKindDifficultyLevel;
        internal static SettingHandle<string> excludedFactionTypes;

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

        public void ProcessSettings () {
            // Read/declare settings
            filterWeakerAnimalsRaids = Settings.GetHandle<bool>(
                "FilterWeakerAnimalsRaids", "FB_FilterWeakerAnimalsRaids_Title".Translate(), "FB_FilterWeakerAnimalsRaids_Description".Translate(),
                true
            );
            filterWeakerAnimalsRaids.DisplayOrder = 1;

            filterSlowPawnsCaravans = Settings.GetHandle<bool>(
                "FilterSlowPawnsCaravans", "FB_FilterSlowPawnsCaravans_Title".Translate(), "FB_FilterSlowPawnsCaravans_Description".Translate(),
                true
            );
            filterSlowPawnsCaravans.DisplayOrder = 2;

            filterSmallerPackAnimalsCaravans = Settings.GetHandle<bool>(
                "FilterSmallerPackAnimalsCaravans", "FB_FilterSmallerPackAnimalsCaravans_Title".Translate(), "FB_FilterSmallerPackAnimalsCaravans_Description".Translate(),
                true
            );
            filterSmallerPackAnimalsCaravans.DisplayOrder = 3;

            pawnKindDifficultyLevel = Settings.GetHandle<int>(
                "PawnKindDifficultyLevel", "FB_PawnKindDifficultyLevel_Title".Translate(), "FB_PawnKindDifficultyLevel_Description".Translate(),
                5000, Validators.IntRangeValidator(100, 100000)
            );
            pawnKindDifficultyLevel.DisplayOrder = 4;

            excludedFactionTypes = Settings.GetHandle<string>(
                "ExcludedFactionTypes", "FB_ExcludedFactionTypes_Title".Translate(), "FB_ExcludedFactionTypes_Description".Translate(),
                // No Vampires: Too many post-XML modifications and they tend to burn up on entry, anyway
                // No Star Vampires: They are loners that attack ANYBODY on contact, including their own faction
                "ROMV_Sabbat, ROM_StarVampire"
                // TODO: Validator here...
            );
            excludedFactionTypes.DisplayOrder = 5;

            // Changing any setting should trigger the repopulation method
            foreach (var setting in new SettingHandle<bool>[] {
                filterWeakerAnimalsRaids, filterSlowPawnsCaravans, filterSmallerPackAnimalsCaravans
            } ) {
                setting.OnValueChanged = x => { Instance.RepopulatePawnKindDefs(); };
            }
            pawnKindDifficultyLevel.OnValueChanged = x => { Instance.RepopulatePawnKindDefs(); };
            excludedFactionTypes   .OnValueChanged = x => { Instance.RepopulatePawnKindDefs(); };
        }

        // TODO: Figure out why ARWoM monsters don't show up

        public void RepopulatePawnKindDefs() {
            var FB_Factions = new List<FactionDef>();
            FB_Factions.Add( FactionDef.Named("FactionBlender_Pirate") );
            FB_Factions.Add( FactionDef.Named("FactionBlender_Civil")  );

            // Split out excludedFactionTypes
            string[] excludedFactionTypesList = Regex.Split(excludedFactionTypes.Value.Trim(), "[^\\w]+");

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
            foreach (PawnKindDef pawn in DefDatabase<PawnKindDef>.AllDefs) {
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

                // Filter by defaultFactionType
                if (pawn.defaultFactionType != null) {
                    foreach (string factionDefName in excludedFactionTypesList) {
                        string trimmed = factionDefName.Trim();
                        if (trimmed.Length >= 1 && pawn.defaultFactionType.defName == trimmed) continue;
                    }
                }

                /* True Story: Sarg Bjornson (Genetic Rim author) added Archotech Centipedes and somebody ended up
                 * fighting one in a FB raid the same day.  Amusing, but, in @Extinction's words, "a fight of
                 * apocalyptic proportions".
                 * 
                 * High combatPower pawns to look out for:
                 * 
                 * Archotech Centipedes at 10K power
                 * Heavy MERFs at 1500 (why?)
                 * AI haul/clean bots are at 999,999 for some reason (Misc. Robots)
                 * Demons (from ARWoM) at 3000 (though I never see ARWoM monsters in FB raids...)
                 * Greater Elementals (ARWoM) at 1500
                 * Alpha Werewolves, Mechathrumbos, Gallatrosses at 1000
                 * 
                 * For vanilla reference, Mech_Centipedes are 400 and Thrumbos are 500.
                 */
                if (pawn.combatPower > pawnKindDifficultyLevel.Value) continue;

                foreach (FactionDef FBfac in FB_Factions) {
                    foreach (PawnGroupMaker maker in FBfac.pawnGroupMakers) {
                        bool isPirate = FBfac.defName == "FactionBlender_Pirate";
                        bool isCombat = isPirate || (maker.kindDef.defName == "Combat");
                        bool isTrader = maker.kindDef.defName == "Trader";

                        // Allow "combat ready" animals
                        int minCombatPower =
                            isPirate ? 150 :
                            isCombat ? 100 :
                            50
                        ;

                        // Create the pawn option
                        var newOpt = new PawnGenOption();
                        newOpt.kind = pawn;
                        newOpt.selectionWeight =
                            race.Animal ? 1 :
                            race.Humanlike ? 10 : 2
                        ;

                        if (isCombat) {
                            // Gotta fight if you're in a combat raid
                            if (!pawn.isFighter) continue;

                            // If it's an animal, make sure Vegeta agrees with the power level
                            if (filterWeakerAnimalsRaids.Value) {
                                if (race.Animal     && pawn.combatPower < minCombatPower)      continue;
                                if (race.herdAnimal && pawn.combatPower < minCombatPower + 50) continue;
                            }

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
                            // Enforce a minimum speed.  Trader pawns shouldn't get left too far behind, especially pack animals.
                            if (filterSlowPawnsCaravans.Value && pawn.race.GetStatValueAbstract(StatDefOf.MoveSpeed) < 3) continue;
                            
                            // Trader group makers split up their pawns into three buckets.  The pawn will go into one of those
                            // three, or none of them.
                            if (pawn.trader) {
                                maker.traders.Add(newOpt);
                            }
                            else if (
                                race.packAnimal &&
                                (!filterSmallerPackAnimalsCaravans.Value || race.baseBodySize >= 1)
                            ) {
                                maker.carriers.Add(newOpt);
                            }
                            else if ((pawn.isFighter || race.predator) && pawn.combatPower >= minCombatPower) {
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
    }
}
