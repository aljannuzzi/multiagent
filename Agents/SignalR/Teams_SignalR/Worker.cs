namespace TBAStatReader;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using TBAAPI.V3Client.Client;

internal class Worker(Kernel sk, PromptExecutionSettings promptSettings, ILoggerFactory loggerFactory, HubConnection receiver) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger("TeamsExpert");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await receiver.StartAsync(cancellationToken);
        Console.WriteLine("Awaiting question from orchestrator...");

        receiver.On<string, string>("question", async (prompt) =>
        {

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            sk.FunctionFilters.Add(new DebugFunctionFilter());
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            do
            {
                try
                {
                    try
                    {
                        FunctionResult promptResult = await sk.InvokePromptAsync(prompt, new(promptSettings), cancellationToken: cancellationToken);

                        _log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

                        return promptResult.ToString();
                    }
                    catch (ApiException ex)
                    {
                        _log.LogError(ex, "Error handling prompt: {prompt}", prompt);

                        return JsonSerializer.Serialize(ex.ErrorContent);
                    }
                }
                catch (HttpOperationException ex)
                {
                    if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && ex.InnerException is Azure.RequestFailedException rex)
                    {
                        Azure.Response? resp = rex.GetRawResponse();
                        if (resp?.Headers.TryGetValue("Retry-After", out var waitTime) is true)
                        {
                            _log.LogWarning("Responses Throttled! Waiting {retryAfter} seconds to try again...", waitTime);
                            await Task.Delay(TimeSpan.FromSeconds(int.Parse(waitTime)), cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    else
                    {
                        throw;
                    }
                }
            } while (true);
        });
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
