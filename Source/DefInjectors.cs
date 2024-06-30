using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;
using HugsLib.Settings;
using UnityEngine;
using HarmonyLib;
using System.Reflection;

namespace FactionBlender {
    public class DefInjectors {
        public void InjectMiscToFactions(List<FactionDef> FB_Factions) {
            Base FB = Base.Instance;

            // Preemptively fix apparelStuffFilters for the basic factions that'll get FB pawn kinds
            // (See also Harmony patch for GenerateWorkingPossibleApparelSetFor)
            FB.ModLogger.Message("Injecting apparel filters to basic factions");

            if ((SettingHandle<bool>)FB.config["EnableMixedAncients"]) {
                // Give Ancients the best stuff (Plasteel, Hyperweave, Gold, etc.)
                InjectApparelStuffIntoFaction(FactionDef.Named("Ancients"),        5, 100_000);
                InjectApparelStuffIntoFaction(FactionDef.Named("AncientsHostile"), 5, 100_000);
            }

            if ((SettingHandle<bool>)FB.config["EnableMixedStartingColonists"]) {
                // PlayerColony gets average stuff (Synthread, Wool, etc.)
                InjectApparelStuffIntoFaction(FactionDef.Named("PlayerColony"), 2.5f, 5);

                // PlayerTribe gets the worst stuff (Cloth, Steel, etc.)
                InjectApparelStuffIntoFaction(FactionDef.Named("PlayerTribe"), 0, 2.5f);
            }

            // Fix caravanTraderKinds, visitorTraderKinds, baseTraderKinds for the civil factions only
            FB.ModLogger.Message("Injecting trader kinds to our factions");
            TraderKindDefInjector.InjectTraderKindDefsToFactions(FB_Factions);

            FB.ModLogger.Message("Injecting styles to our cultures");
            InjectStyleDefsToCultures(FB_Factions);
        }

        private static void InjectApparelStuffIntoFaction(FactionDef faction, float valMin, float valMax) {
            var hasStuffCategory = new HashSet<StuffCategoryDef> {};

            // If apparelStuffFilter is null, everything is allowed, anyway
            if (faction.apparelStuffFilter == null) return;

            // Assign each "stuff" into the faction filter, based on a market value quality filter
            foreach (var stuff in DefDatabase<ThingDef>.AllDefs.Where(
                st => st.IsStuff && st != ThingDefOf.Human.race.leatherDef && st.GetStatValueAbstract(StatDefOf.MarketValue) is float mv &&
                mv >= valMin && mv <= valMax
            ) ) {
                faction.apparelStuffFilter.SetAllow(stuff, true);
                stuff.stuffProps?.categories?.ForEach(scd => hasStuffCategory.Add(scd));
            }
            
            // Make sure every stuffCategory is captured
            foreach (var stuffCategory in DefDatabase<StuffCategoryDef>.AllDefs) {
                if (!hasStuffCategory.Contains(stuffCategory)) {
                    // Find a "stuff" that fits
                    ThingDef stuff = DefDatabase<ThingDef>.AllDefs.Where(
                        st => (
                            st.IsStuff && st.stuffProps is StuffProperties sp && sp.commonality > 0 &&
                            sp.categories != null && sp.categories.Contains(stuffCategory)
                        )
                    ).OrderBy(
                        // Whichever is closest to the min/max ranges
                        st => Math.Min(
                            Math.Abs( st.GetStatValueAbstract(StatDefOf.MarketValue) - valMin ),
                            Math.Abs( st.GetStatValueAbstract(StatDefOf.MarketValue) - valMax )
                        )
                    ).FirstOrFallback();

                    if (stuff != null) faction.apparelStuffFilter.SetAllow(stuff, true);
                }
            }
        }

