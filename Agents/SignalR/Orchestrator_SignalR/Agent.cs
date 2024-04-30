namespace Orchestrator_SignalR;

using System;
using System.Threading;
using System.Threading.Tasks;

using Common;
using Common.Extensions;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

internal class Agent(IConfiguration configuration, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, Kernel kernel, PromptExecutionSettings promptSettings) : Expert(configuration, loggerFactory, httpClientFactory, kernel, promptSettings)
{
    protected override bool PerformsIntroduction { get; } = false;

    protected override async Task AfterSignalRConnectedAsync(CancellationToken cancellationToken)
    {
        this.SignalR.On<string, string>(Constants.SignalR.Functions.Introduce, (n, d) => AddExpert(n, d, cancellationToken));
        this.SignalR.On<string>(Constants.SignalR.Functions.ExpertLeft, RemoveExpert);

        await base.AfterSignalRConnectedAsync(cancellationToken);
    }

    private void AddExpert(string name, string description, CancellationToken cancellationToken)
    {
        using IDisposable scope = _log.CreateMethodScope();

        _log.LogDebug("Adding {expertName} to panel...", name);
        _kernel.ImportPluginFromFunctions(name, [_kernel.CreateFunctionFromMethod(async (string prompt) =>
            {
                var answerStream = await this.SignalR.StreamAsChannelAsync<string>(Constants.SignalR.Functions.GetAnswer, name, prompt, cancellationToken);
                await this.SignalR.SendAsync(Constants.SignalR.Functions.SendAnswerBack, _kernel.Data["channelId"], answerStream);
            },
            name, description,
            [new ("prompt") { IsRequired = true, ParameterType = typeof(string) }],
            new () { Description = "Prompt response as a JSON object or array to be inferred upon.", ParameterType = typeof(string) })]
        );

        _log.LogTrace("Expert {expertName} added.", name);
    }

    private void RemoveExpert(string name)
    {
        using IDisposable scope = _log.CreateMethodScope();
        _log.LogDebug("Removing {expertName} from panel...", name);

        _kernel.Plugins.Remove(_kernel.Plugins[name]);

        _log.LogTrace("Expert {expertName} removed.", name);
    }
}
