using RimWorld.Planet;
using Verse;

namespace DebtCollector
{
    /// <summary>
    /// WorldGenStep def placeholder. Ledger settlement placement is done via Harmony + WorldComponent
    /// after the player selects their starting tile. This step exists only to satisfy the def loader
    /// when DebtCollector_WorldGenStep.xml is present; it does nothing during world generation.
    /// </summary>
    public class WorldGenStep_LedgerSettlement : WorldGenStep
    {
        public WorldGenStep_LedgerSettlement()
        {
        }

        public override int SeedPart => 0xDC01;

        public override void GenerateFresh(string seed, PlanetLayer layer)
        {
            // No-op. Placement handled by WorldComponent_DebtCollector.TryPlaceLedgerSettlement()
            // and Harmony patches (InitNewGame, first map, LoadGame).
        }

        public override void GenerateFromScribe(string seed, PlanetLayer layer)
        {
        }
    }
}
