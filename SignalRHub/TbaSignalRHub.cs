namespace SignalRHub;

using System;

using Common;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class TbaSignalRHub(IServiceProvider sp, IHttpClientFactory httpClientFactory, LoggerFactory loggerFactory)
{
    private readonly HttpClient HttpClient = httpClientFactory.CreateClient(nameof(TbaSignalRHub));
    private readonly ServiceHubContext _signalRcontext = sp.GetRequiredService<ServiceHubContext>();
    private readonly ILogger _logger = loggerFactory.CreateLogger<TbaSignalRHub>();

    [Function("negotiate")]
    public async Task<HttpResponseData> NegotiateAsync([HttpTrigger(AuthorizationLevel.Anonymous, "POST")] HttpRequestData req, [SignalRConnectionInfoInput(HubName = "Hub", UserId = "{query.userid}")] SignalRConnectionInfo signalRConnectionInfo)
    {
        _logger.LogWarning("Fielding log request!");

        var output = req.CreateResponse();
        await output.WriteAsJsonAsync(await _signalRcontext.NegotiateAsync(new() { UserId = "foo" }));

        return output;
    }

    [Function(Constants.SignalR.Functions.RegisterConnectionId)]
    public static Task RegisterConnectionId([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.RegisterConnectionId)] SignalRInvocationContext invocationContext)
    {
        return Task.CompletedTask;
    }


    [Function(Constants.SignalR.Functions.GetAnswer)]
    public static Task GetAnswer([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.GetAnswer)] SignalRInvocationContext invocationContext)
    {
        return Task.CompletedTask;
    }
}