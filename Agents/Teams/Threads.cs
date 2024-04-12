namespace Matches;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

public class Threads(Kernel sk, PromptExecutionSettings promptSettings, ILogger<Threads> Log)
{
    [Function("threads")]
    public async Task<IActionResult> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req, CancellationToken cancellationToken)
    {
        using var sr = new StreamReader(req.Body);
        var prompt = await sr.ReadToEndAsync(cancellationToken);

        Log.LogDebug("Handling prompt: {userPrompt}", prompt);

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
}
