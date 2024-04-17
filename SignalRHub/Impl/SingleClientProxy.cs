// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SignalRHub.Impl;
using Microsoft.AspNetCore.SignalR;

internal sealed class SingleClientProxy : ISingleClientProxy
{
    private readonly IClientProxy _clientProxy;
    private readonly string _memberName;

    public SingleClientProxy(IClientProxy clientProxy, string memberName)
    {
        _clientProxy = clientProxy;
        _memberName = memberName;
    }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
        _clientProxy.SendCoreAsync(method, args, cancellationToken);

    public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException($"The default implementation of {_memberName} does not support client return results.");
}

internal sealed class UserProxy<THub> : IClientProxy where THub : Hub
{
    private readonly string _userId;
    private readonly HubLifetimeManager<THub> _lifetimeManager;

    public UserProxy(HubLifetimeManager<THub> lifetimeManager, string userId)
    {
        ArgumentNullException.ThrowIfNull(userId);

        _lifetimeManager = lifetimeManager;
        _userId = userId;
    }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        return _lifetimeManager.SendUserAsync(_userId, method, args, cancellationToken);
    }
}