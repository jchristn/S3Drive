namespace Test.Automated.Harness
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Registers and runs test cases, printing results and returning a process exit code.
    /// </summary>
    public sealed class TestRunner
    {
        private readonly List<TestCase> _Cases = new List<TestCase>();

        /// <summary>
        /// Registers an asynchronous test.
        /// </summary>
        /// <param name="name">The test name.</param>
        /// <param name="body">The asynchronous test body.</param>
        public void Add(string name, Func<Task> body)
        {
            _Cases.Add(new TestCase(name, body, false, null));
        }

        /// <summary>
        /// Registers a synchronous test.
        /// </summary>
        /// <param name="name">The test name.</param>
        /// <param name="body">The synchronous test body.</param>
        public void Add(string name, Action body)
        {
            _Cases.Add(new TestCase(name, () =>
            {
                body();
                return Task.CompletedTask;
            }, false, null));
        }

        /// <summary>
        /// Registers a skipped test.
        /// </summary>
        /// <param name="name">The test name.</param>
        /// <param name="reason">The reason it is skipped.</param>
        public void Skip(string name, string reason)
        {
            _Cases.Add(new TestCase(name, () => Task.CompletedTask, true, reason));
        }

        /// <summary>
        /// Runs all registered tests.
        /// </summary>
        /// <returns>Zero when all non-skipped tests pass; otherwise one.</returns>
        public async Task<int> RunAsync()
        {
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            List<string> failures = new List<string>();

            foreach (TestCase test in _Cases)
            {
                if (test.Skip)
                {
                    skipped++;
                    Console.WriteLine("SKIP  " + test.Name + " (" + test.SkipReason + ")");
                    continue;
                }

                try
                {
                    await test.Body().ConfigureAwait(false);
                    passed++;
                    Console.WriteLine("PASS  " + test.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add(test.Name + ": " + ex.Message);
                    Console.WriteLine("FAIL  " + test.Name + " -> " + ex.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Results: " + passed + " passed, " + failed + " failed, " + skipped + " skipped, " + _Cases.Count + " total.");

            if (failed > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failures:");
                foreach (string failure in failures)
                {
                    Console.WriteLine("  - " + failure);
                }
            }

            return failed > 0 ? 1 : 0;
        }
    }
}
