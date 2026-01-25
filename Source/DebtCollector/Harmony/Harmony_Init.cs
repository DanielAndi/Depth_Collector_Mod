using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Harmony patch to ensure Ledger settlement is placed after world generation.
    /// This runs after the game has initialized the world and all factions.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.InitNewGame))]
    public static class Patch_Game_InitNewGame
    {
        private static bool loggedInitNewGame;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Find.World == null)
                return;

            // Ensure Ledger settlement is placed when starting a new game
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp != null && !worldComp.SettlementPlaced)
            {
                if (!loggedInitNewGame)
                {
                    loggedInitNewGame = true;
                    Log.Message("[DebtCollector] InitNewGame postfix: ensuring Ledger settlement exists.");
                }
                worldComp.ResolveFactionAndSettlement("InitNewGame");
            }
        }
    }

    /// <summary>
    /// Patch to ensure settlement exists when loading a save that didn't have one.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public static class Patch_Game_LoadGame
    {
        private static bool loggedLoadGame;

        [HarmonyPostfix]
        public static void Postfix()
        {
            // Delayed check to ensure world is fully loaded
            LongEventHandler.QueueLongEvent(() =>
            {
                if (Find.World == null)
                    return;

                WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
                if (worldComp != null && !worldComp.SettlementPlaced)
                {
                    if (!loggedLoadGame)
                    {
                        loggedLoadGame = true;
                        Log.Message("[DebtCollector] LoadGame postfix: ensuring Ledger settlement exists.");
                    }
                    worldComp.ResolveFactionAndSettlement("LoadGame");
                }
            }, "DebtCollector_CheckSettlement", false, null);
        }
    }
}
