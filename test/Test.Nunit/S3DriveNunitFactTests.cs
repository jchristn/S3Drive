namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>
    /// Runs every shared descriptor sequentially in a single NUnit test, honoring suite order.
    /// </summary>
    [TestFixture]
    public sealed class S3DriveNunitFactTests : TouchstoneNunitBase
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
        [Test]
        public async Task RunAll()
        {
            await RunAllAsync().ConfigureAwait(false);
        }
    }
}
