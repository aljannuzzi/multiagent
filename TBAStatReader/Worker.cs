namespace TBAStatReader;

using System;
using System.Diagnostics;
using System.Text;
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

    internal static bool WaitingForResponse = false;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signalr.On<string>(Constants.SignalR.Functions.ExpertJoined, expertName => _log.LogDebug("{expertName} is now available.", expertName));
        signalr.On<string>(Constants.SignalR.Functions.ExpertLeft, expertName => _log.LogDebug("{expertName} has disconnected.", expertName));

        _log.LogInformation("Connecting to server...");

        await signalr.StartAsync(cancellationToken);

        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        static async Task runSpinnerAsync(CancellationToken ct)
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

            var timer = Stopwatch.StartNew();

            CancellationTokenSource spinnerCancelToken = new();
            var combinedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, spinnerCancelToken.Token);
            var t = Task.Run(() => runSpinnerAsync(combinedCancelToken.Token), combinedCancelToken.Token);

            WaitingForResponse = true;
            var answerStream = await signalr.StreamAsChannelAsync<string>(Constants.SignalR.Functions.GetAnswer, Constants.SignalR.Users.Orchestrator, question, cancellationToken);
            await answerStream.WaitToReadAsync();
            WaitingForResponse = false;
            await spinnerCancelToken.CancelAsync();
            Console.CursorLeft = 0;

            bool end = false;
            while (!end && await answerStream.WaitToReadAsync())
            {
                StringBuilder totalStream = new();
                while (!end && answerStream.TryRead(out var token))
                {
                    totalStream.Append(token);

                    if (end = totalStream.ToString().EndsWith(Constants.Token.EndToken))
                    {
                        break;
                    }

                    Console.Write(token);
                }
            }

            Console.WriteLine();

            _log.LogInformation("Time to answer: {tta}", timer.Elapsed);
        } while (!cancellationToken.IsCancellationRequested);
    }

    public async Task StopAsync(CancellationToken cancellationToken) => await signalr.DisposeAsync();
}