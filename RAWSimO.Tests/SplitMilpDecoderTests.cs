using System;
using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core;
using RAWSimO.Core.Control.Defaults.OrderBatching;
using RAWSimO.Core.Items;

namespace RAWSimO.Tests
{
    public static class SplitMilpDecoderTests
    {
        private static Instance _instance = new Instance();
        private static ItemDescription Sku() { return new SimpleItemDescription(_instance); }
        private static KeyValuePair<ItemDescription, int> Q(ItemDescription i, int q)
        { return new KeyValuePair<ItemDescription, int>(i, q); }
        private static Dictionary<ItemDescription, int> D(params KeyValuePair<ItemDescription, int>[] entries)
        { return entries.ToDictionary(e => e.Key, e => e.Value); }

        public static void Register()
        {
            TestRunner.Add("Decoder_M2_Partial_TwoStations", () =>
            {
                var a = Sku(); var b = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 3), Q(b, 2) }.ToList(),
                    new[] { D(Q(a, 2)), D(Q(b, 1)) }.ToList(), true, out full);
                TestRunner.AssertEqual(2, parts.Count, "two non-empty parts");
                TestRunner.AssertEqual(0, parts[0].Key, "first part maps to station index 0");
                TestRunner.AssertEqual(1, parts[1].Key, "second part maps to station index 1");
                TestRunner.AssertEqual(2, parts[0].Value[a], "station 0 gets 2xA");
                TestRunner.AssertTrue(!full, "3 of 5 units assigned -> not fully assigned");
            });
            TestRunner.Add("Decoder_PrunesEmptyAndZeroStations", () =>
            {
                var a = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 2) }.ToList(),
                    new[] { D(), D(Q(a, 0)), D(Q(a, 2)) }.ToList(), true, out full);
                TestRunner.AssertEqual(1, parts.Count, "empty/zero stations pruned");
                TestRunner.AssertEqual(2, parts[0].Key, "surviving part keeps original station index");
                TestRunner.AssertTrue(full, "all 2 units assigned");
            });
            TestRunner.Add("Decoder_AllZero_ReturnsEmpty", () =>
            {
                var a = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 2) }.ToList(),
                    new[] { D(), D() }.ToList(), false, out full);
                TestRunner.AssertEqual(0, parts.Count, "nothing assigned -> empty (M1 z=0 case)");
                TestRunner.AssertTrue(!full, "not fully assigned");
            });
            TestRunner.Add("Decoder_OverAssignment_Throws", () =>
            {
                var a = Sku();
                bool full;
                TestRunner.AssertThrows<InvalidOperationException>(() =>
                    SplitMilpDecoder.Decode(new[] { Q(a, 2) }.ToList(),
                        new[] { D(Q(a, 2)), D(Q(a, 1)) }.ToList(), true, out full),
                    "assigned 3 > remaining 2 must throw");
            });
            TestRunner.Add("Decoder_UnknownSku_Throws", () =>
            {
                var a = Sku(); var ghost = Sku();
                bool full;
                TestRunner.AssertThrows<InvalidOperationException>(() =>
                    SplitMilpDecoder.Decode(new[] { Q(a, 2) }.ToList(),
                        new[] { D(Q(ghost, 1)) }.ToList(), true, out full),
                    "sku not in remaining must throw");
            });
            TestRunner.Add("Decoder_M1_PartialAssignment_Throws", () =>
            {
                var a = Sku(); var b = Sku();
                bool full;
                TestRunner.AssertThrows<InvalidOperationException>(() =>
                    SplitMilpDecoder.Decode(new[] { Q(a, 2), Q(b, 1) }.ToList(),
                        new[] { D(Q(a, 2)) }.ToList(), false, out full),
                    "M1 with 2 of 3 units assigned must throw");
            });
            TestRunner.Add("Decoder_M1_FullAcrossStations_OK", () =>
            {
                var a = Sku(); var b = Sku();
                bool full;
                var parts = SplitMilpDecoder.Decode(
                    new[] { Q(a, 2), Q(b, 1) }.ToList(),
                    new[] { D(Q(a, 1)), D(Q(a, 1), Q(b, 1)) }.ToList(), false, out full);
                TestRunner.AssertEqual(2, parts.Count, "M1 full assignment across two stations");
                TestRunner.AssertTrue(full, "fully assigned");
                TestRunner.AssertEqual(1, parts[0].Value[a], "station 0: 1xA");
                TestRunner.AssertEqual(1, parts[1].Value[b], "station 1: 1xB");
            });
        }
    }
}
