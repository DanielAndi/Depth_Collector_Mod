using LudeonTK;
using RimWorld;
using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Dev-mode debug actions for testing the debt system.
    /// Access via Dev Mode -> Debug Actions menu.
    /// </summary>
    public static class DevActions_DebtCollector
    {
        [DebugAction("Debt Collector", "Force Interest Due Now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceInterestDue()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null || !worldComp.Contract.IsActive)
            {
                Messages.Message("No active debt contract", MessageTypeDefOf.RejectInput);
                return;
            }

            worldComp.Contract.nextInterestDueTick = Find.TickManager.TicksGame;
            worldComp.Contract.interestDemandSent = false;
            Messages.Message("Interest due tick set to now", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("Debt Collector", "Force Collections State", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceCollections()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null || !worldComp.Contract.IsActive)
            {
                Messages.Message("No active debt contract", MessageTypeDefOf.RejectInput);
                return;
            }

            worldComp.Contract.TriggerCollections(Find.TickManager.TicksGame);
            Messages.Message("Forced collections state", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("Debt Collector", "Force Collections Raid Now", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceCollectionsRaid()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null)
            {
                Messages.Message("No debt contract found", MessageTypeDefOf.RejectInput);
                return;
            }

            // If not in collections, put them there first
            if (worldComp.Contract.status != DebtStatus.Collections)
            {
                if (!worldComp.Contract.IsActive)
                {
                    // Start a loan first
                    worldComp.Contract.StartLoan(1000, Find.TickManager.TicksGame);
                }
                worldComp.Contract.TriggerCollections(Find.TickManager.TicksGame);
            }

            // Set deadline to now
            worldComp.Contract.paymentDeadlineTick = Find.TickManager.TicksGame;
            Messages.Message("Collections raid will trigger on next tick", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("Debt Collector", "Grant 5000 Silver", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void GrantSilver()
        {
            Map map = DC_Util.GetPlayerHomeMap();
            if (map == null)
            {
                Messages.Message("No player home map found", MessageTypeDefOf.RejectInput);
                return;
            }

            DC_Util.SpawnSilver(map, 5000);
            Messages.Message("Spawned 5000 silver", MessageTypeDefOf.PositiveEvent);
        }

        [DebugAction("Debt Collector", "Reset Debt Contract", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ResetContract()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null)
            {
                Messages.Message("No debt contract found", MessageTypeDefOf.RejectInput);
                return;
            }

            worldComp.Contract.Reset();
            Messages.Message("Debt contract reset", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("Debt Collector", "Set Locked Out", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SetLockedOut()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null)
            {
                Messages.Message("No debt contract found", MessageTypeDefOf.RejectInput);
                return;
            }

            worldComp.Contract.lastLoanAmount = 1000;
            worldComp.Contract.SettleByForce();
            Messages.Message("Set to locked out state", MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("Debt Collector", "Log Debt Status", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogDebtStatus()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null)
            {
                Log.Message("[DebtCollector Debug] No debt contract found");
                return;
            }

            DebtContract c = worldComp.Contract;
            int currentTick = Find.TickManager.TicksGame;
            double accruedInterest = c.GetAccruedInterest(currentTick);
            int totalOwed = c.GetTotalOwed(currentTick);
            int missedFees = c.GetMissedFees(currentTick);
            
            Log.Message($"[DebtCollector Debug] Status: {c.status}");
            Log.Message($"[DebtCollector Debug] Principal: {c.principal}");
            Log.Message($"[DebtCollector Debug] Accrued Interest: {accruedInterest:F2}");
            Log.Message($"[DebtCollector Debug] Missed Fees: {missedFees}");
            Log.Message($"[DebtCollector Debug] Payments Made: {c.paymentsMade}");
            Log.Message($"[DebtCollector Debug] Total Owed: {totalOwed}");
            Log.Message($"[DebtCollector Debug] Missed Payments Count: {c.GetMissedPaymentsCount(currentTick)}");
            Log.Message($"[DebtCollector Debug] Interest Demand Sent: {c.interestDemandSent}");
            Log.Message($"[DebtCollector Debug] Next Interest Due: {c.nextInterestDueTick}");
            Log.Message($"[DebtCollector Debug] Payment Deadline: {c.paymentDeadlineTick}");
            Log.Message($"[DebtCollector Debug] Current Tick: {currentTick}");
            Log.Message($"[DebtCollector Debug] Loan Received Tick: {c.loanReceivedTick}");
            Log.Message($"[DebtCollector Debug] Elapsed Days: {c.GetElapsedDays(currentTick):F2}");
            Log.Message($"[DebtCollector Debug] Collections Raid Active: {c.collectionsRaidActive}");
            Log.Message($"[DebtCollector Debug] Settlement Placed: {worldComp.SettlementPlaced}");
            Log.Message($"[DebtCollector Debug] Settlement Tile: {worldComp.LedgerSettlementTile}");

            Faction ledger = DC_Util.GetLedgerFaction();
            Log.Message($"[DebtCollector Debug] Ledger Faction: {(ledger != null ? ledger.Name : "NOT FOUND")}");
        }

        [DebugAction("Debt Collector", "Force Place Settlement", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForcePlaceSettlement()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp == null)
            {
                Messages.Message("World component not found", MessageTypeDefOf.RejectInput);
                return;
            }

            // Reset the flag to allow re-placement
            // Note: This uses reflection since we'd need to expose a method for this
            worldComp.TryPlaceLedgerSettlement();
            Messages.Message($"Settlement placement attempted. Tile: {worldComp.LedgerSettlementTile}", 
                MessageTypeDefOf.NeutralEvent);
        }

        [DebugAction("Debt Collector", "Skip to Payment Deadline", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SkipToDeadline()
        {
            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp?.Contract == null || !worldComp.Contract.IsActive)
            {
                Messages.Message("No active debt contract", MessageTypeDefOf.RejectInput);
                return;
            }

            if (worldComp.Contract.paymentDeadlineTick > 0)
            {
                worldComp.Contract.paymentDeadlineTick = Find.TickManager.TicksGame;
                Messages.Message("Payment deadline set to now", MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                Messages.Message("No active payment deadline", MessageTypeDefOf.RejectInput);
            }
        }
    }
}
