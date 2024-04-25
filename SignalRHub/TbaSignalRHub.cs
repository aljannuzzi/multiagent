namespace SignalRASPHub;

using System.Collections.Concurrent;

using Common;
using Common.Extensions;

using Microsoft.AspNetCore.SignalR;

internal class TbaSignalRHub(ILoggerFactory loggerFactory) : Hub
{
    private static readonly ConcurrentDictionary<string, string> UserConnections = [];
    private readonly ILogger _log = loggerFactory.CreateLogger<TbaSignalRHub>();

    public override async Task OnConnectedAsync()
    {
        using IDisposable scope = _log.CreateMethodScope();
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
                await this.Clients.User(Constants.SignalR.Users.EndUser).SendAsync(Constants.SignalR.Functions.ExpertJoined, username).ConfigureAwait(false);

                _log.LogTrace("All clients notified.");
            }
            else if (username == Constants.SignalR.Users.Orchestrator)
            {
                _log.LogInformation("Orchestrator connected");
                _orchestratorWaiter.Set();
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        using IDisposable scope = _log.CreateMethodScope();
        if (this.Context.UserIdentifier?.EndsWith("Expert", StringComparison.InvariantCultureIgnoreCase) is true)
        {
            await this.Clients.Users([Constants.SignalR.Users.Orchestrator, Constants.SignalR.Users.EndUser])
                .SendAsync(Constants.SignalR.Functions.ExpertLeft, this.Context.UserIdentifier).ConfigureAwait(false);
        }
        else if (this.Context.UserIdentifier is "Orchestrator")
        {
            _log.LogDebug("Orchestrator disconnected. Requesting reintroductions from experts...");
            _orchestratorWaiter.Reset();

            await this.Clients.AllExcept(Constants.SignalR.Users.EndUser).SendAsync(Constants.SignalR.Functions.Reintroduce).ConfigureAwait(false);
        }
    }

    [HubMethodName(Constants.SignalR.Functions.GetAnswer)]
    public async Task<string> GetAnswerAsync(string question)
    {
        using IDisposable scope = _log.CreateMethodScope();
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
        using IDisposable scope = _log.CreateMethodScope();
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

    private static readonly ManualResetEventSlim _orchestratorWaiter = new(false);

    [HubMethodName(Constants.SignalR.Functions.Introduce)]
    public async Task IntroduceAsync(string name, string description)
    {
        using IDisposable scope = _log.CreateMethodScope();
        _log.LogDebug("Introduction received: {expertName}", name);
        if (!_orchestratorWaiter.IsSet)
        {
            _log.LogWarning("No orchestrator yet. Waiting...");
        }

        _orchestratorWaiter.Wait();

        await this.Clients.Client(UserConnections[Constants.SignalR.Users.Orchestrator]).SendAsync(Constants.SignalR.Functions.Introduce, name, description);
        _log.LogDebug("Introduction for {expertName} sent to Orchestrator", name);
    }
}
