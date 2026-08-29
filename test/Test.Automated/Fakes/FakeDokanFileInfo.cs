namespace Test.Automated.Fakes
{
    using System;
    using System.Security.Principal;
    using DokanNet;

    /// <summary>
    /// A test double for <see cref="IDokanFileInfo"/> so filesystem operations can be driven
    /// directly without a mounted volume.
    /// </summary>
    public sealed class FakeDokanFileInfo : IDokanFileInfo
    {
        /// <inheritdoc />
        public object? Context { get; set; }

        /// <inheritdoc />
        public bool DeletePending { get; set; }

        /// <inheritdoc />
        public bool IsDirectory { get; set; }

        /// <inheritdoc />
        public bool NoCache
        {
            get { return false; }
        }

        /// <inheritdoc />
        public bool PagingIo
        {
            get { return false; }
        }

        /// <inheritdoc />
        public int ProcessId
        {
            get { return 0; }
        }

        /// <inheritdoc />
        public bool SynchronousIo
        {
            get { return false; }
        }

        /// <summary>
        /// Whether writes append to the end of the file. Settable for tests.
        /// </summary>
        public bool WriteToEndOfFile { get; set; }

        /// <inheritdoc />
        public WindowsIdentity GetRequestor()
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public bool TryResetTimeout(int milliseconds)
        {
            return true;
        }
    }
}
