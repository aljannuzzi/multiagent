namespace TBAStatReader;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class Worker(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, HubConnection receiver, IServiceHubContext sender) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
    private readonly HttpClient _client = httpClientFactory.CreateClient("Orchestrator");

    private readonly AutoResetEvent orchestrationWaiter = new(false);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Task startReceiverTask = receiver.StartAsync(cancellationToken);
        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        receiver.On<string>("answer", s =>
        {
            Console.WriteLine(s);
            orchestrationWaiter.Set();
        });

        CancellationTokenSource spinnerCancelToken, combinedCancelToken;
        async Task runSpinnerAsync(CancellationToken ct)
        {
            CircularCharArray progress = CircularCharArray.ProgressSpinner;
            while (!ct.IsCancellationRequested)
            {
                Console.Write(progress.Next());
                Console.CursorLeft--;

                await Task.Delay(100);
            }
        };

        do
        {
            Console.Write("> ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            await sender.Clients.User("orchestrator").SendAsync("question", question, receiver.ConnectionId, cancellationToken);

            spinnerCancelToken = new CancellationTokenSource();
            combinedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, spinnerCancelToken.Token);
            var t = Task.Run(() => runSpinnerAsync(combinedCancelToken.Token), combinedCancelToken.Token);
            orchestrationWaiter.WaitOne();
            await spinnerCancelToken.CancelAsync();

        } while (!cancellationToken.IsCancellationRequested);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
