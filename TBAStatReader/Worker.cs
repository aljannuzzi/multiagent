namespace TBAStatReader;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class Worker(ILoggerFactory loggerFactory, HubConnection signalr, IConfiguration appConfig) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        signalr.On<string>(Constants.SignalR.Functions.ExpertJoined, expertName => _log.LogDebug("{expertName} is now available.", expertName));
        signalr.On<string>(Constants.SignalR.Functions.ExpertLeft, expertName => _log.LogDebug("{expertName} has disconnected.", expertName));

        _log.LogInformation("Connecting to server...");

        await signalr.StartAsync(cancellationToken);

        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        CancellationTokenSource? spinnerCancelToken = null, combinedCancelToken = null;
        async Task runSpinnerAsync(CancellationToken ct)
        {
            CircularCharArray progress = CircularCharArray.ProgressSpinner;
            while (!ct.IsCancellationRequested)
            {
                Console.Write(progress.Next());
                Console.CursorLeft = 0;

                await Task.Delay(100, ct);
            }
        }

        do
        {
            Console.Write("> ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            Stopwatch timer = Stopwatch.StartNew();

            spinnerCancelToken = new();
            combinedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, spinnerCancelToken.Token);
            var t = Task.Run(() => runSpinnerAsync(combinedCancelToken.Token), combinedCancelToken.Token);
            Task<string> ans = signalr.InvokeAsync<string>(Constants.SignalR.Functions.GetAnswer, question, cancellationToken);

            Task a = await Task.WhenAny(ans, Task.Delay(TimeSpan.FromSeconds(int.Parse(appConfig["ExpertWaitTimeSeconds"] ?? "10")), cancellationToken));
            timer.Stop();

            await spinnerCancelToken.CancelAsync();
            Console.CursorLeft = 0;
            if (a == ans)
            {
                Console.WriteLine(await ans);
            }
            else
            {
                Console.WriteLine("Looks like the Expert is stumped! Try another question.");
            }

            _log.LogInformation("Time to answer: {tta}", timer.Elapsed);
        } while (!cancellationToken.IsCancellationRequested);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}