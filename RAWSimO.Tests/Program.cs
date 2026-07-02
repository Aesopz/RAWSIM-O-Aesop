using System;
using System.Collections.Generic;

namespace RAWSimO.Tests
{
    /// <summary>
    /// Minimal hand-rolled test runner (no NuGet in this legacy solution).
    /// Register tests via Add(), run all via RunAll(); process exit code 0 = all passed.
    /// </summary>
    public static class TestRunner
    {
        private static readonly List<Tuple<string, Action>> _tests = new List<Tuple<string, Action>>();
        public static void Add(string name, Action test) { _tests.Add(Tuple.Create(name, test)); }
        public static void AssertTrue(bool condition, string message)
        { if (!condition) throw new Exception("Assertion failed: " + message); }
        public static void AssertEqual(int expected, int actual, string message)
        { if (expected != actual) throw new Exception("Assertion failed: " + message + " (expected " + expected + ", got " + actual + ")"); }
        public static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Assertion failed (expected " + typeof(T).Name + "): " + message);
        }
        public static int RunAll()
        {
            int failed = 0;
            foreach (var test in _tests)
            {
                try { test.Item2(); Console.WriteLine("[PASS] " + test.Item1); }
                catch (Exception ex) { failed++; Console.WriteLine("[FAIL] " + test.Item1 + " -- " + ex.Message); }
            }
            Console.WriteLine((_tests.Count - failed) + "/" + _tests.Count + " passed");
            return failed == 0 ? 0 : 1;
        }
    }

    public class Program
    {
        public static int Main(string[] args)
        {
            // Test classes register here (added by later tasks)
            return TestRunner.RunAll();
        }
    }
}
