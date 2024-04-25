namespace TBAStatReader;

using System;
using System.Threading;
using System.Threading.Tasks;

using Common;
using Common.Extensions;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.Threading;

internal class Agent(IConfiguration configuration, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, Kernel kernel, PromptExecutionSettings promptSettings) : Expert(configuration, loggerFactory, httpClientFactory, kernel, promptSettings)
{
    protected override bool PerformsIntroduction { get; } = false;

    protected override Task<string> GetAnswerInternalAsync(string prompt, CancellationToken cancellationToken) => base.GetAnswerInternalAsync(prompt, cancellationToken);

    protected override async Task AfterSignalRConnectedAsync(CancellationToken cancellationToken)
    {
        this.SignalR.On<string, string>(Constants.SignalR.Functions.Introduce, (n, d) => AddExpert(n, d, cancellationToken));
        this.SignalR.On<string>(Constants.SignalR.Functions.ExpertLeft, RemoveExpert);

        await base.AfterSignalRConnectedAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddExpert(string name, string description, CancellationToken cancellationToken)
    {
        using IDisposable scope = _log.CreateMethodScope();
        _log.LogDebug("Adding {expertName} to panel...", name);
        _kernel.ImportPluginFromFunctions(name, [_kernel.CreateFunctionFromMethod((string prompt) => this.SignalR!.InvokeAsync<string>(Constants.SignalR.Functions.AskExpert, name, prompt, cancellationToken),
            name, description,
            [new("prompt") { IsRequired = true, ParameterType = typeof(string) }],
            new() { Description = "Prompt response as a JSON object or array to be inferred upon.", ParameterType = typeof(string) })]
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
