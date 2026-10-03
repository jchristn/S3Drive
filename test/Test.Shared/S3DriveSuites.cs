namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using Test.Shared.Helpers;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// The single source of truth for every S3Drive test suite. All runners (Test.Automated,
    /// Test.Xunit, Test.Nunit) execute the descriptors returned here.
    /// </summary>
    public static class S3DriveSuites
    {
        /// <summary>
        /// Every suite, with storage integration configured from the S3DRIVE_TEST_* environment
        /// variables (integration cases are skipped when no endpoint is configured).
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return Build(StorageTestConfig.FromEnvironment()); }
        }

        /// <summary>
        /// Builds every suite using an explicit storage integration configuration.
        /// </summary>
        /// <param name="storage">The storage integration configuration.</param>
        /// <returns>The suites, in execution order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="storage"/> is null.</exception>
        public static IReadOnlyList<TestSuiteDescriptor> Build(StorageTestConfig storage)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));

            return new List<TestSuiteDescriptor>
            {
                KeyMapperSuite.Build(),
                MetadataCacheSuite.Build(),
                ConfigModelSuite.Build(),
                SettingsManagerSuite.Build(),
                CryptoSuite.Build(),
                LockSuite.Build(),
                IpcSuite.Build(),
                LoggingSuite.Build(),
                FileSystemSuite.Build(),
                MountManagerSuite.Build(),
                BlobS3StoreSuite.Build(),
                TelemetrySuite.Build(storage),
                StorageIntegrationSuite.Build(storage)
            };
        }
    }
}
