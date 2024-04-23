﻿namespace Common;
using Microsoft.Extensions.Logging;

public class DebugHttpHandler(ILoggerFactory loggerFactory) : DelegatingHandler
{
    private readonly ILogger _log = loggerFactory.CreateLogger<DebugHttpHandler>();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_log.IsEnabled(LogLevel.Trace) && request.Content is not null)
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _log.LogTrace("*** REQUEST {requestBody}", body);
        }

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (_log.IsEnabled(LogLevel.Trace) && response.Content is not null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _log.LogTrace("*** RESPONSE {responseContent}", body);
        }

        return response;
    }
}