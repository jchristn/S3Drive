namespace Test.Automated.Tests
{
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Sharing;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="ShareNameValidator"/> and <see cref="NullSmbShareManager"/>.
    /// </summary>
    public static class SharingTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("ShareNameValidator accepts valid names", () =>
            {
                Assert.True(ShareNameValidator.IsValid("S3Drive-Prod"));
                Assert.True(ShareNameValidator.IsValid("My Share_1"));
            });

            runner.Add("ShareNameValidator rejects invalid names", () =>
            {
                Assert.False(ShareNameValidator.IsValid(null));
                Assert.False(ShareNameValidator.IsValid(string.Empty));
                Assert.False(ShareNameValidator.IsValid("   "));
                Assert.False(ShareNameValidator.IsValid("bad\\name"));
                Assert.False(ShareNameValidator.IsValid("bad/name"));
                Assert.False(ShareNameValidator.IsValid("a:b"));
                Assert.False(ShareNameValidator.IsValid("a|b"));
                Assert.False(ShareNameValidator.IsValid(new string('x', 81)));
            });

            runner.Add("NullSmbShareManager is inert", async () =>
            {
                NullSmbShareManager manager = new NullSmbShareManager();
                Assert.False(manager.IsSupported);
                await manager.CreateShareAsync(new DriveProfile(), "S:\\", CancellationToken.None);
                await manager.RemoveShareAsync("x", CancellationToken.None);
                Assert.False(await manager.ShareExistsAsync("x", CancellationToken.None));
            });
        }
    }
}
