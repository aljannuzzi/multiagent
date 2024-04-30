﻿namespace SignalRASPHub;

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
    }

    [HubMethodName(Constants.SignalR.Functions.GetAnswer)]
    public async Task<ChannelReader<string>> GetAnswerAsync(string targetUser, string question, CancellationToken cancellationToken)
    {
        using IDisposable scope = _log.CreateMethodScope();

        var id = Guid.NewGuid().ToString();
        var tcs = Channel.CreateUnbounded<string>(new() { SingleWriter = true, SingleReader = true, AllowSynchronousContinuations = true });
        if (!_completions.TryAdd(id, tcs))
        {
            _log.LogWarning("Error adding completion to dictionary!");
        }

        var conn = await WaitForUserToConnectAsync(targetUser, cancellationToken: cancellationToken);
        await this.Clients.Client(conn).SendAsync("SendAnswerBack", id, question, cancellationToken);
        return tcs.Reader;
    }

    [HubMethodName(Constants.SignalR.Functions.SendAnswerBack)]
    public async Task SendAnswerBackAsync(string completionId, IAsyncEnumerable<string> answerStream)
    {
        using IDisposable scope = _log.CreateMethodScope();

        _log.BeginScope("User[{callingUserId}]", this.Context.UserIdentifier ?? "unknown user");

        if (!_completions.TryRemove(completionId, out var completion))
        {
            _log.LogWarning("Unable to get completion {completionId} from dictionary!", completionId);
        }
        else
        {
            _log.LogTrace("Writing to stream");
            await foreach (var s in answerStream)
            {
                if (!completion.Writer.TryWrite(s))
                {
                    _log.LogWarning("Error writing token!");
                }
            }

            completion.Writer.Complete();
        }

        _log.LogTrace("Completed.");
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

    private static readonly ConcurrentDictionary<string, Channel<string>> _completions = [];

    [return: NotNull]
    private async Task<string> WaitForUserToConnectAsync([NotNull] string user, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        _log.LogTrace("Waiting for {user} to connect...", Throws.IfNullOrWhiteSpace(user));

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

        return await waitTask;
    }
}
