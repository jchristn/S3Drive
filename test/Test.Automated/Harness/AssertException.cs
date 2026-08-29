namespace Test.Automated.Harness
{
    using System;

    /// <summary>
    /// Raised by an assertion helper when a check fails.
    /// </summary>
    public sealed class AssertException : Exception
    {
        /// <summary>
        /// Initializes a new instance with a message.
        /// </summary>
        /// <param name="message">The failure message.</param>
        public AssertException(string message)
            : base(message)
        {
        }
    }
}
