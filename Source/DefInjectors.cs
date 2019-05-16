using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HugsLib;
using HugsLib.Settings;
using Verse;

namespace FactionBlender {
    public class DefInjectors {
        public void InjectMiscToFactions(List<FactionDef> FB_Factions) {
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
        }

        // TODO: Figure out why ARWoM monsters don't show up

        public void InjectPawnKindDefsToFactions(List<FactionDef> FB_Factions) {
            Base FB = Base.Instance;

            // Split out excludedFactionTypes
            FB.excludedFactionTypesList =
                Regex.Split(FB.excludedFactionTypes.Value.Trim(), "[^\\w]+").
                Select(x => x.Trim()).
                Where (x => x.Length >= 1).
                ToArray()
            ;

            // Clear out old settings, if any
            foreach (FactionDef FBfac in FB_Factions) {
                foreach (PawnGroupMaker maker in FBfac.pawnGroupMakers) {
                    foreach (var optList in new List<PawnGenOption>[] { maker.options, maker.traders, maker.carriers, maker.guards } ) {
                        optList.RemoveAll(x => true);
                    }
                }
            }

            // Loop through each PawnKindDef
            foreach (PawnKindDef pawn in DefDatabase<PawnKindDef>.AllDefs.Where(pawn => FB.FilterPawnKindDef(pawn, "global"))) {
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
                        int origCP         = (int)FB.filterWeakerAnimalsRaids.Value;
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
                            if (!FB.FilterPawnKindDef(pawn, "combat", minCombatPower)) continue;

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
                            if (!FB.FilterPawnKindDef(pawn, "trade")) continue;
                            
                            // Trader group makers split up their pawns into three buckets.  The pawn will go into one of those
                            // three, or none of them.
                            if (pawn.trader) {
                                maker.traders.Add(newOpt);
                            }
                            else if (race.packAnimal) {
                                maker.carriers.Add(newOpt);
                            }
                            else if (FB.FilterPawnKindDef(pawn, "combat", minCombatPower)) {
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
