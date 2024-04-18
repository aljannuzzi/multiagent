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

    [Function(Constants.SignalR.Functions.AskExpert)]
    [SignalROutput(HubName = Constants.SignalR.HubName)]
    public static SignalRMessageAction GetAnswerFromExpert([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.AskExpert, "expert", "prompt")] SignalRInvocationContext invocationContext, string expert, string prompt)
    {
        if (UserConnections.TryGetValue(expert, out var expertConn) && !string.IsNullOrWhiteSpace(expertConn))
        {
            return new SignalRMessageAction(Constants.SignalR.Functions.AskExpert)
            {
                ConnectionId = expertConn,
                Arguments = [UserConnections[Constants.SignalR.Users.EndUser], prompt]
            };
        }
        else
        {
            return new SignalRMessageAction(Constants.SignalR.Functions.AskExpert)
            {
                UserId = expert,
                Arguments = [UserConnections[Constants.SignalR.Users.EndUser], prompt]
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


    [Function(Constants.SignalR.Functions.Introduce)]
    [SignalROutput(HubName = Constants.SignalR.HubName)]
    public static SignalRMessageAction Introduce([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, Constants.SignalR.Functions.Introduce, "name", "description")] SignalRInvocationContext invocationContext, string name, string description)
    {
        return new SignalRMessageAction(Constants.SignalR.Functions.Introduce)
        {
            ConnectionId = UserConnections[Constants.SignalR.Users.Orchestrator],
            Arguments = [name, description]
        };
    }


    [Function(nameof(Ping))]
    public static string Ping([SignalRTrigger(Constants.SignalR.HubName, Constants.SignalR.Categories.Messages, nameof(Ping))] SignalRInvocationContext invocationContext) => "pong";
}
