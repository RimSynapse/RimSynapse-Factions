using RimWorld;
using RimSynapse.RegionsAndTerritories;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Sizing;
using RimSynapse.Factions.Military;
using RimSynapse.RegionsAndTerritories.Placement;
using Verse;

namespace RimSynapse.Factions
{
    /// <summary>
    /// The one place military reach touches the world. Same shape as
    /// <c>WorldObjectPlacementUtility</c>, <c>SettlementSizeUtility</c>,
    /// <c>ProductionScalingUtility</c> and <c>TaxationUtility</c>: reads <c>Find</c>, photographs
    /// what it finds into a <see cref="SupplyNetwork"/>, decides nothing.
    ///
    /// <para>Epic 5 children 1 and 2. Child 1 asked for Empire's adjacency restriction to be
    /// extracted into a reusable service applied to <i>any</i> mod's military action; this is that
    /// service, and it names no mod. The Empire prefix that used to carry the rule inline now asks
    /// here, exactly as its production postfix now asks <c>ProductionScalingUtility</c>.</para>
    ///
    /// <para>It also closes a real gap rather than only refactoring one: the
    /// <c>militaryGovernance</c> setting has existed in the settings dialog since Epic 1 and until
    /// now controlled nothing at all, because the adjacency check never consulted it. A switch that
    /// does nothing is worse than a missing switch.</para>
    /// </summary>
    public static class MilitaryReachUtility
    {
        /// <summary>
        /// Can <paramref name="faction"/> reach <paramref name="targetTileId"/> from
        /// <paramref name="sourceTileId"/>, and in what state does the force arrive?
        ///
        /// Returns an unrestricted line whenever governance is off or the world has no province data
        /// — a military hook that refuses an action because it could not find the world is far worse
        /// than one that stands aside.
        /// </summary>
        public static SupplyLine ReachBetweenTiles(int sourceTileId, int targetTileId, Faction faction)
        {
            if (!WorldObjectIntegrationSettings.MilitaryGovernanceActive) return SupplyLine.Unrestricted();
            if (sourceTileId < 0 || targetTileId < 0 || Find.World == null) return SupplyLine.Unrestricted();

            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return SupplyLine.Unrestricted();

            int sourceProvinceId = regionManager.GetProvinceId(sourceTileId);
            int targetProvinceId = regionManager.GetProvinceId(targetTileId);
            if (sourceProvinceId < 0 || targetProvinceId < 0) return SupplyLine.Unrestricted();

            SupplyNetwork network = BuildNetwork(regionManager);
            if (network == null) return SupplyLine.Unrestricted();

            return SupplyEvaluator.Evaluate(network, sourceProvinceId, targetProvinceId, faction);
        }

        /// <summary>Yes/no plus a message, for call sites that only want to allow or refuse.</summary>
        public static bool CanReach(int sourceTileId, int targetTileId, Faction faction, out string reason)
        {
            SupplyLine line = ReachBetweenTiles(sourceTileId, targetTileId, faction);
            reason = line.Reason ?? string.Empty;
            return line.Reachable;
        }

        public static SupplyNetwork BuildNetwork(SynapseRegionManager regionManager)
        {
            if (regionManager == null) return null;

            return new SupplyNetwork
            {
                Neighbours = provinceId => ProvinceAdjacency.NeighboursOf(regionManager, provinceId),
                ControlOf = (provinceId, faction) => ControlOf(regionManager, provinceId, faction as Faction)
            };
        }

        private static ProvinceControl ControlOf(SynapseRegionManager regionManager, int provinceId, Faction faction)
        {
            if (regionManager == null || provinceId < 0 || faction == null) return ProvinceControl.Unclaimed;

            GeographicProvince province = regionManager.GetProvince(provinceId);
            if (province == null) return ProvinceControl.Unclaimed;

            // The same question the placement layer asks, answered by the same code. Supply and
            // placement disagreeing about who holds a province would be a bug nobody could reproduce.
            return RegionalOwnershipUtility.GetControl(province, faction);
        }

        /// <summary>Drop the cached geography. Kept as the military-side name for the shared cache.</summary>
        public static void ClearCache()
        {
            ProvinceAdjacency.ClearCache();
        }
    }
}
