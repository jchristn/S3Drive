namespace Test.Automated.Tests
{
    using S3Drive.Core.Storage;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="KeyMapper"/>.
    /// </summary>
    public static class KeyMapperTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("KeyMapper.ToObjectKey root and null", () =>
            {
                Assert.Equal(string.Empty, KeyMapper.ToObjectKey("\\"));
                Assert.Equal(string.Empty, KeyMapper.ToObjectKey(null));
                Assert.Equal(string.Empty, KeyMapper.ToObjectKey(string.Empty));
            });

            runner.Add("KeyMapper.ToObjectKey nested", () =>
            {
                Assert.Equal("a/b/file.txt", KeyMapper.ToObjectKey("\\a\\b\\file.txt"));
                Assert.Equal("a", KeyMapper.ToObjectKey("\\a"));
            });

            runner.Add("KeyMapper.ToPrefix", () =>
            {
                Assert.Equal(string.Empty, KeyMapper.ToPrefix("\\"));
                Assert.Equal("a/b/", KeyMapper.ToPrefix("\\a\\b"));
                Assert.Equal("a/", KeyMapper.ToPrefix("\\a\\"));
            });

            runner.Add("KeyMapper.ToPath", () =>
            {
                Assert.Equal("\\", KeyMapper.ToPath(string.Empty));
                Assert.Equal("\\", KeyMapper.ToPath(null));
                Assert.Equal("\\a\\b\\file.txt", KeyMapper.ToPath("a/b/file.txt"));
                Assert.Equal("\\a\\b", KeyMapper.ToPath("a/b/"));
            });

            runner.Add("KeyMapper.GetName", () =>
            {
                Assert.Equal("file.txt", KeyMapper.GetName("\\a\\b\\file.txt"));
                Assert.Equal(string.Empty, KeyMapper.GetName("\\"));
                Assert.Equal("a", KeyMapper.GetName("\\a"));
            });

            runner.Add("KeyMapper.GetParentPath", () =>
            {
                Assert.Equal("\\a", KeyMapper.GetParentPath("\\a\\b"));
                Assert.Equal("\\", KeyMapper.GetParentPath("\\a"));
                Assert.Equal("\\", KeyMapper.GetParentPath("\\"));
            });

            runner.Add("KeyMapper roundtrip", () =>
            {
                Assert.Equal("\\a\\b\\c.txt", KeyMapper.ToPath(KeyMapper.ToObjectKey("\\a\\b\\c.txt")));
            });
        }
    }
}
