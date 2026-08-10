// Behaviour tests for Epic 3 child 6 — how much of a levy actually reaches the capital.
//
// Three things are worth pinning here, and only three:
//
//   * The collapse-to-baseline property. Every 0.7 model has it and this one is the most exposed:
//     tithes are money, and a player whose income moves when they update a mod will not read that
//     as a new feature.
//
//   * The asymmetry between this model and child 1's. Production's insecurity penalty is parked at
//     zero because it keys on derived security, where "no data" and "overrun" are the same number.
//     This one keys on rival pressure, where they are different numbers, which is the entire reason
//     it can afford a real penalty. If someone later "simplifies" this to take security instead,
//     these assertions are what should stop them.
//
//   * That growing a settlement is never a trap. Tiered production and tiered concession pull in
//     opposite directions on purpose, and the interesting property is a property of the product:
//     net collectible income must not fall when a town becomes a city.
using System;
using RimSynapse.Factions.Economy;
using RimSynapse.RegionsAndTerritories.Sizing;

namespace TaxationTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            Section("an untouched world remits exactly what it always did");

            Check("no ownership, no rival, no tier is exactly 1",
                Near(TaxationEvaluator.CollectionFactor(0f, 0f, SettlementTier.None), 1f));
            Check("a village concedes nothing, so a player colony is untouched",
                Near(TaxationEvaluator.CollectionFactor(0f, 0f, SettlementTier.Village), 1f));
            Check("ownership below the threshold earns nothing rather than losing something",
                Near(TaxationEvaluator.CollectionFactor(0.29f, 0f, SettlementTier.None), 1f));
            Check("ownership exactly at the threshold is still nothing",
                Near(TaxationEvaluator.CollectionFactor(TaxationRules.MinOwnershipForLoyalty, 0f, SettlementTier.None), 1f));

            Section("holding ground firmly is worth something, and not very much");

            Check("total ownership earns the full loyalty bonus",
                Near(TaxationEvaluator.LoyaltyBonus(1f), TaxationRules.MaxLoyaltyBonus));
            Check("and that is the ceiling the whole model can reach",
                Near(TaxationEvaluator.CollectionFactor(1f, 0f, SettlementTier.None), TaxationRules.MaxCollectionFactor));
            Check("half-way past the threshold earns half the bonus",
                Near(TaxationEvaluator.LoyaltyBonus(0.65f), TaxationRules.MaxLoyaltyBonus * 0.5f));
            Check("a score above 1 cannot buy more than the ceiling",
                Near(TaxationEvaluator.LoyaltyBonus(2f), TaxationRules.MaxLoyaltyBonus));
            Check("loyalty rises with ownership rather than jumping",
                TaxationEvaluator.LoyaltyBonus(0.9f) > TaxationEvaluator.LoyaltyBonus(0.6f));

            Section("a rival takes a cut, and a scout does not");

            Check("no rival intercepts nothing",
                Near(TaxationEvaluator.InterceptedFraction(0f), 0f));
            // The floor is the point of this pair. Without it a single wandering faction with a camp
            // in the corner of a province would tax an empire, and the model would twitch constantly.
            Check("a rival below the noise floor intercepts nothing",
                Near(TaxationEvaluator.InterceptedFraction(TaxationRules.RivalPressureFloor), 0f));
            Check("a rival just above the floor intercepts almost nothing",
                TaxationEvaluator.InterceptedFraction(TaxationRules.RivalPressureFloor + 0.01f) < 0.01f);
            Check("a rival holding the province outright intercepts the maximum",
                Near(TaxationEvaluator.InterceptedFraction(1f), TaxationRules.MaxInterceptedFraction));
            Check("interception rises with pressure",
                TaxationEvaluator.InterceptedFraction(0.8f) > TaxationEvaluator.InterceptedFraction(0.4f));
            Check("a contested province remits less than a quiet one",
                TaxationEvaluator.CollectionFactor(0.8f, 0.7f, SettlementTier.None)
                    < TaxationEvaluator.CollectionFactor(0.8f, 0f, SettlementTier.None));

            // This is the assertion that documents why child 6 has a penalty and child 1 does not.
            // Under child 1's derived-security formulation, an unmeasured province and an overrun one
            // both read 0 and would be punished identically. Here they are 0 pressure and 1 pressure.
            Check("no data and no rival are the same answer: full collection, not a penalty",
                Near(TaxationEvaluator.CollectionFactor(0f, 0f, SettlementTier.None), 1f)
                    && TaxationEvaluator.CollectionFactor(0f, 1f, SettlementTier.None) < 1f);

            Section("a great city bargains, a village does not");

            Check("no tier concedes nothing", Near(TaxationEvaluator.ConcessionFraction(SettlementTier.None), 0f));
            Check("a village concedes nothing", Near(TaxationEvaluator.ConcessionFraction(SettlementTier.Village), 0f));
            Check("a town concedes a little", Near(TaxationEvaluator.ConcessionFraction(SettlementTier.Town), TaxationRules.TownConcession));
            Check("a city concedes more", Near(TaxationEvaluator.ConcessionFraction(SettlementTier.City), TaxationRules.CityConcession));
            Check("a major city concedes most", Near(TaxationEvaluator.ConcessionFraction(SettlementTier.MajorCity), TaxationRules.MajorCityConcession));
            Check("concession rises strictly with tier",
                TaxationEvaluator.ConcessionFraction(SettlementTier.Village) < TaxationEvaluator.ConcessionFraction(SettlementTier.Town)
                && TaxationEvaluator.ConcessionFraction(SettlementTier.Town) < TaxationEvaluator.ConcessionFraction(SettlementTier.City)
                && TaxationEvaluator.ConcessionFraction(SettlementTier.City) < TaxationEvaluator.ConcessionFraction(SettlementTier.MajorCity));

            Section("growing a settlement is never a trap");

            // The property that matters is a property of the product, not of either half. Concession
            // is a counterweight to tiered production, and a counterweight that outweighs is a bug:
            // a player who upgrades a town into a city and watches their income fall has been
            // punished for playing the mod as designed.
            var ladder = new[]
            {
                SettlementTier.None, SettlementTier.Village, SettlementTier.Town,
                SettlementTier.City, SettlementTier.MajorCity
            };

            bool neverFalls = true;
            bool endsHigher;
            for (int i = 1; i < ladder.Length; i++)
            {
                if (Net(ladder[i]) < Net(ladder[i - 1]) - 0.0001f) neverFalls = false;
            }
            endsHigher = Net(SettlementTier.MajorCity) > Net(SettlementTier.Village);

            Check("net collectible income never falls as a settlement tiers up", neverFalls);
            Check("and a major city is worth clearly more to the treasury than a village", endsHigher);

            // Sublinear, though — the same anti-snowball property the production table has, measured
            // at the treasury where the player actually feels it. A major city produces 2.25x a
            // village; if it also remitted all of that, one capital would be the whole game.
            Check("but a major city is worth less than its raw production suggests",
                Net(SettlementTier.MajorCity) < SettlementSizeRules.ProductionScale(SettlementTier.MajorCity) - 0.0001f);
            Check("and less than twice a village, despite producing over twice as much",
                Net(SettlementTier.MajorCity) < 2f * Net(SettlementTier.Village));

            Section("the worst case is hard, not broken");

            float worst = TaxationEvaluator.CollectionFactor(0f, 1f, SettlementTier.MajorCity);
            Check("an overrun major city still remits something",
                worst > 0f && worst >= TaxationRules.MinCollectionFactor - 0.0001f);
            Check("and today's constants do not actually reach the floor",
                worst > TaxationRules.MinCollectionFactor);
            Check("nothing in the table can push past the ceiling",
                TaxationEvaluator.CollectionFactor(1f, 0f, SettlementTier.None) <= TaxationRules.MaxCollectionFactor + 0.0001f);
            Check("a negative ownership score is treated as none, not as a debt",
                Near(TaxationEvaluator.CollectionFactor(-1f, 0f, SettlementTier.None), 1f));
            Check("a negative rival score intercepts nothing",
                Near(TaxationEvaluator.CollectionFactor(0f, -1f, SettlementTier.None), 1f));

            Section("loyalty and interception are separate books");

            // Deliberate: ownership feeds loyalty, rival pressure feeds interception, and neither
            // reads the other. Requiring security for the loyalty bonus as well would charge one
            // contested province twice for the same rival.
            float loyalOnly = TaxationEvaluator.CollectionFactor(1f, 0f, SettlementTier.None);
            float loyalContested = TaxationEvaluator.CollectionFactor(1f, 1f, SettlementTier.None);
            Check("a firmly held province keeps its loyalty bonus even while contested",
                Near(loyalOnly - loyalContested, TaxationRules.MaxInterceptedFraction));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL TAXATION TESTS PASSED" : failures + " TAXATION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Net collectible for a tier with nothing else in play.</summary>
        private static float Net(SettlementTier tier)
        {
            return TaxationEvaluator.NetCollectible(0f, 0f, tier);
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
