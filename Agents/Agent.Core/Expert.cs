namespace Common;

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Common.Extensions;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using Models.SignalR;

public abstract class Expert : IHostedService
{
    protected HubConnection SignalR { get; private set; } = default!;

    protected readonly IConfiguration _config;
    protected readonly ILogger _log;
    protected readonly Kernel _kernel;
    protected readonly PromptExecutionSettings _promptSettings;
    protected readonly IHttpClientFactory _httpFactory;

    protected Expert(
        [NotNull] IConfiguration appConfig,
        [NotNull] ILoggerFactory loggerFactory,
        [NotNull] IHttpClientFactory httpClientFactory,
        [NotNull] Kernel sk,
        [NotNull] PromptExecutionSettings promptSettings)
    {
        _config = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _kernel = sk ?? throw new ArgumentNullException(nameof(sk));
        _promptSettings = promptSettings ?? throw new ArgumentNullException(nameof(promptSettings));
        _httpFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        this.Name = Throws.IfNullOrWhiteSpace(appConfig.GetRequiredSection("AgentDefinition")["Name"]);
        this.Description = appConfig.GetRequiredSection("AgentDefinition")["Description"];

        _log = loggerFactory?.CreateLogger(this.Name) ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public string Name { get; protected init; }
    public string? Description { get; protected init; }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        AgentDefinition.OutputRegisteredSkFunctions(_kernel, new LogTraceTextWriter(_log));

        await ConnectToSignalRAsync(cancellationToken);

        if (this.PerformsIntroduction)
        {
            await IntroduceAsync(cancellationToken);
            this.SignalR.On(Constants.SignalR.Functions.Reintroduce, () => IntroduceAsync(cancellationToken));
        }

        await AfterSignalRConnectedAsync(cancellationToken);
    }

    protected virtual Task AfterSignalRConnectedAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Awaiting question...");
        this.SignalR.On<string, string>(Constants.SignalR.Functions.GetAnswer, s => GetAnswerAsync(s, cancellationToken));

        return Task.CompletedTask;
    }

    protected virtual bool PerformsIntroduction { get; } = true;

    protected async Task IntroduceAsync(CancellationToken cancellationToken)
    {
        _log.LogDebug("Introducing myself...");
        await this.SignalR.SendAsync(Constants.SignalR.Functions.Introduce, this.Name, _config.GetRequiredSection("AgentDefinition")["Description"], cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task ConnectToSignalRAsync(CancellationToken cancellationToken)
    {
        using IDisposable scope = _log.CreateMethodScope();
        _log.LogDebug("Connecting to SignalR hub...");

        ConnectionInfo? connInfo = default;
        using (IDisposable? negotiationScope = _log.BeginScope("negotiation"))
        {
            var targetEndpoint = $@"{Throws.IfNullOrWhiteSpace(_config["SignalREndpoint"])}?userid={this.Name}";
            HttpClient client = _httpFactory.CreateClient("negotiation");
            HttpResponseMessage hubNegotiateResponse = new();
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    hubNegotiateResponse = await client.PostAsync(targetEndpoint, null, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception e)
                {
                    _log.LogDebug(e, $@"Negotiation failed");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }

            if (hubNegotiateResponse is null)
            {
                _log.LogCritical("Unable to connect to server {signalrHubEndpoint} - Exiting.", _config["SignalREndpoint"]);
                return;
            }

            hubNegotiateResponse.EnsureSuccessStatusCode();

            try
            {
                connInfo = await hubNegotiateResponse.Content.ReadFromJsonAsync<Models.SignalR.ConnectionInfo>().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Error parsing negotiation response");
                _log.LogCritical("Unable to connect to server {signalrHubEndpoint} - Exiting.", _config["SignalREndpoint"]);
                return;
            }
        }

        ArgumentNullException.ThrowIfNull(connInfo);

        IHubConnectionBuilder builder = new HubConnectionBuilder()
            .WithUrl(connInfo.Url, o => o.AccessTokenProvider = connInfo.GetAccessToken)
            .ConfigureLogging(lb =>
            {
                lb.AddConfiguration(_config.GetSection("Logging"));
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            }).WithAutomaticReconnect()
            .WithStatefulReconnect();

        this.SignalR = builder.Build();
        await this.SignalR.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected Task<string> GetAnswerAsync(string prompt, CancellationToken cancellationToken)
    {
        using IDisposable scope = _log.CreateMethodScope();

        return GetAnswerInternalAsync(prompt, cancellationToken);
    }

    protected virtual Task<string> GetAnswerInternalAsync(string prompt, CancellationToken cancellationToken)
    {
        return ExecuteWithThrottleHandlingAsync(async () =>
        {
            string response;
            try
            {
                FunctionResult promptResult = await _kernel.InvokePromptAsync(prompt, new(_promptSettings), cancellationToken: cancellationToken).ConfigureAwait(false);

                _log.LogDebug("Prompt handled. Response: {promptResponse}", promptResult);

                response = promptResult.ToString();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error handling prompt: {prompt}", prompt);

                response = JsonSerializer.Serialize(ex.Message);
            }

            return response;
        }, cancellationToken);
    }

    protected async Task<T> ExecuteWithThrottleHandlingAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken, int maxRetries = 10)
    {
        Exception? lastException = null;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                return await operation();
            }
            catch (HttpOperationException ex)
            {
                lastException = ex;
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && ex.InnerException is Azure.RequestFailedException rex)
                {
                    Azure.Response? resp = rex.GetRawResponse();
                    if (resp?.Headers.TryGetValue("Retry-After", out var waitTime) is true)
                    {
                        _log.LogWarning("Responses Throttled! Waiting {retryAfter} seconds to try again...", waitTime);
                        await Task.Delay(TimeSpan.FromSeconds(int.Parse(waitTime)), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }
        }

        _log.LogError(lastException!, "Max retries exceeded.");
        throw lastException!;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