        private static void InjectStyleDefsToCultures(List<FactionDef> FB_Factions) {
            List<CultureDef> FB_Cultures = FB_Factions.SelectMany( fd => fd.allowedCultures ).ToList();

            List<string> allPlaceTags = DefDatabase<PlaceDef>.AllDefs.SelectMany( pd  =>  pd.tags      ).Distinct().ToList();
            List<string> allStyleTags = StyleItemDef.AllStyleItemDefs.SelectMany( sid => sid.styleTags ).Distinct().ToList();

            foreach (CultureDef FB_Culture in FB_Cultures) {
                // Clear out old settings
                FB_Culture.allowedPlaceTags.Clear();
                FB_Culture.thingStyleCategories.Clear();
                FB_Culture.styleItemTags.Clear();

                // Add all of the possible placeTags, thingStyleCategories, and styleTags
                FB_Culture.allowedPlaceTags.AddRange( allPlaceTags );
                FB_Culture.thingStyleCategories.AddRange(
                    DefDatabase<StyleCategoryDef>.AllDefs.Select( scd => new ThingStyleCategoryWithPriority( category: scd, priority: 1 ) )
                );
                FB_Culture.styleItemTags.AddRange(
                    allStyleTags.Select( st => new StyleItemTagWeighted(tag: st, baseWeight: 1, weightFactor: 1) )
                );
            }
        }

        public void InjectXenotypeDefsToFactions(List<FactionDef> FB_Factions) {
            if (!ModsConfig.BiotechActive) return;

            foreach (FactionDef FBfac in FB_Factions) {
                bool isPirate = FBfac.defName == "FactionBlender_Pirate";

                List<XenotypeChance> allXenotypes =
                    DefDatabase<XenotypeDef>.AllDefs.
                    // Pirates won't accept non-violent xenotypes
                    Where ( xtd => xtd.canGenerateAsCombatant || !isPirate ).
                    // Start with an inverse of combatPowerFactor, and equalize it to a 100% total for all of them.
                    // Baseliners still get a small majority with double the base rate.
                    Select( xtd => new XenotypeChance(xtd,
                        xtd == XenotypeDefOf.Baseliner ?  2f :
                        xtd.combatPowerFactor == 0     ? 10f :
                        1f / xtd.combatPowerFactor
                    ) ).
                    ToList()
                ;

                float chanceTotal = allXenotypes.Sum( xtc => xtc.chance );
                allXenotypes.ForEach( xtc => xtc.chance /= chanceTotal );

                // [Reflection] FBfac.xenotypeSet.xenotypeChances = allXenotypes
                FieldInfo xenotypeChangesField = AccessTools.Field(typeof(XenotypeSet), "xenotypeChances");
                xenotypeChangesField.SetValue(FBfac.xenotypeSet, allXenotypes);
            }
        }

