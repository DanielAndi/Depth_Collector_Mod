using System;
using RimWorld;
using Verse;

namespace DebtCollector
{
    /// <summary>
    /// Custom incident worker for The Ledger's collections raid.
    /// Uses vanilla raid generation but ensures the faction and letter are correct.
    /// </summary>
    public class IncidentWorker_CollectionsRaid : IncidentWorker_RaidEnemy
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            try
            {
                if (!(parms.target is Map))
                {
                    Log.Warning("[DebtCollector] Collections raid aborted: target is not a map.");
                    return false;
                }

                if (parms.faction == null)
                {
                    parms.faction = DC_Util.GetLedgerFaction();
                }

                if (parms.faction == null)
                {
                    Log.Warning("[DebtCollector] Collections raid aborted: Ledger faction not found.");
                    return false;
                }

                Log.Message($"[DebtCollector] Collections raid executing on map {((Map)parms.target).uniqueID} for faction {parms.faction.Name}");
                return base.TryExecuteWorker(parms);
            }
            catch (Exception ex)
            {
                Log.Error($"[DebtCollector] Collections raid failed: {ex}");
                return false;
            }
        }

        protected override bool TryResolveRaidFaction(IncidentParms parms)
        {
            // If faction already set (from programmatic call), use it
            if (parms.faction != null)
                return true;

            // Otherwise, try to get Ledger faction
            parms.faction = DC_Util.GetLedgerFaction();
            return parms.faction != null;
        }

        public override void ResolveRaidStrategy(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            // Always use immediate attack for collections
            if (parms.raidStrategy == null)
            {
                parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            }
        }

        public override void ResolveRaidArriveMode(IncidentParms parms)
        {
            // Default to edge walk-in if not set
            if (parms.raidArrivalMode == null)
            {
                parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            }
        }

        protected override string GetLetterLabel(IncidentParms parms)
        {
            return "DC_Letter_CollectionsRaid_Title".Translate();
        }

        protected override string GetLetterText(IncidentParms parms, System.Collections.Generic.List<Pawn> pawns)
        {
            return "DC_Letter_CollectionsRaid_Text".Translate();
        }

        protected override LetterDef GetLetterDef()
        {
            return LetterDefOf.ThreatBig;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            // Only allow firing if we have a target map
            if (parms.target == null)
                return false;

            // Ensure we can get the faction
            if (parms.faction == null && DC_Util.GetLedgerFaction() == null)
                return false;

            return base.CanFireNowSub(parms);
        }
    }
}
