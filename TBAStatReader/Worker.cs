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

internal class Worker(ILoggerFactory loggerFactory, HubConnection receiver, ServiceHubContext sender) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
    private readonly TaskCompletionSource<string> _cacheConnectionId = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await receiver.StartAsync(cancellationToken);
        receiver.On<string>(Constants.SignalR.Functions.RegisterCacheConnection, _cacheConnectionId.SetResult);

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
            await sender.Clients.User(Constants.SignalR.Users.ConnectionCache).SendAsync(Constants.SignalR.Functions.RegisterConnectionId, Constants.SignalR.Users.EndUser, receiver.ConnectionId, cancellationToken);
            await Task.Delay(1000, cancellationToken);
        } while (!_cacheConnectionId.Task.IsCompleted && !cancellationToken.IsCancellationRequested);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var cache = await _cacheConnectionId.Task;
        var connId = await sender.Clients.Client(cache!).InvokeAsync<string>(Constants.SignalR.Functions.GetUserConnectionId, Constants.SignalR.Users.Orchestrator, cancellationToken);

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
            var answer = await sender.Clients.Client(connId).InvokeAsync<string>(Constants.SignalR.Functions.GetAnswer, question, cancellationToken);

            await spinnerCancelToken.CancelAsync();

            Console.WriteLine(answer);
        } while (!cancellationToken.IsCancellationRequested);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}