using UnityEngine;
using Verse;

namespace DebtCollector
{
    public class DC_Settings : ModSettings
    {
        // Interest rate as percentage (e.g., 10 = 10%) - legacy, kept for backward compatibility
        public float interestRate = DC_Constants.DEFAULT_INTEREST_RATE;
        
        // Daily interest rate (e.g., 0.02 = 2% per day)
        public float interestRatePerDay = DC_Constants.DEFAULT_INTEREST_RATE_PER_DAY;
        
        // Additional daily interest rate when delinquent (e.g., 0.01 = additional 1% per day)
        public float latePenaltyRatePerDay = DC_Constants.DEFAULT_LATE_PENALTY_RATE_PER_DAY;
        
        // Days between interest payments
        public float interestIntervalDays = DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
        
        // Fixed silver fee per missed payment checkpoint
        public int missedPaymentFee = DC_Constants.DEFAULT_MISSED_PAYMENT_FEE;
        
        // Hours to pay interest before counting as missed
        public float interestPaymentWindowHours = DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS;
        
        // Number of missed payments before collections
        public int graceMissedPayments = DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS;
        
        // Hours to pay after collections notice
        public float collectionsDeadlineHours = DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS;
        
        // Settlement distance from player start
        public int minSettlementDistance = DC_Constants.DEFAULT_MIN_SETTLEMENT_DISTANCE;
        public int maxSettlementDistance = DC_Constants.DEFAULT_MAX_SETTLEMENT_DISTANCE;
        
        // Loan term in days (must be paid off within this time)
        public int loanTermDays = DC_Constants.DEFAULT_LOAN_TERM_DAYS;
        
        // Principal reduction per payment (as fraction of original principal, e.g., 0.05 = 5%)
        public float principalReductionPerPayment = DC_Constants.DEFAULT_PRINCIPAL_REDUCTION_PER_PAYMENT;
        
        // Tribute multiplier based on last loan amount
        public float tributeMultiplier = DC_Constants.DEFAULT_TRIBUTE_MULTIPLIER;
        
        // Raid strength multiplier
        public float raidStrengthMultiplier = DC_Constants.DEFAULT_RAID_STRENGTH_MULTIPLIER;
        
        // Maximum loan amount (0 = unlimited)
        public int maxLoanAmount = DC_Constants.DEFAULT_MAX_LOAN_AMOUNT;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref interestRate, "interestRate", DC_Constants.DEFAULT_INTEREST_RATE);
            Scribe_Values.Look(ref interestRatePerDay, "interestRatePerDay", DC_Constants.DEFAULT_INTEREST_RATE_PER_DAY);
            Scribe_Values.Look(ref latePenaltyRatePerDay, "latePenaltyRatePerDay", DC_Constants.DEFAULT_LATE_PENALTY_RATE_PER_DAY);
            Scribe_Values.Look(ref interestIntervalDays, "interestIntervalDays", DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS);
            Scribe_Values.Look(ref missedPaymentFee, "missedPaymentFee", DC_Constants.DEFAULT_MISSED_PAYMENT_FEE);
            Scribe_Values.Look(ref interestPaymentWindowHours, "interestPaymentWindowHours", DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS);
            Scribe_Values.Look(ref graceMissedPayments, "graceMissedPayments", DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS);
            Scribe_Values.Look(ref collectionsDeadlineHours, "collectionsDeadlineHours", DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS);
            Scribe_Values.Look(ref minSettlementDistance, "minSettlementDistance", DC_Constants.DEFAULT_MIN_SETTLEMENT_DISTANCE);
            Scribe_Values.Look(ref maxSettlementDistance, "maxSettlementDistance", DC_Constants.DEFAULT_MAX_SETTLEMENT_DISTANCE);
            Scribe_Values.Look(ref loanTermDays, "loanTermDays", DC_Constants.DEFAULT_LOAN_TERM_DAYS);
            Scribe_Values.Look(ref principalReductionPerPayment, "principalReductionPerPayment", DC_Constants.DEFAULT_PRINCIPAL_REDUCTION_PER_PAYMENT);
            Scribe_Values.Look(ref tributeMultiplier, "tributeMultiplier", DC_Constants.DEFAULT_TRIBUTE_MULTIPLIER);
            Scribe_Values.Look(ref raidStrengthMultiplier, "raidStrengthMultiplier", DC_Constants.DEFAULT_RAID_STRENGTH_MULTIPLIER);
            Scribe_Values.Look(ref maxLoanAmount, "maxLoanAmount", DC_Constants.DEFAULT_MAX_LOAN_AMOUNT);
            
