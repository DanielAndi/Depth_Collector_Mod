using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DebtCollector
{
    public static class DC_Util
    {
        /// <summary>
        /// Counts total silver in all player-owned stockpiles on all maps.
        /// </summary>
        public static int CountColonySilver()
        {
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    total += CountSilverOnMap(map);
                }
            }
            return total;
        }

        /// <summary>
        /// Counts silver on a specific map in stockpiles.
        /// </summary>
        public static int CountSilverOnMap(Map map)
        {
            if (map == null) return 0;
            
            List<Thing> silverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            int total = 0;
            foreach (Thing t in silverThings)
            {
                if (t.IsInAnyStorage())
                {
                    total += t.stackCount;
                }
            }
            return total;
        }

        /// <summary>
        /// Attempts to remove silver from player stockpiles. Returns true if successful.
        /// </summary>
        public static bool TryRemoveSilver(int amount)
        {
            if (CountColonySilver() < amount)
                return false;

            int remaining = amount;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome || remaining <= 0)
                    continue;

                List<Thing> silverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver)
                    .Where(t => t.IsInAnyStorage())
                    .OrderByDescending(t => t.stackCount)
                    .ToList();

                foreach (Thing silver in silverThings)
                {
                    if (remaining <= 0) break;

                    int toRemove = System.Math.Min(silver.stackCount, remaining);
                    silver.SplitOff(toRemove).Destroy();
                    remaining -= toRemove;
                }
            }

            return remaining <= 0;
        }

        /// <summary>
        /// Spawns silver at a drop spot on the map (for loan disbursement).
        /// </summary>
        public static void SpawnSilver(Map map, int amount)
        {
            if (map == null || amount <= 0) return;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = amount;

            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            GenPlace.TryPlaceThing(silver, dropSpot, map, ThingPlaceMode.Near);
        }

        /// <summary>
        /// Converts days to ticks.
        /// </summary>
        public static int TicksFromDays(float days)
        {
            return (int)(days * DC_Constants.TICKS_PER_DAY);
        }

        /// <summary>
        /// Converts hours to ticks.
        /// </summary>
        public static int TicksFromHours(float hours)
        {
            return (int)(hours * DC_Constants.TICKS_PER_HOUR);
        }

        /// <summary>
        /// Gets the tick at end of current day (hour 24 / 0:00 next day).
        /// </summary>
        public static int EndOfDayTick(int currentTick)
        {
            int ticksIntoDay = currentTick % DC_Constants.TICKS_PER_DAY;
            return currentTick + (DC_Constants.TICKS_PER_DAY - ticksIntoDay);
        }

        /// <summary>
        /// Sends a letter using the vanilla letter stack.
        /// </summary>
        public static void SendLetter(string labelKey, string textKey, LetterDef letterDef, 
            LookTargets targets = null, params NamedArgument[] args)
        {
            string label = labelKey.Translate();
            string text = textKey.Translate(args);
            
            Find.LetterStack.ReceiveLetter(label, text, letterDef, targets);
        }

        /// <summary>
        /// Finds The Ledger faction, or null if not found.
        /// </summary>
        public static Faction GetLedgerFaction()
        {
            if (Find.FactionManager == null)
                return null;

            FactionDef def = DC_DefOf.DC_Faction_TheLedger;
            if (def == null)
                return null;

            return Find.FactionManager.FirstFactionOfDef(def);
        }

        /// <summary>
        /// Checks if any comms console is powered on any player map.
        /// </summary>
        public static bool HasPoweredCommsConsole()
        {
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building.def == ThingDefOf.CommsConsole)
                    {
                        CompPowerTrader power = building.TryGetComp<CompPowerTrader>();
                        if (power == null || power.PowerOn)
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the first player home map, or null if none exists.
        /// </summary>
        public static Map GetPlayerHomeMap()
        {
            return Find.Maps?.FirstOrDefault(m => m.IsPlayerHome);
        }

        /// <summary>
        /// Formats ticks as human-readable time (e.g., "2 days, 3 hours").
        /// </summary>
        public static string FormatTicksAsTime(int ticks)
        {
            if (ticks <= 0) return "now";

            int days = ticks / DC_Constants.TICKS_PER_DAY;
            int remainingTicks = ticks % DC_Constants.TICKS_PER_DAY;
            int hours = remainingTicks / DC_Constants.TICKS_PER_HOUR;

            if (days > 0 && hours > 0)
                return $"{days} day(s), {hours} hour(s)";
            if (days > 0)
                return $"{days} day(s)";
            if (hours > 0)
                return $"{hours} hour(s)";
            
            return "less than an hour";
        }

        /// <summary>
        /// Calculates interest based on principal and settings rate.
        /// </summary>
        public static int CalculateInterest(int principal)
        {
            var settings = DebtCollectorMod.Settings;
            float rate = settings?.interestRate ?? DC_Constants.DEFAULT_INTEREST_RATE;
            return (int)(principal * (rate / 100f));
        }

        /// <summary>
        /// Gets the first player caravan at a Ledger settlement, or null if none exists.
        /// </summary>
        public static Caravan GetCaravanAtLedgerSettlement()
        {
            if (Find.WorldObjects == null)
                return null;

            Faction ledgerFaction = GetLedgerFaction();
            if (ledgerFaction == null)
                return null;

            // Find Ledger settlement
            Settlement ledgerSettlement = null;
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction)
                {
                    ledgerSettlement = settlement;
                    break;
                }
            }

            if (ledgerSettlement == null)
                return null;

            // Find player caravan at that settlement
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.Faction == Faction.OfPlayer && caravan.Tile == ledgerSettlement.Tile)
                {
                    return caravan;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds silver to a caravan inventory.
        /// </summary>
        public static void AddSilverToCaravan(Caravan caravan, int amount)
        {
            if (caravan == null || amount <= 0) return;

            // Add silver to caravan - distribute across pawns that can carry items
            int remaining = amount;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (remaining <= 0) break;
                if (pawn.inventory == null) continue;

                // Try to add to existing silver stack in this pawn's inventory
                Thing existingSilver = pawn.inventory.innerContainer.FirstOrDefault(t => t.def == ThingDefOf.Silver);
                if (existingSilver != null && existingSilver.stackCount < existingSilver.def.stackLimit)
                {
                    int spaceAvailable = existingSilver.def.stackLimit - existingSilver.stackCount;
                    int toAdd = System.Math.Min(remaining, spaceAvailable);
                    existingSilver.stackCount += toAdd;
                    remaining -= toAdd;
                }

                // Add remaining silver as new stack(s) to this pawn
                while (remaining > 0)
                {
                    Thing newSilver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    int stackSize = System.Math.Min(remaining, ThingDefOf.Silver.stackLimit);
                    newSilver.stackCount = stackSize;
                    
                    if (pawn.inventory.innerContainer.TryAdd(newSilver))
                    {
                        remaining -= stackSize;
                    }
                    else
                    {
                        // Can't add more to this pawn, try next
                        break;
                    }
                }
            }

            // If there's still remaining silver and we couldn't add it all, log a warning
            if (remaining > 0)
            {
                Log.Warning($"[DebtCollector] Could not add all silver to caravan. {remaining} silver remaining.");
            }
        }

        /// <summary>
        /// Counts total silver in a caravan's inventory.
        /// </summary>
        public static int CountCaravanSilver(Caravan caravan)
        {
            if (caravan == null) return 0;

            int total = 0;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn.inventory == null) continue;
                
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    if (thing.def == ThingDefOf.Silver)
                    {
                        total += thing.stackCount;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// Attempts to remove silver from a caravan's inventory. Returns true if successful.
        /// </summary>
        public static bool TryRemoveSilverFromCaravan(Caravan caravan, int amount)
        {
            if (caravan == null || amount <= 0) return false;
            if (CountCaravanSilver(caravan) < amount) return false;

            int remaining = amount;
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (remaining <= 0) break;
                if (pawn.inventory == null) continue;

                // Get all silver in this pawn's inventory
                List<Thing> silverThings = pawn.inventory.innerContainer
                    .Where(t => t.def == ThingDefOf.Silver)
                    .OrderByDescending(t => t.stackCount)
                    .ToList();

                foreach (Thing silver in silverThings)
                {
                    if (remaining <= 0) break;

                    int toRemove = System.Math.Min(silver.stackCount, remaining);
                    silver.SplitOff(toRemove).Destroy();
                    remaining -= toRemove;
                }
            }

            return remaining <= 0;
        }

        /// <summary>
        /// Gets the Ledger settlement world object, or null if not found.
        /// </summary>
        public static Settlement GetLedgerSettlement()
        {
            if (Find.WorldObjects == null)
                return null;

            Faction ledgerFaction = GetLedgerFaction();
            if (ledgerFaction == null)
                return null;

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction)
                {
                    return settlement;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets available loan tiers based on the maxLoanAmount setting.
        /// Filters base tiers and adds extended tiers if maxLoanAmount allows.
        /// </summary>
        public static List<int> GetAvailableLoanTiers()
        {
            var settings = DebtCollectorMod.Settings;
            int maxLoanAmount = settings?.maxLoanAmount ?? DC_Constants.DEFAULT_MAX_LOAN_AMOUNT;
            
            // Start with base tiers
            List<int> tiers = new List<int>(DC_Constants.LOAN_TIERS);
            
            // If maxLoanAmount is set and less than highest base tier, filter
            if (maxLoanAmount > 0 && maxLoanAmount < 5000)
            {
                tiers = tiers.Where(t => t <= maxLoanAmount).ToList();
            }
            // If maxLoanAmount is greater than highest base tier, add extended tiers
            else if (maxLoanAmount > 5000)
            {
                // Add extended tiers: 10k, 15k, 20k, 25k, 30k, 40k, 50k, etc. up to maxLoanAmount
                int[] extendedTiers = { 10000, 15000, 20000, 25000, 30000, 40000, 50000 };
                foreach (int tier in extendedTiers)
                {
                    if (tier <= maxLoanAmount && !tiers.Contains(tier))
                    {
                        tiers.Add(tier);
                    }
                }
            }
            // If maxLoanAmount is 0 (unlimited), add extended tiers up to a reasonable cap
            else if (maxLoanAmount == 0)
            {
                int[] extendedTiers = { 10000, 15000, 20000, 25000, 30000, 40000, 50000 };
                foreach (int tier in extendedTiers)
                {
                    if (!tiers.Contains(tier))
                    {
                        tiers.Add(tier);
                    }
                }
            }
            
            // Sort and return
            tiers.Sort();
            return tiers;
        }
    }
}
