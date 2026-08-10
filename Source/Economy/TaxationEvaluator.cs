using RimSynapse.RegionsAndTerritories.Sizing;

namespace RimSynapse.Factions.Economy
{
    /// <summary>
    /// What fraction of a levy actually reaches the capital. Pure — no <c>Find</c>, no Harmony, no
    /// Unity, no <c>TechLevel</c> — in the same sense as <c>ProductionEvaluator</c>.
    ///
    /// <para>Epic 3 child 6. The distinction that makes this worth having as its own model rather
    /// than another factor bolted onto production: <b>production is about the ground, taxation is
    /// about the journey.</b> A rich province you barely hold produces plenty and remits little. A
    /// poor one you hold absolutely remits nearly all of very little. Folding the two together would
    /// have made those two situations indistinguishable, and they are the two situations an empire
    /// mod exists to make you choose between.</para>
    ///
    /// <para><b>Collapses to 1 on absent input,</b> like every other 0.7 model: no ownership, no
    /// rival, no tier returns exactly 1, so an existing world's tithes do not move the moment 0.7
    /// loads.</para>
    /// </summary>
    public static class TaxationEvaluator
    {
        /// <summary>
        /// What firm ownership adds. Ownership alone, deliberately — the rival half of the picture
        /// is applied by <see cref="InterceptedFraction"/>, and requiring security here as well
        /// would charge the same contested province twice for the same rival.
        /// </summary>
        /// <param name="ownershipScore">0 to 1, as <c>RegionalOwnershipData.ScoreFor</c> reports it.</param>
        public static float LoyaltyBonus(float ownershipScore)
        {
            if (ownershipScore <= TaxationRules.MinOwnershipForLoyalty) return 0f;

            float held = (ownershipScore - TaxationRules.MinOwnershipForLoyalty)
                       / (1f - TaxationRules.MinOwnershipForLoyalty);
            if (held > 1f) held = 1f;

            return TaxationRules.MaxLoyaltyBonus * held;
        }

        /// <summary>
        /// What a rival takes off the top before the levy arrives.
        ///
        /// Keyed on the strongest rival's own score rather than on derived security, so that "we
        /// have no ownership data" and "there is genuinely nobody else here" are the same answer —
        /// zero — instead of the maximum penalty. That is the whole reason this penalty can be
        /// non-zero where <c>ProductionRules.MaxInsecurityPenalty</c> could not.
        ///
        /// Only the strongest rival counts, not the sum: three weak neighbours are not one strong
        /// one, and adding them up would make a crowded map uniformly untaxable.
        /// </summary>
        /// <param name="rivalPressure">The strongest competing faction's ownership score, 0 to 1.</param>
        public static float InterceptedFraction(float rivalPressure)
        {
            if (rivalPressure <= TaxationRules.RivalPressureFloor) return 0f;

            float pressure = (rivalPressure - TaxationRules.RivalPressureFloor)
                           / (1f - TaxationRules.RivalPressureFloor);
            if (pressure > 1f) pressure = 1f;

            return TaxationRules.MaxInterceptedFraction * pressure;
        }

        /// <summary>
        /// What a settlement of this tier keeps back. See <c>TaxationRules.TownConcession</c> for
        /// why this exists at all — it is the counterweight to tiered production scaling, not a
        /// punishment for growing.
        /// </summary>
        public static float ConcessionFraction(SettlementTier tier)
        {
            switch (tier)
            {
                case SettlementTier.Town: return TaxationRules.TownConcession;
                case SettlementTier.City: return TaxationRules.CityConcession;
                case SettlementTier.MajorCity: return TaxationRules.MajorCityConcession;
                default: return 0f;
            }
        }

        /// <summary>
        /// The whole model in one call: the multiplier to apply to a tithe, tax or tribute levied
        /// on a holding.
        ///
        /// Returns exactly 1 for a holding with no ownership data, no rival, and no tier.
        /// </summary>
        /// <param name="ownershipScore">The levying faction's share of the province, 0 to 1.</param>
        /// <param name="rivalPressure">The strongest other faction's share of the same province, 0 to 1.</param>
        /// <param name="tier">The holding's settlement tier, or <c>None</c>.</param>
        public static float CollectionFactor(float ownershipScore, float rivalPressure, SettlementTier tier)
        {
            float factor = 1f
                + LoyaltyBonus(ownershipScore)
                - InterceptedFraction(rivalPressure)
                - ConcessionFraction(tier);

            if (float.IsNaN(factor) || factor < TaxationRules.MinCollectionFactor)
            {
                return TaxationRules.MinCollectionFactor;
            }
            if (factor > TaxationRules.MaxCollectionFactor) return TaxationRules.MaxCollectionFactor;
            return factor;
        }

        /// <summary>
        /// What a holding is actually worth to the treasury, tier included on both sides: it
        /// produces <c>ProductionScale</c> and remits <c>CollectionFactor</c> of that.
        ///
        /// Exposed as its own method because the property that matters is a property of the
        /// <i>product</i>, not of either half — this must never decrease as a settlement tiers up,
        /// and having one place to ask makes that testable rather than assumed.
        /// </summary>
        public static float NetCollectible(float ownershipScore, float rivalPressure, SettlementTier tier)
        {
            return SettlementSizeRules.ProductionScale(tier)
                 * CollectionFactor(ownershipScore, rivalPressure, tier);
        }
    }
}
