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
    private readonly AutoResetEvent waitingOnExpert = new(false);
    private static TaskCompletionSource<string> expertAnswer = new();
    private static readonly JoinableTaskFactory TaskFactory = new(new JoinableTaskContext());

    private readonly IDisposable _questionHandler;
    private readonly IDisposable _answerHandler;
    private readonly Kernel _sk;
    private readonly HubConnection _receiver;
    private readonly IServiceHubContext _sender;
    private readonly ImmutableList<AgentDefinition> _agents;

    public Worker(Kernel sk, PromptExecutionSettings promptSettings, ILoggerFactory loggerFactory, HubConnection receiver, IServiceHubContext sender, ImmutableList<AgentDefinition> agents)
    {
        _log = loggerFactory.CreateLogger("Orchestrator");
        _sk = sk;
        _receiver = receiver;
        _sender = sender;
        _agents = agents;

        _answerHandler = receiver.On<string>("answer", s =>
        {
            System.Diagnostics.Debug.WriteLine($@"***** {s} *****");
            //expertAnswer.SetResult(s);
        });

        _questionHandler = receiver.On<string, string>("question", async (prompt, conn) =>
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

                   await sender.Clients.Client(conn).SendAsync("answer", promptResult.ToString());
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
       });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _log.LogDebug("Wiring up agents...");

        var answer = string.Empty;
        AutoResetEvent awaitingAnswer = new(false);
        await _receiver.StartAsync(cancellationToken);

        var expertFunctions = new List<KernelFunction>(_agents.Count);
        foreach (AgentDefinition a in _agents)
        {
            expertFunctions.Add(_sk.CreateFunctionFromMethod(async (string prompt) =>
            {
                expertAnswer = new();
                await _sender.Clients.User(a.Name).SendAsync("question", prompt, _receiver.ConnectionId, cancellationToken);

                return await expertAnswer.Task;
            }, a.Name, a.Description, [new("prompt") { IsRequired = true, ParameterType = typeof(string) }], new() { Description = "Prompt response as a JSON object or array to be inferred upon.", ParameterType = typeof(string) })
            );
        }

        _sk.ImportPluginFromFunctions("Experts", expertFunctions);

        Console.WriteLine("Awaiting question from user...");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
