namespace TBAStatReader;

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.Threading;

internal class Worker : IHostedService
{
    private readonly ILogger _log;

    private readonly Kernel _sk;
    private readonly HubConnection _receiver;
    private readonly IServiceHubContext _sender;
    private readonly ImmutableList<AgentDefinition> _agents;

    public Worker(Kernel sk, PromptExecutionSettings promptSettings, ILoggerFactory loggerFactory, HubConnection receiver, IServiceHubContext sender, ImmutableList<AgentDefinition> agents)
    {
        _log = loggerFactory.CreateLogger(Constants.SignalR.Users.Orchestrator);
        _sk = sk;
        _receiver = receiver;
        _sender = sender;
        _agents = agents;

        receiver.On<string, string>(Constants.SignalR.Functions.GetAnswer, async (prompt) =>
        {
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            sk.FunctionFilters.Add(new DebugFunctionFilter());
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

            do
            {
                try
                {
                    FunctionResult promptResult = await sk.InvokePromptAsync(prompt, new(promptSettings));
                    _log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

                    return promptResult.ToString();
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
        });
    }

    private static string CacheConnectionId { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _receiver.StartAsync(cancellationToken);
        _receiver.On<string>(Constants.SignalR.Functions.RegisterCacheConnection, c => CacheConnectionId = c);

        _log.LogDebug("Wiring up agents...");

        var answer = string.Empty;
        AutoResetEvent awaitingAnswer = new(false);

        var expertFunctions = new List<KernelFunction>(_agents.Count);
        foreach (AgentDefinition a in _agents)
        {
            expertFunctions.Add(_sk.CreateFunctionFromMethod(async (string prompt) =>
            {
                var connId = await _sender.Clients.Client(CacheConnectionId).InvokeAsync<string>(Constants.SignalR.Functions.GetUserConnectionId, a.Name, cancellationToken);
                return await _sender.Clients.Client(connId).InvokeAsync<string>(Constants.SignalR.Functions.GetAnswer, prompt, cancellationToken);
            }, a.Name, a.Description, [new("prompt") { IsRequired = true, ParameterType = typeof(string) }], new() { Description = "Prompt response as a JSON object or array to be inferred upon.", ParameterType = typeof(string) })
            );
        }

        _sk.ImportPluginFromFunctions("Experts", expertFunctions);

        Console.WriteLine("Awaiting question from user...");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
