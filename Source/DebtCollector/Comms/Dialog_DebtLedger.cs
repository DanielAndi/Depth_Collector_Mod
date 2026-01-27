using RimWorld;
using UnityEngine;
using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Simple dialog window showing the current debt status.
    /// </summary>
    public class Dialog_DebtLedger : Window
    {
        private readonly DebtContract contract;

        public override Vector2 InitialSize => new Vector2(460f, 450f);

        public Dialog_DebtLedger(DebtContract contract)
        {
            this.contract = contract;
            doCloseButton = false;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (contract == null)
            {
                Widgets.Label(new Rect(0, 0, inRect.width, 35f), "No debt data available.");
                if (Widgets.ButtonText(new Rect((inRect.width - 120f) / 2f, inRect.height - 35f, 120f, 35f), 
                    "DC_Dialog_Close".Translate()))
                {
                    Close();
                }
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "DC_Dialog_Title".Translate());
            Text.Font = GameFont.Small;

            float y = 45f;
            float lineHeight = 24f;
            int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            // Status
            string statusText = GetStatusText(contract.status);
            Color statusColor = GetStatusColor(contract.status);
            GUI.color = statusColor;
            Widgets.Label(new Rect(0, y, inRect.width, lineHeight), "DC_Dialog_Status".Translate(statusText));
            GUI.color = Color.white;
            y += lineHeight + 5f;

            if (contract.IsActive)
            {
                // Amount Borrowed (original principal - never changes)
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_AmountBorrowed".Translate(contract.originalPrincipal));
                y += lineHeight;

                // Total Interest for Term (fixed at loan start)
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_TotalInterest".Translate(contract.totalInterestForTerm));
                y += lineHeight;

                // Late Fees (if any)
                int lateFees = contract.GetMissedFees(currentTick);
                if (lateFees > 0)
                {
                    int missedCount = contract.GetMissedPaymentsCount(currentTick);
                    GUI.color = Color.yellow;
                    Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                        "DC_Dialog_LateFees_Simple".Translate(lateFees, missedCount));
                    GUI.color = Color.white;
                    y += lineHeight;
                }

                // Payments Made
                if (contract.paymentsMade > 0)
                {
                    GUI.color = Color.green;
                    Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                        "DC_Dialog_PaymentsMade".Translate(contract.paymentsMade));
                    GUI.color = Color.white;
                    y += lineHeight;
                }

                // Total Owed
                Text.Font = GameFont.Medium;
                int totalOwed = contract.GetTotalOwed(currentTick);
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight + 5f), 
                    "DC_Dialog_TotalOwed".Translate(totalOwed));
                Text.Font = GameFont.Small;
                y += lineHeight + 10f;

                // Payment Schedule Info
                int remainingPayments = contract.GetRemainingPaymentCount(currentTick);
                int requiredPayment = contract.GetCurrentInterestDue(currentTick);
                int totalPayments = contract.GetTotalPaymentCount();
                int paymentsDone = contract.paymentsMadeCount;
                
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_PaymentSchedule".Translate(paymentsDone, totalPayments));
                y += lineHeight;
                
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_RequiredPayment".Translate(requiredPayment));
                y += lineHeight;

                // Next Due
                if (contract.status != DebtStatus.Collections)
                {
                    int ticksUntilDue = contract.interestDemandSent 
                        ? contract.paymentDeadlineTick - currentTick
                        : contract.nextInterestDueTick - currentTick;
                    
                    string timeText = ticksUntilDue > 0 
                        ? DC_Util.FormatTicksAsTime(ticksUntilDue) 
                        : "overdue";
                    
                    string dueLabel = contract.interestDemandSent 
                        ? "DC_Dialog_PaymentDeadline".Translate()
                        : "DC_Dialog_NextPaymentDue".Translate();
                    
                    Color dueColor = contract.interestDemandSent ? Color.yellow : Color.white;
                    GUI.color = dueColor;
                    Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                        $"{dueLabel}: {timeText}");
                    GUI.color = Color.white;
                    y += lineHeight;
                }
                else
                {
                    // Collections deadline
                    int ticksUntilRaid = contract.paymentDeadlineTick - currentTick;
                    string timeText = ticksUntilRaid > 0 
                        ? DC_Util.FormatTicksAsTime(ticksUntilRaid) 
                        : "IMMINENT";
                    
                    GUI.color = Color.red;
                    Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                        $"RAID IN: {timeText}");
                    GUI.color = Color.white;
                    y += lineHeight;
                }

                // Loan Term Information
                if (contract.loanTermDays > 0)
                {
                    int daysRemaining = contract.DaysUntilLoanExpiry(currentTick);
                    if (contract.IsLoanTermExpired(currentTick))
                    {
                        GUI.color = Color.red;
                        Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                            "DC_Dialog_LoanExpired".Translate());
                        GUI.color = Color.white;
                    }
                    else if (daysRemaining >= 0)
                    {
                        Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                            "DC_Dialog_DaysUntilExpiry".Translate(daysRemaining));
                    }
                    y += lineHeight;
                }
            }
            else if (contract.status == DebtStatus.LockedOut)
            {
                y += 10f;
                GUI.color = Color.yellow;
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight * 2), 
                    "Your borrowing privileges are suspended.\nSend tribute to restore them.");
                GUI.color = Color.white;
                y += lineHeight * 2 + 10f;

                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_TributeRequired".Translate(contract.RequiredTribute));
                y += lineHeight;
            }
            else
            {
                y += 10f;
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_NoDebt".Translate());
                y += lineHeight;
            }

            y += 15f;

            // Colony Silver
            int colonySilver = DC_Util.CountColonySilver();
            Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                "DC_Dialog_ColonySilver".Translate(colonySilver));
            y += lineHeight + 20f;

            // Close button
            if (Widgets.ButtonText(new Rect((inRect.width - 120f) / 2f, inRect.height - 35f, 120f, 35f), 
                "DC_Dialog_Close".Translate()))
            {
                Close();
            }
        }

        private string GetStatusText(DebtStatus status)
        {
            switch (status)
            {
                case DebtStatus.None: return "DC_Status_None".Translate();
                case DebtStatus.Current: return "DC_Status_Current".Translate();
                case DebtStatus.Delinquent: return "DC_Status_Delinquent".Translate();
                case DebtStatus.Collections: return "DC_Status_Collections".Translate();
                case DebtStatus.LockedOut: return "DC_Status_LockedOut".Translate();
                default: return status.ToString();
            }
        }

        private Color GetStatusColor(DebtStatus status)
        {
            switch (status)
            {
                case DebtStatus.None: return Color.gray;
                case DebtStatus.Current: return Color.green;
                case DebtStatus.Delinquent: return Color.yellow;
                case DebtStatus.Collections: return Color.red;
                case DebtStatus.LockedOut: return new Color(1f, 0.5f, 0f); // Orange
                default: return Color.white;
            }
        }
    }
}
