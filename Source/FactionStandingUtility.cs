using System.Collections.Generic;
using RimWorld;
using RimSynapse.RegionsAndTerritories;
using RimWorld.Planet;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Placement;
using RimSynapse.RegionsAndTerritories.Sizing;
using RimSynapse.Factions.Standing;
using Verse;

namespace RimSynapse.Factions
{
    /// <summary>
    /// The live-game entry point for Epic 6 — and, unlike every other façade in 0.7, one that
    /// exists to be called from outside this mod.
    ///
    /// Same shape as <see cref="WorldObjectPlacementUtility"/> and
    /// <see cref="SettlementSizeUtility"/>: this file photographs the world, and
    /// <see cref="StandingEvaluator"/> — which knows nothing about RimWorld — decides what the
    /// photograph means. Every mod's holdings come through here, because kind, level and population
    /// are read from Epic 1's adapter registry rather than from concrete types, which is what lets
    /// a summary of an Empire faction and a summary of a vanilla one be the same kind of object.
    ///
    /// <para><b>Not gated on a setting.</b> The other façades ask
    /// <see cref="WorldObjectIntegrationSettings"/> first because they change what the game does;
    /// this one only reports what is already true, and a consumer that asked "what does this faction
    /// hold" should not be told "nothing" because placement governance happens to be off. The one
    /// switch that does reach in here reaches in by itself: with settlement tiers disabled,
    /// <see cref="SettlementSizeUtility.TierOf"/> returns <see cref="SettlementTier.None"/> for
    /// everything, and <c>StandingRules.TierStrength</c> maps that to a neutral 1 — so tiers
    /// stop distinguishing holdings without any holding disappearing.</para>
    /// </summary>
    public static class FactionStandingUtility
    {
        /// <summary>
        /// Everything R&amp;T knows about where this faction stands. Never null: a faction with no
        /// holdings, an unloaded world, and a world with no regions all return an empty standing
        /// rather than a null a caller in another assembly has to remember to check.
        /// </summary>
        public static FactionStanding For(Faction faction)
        {
            if (faction == null) return FactionStanding.Empty;
            if (Find.World == null) return FactionStanding.Empty;

            return StandingEvaluator.Evaluate(BuildWorld(faction));
        }

        /// <summary>
        /// Convenience for the common consumer question, which is comparative rather than absolute:
        /// nobody wants to know that a faction scores 14.75, they want to know it outranks the one
        /// next door. Returns 0 for a null faction so a caller can sort a list without filtering it.
        /// </summary>
        public static float PerceivedStrengthOf(Faction faction)
        {
            return For(faction).PerceivedStrength;
        }

        /// <summary>
        /// Snapshot of the faction's position. Rebuilt per call, like
        /// <see cref="WorldObjectPlacementUtility.BuildWorld"/> and for the same reason: the world
        /// object list changes constantly, and a summary cached behind a consumer's back is a
        /// summary that reports a settlement destroyed ten minutes ago.
        /// </summary>
        public static StandingWorld BuildWorld(Faction faction)
        {
            if (faction == null) return null;

            HashSet<Faction> claimants = ClaimantsFor(faction);

            return new StandingWorld
            {
                Holdings = CollectHoldings(claimants),
                Provinces = CollectProvinces(claimants)
            };
        }

        /// <summary>
        /// Which factions count as this one for the purpose of the summary.
        ///
        /// <para>Normally just the faction asked about. The exception is the player, whose holdings
        /// an empire-style mod may distribute across a faction of its own — the same equivalence
        /// Epic 1 established and Epic 2's placement rules already honour. A player summary that
        /// omitted the player's own empire would be the single most confusing thing this surface
        /// could publish, so it is resolved here rather than left to the consumer, who has no way
        /// to know the equivalence exists.</para>
        /// </summary>
        private static HashSet<Faction> ClaimantsFor(Faction faction)
        {
            HashSet<Faction> playerControlled = WorldObjectPlacementUtility.CollectPlayerControlledFactions();

            if (playerControlled.Contains(faction)) return playerControlled;

            return new HashSet<Faction> { faction };
        }

        private static List<StandingHolding> CollectHoldings(HashSet<Faction> claimants)
        {
            var holdings = new List<StandingHolding>();
            if (Find.WorldObjects == null) return holdings;

            List<WorldObject> all = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < all.Count; i++)
            {
                WorldObject obj = all[i];
                if (obj == null || obj.Faction == null) continue;
                if (!claimants.Contains(obj.Faction)) continue;

                WorldObjectKind kind = WorldObjectClassifier.Classify(obj);
                if (!kind.IsTerritorial()) continue;

                holdings.Add(new StandingHolding(
                    kind,
                    SettlementSizeUtility.TierOf(obj),
                    kind.HasPopulation() ? SettlementSizeUtility.PopulationOf(obj, kind) : 0,
                    obj.Faction));
            }

            return holdings;
        }

        /// <summary>
        /// Every province any claimant holds or contests, counted once.
        ///
        /// <para>Where two claimants disagree about a province the stronger claim wins, which can
        /// only happen for the player: one player-side faction may hold a province outright while
        /// another merely contests it, and reporting that province twice — once as held, once as
        /// contested — would make the two counts add up to more provinces than the planet has.</para>
        /// </summary>
        private static List<StandingProvince> CollectProvinces(HashSet<Faction> claimants)
        {
            var provinces = new List<StandingProvince>();

            var regionManager = Find.World?.GetComponent<SynapseRegionManager>();
            if (regionManager == null) return provinces;

            foreach (GeographicProvince province in regionManager.Provinces)
            {
                if (province == null) continue;

                ProvinceControl best = ProvinceControl.Unclaimed;

                foreach (Faction claimant in claimants)
                {
                    ProvinceControl control = RegionalOwnershipUtility.GetControl(province, claimant);

                    if (control == ProvinceControl.Held)
                    {
                        best = ProvinceControl.Held;
                        break;
                    }

                    if (control == ProvinceControl.Contested) best = ProvinceControl.Contested;
                }

                if (best != ProvinceControl.Held && best != ProvinceControl.Contested) continue;

                provinces.Add(new StandingProvince(best, province.tiles != null ? province.tiles.Count : 0));
            }

            return provinces;
        }
    }
}
