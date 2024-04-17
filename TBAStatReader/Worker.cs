namespace TBAStatReader;

using System;
using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class Worker(ILoggerFactory loggerFactory, HubConnection connection) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
    private readonly TaskCompletionSource<string> _cacheConnectionId = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await connection.StartAsync(cancellationToken);

        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        CancellationTokenSource spinnerCancelToken, combinedCancelToken;
        async Task runSpinnerAsync(CancellationToken ct)
        {
            CircularCharArray progress = CircularCharArray.ProgressSpinner;
            while (!ct.IsCancellationRequested)
            {
                Console.Write(progress.Next());
                Console.CursorLeft--;

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

            spinnerCancelToken = new CancellationTokenSource();
            combinedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, spinnerCancelToken.Token);
            var t = Task.Run(() => runSpinnerAsync(combinedCancelToken.Token), combinedCancelToken.Token);
            var answer = await connection.InvokeAsync<string>(Constants.SignalR.Functions.GetAnswer, question, cancellationToken);

            await spinnerCancelToken.CancelAsync();

            Console.WriteLine(answer);
        } while (!cancellationToken.IsCancellationRequested);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}