using HarmonyLib;
using HugsLib.Settings;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;
using Verse.AI;
using UnityEngine;

namespace FactionBlender {
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    internal class HarmonyPatches {
        /* Fix all of the biome.allowedPackAnimals, so that more animals can be proper pack animals.
         *
         * Honestly, putting animal-related information in BiomeDef is a bad idea, because NOBODY bothers
         * to add anything in this field when creating a new animal.  I have RimWorld stuffed with just
         * about every major animal mod out there, and without this fix, the only pack animals that manage
         * to show up are Lórien deer and Muffalo.  No dinos, no genetic hybrids, no alpha animals, nothing
         * except those two.
         *
         * This _should_ be its own mod.  I might split this off eventually.
         */

        [HarmonyPatch]
        public static class BiomeDef_IsPackAnimalAllowed_Patch {
            /* XXX: Trying to figure out the min-max temperatures of a biome by indirect inference of the
             * BiomeWorker scores is a very dicey at best.  I'm just going to resort to a static list, and
             * it's not going to be perfect...
             *
             * Absolute min/max of planet is generally -50C to 40C.  Muffalos, the universally compatible
             * pack animal, has a min/max of -55C to 45C.  Assume a seasonal temp swing of +/-25C, after
             * figuring out the average temp ranges (ie: tile.temperature).
             *
             * Currently supports:
             *   Vanilla
             *   Advanced Biomes
             *   Alpha Biomes
             *   Rainbeau's Realistic Planets
             *   Rainbeau's Permafrost
             *   Nature's Pretty Sweet
             *   Mallorn Forest (from Lord of the Rims - Elves)
             *   Terra Project
             *   Misc. MapGen Xtension 'Urban Biome'
             *   More Vanilla Biomes
             */
            public static readonly Dictionary<string, int[]> biomeMinMaxTemp = new Dictionary<string, int[]> {
                { "SeaIce",                 new[] {-50, 5} },
                { "IceSheet",               new[] {-50, 5} },
                { "Tundra",                 new[] {-50,25} },
                { "Permafrost",             new[] {-50, 5} },
                { "RRP_Permafrost",         new[] {-50, 5} },
                { "TundraSkerries",         new[] {-50,35} },
                { "AB_PropaneLakes",        new[] {-50, 5} },
                { "ZBiome_CloudForest",     new[] {-50,35} },
                { "ZBiome_Iceberg_NoBeach", new[] {-50,10} },

                { "TemperateForest",     new[] {-35,25} },
                { "TemperateSwamp",      new[] {-35,25} },
                { "BorealForest",        new[] {-35,25} },
                { "ColdBog",             new[] {-35,25} },
                { "RRP_Grassland",       new[] {-35,40} },
                { "MallornForest",       new[] {-35,40} },
                { "TKKN_Oasis",          new[] {-35,40} },
                { "TKKN_RedwoodForest",  new[] {-35,35} },
                { "TKKN_SequoiaForest",  new[] {-35,40} },
                { "TKKN_Grasslands",     new[] {-35,40} },
                { "CaveOasis",           new[] {-35,40} },
                { "TunnelworldCave",     new[] {-35,40} },
                { "CaveEntrance",        new[] {-35,40} },
                { "DesertHighPlains",    new[] {-35,40} },
                { "Archipelago",         new[] {-35,40} },
                { "AB_GallatrossGraveyard",     new[] {-35,40} },
                { "AB_GelatinousSuperorganism", new[] {-35,40} },
                { "TemperateForest_UrbanRuins", new[] {-35,25} },
                { "TemperateSwamp_UrbanRuins",  new[] {-35,25} },
                { "BorealForest_UrbanRuins",    new[] {-35,25} },
                { "ZBiome_AlpineMeadow", new[] {-35,40} },
                { "ZBiome_CoastalDunes", new[] {-30,40} },
                { "ZBiome_DesertOasis",  new[] {-35,40} },
                { "ZBiome_Grasslands",   new[] {-35,40} },
                { "ZBiome_Marsh",        new[] {-35,40} },

                { "Desert",              new[] {-25,40} },
                { "AridShrubland",       new[] {-25,40} },
                { "Wasteland",           new[] {-25,40} },
                { "RRP_TemperateDesert", new[] {-25,35} },
                { "TKKN_Desert",         new[] {-25,40} },
                { "VolcanicIsland",      new[] {-25,40} },
                { "AB_RockyCrags",       new[] {-25,40} },
                { "ZBiome_Sandbar_NoBeach", new[] {-25,40} },

                { "TKKN_VolcanicFlow",   new[] {-20,40} },

                { "ExtremeDesert",       new[] {-15,40} },
                { "RRP_Oasis",           new[] {-15,40} },

                { "RRP_Steppes",         new[] {-10,40} },
                { "AB_MycoticJungle",    new[] {-10,40} },
                { "AB_OcularForest",     new[] {-10,40} },
                { "Oasis",               new[] {-10,40} },

                { "TropicalRainforest",  new[] { -5,40} },
                { "TropicalSwamp",       new[] { -5,40} },
                { "PoisonForest",        new[] { -5,40} },
                { "RRP_Savanna",         new[] { -5,40} },
                { "Atoll",               new[] { -5,40} },
                { "AB_FeraliskInfestedJungle",     new[] { -5,40} },
                { "AB_MechanoidIntrusion",         new[] { -5,40} },
                { "TropicalRainforest_UrbanRuins", new[] { -5,40} },
                { "TropicalSwamp_UrbanRuins",      new[] { -5,40} },

                { "Volcano",             new[] {  0,40} },
                { "Wetland",             new[] {  0,40} },
                { "Savanna",             new[] {  0,40} },
                { "TKKN_Savanna",        new[] {  0,40} },
            };

            public static HashSet<string> warnedAboutBiome = new HashSet<string> {};

            [HarmonyPatch(typeof(BiomeDef), "IsPackAnimalAllowed")]
            [HarmonyPostfix]
            static void IsPackAnimalAllowed_Patch(BiomeDef __instance, ref bool __result, List<ThingDef> ___allowedPackAnimals, ThingDef pawn) {
                // If the original already gave us a positive result, just accept it and short-circuit
                if (__result) return;

                RaceProperties race = pawn.race;

                // Needs to be a pack animal, first of all
                if (!race.packAnimal) return;

                // NOTE: For purposes of caching, we add the animal to ___allowedPackAnimals.  But,
                // this doesn't cover negative caching.

                // If it's already on the wildAnimal list, just accept it
                if ( __instance.AllWildAnimals.Any(e => (e.defName == pawn.defName)) ) {
                    __result = true;
                    ___allowedPackAnimals.AddDistinct(pawn);
                    return;
                }

                // Make sure the animal's comfort zone fits inside the min/max biome temperature ranges

                string biomeName = __instance.defName;
                if (!biomeMinMaxTemp.ContainsKey(biomeName)) {
                    // We don't have a temperature defined, so hope for the best
                    if (!warnedAboutBiome.Contains(biomeName)) {
                        Log.Warning(
                            "[FactionBlender] Unrecognized biome " + biomeName + ".  Accepting all pack animals for traders here, which might cause some " +
                            "to freeze/burn to death, if the biome is hostile.  Ask the FB dev to include the biome in the static min/max temp list."
                        );
                        warnedAboutBiome.Add(biomeName);
                    }

                    __result = true;
                    ___allowedPackAnimals.AddDistinct(pawn);
                    return;
                }

                var minMaxTemp = biomeMinMaxTemp[biomeName];
                int minTemp = minMaxTemp[0];
                int maxTemp = minMaxTemp[1];

                if (
                    pawn.GetStatValueAbstract(StatDefOf.ComfyTemperatureMin, null) <= minTemp &&
                    pawn.GetStatValueAbstract(StatDefOf.ComfyTemperatureMax, null) >= maxTemp
                ) {
                    // Success
                    __result = true;
                    ___allowedPackAnimals.AddDistinct(pawn);
                    return;
                }

                // Fell through: keep the false value
                return;
            }
        }

        /* Fix CanBeBuilder checks to not crash.
         *
         * The core CanBeBuilder (and Torann's A RimWorld of Magic) makes some bad assumptions about what
         * properties are available for pawns.  Some animals and non-humans might not have a story or
         * definition.  So, protect all of that by checking all levels of expected objects for undefinedness.
         * 
         * Also, in 1.3, it also assumes non-humanoids have builder skills, which they don't.
         */

        [HarmonyPatch(typeof(LordToil_Siege), "CanBeBuilder")]
        [HarmonyPrefix]
        private static bool CanBeBuilder_Patch(Pawn p, ref bool __result) {
            if (p?.def?.thingClass == null || p?.skills == null || p?.workSettings == null) {
                __result = false;
                return false;
            }
            return true;
        }

        /* A better version of GenerateCarriers.
         *
         * Vanilla pack animal creation for caravans doesn't really take into account the variety of pack
         * animals we now have.  It typically only creates 3-4 pack animals and randomly stuffs them with all
         * of the wares.  This will happen even if, say, tiny chickenffalos can't hold the massive load.  Or
         * it will split them up among 3-4 huge paraceramuffalo that could still hold 10 times the amount.
         *
         * I wish I had the patience to figure out how make the small tweak as a transpiler fix.  However,
         * since I plan on just writing this override, I might as well add in my other ideas.
         *
         * Like the IsPackAnimalAllowed patch, this maybe should be its own "Fix Pack Animals" mod.
         */

        [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), "GenerateCarriers")]
        // This may be an complete override, but if anybody wants to add another prefix, it will default to
        // run before this.  I don't think anybody's really messed with this method, though.
        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        private static bool GenerateCarriers_Override(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, Pawn trader, List<Thing> wares, List<Pawn> outPawns) {
            Func<Thing, float> massTotaler = t => t.stackCount * t.GetStatValue(StatDefOf.Mass, true);

            List<Thing> list = wares.Where(t => !(t is Pawn)).ToList();
            list.SortByDescending(massTotaler);

            float ttlMassThings = list.Sum(massTotaler);
            float ttlCapacity   = 0f;
            float ttlBodySize   = 0f;
            int   numCarriers   = 0;

            IEnumerable<PawnGenOption> carrierKinds = groupMaker.carriers.Where(p => {
                if (parms.tile != -1)
                    return Find.WorldGrid[parms.tile].biome.IsPackAnimalAllowed(p.kind.race);
                return true;
            });

            PawnKindDef kind = carrierKinds.RandomElementByWeight(x => x.selectionWeight).kind;

            // No slow or small juveniles
            Predicate<Pawn> validator = (p =>
                p.ageTracker.CurLifeStage.bodySizeFactor >= 1 &&
                p.GetStatValue(StatDefOf.MoveSpeed, true) >= p.kindDef.race.GetStatValueAbstract(StatDefOf.MoveSpeed)
            );

            // 50/50 chance of uniform carriers (like vanilla) or mixed carriers
            bool mixedCarriers = Rand.RangeInclusive(0, 1) == 1;

            // Generate all of the carrier pawns (empty).  Either we spawn as many pawns as we need to cover
            // 120% of the weight of the items, or enough pawns before it seems "unreasonable" based on body
            // size.
            List<Pawn> carrierPawns = new List<Pawn> {};
            for (; ttlCapacity < ttlMassThings * 1.2 && ttlBodySize < 20; numCarriers++) {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind:             kind,
                    faction:          parms.faction,
                    tile:             parms.tile,
                    inhabitant:       parms.inhabitants,
                    fixedIdeo:        parms.ideo,
                    validatorPreGear: validator
                );
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                carrierPawns.Add(pawn);

                ttlCapacity += MassUtility.Capacity(pawn);
                // Still can't have 100 chickenmuffalos.  That might slow down some PCs.
                ttlBodySize += Mathf.Max(pawn.BodySize, 0.5f);

                if (mixedCarriers) kind = carrierKinds.RandomElementByWeight(x => x.selectionWeight).kind;

                // Include a hard limit of 50 pack animals for extreme ttlMassThings scenarios, like massive
                // percentages from Supply and Demand
                if (numCarriers >= 50) break;
            }

            // Add items (in descending order of weight) to randomly chosen pack animals.  This isn't the most
            // efficient routine, as we're trying to be a bit random.  If I was trying to be efficient, I would
            // use something like SortByDescending(p.Capacity) against the existing thing list.
            foreach (Thing thing in list) {
                List<Pawn> validPawns = carrierPawns.FindAll(p => !MassUtility.WillBeOverEncumberedAfterPickingUp(p, thing, thing.stackCount));

                if (validPawns.Count() != 0) {
                    validPawns.RandomElement().inventory.innerContainer.TryAdd(thing, true);
                }
                else if (thing.stackCount > 1) {
                    // No carrier can handle the full stack; split it up
                    int countLeft = thing.stackCount;
                    int c = 0;  // safety counter (while loops can be dangerous)
                    while (countLeft > 0) {
                        validPawns = carrierPawns.FindAll(p => MassUtility.CountToPickUpUntilOverEncumbered(p, thing) >= 1);
                        if (validPawns.Count() != 0 && c < thing.stackCount) {
                            Pawn pawn = validPawns.RandomElement();
                            int countToAdd = Mathf.Min( MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing), countLeft );
                            countLeft -= pawn.inventory.innerContainer.TryAdd(thing, countToAdd, true);
                        }
                        else {
                            // Either no carrier can handle a single item, or we're just in some bad while loop breakout.  In
                            // any case, force it in, evenly split among all carriers.
                            int splitCount = Mathf.FloorToInt(countLeft / carrierPawns.Count());
                            if (splitCount > 0) {
                                carrierPawns.ForEach(p => p.inventory.innerContainer.TryAdd(thing, splitCount, true));
                                countLeft -= splitCount * carrierPawns.Count();
                            }

                            // Give the remainer to the ones with space (one at a time)
                            while (countLeft > 0) {
                                carrierPawns.MaxBy(p => MassUtility.FreeSpace(p)).inventory.innerContainer.TryAdd(thing, 1, true);
                                countLeft--;
                            }
                            break;
                        }
                        c++;
                    }
                }
                else {
                    // No way to split it; force it in
                    carrierPawns.MaxBy(p => MassUtility.FreeSpace(p)).inventory.innerContainer.TryAdd(thing, true);
                }
            }

            // Finally, add in the carrierPawns to the out list
            outPawns.AddRange(carrierPawns);

            // Always skip the original method
            return false;
        }

        /* Warn user of badly-behaving pawns that attack their own faction (or friendlies).
         *
         * This seems to happen more often than you would think, because many mods assume they are in a
         * homogenous faction that wouldn't possibly have the other enemy in it, especially if they have their
         * own AttackTargetSearcher/Finder or BestAttackTarget method.
         *
         * I attempted to modify the behavior here, but nothing short of a RaceProperties transplant with a new
         * set of ThinkTrees would work here.
         */

        [HarmonyPatch]
        public static class MindStateTick_Patch {
            public static Dictionary<string, bool> hasWarnedAboutMisbehavingPawn = new Dictionary<string, bool> {};

            [HarmonyPatch(typeof(Pawn_MindState), "MindStateTick")]
            [HarmonyPrefix]
            private static void Patch(Pawn_MindState __instance) {
                Pawn pawn = __instance?.pawn;

                // Early state?
                if (pawn == null) return;
                if (pawn.Faction?.def?.defName == null) return;

                // Not ours; short-circuit
                if (!pawn.Faction.def.defName.Contains("FactionBlender")) return;

                // Already warned about it; short-circuit
                if ( hasWarnedAboutMisbehavingPawn.ContainsKey(pawn.ToString()) ) return;

                // Check the current job for hostilities
                Job job = pawn.CurJob;
                if (
                    job != null && TryCheckFriendlyFaction(pawn, job.targetA.Thing) && (
                        job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic || job.def == JobDefOf.Hunt || job.def == JobDefOf.Ignite ||
                        job.def == JobDefOf.Ingest || job.def == JobDefOf.Kidnap || job.def == JobDefOf.PredatorHunt || job.def == JobDefOf.Slaughter
                    )
                ) {
                    // Warn about it
                    Pawn tPawn = job.targetA.Thing as Pawn;
                    Base.Instance.ModLogger.Warning(
                        "Friendly faction attack (" + job.def.ToString() + ") between " + pawn.ToString() + " and " + tPawn.ToString() + ".  " +
                        "The pawn type may need to be blacklisted in the Faction Blender configuration.  " +
                        "(Race: " + pawn.kindDef.race.defName + ", defaultFactionType: " + pawn.kindDef.defaultFactionType + ")"
                    );
                    hasWarnedAboutMisbehavingPawn.Add(pawn.ToString(), true);
                }

                return;
            }

            internal static bool TryCheckFriendlyFaction(Pawn pawn, Thing target) {
                if (target == null) return false;
                Pawn tPawn = target as Pawn;
                if (tPawn  == null) return false;
                if (tPawn  == pawn) return false;

                if (!pawn.Faction.HostileTo(tPawn.Faction) && !GenHostility.IsActiveThreatTo(tPawn, pawn.Faction)) return true;
                return false;
            }
        }

        /* Automatically fix conflicts between FactionDef->apparelStuffFilter and PawnKindDef->apparelRequired->stuffCategories.
         *
         * Apparently, if the game tries to create a tribal's (wooden) war mask in a faction with conflicting
         * apparelStuffFilters, it can't make the pawn.  So, auto-detect the condition and auto-fix it.
         *
         * Some extra details: https://ludeon.com/forums/index.php?topic=50672.0
         */
        [HarmonyPatch(typeof(PawnApparelGenerator), "GenerateWorkingPossibleApparelSetFor")]
        [HarmonyPrefix]
        private static void GenerateWorkingPossibleApparelSetFor_Patch(Pawn pawn, List<ThingStuffPair> ___allApparelPairs) {
            // Short-circuit
            if (pawn == null || pawn.Faction == null || pawn.kindDef.apparelRequired == null) return;

            // [Reflection prep] PawnApparelGenerator.CanUseStuff(pawn, pa);
            MethodInfo CanUseStuffMethod = AccessTools.Method(typeof(PawnApparelGenerator), "CanUseStuff");

            List<ThingDef> reqApparel = pawn.kindDef.apparelRequired;
            for (int i = 0; i < reqApparel.Count; i++) {
                IEnumerable<ThingStuffPair> pairs = ___allApparelPairs.Where(
                    pa => pa.thing == reqApparel[i]
                );

                // The original method is going to have a bad time, so auto-add an appropriate filter to fix it
                if ( !pairs.Any( pa => (bool)CanUseStuffMethod.Invoke(null, new object[] { pawn, pa }) && pa.Commonality > 0 ) ) {
                    string logMsg =
                        "Found an apparelStuffFilter/stuffCategories conflict for required apparel " +
                        reqApparel[i] + " while generating apparel for " + pawn.kindDef.defName + "; "
                    ;

                    ThingFilter factionFilter = pawn.Faction.def.apparelStuffFilter;
                    string      factionName   = pawn.Faction.def.defName;
                    if (factionFilter != null) {
                        List<StuffCategoryDef> stuffCategories = reqApparel[i].stuffCategories;
                        ThingStuffPair examplePair = pairs.RandomElementByWeight(pa => pa.Commonality);

                        if (stuffCategories == null && examplePair != null && examplePair.stuff != null) stuffCategories = examplePair.stuff.stuffProps.categories;

                        if (stuffCategories != null) {
                            logMsg = logMsg + "adding extra stuffCategories to " + factionName + "'s apparelStuffFilter: " + string.Join(", ", stuffCategories);

                            foreach (StuffCategoryDef sc in stuffCategories) {
                                factionFilter.SetAllow(sc, true);
                            }
                        }
                        else if (examplePair.stuff != null) {
                            logMsg = logMsg + "adding " + examplePair.stuff + " to " + factionName + "'s apparelStuffFilter";
                            factionFilter.SetAllow(examplePair.stuff, true);
                        }
                        else if (examplePair != null) {
                            logMsg = logMsg + "adding " + examplePair.thing + " to " + factionName + "'s apparelStuffFilter";
                            factionFilter.SetAllow(examplePair.thing, true);
                        }
                        else {  // probably pendatic, but ¯\_(ツ)_/¯
                            logMsg = logMsg + "adding " + reqApparel[i] + " to " + factionName + "'s apparelStuffFilter";
                            factionFilter.SetAllow(reqApparel[i], true);
                        }
                    }
                    else {
                        logMsg = logMsg + "cannot fix since there is no apparelStuffFilter for " + factionName;
                    }

                    Base.Instance.ModLogger.Warning(logMsg);
                }
            }

            return;
        }

        /* Don't attempt to create a title for a pawn if the faction doesn't exist.
         *
         * This may happen because the user disabled the Royalty faction, but the FB pawn is a Royalty pawn that requires a title.
         */

        [HarmonyPatch(typeof(PawnGenerator), "TryGenerateNewPawnInternal")]
        [HarmonyPrefix]
        private static void TryGenerateNewPawnInternal_Patch(ref PawnGenerationRequest request) {
            // Royalty faction exists; bounce out
            if (Find.FactionManager.RandomRoyalFaction() != null) return;

            request.FixedTitle     = null;
            request.ForbidAnyTitle = true;
        }

        /*
         * These are a series of patches against Ancient pawn generators, to allow a mixed set of pawns.
         */

        [HarmonyPatch]
        public static class GenerateAncientsPatches {
            private static Pawn GenerateBaseAncient(ThingSetMaker_MapGen_AncientPodContents instance) {
                // Faction grabber, with a few backup plans
                Faction faction = null;
                if (faction == null) faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def.defName == "FactionBlender_Civil");
                if (faction == null) faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.def.defName == "FactionBlender_Pirate");
                if (faction == null) Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, tryMedievalOrBetter: true, allowDefeated: true, allowTemporary: true, minTechLevel: TechLevel.Spacer);
                if (faction == null) Find.FactionManager.TryGetRandomNonColonyHumanlikeFaction(out faction, tryMedievalOrBetter: true, allowDefeated: true, allowTemporary: true);

                // 20% chance of animals and others
                PawnKindDef pawnKind = null;
                bool allowNonHuman = Rand.Range(1, 5) == 1;
                for (int i = 0; i < 10; i++) {
                    pawnKind = faction != null ? faction.RandomPawnKind() : PawnKindDefOf.AncientSoldier;  // fallback of last resort

                    if (pawnKind.RaceProps.baseBodySize >= 3)          continue;  // maybe no dinosaurs in cryptosleep caskets?
                    if (allowNonHuman || pawnKind.RaceProps.Humanlike) break;
                }

                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind: pawnKind,
                    faction: Faction.OfAncients,
                    certainlyBeenInCryptosleep: true
                ));

                // [Reflection] this.GiveRandomLootInventoryForTombPawn(pawn);
                MethodInfo giveLootMethod = AccessTools.Method(typeof(ThingSetMaker_MapGen_AncientPodContents), "GiveRandomLootInventoryForTombPawn");
                giveLootMethod.Invoke(instance, new object[] { pawn });

                return pawn;
            }

            [HarmonyPatch(typeof(ThingSetMaker_MapGen_AncientPodContents), "GenerateFriendlyAncient")]
            [HarmonyPrefix]
            private static bool GenerateFriendlyAncient_Override(ThingSetMaker_MapGen_AncientPodContents __instance, ref Pawn __result) {
                // Skip this method (if the config is disabled)
                if (!(SettingHandle<bool>)Base.Config["EnableMixedAncients"]) return true;

                __result = GenerateBaseAncient(__instance);

                // Skip the original method
                return false;
            }

            [HarmonyPatch(typeof(ThingSetMaker_MapGen_AncientPodContents), "GenerateIncappedAncient")]
            [HarmonyPrefix]
            private static bool GenerateIncappedAncient_Override(ThingSetMaker_MapGen_AncientPodContents __instance, ref Pawn __result) {
                // Skip this method (if the config is disabled)
                if (!(SettingHandle<bool>)Base.Config["EnableMixedAncients"]) return true;

                Pawn pawn = GenerateBaseAncient(__instance);
                HealthUtility.DamageUntilDowned(pawn, true);

                __result = pawn;

                // Skip the original method
                return false;
            }

            [HarmonyPatch(typeof(ThingSetMaker_MapGen_AncientPodContents), "GenerateAngryAncient")]
            [HarmonyPrefix]
            private static bool GenerateAngryAncient_Override(ThingSetMaker_MapGen_AncientPodContents __instance, ref Pawn __result) {
                // Skip this method (if the config is disabled)
                if (!(SettingHandle<bool>)Base.Config["EnableMixedAncients"]) return true;

                Pawn pawn = GenerateBaseAncient(__instance);
                pawn.SetFactionDirect(Faction.OfAncientsHostile);

                __result = pawn;

                // Skip the original method
                return false;
            }

            [HarmonyPatch(typeof(ThingSetMaker_MapGen_AncientPodContents), "GenerateHalfEatenAncient")]
            [HarmonyPrefix]
            private static bool GenerateHalfEatenAncient_Override(ThingSetMaker_MapGen_AncientPodContents __instance, ref Pawn __result) {
                // Skip this method (if the config is disabled)
                if (!(SettingHandle<bool>)Base.Config["EnableMixedAncients"]) return true;

                Pawn pawn = GenerateBaseAncient(__instance);

                int num = Rand.Range(6, 10);
                for (int index = 0; index < num; ++index) {
                    pawn.TakeDamage(new DamageInfo(
                        def: DamageDefOf.Bite,
                        amount: Rand.Range(3, 8),
                        instigator: (Thing) pawn
                    ));
                }

                __result = pawn;

                // Skip the original method
                return false;
            }

            // Find other appropriate pawnkind types besides just megascarabs
            [HarmonyPatch(typeof(ThingSetMaker_MapGen_AncientPodContents), "GenerateScarabs")]
            [HarmonyPrefix]
            private static bool GenerateScarabs_Override(ThingSetMaker_MapGen_AncientPodContents __instance, ref List<Thing> __result) {
                // Skip this method (if the config is disabled)
                if (!(SettingHandle<bool>)Base.Config["EnableMixedAncients"]) return true;

                PawnKindDef pawnKind = DefDatabase<PawnKindDef>.AllDefsListForReading.Where(p =>
                    p.isFighter && !p.RaceProps.Humanlike &&
                    p.combatPower >= 20 && p.combatPower <= 240 &&
                    (
                        // Other kinds of insects
                        p.RaceProps.FleshType == FleshTypeDefOf.Insectoid ||
                        // Something something creepy crawlies
                        Regex.IsMatch(
                            p.RaceProps.meatDef.label.ToLower(),
                            @"\b(?:insect|bug|snake|rat|arachnid|spider|worm|ant|slug)\b|cobraflesh|" +
                            // Some people might call this evil, but who's the one who installed the AvP mod?  We all know this is the
                            // kind of frightening stuff you wanted...
                            "xenomorph flesh"
                        ) ||
                        // Work around the Optimization: Meats mod
                        Regex.IsMatch(
                            p.RaceProps.body.label.ToLower(),
                            @"\b(?:insect|bug|snake|rat|arachnid|spider|worm|ant|slug)\b|cobra|facehugger"
                        )
                    ) && p.RaceProps.baseBodySize <= 0.6
                ).RandomElement();

                List<Thing> thingList = new List<Thing>();
                int cpLimit = Rand.Range(120, 240);
                int ttlCP   = 0;
                while (ttlCP < cpLimit) {
                    Pawn pawn = PawnGenerator.GeneratePawn(pawnKind);
                    pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
                    thingList.Add(pawn);
                    ttlCP += (int)pawnKind.combatPower;
                }

                __result = thingList;

                // Skip the original method
                return false;
            }

        }

    }
}
