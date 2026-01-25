using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Component attached to Comms Console via XML patch.
    /// Provides gizmos for interacting with The Ledger's debt system.
    /// </summary>
    public class CompDebtCollectorComms : ThingComp
    {
        public CompProperties_DebtCollectorComms Props => (CompProperties_DebtCollectorComms)props;

        private bool IsPowered
        {
            get
            {
                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                return power == null || power.PowerOn;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (Find.World == null || Find.FactionManager == null)
                yield break;

            // Only show gizmos if powered
            if (!IsPowered)
                yield break;

            // Check if Ledger faction exists
            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
                yield break;

            WorldComponent_DebtCollector worldComp = WorldComponent_DebtCollector.Get();
            if (worldComp == null)
                yield break;

            DebtContract contract = worldComp.Contract;

            // View Ledger button (always available)
            yield return new Command_Action
            {
                defaultLabel = "DC_Gizmo_ViewLedger".Translate(),
                defaultDesc = "DC_Gizmo_ViewLedger_Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/CallAid", false) ?? BaseContent.BadTex,
                action = () => OpenLedgerDialog(contract)
            };

            // Request Loan button
            if (contract.CanBorrow)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DC_Gizmo_RequestLoan".Translate(),
                    defaultDesc = "DC_Gizmo_RequestLoan_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Trade", false) ?? BaseContent.BadTex,
                    action = () => OpenLoanMenu(worldComp)
                };
            }

            // Pay Interest button
            if (contract.IsActive && contract.status != DebtStatus.Collections && contract.interestDemandSent)
            {
                int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                int interestDue = contract.GetCurrentInterestDue(currentTick);
                yield return new Command_Action
                {
                    defaultLabel = "DC_Gizmo_PayInterest".Translate() + $" ({interestDue})",
                    defaultDesc = "DC_Gizmo_PayInterest_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Item/Resource/Silver/Silver_c", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (!worldComp.TryPayInterest(out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    }
                };
            }

            // Pay Full Balance button
            if (contract.IsActive)
            {
                int currentTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                int totalOwed = contract.GetTotalOwed(currentTick);
                yield return new Command_Action
                {
                    defaultLabel = "DC_Gizmo_PayFull".Translate() + $" ({totalOwed})",
                    defaultDesc = "DC_Gizmo_PayFull_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Item/Resource/Silver/Silver_c", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (!worldComp.TryPayFullBalance(out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    }
                };
            }

            // Send Tribute button (when locked out)
            if (contract.status == DebtStatus.LockedOut)
            {
                int tributeRequired = contract.RequiredTribute;
                yield return new Command_Action
                {
                    defaultLabel = "DC_Gizmo_SendTribute".Translate() + $" ({tributeRequired})",
                    defaultDesc = "DC_Gizmo_SendTribute_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Item/Resource/Silver/Silver_c", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (!worldComp.TrySendTribute(out string reason))
                        {
                            Messages.Message(reason, MessageTypeDefOf.RejectInput);
                        }
                    }
                };
            }
        }

        private void OpenLoanMenu(WorldComponent_DebtCollector worldComp)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (int tier in DC_Constants.LOAN_TIERS)
            {
                int amount = tier; // Capture for closure
                string label = "DC_FloatMenu_LoanTier".Translate(amount);
                
                options.Add(new FloatMenuOption(label, () =>
                {
                    if (!worldComp.TryRequestLoan(amount, out string reason))
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

        private void OpenLedgerDialog(DebtContract contract)
        {
            Find.WindowStack.Add(new Dialog_DebtLedger(contract));
        }
    }
}