            // Validate and clamp settings to safe ranges (NFR-007)
            ValidateSettings();
        }

        /// <summary>
        /// Clamps all settings to valid ranges to prevent invalid/exploit values (NFR-007).
        /// </summary>
        public void ValidateSettings()
        {
            // Interest rates (must be positive or zero)
            interestRate = Mathf.Clamp(interestRate, 0f, 100f);
            interestRatePerDay = Mathf.Clamp(interestRatePerDay, 0f, 0.5f); // Max 50% per day
            latePenaltyRatePerDay = Mathf.Clamp(latePenaltyRatePerDay, 0f, 0.1f); // Max 10% per day
            
            // Intervals (must be positive)
            interestIntervalDays = Mathf.Max(0.5f, interestIntervalDays);
            interestPaymentWindowHours = Mathf.Clamp(interestPaymentWindowHours, 1f, 168f); // 1 hour to 1 week
            
            // Fees (must be non-negative)
            missedPaymentFee = System.Math.Max(0, missedPaymentFee);
            
            // Grace and deadline (must be positive)
            graceMissedPayments = System.Math.Max(1, graceMissedPayments);
            collectionsDeadlineHours = Mathf.Max(1f, collectionsDeadlineHours);
            
            // Loan settings
            loanTermDays = System.Math.Max(1, loanTermDays);
            maxLoanAmount = System.Math.Max(0, maxLoanAmount); // 0 = unlimited
            principalReductionPerPayment = Mathf.Clamp(principalReductionPerPayment, 0f, 1f);
            
            // Settlement distance
            minSettlementDistance = System.Math.Max(1, minSettlementDistance);
            maxSettlementDistance = System.Math.Max(minSettlementDistance + 1, maxSettlementDistance);
            
            // Multipliers (must be positive)
            tributeMultiplier = Mathf.Max(0.1f, tributeMultiplier);
            raidStrengthMultiplier = Mathf.Max(0.1f, raidStrengthMultiplier);
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            // Use scrollable view for many settings
            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, 900f);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            // === Loan Settings ===
            listing.Label("<b>" + "DC_Settings_Section_Loan".Translate() + "</b>");
            listing.GapLine();

            listing.Label("DC_Settings_MaxLoanAmount".Translate() + ": " + (maxLoanAmount > 0 ? maxLoanAmount.ToString() : "Unlimited"));
            listing.Label("DC_Settings_MaxLoanAmount_Desc".Translate());
            maxLoanAmount = (int)listing.Slider(maxLoanAmount, 0f, 100000f);
            listing.Gap();

            listing.Label("DC_Settings_LoanTermDays".Translate() + ": " + loanTermDays);
            listing.Label("DC_Settings_LoanTermDays_Desc".Translate());
            loanTermDays = (int)listing.Slider(loanTermDays, 7f, 90f);
            listing.Gap();

            // === Interest Settings ===
            listing.Label("<b>" + "DC_Settings_Section_Interest".Translate() + "</b>");
            listing.GapLine();

            listing.Label("DC_Settings_InterestRatePerDay".Translate() + ": " + (interestRatePerDay * 100f).ToString("F1") + "%");
            listing.Label("DC_Settings_InterestRatePerDay_Desc".Translate());
            interestRatePerDay = listing.Slider(interestRatePerDay, 0.005f, 0.10f);
            listing.Gap();

            listing.Label("DC_Settings_LatePenaltyRate".Translate() + ": " + (latePenaltyRatePerDay * 100f).ToString("F1") + "%");
            listing.Label("DC_Settings_LatePenaltyRate_Desc".Translate());
            latePenaltyRatePerDay = listing.Slider(latePenaltyRatePerDay, 0f, 0.05f);
            listing.Gap();

            listing.Label("DC_Settings_InterestIntervalDays".Translate() + ": " + interestIntervalDays.ToString("F1"));
            listing.Label("DC_Settings_InterestIntervalDays_Desc".Translate());
            interestIntervalDays = listing.Slider(interestIntervalDays, 1f, 15f);
            listing.Gap();

            listing.Label("DC_Settings_InterestPaymentWindowHours".Translate() + ": " + interestPaymentWindowHours.ToString("F0"));
            listing.Label("DC_Settings_InterestPaymentWindowHours_Desc".Translate());
            interestPaymentWindowHours = listing.Slider(interestPaymentWindowHours, 6f, 72f);
            listing.Gap();

            listing.Label("DC_Settings_PrincipalReductionPerPayment".Translate() + ": " + (principalReductionPerPayment * 100f).ToString("F1") + "%");
            listing.Label("DC_Settings_PrincipalReductionPerPayment_Desc".Translate());
            principalReductionPerPayment = listing.Slider(principalReductionPerPayment, 0.01f, 0.20f);
            listing.Gap();

            // === Penalties & Collections ===
            listing.Label("<b>" + "DC_Settings_Section_Collections".Translate() + "</b>");
            listing.GapLine();

            listing.Label("DC_Settings_MissedPaymentFee".Translate() + ": " + missedPaymentFee);
            listing.Label("DC_Settings_MissedPaymentFee_Desc".Translate());
            missedPaymentFee = (int)listing.Slider(missedPaymentFee, 0f, 500f);
            listing.Gap();

            listing.Label("DC_Settings_GraceMissedPayments".Translate() + ": " + graceMissedPayments);
            listing.Label("DC_Settings_GraceMissedPayments_Desc".Translate());
            graceMissedPayments = (int)listing.Slider(graceMissedPayments, 1f, 10f);
            listing.Gap();

            listing.Label("DC_Settings_CollectionsDeadlineHours".Translate() + ": " + collectionsDeadlineHours.ToString("F0"));
            listing.Label("DC_Settings_CollectionsDeadlineHours_Desc".Translate());
            collectionsDeadlineHours = listing.Slider(collectionsDeadlineHours, 6f, 48f);
            listing.Gap();

            listing.Label("DC_Settings_RaidStrengthMultiplier".Translate() + ": " + raidStrengthMultiplier.ToString("F1") + "x");
            listing.Label("DC_Settings_RaidStrengthMultiplier_Desc".Translate());
            raidStrengthMultiplier = listing.Slider(raidStrengthMultiplier, 0.5f, 5f);
            listing.Gap();

            // === Lockout & Tribute ===
            listing.Label("<b>" + "DC_Settings_Section_Lockout".Translate() + "</b>");
            listing.GapLine();

            listing.Label("DC_Settings_TributeMultiplier".Translate() + ": " + tributeMultiplier.ToString("F1") + "x");
            listing.Label("DC_Settings_TributeMultiplier_Desc".Translate());
            tributeMultiplier = listing.Slider(tributeMultiplier, 0.5f, 5f);
            listing.Gap();

            // === Settlement Settings ===
            listing.Label("<b>" + "DC_Settings_Section_Settlement".Translate() + "</b>");
            listing.GapLine();

            listing.Label("DC_Settings_MinSettlementDistance".Translate() + ": " + minSettlementDistance);
            listing.Label("DC_Settings_MinSettlementDistance_Desc".Translate());
            minSettlementDistance = (int)listing.Slider(minSettlementDistance, 2f, 20f);
            listing.Gap();

            listing.Label("DC_Settings_MaxSettlementDistance".Translate() + ": " + maxSettlementDistance);
            listing.Label("DC_Settings_MaxSettlementDistance_Desc".Translate());
            maxSettlementDistance = (int)listing.Slider(maxSettlementDistance, minSettlementDistance + 1, 50f);

            listing.End();
            Widgets.EndScrollView();
        }
        
        private static Vector2 scrollPosition = Vector2.zero;
    }
}
