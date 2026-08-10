// Behaviour tests for the 0.7 settlement size-tier layer (Epic 4).
//
// SettlementSizeEvaluator is pure, so this suite needs no RimWorld: everything under test is
// arithmetic over plain numbers. What is being checked is that the thresholds line up with the
// populations the game actually generates, that the tier can never go backwards as a settlement
// grows, and that the tier effects cannot silently zero an economy.
using System;
using RimSynapse.RegionsAndTerritories.Integration;
using RimSynapse.RegionsAndTerritories.Sizing;

namespace SizingTests
{
    public static class Program
    {
        private static int failures;

        private const WorldObjectKind Town_ = WorldObjectKind.Settlement;

        public static int Main()
        {
            Section("population thresholds");
            Check("nobody is not a settlement", SettlementSizeEvaluator.FromPopulation(0) == SettlementTier.None);
            Check("a negative population is not a settlement", SettlementSizeEvaluator.FromPopulation(-5) == SettlementTier.None);
            Check("one resident is a village", SettlementSizeEvaluator.FromPopulation(1) == SettlementTier.Village);
            Check("39 is still a village", SettlementSizeEvaluator.FromPopulation(39) == SettlementTier.Village);
            Check("40 is a town", SettlementSizeEvaluator.FromPopulation(40) == SettlementTier.Town);
            Check("79 is still a town", SettlementSizeEvaluator.FromPopulation(79) == SettlementTier.Town);
            Check("80 is a city", SettlementSizeEvaluator.FromPopulation(80) == SettlementTier.City);
            Check("139 is still a city", SettlementSizeEvaluator.FromPopulation(139) == SettlementTier.City);
            Check("140 is a major city", SettlementSizeEvaluator.FromPopulation(140) == SettlementTier.MajorCity);

            Section("thresholds match the populations the game actually generates");
            // PopulationDensityUtility seeds NPC settlements from faction tech level + random(-10,+20),
            // and reports live FreeColonistsCount for the player. If these drift apart, the tiers stop
            // describing the world.
            Check("a player colony of 12 is a village", SettlementSizeEvaluator.FromPopulation(12) == SettlementTier.Village);
            Check("a neolithic settlement (50-79) is a town",
                SettlementSizeEvaluator.FromPopulation(50) == SettlementTier.Town &&
                SettlementSizeEvaluator.FromPopulation(79) == SettlementTier.Town);
            Check("an industrial settlement (80-109) is a city",
                SettlementSizeEvaluator.FromPopulation(80) == SettlementTier.City &&
                SettlementSizeEvaluator.FromPopulation(109) == SettlementTier.City);
            Check("a spacer settlement (140-169) is a major city",
                SettlementSizeEvaluator.FromPopulation(140) == SettlementTier.MajorCity &&
                SettlementSizeEvaluator.FromPopulation(169) == SettlementTier.MajorCity);

            Section("a settlement never shrinks a tier by growing");
            bool monotonic = true;
            for (int pop = 0; pop < 400; pop++)
            {
                if ((int)SettlementSizeEvaluator.FromPopulation(pop + 1) < (int)SettlementSizeEvaluator.FromPopulation(pop))
                {
                    monotonic = false;
                    break;
                }
            }
            Check("tier is monotonic in population", monotonic);

            Section("dwellings stand in for an unknown population");
            Check("20 dwellings is a town", SettlementSizeEvaluator.FromDwellings(20) == SettlementTier.Town);
            Check("no dwellings is no settlement", SettlementSizeEvaluator.FromDwellings(0) == SettlementTier.None);
            Check("dwellings agree with the population they represent",
                SettlementSizeEvaluator.FromDwellings(45) == SettlementSizeEvaluator.FromPopulation(90));

            Section("a mod's own upgrade level maps onto a tier");
            Check("level 0 says nothing", SettlementSizeEvaluator.FromLevel(0, 5) == SettlementTier.None);
            Check("an unknown maximum says nothing", SettlementSizeEvaluator.FromLevel(3, 0) == SettlementTier.None);
            Check("the first level is a village", SettlementSizeEvaluator.FromLevel(1, 5) == SettlementTier.Village);
            Check("the middle level is a town", SettlementSizeEvaluator.FromLevel(3, 5) == SettlementTier.Town);
            Check("the second-highest is a city", SettlementSizeEvaluator.FromLevel(4, 5) == SettlementTier.City);
            Check("fully upgraded earns major city", SettlementSizeEvaluator.FromLevel(5, 5) == SettlementTier.MajorCity);
            Check("a single-level mod is maxed at level 1", SettlementSizeEvaluator.FromLevel(1, 1) == SettlementTier.MajorCity);
            Check("a level past the maximum clamps", SettlementSizeEvaluator.FromLevel(9, 5) == SettlementTier.MajorCity);

            bool levelMonotonic = true;
            for (int lvl = 1; lvl < 12; lvl++)
            {
                if ((int)SettlementSizeEvaluator.FromLevel(lvl + 1, 12) < (int)SettlementSizeEvaluator.FromLevel(lvl, 12))
                {
                    levelMonotonic = false;
                    break;
                }
            }
            Check("tier is monotonic in level", levelMonotonic);

            Section("only population centres get a tier");
            Check("settlements may reach the top", SettlementSizeEvaluator.MaxTierFor(WorldObjectKind.Settlement) == SettlementTier.MajorCity);
            Check("outposts are capped at town", SettlementSizeEvaluator.MaxTierFor(WorldObjectKind.Outpost) == SettlementTier.Town);
            Check("camps have no tier", SettlementSizeEvaluator.MaxTierFor(WorldObjectKind.Camp) == SettlementTier.None);
            Check("military installations have no tier", SettlementSizeEvaluator.MaxTierFor(WorldObjectKind.Military) == SettlementTier.None);
            Check("caravans have no tier", SettlementSizeEvaluator.MaxTierFor(WorldObjectKind.Caravan) == SettlementTier.None);
            Check("unclassified objects have no tier", SettlementSizeEvaluator.MaxTierFor(WorldObjectKind.Unknown) == SettlementTier.None);

            Section("the ceiling actually binds");
            Check("a huge outpost is still only a town",
                SettlementSizeEvaluator.Classify(WorldObjectKind.Outpost, 500) == SettlementTier.Town);
            Check("a fully upgraded outpost is still only a town",
                SettlementSizeEvaluator.Classify(WorldObjectKind.Outpost, 0, 0, 5, 5) == SettlementTier.Town);
            Check("a huge camp is still nothing",
                SettlementSizeEvaluator.Classify(WorldObjectKind.Camp, 500) == SettlementTier.None);

            Section("population and level are combined by taking the larger");
            Check("a maxed colony with few pawns is a major city",
                SettlementSizeEvaluator.Classify(Town_, 5, 0, 5, 5) == SettlementTier.MajorCity);
            Check("a huge settlement in a mod with no levels is judged on headcount",
                SettlementSizeEvaluator.Classify(Town_, 150, 0, 0, 0) == SettlementTier.MajorCity);
            Check("a populous but barely-upgraded colony is judged on headcount",
                SettlementSizeEvaluator.Classify(Town_, 100, 0, 1, 5) == SettlementTier.City);
            Check("neither source means no tier",
                SettlementSizeEvaluator.Classify(Town_, 0, 0, 0, 0) == SettlementTier.None);
            Check("dwellings are used only when population is unknown",
                SettlementSizeEvaluator.Classify(Town_, 0, 45, 0, 0) == SettlementTier.City);

            // Several adapter profiles (VOE, World Domination) can read a level but declare
            // assumedMaxLevel = 0, because nobody has pinned their maximum against a live install.
            // A numerator with no denominator must be dropped, not guessed at — otherwise a level-2
            // outpost in a twenty-level mod gets promoted on no evidence.
            Check("a level with no known maximum falls back to headcount",
                SettlementSizeEvaluator.Classify(Town_, 45, 0, 7, 0) == SettlementTier.Town);
            Check("a level with no known maximum cannot promote an empty holding",
                SettlementSizeEvaluator.Classify(Town_, 0, 0, 7, 0) == SettlementTier.None);

            Section("tier effects cannot zero an economy");
            // The trap this guards: production is a multiplier, so an untiered holding must come
            // back as a neutral 1. Returning 0 would wipe out every camp and every holding in the
            // world the moment tiers are switched off.
            Check("no tier means a neutral production multiplier",
                Math.Abs(SettlementSizeRules.ProductionScale(SettlementTier.None) - 1f) < 0.0001f);
            Check("no tier means no footprint", SettlementSizeRules.TerritoryFootprint(SettlementTier.None) == 0);
            Check("no tier means no imposed capacity", SettlementSizeRules.PopulationCapacity(SettlementTier.None) == 0);

            Section("tier effects are ordered and restrained");
            Check("production rises with tier",
                SettlementSizeRules.ProductionScale(SettlementTier.Village) < SettlementSizeRules.ProductionScale(SettlementTier.Town) &&
                SettlementSizeRules.ProductionScale(SettlementTier.Town) < SettlementSizeRules.ProductionScale(SettlementTier.City) &&
                SettlementSizeRules.ProductionScale(SettlementTier.City) < SettlementSizeRules.ProductionScale(SettlementTier.MajorCity));

            // The anti-snowball property: a major city holds ~3.5x a town's minimum headcount but
            // must not produce 3.5x as much, or the optimal play is one capital and nothing else.
            float productionRatio = SettlementSizeRules.ProductionScale(SettlementTier.MajorCity) / SettlementSizeRules.ProductionScale(SettlementTier.Village);
            float populationRatio = SettlementSizeRules.MajorCityMinPopulation / (float)SettlementSizeRules.TownMinPopulation;
            Check("production scales sublinearly against population", productionRatio < populationRatio);

            Check("footprint never shrinks as tier rises",
                SettlementSizeRules.TerritoryFootprint(SettlementTier.Village) <= SettlementSizeRules.TerritoryFootprint(SettlementTier.Town) &&
                SettlementSizeRules.TerritoryFootprint(SettlementTier.Town) <= SettlementSizeRules.TerritoryFootprint(SettlementTier.City) &&
                SettlementSizeRules.TerritoryFootprint(SettlementTier.City) <= SettlementSizeRules.TerritoryFootprint(SettlementTier.MajorCity));

            Section("capacity agrees with the thresholds");
            // A settlement at its tier's capacity should be exactly at the next tier's doorstep,
            // otherwise growth either stalls below a promotion or skips past one.
            Check("a full village is exactly a town",
                SettlementSizeRules.PopulationCapacity(SettlementTier.Village) == SettlementSizeRules.MinPopulationFor(SettlementTier.Town));
            Check("a full town is exactly a city",
                SettlementSizeRules.PopulationCapacity(SettlementTier.Town) == SettlementSizeRules.MinPopulationFor(SettlementTier.City));
            Check("a full city is exactly a major city",
                SettlementSizeRules.PopulationCapacity(SettlementTier.City) == SettlementSizeRules.MinPopulationFor(SettlementTier.MajorCity));
            Check("a major city has room beyond its threshold",
                SettlementSizeRules.PopulationCapacity(SettlementTier.MajorCity) > SettlementSizeRules.MajorCityMinPopulation);

            Section("tier comparison helpers");
            Check("a city is at least a town", SettlementTier.City.IsAtLeast(SettlementTier.Town));
            Check("a town is not at least a city", !SettlementTier.Town.IsAtLeast(SettlementTier.City));
            Check("a tier is at least itself", SettlementTier.Town.IsAtLeast(SettlementTier.Town));
            Check("max picks the larger", SettlementTier.Village.Max(SettlementTier.City) == SettlementTier.City);
            Check("max is commutative", SettlementTier.City.Max(SettlementTier.Village) == SettlementTier.Village.Max(SettlementTier.City));

            Section("every tier has a label");
            bool labelled = true;
            foreach (SettlementTier t in Enum.GetValues(typeof(SettlementTier)))
            {
                if (string.IsNullOrEmpty(t.Label()) || string.IsNullOrEmpty(t.LabelCapitalized())) labelled = false;
            }
            Check("labels are present for all tiers", labelled);
            Check("the village label reads naturally", SettlementTier.Village.Label() == "village");
            Check("the major city label reads naturally", SettlementTier.MajorCity.Label() == "major city");

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SIZING TESTS PASSED" : failures + " SIZING TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
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
