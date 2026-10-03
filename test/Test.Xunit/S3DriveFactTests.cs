namespace Test.Xunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using global::Xunit;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;

    /// <summary>
    /// Runs every shared descriptor sequentially in a single xUnit fact, honoring suite order.
    /// </summary>
    public sealed class S3DriveFactTests : TouchstoneFactBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites
        {
            get { return S3DriveSuites.All; }
        }

        /// <summary>
        /// Runs all suites.
        /// </summary>
        /// <returns>A task that completes when every suite has run.</returns>
        [Fact]
        public async Task RunAll()
        {
            await RunAllAsync();
        }
    }
}
