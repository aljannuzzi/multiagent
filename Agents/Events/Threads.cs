namespace Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

public class Threads(ILogger<Threads> logger)
{
    private readonly ILogger<Threads> _logger = logger;

    [Function("threads")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("Events API called: {requestBody}", new StreamReader(req.Body).ReadToEnd());
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}
