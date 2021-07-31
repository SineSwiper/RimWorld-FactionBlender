using RimWorld;
using System;
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
            { "imperial trader",            "bulk goods trader"    },
            { "imperial bulk goods provisioner", "bulk goods trader" },
            { "general supplier",           "bulk goods trader"    },
            { "supply officer",             "bulk goods trader"    },

            { "war merchant",               "combat supplier"      },
            { "weapons dealer",             "combat supplier"      },
            { "dwarven smithy",             "combat supplier"      },
            { "military industrial trader", "combat supplier"      },
            { "imperial weapons provisioner", "combat supplier"    },
            { "armorsmith",                 "combat supplier"      },
            { "weaponsmith",                "combat supplier"      },

            { "slaver",                     "pirate merchant"      },
            { "pet dealer",                 "pirate merchant"      },
            { "smuggler",                   "pirate merchant"      },
            { "drug dealer",                "pirate merchant"      },
            { "pharmaceutical trader",      "pirate merchant"      },
            { "black market trader",        "pirate merchant"      },
            { "frog drug trader",           "pirate merchant"      },
            { "bulk slave trader",          "pirate merchant"      },
            { "imperial slaver",            "pirate merchant"      },
            { "loot trader",                "pirate merchant"      },
            { "booty trader",               "pirate merchant"      },
            { "sellswords",                 "pirate merchant"      },

            { "shaman merchant",            "exotic goods trader"  },
            { "archelogical expedition",    "exotic goods trader"  },
            { "art dealer",                 "exotic goods trader"  },
            { "arcane items collector",     "exotic goods trader"  },
            { "artifact dealer",            "exotic goods trader"  },
            { "artists troupe",             "exotic goods trader"  },
            { "art patron",                 "exotic goods trader"  },
            { "wine merchant",              "exotic goods trader"  },

            { "nomadic shepherd",           "farming goods trader" },
            { "agricultural trader",        "farming goods trader" },
            { "butcher",                    "farming goods trader" },
            { "livestock wranglers",        "farming goods trader" },
            { "animal herder",              "farming goods trader" },
            { "zoological trader",          "farming goods trader" },
            { "bulk dirt trader",           "farming goods trader" },

            { "pelt trader",                "fabric trader"        },
            { "textiles trader",            "fabric trader"        },
            { "herbal suppliers",           "medical goods trader" },
            { "surgical supplier",          "medical goods trader" },
            { "gnoll organ trader",         "medical goods trader" },
            
            { "masonary material trader",   "mining company"       },
            { "mining goods trader",        "mining company"       },
        };

        public static void InjectTraderKindDefsToFactions(List<FactionDef> FB_Factions) {
            /* Caravan Trader Kinds */
            
            foreach (FactionDef FB_Faction in FB_Factions.Where( fd => !fd.permanentEnemy )) {
                string shortDefName = FB_Faction.defName.Replace("FactionBlender", "FB");
                string tkdPrefix    = shortDefName + "_Caravan";

                List<TraderKindDef> FB_TraderKinds = FB_Faction.caravanTraderKinds;

                List<TraderKindDef> traderKindDefs = DefDatabase<FactionDef>.AllDefs.
                    Where     (f => !FB_Factions.Contains(f) && f.defName != "OutlanderCivil").
                    SelectMany(f => f.caravanTraderKinds).ToList()
                ;
                traderKindDefs.RemoveDuplicates();  // parent classes, etc.

                // Convert the current ones for name changes (like "slaver" → "pirate merchant"), and requestable flags
                for (int i = 0; i < FB_TraderKinds.Count; i++) {
                    TraderKindDef traderKind = FB_TraderKinds[i];

                    string curLabel = traderKind.label?.ToLower();
                    if (curLabel == null) continue;
                    string newLabel = newTraderLabel.ContainsKey(curLabel) ? newTraderLabel[curLabel] : null;
                    if (newLabel == null) continue;

                    FB_TraderKinds[i] = CopyTraderKindDef(traderKind, tkdPrefix, newLabel, newLabel);
                }

                // Add the rest, while merging where we find (label-like) dupes
                foreach (var traderKind in traderKindDefs.ToList()) {
                    string curLabel = traderKind.label?.ToLower();
                    string newLabel = curLabel != null && newTraderLabel.ContainsKey(curLabel) ? newTraderLabel[curLabel] : null;
                    int lm = FB_TraderKinds.FirstIndexOf(tkd =>
                       tkd.label?.ToLower() is string lcLabel && lcLabel != null && (lcLabel == curLabel || lcLabel == newLabel)
                    ); // returns Count on failure, not -1
            
                    // If we somehow missed a dupe, skip it
                    if (FB_Faction.caravanTraderKinds.Contains(traderKind)) continue;

                    // If we found a label-like dupe, merge them
                    else if (lm < FB_Faction.caravanTraderKinds.Count) {
                        var labelMatchTKD = FB_TraderKinds[lm];
                        if (!labelMatchTKD.defName.StartsWith("FB_Caravan_")) {
                            TraderKindDef newTraderKind = CopyTraderKindDef(labelMatchTKD, tkdPrefix, labelMatchTKD.LabelCap);
                            FB_Faction.caravanTraderKinds[lm] = newTraderKind;
                            labelMatchTKD = newTraderKind;
                        }
                        MergeTraderKindDefs(labelMatchTKD, traderKind);
                    }

                    // If we have a new one but it needs a new label, copy to a new one
                    else if (newLabel != null) {
                        TraderKindDef newTraderKind = CopyTraderKindDef(traderKind, tkdPrefix, newLabel, newLabel);
                        FB_Faction.caravanTraderKinds.Add(newTraderKind);
                    }

                    // Must be unique; add it
                    else FB_Faction.caravanTraderKinds.Add(traderKind);
                }

                /* Visitor Trader Kinds */

                // Add every visitor trader as a combined list
                FB_Faction.visitorTraderKinds.Clear();
                FB_Faction.visitorTraderKinds.AddRange(
                    DefDatabase<FactionDef>.AllDefs.Where(f => !FB_Factions.Contains(f)).SelectMany(f => f.visitorTraderKinds)
                );

                /* Base Trader Kinds */

                // The base starts with the outlander base StockGenerators, for the essentials
                var baseTraderKind = CopyTraderKindDef(DefDatabase<TraderKindDef>.GetNamed("Base_Outlander_Standard"), shortDefName, "Base Trade Standard");
                FB_Faction.baseTraderKinds[0] = baseTraderKind;
                baseTraderKind.requestable = false;

                List<TraderKindDef>   otherTraderKinds     = DefDatabase<FactionDef>.AllDefs.Where(f => !FB_Factions.Contains(f)).SelectMany(f => f.baseTraderKinds).ToList();
                List<StockGenerator>  otherStockGenerators = otherTraderKinds.SelectMany( tkd => tkd.stockGenerators ).ToList();
                otherStockGenerators.RemoveDuplicates();

                // Since we don't have access to edit the StockGenerators themselves (and properties like
                // countRange), figure out the average SG count of each faction and only add a random set
                // of twice as many StockGenerators.  Still higher than average, but not crazy rich.  The
                // list will change on restart.

                double avgBaseGeneratorCount = otherTraderKinds.Sum( tkd => tkd.stockGenerators.Count ) / otherTraderKinds.Count;
                int     targetGeneratorCount = (int)Math.Ceiling(avgBaseGeneratorCount * 2);

                // NOTE: Adding up the Outlander SGs too, this ends up being more like a 3:1 ratio, but
                // we also do some removals after the list addition.
            
                var newGeneratorList = otherStockGenerators.InRandomOrder().ToList();
                if (newGeneratorList.Count > targetGeneratorCount) newGeneratorList = newGeneratorList.GetRange(0, targetGeneratorCount);
                baseTraderKind.stockGenerators.AddRange( newGeneratorList );

                // Add all buyer generators
                baseTraderKind.stockGenerators.AddRange(
                    otherStockGenerators.Where( sg =>
                        sg is StockGenerator_BuyExpensiveSimple || sg is StockGenerator_BuySingleDef || sg is StockGenerator_BuySlaves || sg is StockGenerator_BuyTradeTag
                    )
                );
                baseTraderKind.stockGenerators.RemoveDuplicates();

                // Slave trading is against the civil coalition's ideology.  Remove any slaves from the base stockGenerators.
                // Only the pirate merchant gets away with it (in secret).  They will still buy slaves, however, since they prefer to
                // free them.
                if (shortDefName == "FB_Civil")
                    baseTraderKind.stockGenerators = baseTraderKind.stockGenerators.Where(sg => !(sg is StockGenerator_Slaves)).ToList();
            }
        }

        public static TraderKindDef CopyTraderKindDef(TraderKindDef origTraderKind, string defNamePrefix, string labelBase, string newLabel = null) {
            string newDefName = defNamePrefix + "_" + GenText.ToTitleCaseSmart(labelBase).Replace(" ", "_");

            // Construction
            var newTraderKind = new TraderKindDef {
                defName         = newDefName,
                label           = newLabel ?? origTraderKind.label,
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
