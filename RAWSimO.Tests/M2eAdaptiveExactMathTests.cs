using System;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control.Defaults.OrderBatching;

namespace RAWSimO.Tests
{
    public static class M2eAdaptiveExactMathTests
    {
        public static void Register()
        {
            TestRunner.Add("M2e-AE completion dominates pod pile-on", CompletionDominatesLowerLevels);
            TestRunner.Add("M2e-AE slot-efficient completion dominates raw completion", EfficientCompletionDominatesRawCompletion);
            TestRunner.Add("M2e-AE completed pod-order pile-on dominates lower levels", FocusOrdersDominateLowerLevels);
            TestRunner.Add("M2e-AE new-pod order pile-on dominates station parts", NewPodOrdersDominateLowerLevels);
            TestRunner.Add("M2e-AE fewer station parts dominates pod items", StationPartsDominateFocusUnits);
            TestRunner.Add("M2e-AE pod-item pile-on dominates generic items", FocusUnitsDominateLowerLevels);
            TestRunner.Add("M2e-AE urgency dominates pod items", UrgencyDominatesLowerLevels);
            TestRunner.Add("M2e-AE generic items break final tie", ItemsBreakFinalTie);
            TestRunner.Add("M2e-AE rejects negative hierarchy bounds", NegativeBoundsAreRejected);
            TestRunner.Add("M2e-AE replenishes behind a processing pod", ProcessingPodDoesNotCountAsFutureSupply);
            TestRunner.Add("M2e-AE keeps one future pod in pipeline", FuturePodSuppressesReplenishment);
            TestRunner.Add("M2e-AE PS ranks completion before coverage and distance", PodSelectionIsLexicographic);
            TestRunner.Add("M2e-AE configuration defaults off", ConfigurationDefaultsOff);
        }

        private static M2eAdaptiveExactMath.Weights Weights()
        {
            return M2eAdaptiveExactMath.BuildWeights(8, 20, 6, 36);
        }

        private static void CompletionDominatesLowerLevels()
        {
            var w = Weights();
            long moreWorst = M2eAdaptiveExactMath.Score(1, 1, 0, 0, 0, 6, 0, w);
            long fewerBest = M2eAdaptiveExactMath.Score(1, 0, 8, 20, 36, 0, 20, w);
            TestRunner.AssertTrue(moreWorst > fewerBest, "one completion must dominate every pile-on tie-break");
        }

        private static void EfficientCompletionDominatesRawCompletion()
        {
            var w = Weights();
            long oneEfficient = M2eAdaptiveExactMath.Score(1, 1, 0, 0, 0, 1, 0, w);
            long manyInefficient = M2eAdaptiveExactMath.Score(0, 8, 8, 20, 36, 0, 20, w);
            TestRunner.AssertTrue(oneEfficient > manyInefficient,
                "one net completion after extra station parts must dominate raw allocated completions");
        }

        private static void FocusOrdersDominateLowerLevels()
        {
            var w = Weights();
            long moreWorst = M2eAdaptiveExactMath.Score(2, 2, 1, 0, 0, 0, 6, 0, w);
            long fewerBest = M2eAdaptiveExactMath.Score(2, 2, 0, 8, 20, 36, 0, 20, w);
            TestRunner.AssertTrue(moreWorst > fewerBest, "one focus-pod order must dominate item-level terms");
        }

        private static void NewPodOrdersDominateLowerLevels()
        {
            var w = Weights();
            long moreWorst = M2eAdaptiveExactMath.Score(2, 2, 2, 1, 0, 0, 6, 0, w);
            long fewerBest = M2eAdaptiveExactMath.Score(2, 2, 2, 0, 20, 36, 0, 20, w);
            TestRunner.AssertTrue(moreWorst > fewerBest,
                "one selected-pod completed order must dominate station parts and item levels");
        }

        private static void FocusUnitsDominateLowerLevels()
        {
            var w = Weights();
            long moreWorst = M2eAdaptiveExactMath.Score(2, 2, 2, 1, 0, 2, 0, w);
            long fewerBest = M2eAdaptiveExactMath.Score(2, 2, 2, 0, 0, 2, 20, w);
            TestRunner.AssertTrue(moreWorst > fewerBest, "one focus-pod item must dominate generic items");
        }

        private static void UrgencyDominatesLowerLevels()
        {
            var w = Weights();
            long moreWorst = M2eAdaptiveExactMath.Score(2, 2, 2, 0, 1, 2, 0, w);
            long fewerBest = M2eAdaptiveExactMath.Score(2, 2, 2, 20, 0, 2, 20, w);
            TestRunner.AssertTrue(moreWorst > fewerBest,
                "one urgency point must dominate all pod-item and generic-item gains");
        }

