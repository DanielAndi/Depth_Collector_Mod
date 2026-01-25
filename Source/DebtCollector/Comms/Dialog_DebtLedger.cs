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

        public override Vector2 InitialSize => new Vector2(400f, 350f);

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
                // Principal
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_Principal".Translate(contract.principal));
                y += lineHeight;

                // Accrued Interest
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_Interest".Translate(contract.accruedInterest));
                y += lineHeight;

                // Total Owed
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight + 5f), 
                    "DC_Dialog_TotalOwed".Translate(contract.TotalOwed));
                Text.Font = GameFont.Small;
                y += lineHeight + 10f;

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
                        ? "Payment Deadline" 
                        : "Next Interest Due";
                    
                    Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                        $"{dueLabel}: {timeText}");
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

                // Missed Payments
                int graceMissed = DebtCollectorMod.Settings?.graceMissedPayments ?? 
                                 DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS;
                Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                    "DC_Dialog_MissedPayments".Translate(contract.missedPayments, graceMissed + 1));
                y += lineHeight;

                // Loan Term Information
                if (contract.loanTermDays > 0)
                {
                    int daysRemaining = contract.DaysUntilLoanExpiry(currentTick);
                    if (daysRemaining >= 0)
                    {
                        if (contract.IsLoanTermExpired(currentTick))
                        {
                            GUI.color = Color.red;
                            Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                                "DC_Dialog_LoanExpired".Translate());
                            GUI.color = Color.white;
                        }
                        else
                        {
                            Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                                "DC_Dialog_DaysUntilExpiry".Translate(daysRemaining));
                        }
                        y += lineHeight;
                    }
                    Widgets.Label(new Rect(0, y, inRect.width, lineHeight), 
                        "DC_Dialog_LoanTerm".Translate(contract.loanTermDays));
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
