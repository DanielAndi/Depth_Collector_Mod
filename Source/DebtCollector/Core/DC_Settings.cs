using UnityEngine;
using Verse;

namespace DebtCollector
{
    public class DC_Settings : ModSettings
    {
        // Interest rate as percentage (e.g., 10 = 10%)
        public float interestRate = DC_Constants.DEFAULT_INTEREST_RATE;
        
        // Days between interest payments
        public float interestIntervalDays = DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
        
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

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref interestRate, "interestRate", DC_Constants.DEFAULT_INTEREST_RATE);
            Scribe_Values.Look(ref interestIntervalDays, "interestIntervalDays", DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS);
            Scribe_Values.Look(ref interestPaymentWindowHours, "interestPaymentWindowHours", DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS);
            Scribe_Values.Look(ref graceMissedPayments, "graceMissedPayments", DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS);
            Scribe_Values.Look(ref collectionsDeadlineHours, "collectionsDeadlineHours", DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS);
            Scribe_Values.Look(ref minSettlementDistance, "minSettlementDistance", DC_Constants.DEFAULT_MIN_SETTLEMENT_DISTANCE);
            Scribe_Values.Look(ref maxSettlementDistance, "maxSettlementDistance", DC_Constants.DEFAULT_MAX_SETTLEMENT_DISTANCE);
            Scribe_Values.Look(ref loanTermDays, "loanTermDays", DC_Constants.DEFAULT_LOAN_TERM_DAYS);
            Scribe_Values.Look(ref principalReductionPerPayment, "principalReductionPerPayment", DC_Constants.DEFAULT_PRINCIPAL_REDUCTION_PER_PAYMENT);
            Scribe_Values.Look(ref tributeMultiplier, "tributeMultiplier", DC_Constants.DEFAULT_TRIBUTE_MULTIPLIER);
            Scribe_Values.Look(ref raidStrengthMultiplier, "raidStrengthMultiplier", DC_Constants.DEFAULT_RAID_STRENGTH_MULTIPLIER);
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label("DC_Settings_InterestRate".Translate() + ": " + interestRate.ToString("F1") + "%");
            listing.Label("DC_Settings_InterestRate_Desc".Translate());
            interestRate = listing.Slider(interestRate, 1f, 50f);
            listing.Gap();

            listing.Label("DC_Settings_InterestIntervalDays".Translate() + ": " + interestIntervalDays.ToString("F1"));
            listing.Label("DC_Settings_InterestIntervalDays_Desc".Translate());
            interestIntervalDays = listing.Slider(interestIntervalDays, 1f, 15f);
            listing.Gap();

            listing.Label("DC_Settings_InterestPaymentWindowHours".Translate() + ": " + interestPaymentWindowHours.ToString("F0"));
            listing.Label("DC_Settings_InterestPaymentWindowHours_Desc".Translate());
            interestPaymentWindowHours = listing.Slider(interestPaymentWindowHours, 6f, 72f);
            listing.Gap();

            listing.Label("DC_Settings_GraceMissedPayments".Translate() + ": " + graceMissedPayments);
            listing.Label("DC_Settings_GraceMissedPayments_Desc".Translate());
            graceMissedPayments = (int)listing.Slider(graceMissedPayments, 1f, 5f);
            listing.Gap();

            listing.Label("DC_Settings_CollectionsDeadlineHours".Translate() + ": " + collectionsDeadlineHours.ToString("F0"));
            listing.Label("DC_Settings_CollectionsDeadlineHours_Desc".Translate());
            collectionsDeadlineHours = listing.Slider(collectionsDeadlineHours, 6f, 48f);
            listing.Gap();

            listing.Label("DC_Settings_LoanTermDays".Translate() + ": " + loanTermDays);
            listing.Label("DC_Settings_LoanTermDays_Desc".Translate());
            loanTermDays = (int)listing.Slider(loanTermDays, 7f, 90f);
            listing.Gap();

            listing.Label("DC_Settings_PrincipalReductionPerPayment".Translate() + ": " + (principalReductionPerPayment * 100f).ToString("F1") + "%");
            listing.Label("DC_Settings_PrincipalReductionPerPayment_Desc".Translate());
            principalReductionPerPayment = listing.Slider(principalReductionPerPayment, 0.01f, 0.20f);
            listing.Gap();

            listing.Label("DC_Settings_MinSettlementDistance".Translate() + ": " + minSettlementDistance);
            listing.Label("DC_Settings_MinSettlementDistance_Desc".Translate());
            minSettlementDistance = (int)listing.Slider(minSettlementDistance, 2f, 20f);
            listing.Gap();

            listing.Label("DC_Settings_MaxSettlementDistance".Translate() + ": " + maxSettlementDistance);
            listing.Label("DC_Settings_MaxSettlementDistance_Desc".Translate());
            maxSettlementDistance = (int)listing.Slider(maxSettlementDistance, minSettlementDistance + 1, 50f);
            listing.Gap();

            listing.Label("DC_Settings_TributeMultiplier".Translate() + ": " + tributeMultiplier.ToString("F1") + "x");
            listing.Label("DC_Settings_TributeMultiplier_Desc".Translate());
            tributeMultiplier = listing.Slider(tributeMultiplier, 0.5f, 5f);
            listing.Gap();

            listing.Label("DC_Settings_RaidStrengthMultiplier".Translate() + ": " + raidStrengthMultiplier.ToString("F1") + "x");
            listing.Label("DC_Settings_RaidStrengthMultiplier_Desc".Translate());
            raidStrengthMultiplier = listing.Slider(raidStrengthMultiplier, 0.5f, 5f);

            listing.End();
        }
    }
}
