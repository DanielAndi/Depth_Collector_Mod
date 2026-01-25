using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
    /// Patch to add UI gizmos when left-clicking on a Ledger faction settlement while a caravan is present.
    /// This provides quick access to debt management options directly from the settlement (not the caravan).
    /// </summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetGizmos))]
    public static class Patch_Settlement_GetGizmos
    {
        [HarmonyPostfix]
        public static void Postfix(Settlement __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__result == null)
                return;

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
                return;

            // Only add gizmos for Ledger faction settlements
            if (__instance.Faction != ledgerFaction)
                return;

            // Check if a player caravan is at this settlement
            Caravan caravanAtSettlement = GetCaravanAtSettlement(__instance);
            if (caravanAtSettlement == null)
                return;

            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp == null)
                return;

            DebtContract contract = worldComp.Contract;
            if (contract == null)
                return;

            // Convert to list to modify
            List<Gizmo> gizmos = __result.ToList();

            // Add debt management gizmos
            AddDebtGizmos(gizmos, worldComp, contract, caravanAtSettlement);

            __result = gizmos;
        }

        private static Caravan GetCaravanAtSettlement(Settlement settlement)
        {
            if (Find.WorldObjects == null)
                return null;

            // Check if any player caravan is at the settlement tile
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.Faction == Faction.OfPlayer && caravan.Tile == settlement.Tile)
                {
                    return caravan;
                }
            }

            return null;
        }

        internal static void AddDebtGizmos(List<Gizmo> gizmos, WorldComponent_DebtCollector worldComp, DebtContract contract, Caravan caravan)
        {
            // Add "View Ledger" gizmo (always available)
            gizmos.Add(new Command_Action
            {
                defaultLabel = "DC_Gizmo_ViewLedger".Translate(),
                defaultDesc = "DC_Gizmo_ViewLedger_Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/CallAid", false) ?? BaseContent.BadTex,
                action = () => Find.WindowStack.Add(new Dialog_DebtLedger(contract))
            });

            // Add "Request Loan" gizmo (if borrowing is allowed)
            if (contract.CanBorrow)
            {
                gizmos.Add(new Command_Action
                {
                    defaultLabel = "DC_Gizmo_RequestLoan".Translate(),
                    defaultDesc = "DC_Gizmo_RequestLoan_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Trade", false) ?? BaseContent.BadTex,
                    action = () => OpenLoanMenuForCaravan(worldComp, caravan)
                });
            }

            // Add "Pay Interest" gizmo (if applicable)
            if (contract.IsActive && contract.status != DebtStatus.Collections && contract.interestDemandSent)
            {
                int interestDue = contract.CurrentInterestDue;
                int caravanSilver = DC_Util.CountCaravanSilver(caravan);
                
                gizmos.Add(new Command_Action
                {
                    defaultLabel = "DC_Gizmo_PayInterest".Translate() + $" ({interestDue})",
                    defaultDesc = "DC_Gizmo_PayInterest_Desc".Translate() + $"\n\nCaravan silver: {caravanSilver}",
                    icon = ContentFinder<Texture2D>.Get("Things/Item/Resource/Silver/Silver_c", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (!worldComp.TryPayInterestFromCaravan(caravan, out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    }
                });
            }

            // Add "Pay Full Balance" gizmo (if there's active debt)
            if (contract.IsActive)
            {
                int totalOwed = contract.TotalOwed;
                int caravanSilver = DC_Util.CountCaravanSilver(caravan);
                
                gizmos.Add(new Command_Action
                {
                    defaultLabel = "DC_Gizmo_PayFull".Translate() + $" ({totalOwed})",
                    defaultDesc = "DC_Gizmo_PayFull_Desc".Translate() + $"\n\nCaravan silver: {caravanSilver}",
                    icon = ContentFinder<Texture2D>.Get("Things/Item/Resource/Silver/Silver_c", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (!worldComp.TryPayFullBalanceFromCaravan(caravan, out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    }
                });
            }

            // Add "Send Tribute" gizmo (when locked out)
            if (contract.status == DebtStatus.LockedOut)
            {
                int tributeRequired = contract.RequiredTribute;
                int caravanSilver = DC_Util.CountCaravanSilver(caravan);
                
                gizmos.Add(new Command_Action
                {
                    defaultLabel = "DC_Gizmo_SendTribute".Translate() + $" ({tributeRequired})",
                    defaultDesc = "DC_Gizmo_SendTribute_Desc".Translate() + $"\n\nCaravan silver: {caravanSilver}",
                    icon = ContentFinder<Texture2D>.Get("Things/Item/Resource/Silver/Silver_c", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (!worldComp.TrySendTributeFromCaravan(caravan, out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    }
                });
            }
        }

        private static void OpenLoanMenuForCaravan(WorldComponent_DebtCollector worldComp, Caravan caravan)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (int tier in DC_Constants.LOAN_TIERS)
            {
                int amount = tier; // Capture for closure
                string label = "DC_FloatMenu_LoanTier".Translate(amount);
                
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (!worldComp.TryRequestLoanToCaravan(caravan, amount, out string reason))
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

    /// <summary>
    /// Patch to add UI gizmos when selecting a player caravan that is at a Ledger settlement.
    /// This makes the lending options available from the caravan selection as well.
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Patch_Caravan_GetGizmos
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__result == null)
                return;

            // Only for player caravans
            if (__instance.Faction != Faction.OfPlayer)
                return;

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
                return;

            // Check if caravan is at a Ledger settlement
            Settlement ledgerSettlement = GetLedgerSettlementAtTile(__instance.Tile, ledgerFaction);
            if (ledgerSettlement == null)
                return;

            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp == null)
                return;

            DebtContract contract = worldComp.Contract;
            if (contract == null)
                return;

            // Convert to list to modify
            List<Gizmo> gizmos = __result.ToList();

            // Add debt management gizmos
            Patch_Settlement_GetGizmos.AddDebtGizmos(gizmos, worldComp, contract, __instance);

            __result = gizmos;
        }

        private static Settlement GetLedgerSettlementAtTile(int tile, Faction ledgerFaction)
        {
            if (Find.WorldObjects == null)
                return null;

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction && settlement.Tile == tile)
                {
                    return settlement;
                }
            }

            return null;
        }
    }
}