        public void InjectPawnKindDefsToFactions(List<FactionDef> FB_Factions) {
            Base FB = Base.Instance;

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
                try {
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
                        Regex.IsMatch(t, "Grenade|Flame|Demolition|Destructive|Breach|GunHeavy|Turret|Pylon|Artillery|GlitterTech|OC(Heavy|Tank)|Bomb|Sentinel|FedHeavy")
                    );
                    // Include animals with BFGs and death explodey types
                    if (race.Animal) {
                        if (isRanged && pawn.combatPower >= 500) isHeavyWeapons = true;
                        if (
                            race.DeathActionWorker is DeathActionWorker deathAction && (
                                deathAction.DangerousInMelee || Regex.IsMatch(deathAction.GetType().Name, "E?xplosion|Bomb")
                            )
                        ) isHeavyWeapons = true;
                    }

                    // Work site pawns booleans //

                    bool isMiner = pawn.isGoodBreacher || pawn.requiredWorkTags.HasFlag(WorkTags.Mining) || pawn.race.GetStatValueAbstract(StatDefOf.MiningSpeed) >= 1.1f;
                    if (race.ToolUser) {
                        if (pawn.weaponTags       != null && pawn.weaponTags      .Any(t  => Regex.IsMatch(t, "Miner|Digger|Drill"))) isMiner = true;
                        if (pawn.techHediffsTags  != null && pawn.techHediffsTags .Any(t  => Regex.IsMatch(t, "Miner|Digger|Drill"))) isMiner = true;
                    }
                    // Include animals with "digging" capabilities (Groundrunner *hint hint*)
                    if (race.Animal) {
                        if (race.thinkTreeConstant != null && Regex.IsMatch(race.thinkTreeConstant.defName, "Miner|Digger|Driller")) isMiner = true;
                    }

                    bool isHunter = isSniper && !isHeavyWeapons;
                    if (pawn.requiredWorkTags.HasFlag(WorkTags.Hunting)) isHunter = true;
                    if (race.Animal && race.predator)                    isHunter = true;

                    bool isPlanter = pawn.requiredWorkTags.HasFlag(WorkTags.PlantWork) || pawn.race.GetStatValueAbstract(StatDefOf.PlantWorkSpeed) >= 1.1f || pawn.race.GetStatValueAbstract(StatDefOf.PlantHarvestYield) >= 1.1f;
                    if (race.ToolUser) {
                        if (pawn.weaponTags       != null && pawn.weaponTags      .Any(t  => Regex.IsMatch(t, "Plant|Logger|Harvest|Field"))) isPlanter = true;
                        if (pawn.techHediffsTags  != null && pawn.techHediffsTags .Any(t  => Regex.IsMatch(t, "Plant|Logger|Harvest|Field"))) isPlanter = true;
                    }

                    /*
                     * DEBUG
                     *
                    string msg = pawn.defName;
                    msg += " --> ";
                    if (isRanged)        msg += "Ranged, ";
                    if (!isRanged)       msg += "Melee, ";
                    if (isSniper)        msg += "Sniper, ";
                    if (isHeavyWeapons)  msg += "Heavy Weapons, ";
                    if (isMiner)         msg += "Miner, ";
                    if (isHunter)        msg += "Hunter, ";
                    if (isPlanter)       msg += "Planter, ";

                    if (pawn.defName.StartsWith(...)|| pawn.defName.Contains(...)) FB.ModLogger.Message(msg);
                    */

                    foreach (FactionDef FBfac in FB_Factions) {
                        foreach (PawnGroupMaker maker in FBfac.pawnGroupMakers) {
                            string makerName = maker.kindDef.defName;
                            bool isPirate = FBfac.defName == "FactionBlender_Pirate";
                            bool isCombat = isPirate || makerName == "Combat";

                            // Allow "combat ready" animals
                            int origCP         = (int)((SettingHandle<float>)FB.config["FilterWeakerAnimalsRaids"]).Value;
                            int minCombatPower =
                                isPirate ? origCP :                                     // 100%
                                isCombat ? (int)System.Math.Round( origCP / 3f * 2f ) : // 66%
                                           (int)System.Math.Round( origCP / 3f )        // 33%
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
                            else if (makerName == "Trader") {
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
                            // Work Site raids
                            else if (makerName == "Miners") {
                                if (isMiner)   maker.options.Add(newOpt);
                            }
                            else if (makerName == "Hunters") {
                                if (isHunter)  maker.options.Add(newOpt);
                            }
                            else if (makerName == "Loggers" || makerName == "Farmers") {
                                if (isPlanter) maker.options.Add(newOpt);
                            }
                            else {
                                // Peaceful or Settlement: Accept almost anybody
                                maker.options.Add(newOpt);
                            }

                        }
                    }
                }
                catch (Exception ex) {
                    throw new Exception(
                        string.Format("Triggered exception while injecting a {0} PawnKindDef from race {1}", pawn.defName, pawn.race?.defName),
                        ex
                    );
                }
            }
        }

        // TODO: Make the hasAlienRace checks actually work.  It's exceptionally hard to make references
        // optional in C#.

