using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Represents the player's debt contract with The Ledger.
    /// 
    /// Payment Model:
    /// - Total debt = originalPrincipal + totalInterestForTerm (calculated at loan start)
    /// - 10 equal payments over 30 days (every 3 days)
    /// - Each payment = totalOwed / remainingPayments
    /// - Missed payments add fees, redistribute remaining debt
    /// - Raids only at day 30 if not fully paid
    /// </summary>
    public class DebtContract : IExposable
    {
        public int originalPrincipal;        // Original loan amount (never changes)
        public int totalInterestForTerm;     // Total interest for the full loan term (calculated at start)
        public int paymentsMade;             // Cumulative silver paid toward this contract
        public int accumulatedLateFees;      // Late fees that have been added to the debt
        public int nextInterestDueTick;      // Tick when next payment checkpoint is due
        public int paymentDeadlineTick;      // Deadline for current payment
        public int lastPaymentTick;          // Tick when last payment was made
        public int firstMissedPaymentTick;   // Tick when payment deadline was first missed
        public int paymentsMadeCount;        // Number of successful payments made (for tracking remaining payments)
        public DebtStatus status;            // Current contract status
        public bool interestDemandSent;      // Whether we sent the payment demand letter
        public int lastLoanAmount;           // Used for tribute calculation after lockout
        public int loanTermDays;             // Loan term in days (must be paid off within this time)
        public int loanStartTick;            // Tick when loan was started (legacy name)
        public int loanReceivedTick;         // Tick when loan was received (primary field)
        
        // Legacy field - kept for backward compatibility but no longer used
        public int principal;
        
        // Raid tracking
        public bool collectionsRaidActive;
        public int collectionsRaidStartTick;
        public int collectionsRaidMapId;
        
        // Debug logging throttling
        private static int lastDebugLogTick = 0;
        private const int DEBUG_LOG_INTERVAL = 2500;

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
            return (currentTick - loanTick) / 60000f;
        }

        /// <summary>
        /// Gets the total number of payment checkpoints for this loan.
        /// </summary>
        public int GetTotalPaymentCount()
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            return System.Math.Max(1, (int)(loanTermDays / intervalDays));
        }

        /// <summary>
        /// Gets the number of remaining payment checkpoints.
        /// </summary>
        public int GetRemainingPaymentCount(int currentTick)
        {
            int totalPayments = GetTotalPaymentCount();
            int remaining = totalPayments - paymentsMadeCount;
            return System.Math.Max(1, remaining); // At least 1 remaining
        }

        /// <summary>
        /// Gets the base total debt (principal + interest for full term, calculated at loan start).
        /// This is fixed and doesn't change during the loan.
        /// </summary>
        public int GetBaseTotalDebt()
        {
            return originalPrincipal + totalInterestForTerm;
        }

        /// <summary>
        /// Gets late fees accumulated from missed payments.
        /// </summary>
        public int GetMissedFees(int currentTick)
        {
            return accumulatedLateFees;
        }

        /// <summary>
        /// Gets the number of missed payment periods (for display purposes).
        /// </summary>
        public int GetMissedPaymentsCount(int currentTick)
        {
            if (accumulatedLateFees <= 0)
                return 0;
            
            var settings = DebtCollectorMod.Settings;
            int feePerMissed = settings?.missedPaymentFee ?? DC_Constants.DEFAULT_MISSED_PAYMENT_FEE;
            if (feePerMissed <= 0)
                return 0;
            
            return accumulatedLateFees / feePerMissed;
        }

        /// <summary>
        /// Total amount owed: baseTotalDebt + lateFees - paymentsMade.
        /// Never returns negative.
        /// </summary>
        public int GetTotalOwed(int currentTick)
        {
            int baseTotalDebt = GetBaseTotalDebt();
            int totalOwedRaw = baseTotalDebt + accumulatedLateFees - paymentsMade;
            int totalOwed = System.Math.Max(0, totalOwedRaw);
            
            // Dev mode logging
            if (Prefs.DevMode && (currentTick - lastDebugLogTick >= DEBUG_LOG_INTERVAL || lastDebugLogTick == 0))
            {
                lastDebugLogTick = currentTick;
                float elapsedDays = GetElapsedDays(currentTick);
                int remaining = GetRemainingPaymentCount(currentTick);
                Log.Message($"[DC] Owed={totalOwed} Principal={originalPrincipal} Interest={totalInterestForTerm} Fees={accumulatedLateFees} Paid={paymentsMade} PaymentsDone={paymentsMadeCount} Remaining={remaining} Days={elapsedDays:F2}");
            }
            
            return totalOwed;
        }

        /// <summary>
        /// Legacy property for backward compatibility.
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
        /// Gets the accrued interest (for display - shows the fixed interest amount).
        /// </summary>
        public double GetAccruedInterest(int currentTick)
        {
            return totalInterestForTerm;
        }

        /// <summary>
        /// Gets penalty interest (for backward compatibility - now returns 0 as fees are tracked separately).
        /// </summary>
        public double GetPenaltyInterest(int currentTick)
        {
            return 0.0;
        }

        /// <summary>
        /// Gets the current required payment amount.
        /// This is totalOwed divided by remaining payment checkpoints.
        /// </summary>
        public int GetCurrentInterestDue(int currentTick)
        {
            int totalOwed = GetTotalOwed(currentTick);
            if (totalOwed <= 0)
                return 0;
            
            int remainingPayments = GetRemainingPaymentCount(currentTick);
            int requiredPayment = (int)System.Math.Ceiling((double)totalOwed / remainingPayments);
            
            return requiredPayment;
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
            totalInterestForTerm = 0;
            paymentsMade = 0;
            accumulatedLateFees = 0;
            paymentsMadeCount = 0;
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
        /// Calculates total interest for the full term upfront.
        /// </summary>
        public void StartLoan(int amount, int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int loanTerm = settings?.loanTermDays ?? DC_Constants.DEFAULT_LOAN_TERM_DAYS;
            float interestRatePerDay = settings?.interestRatePerDay ?? DC_Constants.DEFAULT_INTEREST_RATE_PER_DAY;

            // Calculate total interest for the full loan term upfront
            // This is fixed and won't change during the loan
            int calculatedInterest = (int)System.Math.Ceiling(amount * interestRatePerDay * loanTerm);
            int totalPayments = System.Math.Max(1, (int)(loanTerm / intervalDays));

            principal = amount; // Legacy field for backward compatibility
            originalPrincipal = amount;
            totalInterestForTerm = calculatedInterest;
            paymentsMade = 0;
            accumulatedLateFees = 0;
            paymentsMadeCount = 0;
            nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            paymentDeadlineTick = 0;
            lastPaymentTick = currentTick;
            firstMissedPaymentTick = 0;
            status = DebtStatus.Current;
            interestDemandSent = false;
            lastLoanAmount = amount;
            collectionsRaidActive = false;
            loanTermDays = loanTerm;
            loanStartTick = currentTick;
            loanReceivedTick = currentTick;

            int totalDebt = originalPrincipal + totalInterestForTerm;
            int paymentPerCheckpoint = (int)System.Math.Ceiling((double)totalDebt / totalPayments);

            Log.Message($"[DebtCollector] Loan started: {amount} silver. Interest: {calculatedInterest}. Total: {totalDebt}. {totalPayments} payments of ~{paymentPerCheckpoint} each.");
        }

        /// <summary>
        /// Marks that payment demand was sent and sets the payment deadline.
        /// </summary>
        public void SetInterestDemandSent(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float windowHours = settings?.interestPaymentWindowHours ?? DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS;
            
            interestDemandSent = true;
            paymentDeadlineTick = currentTick + DC_Util.TicksFromHours(windowHours);
        }

        /// <summary>
        /// Processes a successful payment.
        /// Simply adds the payment to paymentsMade - no principal reduction.
        /// </summary>
        public void PayInterest(int currentTick, int paymentAmount)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;

            // Apply the payment - just add to paymentsMade
            ApplyPayment(paymentAmount, currentTick);
            paymentsMadeCount++;
            
            // Schedule next payment checkpoint
            nextInterestDueTick = currentTick + DC_Util.TicksFromDays(intervalDays);
            paymentDeadlineTick = 0;
            interestDemandSent = false;
            firstMissedPaymentTick = 0;
            
            // If we were delinquent but not in collections, return to current
            if (status == DebtStatus.Delinquent)
            {
                status = DebtStatus.Current;
            }
            
            // Check if fully paid
            int finalOwed = GetTotalOwed(currentTick);
            if (finalOwed <= 0)
            {
                PayFull(currentTick, 0);
                Log.Message("[DebtCollector] Debt fully paid.");
                return;
            }
            
            int remainingPayments = GetRemainingPaymentCount(currentTick);
            int nextPayment = GetCurrentInterestDue(currentTick);
            Log.Message($"[DebtCollector] Payment of {paymentAmount} applied. Remaining: {finalOwed}. {remainingPayments} payments left (~{nextPayment} each).");
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
                totalInterestForTerm = 0;
                paymentsMade = 0;
                accumulatedLateFees = 0;
                paymentsMadeCount = 0;
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
        /// Records that a payment deadline was missed.
        /// Adds late fee and redistributes remaining debt to remaining payments.
        /// Collections/raids only happen when loan term expires.
        /// </summary>
        public void RecordMissedPayment(int currentTick)
        {
            var settings = DebtCollectorMod.Settings;
            float intervalDays = settings?.interestIntervalDays ?? DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int intervalTicks = DC_Util.TicksFromDays(intervalDays);
            int lateFee = settings?.missedPaymentFee ?? DC_Constants.DEFAULT_MISSED_PAYMENT_FEE;

            // If this is the first time missing a payment, record the tick
            if (firstMissedPaymentTick == 0)
            {
                firstMissedPaymentTick = paymentDeadlineTick;
            }

            // Add late fee to accumulated fees
            accumulatedLateFees += lateFee;
            
            int totalOwed = GetTotalOwed(currentTick);
            int remainingPayments = GetRemainingPaymentCount(currentTick);
            int nextPayment = GetCurrentInterestDue(currentTick);
            
            Log.Message($"[DebtCollector] Payment missed. Late fee: {lateFee}. Total owed: {totalOwed}. {remainingPayments} payments left (~{nextPayment} each).");

            interestDemandSent = false;
            paymentDeadlineTick = 0;

            // Schedule next payment checkpoint
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
            totalInterestForTerm = 0;
            paymentsMade = 0;
            accumulatedLateFees = 0;
            paymentsMadeCount = 0;
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
            Scribe_Values.Look(ref totalInterestForTerm, "totalInterestForTerm", 0);
            Scribe_Values.Look(ref paymentsMade, "paymentsMade", 0);
            Scribe_Values.Look(ref accumulatedLateFees, "accumulatedLateFees", 0);
            Scribe_Values.Look(ref paymentsMadeCount, "paymentsMadeCount", 0);
            Scribe_Values.Look(ref nextInterestDueTick, "nextInterestDueTick", 0);
            Scribe_Values.Look(ref paymentDeadlineTick, "paymentDeadlineTick", 0);
            Scribe_Values.Look(ref lastPaymentTick, "lastPaymentTick", 0);
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
            
            // Backward compatibility migrations
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // If loading old save without loanReceivedTick, use loanStartTick
                if (loanReceivedTick == 0 && loanStartTick > 0)
                {
                    loanReceivedTick = loanStartTick;
                }
                
                // If loading old save without originalPrincipal, use principal
                if (originalPrincipal == 0 && principal > 0)
                {
                    originalPrincipal = principal;
                }
                
                // If loading old save without totalInterestForTerm, calculate it
                if (totalInterestForTerm == 0 && originalPrincipal > 0 && loanTermDays > 0)
                {
                    var settings = DebtCollectorMod.Settings;
                    float interestRatePerDay = settings?.interestRatePerDay ?? DC_Constants.DEFAULT_INTEREST_RATE_PER_DAY;
                    totalInterestForTerm = (int)System.Math.Ceiling(originalPrincipal * interestRatePerDay * loanTermDays);
                    Log.Message($"[DebtCollector] Migrated old save: calculated interest {totalInterestForTerm} for term.");
                }
                
                // If loading old save without lastPaymentTick, use loanReceivedTick
                if (lastPaymentTick == 0 && loanReceivedTick > 0)
                {
                    lastPaymentTick = loanReceivedTick;
                }
            }
        }
    }
}
