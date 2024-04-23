namespace Matches;

using Json.More;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

public class Threads
{
    private readonly Kernel sk;
    private readonly PromptExecutionSettings promptSettings;
    private readonly ILogger<Threads> log;

    public Threads(Kernel sk, PromptExecutionSettings promptSettings, ILogger<Threads> Log)
    {
        this.sk = sk;
        this.promptSettings = promptSettings;
        log = Log;

        log.LogCritical("crit");
        log.LogError("err");
        log.LogWarning("warn");
        log.LogInformation("info");
        log.LogDebug("debug");
        log.LogTrace("trace");
    }

    [Function("threads")]
    public async Task<IActionResult> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req, CancellationToken cancellationToken)
    {
        using var sr = new StreamReader(req.Body);
        var prompt = await sr.ReadToEndAsync(cancellationToken);

        log.LogDebug("Handling prompt: {userPrompt}", prompt);

        do
        {
            try
            {
                FunctionResult promptResult = await sk.InvokePromptAsync(prompt, new(promptSettings), cancellationToken: cancellationToken);

                var funcResult = new OkObjectResult(promptResult.ToString()) { ContentTypes = ["application/json"] };
                log.LogDebug("Function Returning: {result}", funcResult.ToJsonDocument().RootElement);

                return funcResult;
            }
            catch (HttpOperationException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && ex.InnerException is Azure.RequestFailedException rex)
                {
                    Azure.Response? resp = rex.GetRawResponse();
                    if (resp?.Headers.TryGetValue("Retry-After", out var waitTime) is true)
                    {
                        log.LogWarning("Responses Throttled! Waiting {retryAfter} seconds to try again...", waitTime);
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
}
