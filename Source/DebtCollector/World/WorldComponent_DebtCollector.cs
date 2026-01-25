using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace DebtCollector
{
    /// <summary>
    /// Main world component that manages the debt contract and processes tick events.
    /// </summary>
    public class WorldComponent_DebtCollector : WorldComponent
    {
        private static bool loggedPostLoadInit;
        private static bool loggedSettlementFailure;
        private static bool loggedSettlementRebuild;

        private DebtContract contract;
        private bool settlementPlaced;
        private int ledgerSettlementTile = -1;

        // Tick interval for checking raid status (every ~1 in-game hour)
        private const int RAID_CHECK_INTERVAL = 2500;
        private int lastRaidCheckTick;

        public DebtContract Contract => contract;
        public bool SettlementPlaced => settlementPlaced;
        public int LedgerSettlementTile => ledgerSettlementTile;

        public WorldComponent_DebtCollector(World world) : base(world)
        {
            contract = new DebtContract();
        }

        public static WorldComponent_DebtCollector Get()
        {
            return Find.World?.GetComponent<WorldComponent_DebtCollector>();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            if (contract == null || Find.TickManager == null)
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Skip processing if no active debt
            if (!contract.IsActive && contract.status != DebtStatus.LockedOut)
                return;

            // Check for collections raid end
            if (contract.collectionsRaidActive)
            {
                if (currentTick - lastRaidCheckTick >= RAID_CHECK_INTERVAL)
                {
                    lastRaidCheckTick = currentTick;
                    CheckCollectionsRaidEnded(currentTick);
                }
                return; // Don't process other events during active raid
            }

            // Check if loan term has expired (only trigger once; then fall through to ProcessCollectionsDeadline)
            if (contract.IsActive && contract.IsLoanTermExpired(currentTick))
            {
                if (contract.status != DebtStatus.Collections)
                {
                    TriggerLoanTermExpiration(currentTick);
                    return; // Just triggered; deadline handling will run on subsequent ticks
                }
                // Already in Collections from loan expiry — fall through to process raid deadline
            }

            // Process interest and collections deadlines
            if (contract.status == DebtStatus.Current || contract.status == DebtStatus.Delinquent)
            {
                ProcessInterestCycle(currentTick);
            }
            else if (contract.status == DebtStatus.Collections)
            {
                ProcessCollectionsDeadline(currentTick);
            }
        }

        private void ProcessInterestCycle(int currentTick)
        {
            // Time to send interest demand?
            if (!contract.interestDemandSent && currentTick >= contract.nextInterestDueTick)
            {
                SendInterestDemand(currentTick);
                return;
            }

            // Check if payment deadline passed
            if (contract.interestDemandSent && currentTick >= contract.paymentDeadlineTick)
            {
                // Player missed the payment window
                int graceMissed = DebtCollectorMod.Settings?.graceMissedPayments ?? 
                                  DC_Constants.DEFAULT_GRACE_MISSED_PAYMENTS;
                
                DC_Util.SendLetter(
                    "DC_Letter_InterestMissed_Title",
                    "DC_Letter_InterestMissed_Text",
                    LetterDefOf.NegativeEvent,
                    null,
                    contract.missedPayments + 1,
                    graceMissed + 1
                );

                contract.RecordMissedPayment(currentTick);

                // If this triggered collections, send that notice too
                if (contract.status == DebtStatus.Collections)
                {
                    SendCollectionsNotice();
                }
            }
        }

        private void SendInterestDemand(int currentTick)
        {
            int interestDue = contract.CurrentInterestDue;
            float windowHours = DebtCollectorMod.Settings?.interestPaymentWindowHours ?? 
                               DC_Constants.DEFAULT_INTEREST_PAYMENT_WINDOW_HOURS;

            DC_Util.SendLetter(
                "DC_Letter_InterestDue_Title",
                "DC_Letter_InterestDue_Text",
                LetterDefOf.NeutralEvent,
                null,
                interestDue
            );

            contract.SetInterestDemandSent(currentTick);
            
            Log.Message($"[DebtCollector] Interest demand sent: {interestDue} silver due within {windowHours} hours");
        }

        private void SendCollectionsNotice()
        {
            DC_Util.SendLetter(
                "DC_Letter_CollectionsNotice_Title",
                "DC_Letter_CollectionsNotice_Text",
                LetterDefOf.ThreatBig,
                null,
                contract.TotalOwed
            );
        }

        private void TriggerLoanTermExpiration(int currentTick)
        {
            // Loan term expired - require full payment immediately
            var settings = DebtCollectorMod.Settings;
            float deadlineHours = settings?.collectionsDeadlineHours ?? DC_Constants.DEFAULT_COLLECTIONS_DEADLINE_HOURS;
            
            contract.TriggerCollections(currentTick);
            
            // Send letter about loan term expiration
            DC_Util.SendLetter(
                "DC_Letter_LoanTermExpired_Title",
                "DC_Letter_LoanTermExpired_Text",
                LetterDefOf.ThreatBig,
                null,
                contract.TotalOwed,
                contract.loanTermDays
            );
            
            Log.Message($"[DebtCollector] Loan term expired. Full payment of {contract.TotalOwed} required within {deadlineHours} hours.");
        }

        private void ProcessCollectionsDeadline(int currentTick)
        {
            if (currentTick >= contract.paymentDeadlineTick)
            {
                // Deadline passed - trigger raid
                TriggerCollectionsRaid();
            }
        }

        private void TriggerCollectionsRaid()
        {
            Map targetMap = DC_Util.GetPlayerHomeMap();
            if (targetMap == null)
            {
                Log.Warning("[DebtCollector] No player home map found for collections raid");
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                Log.Warning("[DebtCollector] Ledger faction not found for collections raid");
                return;
            }

            // Ensure faction is hostile to player for the raid
            // Use TryAffectGoodwill to make faction hostile if not already
            if (!ledgerFaction.HostileTo(Faction.OfPlayer))
            {
                ledgerFaction.TryAffectGoodwillWith(Faction.OfPlayer, -100, canSendMessage: false, canSendHostilityLetter: false, null);
                Log.Message("[DebtCollector] Set Ledger faction to hostile for collections raid");
            }

            // Build incident parameters
            var parms = new IncidentParms
            {
                target = targetMap,
                faction = ledgerFaction,
                forced = true,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn
            };

            // Scale points based on debt and multiplier
            float multiplier = DebtCollectorMod.Settings?.raidStrengthMultiplier ?? 
                              DC_Constants.DEFAULT_RAID_STRENGTH_MULTIPLIER;
            parms.points = contract.TotalOwed * multiplier;
            parms.points = System.Math.Max(parms.points, 200f); // Minimum viable raid

            // Execute the raid incident
            if (DC_DefOf.DC_Incident_CollectionsRaid == null)
            {
                Log.Warning("[DebtCollector] Collections raid aborted: incident def not loaded.");
                return;
            }

            bool success = DC_DefOf.DC_Incident_CollectionsRaid.Worker.TryExecute(parms);
            
            if (success)
            {
                contract.StartCollectionsRaid(Find.TickManager.TicksGame, targetMap.uniqueID);
                lastRaidCheckTick = Find.TickManager.TicksGame;
            }
            else
            {
                Log.Warning("[DebtCollector] Failed to execute collections raid incident");
                // Fall back to settling by force anyway to prevent stuck state
                contract.SettleByForce();
                SendDebtSettledLetter();
            }
        }

        private void CheckCollectionsRaidEnded(int currentTick)
        {
            if (Find.Maps == null)
                return;

            // Don't check too soon after raid started
            if (currentTick - contract.collectionsRaidStartTick < DC_Constants.MIN_RAID_DURATION_TICKS)
                return;

            Map raidMap = null;
            foreach (Map map in Find.Maps)
            {
                if (map.uniqueID == contract.collectionsRaidMapId)
                {
                    raidMap = map;
                    break;
                }
            }

            if (raidMap == null)
            {
                // Map no longer exists (abandoned?) - settle debt
                Log.Message("[DebtCollector] Raid map no longer exists, settling debt");
                contract.SettleByForce();
                SendDebtSettledLetter();
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                contract.SettleByForce();
                SendDebtSettledLetter();
                return;
            }

            // Check if any Ledger pawns with assault/raid lords still exist on map
            bool raidActive = false;
            foreach (Lord lord in raidMap.lordManager.lords)
            {
                if (lord.faction == ledgerFaction && lord.ownedPawns.Count > 0)
                {
                    // Check if this is a hostile raid lord (assault, etc.)
                    if (lord.LordJob is LordJob_AssaultColony || 
                        lord.LordJob is LordJob_AssaultThings)
                    {
                        raidActive = true;
                        break;
                    }
                }
            }

            if (!raidActive)
            {
                // Also check for any hostile pawns from the faction on the map
                int hostilePawnCount = 0;
                foreach (Pawn pawn in raidMap.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.Faction == ledgerFaction && pawn.HostileTo(Faction.OfPlayer))
                    {
                        hostilePawnCount++;
                    }
                }

                if (hostilePawnCount == 0)
                {
                    Log.Message("[DebtCollector] Collections raid ended - no hostile Ledger pawns remain");
                    contract.SettleByForce();
                    SendDebtSettledLetter();
                }
            }
        }

        private void SendDebtSettledLetter()
        {
            DC_Util.SendLetter(
                "DC_Letter_DebtSettled_Title",
                "DC_Letter_DebtSettled_Text",
                LetterDefOf.NeutralEvent
            );
        }

        /// <summary>
        /// Called when player requests a new loan from colony (via comms console).
        /// </summary>
        public bool TryRequestLoan(int amount, out string failReason)
        {
            failReason = null;

            if (contract.status == DebtStatus.LockedOut)
            {
                failReason = "DC_Message_LockedOut".Translate();
                return false;
            }

            if (contract.IsActive)
            {
                failReason = "DC_Message_AlreadyHaveLoan".Translate();
                return false;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                failReason = "DC_Message_NoLedgerFaction".Translate();
                return false;
            }

            // Start the loan
            contract.StartLoan(amount, Find.TickManager.TicksGame);

            // Give silver to caravan if at settlement, otherwise spawn on map
            LookTargets lookTarget = null;
            Caravan caravan = DC_Util.GetCaravanAtLedgerSettlement();
            if (caravan != null)
            {
                DC_Util.AddSilverToCaravan(caravan, amount);
                lookTarget = new LookTargets(caravan);
                Messages.Message("DC_Message_LoanReceivedCaravan".Translate(amount), caravan, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                Map map = DC_Util.GetPlayerHomeMap();
                if (map != null)
                {
                    IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
                    DC_Util.SpawnSilver(map, amount);
                    lookTarget = new LookTargets(dropSpot, map);
                    Messages.Message("DC_Message_LoanReceived".Translate(amount), MessageTypeDefOf.PositiveEvent);
                }
            }

            // Calculate next interest for the letter
            float intervalDays = DebtCollectorMod.Settings?.interestIntervalDays ?? 
                                DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int interestAmount = contract.CurrentInterestDue;

            DC_Util.SendLetter(
                "DC_Letter_LoanGranted_Title",
                "DC_Letter_LoanGranted_Text",
                LetterDefOf.PositiveEvent,
                lookTarget,
                amount,
                interestAmount,
                intervalDays
            );
            
            return true;
        }

        /// <summary>
        /// Called when player requests a new loan while caravan is at the Ledger settlement.
        /// Silver is delivered directly to the caravan's inventory.
        /// </summary>
        public bool TryRequestLoanToCaravan(Caravan caravan, int amount, out string failReason)
        {
            failReason = null;

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (contract.status == DebtStatus.LockedOut)
            {
                failReason = "DC_Message_LockedOut".Translate();
                return false;
            }

            if (contract.IsActive)
            {
                failReason = "DC_Message_AlreadyHaveLoan".Translate();
                return false;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                failReason = "DC_Message_NoLedgerFaction".Translate();
                return false;
            }

            // Start the loan
            contract.StartLoan(amount, Find.TickManager.TicksGame);

            // Give silver directly to the caravan
            DC_Util.AddSilverToCaravan(caravan, amount);

            // Calculate next interest for the letter
            float intervalDays = DebtCollectorMod.Settings?.interestIntervalDays ?? 
                                DC_Constants.DEFAULT_INTEREST_INTERVAL_DAYS;
            int interestAmount = contract.CurrentInterestDue;

            // Letter points to the caravan
            LookTargets lookTarget = new LookTargets(caravan);

            DC_Util.SendLetter(
                "DC_Letter_LoanGranted_Title",
                "DC_Letter_LoanGranted_Text",
                LetterDefOf.PositiveEvent,
                lookTarget,
                amount,
                interestAmount,
                intervalDays
            );

            Messages.Message("DC_Message_LoanReceivedCaravan".Translate(amount), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays interest from colony stockpiles.
        /// </summary>
        public bool TryPayInterest(out string failReason)
        {
            failReason = null;

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (contract.status == DebtStatus.Collections)
            {
                failReason = "DC_Message_InCollections".Translate();
                return false;
            }

            int interestDue = contract.CurrentInterestDue;
            int colonySilver = DC_Util.CountColonySilver();

            if (colonySilver < interestDue)
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(interestDue, colonySilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilver(interestDue))
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(interestDue, colonySilver);
                return false;
            }

            contract.PayInterest(Find.TickManager.TicksGame);
            Messages.Message("DC_Message_PaymentSuccess".Translate(interestDue), MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays interest from caravan inventory (at Ledger settlement).
        /// </summary>
        public bool TryPayInterestFromCaravan(Caravan caravan, out string failReason)
        {
            failReason = null;

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            if (contract.status == DebtStatus.Collections)
            {
                failReason = "DC_Message_InCollections".Translate();
                return false;
            }

            int interestDue = contract.CurrentInterestDue;
            int caravanSilver = DC_Util.CountCaravanSilver(caravan);

            if (caravanSilver < interestDue)
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(interestDue, caravanSilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilverFromCaravan(caravan, interestDue))
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(interestDue, caravanSilver);
                return false;
            }

            contract.PayInterest(Find.TickManager.TicksGame);
            Messages.Message("DC_Message_PaymentSuccess".Translate(interestDue), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays full balance from colony stockpiles.
        /// </summary>
        public bool TryPayFullBalance(out string failReason)
        {
            failReason = null;

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int totalOwed = contract.TotalOwed;
            int colonySilver = DC_Util.CountColonySilver();

            if (colonySilver < totalOwed)
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(totalOwed, colonySilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilver(totalOwed))
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(totalOwed, colonySilver);
                return false;
            }

            contract.PayFull();
            Messages.Message("DC_Message_PaymentSuccess".Translate(totalOwed), MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player pays full balance from caravan inventory (at Ledger settlement).
        /// </summary>
        public bool TryPayFullBalanceFromCaravan(Caravan caravan, out string failReason)
        {
            failReason = null;

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (!contract.IsActive)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int totalOwed = contract.TotalOwed;
            int caravanSilver = DC_Util.CountCaravanSilver(caravan);

            if (caravanSilver < totalOwed)
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(totalOwed, caravanSilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilverFromCaravan(caravan, totalOwed))
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(totalOwed, caravanSilver);
                return false;
            }

            contract.PayFull();
            Messages.Message("DC_Message_PaymentSuccess".Translate(totalOwed), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Called when player sends tribute to unlock borrowing from colony stockpiles.
        /// </summary>
        public bool TrySendTribute(out string failReason)
        {
            failReason = null;

            if (contract.status != DebtStatus.LockedOut)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int tributeRequired = contract.RequiredTribute;
            int colonySilver = DC_Util.CountColonySilver();

            if (colonySilver < tributeRequired)
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(tributeRequired, colonySilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilver(tributeRequired))
            {
                failReason = "DC_Message_NotEnoughSilver".Translate(tributeRequired, colonySilver);
                return false;
            }

            contract.PayTribute();

            DC_Util.SendLetter(
                "DC_Letter_TributeSent_Title",
                "DC_Letter_TributeSent_Text",
                LetterDefOf.PositiveEvent,
                null,
                tributeRequired
            );
            
            return true;
        }

        /// <summary>
        /// Called when player sends tribute from caravan inventory (at Ledger settlement).
        /// </summary>
        public bool TrySendTributeFromCaravan(Caravan caravan, out string failReason)
        {
            failReason = null;

            if (caravan == null)
            {
                failReason = "DC_Message_NoCaravan".Translate();
                return false;
            }

            if (contract.status != DebtStatus.LockedOut)
            {
                failReason = "DC_Message_NoDebt".Translate();
                return false;
            }

            int tributeRequired = contract.RequiredTribute;
            int caravanSilver = DC_Util.CountCaravanSilver(caravan);

            if (caravanSilver < tributeRequired)
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(tributeRequired, caravanSilver);
                return false;
            }

            if (!DC_Util.TryRemoveSilverFromCaravan(caravan, tributeRequired))
            {
                failReason = "DC_Message_NotEnoughSilverCaravan".Translate(tributeRequired, caravanSilver);
                return false;
            }

            contract.PayTribute();

            DC_Util.SendLetter(
                "DC_Letter_TributeSent_Title",
                "DC_Letter_TributeSent_Text",
                LetterDefOf.PositiveEvent,
                new LookTargets(caravan),
                tributeRequired
            );

            Messages.Message("DC_Message_TributeSent".Translate(tributeRequired), caravan, MessageTypeDefOf.PositiveEvent);
            
            return true;
        }

        /// <summary>
        /// Places a Ledger settlement near the player's starting tile.
        /// Called once per game during world finalization.
        /// </summary>
        public void TryPlaceLedgerSettlement()
        {
            if (settlementPlaced)
                return;

            if (Find.World == null || Find.WorldObjects == null)
            {
                if (!loggedSettlementFailure)
                {
                    loggedSettlementFailure = true;
                    Log.Warning("[DebtCollector] Cannot place settlement - World/WorldObjects not ready.");
                }
                return;
            }

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
            {
                Log.Warning("[DebtCollector] Cannot place settlement - Ledger faction not found");
                return;
            }

            // Check if faction already has a settlement
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction)
                {
                    settlementPlaced = true;
                    ledgerSettlementTile = settlement.Tile;
                    Log.Message($"[DebtCollector] Ledger settlement already exists at tile {ledgerSettlementTile}");
                    return;
                }
            }

            // Find player start tile
            int playerStartTile = -1;
            if (Find.GameInitData != null)
            {
                playerStartTile = Find.GameInitData.startingTile;
            }

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == Faction.OfPlayer)
                {
                    playerStartTile = settlement.Tile;
                    break;
                }
            }

            if (playerStartTile < 0)
            {
                Log.Warning("[DebtCollector] Cannot find player starting settlement");
                return;
            }

            // Find a suitable tile
            var settings = DebtCollectorMod.Settings;
            int minDist = settings?.minSettlementDistance ?? DC_Constants.DEFAULT_MIN_SETTLEMENT_DISTANCE;
            int maxDist = settings?.maxSettlementDistance ?? DC_Constants.DEFAULT_MAX_SETTLEMENT_DISTANCE;

            int? targetTile = FindSettlementTile(playerStartTile, minDist, maxDist);
            
            if (!targetTile.HasValue)
            {
                // Retry with wider range
                targetTile = FindSettlementTile(playerStartTile, 2, maxDist * 2);
            }

            if (!targetTile.HasValue)
            {
                Log.Warning("[DebtCollector] Could not find suitable tile for Ledger settlement");
                settlementPlaced = true; // Prevent retrying every tick
                return;
            }

            // Create the settlement
            Settlement newSettlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            newSettlement.Tile = targetTile.Value;
            newSettlement.SetFaction(ledgerFaction);
            newSettlement.Name = SettlementNameGenerator.GenerateSettlementName(newSettlement);
            Find.WorldObjects.Add(newSettlement);

            settlementPlaced = true;
            ledgerSettlementTile = targetTile.Value;

            Log.Message($"[DebtCollector] Created Ledger settlement '{newSettlement.Name}' at tile {targetTile.Value}");
        }

        private int? FindSettlementTile(int centerTile, int minDist, int maxDist)
        {
            // Simple manual search for valid settlement tiles within distance range
            var candidates = new System.Collections.Generic.List<int>();
            int tilesCount = Find.WorldGrid.TilesCount;
            
            for (int i = 0; i < tilesCount; i++)
            {
                if (Find.World.Impassable(i))
                    continue;
                    
                float dist = Find.WorldGrid.ApproxDistanceInTiles(centerTile, i);
                if (dist >= minDist && dist <= maxDist)
                {
                    if (TileFinder.IsValidTileForNewSettlement(i))
                    {
                        candidates.Add(i);
                        // Limit candidates to avoid performance issues
                        if (candidates.Count >= 50)
                            break;
                    }
                }
            }

            if (candidates.Count > 0)
            {
                return candidates.RandomElement();
            }

            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contract, "contract");
            Scribe_Values.Look(ref settlementPlaced, "settlementPlaced", false);
            Scribe_Values.Look(ref ledgerSettlementTile, "ledgerSettlementTile", -1);
            Scribe_Values.Look(ref lastRaidCheckTick, "lastRaidCheckTick", 0);

            // Ensure contract is never null after loading
            if (contract == null)
            {
                contract = new DebtContract();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (!loggedPostLoadInit)
                {
                    loggedPostLoadInit = true;
                    Log.Message("[DebtCollector] WorldComponent.ExposeData PostLoadInit: scheduling settlement validation.");
                }

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    ResolveFactionAndSettlement("PostLoadInit");
                });
            }
        }

        public void ResolveFactionAndSettlement(string context)
        {
            if (Find.World == null || Find.WorldObjects == null || Find.FactionManager == null)
                return;

            Faction ledgerFaction = DC_Util.GetLedgerFaction();
            if (ledgerFaction == null)
                return;

            // If flagged as placed but no settlement exists, allow re-placement
            bool foundSettlement = false;
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == ledgerFaction)
                {
                    foundSettlement = true;
                    settlementPlaced = true;
                    ledgerSettlementTile = settlement.Tile;
                    break;
                }
            }

            if (!foundSettlement && settlementPlaced)
            {
                if (!loggedSettlementRebuild)
                {
                    loggedSettlementRebuild = true;
                    Log.Warning($"[DebtCollector] {context}: settlement flag set but none found. Re-attempting placement.");
                }
                settlementPlaced = false;
                ledgerSettlementTile = -1;
            }

            if (!settlementPlaced)
            {
                TryPlaceLedgerSettlement();
            }
        }
    }
}
