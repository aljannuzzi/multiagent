namespace TBAStatReader;

using System;
using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.Threading;

internal class Worker(Kernel kernel, PromptExecutionSettings promptSettings, ILoggerFactory loggerFactory, HubConnection signalr) : IHostedService
{
    private readonly ILogger _log = loggerFactory.CreateLogger(Constants.SignalR.Users.Orchestrator);

    private static string CacheConnectionId { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await signalr.StartAsync(cancellationToken);

        signalr.On<string, string>(Constants.SignalR.Functions.GetAnswer, async (t, s) => await signalr.SendAsync(Constants.SignalR.Functions.ExpertAnswerReceived, t, await AskExpertAsync(s)));
        signalr.On<string, string>(Constants.SignalR.Functions.Introduce, (n, d) => AddExpert(n, d, cancellationToken));

        Console.WriteLine("Awaiting question from user...");
    }

    private void AddExpert(string name, string description, CancellationToken cancellationToken)
    {
        _log.LogDebug("Adding {expertName} to colleagues...", name);
        kernel.ImportPluginFromFunctions(name, [kernel.CreateFunctionFromMethod((string prompt) => signalr.InvokeAsync<string>("GetAnswerFromExpert", name, prompt, cancellationToken),
            name, description,
            [new("prompt") { IsRequired = true, ParameterType = typeof(string) }],
            new() { Description = "Prompt response as a JSON object or array to be inferred upon.", ParameterType = typeof(string) })]
        );
        _log.LogTrace("Expert {expertName} added.", name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<string> AskExpertAsync(string prompt)
    {
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        kernel.FunctionFilters.Add(new DebugFunctionFilter());
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        _log.LogInformation("Question received: {question}", prompt);

        do
        {
            try
            {
                FunctionResult promptResult = await kernel.InvokePromptAsync(prompt, new(promptSettings));
                _log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

                return promptResult.ToString();
                break;
            }
            catch (HttpOperationException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && ex.InnerException is Azure.RequestFailedException rex)
                {
                    Azure.Response? resp = rex.GetRawResponse();
                    if (resp?.Headers.TryGetValue("Retry-After", out var waitTime) is true)
                    {
                        _log.LogWarning("Responses Throttled! Waiting {retryAfter} seconds to try again...", waitTime);
                        await Task.Delay(TimeSpan.FromSeconds(int.Parse(waitTime))).ConfigureAwait(false);
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
            catch (Exception ex) { }
        } while (true);
    }
}
