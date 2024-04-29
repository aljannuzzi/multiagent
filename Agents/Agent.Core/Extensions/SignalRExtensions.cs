namespace Agent.Core.Extensions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Common;

using Microsoft.AspNetCore.SignalR.Client;

public static class SignalRExtensions
{
    public static async IAsyncEnumerable<string> ListenForNewResponseAsync(this HubConnection connection, string user, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        System.Threading.Channels.ChannelReader<string> ans = await connection.StreamAsChannelAsync<string>(Constants.SignalR.Functions.ListenToResponseStream, user, cancellationToken);
        string? lastToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (ans.TryRead(out var token))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (token.Contains(Constants.Token.EndToken))
                {
                    lastToken = token.Replace(Constants.Token.EndToken, string.Empty);
                    break;
                }

                yield return token;
            }
        } while (lastToken is null && await ans.WaitToReadAsync(cancellationToken));

        yield return lastToken ?? string.Empty;
    }
}
