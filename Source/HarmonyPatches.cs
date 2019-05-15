using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using UnityEngine;

namespace FactionBlender {
    [StaticConstructorOnStartup]
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

        [HarmonyPatch(typeof(RimWorld.BiomeDef), "IsPackAnimalAllowed")]
        public static class BiomeDef_IsPackAnimalAllowed_Patch {
            /* XXX: Trying to figure out the min-max temperatures of a biome by indirect inference of the
             * BiomeWorker scores is a very dicey at best.  I'm just going to resort to a static list, and
             * it's not going to be perfect...
             */

            // Currently supports vanilla, Advanced Biomes, and Mallorn Forest (from Lord of the Rims - Elves)
            public static readonly Dictionary<string, int> minBiomeTemp = new Dictionary<string, int> {
                { "SeaIce",           -50 },
                { "IceSheet",         -50 },
                { "Tundra",           -50 },

                { "BorealForest",     -35 },
                { "ColdBog",          -35 },

                { "TemperateForest",    -25 },
                { "TemperateSwamp",     -25 },
                { "MallornForest",      -25 },
                { "TropicalRainforest", -25 },
                { "PoisonForest",       -25 },
                { "TropicalSwamp",      -25 },
                { "Desert",             -25 },
                { "Wasteland",          -25 },

                { "AridShrubland",   0 },
                { "ExtremeDesert",   0 },
                { "Volcano",         0 },
                { "Wetland",         0 },
                { "Savanna",         0 },
            };

            public static readonly Dictionary<string, int> maxBiomeTemp = new Dictionary<string, int> {
                { "SeaIce",   5 },
                { "IceSheet", 5 },

                { "Tundra",       25 },
                { "BorealForest", 25 },
                { "ColdBog",      25 },

                { "TemperateForest",    35 },
                { "TemperateSwamp",     35 },
                { "MallornForest",      35 },
                { "TropicalRainforest", 35 },
                { "PoisonForest",       35 },
                { "TropicalSwamp",      35 },
                { "Desert",             35 },
                { "Wasteland",          35 },
                { "AridShrubland",      35 },
                { "ExtremeDesert",      35 },
                { "Volcano",            35 },
                { "Wetland",            35 },
                { "Savanna",            35 },
            };

