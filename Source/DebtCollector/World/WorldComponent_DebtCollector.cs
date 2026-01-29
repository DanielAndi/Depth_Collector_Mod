using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace DebtCollector
{
    /// <summary>
    /// Main world component that manages the debt contract and processes tick events.
    /// </summary>
    public class WorldComponent_DebtCollector : WorldComponent
    {
        // These flags are reset when a new world component is created (new game or load)
        private bool loggedPostLoadInit;
        private bool loggedSettlementFailure;
        private bool loggedSettlementRebuild;
        private bool loggedNoPlayerStartTile;
        private bool loggedFirstTick;

        private DebtContract contract;
        private bool settlementPlaced;
        private int ledgerSettlementTile = -1;

        // Tick interval for checking raid status (every ~1 in-game hour)
        private const int RAID_CHECK_INTERVAL = 2500;
        private int lastRaidCheckTick;

        // Tick interval for checking settlement placement (every ~60 in-game seconds)
        private const int SETTLEMENT_CHECK_INTERVAL = 1500; // 60 seconds
        private int lastSettlementCheckTick;

        public DebtContract Contract => contract;
        public bool SettlementPlaced => settlementPlaced;
        public int LedgerSettlementTile => ledgerSettlementTile;

        public WorldComponent_DebtCollector(World world) : base(world)
        {
            contract = new DebtContract();
            Log.Message("[DebtCollector] WorldComponent_DebtCollector created for new world.");
        }

        public static WorldComponent_DebtCollector Get()
        {
            return Find.World?.GetComponent<WorldComponent_DebtCollector>();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (contract == null || Find.TickManager == null)
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Log first tick to verify ticking is working
            if (!loggedFirstTick)
            {
                loggedFirstTick = true;
                Log.Message($"[DebtCollector] WorldComponentTick first run. settlementPlaced={settlementPlaced}, currentTick={currentTick}");
                
                // Immediately check on first tick - don't wait for interval
                if (!settlementPlaced)
                {
                    lastSettlementCheckTick = currentTick;
                    Log.Message($"[DebtCollector] First tick - immediately checking for settlement placement");
                    ResolveFactionAndSettlement("WorldComponentTick_First");
                }
            }

            // Periodically check if settlement needs to be placed (fallback for standard playthroughs)
            // Check every tick for the first 5 seconds (12500 ticks), then every 500 ticks for next 5 seconds, then normal interval
            int checkInterval;
            if (currentTick < 12500) // First 5 seconds - check every tick
            {
                checkInterval = 1;
            }
            else if (currentTick < 25000) // Next 5 seconds - check every 500 ticks
            {
                checkInterval = 500;
            }
            else // After 10 seconds - normal interval
            {
                checkInterval = SETTLEMENT_CHECK_INTERVAL;
            }
            
            if (!settlementPlaced && currentTick - lastSettlementCheckTick >= checkInterval)
            {
                lastSettlementCheckTick = currentTick;
                if (currentTick < 12500 || currentTick % 500 == 0) // Log frequently during first 5 seconds, then every 500 ticks
                {
                    Log.Message($"[DebtCollector] Periodic settlement check at tick {currentTick} (interval={checkInterval})");
                }
                ResolveFactionAndSettlement("WorldComponentTick");
            }

            // Check if contract is fully paid (safety check)
            if (contract.IsActive)
            {
                int totalOwed = contract.GetTotalOwed(currentTick);
                if (totalOwed <= 0)
                {
                    contract.PayFull(currentTick, 0); // Already paid, just update status
                    return;
                }
            }

            // Skip processing if no active debt
            if (!contract.IsActive && contract.status != DebtStatus.LockedOut)
                return;

            // Check for collections raid end
            if (contract.collectionsRaidActive)
            {
                if (currentTick - lastRaidCheckTick >= RAID_CHECK_INTERVAL)
                {
                    lastRaidCheckTick = currentTick;
                    CheckCollectionsRaidEnded(currentTick);
                }
                return; // Don't process other events during active raid
            }

            // Check if loan term has expired (only trigger once; then fall through to ProcessCollectionsDeadline)
            if (contract.IsActive && contract.IsLoanTermExpired(currentTick))
            {
                if (contract.status != DebtStatus.Collections)
                {
                    TriggerLoanTermExpiration(currentTick);
                    return; // Just triggered; deadline handling will run on subsequent ticks
                }
                // Already in Collections from loan expiry — fall through to process raid deadline
            }

            // Check if missed payments exceed grace limit (FR-012)
            if (contract.IsActive && contract.status != DebtStatus.Collections)
            {
                int missedCount = contract.GetMissedPaymentsCount(currentTick);
                int graceLimit = DebtCollectorMod.Settings?.graceMissedPayments ?? DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS;
                
                if (missedCount > graceLimit)
                {
                    TriggerGraceLimitExceeded(currentTick, missedCount, graceLimit);
                    return;
                }
            }

            // Process interest and collections deadlines
            if (contract.status == DebtStatus.Current || contract.status == DebtStatus.Delinquent)
            {
                ProcessInterestCycle(currentTick);
            }
            else if (contract.status == DebtStatus.Collections)
            {
                ProcessCollectionsDeadline(currentTick);
            }
        }

        private void ProcessInterestCycle(int currentTick)
        {
            // Time to send interest demand?
            if (!contract.interestDemandSent && currentTick >= contract.nextInterestDueTick)
            {
                SendInterestDemand(currentTick);
                return;
            }

            // Check if payment deadline passed
            bool deadlinePassed = contract.interestDemandSent && currentTick >= contract.paymentDeadlineTick;
            
            if (deadlinePassed)
            {
                // Payment deadline missed - record it and continue accruing interest
                // Raids only happen when the 30-day loan term expires, NOT when individual payments are late
                int missedCount = contract.GetMissedPaymentsCount(currentTick) + 1;
                int lateFees = contract.GetMissedFees(currentTick);
                float penaltyRate = (DebtCollectorMod.Settings?.latePenaltyRatePerDay ?? DC_Constants.DEFAULT_LATE_PENALTY_RATE_PER_DAY) * 100f;
                
                contract.RecordMissedPayment(currentTick);
                
                // Send letter about missed payment (not demanding full payment)
                DC_Util.SendLetter(
                    "DC_Letter_PaymentMissed_Title",
                    "DC_Letter_PaymentMissed_Text",
                    LetterDefOf.NegativeEvent,
                    null,
                    missedCount,
                    lateFees,
                    penaltyRate.ToString("F1")
                );
            }
            // Note: When already delinquent, interest continues to accrue automatically via GetAccruedInterest()
            // No need to call RecordMissedPayment repeatedly - missed payments are computed based on elapsed time
        }

        private void SendInterestDemand(int currentTick)
        {
            int interestDue = contract.GetCurrentInterestDue(currentTick);
            float windowHours = DebtCollectorMod.Settings?.interestPaymentWindowHours ?? 
                               DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS;

            DC_Util.SendLetter(
                "DC_Letter_InterestDue_Title",
                "DC_Letter_InterestDue_Text",
                LetterDefOf.NeutralEvent,
                null,
                interestDue
            );

            contract.SetInterestDemandSent(currentTick);
            
            Log.Message($"[DebtCollector] Interest demand sent: {interestDue} silver due within {windowHours} hours");
        }

        private void SendCollectionsNotice()
        {
            DC_Util.SendLetter(
                "DC_Letter_CollectionsNotice_Title",
                "DC_Letter_CollectionsNotice_Text",
                LetterDefOf.ThreatBig,
                null,
                contract.TotalOwed
            );
        }

        private void TriggerLoanTermExpiration(int currentTick)
        {
            // Loan term expired - require full payment immediately
            var settings = DebtCollectorMod.Settings;
            float deadlineHours = settings?.collectionsDeadlineHours ?? DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS;
            
            contract.TriggerCollections(currentTick);
            
            // Send letter about loan term expiration
            int totalOwed = contract.GetTotalOwed(currentTick);
            DC_Util.SendLetter(
                "DC_Letter_LoanTermExpired_Title",
                "DC_Letter_LoanTermExpired_Text",
                LetterDefOf.ThreatBig,
                null,
                totalOwed,
                contract.loanTermDays
            );
            
            Log.Message($"[DebtCollector] Loan term expired. Full payment of {totalOwed} required within {deadlineHours} hours.");
        }

        private void TriggerGraceLimitExceeded(int currentTick, int missedCount, int graceLimit)
        {
            // Grace limit exceeded - require full payment immediately (FR-012)
            var settings = DebtCollectorMod.Settings;
            float deadlineHours = settings?.collectionsDeadlineHours ?? DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS;
            
            contract.TriggerCollections(currentTick);
            
            // Send letter about grace limit exceeded
            int totalOwed = contract.GetTotalOwed(currentTick);
            DC_Util.SendLetter(
                "DC_Letter_GraceLimitExceeded_Title",
                "DC_Letter_GraceLimitExceeded_Text",
                LetterDefOf.ThreatBig,
                null,
                missedCount,
                graceLimit,
                totalOwed
            );
            
            Log.Message($"[DebtCollector] Grace limit exceeded ({missedCount} missed > {graceLimit} allowed). Full payment of {totalOwed} required within {deadlineHours} hours.");
        }

        private void ProcessCollectionsDeadline(int currentTick)
        {
            if (currentTick >= contract.paymentDeadlineTick)
            {
                // Deadline passed - trigger raid
                TriggerCollectionsRaid();
            }
        }

        private void TriggerCollectionsRaid()
        {
            Map targetMap = DC_Util.GetPlayerHomeMap();
            if (targetMap == null)
            {
                Log.Warning("[DebtCollector] No player home map found for collections raid");
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                Log.Warning("[DebtCollector] Ledger faction not found for collections raid");
                return;
            }

            // Ensure faction is hostile to player for the raid
            // Use TryAffectGoodwill to make faction hostile if not already
            if (!ledgerFaction.HostileTo(Faction.OfPlayer))
            {
                ledgerFaction.TryAffectGoodwillWith(Faction.OfPlayer, -100, canSendMessage: false, canSendHostilityLetter: false, null);
                Log.Message("[DebtCollector] Set Ledger faction to hostile for collections raid");
            }

            // Build incident parameters
            var parms = new IncidentParms
            {
                target = targetMap,
                faction = ledgerFaction,
                forced = true,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn
            };

            // Scale points based on debt and multiplier
            float multiplier = DebtCollectorMod.Settings?.raidStrengthMultiplier ?? 
                              DC_Constants.DEFAULT_RAID_STRENGTH_MULTIPLIER;
            int currentTick = Find.TickManager.TicksGame;
            parms.points = contract.GetTotalOwed(currentTick) * multiplier;
            parms.points = System.Math.Max(parms.points, 200f); // Minimum viable raid

            // Execute the raid incident
            if (DC_DefOf.DC_Incident_CollectionsRaid == null)
            {
                Log.Warning("[DebtCollector] Collections raid aborted: incident def not loaded.");
                return;
            }

            bool success = DC_DefOf.DC_Incident_CollectionsRaid.Worker.TryExecute(parms);
            
            if (success)
            {
                contract.StartCollectionsRaid(Find.TickManager.TicksGame, targetMap.uniqueID);
                lastRaidCheckTick = Find.TickManager.TicksGame;
            }
            else
            {
                Log.Warning("[DebtCollector] Failed to execute collections raid incident");
                // Fall back to settling by force anyway to prevent stuck state
                contract.SettleByForce();
                SendDebtSettledLetter();
            }
        }

        private void CheckCollectionsRaidEnded(int currentTick)
        {
            if (Find.Maps == null)
                return;

            // Don't check too soon after raid started
            if (currentTick - contract.collectionsRaidStartTick < DC_Constants.MIN_RAID_DURATION_TICKS)
                return;

            Map raidMap = null;
            foreach (Map map in Find.Maps)
            {
                if (map.uniqueID == contract.collectionsRaidMapId)
                {
                    raidMap = map;
                    break;
                }
            }

            if (raidMap == null)
            {
                // Map no longer exists (abandoned?) - settle debt
                Log.Message("[DebtCollector] Raid map no longer exists, settling debt");
                contract.SettleByForce();
                SendDebtSettledLetter();
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                contract.SettleByForce();
                SendDebtSettledLetter();
                return;
            }

            // Check if any Ledger pawns with assault/raid lords still exist on map
            bool raidActive = false;
            foreach (Lord lord in raidMap.lordManager.lords)
            {
                if (lord.faction == ledgerFaction && lord.ownedPawns.Count > 0)
                {
                    // Check if this is a hostile raid lord (assault, etc.)
                    if (lord.LordJob is LordJob_AssaultColony || 
                        lord.LordJob is LordJob_AssaultThings)
                    {
                        raidActive = true;
                        break;
                    }
                }
            }

            if (!raidActive)
            {
                // Also check for any hostile pawns from the faction on the map
                int hostilePawnCount = 0;
                foreach (Pawn pawn in raidMap.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.Faction == ledgerFaction && pawn.HostileTo(Faction.OfPlayer))
                    {
                        hostilePawnCount++;
                    }
                }

                if (hostilePawnCount == 0)
                {
                    Log.Message("[DebtCollector] Collections raid ended - no hostile Ledger pawns remain");
                    contract.SettleByForce();
                    SendDebtSettledLetter();
                }
            }
        }

        private void SendDebtSettledLetter()
        {
            DC_Util.SendLetter(
                "DC_Letter_DebtSettled_Title",
                "DC_Letter_DebtSettled_Text",
                LetterDefOf.NeutralEvent
            );
        }

        /// <summary>
        /// Called when player requests a new loan from colony (via comms console).
        /// </summary>
        public bool TryRequestLoan(int amount, out string failReason)
        {
            failReason = null;

            if (contract.status == DebtStatus.LockedOut)
            {
                failReason = "DC_Message_LockedOut".Translate();
                return false;
            }

            if (contract.IsActive)
            {
                failReason = "DC_Message_AlreadyHaveLoan".Translate();
                return false;
            }

            // Check max loan amount (FR-004)
            int maxLoan = DebtCollectorMod.Settings?.maxLoanAmount ?? DC_Constants.DEFAULT_MAX_LOAN_AMOUNT;
            if (maxLoan > 0 && amount > maxLoan)
            {
                failReason = "DC_Message_ExceedsMaxLoan".Translate(amount, maxLoan);
                return false;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                failReason = "DC_Message_NoLedgerFaction".Translate();
                return false;
            }

            // Start the loan
            contract.StartLoan(amount, Find.TickManager.TicksGame);

            // Give silver to caravan if at settlement, otherwise spawn on map
            LookTargets lookTarget = null;
            Caravan caravan = DC_Util.GetCaravanAtLedgerSettlement();
            if (caravan != null)
            {
                DC_Util.AddSilverToCaravan(caravan, amount);
                lookTarget = new LookTargets(caravan);
                Messages.Message("DC_Message_LoanReceivedCaravan".Translate(amount), caravan, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Map map = DC_Util.GetPlayerHomeMap();
                if (map != null)
                {
                    IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
                    DC_Util.SpawnSilver(map, amount);
                    lookTarget = new LookTargets(dropSpot, map);
                    Messages.Message("DC_Message_LoanReceived".Translate(amount), MessageTypeDefOf.PositiveEvent);
                }
            }

            // Calculate next interest for the letter
            float intervalDays = DebtCollectorMod.Settings?.interestIntervalDays ?? 
                                DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int currentTick = Find.TickManager.TicksGame;
            int interestAmount = contract.GetCurrentInterestDue(currentTick);

            DC_Util.SendLetter(
                "DC_Letter_LoanGranted_Title",
                "DC_Letter_LoanGranted_Text",
                LetterDefOf.PositiveEvent,
                lookTarget,
                amount,
                interestAmount,
                intervalDays
            );
            
            return true;
        }

        /// <summary>
        /// Called when player requests a new loan while caravan is at the Ledger settlement.
        /// Silver is delivered directly to the caravan's inventory.
        /// </summary>
        public bool TryRequestLoanToCaravan(Caravan caravan, int amount, out string failReason)
        {
            failReason = null;

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (contract.status == DebtStatus.LockedOut)
            {
                failReason = "DC_Message_LockedOut".Translate();
                return false;
            }

            if (contract.IsActive)
            {
                failReason = "DC_Message_AlreadyHaveLoan".Translate();
                return false;
            }

            // Check max loan amount (FR-004)
            int maxLoan = DebtCollectorMod.Settings?.maxLoanAmount ?? DC_Constants.DEFAULT_MAX_LOAN_AMOUNT;
            if (maxLoan > 0 && amount > maxLoan)
            {
                failReason = "DC_Message_ExceedsMaxLoan".Translate(amount, maxLoan);
                return false;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                failReason = "DC_Message_NoLedgerFaction".Translate();
                return false;
            }

            // Start the loan
            contract.StartLoan(amount, Find.TickManager.TicksGame);

            // Give silver directly to the caravan
            DC_Util.AddSilverToCaravan(caravan, amount);

            // Calculate next interest for the letter
            float intervalDays = DebtCollectorMod.Settings?.interestIntervalDays ?? 
                                DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int currentTick = Find.TickManager.TicksGame;
            int interestAmount = contract.GetCurrentInterestDue(currentTick);

            // Letter points to the caravan
            LookTargets lookTarget = new LookTargets(caravan);

            DC_Util.SendLetter(
                "DC_Letter_LoanGranted_Title",
                "DC_Letter_LoanGranted_Text",
                LetterDefOf.PositiveEvent,
                lookTarget,
                amount,
                interestAmount,
                intervalDays
            );

            Messages.Message("DC_Message_LoanReceivedCaravan".Translate(amount), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays interest from colony stockpiles.
        /// </summary>
        public bool TryPayInterest(out string failReason)
        {
            failReason = null;

            if (Find.TickManager == null)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (contract.status == DebtStatus.Collections)
            {
                failReason = "DC_Message_InCollections".Translate();
                return false;
            }

            int currentTick = Find.TickManager.TicksGame;
            int interestDue = contract.GetCurrentInterestDue(currentTick);
            int colonySilver = DC_Util.CountColonySilver();

            if (colonySilver < interestDue)
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(interestDue, colonySilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilver(interestDue))
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(interestDue, colonySilver);
                return false;
            }

            contract.PayInterest(currentTick, interestDue);
            Messages.Message("DC_Message_PaymentSuccess".Translate(interestDue), MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays interest from caravan inventory (at Ledger settlement).
        /// </summary>
        public bool TryPayInterestFromCaravan(Caravan caravan, out string failReason)
        {
            failReason = null;

            if (Find.TickManager == null)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (contract.status == DebtStatus.Collections)
            {
                failReason = "DC_Message_InCollections".Translate();
                return false;
            }

            int currentTick = Find.TickManager.TicksGame;
            int interestDue = contract.GetCurrentInterestDue(currentTick);
            int caravanSilver = DC_Util.CountCaravanSilver(caravan);

            if (caravanSilver < interestDue)
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(interestDue, caravanSilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilverFromCaravan(caravan, interestDue))
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(interestDue, caravanSilver);
                return false;
            }

            contract.PayInterest(currentTick, interestDue);
            Messages.Message("DC_Message_PaymentSuccess".Translate(interestDue), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays full balance from colony stockpiles.
        /// </summary>
        public bool TryPayFullBalance(out string failReason)
        {
            failReason = null;

            if (Find.TickManager == null)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int currentTick = Find.TickManager.TicksGame;
            int totalOwed = contract.GetTotalOwed(currentTick);
            int colonySilver = DC_Util.CountColonySilver();

            if (colonySilver < totalOwed)
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(totalOwed, colonySilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilver(totalOwed))
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(totalOwed, colonySilver);
                return false;
            }

            contract.PayFull(currentTick, totalOwed);
            Messages.Message("DC_Message_PaymentSuccess".Translate(totalOwed), MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays full balance from caravan inventory (at Ledger settlement).
        /// </summary>
        public bool TryPayFullBalanceFromCaravan(Caravan caravan, out string failReason)
        {
            failReason = null;

            if (Find.TickManager == null)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int currentTick = Find.TickManager.TicksGame;
            int totalOwed = contract.GetTotalOwed(currentTick);
            int caravanSilver = DC_Util.CountCaravanSilver(caravan);

            if (caravanSilver < totalOwed)
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(totalOwed, caravanSilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilverFromCaravan(caravan, totalOwed))
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(totalOwed, caravanSilver);
                return false;
            }

            contract.PayFull(currentTick, totalOwed);
            Messages.Message("DC_Message_PaymentSuccess".Translate(totalOwed), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player sends tribute to unlock borrowing from colony stockpiles.
        /// </summary>
        public bool TrySendTribute(out string failReason)
        {
            failReason = null;

            if (contract.status != DebtStatus.LockedOut)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int tributeRequired = contract.RequiredTribute;
            int colonySilver = DC_Util.CountColonySilver();

            if (colonySilver < tributeRequired)
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(tributeRequired, colonySilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilver(tributeRequired))
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(tributeRequired, colonySilver);
                return false;
            }

            contract.PayTribute();

            DC_Util.SendLetter(
                "DC_Letter_TributeSent_Title",
                "DC_Letter_TributeSent_Text",
                LetterDefOf.PositiveEvent,
                null,
                tributeRequired
            );
            
            return true;
        }

        /// <summary>
        /// Called when player sends tribute from caravan inventory (at Ledger settlement).
        /// </summary>
        public bool TrySendTributeFromCaravan(Caravan caravan, out string failReason)
        {
            failReason = null;

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (contract.status != DebtStatus.LockedOut)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int tributeRequired = contract.RequiredTribute;
            int caravanSilver = DC_Util.CountCaravanSilver(caravan);

            if (caravanSilver < tributeRequired)
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(tributeRequired, caravanSilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilverFromCaravan(caravan, tributeRequired))
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(tributeRequired, caravanSilver);
                return false;
            }

            contract.PayTribute();

            DC_Util.SendLetter(
                "DC_Letter_TributeSent_Title",
                "DC_Letter_TributeSent_Text",
                LetterDefOf.PositiveEvent,
                new LookTargets(caravan),
                tributeRequired
            );

            Messages.Message("DC_Message_TributeSent".Translate(tributeRequired), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Places a Ledger settlement near the player's starting tile.
        /// Called once per game during world finalization.
        /// </summary>
        public void TryPlaceLedgerSettlement()
        {
            if (settlementPlaced)
                return;

            if (Find.World == null || Find.WorldObjects == null)
            {
                if (!loggedSettlementFailure)
                {
                    loggedSettlementFailure = true;
                    Log.Warning("[DebtCollector] Cannot place settlement - World/WorldObjects not ready.");
                }
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                Log.Warning("[DebtCollector] Cannot place settlement - Ledger faction not found");
                return;
            }

            // Check if faction already has a settlement
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction)
                {
                    settlementPlaced = true;
                    ledgerSettlementTile = settlement.Tile;
                    Log.Message($"[DebtCollector] Ledger settlement already exists at tile {ledgerSettlementTile}");
                    return;
                }
            }

            // Find player start tile - try multiple sources
            int playerStartTile = -1;
            
            // First, check if there's a player settlement on the world map (most reliable)
            // Use FactionManager to find player faction safely
            Faction playerFaction = Find.FactionManager?.AllFactions?.FirstOrDefault(f => f.IsPlayer);
            if (playerFaction != null)
            {
                foreach (Settlement settlement in Find.WorldObjects.Settlements)
                {
                    if (settlement.Faction == playerFaction)
                    {
                        playerStartTile = settlement.Tile;
                        Log.Message($"[DebtCollector] Found player settlement at tile {playerStartTile}");
                        break;
                    }
                }
            }
            
            // If no player settlement, try GameInitData (only available during game init)
            if (playerStartTile < 0 && Find.GameInitData != null && Find.GameInitData.startingTile >= 0)
            {
                playerStartTile = Find.GameInitData.startingTile;
                Log.Message($"[DebtCollector] Using GameInitData.startingTile: {playerStartTile}");
            }

            // If still no valid tile, check if there are any player maps
            if (playerStartTile < 0 && Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map.IsPlayerHome && map.Tile >= 0)
                    {
                        playerStartTile = map.Tile;
                        Log.Message($"[DebtCollector] Found player home map at tile {playerStartTile}");
                        break;
                    }
                }
            }

            if (playerStartTile < 0)
            {
                // In standard playthroughs, player hasn't selected starting location yet
                // Only log once to avoid spam, then keep trying periodically
                if (!loggedNoPlayerStartTile)
                {
                    loggedNoPlayerStartTile = true;
                    int settlementCount = Find.WorldObjects?.Settlements?.Count() ?? 0;
                    int mapCount = Find.Maps?.Count ?? 0;
                    Log.Message($"[DebtCollector] Player starting location not found yet. Settlements: {settlementCount}, Maps: {mapCount}. Will retry periodically.");
                }
                return;
            }

            // Reset the flag once we have a valid tile
            loggedNoPlayerStartTile = false;

            // Find a suitable tile
            var settings = DebtCollectorMod.Settings;
            int minDist = settings?.minSettlementDistance ?? DC_Constants.DEFAULT_MIN_SETTLEMENT_DISTANCE;
            int maxDist = settings?.maxSettlementDistance ?? DC_Constants.DEFAULT_MAX_SETTLEMENT_DISTANCE;

            int? targetTile = FindSettlementTile(playerStartTile, minDist, maxDist);
            
            if (!targetTile.HasValue)
            {
                // Retry with wider range
                targetTile = FindSettlementTile(playerStartTile, 2, maxDist * 2);
            }

            if (!targetTile.HasValue)
            {
                Log.Warning("[DebtCollector] Could not find suitable tile for Ledger settlement");
                settlementPlaced = true; // Prevent retrying every tick
                return;
            }

            // Create the settlement
            Settlement newSettlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newSettlement.Tile = targetTile.Value;
            newSettlement.SetFaction(ledgerFaction);
            newSettlement.Name = SettlementNameGenerator.GenerateSettlementName(newSettlement);
            Find.WorldObjects.Add(newSettlement);

            settlementPlaced = true;
            ledgerSettlementTile = targetTile.Value;

            Log.Message($"[DebtCollector] Created Ledger settlement '{newSettlement.Name}' at tile {targetTile.Value}");
        }

        private int? FindSettlementTile(int centerTile, int minDist, int maxDist)
        {
            // Simple manual search for valid settlement tiles within distance range
            var candidates = new System.Collections.Generic.List<int>();
            int tilesCount = Find.WorldGrid.TilesCount;
            
            for (int i = 0; i < tilesCount; i++)
            {
                if (Find.World.Impassable(i))
                    continue;
                    
                float dist = Find.WorldGrid.ApproxDistanceInTiles(centerTile, i);
                if (dist >= minDist && dist <= maxDist)
                {
                    if (TileFinder.IsValidTileForNewSettlement(i))
                    {
                        candidates.Add(i);
                        // Limit candidates to avoid performance issues
                        if (candidates.Count >= 50)
                            break;
                    }
                }
            }

            if (candidates.Count > 0)
            {
                return candidates.RandomElement();
            }

            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contract, "contract");
            Scribe_Values.Look(ref settlementPlaced, "settlementPlaced", false);
            Scribe_Values.Look(ref ledgerSettlementTile, "ledgerSettlementTile", -1);
            Scribe_Values.Look(ref lastRaidCheckTick, "lastRaidCheckTick", 0);
            Scribe_Values.Look(ref lastSettlementCheckTick, "lastSettlementCheckTick", 0);

            // Ensure contract is never null after loading
            if (contract == null)
            {
                contract = new DebtContract();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!loggedPostLoadInit)
                {
                    loggedPostLoadInit = true;
                    Log.Message("[DebtCollector] WorldComponent.ExposeData PostLoadInit: scheduling settlement validation.");
                }

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    ResolveFactionAndSettlement("PostLoadInit");
                });
            }
        }

        public void ResolveFactionAndSettlement(string context)
        {
            if (Find.World == null || Find.WorldObjects == null || Find.FactionManager == null)
            {
                Log.Message($"[DebtCollector] {context}: World/WorldObjects/FactionManager not ready. World={Find.World != null}, WorldObjects={Find.WorldObjects != null}, FactionManager={Find.FactionManager != null}");
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                // More detailed diagnostics
                FactionDef def = DC_DefOf.DC_Faction_TheLedger;
                Log.Warning($"[DebtCollector] {context}: Ledger faction not found. DefOf.DC_Faction_TheLedger={def?.defName ?? "NULL"}. Total factions: {Find.FactionManager.AllFactions.Count()}");
                
                // List all factions for debugging
                foreach (Faction f in Find.FactionManager.AllFactions)
                {
                    Log.Message($"[DebtCollector] Found faction: {f.Name} (def={f.def?.defName})");
                }
                return;
            }

            Log.Message($"[DebtCollector] {context}: Ledger faction found: {ledgerFaction.Name}");

            // If flagged as placed but no settlement exists, allow re-placement
            bool foundSettlement = false;
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction)
                {
                    foundSettlement = true;
                    settlementPlaced = true;
                    ledgerSettlementTile = settlement.Tile;
                    break;
                }
            }

            if (!foundSettlement && settlementPlaced)
            {
                if (!loggedSettlementRebuild)
                {
                    loggedSettlementRebuild = true;
                    Log.Warning($"[DebtCollector] {context}: settlement flag set but none found. Re-attempting placement.");
                }
                settlementPlaced = false;
                ledgerSettlementTile = -1;
            }

            if (!settlementPlaced)
            {
                TryPlaceLedgerSettlement();
            }
        }
    }
}
