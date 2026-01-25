using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
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

    /// <summary>
    /// Patch to allow comms console calls to The Ledger faction even when leader is unavailable.
    /// This bypasses the leader availability check for The Ledger faction by replacing
    /// disabled options with enabled ones that open the debt ledger dialog.
    /// </summary>
    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
    public static class Patch_Building_CommsConsole_GetFloatMenuOptions
    {
        [HarmonyPostfix]
        public static void Postfix(ref IEnumerable<FloatMenuOption> __result)
        {
            if (__result == null)
                return;

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
                return;

            // Convert to list to modify
            List<FloatMenuOption> options = __result.ToList();
            bool modified = false;

            // Find and replace any disabled Ledger faction options
            for (int i = 0; i < options.Count; i++)
            {
                FloatMenuOption option = options[i];
                
                // Check if this option is for The Ledger faction
                if (option.Label.Contains(ledgerFaction.Name) || 
                    option.Label.Contains("The Ledger"))
                {
                    // Check if it's disabled due to leader unavailability
                    // The disabled reason is typically in the label text
                    if (option.Disabled && 
                        (option.Label.Contains("leader is unavailable") ||
                         option.Label.Contains("leader unavailable") ||
                         option.Label.Contains("unavailable")))
                    {
                        // Replace with an enabled option that opens the debt ledger
                        options[i] = new FloatMenuOption(
                            "Call " + ledgerFaction.Name,
                            () => OpenLedgerCommsDialog(ledgerFaction),
                            MenuOptionPriority.Default,
                            null,
                            null,
                            0f,
                            null,
                            null
                        );
                        modified = true;
                    }
                }
            }

            if (modified)
            {
                __result = options;
            }
        }

        private static void OpenLedgerCommsDialog(Faction faction)
        {
            // Open the debt ledger dialog instead of the standard comms dialog
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp != null)
            {
                Find.WindowStack.Add(new Dialog_DebtLedger(worldComp.Contract));
            }
            else
            {
                Messages.Message("DC_Message_NoLedgerFaction".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
    }

    /// <summary>
    /// Patch to add lending options when visiting a Ledger faction settlement.
    /// This allows players to access debt management options directly from the settlement.
    /// </summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetFloatMenuOptions))]
    public static class Patch_Settlement_GetFloatMenuOptions
    {
        [HarmonyPostfix]
        public static void Postfix(Settlement __instance, Caravan caravan, ref IEnumerable<FloatMenuOption> __result)
        {
            if (__result == null || caravan == null)
                return;

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
                return;

            // Only add options for Ledger faction settlements
            if (__instance.Faction != ledgerFaction)
                return;

            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp == null)
                return;

            DebtContract contract = worldComp.Contract;
            if (contract == null)
                return;

            // Convert to list to modify
            List<FloatMenuOption> options = __result.ToList();

            // Add "View Ledger" option (always available)
            options.Add(new FloatMenuOption(
                "DC_Settlement_ViewLedger".Translate(),
                () => Find.WindowStack.Add(new Dialog_DebtLedger(contract)),
                MenuOptionPriority.Default,
                null,
                null,
                0f,
                null,
                null
            ));

            // Add "Request Loan" option (if borrowing is allowed)
            if (contract.CanBorrow)
            {
                options.Add(new FloatMenuOption(
                    "DC_Settlement_RequestLoan".Translate(),
                    () => OpenLoanMenuAtSettlement(worldComp),
                    MenuOptionPriority.Default,
                    null,
                    null,
                    0f,
                    null,
                    null
                ));
            }

            // Add "Pay Interest" option (if applicable)
            if (contract.IsActive && contract.status != DebtStatus.Collections && contract.interestDemandSent)
            {
                int interestDue = contract.CurrentInterestDue;
                options.Add(new FloatMenuOption(
                    "DC_Settlement_PayInterest".Translate() + $" ({interestDue})",
                    () =>
                    {
                        if (!worldComp.TryPayInterest(out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    },
                    MenuOptionPriority.Default,
                    null,
                    null,
                    0f,
                    null,
                    null
                ));
            }

            // Add "Pay Full Balance" option (if there's active debt)
            if (contract.IsActive)
            {
                int totalOwed = contract.TotalOwed;
                options.Add(new FloatMenuOption(
                    "DC_Settlement_PayFull".Translate() + $" ({totalOwed})",
                    () =>
                    {
                        if (!worldComp.TryPayFullBalance(out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    },
                    MenuOptionPriority.Default,
                    null,
                    null,
                    0f,
                    null,
                    null
                ));
            }

            // Add "Send Tribute" option (when locked out)
            if (contract.status == DebtStatus.LockedOut)
            {
                int tributeRequired = contract.RequiredTribute;
                options.Add(new FloatMenuOption(
                    "DC_Settlement_SendTribute".Translate() + $" ({tributeRequired})",
                    () =>
                    {
                        if (!worldComp.TrySendTribute(out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    },
                    MenuOptionPriority.Default,
                    null,
                    null,
                    0f,
                    null,
                    null
                ));
            }

            __result = options;
        }

        private static void OpenLoanMenuAtSettlement(WorldComponent_DebtCollector worldComp)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (int tier in DC_Constants.LOAN_TIERS)
            {
                int amount = tier; // Capture for closure
                string label = "DC_FloatMenu_LoanTier".Translate(amount);
                
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (!worldComp.TryRequestLoan(amount, out string reason))
                    {
                        Messages.Message(reason, MessageTypeDefOf.RejectInput);
                    }
                }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }
}