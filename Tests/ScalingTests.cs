// Behaviour tests for the parts of ProductionScalingUtility that decide something.
//
// The façade itself mostly photographs the world and hands the numbers to ProductionEvaluator, and
// that photographing is not testable in a sandbox. Two things in it are not photography, and those
// are what this suite pins:
//
//   * SecurityOf — a genuinely new rule. R&T has no security subsystem, so Epic 3 derives security
//     from what the mod already measures: security is what your strongest rival has left you. If
//     that derivation is wrong, every contested province in the game produces the wrong number and
//     nothing else in the codebase would catch it.
//
//   * TryResolveResourceKind — the table that decides which pool an Empire resource draws on. A
//     wrong entry here silently charges mining output against the food supply.
//
// The last section is the one that matters most: it puts the two together and asserts the thing the
// child was actually asked for — that holding a province uncontested is worth more than sharing it.
using System;
using System.Collections.Generic;
using RimSynapse.RegionsAndTerritories;
using RimSynapse.Factions;
using RimWorld;
using RimSynapse.Factions.Economy;
using RimSynapse.RegionsAndTerritories.Economy;
using RimSynapse.RegionsAndTerritories.Sizing;
using Verse;

namespace ScalingTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Faction us = new Faction();
            Faction them = new Faction();
            Faction thirdParty = new Faction();

            Section("security is what your rivals have left you");

            Check("no data at all is not treated as safe", Near(ProductionScalingUtility.SecurityOf(null, us), 0f));
            Check("no faction is not treated as safe", Near(ProductionScalingUtility.SecurityOf(Data(), null), 0f));

            var aloneHere = Data(Score(us, 0.9f));
            Check("a province nobody contests is fully secure",
                Near(ProductionScalingUtility.SecurityOf(aloneHere, us), 1f));

            // Our own score is explicitly skipped. Owning a province harder must never make it read
            // as less secure — that inversion is the obvious way to get this derivation backwards.
            var dominant = Data(Score(us, 1.0f));
            Check("holding a province outright does not make it insecure",
                Near(ProductionScalingUtility.SecurityOf(dominant, us), 1f));

            var shared = Data(Score(us, 0.6f), Score(them, 0.4f));
            Check("a rival's share comes straight off ours",
                Near(ProductionScalingUtility.SecurityOf(shared, us), 0.6f));
            Check("and the same province reads differently from the other side",
                Near(ProductionScalingUtility.SecurityOf(shared, them), 0.4f));

            var threeWay = Data(Score(us, 0.3f), Score(them, 0.25f), Score(thirdParty, 0.45f));
            Check("only the strongest rival counts, not the sum of them",
                Near(ProductionScalingUtility.SecurityOf(threeWay, us), 0.55f));

            var overwhelmed = Data(Score(us, 0.05f), Score(them, 1.0f));
            Check("being comprehensively outmatched floors at zero, not below",
                Near(ProductionScalingUtility.SecurityOf(overwhelmed, us), 0f));

            var absent = Data(Score(them, 0.2f));
            Check("a faction with no presence still reads the province it is standing in",
                Near(ProductionScalingUtility.SecurityOf(absent, us), 0.8f));

            var ragged = Data(null, new FactionOwnershipScore(), Score(them, 0.5f));
            Check("null and factionless entries are skipped rather than thrown on",
                Near(ProductionScalingUtility.SecurityOf(ragged, us), 0.5f));

            Section("resource names resolve to the pool they actually draw on");

            Check("food draws on nutrition", Resolves("RTD_Food", ResourceKind.Nutrition));
            Check("animals draw on nutrition too", Resolves("RTD_Animals", ResourceKind.Nutrition));
            Check("logging draws on biomass", Resolves("RTD_Logging", ResourceKind.Biomass));
            Check("mining draws on minerals", Resolves("RTD_Mining", ResourceKind.Minerals));
            Check("apparel draws on textiles", Resolves("RTD_Apparel", ResourceKind.Textiles));
            Check("weapons draw on pre-industrial goods", Resolves("RTD_Weapons", ResourceKind.PreIndustrialGoods));
            Check("medicine draws on industrial goods", Resolves("RTD_Medicine", ResourceKind.IndustrialGoods));
            Check("research draws on spacer goods", Resolves("RTD_Research", ResourceKind.SpacerGoods));

            ResourceKind unused;
            Check("an unknown resource declines rather than guessing",
                !ProductionScalingUtility.TryResolveResourceKind("RTD_SomethingElse", out unused));
            Check("an empty name declines", !ProductionScalingUtility.TryResolveResourceKind("", out unused));
            Check("a null name declines", !ProductionScalingUtility.TryResolveResourceKind(null, out unused));

            Section("holding ground uncontested is worth more than sharing it");

            // The whole point of the child, expressed as one comparison: identical provinces,
            // identical stock, identical population — the only difference is who else is standing
            // there. This is what a player should be able to feel.
            var quiet = Data(Score(us, 0.8f));
            var contested = Data(Score(us, 0.8f), Score(them, 0.7f));

            float quietFactor = FactorWith(quiet, us);
            float contestedFactor = FactorWith(contested, us);

            Check("a secure province outproduces a contested one", quietFactor > contestedFactor);

            // With the insecurity penalty deliberately parked at zero, the worst a contested province
            // can do is earn none of the security bonus. Nothing about 0.7 may make an existing world
            // produce less than it did in 0.6 — that is the collapse-to-baseline rule, checked here at
            // the far end of the pipeline rather than only at the evaluator.
            var hopeless = Data(Score(us, 0.8f), Score(them, 1.0f));
            float baseline = ProductionEvaluator.Evaluate(
                new ResourcePool(500f), ResourceKind.Minerals, 900,
                ownershipScore: 0f, security: 0f,
                localRichness: 0f, provinceAverageRichness: 0f,
                tier: SettlementTier.None);
            Check("even a province about to be overrun produces no less than 0.6 would have",
                FactorWith(hopeless, us) >= baseline - 0.001f);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SCALING TESTS PASSED" : failures + " SCALING TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>The evaluator fed a real ownership picture, as the façade would feed it.</summary>
        private static float FactorWith(RegionalOwnershipData data, Faction faction)
        {
            return ProductionEvaluator.Evaluate(
                new ResourcePool(500f),
                ResourceKind.Minerals,
                surroundingPopulation: 900,
                ownershipScore: data.ScoreFor(faction),
                security: ProductionScalingUtility.SecurityOf(data, faction),
                localRichness: 0f,
                provinceAverageRichness: 0f,
                tier: SettlementTier.None);
        }

        private static bool Resolves(string defName, ResourceKind expected)
        {
            ResourceKind kind;
            return ProductionScalingUtility.TryResolveResourceKind(defName, out kind) && kind == expected;
        }

        private static FactionOwnershipScore Score(Faction faction, float total)
        {
            return new FactionOwnershipScore { faction = faction, settlementScore = total };
        }

        private static RegionalOwnershipData Data(params FactionOwnershipScore[] scores)
        {
            var data = new RegionalOwnershipData();
            data.factionScores = new List<FactionOwnershipScore>(scores);
            return data;
        }

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
