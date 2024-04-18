namespace SignalRHub;

using System.Collections.Concurrent;

using Common;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class TbaSignalRHub(ILogger<TbaSignalRHub> logger)
{
    private static readonly ConcurrentDictionary<string, string> UserConnections = [];

    [Function("Negotiate")]
    public SignalRConnectionInfo Negotiate([HttpTrigger(AuthorizationLevel.Anonymous, "POST")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = Constants.SignalR.HubName, UserId = "{query.userid}")] SignalRConnectionInfo signalRConnectionInfo)
    {
        logger.LogInformation("Executing negotiation.");
        return signalRConnectionInfo;
    }

    [Function("OnConnected")]
    [SignalROutput(HubName = Constants.SignalR.HubName)]
    public SignalRMessageAction OnConnected([SignalRTrigger(Constants.SignalR.HubName, "connections", "connected")] SignalRInvocationContext invocationContext)
    {
        invocationContext.Headers.TryGetValue("Authorization", out var auth);
        logger.LogInformation($"{invocationContext.ConnectionId} ({invocationContext.UserId}) has connected");
        UserConnections.AddOrUpdate(invocationContext.UserId, invocationContext.ConnectionId, (_, _) => invocationContext.ConnectionId);
        return new SignalRMessageAction("newConnection")
        {
            Arguments = [invocationContext.ConnectionId, auth],
        };
    }

    [Function(Constants.SignalR.Functions.GetAnswer)]
    [SignalROutput(HubName = Constants.SignalR.HubName)]
    public static SignalRMessageAction GetAnswer([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.GetAnswer, "question")] SignalRInvocationContext invocationContext, string question)
    {
        if (UserConnections.TryGetValue(Constants.SignalR.Users.Orchestrator, out var orchConn) && !string.IsNullOrWhiteSpace(orchConn))
        {
            return new SignalRMessageAction(Constants.SignalR.Functions.GetAnswer)
            {
                ConnectionId = orchConn,
                Arguments = [invocationContext.ConnectionId, question]
            };
        }
        else
        {
            return new SignalRMessageAction(Constants.SignalR.Functions.GetAnswer)
            {
                UserId = Constants.SignalR.Users.Orchestrator,
                Arguments = [invocationContext.ConnectionId, question]
            };

        }
    }

    [Function(Constants.SignalR.Functions.GetAnswerFromExpert)]
    [SignalROutput(HubName = Constants.SignalR.HubName)]
    public static SignalRMessageAction GetAnswerFromExpert([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.GetAnswerFromExpert, "target", "expert", "prompt")] SignalRInvocationContext invocationContext, string target, string expert, string prompt)
    {
        if (UserConnections.TryGetValue(expert, out var expertConn) && !string.IsNullOrWhiteSpace(expertConn))
        {
            return new SignalRMessageAction(Constants.SignalR.Functions.GetAnswerFromExpert)
            {
                ConnectionId = expertConn,
                Arguments = [invocationContext.ConnectionId, prompt]
            };
        }
        else
        {
            return new SignalRMessageAction(Constants.SignalR.Functions.GetAnswerFromExpert)
            {
                UserId = expert,
                Arguments = [invocationContext.ConnectionId, prompt]
            };

        }
    }

    [Function(Constants.SignalR.Functions.ExpertAnswerReceived)]
    [SignalROutput(HubName = Constants.SignalR.HubName)]
    public static SignalRMessageAction ExpertAnswerReceived([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.ExpertAnswerReceived, "target", "answer")] SignalRInvocationContext invocationContext, string target, string answer)
    {
        return new SignalRMessageAction(Constants.SignalR.Functions.ExpertAnswerReceived)
        {
            ConnectionId = target,
            Arguments = [answer]
        };
    }
}
