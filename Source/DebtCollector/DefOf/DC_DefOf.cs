using RimWorld;
using Verse;

namespace DebtCollector
{
    [DefOf]
    public static class DC_DefOf
    {
        public static FactionDef DC_Faction_TheLedger;
        public static IncidentDef DC_Incident_CollectionsRaid;

        static DC_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(DC_DefOf));
        }
    }
}
