namespace Events;

using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

public class Threads(Kernel sk, PromptExecutionSettings promptSettings, ILogger<Threads> Log)
{
    [Function("threads")]
    public async Task<IActionResult> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        using var sr = new StreamReader(req.Body);
        var prompt = await sr.ReadToEndAsync();

        Log.LogDebug("Handling prompt: {userPrompt}", prompt);

        FunctionResult promptResult = await sk.InvokePromptAsync(prompt, new(promptSettings));

        Log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

        return new OkObjectResult(promptResult.ToString());
    }
}
