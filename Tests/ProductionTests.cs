// Behaviour tests for the 0.7 production model (Epic 3 children 1, 3, 4 — pure core).
//
// Split out of Regions-and-Territories' EconomyTests when production, taxation, sizing, military
// reach and standing moved to this mod. The resource capacity/depletion half stayed there, on
// GeographicProvince, because that is persisted world state; what moved is the model for what a
// faction extracts from ground it holds.
//
// The load-bearing property is the last section: with no new data, the model must reproduce 0.6's
// arithmetic exactly. Everything above it is behaviour that only appears once a caller actually
// has something to say.
using System;
using RimSynapse.Factions.Economy;
using RimSynapse.RegionsAndTerritories.Sizing;
using RimSynapse.RegionsAndTerritories.Economy;

namespace ProductionTests
{
    public static class Program
    {
        private static int failures;

        public static int Main()
        {
            // only appears once a caller actually has something to say.

            Section("abundance reproduces the 0.6 resource scale");
            Check("a province at its baseline is neutral",
                Near(ProductionEvaluator.AbundanceFactor(500f, 500f), 1f));
            Check("twice the baseline doubles output",
                Near(ProductionEvaluator.AbundanceFactor(1000f, 500f), 2f));
            Check("but the bonus is capped there",
                Near(ProductionEvaluator.AbundanceFactor(50000f, 500f), 2f));
            Check("a poor province floors rather than collapsing",
                Near(ProductionEvaluator.AbundanceFactor(1f, 500f), 0.2f));
            Check("an empty province still produces something",
                Near(ProductionEvaluator.AbundanceFactor(0f, 500f), 0.2f));
            Check("a null pool is survivable",
                Near(ProductionEvaluator.AbundanceFactor(null, ResourceKind.Minerals), 0.2f));

            // This is the depletion feedback loop closing: the same province, worked down, yields
            // less. Nothing before 0.7 could express this, because there was no "worked down".
            var seam = new ResourcePool(1000f);
            float rich = ProductionEvaluator.AbundanceFactor(seam, ResourceKind.Minerals);
            seam.Draw(800f);
            float spent = ProductionEvaluator.AbundanceFactor(seam, ResourceKind.Minerals);
            Check("working a province down reduces what it yields", spent < rich);
            Check("and a full one yields more than a poor one", rich > 1f && spent < 1f);

            Section("labour reproduces the 0.6 population curve");
            Check("an empty province sits below baseline", Near(ProductionEvaluator.LabourFactor(0), 0.8f));
            Check("population lifts it", Near(ProductionEvaluator.LabourFactor(1000), 1.3f));
            Check("and it caps", Near(ProductionEvaluator.LabourFactor(999999), 1.5f));
            Check("a negative headcount is treated as empty", Near(ProductionEvaluator.LabourFactor(-50), 0.8f));

            Section("security is a bonus for holding ground, never a tax");
            Check("owning nothing changes nothing", Near(ProductionEvaluator.SecurityFactor(0f, 1f), 1f));
            Check("owning nothing and policing nothing still changes nothing",
                Near(ProductionEvaluator.SecurityFactor(0f, 0f), 1f));
            Check("ownership below the placement threshold earns nothing",
                Near(ProductionEvaluator.SecurityFactor(0.29f, 1f), 1f));
            Check("full ownership fully secured earns the whole bonus",
                Near(ProductionEvaluator.SecurityFactor(1f, 1f), 1.25f));
            Check("the same territory unpoliced earns none of it",
                Near(ProductionEvaluator.SecurityFactor(1f, 0f), 1f));
            Check("policing ground you do not own earns nothing",
                Near(ProductionEvaluator.SecurityFactor(0.1f, 1f), 1f));
            Check("half-held and half-policed earns less than either alone would suggest",
                ProductionEvaluator.SecurityFactor(0.65f, 0.5f) < ProductionEvaluator.SecurityFactor(1f, 0.5f));
            Check("the factor never drops below 1 while the penalty is off",
                ProductionEvaluator.SecurityFactor(0f, 0f) >= 1f
                && ProductionEvaluator.SecurityFactor(0.5f, 0.1f) >= 1f);

            Section("locality tilts output without dictating it");
            Check("ground no better than the province average is neutral",
                Near(ProductionEvaluator.LocalityFactor(100f, 100f), 1f));
            Check("a caller with no survey loses nothing by passing zeroes",
                Near(ProductionEvaluator.LocalityFactor(0f, 0f), 1f));
            Check("twice-as-rich ground does not double output",
                Near(ProductionEvaluator.LocalityFactor(200f, 100f), 1.5f));
            Check("barren ground is a penalty, not a shutdown",
                Near(ProductionEvaluator.LocalityFactor(0f, 100f), 0.5f));
            Check("absurdly rich ground is still capped",
                Near(ProductionEvaluator.LocalityFactor(99999f, 100f), 1.5f));
            Check("richer ground always beats poorer ground",
                ProductionEvaluator.LocalityFactor(150f, 100f) > ProductionEvaluator.LocalityFactor(80f, 100f));

            Section("composition is bounded");
            Check("neutral everything is neutral",
                Near(ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.None), 1f));
            Check("the best case cannot run away",
                Near(ProductionEvaluator.Compose(2f, 1.5f, 1.25f, 1.5f, SettlementTier.MajorCity), 3f));
            Check("the worst case cannot reach zero",
                Near(ProductionEvaluator.Compose(0.2f, 0.8f, 1f, 0.5f, SettlementTier.None), 0.15f));
            Check("the best province is at most twenty times the worst",
                ProductionRules.MaxTotalFactor / ProductionRules.MinTotalFactor <= 20f);
            Check("an untiered holding is not zeroed by the tier scale",
                ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.None) > 0.9f);
            Check("a bigger settlement produces more, all else equal",
                ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.City)
                    > ProductionEvaluator.Compose(1f, 1f, 1f, 1f, SettlementTier.Village));

            Section("a world with no new data produces exactly what 0.6 produced");
            // 0.6 was: GetResourceScale(stock, baseline) * (0.8 + population / 2000), capped.
            // A caller that has no security survey, no per-tile richness and no tier passes the
            // neutral inputs below, and must land on that number to the digit -- otherwise every
            // existing save's economy shifts the moment 0.7 loads.
            var legacyPool = new ResourcePool(750f);
            float legacyExpected = (750f / 500f) * (0.8f + 900 * (1f / 2000f));
            float legacyActual = ProductionEvaluator.Evaluate(
                legacyPool, ResourceKind.Minerals,
                surroundingPopulation: 900,
                ownershipScore: 0f, security: 0f,
                localRichness: 0f, provinceAverageRichness: 0f,
                tier: SettlementTier.None);
            Check("the composed model reproduces the 0.6 figure exactly", Near(legacyActual, legacyExpected));

            float legacyPoor = ProductionEvaluator.Evaluate(
                new ResourcePool(0f), ResourceKind.Minerals, 0, 0f, 0f, 0f, 0f, SettlementTier.None);
            Check("and reproduces it at the floor too", Near(legacyPoor, 0.2f * 0.8f));

            Section("the model cannot produce a nonsense number");
            float worst = float.MaxValue, best = float.MinValue;
            for (int stock = 0; stock <= 4000; stock += 137)
            {
                for (int pop = 0; pop <= 5000; pop += 419)
                {
                    for (int own = 0; own <= 10; own += 3)
                    {
                        float f = ProductionEvaluator.Evaluate(
                            new ResourcePool(stock), ResourceKind.Minerals, pop,
                            own / 10f, own / 10f, own * 40f, 100f, SettlementTier.City);
                        if (f < worst) worst = f;
                        if (f > best) best = f;
                    }
                }
            }
            Check("every combination stays inside the declared band",
                worst >= ProductionRules.MinTotalFactor - 0.0001f
                && best <= ProductionRules.MaxTotalFactor + 0.0001f);
            Check("and the band is actually exercised, not just respected", best > worst * 2f);

            // Cross-layer, and the reason this suite compiles against R&T's Economy as well as its
            // own: extraction lives with the province's resource state in Regions and Territories,
            // the tier that drives it lives here. Neither repo can assert this alone, and it is
            // exactly the property a split like this one can silently break.
            Section("settlement tier drives how hard a holding draws");
            Check("a major city draws harder than a village at the same headcount",
                ResourceEvaluator.ExtractionPerYear(100, SettlementSizeRules.ProductionScale(SettlementTier.Village)) <
                ResourceEvaluator.ExtractionPerYear(100, SettlementSizeRules.ProductionScale(SettlementTier.MajorCity)));
            Check("an untiered holding still extracts at the neutral rate",
                Near(ResourceEvaluator.ExtractionPerYear(100, SettlementSizeRules.ProductionScale(SettlementTier.None)),
                     ResourceEvaluator.ExtractionPerYear(100, 1f)));
            Check("a major city sustains fewer people on the same ground than a village",
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, 10000f, 1f,
                    SettlementSizeRules.ProductionScale(SettlementTier.MajorCity)) <
                ResourceEvaluator.SustainablePopulation(ResourceKind.Minerals, 10000f, 1f,
                    SettlementSizeRules.ProductionScale(SettlementTier.Village)));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL PRODUCTION TESTS PASSED" : failures + " PRODUCTION TEST(S) FAILED");
            return failures == 0 ? 0 : 1;
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
