namespace Models.SignalR;
/// <summary>
/// Contains necessary information for a SignalR client to connect to SignalR Service.
/// </summary>
public sealed class ConnectionInfo
{
    /// <summary>
    /// The URL for a client to connect to SignalR Service.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// The access token for a client to connect to SignalR service.
    /// </summary>
    public string AccessToken { get; set; }

    public Task<string?> GetAccessToken() => Task.FromResult(this.AccessToken);
}