            [HarmonyPostfix]
            static void IsPackAnimalAllowed_Postfix(RimWorld.BiomeDef __instance, ref bool __result, List<ThingDef> ___allowedPackAnimals, ThingDef pawn) {
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
                if (!minBiomeTemp.ContainsKey(biomeName)) {
                    // We don't have a temperature defined, so hope for the best
                    Log.ErrorOnce(
                        "[FactionBlender] Unrecognized biome " + biomeName + ".  Accepting all pack animals for traders here, which might cause some " +
                        "to freeze/burn to death, if the biome is hostile.  Ask the dev to include the biome in the static min/max temp list.",
                        __instance.debugRandomId + 1688085595
                    );

                    __result = true;
                    ___allowedPackAnimals.AddDistinct(pawn);
                    return;
                }
                else if (!maxBiomeTemp.ContainsKey(biomeName)) {
                    Log.ErrorOnce(
                        "[FactionBlender] Found minBiomeTemp without matching maxBiomeTemp for " + biomeName + "!  Dev goofed up!",
                        __instance.debugRandomId + 1688085595
                    );
                    Log.TryOpenLogWindow();

                    __result = true;
                    ___allowedPackAnimals.AddDistinct(pawn);
                    return;
                }

                int minTemp = minBiomeTemp[biomeName];
                int maxTemp = maxBiomeTemp[biomeName];

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
         */

        [HarmonyPatch(typeof (LordToil_Siege), "CanBeBuilder", null)]
        public static class CanBeBuilder_Patch {
            [HarmonyPrefix]
            private static bool Prefix(Pawn p, ref bool __result) {
                if (
                    // Carefully short-circuit, so that we don't cause our own ticking exception
                    p == null ||
                    p.def == null || p.story == null ||
                    p.def.thingClass == null
                ) {
                    __result = false;
                    return false;
                }
                return true;
            }
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

        [HarmonyPatch(typeof (PawnGroupKindWorker_Trader), "GenerateCarriers", null)]
        public static class GenerateCarriers_Override {
            
            // This may be an complete override, but if anybody wants to add another prefix, it will default to
            // run before this.  I don't think anybody's really messed with this method, though.
            [HarmonyPriority(Priority.Last)]
            [HarmonyPrefix]
            private static bool Prefix(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, Pawn trader, List<Thing> wares, List<Pawn> outPawns) {
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
                for (; ttlCapacity < ttlMassThings * 1.2 && ttlBodySize < 20; numCarriers++) {
                    // Still the most abusive constructor I've ever seen...
                    PawnGenerationRequest request = new PawnGenerationRequest(
                        kind, parms.faction, PawnGenerationContext.NonPlayer, parms.tile, false, false, false, false, true, false, 1f, false, true, true,
                        parms.inhabitants, false, false, false, validator, null, new float?(), new float?(), new float?(), new Gender?(), new float?(), null
                    );
                    Pawn pawn = PawnGenerator.GeneratePawn(request);
                    outPawns.Add(pawn);
                    
                    ttlCapacity += MassUtility.Capacity(pawn);
                    // Still can't have 100 chickenmuffalos.  That might slow down some PCs.
                    ttlBodySize += Mathf.Max(pawn.BodySize, 0.5f);

                    if (mixedCarriers) kind = carrierKinds.RandomElementByWeight(x => x.selectionWeight).kind;
                }

                // Add items (in descending order of weight) to randomly chosen pack animals.  This isn't the most
                // efficient routine, as we're trying to be a bit random.  If I was trying to be efficient, I would
                // use something like SortByDescending(p.Capacity) against the existing thing list.
                foreach (Thing thing in list) {
                    List<Pawn> validPawns = outPawns.FindAll(p => !MassUtility.WillBeOverEncumberedAfterPickingUp(p, thing, thing.stackCount));

                    if (validPawns.Count() != 0) {
                        validPawns.RandomElement().inventory.innerContainer.TryAdd(thing, true);
                    }
                    else if (thing.stackCount > 1) {
                        // No carrier can handle the full stack; split it up
                        int countLeft = thing.stackCount;
                        int c = 0;  // safety counter (while loops can be dangerous)
                        while (countLeft > 0) {
                            validPawns = outPawns.FindAll(p => MassUtility.CountToPickUpUntilOverEncumbered(p, thing) >= 1);
                            if (validPawns.Count() != 0 && c < thing.stackCount) {
                                Pawn pawn = validPawns.RandomElement();
                                int countToAdd = Mathf.Min( MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing), countLeft );
                                countLeft -= pawn.inventory.innerContainer.TryAdd(thing, countToAdd, true);
                            }
                            else {
                                // Either no carrier can handle a single item, or we're just in some bad while loop breakout.  In
                                // any case, force it in, evenly split among all carriers.
                                int splitCount = Mathf.FloorToInt(countLeft / outPawns.Count());
                                if (splitCount > 0) {
                                    outPawns.ForEach(p => p.inventory.innerContainer.TryAdd(thing, splitCount, true));
                                    countLeft -= splitCount * outPawns.Count();
                                }
                                
                                // Give the remainer to the ones with space (one at a time)
                                while (countLeft > 0) {
                                    validPawns = new List<Pawn>(outPawns);
                                    validPawns.SortByDescending(p => MassUtility.FreeSpace(p));
                                    validPawns.First().inventory.innerContainer.TryAdd(thing, 1, true);
                                    countLeft--;
                                }
                                break;
                            }
                            c++;
                        }
                    }
                    else {
                        // No way to split it; force it in
                        validPawns = new List<Pawn>(outPawns);
                        validPawns.SortByDescending(p => MassUtility.FreeSpace(p));
                        validPawns.First().inventory.innerContainer.TryAdd(thing, true);
                    }
                }

                // Always skip the original method
                return false;
            }
        }
    }
}
