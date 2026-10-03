namespace Test.Xunit
{
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using global::Xunit.Abstractions;
    using Test.Shared;
    using Touchstone.Core;

    /// <summary>
    /// Runs each shared descriptor as its own xUnit theory row for per-test visibility.
    /// </summary>
    public sealed class S3DriveTheoryTests
    {
        private readonly ITestOutputHelper _Output;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="output">The xUnit output helper.</param>
        public S3DriveTheoryTests(ITestOutputHelper output)
        {
            _Output = output;
        }

        /// <summary>
        /// Every non-skipped shared descriptor.
        /// </summary>
        /// <returns>The theory data.</returns>
        public static TheoryData<TestCaseDescriptor> TestCases()
        {
            TheoryData<TestCaseDescriptor> data = new TheoryData<TestCaseDescriptor>();

            foreach (TestSuiteDescriptor suite in S3DriveSuites.All)
            {
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip) data.Add(testCase);
                }
            }

            return data;
        }

        /// <summary>
        /// Runs one descriptor.
        /// </summary>
        /// <param name="testCase">The descriptor.</param>
        /// <returns>A task that completes when the descriptor has run.</returns>
        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            _Output.WriteLine("Running: " + testCase.DisplayName);
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
