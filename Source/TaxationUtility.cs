using RimWorld;
using RimSynapse.RegionsAndTerritories;
using RimSynapse.Factions.Economy;
using RimSynapse.RegionsAndTerritories.Economy;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Sizing;
using Verse;

namespace RimSynapse.Factions
{
    /// <summary>
    /// The one place taxation touches the world. Same shape as <c>ProductionScalingUtility</c>,
    /// <c>WorldObjectPlacementUtility</c> and <c>SettlementSizeUtility</c>: reads <c>Find</c>,
    /// photographs what it finds into plain numbers, hands them to a pure evaluator, decides nothing.
    ///
    /// <para>Epic 3 child 6. This is the mod-agnostic half — any mod's levy on any tile can ask it,
    /// and the rules never learn a mod's name. <b>Attaching it to Empire's tax path is not done and
    /// cannot be done here:</b> R&amp;T already patches Empire's production, reward and tithe-value
    /// methods by name, but the method that decides what a settlement owes has not been identified
    /// against a real <c>FactionColonies</c> assembly, and guessing a name and signature would
    /// produce a patch that silently never binds. Finding that entry point is a dispatch-mode task;
    /// everything it will need is below and under test.</para>
    /// </summary>
    public static class TaxationUtility
    {
        /// <summary>
        /// The multiplier a tithe, tax or tribute levied on <paramref name="tileId"/> by
        /// <paramref name="faction"/> should be scaled by.
        ///
        /// Returns exactly 1 when economy governance is off, when the world has no province data,
        /// or when anything is missing. A tax hook that throws inside another mod's arithmetic is
        /// far worse than one that declines to have an opinion — the player loses a whole
        /// settlement's income to a stack trace they cannot read.
        /// </summary>
        public static float CollectionFactorFor(int tileId, Faction faction)
        {
            if (!WorldObjectIntegrationSettings.EconomyGovernanceActive) return 1f;
            if (tileId < 0 || faction == null || Find.World == null) return 1f;

            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return 1f;

            GeographicProvince province = regionManager.GetProvinceForTile(tileId);
            if (province == null) return 1f;

            float ownership = 0f;
            float rivalPressure = 0f;

            var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
            if (data != null)
            {
                ownership = data.ScoreFor(faction);
                rivalPressure = data.StrongestRivalScore(faction);
            }

            // Tier of the largest holding standing on the tile. None when nothing is there, and
            // ConcessionFraction(None) is 0 — so an untiered world remits exactly what it always did.
            SettlementTier tier;
            if (SettlementSizeUtility.LargestTieredObjectAt(tileId, out tier) == null)
            {
                tier = SettlementTier.None;
            }

            return TaxationEvaluator.CollectionFactor(ownership, rivalPressure, tier);
        }

        /// <summary>
        /// The same question asked about a world object rather than a bare tile, which is the shape
        /// most callers will actually have. Falls back to 1 for an object with no tile.
        /// </summary>
        public static float CollectionFactorFor(RimWorld.Planet.WorldObject worldObject, Faction faction)
        {
            if (worldObject == null) return 1f;
            return CollectionFactorFor(worldObject.Tile, faction);
        }
    }
}
