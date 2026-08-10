// Behaviour tests for Epic 6 child 1 — the per-faction summary R&T publishes for other mods, and
// child 2's optional perceived-strength figure.
//
// StandingEvaluator is pure by design, so this suite needs no RimWorld: holdings are plain records,
// provinces are a control plus a tile count, and factions never appear at all because the façade has
// already resolved which holdings belong to whom.
//
// Four things are worth pinning here, and they are pinned because this is the only surface in 0.7
// whose consumer lives in another repository and therefore cannot read the source to find out what
// was meant:
//
//   * That the counts agree with each other. Holdings has to equal the sum of CountOfKind, and held
//     plus contested provinces can never exceed the provinces the faction was actually given. A
//     consumer that finds two of these disagreeing has no way to know which one to believe.
//
//   * That a missing tier costs nothing. An untiered holding — from a mod that exposes no level, or
//     from any holding at all with settlement tiers switched off — is worth exactly its kind weight.
//     This is the collapse-to-baseline rule every 0.7 factor obeys, and here it is what stops a
//     settings toggle from silently demoting every faction on the map.
//
//   * That nothing non-territorial leaks in. A caravan is not a small holding; it is not a holding.
//
//   * That the strength figure is reproducible from the published counts. It has to be, because a
//     consumer that disagrees with the weights should be able to recompute the score without a live
//     world and without R&T's cooperation.
using System;
using System.Collections.Generic;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Placement;
using RimSynapse.RegionsAndTerritories.Sizing;
using RimSynapse.Factions.Standing;

