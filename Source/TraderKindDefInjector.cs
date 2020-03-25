using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using HugsLib.Utils;
using UnityEngine;

namespace FactionBlender {
    public class TraderKindDefInjector {

        // Reduce fancy modded traders to similar categories
        public static readonly Dictionary<string, string> newTraderLabel = new Dictionary<string, string> {
            { "dwarven goods trader",       "bulk goods trader"    },
            { "trader",                     "bulk goods trader"    },
            { "scrap trader",               "bulk goods trader"    },
            { "odds trader",                "bulk goods trader"    },
            { "war merchant",               "combat supplier"      },
            { "weapons dealer",             "combat supplier"      },
            { "dwarven smithy",             "combat supplier"      },
            { "military industrial trader", "combat supplier"      },
            { "slaver",                     "pirate merchant"      },
            { "pet dealer",                 "pirate merchant"      },
            { "smuggler",                   "pirate merchant"      },
            { "drug dealer",                "pirate merchant"      },
            { "pharmaceutical trader",      "pirate merchant"      },
            { "black market trader",        "pirate merchant"      },
            { "shaman merchant",            "exotic goods trader"  },
            { "archelogical expedition",    "exotic goods trader"  },
            { "art dealer",                 "exotic goods trader"  },
            { "arcane items collector",     "exotic goods trader"  },
            { "artifact dealer",            "exotic goods trader"  },
            { "artists troupe",             "exotic goods trader"  },
            { "nomadic shepherd",           "farming goods trader" },
            { "agricultural trader",        "farming goods trader" },
            { "butcher",                    "farming goods trader" },
            { "livestock wranglers",        "farming goods trader" },
            { "pelt trader",                "fabric trader"        },
            { "textiles trader",            "fabric trader"        },
            { "herbal suppliers",           "medical goods trader" },
            { "surgical supplier",          "medical goods trader" },
            { "masonary material trader",   "mining company"       },
        };

        public static void InjectTraderKindDefsToFactions(List<FactionDef> FB_Factions) {
            // Fix caravanTraderKinds for the civil faction only
            FactionDef FB_Civil = FB_Factions[1];

            List<TraderKindDef> traderKindDefs = DefDatabase<FactionDef>.AllDefs.
                Where     (f => f != FB_Civil && f.defName != "OutlanderCivil").
                SelectMany(f => f.caravanTraderKinds).ToList()
            ;
            traderKindDefs.RemoveDuplicates();  // parent classes, etc.

            // Add the rest, while merging where we find (label-like) dupes
            foreach (var traderKind in traderKindDefs.ToList()) {
                string curLabel = traderKind.label?.ToLower();
                string newLabel = curLabel != null && newTraderLabel.ContainsKey(curLabel) ? newTraderLabel[curLabel] : null;
                int lm = FB_Civil.caravanTraderKinds.FirstIndexOf(tkd =>
                   tkd.label != null && tkd.label?.ToLower() == curLabel || tkd.label?.ToLower() == newLabel
                ); // returns Count on failure, not -1
            
                // If we somehow missed a dupe, skip it
                if (FB_Civil.caravanTraderKinds.Contains(traderKind)) continue;

                // If we found a label-like dupe, merge them
                else if (lm < FB_Civil.caravanTraderKinds.Count) {
                    var labelMatch = traderKindDefs[lm];
                    if (!labelMatch.defName.StartsWith("FB_Caravan_")) {
                        TraderKindDef newTraderKind = CopyTraderKindDef(labelMatch, "Caravan " + labelMatch.LabelCap);
                        traderKindDefs[lm] = newTraderKind;
                        labelMatch = newTraderKind;
                    }
                    MergeTraderKindDefs(labelMatch, traderKind);
                }

                // If we have a new one but it needs a new label, copy to a new one
                else if (newLabel != null) {
                    TraderKindDef newTraderKind = CopyTraderKindDef(traderKind, "Caravan " + newLabel);
                    newTraderKind.label = newLabel;
                    FB_Civil.caravanTraderKinds.Add(newTraderKind);
                }

                // Must be unique; add it
                else FB_Civil.caravanTraderKinds.Add(traderKind);
            }

            // Add every visitor trader as a combined list
            FB_Civil.visitorTraderKinds.Clear();
            FB_Civil.visitorTraderKinds.AddRange(
                DefDatabase<FactionDef>.AllDefs.Where(f => f != FB_Civil).SelectMany(f => f.visitorTraderKinds)
            );

            // The base gets to be Rich AF with a CostCo mega list
            var baseTraderkind = CopyTraderKindDef(FB_Civil.baseTraderKinds[0], "Base Trade Standard");
            FB_Civil.baseTraderKinds[0] = baseTraderkind;
            baseTraderkind.requestable = false;

            DefDatabase<FactionDef>.AllDefs.Where(f => f != FB_Civil).SelectMany(f => f.baseTraderKinds).ToList().ForEach( tkd =>
                MergeTraderKindDefs(baseTraderkind, tkd)
            );

            // Remove any slaves from the base stockGenerators
            baseTraderkind.stockGenerators = baseTraderkind.stockGenerators.Where(sg => !(sg is StockGenerator_Slaves)).ToList();
        }

        public static TraderKindDef CopyTraderKindDef(TraderKindDef origTraderKind, string labelBase) {
            string newDefName = "FB_" + GenText.ToTitleCaseSmart(labelBase).Replace(" ", "_");

            // Construction
            var newTraderKind = new TraderKindDef {
                defName         = newDefName,
                label           = origTraderKind.label,
                commonality     = origTraderKind.commonality,
                orbital         = false,
                requestable     = true,
                
                // Can't change most of these anyway, so we're just going to add them as a new list
                stockGenerators = origTraderKind.stockGenerators.ListFullCopyOrNull(),
            };

            InjectedDefHasher.GiveShortHashToDef(newTraderKind, typeof(TraderKindDef));

            return newTraderKind;
        }

        // XXX: Well, since I'm locked out of most of the properties in StockGenerator, this function is
        // less of a merge and more of a light copy.
        public static void MergeTraderKindDefs(TraderKindDef fbTraderKind, TraderKindDef newTraderKind) {
            if (newTraderKind.orbital) return;  // pendatic sanity check
        
            // Use the highest commonality
            fbTraderKind.commonality = Mathf.Max(fbTraderKind.commonality, newTraderKind.commonality);

            // *sigh*
            fbTraderKind.stockGenerators.AddRange( newTraderKind.stockGenerators );
            fbTraderKind.stockGenerators.RemoveDuplicates();
        }
    }
}
