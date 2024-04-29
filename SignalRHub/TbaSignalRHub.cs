namespace SignalRASPHub;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

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

            await this.Clients.AllExcept(Constants.SignalR.Users.EndUser).SendAsync(Constants.SignalR.Functions.Reintroduce).ConfigureAwait(false);
        }

        _listeners.TryRemove(this.Context.ConnectionId, out _);
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

    [HubMethodName(Constants.SignalR.Functions.Introduce)]
    public async Task IntroduceAsync(string name, string description)
    {
        using IDisposable scope = _log.CreateMethodScope();

        await WaitForUserToConnectAsync(Constants.SignalR.Users.Orchestrator);

        _log.LogDebug("Introduction received: {expertName}", name);

        await this.Clients.Client(UserConnections[Constants.SignalR.Users.Orchestrator]).SendAsync(Constants.SignalR.Functions.Introduce, name, description);
        _log.LogDebug("Introduction for {expertName} sent to Orchestrator", name);
    }

    private static readonly ConcurrentDictionary<string, Channel<string>> _listeners = [];

    [HubMethodName(Constants.SignalR.Functions.WriteToResponseStream)]
    public async Task WriteToResponseStreamAsync(IAsyncEnumerable<string> incomingStream)
    {
        using var scope = _log.CreateMethodScope();

        await WaitForListenerAsync();

        if (_listeners.TryGetValue(this.Context.ConnectionId, out Channel<string>? channel))
        {
            var ended = false;
            await foreach (var s in incomingStream)
            {
                await channel.Writer.WriteAsync(s);
                if (ended = s.EndsWith(Constants.Token.EndToken))
                {
                    break;
                }
            }

            if (!ended)
            {
                await channel.Writer.WriteAsync(Constants.Token.EndToken);
            }
        }
        else
        {
            _log.LogWarning("No listeners subscribed to {user}. Ignoring response stream writing.", this.Context.UserIdentifier);
        }
    }

    [HubMethodName(Constants.SignalR.Functions.WriteChannelToResponseStream)]
    public async Task WriteChannelToResponseStreamAsync(ChannelReader<string> incomingStream)
    {
        using var scope = _log.CreateMethodScope();

        await WaitForListenerAsync();

        if (_listeners.TryGetValue(this.Context.ConnectionId, out Channel<string>? channel))
        {
            var end = false;
            while (!end && await incomingStream.WaitToReadAsync())
            {
                while (!end && incomingStream.TryRead(out var s))
                {
                    await channel.Writer.WriteAsync(s);
                    end = s.EndsWith(Constants.Token.EndToken);
                }
            }
        }
        else
        {
            _log.LogWarning("No listeners subscribed to {user}. Ignoring response stream writing.", this.Context.UserIdentifier);
        }
    }

    [HubMethodName(Constants.SignalR.Functions.ListenToResponseStream)]
    public async Task<ChannelReader<string>> ListenToResponseStreamAsync(string fromUser)
    {
        using var scope = _log.CreateMethodScope();

        var connInfo = await WaitForUserToConnectAsync(Throws.IfNullOrWhiteSpace(fromUser));

        Channel<string> channel = _listeners.GetOrAdd(connInfo, _ => Channel.CreateUnbounded<string>());
        _log.LogTrace("Listener {listener} connected to {user} ({connectionInfo})", this.Context.ConnectionId, fromUser, connInfo);
        return channel;
    }

    [return: NotNull]
    private async Task<string> WaitForUserToConnectAsync(string user, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        _log.LogTrace("Waiting for {user} to connect...", user);

        var waitTask = Task.Run(async () =>
        {
            string? userConn;
            while (!UserConnections.TryGetValue(user, out userConn) || string.IsNullOrWhiteSpace(userConn))
            {
                await Task.Delay(100, cancellationToken);
            }

            return userConn;

        }, cancellationToken);

        Task completedTask = await Task.WhenAny(waitTask, Task.Run(async () => await Task.Delay(timeout ?? TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false), cancellationToken)).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask != waitTask)
        {
            throw new TimeoutException($@"Timed out waiting for user '{user}' to connect.");
        }

        _log.LogDebug("{user} connected.", user);

        return waitTask.Result;
    }

    [return: NotNull]
    private async Task WaitForListenerAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Waiting for listener for {user} ...", this.Context.UserIdentifier);
        var waitTask = Task.Run(async () =>
        {
            while (!_listeners.ContainsKey(this.Context.ConnectionId))
            {
                await Task.Delay(100, cancellationToken);
            }
        }, cancellationToken);

        Task completedTask = await Task.WhenAny(waitTask, Task.Run(async () => await Task.Delay(timeout ?? TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false), cancellationToken)).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask != waitTask)
        {
            throw new TimeoutException($@"Timed out waiting for any listener to connect.");
        }

        _log.LogDebug("One/more listeners connected to {user}.", this.Context.UserIdentifier);
    }
}
