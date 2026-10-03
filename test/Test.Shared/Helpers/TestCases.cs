namespace Test.Shared.Helpers
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Factory helpers that build Touchstone <see cref="TestCaseDescriptor"/> instances from
    /// synchronous or asynchronous test bodies.
    /// </summary>
    public static class TestCases
    {
        /// <summary>
        /// Creates a case with a synchronous body.
        /// </summary>
        /// <param name="suiteId">The owning suite id.</param>
        /// <param name="caseId">The case id, unique within the suite.</param>
        /// <param name="displayName">The human-readable name.</param>
        /// <param name="body">The test body; throws to fail.</param>
        /// <returns>The descriptor.</returns>
        public static TestCaseDescriptor Create(string suiteId, string caseId, string displayName, Action body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            return new TestCaseDescriptor(
                suiteId: suiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: ct =>
                {
                    body();
                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Creates a case with an asynchronous body.
        /// </summary>
        /// <param name="suiteId">The owning suite id.</param>
        /// <param name="caseId">The case id, unique within the suite.</param>
        /// <param name="displayName">The human-readable name.</param>
        /// <param name="body">The asynchronous test body; throws to fail.</param>
        /// <returns>The descriptor.</returns>
        public static TestCaseDescriptor Create(string suiteId, string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            return new TestCaseDescriptor(
                suiteId: suiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body);
        }

        /// <summary>
        /// Creates a skipped case.
        /// </summary>
        /// <param name="suiteId">The owning suite id.</param>
        /// <param name="caseId">The case id, unique within the suite.</param>
        /// <param name="displayName">The human-readable name.</param>
        /// <param name="reason">Why the case is skipped.</param>
        /// <returns>The descriptor.</returns>
        public static TestCaseDescriptor Skipped(string suiteId, string caseId, string displayName, string reason)
        {
            return new TestCaseDescriptor(
                suiteId: suiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: ct => Task.CompletedTask,
                skip: true,
                skipReason: reason);
        }
    }
}
