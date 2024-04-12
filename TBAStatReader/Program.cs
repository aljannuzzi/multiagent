using System.Text.Json;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

        HostApplicationBuilder b = Host.CreateApplicationBuilder(args);
        b.Configuration.AddUserSecrets<Program>();
        b.Services.AddHostedService<Worker>()
            .AddTransient<DebugHttpHandler>()
            .AddLogging(lb =>
            {
                lb.SetMinimumLevel(LogLevel.Trace);
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            })
            .AddHttpLogging(o => o.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestBody | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseBody);

        var signalRConnString = b.Configuration["SignalRConnectionString"];
        if (!string.IsNullOrWhiteSpace(signalRConnString))
        {
            var options = new ServiceManagerOptions
            {
                ConnectionString = signalRConnString
            };

            var signalRserviceManager = new ServiceManagerBuilder()
                .WithOptions(o => o.ConnectionString = options.ConnectionString)
                .WithLoggerFactory(b.Services.BuildServiceProvider().GetService<ILoggerFactory>())
                //.AddHubProtocol<JsonHubProtocol>()
                .BuildServiceManager();
            var signalRhub = await signalRserviceManager.CreateHubContextAsync(b.Configuration["SignalRHubName"] ?? "TBABot", cts.Token);
            var negotiation = await signalRhub.NegotiateAsync(new NegotiationOptions
            {
                UserId = b.Configuration["SignalRUserId"] ?? Environment.MachineName,
                EnableDetailedErrors = true
            });

            b.Services.AddSingleton(sp =>
            {
                return new HubConnectionBuilder()
                    .WithUrl(negotiation.Url!, options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(negotiation.AccessToken);
                    })
                    .WithAutomaticReconnect()
                    .ConfigureLogging(lb =>
                    {
                        lb.SetMinimumLevel(LogLevel.Trace);
                        lb.AddSimpleConsole(o =>
                        {
                            o.SingleLine = true;
                            o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                            o.IncludeScopes = true;
                        });
                    })
                    .AddJsonProtocol(o => o.PayloadSerializerOptions = new(JsonSerializerDefaults.Web))
                    .Build();
            });
        }

        b.Services.AddHttpClient("Orchestrator", (sp, c) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var log = sp.GetService<ILoggerFactory>()?.CreateLogger("OrchestratorClientCreator");
                c.BaseAddress = new(sp.GetRequiredService<IConfiguration>()["OrchestratorEndpoint"] ?? throw new ArgumentNullException("Endpoint missing for 'Orchestrator' configuration options"));
                log?.LogTrace("SignalR Connection String: {SignalRConnectionString}", signalRConnString);

                if (!string.IsNullOrWhiteSpace(signalRConnString))
                {
                    c.DefaultRequestHeaders.Add("X-SignalR-Hub-ConnectionString", signalRConnString);
                }
            })
            .AddHttpMessageHandler<DebugHttpHandler>();

        await b.Build().RunAsync(cts.Token);

    }
}
