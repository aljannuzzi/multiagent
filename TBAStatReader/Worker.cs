namespace TBAStatReader;

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class Worker(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
    private readonly HttpClient _client = httpClientFactory.CreateClient("Orchestrator");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        do
        {
            Console.Write("> ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            CircularCharArray progress = CircularCharArray.ProgressSpinner;

            Task<HttpResponseMessage> response = _client.PostAsync("api/messages", new StringContent(question), cancellationToken);
            while (!response.IsCompleted)
            {
                Console.Write(progress.Next());
                Console.CursorLeft--;

                await Task.Delay(100);
            }

            HttpResponseMessage responseResult = await response;
            responseResult.EnsureSuccessStatusCode();

            var responseString = await responseResult.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine(responseString);

        } while (!cancellationToken.IsCancellationRequested);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
