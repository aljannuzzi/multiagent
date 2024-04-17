namespace SignalRHub;
using System.Net;

using Common;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;

public class TbaSignalRHub(ServiceHubContext sender, IHttpClientFactory httpClientFactory) : ServerlessHub(sender)
{
    private readonly HttpClient HttpClient = httpClientFactory.CreateClient(nameof(TbaSignalRHub));

    [Function("negotiate")]
    public static HttpResponseData Negotiate([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = Constants.SignalR.HubName)] string connectionInfo)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.WriteString(connectionInfo);
        return response;
    }
}