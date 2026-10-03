namespace Test.Nunit
{
    using System.Collections;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs each shared descriptor as its own NUnit test case for per-test visibility.
    /// </summary>
    [TestFixture]
    public sealed class S3DriveNunitTests
    {
        /// <summary>
        /// Runs one descriptor.
        /// </summary>
        /// <param name="testCase">The descriptor.</param>
        /// <returns>A task that completes when the descriptor has run.</returns>
        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RunTest(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(S3DriveSuites.All);
        }
    }
}
