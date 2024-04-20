namespace Orchestrator_SignalR;
using System.Net.Http.Json;

using Assistants;

using Azure.Identity;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

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

        var client = new HttpClient();
        HttpResponseMessage hubNegotiateResponse = new();
        ILogger negotiationLogger = loggerFactory.CreateLogger("negotiation");
        for (int i = 0; i < 10; i++)
        {
            try
            {
                hubNegotiateResponse = await client.PostAsync($@"{b.Configuration["SignalREndpoint"]}?userid={Constants.SignalR.Users.Orchestrator}", null, cts.Token);
                break;
            }
            catch (Exception e)
            {
                negotiationLogger.LogDebug(e, $@"Negotiation failed");
                await Task.Delay(1000);
            }
        }

        if (hubNegotiateResponse is null)
        {
            negotiationLogger.LogCritical("Unable to connect to server. Exiting.");
            return;
        }

        hubNegotiateResponse.EnsureSuccessStatusCode();

        Models.SignalR.ConnectionInfo? connInfo;
        try
        {
            connInfo = await hubNegotiateResponse.Content.ReadFromJsonAsync<Models.SignalR.ConnectionInfo>();
        }
        catch (Exception ex)
        {
            negotiationLogger.LogDebug(ex, "Error parsing negotiation response");
            negotiationLogger.LogCritical("Unable to connect to server. Exiting.");
            return;
        }

        HubConnection hubConn = new HubConnectionBuilder()
            .WithUrl(connInfo.Url, o =>
            {
                o.AccessTokenProvider = connInfo.GetAccessToken;
                o.UseDefaultCredentials = true;
            })
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

                var log = loggerFactory.CreateLogger("SKSetup");
                if (b.Configuration["AzureOpenAIKey"] is not null)
                {
                    log.LogTrace("Using AzureOpenAIKey {AzureOpenAIKey}", b.Configuration["AzureOpenAIKey"]);

                    kernelBuilder.AddAzureOpenAIChatCompletion(
                        b.Configuration["AzureOpenDeployment"]!,
                        b.Configuration["AzureOpenAIEndpoint"]!,
                        b.Configuration["AzureOpenAIKey"]!,
                        httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
                }
                else
                {
                    log.LogWarning("AzureOpenAIKey not found. Using DefaultAzureCredential.");

                    kernelBuilder.AddAzureOpenAIChatCompletion(
                        b.Configuration["AzureOpenDeployment"]!,
                        b.Configuration["AzureOpenAIEndpoint"]!,
                        new DefaultAzureCredential(),
                        httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
                }

                Kernel kernel = kernelBuilder.Build();
                return kernel;
            });

        await b.Build().RunAsync(cts.Token);

    }
}