        public void InjectPawnKindEntriesToRaceSettings() {
            Base FB = Base.Instance;

            if (!FB.config.ContainsKey("EnableMixedStartingColonists")) return;

            bool enabledStartingColonists = ((SettingHandle<bool>)FB.config["EnableMixedStartingColonists"]).Value;
            bool enabledRefugees          = ((SettingHandle<bool>)FB.config["EnableMixedRefugees"         ]).Value;
            bool enabledSlaves            = ((SettingHandle<bool>)FB.config["EnableMixedSlaves"           ]).Value;
            bool enabledWanderers         = ((SettingHandle<bool>)FB.config["EnableMixedWanderers"        ]).Value;

            if (!Base.hasAlienRace) return;

            var pks = DefDatabase<AlienRace.RaceSettings>.GetNamed("FactionBlender_RaceSettings").pawnKindSettings;

            var pkeLists = new List<List<AlienRace.PawnKindEntry>> {
                pks.alienrefugeekinds, pks.alienslavekinds, pks.alienwandererkinds[0].pawnKindEntries, pks.startingColonists[0].pawnKindEntries
            };

            // Clear out old settings, if any
            pkeLists.ForEach( pkel => pkel.RemoveAll(x => true) );

            // If everything is disabled, short-circuit here
            if (!enabledStartingColonists && !enabledRefugees && !enabledSlaves && !enabledWanderers) return;

            /* AlienRace's pawn generation system works by collecting all of the PKEs, looking at the chance,
             * and if it hits the chance, and randomly picks a kindDef from the PKE bucket to spawn (equal chance
             * here).  And then it keeps going.  So, it could end up with a 100% entry, but still snag a 10 or 1%
             * entry on the way down.
             * 
             * We'll take advantage of this by always have a 100% bucket for most of the pawns, so we're
             * guaranteed to have a variety pool.  If it ends up getting hit by one of the other pools, so be
             * it, but at least it will never hit the vanilla basicMemberKind "pool".
             */

            // Slaves will just have a 100% bucket, which we'll insert directly
            if (enabledSlaves) {
                pks.alienslavekinds.Add( new AlienRace.PawnKindEntry() );
                pks.alienslavekinds[0].chance = 100;
                pks.alienslavekinds[0].kindDefs.AddRange(
                    DefDatabase<PawnKindDef>.AllDefs.Where(
                        // Any non-fighter is probably a "slave" type.  But exclude traders.  It doesn't make any sense to 
                        // have traders trying to trade away themselves.
                        pawn => pawn.RaceProps.Humanlike && !pawn.isFighter && !pawn.trader && FB.FilterPawnKindDef(pawn, "global")
                    ).Select(pawn => pawn).ToList()
                );
            }

            // Everything else will have (the same) chance buckets, based on combat power
            var chanceBuckets = new Dictionary<int, AlienRace.PawnKindEntry>();

            // Before we start, figure out if there are any PKDs that seem outnumbered, based on the number of
            // PKDs tied to that race.  We'll use that to balance the kindDef string counts.
            List<PawnKindDef> allFilteredPKDs = DefDatabase<PawnKindDef>.AllDefs.Where( pkd =>
                pkd.RaceProps.Humanlike && Base.Instance.FilterPawnKindDef(pkd, "global") &&
                // Creep joiners have their own events, and not really compatible as starting colonists
                pkd is not CreepJoinerFormKindDef
            ).ToList();

            var raceCounts = new Dictionary<string, int>();
            allFilteredPKDs.ForEach( pawn => {
                string name = pawn.race.defName;
                if (!raceCounts.ContainsKey(name)) raceCounts[name] = 0;
                raceCounts[name]++;
            });

            // Loop through each humanlike PawnKindDef
            foreach (PawnKindDef pawn in allFilteredPKDs) {
                string name = pawn.defName;

                // Calculate the chance
                // 50 and below = 100% chance (base colonist is 35)
                // 75  = 20%
                // 100 = ~14% --> 10%
                // 150 = 10% (good pirate mercs)
                // 250 = 7% (thrumbo race)
                int chance = Mathf.RoundToInt( 100 / Mathf.Sqrt( Mathf.Max(1, pawn.combatPower - 50) ) );

                // Use increments of 10% until we get to 10%, and make sure we don't try for 0%
                if (chance >= 10) chance = Mathf.RoundToInt( chance / 10f ) * 10;
                if (chance <= 0)  chance = 1;

                if (!chanceBuckets.ContainsKey(chance)) chanceBuckets[chance] = new AlienRace.PawnKindEntry { chance = chance };

                // Add a number of entries based on the popularity of the race within PKDs (maximum of 8)
                int numEntries = Mathf.Clamp(Mathf.RoundToInt( 8 / raceCounts[pawn.race.defName] ), 1, 8);
                foreach (int i in Enumerable.Range(1, numEntries)) {
                    chanceBuckets[chance].kindDefs.Add(pawn);
                }
            }

            var newPKEList = chanceBuckets.Values.ToList();
            newPKEList.SortByDescending(pke => pke.chance);

            if (enabledRefugees)          pks.alienrefugeekinds                    .AddRange(newPKEList);
            if (enabledWanderers)         pks.alienwandererkinds[0].pawnKindEntries.AddRange(newPKEList);
            if (enabledStartingColonists) pks.startingColonists [0].pawnKindEntries.AddRange(newPKEList);
        }
    }
}
