using RAWSimO.Core.Elements;

namespace RAWSimO.Tests
{
    public static class PackingBufferTests
    {
        public static void Register()
        {
            TestRunner.Add("PackingBuffer.RegisterParent counts each parent once", () =>
            {
                var buffer = new PackingBuffer(78);
                TestRunner.AssertTrue(buffer.RegisterParent(7), "first registration");
                TestRunner.AssertTrue(!buffer.RegisterParent(7), "idempotent re-registration");
                TestRunner.AssertEqual(1, buffer.AliveParentCount, "one alive parent");
            });
            TestRunner.Add("PackingBuffer.ReleaseParent frees the box once", () =>
            {
                var buffer = new PackingBuffer(78);
                buffer.RegisterParent(7);
                TestRunner.AssertTrue(buffer.ReleaseParent(7), "release");
                TestRunner.AssertEqual(0, buffer.AliveParentCount, "box freed");
                TestRunner.AssertTrue(!buffer.ReleaseParent(7), "idempotent re-release");
            });
            TestRunner.Add("PackingBuffer.ReleaseParent unknown parent is safe", () =>
            {
                var buffer = new PackingBuffer(78);
                TestRunner.AssertTrue(!buffer.ReleaseParent(99), "unknown id no-op");
                TestRunner.AssertEqual(0, buffer.AliveParentCount, "still empty");
            });
            TestRunner.Add("PackingBuffer capacity is stored as configured", () =>
            {
                TestRunner.AssertEqual(78, new PackingBuffer(78).Capacity, "capacity 78");
                TestRunner.AssertEqual(0, new PackingBuffer(0).Capacity, "capacity 0 = unlimited");
            });
        }
    }
}
