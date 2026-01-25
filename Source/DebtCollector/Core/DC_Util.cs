using System.Collections.Generic;
using System.Linq;
using RimWorld;
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
    }
}
