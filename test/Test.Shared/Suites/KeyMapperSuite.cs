namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using S3Drive.Core.Storage;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for <see cref="KeyMapper"/>: translation between Windows paths and S3 object keys.
    /// </summary>
    public static class KeyMapperSuite
    {
        private const string SuiteId = "KeyMapper";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "ToObjectKeyRootAndNull", "KeyMapper.ToObjectKey maps root, null, and empty to the empty key", () =>
                {
                    Assert.Equal(string.Empty, KeyMapper.ToObjectKey("\\"));
                    Assert.Equal(string.Empty, KeyMapper.ToObjectKey(null));
                    Assert.Equal(string.Empty, KeyMapper.ToObjectKey(string.Empty));
                }),

                TestCases.Create(SuiteId, "ToObjectKeyNested", "KeyMapper.ToObjectKey maps nested paths to slash-separated keys", () =>
                {
                    Assert.Equal("a/b/file.txt", KeyMapper.ToObjectKey("\\a\\b\\file.txt"));
                    Assert.Equal("a", KeyMapper.ToObjectKey("\\a"));
                }),

                TestCases.Create(SuiteId, "ToObjectKeyMixedSeparators", "KeyMapper.ToObjectKey normalizes forward slashes and strips all leading separators", () =>
                {
                    Assert.Equal("a/b/c.txt", KeyMapper.ToObjectKey("/a/b\\c.txt"));
                    Assert.Equal("a/b", KeyMapper.ToObjectKey("\\\\a\\b"));
                    Assert.Equal("a.txt", KeyMapper.ToObjectKey("a.txt"));
                }),

                TestCases.Create(SuiteId, "ToObjectKeyPreservesSpecialCharacters", "KeyMapper.ToObjectKey preserves spaces, unicode, and punctuation", () =>
                {
                    Assert.Equal("my docs/résumé (final) #1.txt", KeyMapper.ToObjectKey("\\my docs\\résumé (final) #1.txt"));
                    Assert.Equal("日本/ファイル.txt", KeyMapper.ToObjectKey("\\日本\\ファイル.txt"));
                }),

                TestCases.Create(SuiteId, "ToPrefix", "KeyMapper.ToPrefix appends exactly one trailing slash", () =>
                {
                    Assert.Equal(string.Empty, KeyMapper.ToPrefix("\\"));
                    Assert.Equal("a/b/", KeyMapper.ToPrefix("\\a\\b"));
                    Assert.Equal("a/", KeyMapper.ToPrefix("\\a\\"));
                }),

                TestCases.Create(SuiteId, "ToPrefixNullAndEmpty", "KeyMapper.ToPrefix maps null and empty to the bucket root", () =>
                {
                    Assert.Equal(string.Empty, KeyMapper.ToPrefix(null));
                    Assert.Equal(string.Empty, KeyMapper.ToPrefix(string.Empty));
                }),

                TestCases.Create(SuiteId, "ToPath", "KeyMapper.ToPath maps keys and folder prefixes back to Windows paths", () =>
                {
                    Assert.Equal("\\", KeyMapper.ToPath(string.Empty));
                    Assert.Equal("\\", KeyMapper.ToPath(null));
                    Assert.Equal("\\a\\b\\file.txt", KeyMapper.ToPath("a/b/file.txt"));
                    Assert.Equal("\\a\\b", KeyMapper.ToPath("a/b/"));
                }),

                TestCases.Create(SuiteId, "ToPathTrimsLeadingSlash", "KeyMapper.ToPath does not double the separator for keys with a leading slash", () =>
                {
                    Assert.Equal("\\a\\b", KeyMapper.ToPath("/a/b"));
                }),

                TestCases.Create(SuiteId, "GetName", "KeyMapper.GetName returns the last path segment", () =>
                {
                    Assert.Equal("file.txt", KeyMapper.GetName("\\a\\b\\file.txt"));
                    Assert.Equal(string.Empty, KeyMapper.GetName("\\"));
                    Assert.Equal("a", KeyMapper.GetName("\\a"));
                }),

                TestCases.Create(SuiteId, "GetNameTrailingSeparatorAndNull", "KeyMapper.GetName ignores a trailing separator and tolerates null", () =>
                {
                    Assert.Equal("b", KeyMapper.GetName("\\a\\b\\"));
                    Assert.Equal(string.Empty, KeyMapper.GetName(null));
                }),

                TestCases.Create(SuiteId, "GetParentPath", "KeyMapper.GetParentPath returns the containing folder", () =>
                {
                    Assert.Equal("\\a", KeyMapper.GetParentPath("\\a\\b"));
                    Assert.Equal("\\", KeyMapper.GetParentPath("\\a"));
                    Assert.Equal("\\", KeyMapper.GetParentPath("\\"));
                }),

                TestCases.Create(SuiteId, "GetParentPathDeepAndNull", "KeyMapper.GetParentPath handles deep paths, trailing separators, and null", () =>
                {
                    Assert.Equal("\\a\\b\\c", KeyMapper.GetParentPath("\\a\\b\\c\\d.txt"));
                    Assert.Equal("\\a", KeyMapper.GetParentPath("\\a\\b\\"));
                    Assert.Equal("\\", KeyMapper.GetParentPath(null));
                }),

                TestCases.Create(SuiteId, "RoundTrip", "KeyMapper path to key to path round-trips", () =>
                {
                    Assert.Equal("\\a\\b\\c.txt", KeyMapper.ToPath(KeyMapper.ToObjectKey("\\a\\b\\c.txt")));
                    Assert.Equal("a/b/c.txt", KeyMapper.ToObjectKey(KeyMapper.ToPath("a/b/c.txt")));
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Key mapping", cases);
        }
    }
}
