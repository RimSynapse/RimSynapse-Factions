using System;
using RimSynapse.RegionsAndTerritories.Sizing;

using RimSynapse.RegionsAndTerritories.Economy;

namespace RimSynapse.Factions.Economy
{
    /// <summary>
    /// What a province multiplies a holding's production by. Pure — no <c>Find</c>, no Harmony, no
    /// Unity, no <c>TechLevel</c> — so the factors can be tested without a running game and lifted
    /// into defs in 0.8 without untangling them from reflection.
    ///
    /// This covers Epic 3 children 1 (security and ownership), 3 (local-area richness) and 4
    /// (surrounding population). Child 5 is what calls it from Empire's and VOE's production hooks.
    ///
    /// <para><b>Every factor collapses to 1 when its input is absent.</b> That is the design
    /// constraint, not a nicety. Ownership below the threshold earns nothing rather than losing
    /// something; local richness equal to the province average returns exactly 1, which is what
    /// "we only have a flat province average" means; and the labour curve fed the province
    /// population reproduces 0.6's number to the digit. So a world with none of the new data, or
    /// with the integration switched off, produces exactly what it produced before — the same
    /// property Epic 2 built its ownership weights around.</para>
    /// </summary>
    public static class ProductionEvaluator
    {
        /// <summary>
        /// How much a resource's remaining stock is worth, against the reference level for that
        /// resource. This is 0.6's <c>GetResourceScale</c>, unchanged, with one consequence that is
        /// new and intended: it now reads the pool's <i>current</i> stock, so working a province out
        /// visibly reduces what it yields.
        /// </summary>
        public static float AbundanceFactor(float stock, float baseline)
        {
            if (stock <= 0f) return ProductionRules.MinAbundanceFactor;
            if (baseline <= 0f) return 1f;

            float scale = stock / baseline;
            if (scale < ProductionRules.MinAbundanceFactor) return ProductionRules.MinAbundanceFactor;
            if (scale > ProductionRules.MaxAbundanceFactor) return ProductionRules.MaxAbundanceFactor;
            return scale;
        }

        /// <summary>The reference stock level a resource's abundance is judged against.</summary>
        public static float BaselineFor(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Nutrition: return ProductionRules.NutritionBaseline;
                case ResourceKind.Biomass: return ProductionRules.BiomassBaseline;
                case ResourceKind.Minerals: return ProductionRules.MineralsBaseline;
                case ResourceKind.Textiles: return ProductionRules.TextilesBaseline;
                default: return ProductionRules.GoodsBaseline;
            }
        }

        /// <summary>Abundance straight off a pool, which is how callers should normally ask.</summary>
        public static float AbundanceFactor(ResourcePool pool, ResourceKind kind)
        {
            if (pool == null) return ProductionRules.MinAbundanceFactor;
            return AbundanceFactor(pool.current, BaselineFor(kind));
        }

        /// <summary>
        /// How much labour a population supplies. 0.6's curve exactly: a floor below baseline for an
        /// empty province, rising with headcount, capped so no city becomes self-justifying.
        ///
        /// Epic 3 child 4's refinement is not a new curve — it is feeding this the population
        /// <i>around the holding</i> rather than the province total. Pass the province total and the
        /// answer is 0.6's, which is the fallback when nobody has counted the neighbourhood.
        /// </summary>
        public static float LabourFactor(int population)
        {
            if (population < 0) population = 0;

            float factor = ProductionRules.LabourFloor + population * ProductionRules.LabourPerPerson;
            return factor > ProductionRules.MaxLabourFactor ? ProductionRules.MaxLabourFactor : factor;
        }

