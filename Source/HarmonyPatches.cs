using Harmony;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

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
    }
}
