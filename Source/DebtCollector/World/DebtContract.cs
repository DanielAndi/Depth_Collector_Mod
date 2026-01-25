using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Represents the player's debt contract with The Ledger.
    /// Handles serialization for save/load.
    /// </summary>
    public class DebtContract : IExposable
    {
        public int principal;           // Original borrowed amount (never changes, used for interest calculation)
        public int originalPrincipal;   // Original loan amount (kept for backward compatibility)
        public int paymentsMade;       // Cumulative silver paid toward this contract (interest or principal)
        public int nextInterestDueTick; // Tick when next interest checkpoint is due
        public int paymentDeadlineTick; // Deadline for current payment (interest or full)
        public int lastPaymentTick;     // Tick when last payment was made (for calculating missed payments)
        public int firstMissedPaymentTick; // Tick when payment deadline was first missed (for calculating periods)
        public DebtStatus status;       // Current contract status
        public bool interestDemandSent; // Whether we sent the interest demand letter
        public int lastLoanAmount;      // Used for tribute calculation after lockout
        public int loanTermDays;        // Loan term in days (must be paid off within this time)
        public int loanStartTick;       // Tick when loan was started (legacy name)
        public int loanReceivedTick;    // Tick when loan was received (primary field)
        
        // Raid tracking
        public bool collectionsRaidActive;
        public int collectionsRaidStartTick;
        public int collectionsRaidMapId;
        
        // Debug logging throttling (only log once per in-game hour in dev mode)
        private static int lastDebugLogTick = 0;
        private const int DEBUG_LOG_INTERVAL = 2500; // Once per in-game hour

        public DebtContract()
        {
            Reset();
        }

        /// <summary>
        /// Gets elapsed days since loan was received.
        /// </summary>
        public float GetElapsedDays(int currentTick)
        {
            int loanTick = loanReceivedTick > 0 ? loanReceivedTick : loanStartTick;
            if (loanTick <= 0 || currentTick < loanTick)
                return 0f;
            return (currentTick - loanTick) / 60000f; // RimWorld ticks per day = 60,000
        }

        /// <summary>
        /// Gets accrued interest based on continuous daily calculation.
        /// Includes penalty interest when delinquent.
        /// </summary>
        public double GetAccruedInterest(int currentTick)
        {
            if (principal <= 0)
                return 0.0;
            
            var settings = DebtCollectorMod.Settings;
            float interestRatePerDay = settings?.interestRatePerDay ?? DC_Constants.DEFAULT_INTEREST_RATE_PER_DAY;
            float elapsedDays = GetElapsedDays(currentTick);
            
            double baseInterest = principal * interestRatePerDay * elapsedDays;
            double penaltyInterest = GetPenaltyInterest(currentTick);
            
            return baseInterest + penaltyInterest;
        }

        /// <summary>
        /// Gets penalty interest accrued since first missed payment.
        /// Only applies when delinquent.
        /// </summary>
        public double GetPenaltyInterest(int currentTick)
        {
            if (principal <= 0 || firstMissedPaymentTick <= 0)
                return 0.0;
            
            // Only apply penalty if we're delinquent (missed at least one payment)
            if (status != DebtStatus.Delinquent && status != DebtStatus.Collections)
                return 0.0;
            
            var settings = DebtCollectorMod.Settings;
            float penaltyRatePerDay = settings?.latePenaltyRatePerDay ?? DC_Constants.DEFAULT_LATE_PENALTY_RATE_PER_DAY;
            
            // Calculate days since first missed payment
            int ticksSinceMissed = currentTick - firstMissedPaymentTick;
            if (ticksSinceMissed <= 0)
                return 0.0;
            
            float daysSinceMissed = ticksSinceMissed / 60000f;
            
            return principal * penaltyRatePerDay * daysSinceMissed;
        }

        /// <summary>
        /// Gets the number of missed payment periods based on days since last payment.
        /// </summary>
        public int GetMissedPaymentsCount(int currentTick)
        {
            if (lastPaymentTick <= 0)
                return 0;
            
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int intervalTicks = DC_Util.TicksFromDays(intervalDays);
            
            int ticksSinceLastPayment = currentTick - lastPaymentTick;
            if (ticksSinceLastPayment <= 0)
                return 0;
            
            // Calculate how many complete payment intervals have passed
            return System.Math.Max(0, ticksSinceLastPayment / intervalTicks);
        }

        /// <summary>
        /// Gets missed payment fees (missedPaymentsCount * missedPaymentFee).
        /// </summary>
        public int GetMissedFees(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            int fee = settings?.missedPaymentFee ?? DC_Constants.DEFAULT_MISSED_PAYMENT_FEE;
            return GetMissedPaymentsCount(currentTick) * fee;
        }

        /// <summary>
        /// Total amount owed: principal + accruedInterest + missedFees - paymentsMade.
        /// Never returns negative.
        /// </summary>
        public int GetTotalOwed(int currentTick)
        {
            double accruedInterestRaw = GetAccruedInterest(currentTick);
            int missedFees = GetMissedFees(currentTick);
            double totalOwedRaw = principal + accruedInterestRaw + missedFees - paymentsMade;
            int totalOwed = System.Math.Max(0, (int)System.Math.Ceiling(totalOwedRaw));
            
            // Dev mode logging (throttled to once per in-game hour to reduce spam)
            if (Prefs.DevMode && (currentTick - lastDebugLogTick >= DEBUG_LOG_INTERVAL || lastDebugLogTick == 0))
            {
                lastDebugLogTick = currentTick;
                float elapsedDays = GetElapsedDays(currentTick);
                int missedCount = GetMissedPaymentsCount(currentTick);
                Log.Message($"[DC] Owed={totalOwed} Principal={principal} AccruedInt={accruedInterestRaw:F2} Fees={missedFees} Missed={missedCount} Paid={paymentsMade} Days={elapsedDays:F2}");
            }
            
            return totalOwed;
        }

        /// <summary>
        /// Legacy property for backward compatibility - uses current tick.
        /// </summary>
        public int TotalOwed
        {
            get
            {
                int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                return GetTotalOwed(currentTick);
            }
        }

        /// <summary>
        /// Current interest payment due at checkpoint (based on continuous interest since last checkpoint).
        /// </summary>
        public int GetCurrentInterestDue(int currentTick)
        {
            // Interest portion due = accrued interest - payments made (which apply to interest first)
            double accruedInterestNow = GetAccruedInterest(currentTick);
            int missedFees = GetMissedFees(currentTick);
            
            // Payments apply to interest/fees first, then principal
            double interestAndFeesOwed = accruedInterestNow + missedFees;
            double interestPortionDue = System.Math.Max(0, interestAndFeesOwed - paymentsMade);
            
            return System.Math.Min(GetTotalOwed(currentTick), (int)System.Math.Ceiling(interestPortionDue));
        }

        /// <summary>
        /// Legacy property for backward compatibility.
        /// </summary>
        public int CurrentInterestDue
        {
            get
            {
                int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                return GetCurrentInterestDue(currentTick);
            }
        }

        /// <summary>
        /// Applies a payment amount, incrementing paymentsMade and updating lastPaymentTick.
        /// </summary>
        public void ApplyPayment(int amount, int currentTick)
        {
            if (amount <= 0)
                return;
            paymentsMade = System.Math.Max(0, paymentsMade + amount);
            lastPaymentTick = currentTick;
        }

        /// <summary>
        /// Whether the contract is active (has outstanding debt).
        /// </summary>
        public bool IsActive => status == DebtStatus.Current || 
                                status == DebtStatus.Delinquent || 
                                status == DebtStatus.Collections;

        /// <summary>
        /// Whether borrowing is currently allowed.
        /// </summary>
        public bool CanBorrow => status == DebtStatus.None;

        /// <summary>
        /// Required tribute to unlock borrowing after forced collection.
        /// </summary>
        public int RequiredTribute
        {
            get
            {
                var settings = DebtCollectorMod.Settings;
                float multiplier = settings?.tributeMultiplier ?? DC_Constants.DEFAULT_TRIBUTE_MULTIPLIER;
                return (int)(lastLoanAmount * multiplier);
            }
        }

        /// <summary>
        /// Resets the contract to initial state (no debt).
        /// </summary>
        public void Reset()
        {
            principal = 0;
            originalPrincipal = 0;
            paymentsMade = 0;
            nextInterestDueTick = 0;
            paymentDeadlineTick = 0;
            lastPaymentTick = 0;
            firstMissedPaymentTick = 0;
            status = DebtStatus.None;
            interestDemandSent = false;
            collectionsRaidActive = false;
            collectionsRaidStartTick = 0;
            collectionsRaidMapId = -1;
            loanTermDays = 0;
            loanStartTick = 0;
            loanReceivedTick = 0;
        }

        /// <summary>
        /// Initiates a new loan.
        /// </summary>
        public void StartLoan(int amount, int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int loanTerm = settings?.loanTermDays ?? DC_Constants.DEFAULT_LOAN_TERM_DAYS;

            principal = amount;
            originalPrincipal = amount;
            paymentsMade = 0;
            nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            paymentDeadlineTick = 0;
            lastPaymentTick = currentTick; // Initialize to loan start (no payments yet, but sets baseline)
            firstMissedPaymentTick = 0;
            status = DebtStatus.Current;
            interestDemandSent = false;
            lastLoanAmount = amount;
            collectionsRaidActive = false;
            loanTermDays = loanTerm;
            loanStartTick = currentTick;
            loanReceivedTick = currentTick;

            Log.Message($"[DebtCollector] Loan started: {amount} silver. Term: {loanTerm} days. First interest checkpoint at tick {nextInterestDueTick}");
        }

        /// <summary>
        /// Marks that interest demand was sent and sets the payment deadline.
        /// </summary>
        public void SetInterestDemandSent(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float windowHours = settings?.interestPaymentWindowHours ?? DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS;
            
            interestDemandSent = true;
            paymentDeadlineTick = currentTick + DC_Util.TicksFromHours(windowHours);
        }

        /// <summary>
        /// Processes a successful interest payment at a checkpoint.
        /// Payment is applied via ApplyPayment, and principal may be reduced.
        /// </summary>
        public void PayInterest(int currentTick, int paymentAmount)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            float principalReduction = settings?.principalReductionPerPayment ?? DC_Constants.DEFAULT_PRINCIPAL_REDUCTION_PER_PAYMENT;

            // Apply the payment
            ApplyPayment(paymentAmount, currentTick);
            
            // Reduce principal by a percentage of the original loan amount (legacy behavior)
            if (originalPrincipal > 0 && principal > 0)
            {
                int reductionAmount = (int)(originalPrincipal * principalReduction);
                principal = System.Math.Max(0, principal - reductionAmount);
                
                // If principal is fully paid, clear the debt
                if (principal <= 0)
                {
                    int remainingOwed = GetTotalOwed(currentTick);
                    PayFull(currentTick, remainingOwed);
                    Log.Message("[DebtCollector] Principal fully paid through interest payments. Debt cleared.");
                    return;
                }
            }
            
            nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            paymentDeadlineTick = 0;
            interestDemandSent = false;
            firstMissedPaymentTick = 0; // Reset missed payment tracking when payment is made
            
            // If we were delinquent but not in collections, return to current
            if (status == DebtStatus.Delinquent)
            {
                status = DebtStatus.Current;
            }
            
            // Check if fully paid
            int finalOwed = GetTotalOwed(currentTick);
            if (finalOwed <= 0)
            {
                PayFull(currentTick, 0); // Already paid, just update status
                Log.Message("[DebtCollector] Debt fully paid through interest payment.");
                return;
            }
            
            Log.Message($"[DebtCollector] Interest payment of {paymentAmount} applied. Total owed: {finalOwed}. Next checkpoint at tick {nextInterestDueTick}");
        }

        /// <summary>
        /// Pays off the full balance, clearing the debt.
        /// </summary>
        public void PayFull(int currentTick, int paymentAmount)
        {
            ApplyPayment(paymentAmount, currentTick);
            
            // Check if fully paid
            int totalOwed = GetTotalOwed(currentTick);
            if (totalOwed <= 0)
            {
                principal = 0;
                originalPrincipal = 0;
                paymentsMade = 0;
                nextInterestDueTick = 0;
                paymentDeadlineTick = 0;
                lastPaymentTick = 0;
                firstMissedPaymentTick = 0;
                status = DebtStatus.None;
                interestDemandSent = false;
                collectionsRaidActive = false;
                loanTermDays = 0;
                loanStartTick = 0;
                loanReceivedTick = 0;
                
                Log.Message("[DebtCollector] Full balance paid. Debt cleared.");
            }
            else
            {
                Log.Message($"[DebtCollector] Partial payment of {paymentAmount} applied. Remaining owed: {totalOwed}");
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility.
        /// </summary>
        public void PayFull()
        {
            int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int totalOwed = GetTotalOwed(currentTick);
            PayFull(currentTick, totalOwed);
        }

        /// <summary>
        /// Records that a payment deadline was missed. Does NOT trigger collections - only marks as delinquent.
        /// Collections/raids only happen when loan term expires.
        /// </summary>
        public void RecordMissedPayment(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int intervalTicks = DC_Util.TicksFromDays(intervalDays);

            // If this is the first time missing a payment, record the tick
            if (firstMissedPaymentTick == 0)
            {
                firstMissedPaymentTick = paymentDeadlineTick;
            }

            // Get computed missed payments count (based on days since last payment)
            int missedPaymentsCount = GetMissedPaymentsCount(currentTick);
            
            Log.Message($"[DebtCollector] Payment deadline missed. Missed payments: {missedPaymentsCount} (based on {intervalDays} day intervals)");

            interestDemandSent = false;
            paymentDeadlineTick = 0;

            // Do NOT trigger collections here - only mark as delinquent
            // Collections/raids only happen when loan term expires
            // Schedule next interest checkpoint
            nextInterestDueTick = currentTick + intervalTicks;
            status = DebtStatus.Delinquent;
        }

        /// <summary>
        /// Enters collections state with a deadline for full payment.
        /// </summary>
        public void TriggerCollections(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float deadlineHours = settings?.collectionsDeadlineHours ?? DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS;

            status = DebtStatus.Collections;
            paymentDeadlineTick = currentTick + DC_Util.TicksFromHours(deadlineHours);
            nextInterestDueTick = 0; // No more interest cycles, just full payment
            
            Log.Message($"[DebtCollector] Collections triggered. Full payment due by tick {paymentDeadlineTick}");
        }

        /// <summary>
        /// Marks the collections raid as started.
        /// </summary>
        public void StartCollectionsRaid(int currentTick, int mapId)
        {
            collectionsRaidActive = true;
            collectionsRaidStartTick = currentTick;
            collectionsRaidMapId = mapId;
            
            Log.Message($"[DebtCollector] Collections raid started on map {mapId} at tick {currentTick}");
        }

        /// <summary>
        /// Called when collections raid ends - settles debt by force and locks out borrowing.
        /// </summary>
        public void SettleByForce()
        {
            // lastLoanAmount is preserved for tribute calculation
            principal = 0;
            originalPrincipal = 0;
            paymentsMade = 0;
            nextInterestDueTick = 0;
            paymentDeadlineTick = 0;
            lastPaymentTick = 0;
            firstMissedPaymentTick = 0;
            status = DebtStatus.LockedOut;
            interestDemandSent = false;
            collectionsRaidActive = false;
            collectionsRaidStartTick = 0;
            collectionsRaidMapId = -1;
            loanTermDays = 0;
            loanStartTick = 0;
            loanReceivedTick = 0;
            
            Log.Message("[DebtCollector] Debt settled by force. Borrowing locked out.");
        }

        /// <summary>
        /// Pays tribute to unlock borrowing after forced settlement.
        /// </summary>
        public void PayTribute()
        {
            status = DebtStatus.None;
            lastLoanAmount = 0;
            
            Log.Message("[DebtCollector] Tribute paid. Borrowing privileges restored.");
        }

        /// <summary>
        /// Checks if the loan term has expired.
        /// </summary>
        public bool IsLoanTermExpired(int currentTick)
        {
            if (loanTermDays <= 0 || loanStartTick <= 0)
                return false;
            
            int loanExpiryTick = loanStartTick + DC_Util.TicksFromDays(loanTermDays);
            return currentTick >= loanExpiryTick;
        }

        /// <summary>
        /// Gets the number of days remaining until loan term expires.
        /// </summary>
        public int DaysUntilLoanExpiry(int currentTick)
        {
            if (loanTermDays <= 0 || loanStartTick <= 0)
                return -1;
            
            int loanExpiryTick = loanStartTick + DC_Util.TicksFromDays(loanTermDays);
            int ticksRemaining = loanExpiryTick - currentTick;
            return ticksRemaining > 0 ? (ticksRemaining / DC_Constants.TICKS_PER_DAY) + 1 : 0;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref principal, "principal", 0);
            Scribe_Values.Look(ref originalPrincipal, "originalPrincipal", 0);
            
            // Backward compatibility: migrate old accruedInterest field
            int legacyAccruedInterest = 0;
            Scribe_Values.Look(ref legacyAccruedInterest, "accruedInterest", 0);
            
            Scribe_Values.Look(ref paymentsMade, "paymentsMade", 0);
            Scribe_Values.Look(ref nextInterestDueTick, "nextInterestDueTick", 0);
            Scribe_Values.Look(ref paymentDeadlineTick, "paymentDeadlineTick", 0);
            Scribe_Values.Look(ref lastPaymentTick, "lastPaymentTick", 0);
            
            // Backward compatibility: migrate old missedPayments/missedPaymentsCount fields
            int legacyMissedPayments = 0;
            Scribe_Values.Look(ref legacyMissedPayments, "missedPayments", 0);
            int legacyMissedPaymentsCount = 0;
            Scribe_Values.Look(ref legacyMissedPaymentsCount, "missedPaymentsCount", 0);
            
            // If loading old save without lastPaymentTick, initialize it
            if (Scribe.mode == LoadSaveMode.LoadingVars && lastPaymentTick == 0)
            {
                // Use loanReceivedTick or loanStartTick as baseline
                int loanTick = loanReceivedTick > 0 ? loanReceivedTick : loanStartTick;
                if (loanTick > 0)
                {
                    lastPaymentTick = loanTick;
                }
            }
            
            Scribe_Values.Look(ref firstMissedPaymentTick, "firstMissedPaymentTick", 0);
            Scribe_Values.Look(ref status, "status", DebtStatus.None);
            Scribe_Values.Look(ref interestDemandSent, "interestDemandSent", false);
            Scribe_Values.Look(ref lastLoanAmount, "lastLoanAmount", 0);
            Scribe_Values.Look(ref collectionsRaidActive, "collectionsRaidActive", false);
            Scribe_Values.Look(ref collectionsRaidStartTick, "collectionsRaidStartTick", 0);
            Scribe_Values.Look(ref collectionsRaidMapId, "collectionsRaidMapId", -1);
            Scribe_Values.Look(ref loanTermDays, "loanTermDays", 0);
            Scribe_Values.Look(ref loanStartTick, "loanStartTick", 0);
            Scribe_Values.Look(ref loanReceivedTick, "loanReceivedTick", 0);
            
            // Backward compatibility: if loanReceivedTick is 0 but loanStartTick > 0, use loanStartTick
            if (loanReceivedTick == 0 && loanStartTick > 0)
            {
                loanReceivedTick = loanStartTick;
            }
            
            // Backward compatibility: if originalPrincipal is 0 but principal > 0, set originalPrincipal
            if (originalPrincipal == 0 && principal > 0)
            {
                originalPrincipal = principal;
            }
        }
    }
}
