using System.Collections.Generic;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Placement;
using RimSynapse.RegionsAndTerritories.Sizing;

namespace RimSynapse.Factions.Standing
{
    /// <summary>One holding, reduced to the four things a summary cares about.</summary>
    public sealed class StandingHolding
    {
        public readonly WorldObjectKind kind;
        public readonly SettlementTier tier;
        public readonly int population;
        public readonly object faction;

        public StandingHolding(WorldObjectKind kind, SettlementTier tier, int population, object faction)
        {
            this.kind = kind;
            this.tier = tier;
            this.population = population < 0 ? 0 : population;
            this.faction = faction;
        }
    }

    /// <summary>One province, reduced to how the asking faction stands in it and how big it is.</summary>
    public sealed class StandingProvince
    {
        public readonly ProvinceControl control;
        public readonly int tileCount;

        public StandingProvince(ProvinceControl control, int tileCount)
        {
            this.control = control;
            this.tileCount = tileCount < 0 ? 0 : tileCount;
        }
    }

    /// <summary>
    /// Everything <see cref="StandingEvaluator"/> needs, as plain lists.
    ///
    /// <para>Unlike <see cref="PlacementWorld"/> and <see cref="Military.SupplyNetwork"/>, this
    /// carries data rather than delegates, and the difference is not an inconsistency. Those two
    /// answer questions — the caller does not know in advance which province it will ask about, so
    /// a delegate is the only honest shape. A standing summary is a single pass over everything,
    /// once. Handing the evaluator a delegate it would immediately call for every element would buy
    /// nothing except the ability to be surprised by what the delegate does mid-walk.</para>
    ///
    /// <para>The provinces here are already filtered to the ones the faction stands in at all. The
    /// façade knows which those are — it has to walk the province list to find them either way —
    /// and passing the whole planet so the evaluator can skip most of it would make the pure layer
    /// pay for the impure layer's convenience.</para>
    /// </summary>
    public sealed class StandingWorld
    {
        /// <summary>Territorial holdings belonging to the faction being summarised.</summary>
        public List<StandingHolding> Holdings;

        /// <summary>Provinces the faction holds or contests. Provinces it has no stake in are omitted.</summary>
        public List<StandingProvince> Provinces;
    }
}
