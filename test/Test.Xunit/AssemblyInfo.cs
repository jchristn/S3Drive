// Shared descriptors touch process-wide state (S3DRIVE_HOME, the static logger, IdGenerator
// length), and the fact and theory classes run the same descriptors, so run serially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
