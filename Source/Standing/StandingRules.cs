using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Sizing;

namespace RimSynapse.Factions.Standing
{
    /// <summary>
    /// What a faction's map position is worth, in one table.
    ///
    /// Seventh named table, alongside <c>PlacementRules</c>, <c>SettlementSizeRules</c>,
    /// <c>ResourceRules</c>, <c>ProductionRules</c>, <c>TaxationRules</c> and <c>SupplyRules</c>,
    /// and for the same 0.8 reason: Logic Externalization should move one object per subsystem
    /// rather than hunt constants through patch files.
    ///
    /// <para>These weights are the softest numbers in 0.7 and they are meant to be. Every other
    /// table decides whether something is allowed or how much of a resource arrives; this one
    /// decides how imposing a faction looks, which has no correct answer, only a defensible one.
    /// The defensible answer taken here is: <b>ground is worth less than what stands on it, and a
    /// city is worth more than a village but not four villages.</b> A consumer that disagrees can
    /// ignore <see cref="Strength"/> entirely and read the counts, which is why the counts are
    /// published separately rather than folded into a single score.</para>
    ///
    /// <para>Nothing in R&amp;T changes behaviour based on these numbers. Getting them wrong makes
    /// another mod's opinion of a faction wrong; it cannot make a placement refuse or a resource
    /// vanish. That is the reason this table can ship uncalibrated when <c>SupplyRules</c> could
    /// not.</para>
    /// </summary>
    public static class StandingRules
    {
        /// <summary>Worth of a province owned outright.</summary>
        public const float HeldProvinceWeight = 1.0f;

        /// <summary>
        /// Worth of a province still being argued over. Half, because a contested province is
        /// ground the faction can stage from and cannot rely on, and because a border war should
        /// not read as an expansion.
        /// </summary>
        public const float ContestedProvinceWeight = 0.5f;

        /// <summary>
        /// Residents per point of strength. The one figure here with any claim to a scale: a
        /// hundred-strong city adds four, which is about what two of its own settlements are worth,
        /// so population reinforces the holding count without overwhelming it.
        /// </summary>
        public const int PopulationPerStrengthPoint = 25;

        /// <summary>
        /// Base worth of a holding, before its tier is applied. A settlement is the unit; a
        /// military installation is most of one because it projects force without producing; an
        /// outpost is half a settlement because it produces without projecting; a camp is almost
        /// nothing because it will be gone next season.
        /// </summary>
        public static float KindWeight(WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.Settlement: return 2.0f;
                case WorldObjectKind.Military: return 1.5f;
                case WorldObjectKind.Outpost: return 1.0f;
                case WorldObjectKind.Camp: return 0.25f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Multiplier a holding's tier applies to its <see cref="KindWeight"/>.
        ///
        /// <para><see cref="SettlementTier.None"/> returns exactly 1, and that is the
        /// collapse-to-baseline rule this codebase applies to every new factor: a holding whose tier
        /// is unknown — an outpost from a mod that exposes no level, every holding at all when
        /// settlement tiers are switched off — is worth precisely its kind weight and nothing is
        /// silently subtracted for the missing information.</para>
        ///
        /// <para>Deliberately not <c>SettlementSizeRules.ProductionScale</c>, though the shape is
        /// similar. Production scale answers what a settlement makes; this answers what it looks
        /// like from outside. They are allowed to diverge — a fortified town can be more
        /// intimidating than productive — and tying them together would mean a future economy
        /// rebalance quietly re-ranked the world's powers.</para>
        /// </summary>
        public static float TierStrength(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Village: return 1.0f;
                case SettlementTier.Town: return 1.5f;
                case SettlementTier.City: return 2.25f;
                case SettlementTier.MajorCity: return 3.0f;
                default: return 1.0f;
            }
        }

        /// <summary>
        /// What one holding contributes. Zero for anything non-territorial, which is what keeps a
        /// caravan or a quest site from making a faction look like an empire.
        /// </summary>
        public static float HoldingStrength(WorldObjectKind kind, SettlementTier tier)
        {
            float baseWeight = KindWeight(kind);
            if (baseWeight <= 0f) return 0f;

            return baseWeight * TierStrength(tier);
        }

        /// <summary>
        /// The whole score, assembled from a finished <see cref="FactionStanding"/>.
        ///
        /// <para>Takes the summary rather than the world because it must be reproducible: a
        /// consumer holding a snapshot can recompute the score after adjusting a weight, without a
        /// live world and without R&amp;T's agreement. A faction with nothing scores exactly
        /// 0.</para>
        /// </summary>
        public static float Strength(FactionStanding standing, float holdingStrengthTotal)
        {
            if (standing == null) return 0f;

            float total = holdingStrengthTotal
                + standing.HeldProvinces * HeldProvinceWeight
                + standing.ContestedProvinces * ContestedProvinceWeight;

            if (PopulationPerStrengthPoint > 0)
            {
                total += standing.Population / (float)PopulationPerStrengthPoint;
            }

            return total < 0f ? 0f : total;
        }
    }
}
