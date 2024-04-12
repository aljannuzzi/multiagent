namespace Orchestrator;

using System.Collections.Concurrent;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using SignalRSupport;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class Messages(Kernel sk, PromptExecutionSettings promptSettings, ILogger<Messages> Log, AgentHub hub)
{
    private static readonly ConcurrentDictionary<Uri, HubConnection> SignalRClients = new();

    [Function("messages")]
    public async Task<IActionResult> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req, CancellationToken cancellationToken)
    {
        if (req.Headers.TryGetValue("X-SignalR-ConnectionString", out var signalrEndpointValue))
        {
            if (Uri.TryCreate(signalrEndpointValue[0], UriKind.Absolute, out var signalRendpoint) && !string.IsNullOrWhiteSpace(signalrEndpointValue))
            {
                SignalRClients.GetOrAdd(signalRendpoint, _ => new HubConnectionBuilder().WithUrl(signalrEndpointValue!).Build());
            }
            else
            {
                throw new ArgumentException("Missing or empty SignalR Hub Endpoint value");
            }
        }

        using var sr = new StreamReader(req.Body);
        var prompt = await sr.ReadToEndAsync(cancellationToken);

        Log.LogDebug("Handling prompt: {userPrompt}", prompt);

        sk.FunctionFilters.Add(new DebugFunctionFilter());

        do
        {
            try
            {
                FunctionResult promptResult = await sk.InvokePromptAsync(prompt, new(promptSettings), cancellationToken: cancellationToken);

                Log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

                return new OkObjectResult(promptResult.ToString());
            }
            catch (HttpOperationException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && ex.InnerException is Azure.RequestFailedException rex)
                {
                    Azure.Response? resp = rex.GetRawResponse();
                    if (resp?.Headers.TryGetValue("Retry-After", out var waitTime) is true)
                    {
                        Log.LogWarning("Responses Throttled! Waiting {retryAfter} seconds to try again...", waitTime);
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
    }
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}