        private static void StationPartsDominateFocusUnits()
        {
            var w = Weights();
            long fewer = M2eAdaptiveExactMath.Score(2, 2, 2, 0, 0, 1, 0, w);
            long more = M2eAdaptiveExactMath.Score(2, 2, 2, 20, 36, 2, 20, w);
            TestRunner.AssertTrue(fewer > more, "one saved station part must dominate all item-level gains");
        }

        private static void ItemsBreakFinalTie()
        {
            var w = Weights();
            long low = M2eAdaptiveExactMath.Score(2, 2, 2, 4, 5, 1, 10, w);
            long high = M2eAdaptiveExactMath.Score(2, 2, 2, 4, 5, 1, 11, w);
            TestRunner.AssertTrue(high > low, "assigned items must break the final integer tie");
        }

        private static void NegativeBoundsAreRejected()
        {
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eAdaptiveExactMath.BuildWeights(-1, 1, 1, 1), "negative order bound is invalid");
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eAdaptiveExactMath.BuildWeights(1, -1, 1, 1), "negative unit bound is invalid");
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eAdaptiveExactMath.BuildWeights(1, 1, -1, 1), "negative part bound is invalid");
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eAdaptiveExactMath.BuildWeights(1, 1, 1, -1), "negative urgency bound is invalid");
        }

        private static void ProcessingPodDoesNotCountAsFutureSupply()
        {
            TestRunner.AssertTrue(M2eAdaptiveExactMath.NeedsFuturePod(1, 1),
                "a station with only its current processing pod needs future supply");
        }

        private static void FuturePodSuppressesReplenishment()
        {
            TestRunner.AssertTrue(!M2eAdaptiveExactMath.NeedsFuturePod(2, 1),
                "one queued or en-route pod is enough future supply");
            TestRunner.AssertTrue(!M2eAdaptiveExactMath.NeedsFuturePod(1, 0),
                "an en-route pod must suppress another dispatch");
            TestRunner.AssertTrue(M2eAdaptiveExactMath.NeedsFuturePod(2, 1, 2),
                "a two-pod target must request one more pod when only one is future supply");
            TestRunner.AssertTrue(!M2eAdaptiveExactMath.NeedsFuturePod(3, 1, 2),
                "a two-pod target must stop when two future pods are queued");
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eAdaptiveExactMath.NeedsFuturePod(1, 2), "physical pod count cannot exceed inbound count");
            TestRunner.AssertThrows<ArgumentOutOfRangeException>(
                () => M2eAdaptiveExactMath.NeedsFuturePod(1, 0, 0), "future pod target must be positive");
        }

        private static void PodSelectionIsLexicographic()
        {
            TestRunner.AssertTrue(M2eAdaptiveExactMath.IsBetterPodCandidate(2, 1, 100, 9, 1, 99, 1, 1),
                "one more completable order must dominate coverage and distance");
            TestRunner.AssertTrue(M2eAdaptiveExactMath.IsBetterPodCandidate(2, 5, 100, 9, 2, 4, 1, 1),
                "coverage must break a completed-order tie");
            TestRunner.AssertTrue(M2eAdaptiveExactMath.IsBetterPodCandidate(2, 5, 10, 9, 2, 5, 11, 1),
                "distance must break an order and coverage tie");
            TestRunner.AssertTrue(M2eAdaptiveExactMath.IsBetterPodCandidate(2, 5, 10, 3, 2, 5, 10, 4),
                "pod id must make an exact tie deterministic");
        }

        private static void ConfigurationDefaultsOff()
        {
            var configuration = new SplitM1GExactConfiguration();
            TestRunner.AssertTrue(!configuration.AdaptiveExactResweeps,
                "existing exact configurations must retain the one-shot path");
            TestRunner.AssertTrue(configuration.AdaptiveExactResweepLimit > 0,
                "the default safety limit must be usable when enabled");
            TestRunner.AssertEqual(1, configuration.AdaptiveFuturePodTarget,
                "existing exact configurations keep the conservative one-future-pod target");
            TestRunner.AssertEqual(1, configuration.AdaptiveMaxPodBurst,
                "existing exact configurations keep a one-pod burst unless explicitly enabled");
            TestRunner.AssertTrue(!configuration.AdaptiveRiskPrefetch,
                "risk-triggered prefetch must remain opt-in");
            TestRunner.AssertTrue(configuration.AdaptivePeriodicSupplyLeadTimeSec == 0.0,
                "periodic supply timing remains disabled by default");
            TestRunner.AssertTrue(!configuration.AdaptiveSlotEfficientCompletion,
                "slot-efficient completion remains an explicit ablation");
        }
    }
}
