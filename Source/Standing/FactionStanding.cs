using System;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Sizing;

namespace RimSynapse.Factions.Standing
{
    /// <summary>
    /// What Regions &amp; Territories knows about one faction's position on the world map, in the
    /// form another mod can read without knowing anything about provinces, adapters, or tiers.
    ///
    /// <para>This is Epic 6's published surface, and it is deliberately a <b>value</b> rather than a
    /// live view. Every field is a count or a total taken at one moment. A consumer that wants
    /// current numbers asks again; a consumer that caches this is caching a snapshot and knows it.
    /// The alternative — handing out an object that walks the world on every property read — is how
    /// a UI ends up doing a province sweep sixty times a second because somebody wrote a tooltip.</para>
    ///
    /// <para>Nothing inside R&amp;T reads this type. It exists to be consumed from outside, which
    /// means the cost of getting it wrong is paid in another repository, and that is the whole
    /// reason Epic 6 wants a real build in front of it before the shape is treated as settled.</para>
    /// </summary>
    public sealed class FactionStanding
    {
        /// <summary>Provinces the faction owns outright.</summary>
        public int HeldProvinces;

        /// <summary>Provinces the faction and a rival are within a hair of each other in.</summary>
        public int ContestedProvinces;

        /// <summary>
        /// World tiles inside the provinces the faction holds. Contested provinces do not count:
        /// this is meant to answer "how much of the map is theirs", and ground still being argued
        /// over is not yet an answer to that.
        /// </summary>
        public int TerritoryTiles;

        /// <summary>Total territorial holdings, of every kind.</summary>
        public int Holdings;

        /// <summary>Resident population across every holding that carries one.</summary>
        public int Population;

        /// <summary>
        /// The largest settlement tier the faction has anywhere, or
        /// <see cref="SettlementTier.None"/> if it has no tiered holding — which includes every
        /// faction when settlement tiers are switched off.
        /// </summary>
        public SettlementTier HighestTier = SettlementTier.None;

        /// <summary>
        /// A relative measure of how strong the faction looks from the map alone, or 0 for a
        /// faction that holds nothing. Unitless and only meaningful against another faction's
        /// figure from the same world.
        ///
        /// <para>R&amp;T never consumes this. It is offered because a faction-strength model
        /// elsewhere would otherwise have to re-derive it from the counts above and would derive it
        /// slightly differently every time. <see cref="StandingRules"/> holds the weights.</para>
        /// </summary>
        public float PerceivedStrength;

        private readonly int[] holdingsByKind = new int[8];
        private readonly int[] holdingsByTier = new int[5];

        /// <summary>A faction with no position on the map at all. Also what a missing world returns.</summary>
        public static FactionStanding Empty
        {
            get { return new FactionStanding(); }
        }

        /// <summary>Holdings of one kind. Non-territorial kinds always answer zero.</summary>
        public int CountOfKind(WorldObjectKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= holdingsByKind.Length) return 0;
            return holdingsByKind[index];
        }

        /// <summary>Holdings at one tier. <see cref="SettlementTier.None"/> counts the untiered.</summary>
        public int CountOfTier(SettlementTier tier)
        {
            int index = (int)tier;
            if (index < 0 || index >= holdingsByTier.Length) return 0;
            return holdingsByTier[index];
        }

        /// <summary>Does the faction have any presence on the map worth reporting?</summary>
        public bool HasPresence
        {
            get { return Holdings > 0 || HeldProvinces > 0 || ContestedProvinces > 0; }
        }

        internal void Record(WorldObjectKind kind, SettlementTier tier)
        {
            int kindIndex = (int)kind;
            if (kindIndex >= 0 && kindIndex < holdingsByKind.Length) holdingsByKind[kindIndex]++;

            int tierIndex = (int)tier;
            if (tierIndex >= 0 && tierIndex < holdingsByTier.Length) holdingsByTier[tierIndex]++;

            if ((int)tier > (int)HighestTier) HighestTier = tier;

            Holdings++;
        }

        public override string ToString()
        {
            return string.Format(
                "FactionStanding(holdings {0}, held {1}, contested {2}, tiles {3}, pop {4}, top {5}, strength {6:0.##})",
                Holdings, HeldProvinces, ContestedProvinces, TerritoryTiles, Population, HighestTier, PerceivedStrength);
        }
    }
}
