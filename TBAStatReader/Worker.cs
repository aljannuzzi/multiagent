namespace TBAStatReader;

using System;
using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class Worker(ILoggerFactory loggerFactory, HubConnection signalr) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
    private readonly TaskCompletionSource<string> _expertAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        signalr.On<string>("expertJoined", expertName => _log.LogDebug("{expertName} is available", expertName));
        signalr.On<string>("newMessage", m => _log.LogWarning("Message received! {message}", m));

        await signalr.StartAsync(cancellationToken);

        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        CancellationTokenSource? spinnerCancelToken = null, combinedCancelToken = null;
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

        signalr.On<string>(Constants.SignalR.Functions.ExpertAnswerReceived, a => _expertAnswer.SetResult(a));

        do
        {
            Console.Write("> ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            spinnerCancelToken = new();
            combinedCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, spinnerCancelToken.Token);
            var t = Task.Run(() => runSpinnerAsync(combinedCancelToken.Token), combinedCancelToken.Token);
            await signalr.SendAsync(Constants.SignalR.Functions.GetAnswer, question, cancellationToken);

            var a = await Task.WhenAny(_expertAnswer.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            await spinnerCancelToken.CancelAsync();
            Console.CursorLeft--;
            if (a == _expertAnswer.Task)
            {
                Console.WriteLine(_expertAnswer.Task.Result);
            }
            else
            {
                Console.WriteLine("Looks like the Expert is stumped! Try another question.");
            }
        } while (!cancellationToken.IsCancellationRequested);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}