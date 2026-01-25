using System;
using RimWorld;
using Verse;
using Verse.AI.Group;

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

                // Ensure faction is hostile to player for the raid
                // Use TryAffectGoodwill to make faction hostile if not already
                if (!parms.faction.HostileTo(Faction.OfPlayer))
                {
                    parms.faction.TryAffectGoodwillWith(Faction.OfPlayer, -100, canSendMessage: false, canSendHostilityLetter: false, null);
                    Log.Message("[DebtCollector] Set Ledger faction to hostile in incident worker");
                }

                // Ensure raid parameters are set correctly
                if (parms.raidStrategy == null)
                {
                    parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
                }
                if (parms.raidArrivalMode == null)
                {
                    parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
                }

                Log.Message($"[DebtCollector] Collections raid executing on map {((Map)parms.target).uniqueID} for faction {parms.faction.Name}");
                bool result = base.TryExecuteWorker(parms);
                
                // Post-spawn: Ensure all spawned pawns are hostile and have proper lord jobs
                if (result)
                {
                    EnsureRaidPawnsHostile((Map)parms.target, parms.faction);
                }
                
                return result;
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

        /// <summary>
        /// Ensures all spawned raid pawns are assigned to proper assault lord jobs.
        /// The base raid worker should create assault lords, but we verify and fix if needed.
        /// </summary>
        private void EnsureRaidPawnsHostile(Map map, Faction faction)
        {
            if (map == null || faction == null)
                return;

            // Find existing assault lord for this faction
            Lord assaultLord = null;
            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.faction == faction && 
                    (lord.LordJob is LordJob_AssaultColony || 
                     lord.LordJob is LordJob_AssaultThings))
                {
                    assaultLord = lord;
                    break;
                }
            }

            // If no assault lord exists, the base raid worker should have created one
            // Just log a warning and let the base system handle it
            if (assaultLord == null)
            {
                Log.Warning("[DebtCollector] No assault lord found for collections raid - base raid worker should have created one");
                return;
            }

            // Ensure all faction pawns are in the assault lord
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction == faction && pawn.GetLord() != assaultLord)
                {
                    Lord currentLord = pawn.GetLord();
                    if (currentLord != null)
                    {
                        currentLord.RemovePawn(pawn);
                    }
                    assaultLord.AddPawn(pawn);
                    Log.Message($"[DebtCollector] Assigned pawn {pawn.Name} to assault lord for collections raid");
                }
            }
        }
    }
}