namespace StandingTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("a faction with nothing on the map");

            FactionStanding nothing = StandingEvaluator.Evaluate(null);

            Check("a missing world summarises to empty rather than to null",
                nothing != null);
            Check("with no holdings",
                nothing.Holdings == 0);
            Check("no territory",
                nothing.HeldProvinces == 0 && nothing.ContestedProvinces == 0 && nothing.TerritoryTiles == 0);
            Check("no tier",
                nothing.HighestTier == SettlementTier.None);
            Check("no strength",
                Near(nothing.PerceivedStrength, 0f));
            Check("and says so",
                !nothing.HasPresence);

            FactionStanding empty = StandingEvaluator.Evaluate(new StandingWorld());

            Check("an empty world reads the same as a missing one",
                empty.Holdings == 0 && !empty.HasPresence && Near(empty.PerceivedStrength, 0f));
            Check("a world of empty lists reads the same again",
                Near(Evaluate(new StandingHolding[0], new StandingProvince[0]).PerceivedStrength, 0f));

            Section("holdings are counted by kind");

            FactionStanding mixed = Evaluate(
                new[]
                {
                    Holding(WorldObjectKind.Settlement, SettlementTier.Town, 40),
                    Holding(WorldObjectKind.Settlement, SettlementTier.Village, 12),
                    Holding(WorldObjectKind.Outpost, SettlementTier.None, 6),
                    Holding(WorldObjectKind.Military, SettlementTier.None, 10),
                    Holding(WorldObjectKind.Camp, SettlementTier.None, 3)
                },
                new StandingProvince[0]);

            Check("every territorial kind is a holding",
                mixed.Holdings == 5);
            Check("settlements are counted as settlements",
                mixed.CountOfKind(WorldObjectKind.Settlement) == 2);
            Check("outposts as outposts",
                mixed.CountOfKind(WorldObjectKind.Outpost) == 1);
            Check("military installations as their own kind",
                mixed.CountOfKind(WorldObjectKind.Military) == 1);
            Check("camps as camps",
                mixed.CountOfKind(WorldObjectKind.Camp) == 1);
            Check("and the kinds add up to the total",
                mixed.CountOfKind(WorldObjectKind.Settlement)
                + mixed.CountOfKind(WorldObjectKind.Outpost)
                + mixed.CountOfKind(WorldObjectKind.Military)
                + mixed.CountOfKind(WorldObjectKind.Camp) == mixed.Holdings);
            Check("population is the sum across every holding that carries one",
                mixed.Population == 71);
            Check("the highest tier anywhere is the one reported",
                mixed.HighestTier == SettlementTier.Town);
            Check("tiers add up to the total too",
                mixed.CountOfTier(SettlementTier.None)
                + mixed.CountOfTier(SettlementTier.Village)
                + mixed.CountOfTier(SettlementTier.Town) == mixed.Holdings);

            Section("what is not a holding stays out");

            FactionStanding noise = Evaluate(
                new[]
                {
                    Holding(WorldObjectKind.Settlement, SettlementTier.Village, 20),
                    Holding(WorldObjectKind.Caravan, SettlementTier.None, 8),
                    Holding(WorldObjectKind.Site, SettlementTier.None, 5),
                    Holding(WorldObjectKind.Unknown, SettlementTier.None, 5),
                    Holding(WorldObjectKind.Ignored, SettlementTier.None, 5)
                },
                new StandingProvince[0]);

            Check("a caravan is not a holding",
                noise.CountOfKind(WorldObjectKind.Caravan) == 0);
            Check("neither is a quest site",
                noise.CountOfKind(WorldObjectKind.Site) == 0);
            Check("nor anything unclassified",
                noise.CountOfKind(WorldObjectKind.Unknown) == 0);
            Check("so only the settlement is counted",
                noise.Holdings == 1);
            Check("and only the settlement's people are counted",
                noise.Population == 20);
            Check("a null in the list is skipped rather than thrown over",
                Evaluate(new StandingHolding[] { null, Holding(WorldObjectKind.Settlement, SettlementTier.None, 0) },
                    new StandingProvince[0]).Holdings == 1);

            Section("territory is ground you own, not ground you are still arguing over");

            FactionStanding ground = Evaluate(
                new StandingHolding[0],
                new[]
                {
                    Province(ProvinceControl.Held, 30),
                    Province(ProvinceControl.Held, 20),
                    Province(ProvinceControl.Contested, 100)
                });

            Check("held provinces are counted",
                ground.HeldProvinces == 2);
            Check("contested provinces are counted separately",
                ground.ContestedProvinces == 1);
            Check("territory is the tiles of the ground held outright",
                ground.TerritoryTiles == 50);
            Check("a contested province adds no territory however large it is",
                ground.TerritoryTiles == 50);
            Check("territory alone is a presence",
                ground.HasPresence);
            Check("a null province is skipped",
                Evaluate(new StandingHolding[0], new StandingProvince[] { null, Province(ProvinceControl.Held, 7) })
                    .TerritoryTiles == 7);

            Section("a missing tier costs a holding nothing");

            // The collapse-to-baseline rule, seen from Epic 6. This is the case that occurs when
            // settlement tiers are switched off, and when a mod exposes no upgrade level at all.
            float untieredSettlement = Evaluate(
                new[] { Holding(WorldObjectKind.Settlement, SettlementTier.None, 0) },
                new StandingProvince[0]).PerceivedStrength;

            Check("an untiered settlement is worth exactly its kind weight",
                Near(untieredSettlement, StandingRules.KindWeight(WorldObjectKind.Settlement)));
            Check("which is what a village is worth too",
                Near(untieredSettlement, StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Village)));
            Check("so switching tiers off cannot demote a faction",
                Near(StandingRules.TierStrength(SettlementTier.None), 1f));
            Check("and it still counts as a holding",
                Evaluate(new[] { Holding(WorldObjectKind.Settlement, SettlementTier.None, 0) }, new StandingProvince[0])
                    .Holdings == 1);
            Check("with its tier recorded as None rather than dropped",
                Evaluate(new[] { Holding(WorldObjectKind.Settlement, SettlementTier.None, 0) }, new StandingProvince[0])
                    .CountOfTier(SettlementTier.None) == 1);

            Section("bigger is worth more, and not proportionally more");

            Check("a town outweighs a village",
                StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Town)
                > StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Village));
            Check("a city outweighs a town",
                StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.City)
                > StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Town));
            Check("a major city outweighs a city",
                StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.MajorCity)
                > StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.City));
            Check("but a city is not worth four villages",
                StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.City)
                < 4f * StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Village));
            Check("a settlement outweighs a military installation",
                StandingRules.KindWeight(WorldObjectKind.Settlement) > StandingRules.KindWeight(WorldObjectKind.Military));
            Check("which outweighs an outpost",
                StandingRules.KindWeight(WorldObjectKind.Military) > StandingRules.KindWeight(WorldObjectKind.Outpost));
            Check("which outweighs a camp",
                StandingRules.KindWeight(WorldObjectKind.Outpost) > StandingRules.KindWeight(WorldObjectKind.Camp));
            Check("and a caravan is worth nothing at all",
                Near(StandingRules.HoldingStrength(WorldObjectKind.Caravan, SettlementTier.MajorCity), 0f));

            Section("the score is reproducible from the published counts");

            StandingHolding[] oneSettlement = { Holding(WorldObjectKind.Settlement, SettlementTier.None, 0) };

            float bare = Evaluate(oneSettlement, new StandingProvince[0]).PerceivedStrength;
            float withHeld = Evaluate(oneSettlement, new[] { Province(ProvinceControl.Held, 10) }).PerceivedStrength;
            float withContested = Evaluate(oneSettlement, new[] { Province(ProvinceControl.Contested, 10) }).PerceivedStrength;

            Check("a held province adds its weight",
                Near(withHeld - bare, StandingRules.HeldProvinceWeight));
            Check("a contested one adds half",
                Near(withContested - bare, StandingRules.ContestedProvinceWeight));
            Check("and half is really half",
                Near(StandingRules.ContestedProvinceWeight * 2f, StandingRules.HeldProvinceWeight));
            Check("territory tiles do not move the score by themselves",
                Near(Evaluate(oneSettlement, new[] { Province(ProvinceControl.Held, 10) }).PerceivedStrength,
                     Evaluate(oneSettlement, new[] { Province(ProvinceControl.Held, 900) }).PerceivedStrength));

            FactionStanding populous = Evaluate(
                new[] { Holding(WorldObjectKind.Settlement, SettlementTier.None, StandingRules.PopulationPerStrengthPoint * 2) },
                new StandingProvince[0]);

            Check("population adds one point per bracket",
                Near(populous.PerceivedStrength - bare, 2f));
            Check("the whole score can be rebuilt from the counts and the table",
                Near(mixed.PerceivedStrength, Recompute(mixed,
                    StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Town)
                    + StandingRules.HoldingStrength(WorldObjectKind.Settlement, SettlementTier.Village)
                    + StandingRules.HoldingStrength(WorldObjectKind.Outpost, SettlementTier.None)
                    + StandingRules.HoldingStrength(WorldObjectKind.Military, SettlementTier.None)
                    + StandingRules.HoldingStrength(WorldObjectKind.Camp, SettlementTier.None))));
            Check("and the score is never negative",
                StandingRules.Strength(nothing, 0f) >= 0f);
            Check("a null standing scores zero rather than throwing",
                Near(StandingRules.Strength(null, 5f), 0f));

            Section("an empire outranks a homestead");

            FactionStanding empire = Evaluate(
                new[]
                {
                    Holding(WorldObjectKind.Settlement, SettlementTier.MajorCity, 180),
                    Holding(WorldObjectKind.Settlement, SettlementTier.City, 90),
                    Holding(WorldObjectKind.Military, SettlementTier.None, 15)
                },
                new[] { Province(ProvinceControl.Held, 40), Province(ProvinceControl.Held, 35), Province(ProvinceControl.Contested, 20) });

            FactionStanding homestead = Evaluate(
                new[] { Holding(WorldObjectKind.Settlement, SettlementTier.Village, 8) },
                new[] { Province(ProvinceControl.Held, 30) });

            Check("the empire scores higher",
                empire.PerceivedStrength > homestead.PerceivedStrength);
            Check("and the homestead still scores above nothing",
                homestead.PerceivedStrength > nothing.PerceivedStrength);
            Check("the empire's top tier is its largest holding",
                empire.HighestTier == SettlementTier.MajorCity);
            Check("its territory is the held ground only",
                empire.TerritoryTiles == 75);
            Check("and both have a presence",
                empire.HasPresence && homestead.HasPresence);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL STANDING TESTS PASSED" : failures + " STANDING TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        // -- fixtures ---------------------------------------------------------

        private static StandingHolding Holding(WorldObjectKind kind, SettlementTier tier, int population)
        {
            return new StandingHolding(kind, tier, population, "faction");
        }

        private static StandingProvince Province(ProvinceControl control, int tiles)
        {
            return new StandingProvince(control, tiles);
        }

        private static FactionStanding Evaluate(StandingHolding[] holdings, StandingProvince[] provinces)
        {
            return StandingEvaluator.Evaluate(new StandingWorld
            {
                Holdings = new List<StandingHolding>(holdings),
                Provinces = new List<StandingProvince>(provinces)
            });
        }

        /// The score as a consumer in another repository would rebuild it: from the published
        /// counts and the published table, without touching the evaluator.
        private static float Recompute(FactionStanding standing, float holdingTotal)
        {
            return holdingTotal
                + standing.HeldProvinces * StandingRules.HeldProvinceWeight
                + standing.ContestedProvinces * StandingRules.ContestedProvinceWeight
                + standing.Population / (float)StandingRules.PopulationPerStrengthPoint;
        }

        // -- harness ----------------------------------------------------------

        private static bool Near(float a, float b, float tolerance = 0.001f)
        {
            return Math.Abs(a - b) < tolerance;
        }

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + label);
        }
    }
}
