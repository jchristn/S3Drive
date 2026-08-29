namespace Test.Automated.Harness
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// A single named test with an asynchronous body.
    /// </summary>
    public sealed class TestCase
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="name">The test name.</param>
        /// <param name="body">The test body.</param>
        /// <param name="skip">Whether the test is skipped.</param>
        /// <param name="skipReason">The reason the test is skipped, when applicable.</param>
        public TestCase(string name, Func<Task> body, bool skip, string? skipReason)
        {
            Name = name;
            Body = body;
            Skip = skip;
            SkipReason = skipReason;
        }

        /// <summary>
        /// The test name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The test body.
        /// </summary>
        public Func<Task> Body { get; }

        /// <summary>
        /// Whether the test is skipped.
        /// </summary>
        public bool Skip { get; }

        /// <summary>
        /// The reason the test is skipped, when applicable.
        /// </summary>
        public string? SkipReason { get; }
    }
}
