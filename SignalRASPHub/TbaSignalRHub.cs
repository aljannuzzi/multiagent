namespace SignalRASPHub;

using System.Collections.Concurrent;

using Common;

using Microsoft.AspNetCore.SignalR;

internal class TbaSignalRHub(ILoggerFactory loggerFactory) : Hub
{
    private static readonly ConcurrentDictionary<string, string> UserConnections = [];
    private readonly ILogger _log = loggerFactory.CreateLogger<TbaSignalRHub>();

    public override async Task OnConnectedAsync()
    {
        var username = this.Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(username))
        {
            _log.LogWarning("UserID empty!");
        }
        else
        {
            UserConnections.AddOrUpdate(username, this.Context.ConnectionId, (_, _) => this.Context.ConnectionId);
            _log.LogDebug("Stored connection {connectionId} for user {userId}", this.Context.ConnectionId, username);
            if (username.EndsWith("expert", StringComparison.InvariantCultureIgnoreCase) is true)
            {
                _log.LogDebug("Expert {expertName} connected.", username);
                await this.Clients.All.SendCoreAsync(Constants.SignalR.Functions.ExpertJoined, [username]);

                _log.LogTrace("All clients notified.");
            }
        }
    }

    [HubMethodName(Constants.SignalR.Functions.GetAnswer)]
    public async Task<string> GetAnswerAsync(string question)
    {
        if (UserConnections.TryGetValue(Constants.SignalR.Users.Orchestrator, out var orchConn) && !string.IsNullOrWhiteSpace(orchConn))
        {
            return await this.Clients.Client(orchConn).InvokeAsync<string>(Constants.SignalR.Functions.GetAnswer, question, default).ConfigureAwait(false);
        }
        else
        {
            _log.LogError("Unable to send GetAnswer request to orchestrator; connection not found!");
            return "ERROR: Orchestrator not connected!";
        }
    }

    [HubMethodName(Constants.SignalR.Functions.AskExpert)]
    public async Task<string> AskExpertAsync(string expertName, string question)
    {
        if (UserConnections.TryGetValue(expertName, out var expertConn) && !string.IsNullOrWhiteSpace(expertConn))
        {
            return await this.Clients.Client(expertConn).InvokeAsync<string>(Constants.SignalR.Functions.GetAnswer, question, default).ConfigureAwait(false);
        }
        else
        {
            _log.LogError("Unable to send GetAnswer request to {expertName}; connection not found!", expertName);
            return $@"ERROR: Expert '{expertName}' is not here!";
        }
    }

    [HubMethodName(Constants.SignalR.Functions.Introduce)]
    public Task IntroduceAsync(string name, string description) => this.Clients.Client(UserConnections[Constants.SignalR.Users.Orchestrator]).SendAsync(Constants.SignalR.Functions.Introduce, name, description);
}