        /// <summary>
        /// What a faction earns for holding and securing the ground it produces on.
        ///
        /// Both inputs are required and neither substitutes for the other: territory you own but
        /// cannot police is not productive territory, and policing ground that is not yours is
        /// somebody else's problem. Ownership below <c>MinOwnershipForBonus</c> earns nothing, which
        /// is what makes this safe to switch on in an existing world.
        /// </summary>
        /// <param name="ownershipScore">0 to 1, as <c>RegionalOwnershipData.ScoreFor</c> reports it.</param>
        /// <param name="security">0 to 1. 1 means the province is fully policed.</param>
        public static float SecurityFactor(float ownershipScore, float security)
        {
            if (security < 0f) security = 0f;
            if (security > 1f) security = 1f;

            float penalty = ProductionRules.MaxInsecurityPenalty * (1f - security);

            float bonus = 0f;
            if (ownershipScore > ProductionRules.MinOwnershipForBonus)
            {
                float held = (ownershipScore - ProductionRules.MinOwnershipForBonus)
                           / (1f - ProductionRules.MinOwnershipForBonus);
                if (held > 1f) held = 1f;
                bonus = ProductionRules.MaxSecurityBonus * held * security;
            }

            float factor = 1f + bonus - penalty;
            return factor < 0f ? 0f : factor;
        }

        /// <summary>
        /// How much the ground immediately around a holding differs from the province as a whole.
        ///
        /// Epic 3 child 3: a mining outpost sitting on ore should outproduce one in the same province
        /// standing on clay. The gap is dampened by <c>LocalityWeight</c> because a holding trades
        /// and forages well beyond the tiles it occupies — terrain underfoot should tilt output, not
        /// dictate it.
        ///
        /// Identical inputs return exactly 1, so a caller with no per-tile survey can pass the
        /// province average for both and lose nothing.
        /// </summary>
        /// <param name="localRichness">Richness of the tiles around the holding.</param>
        /// <param name="provinceAverageRichness">The province's mean richness for the same resource.</param>
        public static float LocalityFactor(float localRichness, float provinceAverageRichness)
        {
            if (provinceAverageRichness <= 0f) return 1f;
            if (localRichness < 0f) localRichness = 0f;

            float ratio = localRichness / provinceAverageRichness;
            float factor = 1f + (ratio - 1f) * ProductionRules.LocalityWeight;

            if (factor < ProductionRules.MinLocalityFactor) return ProductionRules.MinLocalityFactor;
            if (factor > ProductionRules.MaxLocalityFactor) return ProductionRules.MaxLocalityFactor;
            return factor;
        }

        /// <summary>
        /// Every factor together, bounded.
        ///
        /// The clamp is the point. Multiplying four independently reasonable factors produces a
        /// range nobody chose, and an unbounded product is how a mod about regions ends up with one
        /// province worth playing. See <c>ProductionRules.MinTotalFactor</c>.
        /// </summary>
        public static float Compose(
            float abundance, float labour, float security, float locality, SettlementTier tier)
        {
            float tierScale = SettlementSizeRules.ProductionScale(tier);
            if (tierScale <= 0f) tierScale = 1f;

            float total = abundance * labour * security * locality * tierScale;

            if (float.IsNaN(total) || total < ProductionRules.MinTotalFactor)
            {
                return ProductionRules.MinTotalFactor;
            }
            if (total > ProductionRules.MaxTotalFactor) return ProductionRules.MaxTotalFactor;
            return total;
        }

        /// <summary>
        /// The whole model in one call, for the production hooks child 5 will write.
        ///
        /// <paramref name="localRichness"/> and <paramref name="provinceAverageRichness"/> may be
        /// passed equal — or both zero — by a caller that has no per-tile survey; the locality factor
        /// then contributes nothing. Likewise <paramref name="surroundingPopulation"/> may be the
        /// province total.
        /// </summary>
        public static float Evaluate(
            ResourcePool pool,
            ResourceKind kind,
            int surroundingPopulation,
            float ownershipScore,
            float security,
            float localRichness,
            float provinceAverageRichness,
            SettlementTier tier)
        {
            return Compose(
                AbundanceFactor(pool, kind),
                LabourFactor(surroundingPopulation),
                SecurityFactor(ownershipScore, security),
                LocalityFactor(localRichness, provinceAverageRichness),
                tier);
        }
    }
}
