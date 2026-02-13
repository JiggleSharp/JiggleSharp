using System.Diagnostics;
using JiggleSharp.Core.Engine;
using JiggleSharp.Core.Idle;

namespace JiggleSharp.App;

class Program
{
    static async Task Main(string[] args)
    {
        IPlatformServices services = PlatformServicesFactory.Create();

        if (services.EnvironmentValidator.VerifyDependencies())
        {
            using (var cts = new CancellationTokenSource())
            {
                // Handle Ctrl+C gracefully
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true; // prevent immediate termination
                    cts.Cancel();
                };

                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

                try
                {
                    while (await timer.WaitForNextTickAsync(cts.Token))
                    {
                        await DoWorkAsync(services.IdleTimeProvider);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
            }
        }
        else
        {
            await Console.Error.WriteLineAsync("Application dependencies are not ready!");
        }
    }

    private static async Task DoWorkAsync(IIdleTimeProvider idleTimeProvider)
    {
        var idleTime = await idleTimeProvider.GetIdleTimeAsync();
        Console.WriteLine($"Idle time: {idleTime.TotalMilliseconds}ms");
    }
}