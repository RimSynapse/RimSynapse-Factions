using System;
using System.Collections.Generic;
using RimWorld;
using RimSynapse.RegionsAndTerritories;
using RimWorld.Planet;
using RimSynapse.Factions.Economy;
using RimSynapse.RegionsAndTerritories.Economy;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Sizing;
using Verse;

namespace RimSynapse.Factions
{
    /// <summary>
    /// The one place production scaling touches the world. Same shape as
    /// <c>WorldObjectPlacementUtility</c> and <c>SettlementSizeUtility</c>: this file reads
    /// <c>Find</c>, photographs what it finds into plain numbers, and hands them to the pure
    /// <c>ProductionEvaluator</c>. Nothing here decides anything.
    ///
    /// Epic 3 child 5. Empire's <c>CalculateProductionBase</c> postfix used to carry its own
    /// resource-abundance arithmetic inline; it now asks this, which means VOE yields and anything
    /// else the registry learns about get the identical treatment without the rules naming a mod.
    /// </summary>
    public static class ProductionScalingUtility
    {
        /// <summary>
        /// The multiplier a holding on <paramref name="tileId"/> should apply to its production of
        /// <paramref name="kind"/>.
        ///
        /// Returns exactly 1 when economy governance is off, when the world has no province data,
        /// or when anything at all goes wrong. A production hook that throws inside another mod's
        /// arithmetic is far worse than one that declines to have an opinion.
        /// </summary>
        public static float FactorFor(int tileId, ResourceKind kind, Faction faction)
        {
            if (!WorldObjectIntegrationSettings.EconomyGovernanceActive) return 1f;
            if (tileId < 0 || Find.World == null) return 1f;

            var regionManager = Find.World.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return 1f;

            GeographicProvince province = regionManager.GetProvinceForTile(tileId);
            if (province == null) return 1f;

            if (!province.initializedEconomics) province.InitializeProvinceEconomics();

            ResourcePool pool = province.Pool(kind);

            // Population around the holding rather than the province total (child 4). The province
            // total is the honest fallback and is what 0.6 used, so a tile with no neighbourhood
            // data is not penalised — it simply gets the old answer.
            int surrounding = SurroundingPopulation(tileId, province);

            float ownership = 0f;
            float security = 0f;
            if (faction != null)
            {
                var data = province.ownershipData ?? RegionalOwnershipUtility.CalculateOwnership(province);
                if (data != null)
                {
                    ownership = data.ScoreFor(faction);
                    security = SecurityOf(data, faction);
                }
            }

            // Local richness against the province average for this resource (child 3). Both are
            // measured the same way, so a uniform province returns a ratio of 1 and contributes
            // nothing — which is exactly what "we only have a province average" should mean.
            float localRichness = TileRichness(tileId, kind);
            float averageRichness = AverageRichness(province, kind);

            // Tier of the largest holding actually standing on the tile. None when nothing is there,
            // and SettlementSizeRules.ProductionScale(None) is exactly 1 — so an untiered world gets
            // the 0.6 answer rather than a scaled one.
            SettlementTier tier;
            if (SettlementSizeUtility.LargestTieredObjectAt(tileId, out tier) == null)
            {
                tier = SettlementTier.None;
            }

            return ProductionEvaluator.Evaluate(
                pool, kind, surrounding, ownership, security, localRichness, averageRichness, tier);
        }

        /// <summary>
        /// How uncontested a faction's hold on a province is, 0 to 1.
        ///
        /// R&amp;T has no security system, and inventing one for Epic 3 would have meant a number
        /// with nothing behind it. This derives security from what the mod already measures: a
        /// province nobody else is claiming is fully secure, and every point of a rival's ownership
        /// score is a point off yours. A contested border province is therefore genuinely less
        /// productive than a quiet interior one, which is the behaviour the child asked for, without
        /// a new subsystem to keep in sync.
        ///
        /// <para>Note what the null guard costs: no data reads as <i>zero</i> security, not full,
        /// because a caller with nothing to go on should not be told the province is safe. That is
        /// also why <c>ProductionRules.MaxInsecurityPenalty</c> is parked at zero — under this
        /// formulation a penalty would fall on every province nobody has measured yet. Child 6's
        /// interception avoids the problem by reading <c>StrongestRivalScore</c> directly rather than
        /// through this inversion.</para>
        /// </summary>
        public static float SecurityOf(RegionalOwnershipData data, Faction faction)
        {
            if (data == null || faction == null) return 0f;

            float security = 1f - data.StrongestRivalScore(faction);
            if (security < 0f) return 0f;
            if (security > 1f) return 1f;
            return security;
        }

