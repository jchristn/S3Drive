namespace S3Drive.Publisher
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cancel.Cancel();
                };

                try
                {
                    PublisherOptions options = PublisherOptions.Parse(args);
                    PublisherRunner runner = new PublisherRunner(Console.Out);
                    return await runner.RunAsync(options, cancel.Token).ConfigureAwait(false);
                }
                catch (PublisherException ex)
                {
                    Console.Error.WriteLine("error: " + ex.Message);
                    return 1;
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("Canceled.");
                    return 130;
                }
            }
        }
    }
}
