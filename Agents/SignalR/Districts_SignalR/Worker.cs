namespace TBAStatReader;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Common;
using Common.Extensions;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using TBAAPI.V3Client.Client;

internal class Worker(Kernel sk, PromptExecutionSettings promptSettings, ILoggerFactory loggerFactory, HubConnection receiver, IConfiguration appConfig) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger(appConfig.GetRequiredSection("ExpertDefinition")["Name"] ?? throw new ArgumentNullException("Missing ExpertDefinition.Name setting"));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await receiver.StartAsync(cancellationToken);

        _log.LogDebug("Introducing myself to the orchestrator...");
        await receiver.SendAsync(Constants.SignalR.Functions.Introduce, appConfig.GetRequiredSection("ExpertDefinition")["Name"], appConfig.GetRequiredSection("ExpertDefinition")["Description"], cancellationToken: cancellationToken);

        _log.LogInformation("Awaiting question from orchestrator...");

        receiver.On<string, string>(Constants.SignalR.Functions.GetAnswer, async prompt =>
        {
            using var scope = _log.CreateMethodScope(Constants.SignalR.Functions.GetAnswer);
            _log.LogDebug("Prompt received from orchestrator: {prompt}", prompt);

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            sk.FunctionFilters.Add(new DebugFunctionFilter());
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            do
            {
                try
                {
                    string response;
                    try
                    {
                        FunctionResult promptResult = await sk.InvokePromptAsync(prompt, new(promptSettings), cancellationToken: cancellationToken);

                        _log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

                        response = promptResult.ToString();
                    }
                    catch (ApiException ex)
                    {
                        _log.LogError(ex, "Error handling prompt: {prompt}", prompt);

                        response = JsonSerializer.Serialize(ex.ErrorContent);
                    }

                    return response;
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
