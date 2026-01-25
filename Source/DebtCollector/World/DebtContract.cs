using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Represents the player's debt contract with The Ledger.
    /// Handles serialization for save/load.
    /// </summary>
    public class DebtContract : IExposable
    {
        public int principal;           // Current remaining loan amount (reduces with payments)
        public int originalPrincipal;   // Original loan amount (for calculating principal reduction)
        public int accruedInterest;     // Interest accumulated since last payment
        public int nextInterestDueTick; // Tick when next interest is due
        public int paymentDeadlineTick; // Deadline for current payment (interest or full)
        public int missedPayments;      // Count of missed interest payments
        public DebtStatus status;       // Current contract status
        public bool interestDemandSent; // Whether we sent the interest demand letter
        public int lastLoanAmount;      // Used for tribute calculation after lockout
        public int loanTermDays;        // Loan term in days (must be paid off within this time)
        public int loanStartTick;       // Tick when loan was started
        
        // Raid tracking
        public bool collectionsRaidActive;
        public int collectionsRaidStartTick;
        public int collectionsRaidMapId;

        public DebtContract()
        {
            Reset();
        }

        /// <summary>
        /// Total amount owed including principal and accrued interest.
        /// </summary>
        public int TotalOwed => principal + accruedInterest;

        /// <summary>
        /// Current interest payment due (calculated from principal).
        /// </summary>
        public int CurrentInterestDue => DC_Util.CalculateInterest(principal);

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
            accruedInterest = 0;
            nextInterestDueTick = 0;
            paymentDeadlineTick = 0;
            missedPayments = 0;
            status = DebtStatus.None;
            interestDemandSent = false;
            collectionsRaidActive = false;
            collectionsRaidStartTick = 0;
            collectionsRaidMapId = -1;
            loanTermDays = 0;
            loanStartTick = 0;
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
            accruedInterest = 0;
            nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            paymentDeadlineTick = 0;
            missedPayments = 0;
            status = DebtStatus.Current;
            interestDemandSent = false;
            lastLoanAmount = amount;
            collectionsRaidActive = false;
            loanTermDays = loanTerm;
            loanStartTick = currentTick;

            Log.Message($"[DebtCollector] Loan started: {amount} silver. Term: {loanTerm} days. First interest due at tick {nextInterestDueTick}");
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
        /// Processes a successful interest payment.
        /// Also reduces principal by a percentage of the original loan amount.
        /// </summary>
        public void PayInterest(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            float principalReduction = settings?.principalReductionPerPayment ?? DC_Constants.DEFAULT_PRINCIPAL_REDUCTION_PER_PAYMENT;

            accruedInterest = 0;
            
            // Reduce principal by a percentage of the original loan amount
            if (originalPrincipal > 0 && principal > 0)
            {
                int reductionAmount = (int)(originalPrincipal * principalReduction);
                principal = System.Math.Max(0, principal - reductionAmount);
                
                // If principal is fully paid, clear the debt
                if (principal <= 0)
                {
                    PayFull();
                    Log.Message("[DebtCollector] Principal fully paid through interest payments. Debt cleared.");
                    return;
                }
            }
            
            nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            paymentDeadlineTick = 0;
            interestDemandSent = false;
            
            // If we were delinquent but not in collections, return to current
            if (status == DebtStatus.Delinquent)
            {
                status = DebtStatus.Current;
            }
            
            Log.Message($"[DebtCollector] Interest paid. Principal reduced to {principal}. Next due at tick {nextInterestDueTick}");
        }

        /// <summary>
        /// Pays off the full balance, clearing the debt.
        /// </summary>
        public void PayFull()
        {
            principal = 0;
            originalPrincipal = 0;
            accruedInterest = 0;
            nextInterestDueTick = 0;
            paymentDeadlineTick = 0;
            missedPayments = 0;
            status = DebtStatus.None;
            interestDemandSent = false;
            collectionsRaidActive = false;
            loanTermDays = 0;
            loanStartTick = 0;
            
            Log.Message("[DebtCollector] Full balance paid. Debt cleared.");
        }

        /// <summary>
        /// Records a missed interest payment.
        /// </summary>
        public void RecordMissedPayment(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            int graceMissed = settings?.graceMissedPayments ?? DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;

            missedPayments++;
            accruedInterest += CurrentInterestDue;
            interestDemandSent = false;
            paymentDeadlineTick = 0;
            
            Log.Message($"[DebtCollector] Missed payment #{missedPayments}. Accrued interest: {accruedInterest}");

            if (missedPayments > graceMissed)
            {
                // Trigger collections
                TriggerCollections(currentTick);
            }
            else
            {
                // Schedule next interest
                status = DebtStatus.Delinquent;
                nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            }
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
            accruedInterest = 0;
            nextInterestDueTick = 0;
            paymentDeadlineTick = 0;
            missedPayments = 0;
            status = DebtStatus.LockedOut;
            interestDemandSent = false;
            collectionsRaidActive = false;
            collectionsRaidStartTick = 0;
            collectionsRaidMapId = -1;
            loanTermDays = 0;
            loanStartTick = 0;
            
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
            Scribe_Values.Look(ref accruedInterest, "accruedInterest", 0);
            Scribe_Values.Look(ref nextInterestDueTick, "nextInterestDueTick", 0);
            Scribe_Values.Look(ref paymentDeadlineTick, "paymentDeadlineTick", 0);
            Scribe_Values.Look(ref missedPayments, "missedPayments", 0);
            Scribe_Values.Look(ref status, "status", DebtStatus.None);
            Scribe_Values.Look(ref interestDemandSent, "interestDemandSent", false);
            Scribe_Values.Look(ref lastLoanAmount, "lastLoanAmount", 0);
            Scribe_Values.Look(ref collectionsRaidActive, "collectionsRaidActive", false);
            Scribe_Values.Look(ref collectionsRaidStartTick, "collectionsRaidStartTick", 0);
            Scribe_Values.Look(ref collectionsRaidMapId, "collectionsRaidMapId", -1);
            Scribe_Values.Look(ref loanTermDays, "loanTermDays", 0);
            Scribe_Values.Look(ref loanStartTick, "loanStartTick", 0);
            
            // Backward compatibility: if originalPrincipal is 0 but principal > 0, set originalPrincipal
            if (originalPrincipal == 0 && principal > 0)
            {
                originalPrincipal = principal;
            }
        }
    }
}
