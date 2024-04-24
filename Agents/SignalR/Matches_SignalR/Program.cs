namespace Teams_SignalR;
using System.Net.Http.Json;

using Assistants;

using Azure.Identity;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using TBAAPI.V3Client.Api;
using TBAAPI.V3Client.Client;

using TBAStatReader;

internal partial class Program
{
    private static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        cts.Token.Register(() => Console.WriteLine("Cancellation requested. Exiting..."));

        HostApplicationBuilder b = Host.CreateApplicationBuilder(args);
        b.Services.AddHostedService<Worker>()
            .AddHttpClient()
            .AddTransient<DebugHttpHandler>()
            .AddLogging(lb =>
            {
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            })
            .AddHttpLogging(o => o.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestBody | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseBody);

        ILoggerFactory loggerFactory = b.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

        ArgumentException.ThrowIfNullOrWhiteSpace(b.Configuration["SignalREndpoint"]);

        var client = new HttpClient();
        HttpResponseMessage hubNegotiateResponse = new();
        ILogger negotiationLogger = loggerFactory.CreateLogger("negotiation");
        for (int i = 0; i < 10; i++)
        {
            try
            {
                hubNegotiateResponse = await client.PostAsync($@"{b.Configuration["SignalREndpoint"]}?userid={b.Configuration.GetSection("ExpertDefinition")["Name"]!}", null, cts.Token).ConfigureAwait(false);
                break;
            }
            catch (Exception e)
            {
                negotiationLogger.LogDebug(e, $@"Negotiation failed");
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        if (hubNegotiateResponse is null)
        {
            negotiationLogger.LogCritical("Unable to connect to server {signalrHubEndpoint} - Exiting.", b.Configuration["SignalREndpoint"]);
            return;
        }

        hubNegotiateResponse.EnsureSuccessStatusCode();

        Models.SignalR.ConnectionInfo? connInfo;
        try
        {
            connInfo = await hubNegotiateResponse.Content.ReadFromJsonAsync<Models.SignalR.ConnectionInfo>().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            negotiationLogger.LogDebug(ex, "Error parsing negotiation response");
            negotiationLogger.LogCritical("Unable to connect to server {signalrHubEndpoint} - Exiting.", b.Configuration["SignalREndpoint"]);
            return;
        }

        ArgumentNullException.ThrowIfNull(connInfo);

        HubConnection hubConn = new HubConnectionBuilder()
            .WithUrl(connInfo.Url, o => o.AccessTokenProvider = connInfo.GetAccessToken)
            .ConfigureLogging(lb =>
            {
                lb.AddConfiguration(b.Configuration.GetSection("Logging"));
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            }).WithAutomaticReconnect()
            .Build();

        b.Services
            .AddSingleton(hubConn)
            .AddSingleton<PromptExecutionSettings>(new OpenAIPromptExecutionSettings
            {
                ChatSystemPrompt = b.Configuration["SystemPrompt"] ?? throw new ArgumentNullException("SystemPrompt", "Missing SystemPrompt environment variable"),
                Temperature = 0.1,
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                User = Environment.MachineName
            })
            .AddSingleton(sp =>
            {
                IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
                kernelBuilder.Services.AddSingleton(loggerFactory);
                kernelBuilder.Plugins.AddFromType<Calendar>();
                kernelBuilder.Plugins.AddFromObject(new MatchApi(new Configuration(new Dictionary<string, string>(),
                    new Dictionary<string, string>() { { "X-TBA-Auth-Key", b.Configuration["TBA_API_KEY"] ?? throw new ArgumentNullException("TBA_API_KEY", "Missing TBA_API_KEY environment variable") } },
                    new Dictionary<string, string>()))
                { Log = loggerFactory.CreateLogger(nameof(MatchApi)) });

                if (b.Configuration["AzureOpenAIKey"] is not null)
                {
                    kernelBuilder.AddAzureOpenAIChatCompletion(
                        b.Configuration["AzureOpenDeployment"]!,
                        b.Configuration["AzureOpenAIEndpoint"]!,
                        b.Configuration["AzureOpenAIKey"]!,
                        httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
                }
                else
                {
                    kernelBuilder.AddAzureOpenAIChatCompletion(
                        b.Configuration["AzureOpenDeployment"]!,
                        b.Configuration["AzureOpenAIEndpoint"]!,
                        new DefaultAzureCredential(),
                        httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
                }

                Kernel kernel = kernelBuilder.Build();
                return kernel;
            });

        await b.Build().RunAsync(cts.Token).ConfigureAwait(false);

    }
}