        /// <summary>
        /// Population on a tile and its immediate neighbours.
        ///
        /// Falls back to the province total whenever the grid cannot be walked or the neighbourhood
        /// reports nobody — an empty answer here is almost always missing data rather than an
        /// genuinely empty region, and treating it as truth would hand a real penalty to holdings
        /// whose surroundings simply were not counted.
        /// </summary>
        public static int SurroundingPopulation(int tileId, GeographicProvince province)
        {
            int fallback = province != null ? province.currentPopulation : 0;

            try
            {
                if (Find.WorldGrid == null) return fallback;

                int total = PopulationDensityUtility.GetPopulationAtTile(tileId);

                List<PlanetTile> neighbours = new List<PlanetTile>();
                Find.WorldGrid.GetTileNeighbors(tileId, neighbours);
                foreach (PlanetTile neighbour in neighbours)
                {
                    total += PopulationDensityUtility.GetPopulationAtTile(neighbour);
                }

                return total > 0 ? total : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// How rich a single tile is in a resource, on the same terms
        /// <c>GeographicProvince.InitializeProvinceEconomics</c> uses to build the province cap. The
        /// two must be measured identically or the ratio between them is meaningless.
        /// </summary>
        public static float TileRichness(int tileId, ResourceKind kind)
        {
            if (Find.WorldGrid == null) return 0f;

            Tile tile = Find.WorldGrid[tileId];
            if (tile == null) return 0f;

            BiomeDef biome = tile.PrimaryBiome;

            switch (kind)
            {
                case ResourceKind.Minerals:
                    return HillMultiplier(tile.hilliness) * 500f;

                case ResourceKind.Biomass:
                    return biome != null ? biome.TreeDensity * 500f : 0f;

                case ResourceKind.Nutrition:
                    return biome != null ? biome.forageability * 500f : 0f;

                case ResourceKind.Textiles:
                    return 100f;

                default:
                    // Manufactured goods are not a property of the ground, so no tile is better at
                    // them than any other. Returning the same figure everywhere makes the ratio 1.
                    return 100f;
            }
        }

        /// <summary>The province's mean tile richness for a resource — the denominator of the ratio.</summary>
        public static float AverageRichness(GeographicProvince province, ResourceKind kind)
        {
            if (province == null || province.tiles == null || province.tiles.Count == 0) return 0f;
            if (Find.WorldGrid == null) return 0f;

            float total = 0f;
            foreach (int tileId in province.tiles)
            {
                total += TileRichness(tileId, kind);
            }

            return total / province.tiles.Count;
        }

        private static float HillMultiplier(Hilliness hilliness)
        {
            if (hilliness == Hilliness.SmallHills) return 1.0f;
            if (hilliness == Hilliness.LargeHills) return 2.0f;
            if (hilliness == Hilliness.Mountainous) return 3.0f;
            return 0.5f;
        }

        /// <summary>
        /// Which pool an Empire resource type draws on. Kept here rather than in the Empire patch so
        /// a second mod's resource names are a second case in one table, not a second table.
        /// </summary>
        public static bool TryResolveResourceKind(string defName, out ResourceKind kind)
        {
            kind = ResourceKind.Nutrition;
            if (string.IsNullOrEmpty(defName)) return false;

            switch (defName)
            {
                case "RTD_Food":
                case "RTD_Animals":
                    kind = ResourceKind.Nutrition; return true;
                case "RTD_Logging":
                    kind = ResourceKind.Biomass; return true;
                case "RTD_Mining":
                    kind = ResourceKind.Minerals; return true;
                case "RTD_Apparel":
                    kind = ResourceKind.Textiles; return true;
                case "RTD_Weapons":
                    kind = ResourceKind.PreIndustrialGoods; return true;
                case "RTD_Medicine":
                    kind = ResourceKind.IndustrialGoods; return true;
                case "RTD_Research":
                    kind = ResourceKind.SpacerGoods; return true;
                default:
                    return false;
            }
        }
    }
}
