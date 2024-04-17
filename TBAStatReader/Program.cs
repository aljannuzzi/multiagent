using Common;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SignalRConnectionCache;

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

        ArgumentException.ThrowIfNullOrWhiteSpace(b.Configuration["SignalRConnectionString"]);
        var signalRConnString = b.Configuration["SignalRConnectionString"];
        var options = new ServiceManagerOptions
        {
            ConnectionString = signalRConnString
        };

        b.Services.AddSingleton<Hub, ConnectionTrackingHub>();

        ServiceManager signalRserviceManager = new ServiceManagerBuilder()
            .WithOptions(o =>
            {
                o.ConnectionString = options.ConnectionString;
                o.ServiceTransportType = ServiceTransportType.Persistent;
            })
            .WithLoggerFactory(loggerFactory)
            .BuildServiceManager();
        ServiceHubContext signalRhub = await signalRserviceManager.CreateHubContextAsync(b.Configuration["SignalRHubName"] ?? Constants.SignalR.HubName, cts.Token);
        Microsoft.AspNetCore.Http.Connections.NegotiationResponse userNegotiation = await signalRhub.NegotiateAsync(new NegotiationOptions
        {
            UserId = Constants.SignalR.Users.EndUser,
            EnableDetailedErrors = true
        });

        b.Services.AddSingleton(signalRhub);
        HubConnection hubConn = new HubConnectionBuilder()
            .WithUrl(userNegotiation.Url!, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(userNegotiation.AccessToken);
                options.HttpMessageHandlerFactory = f => new DebugHttpHandler(loggerFactory, f);
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
            })
            .Build();

        b.Services.AddSingleton(hubConn);

        //b.Services.AddHttpClient("Orchestrator", (sp, c) =>
        //    {
        //        IConfiguration config = sp.GetRequiredService<IConfiguration>();
        //        ILogger? log = sp.GetService<ILoggerFactory>()?.CreateLogger("OrchestratorClientCreator");
        //        c.BaseAddress = new(sp.GetRequiredService<IConfiguration>()["OrchestratorEndpoint"] ?? throw new ArgumentNullException("Endpoint missing for 'Orchestrator' configuration options"));
        //        log?.LogTrace("SignalR Connection String: {SignalRConnectionString}", signalRConnString);

        //        if (!string.IsNullOrWhiteSpace(signalRConnString))
        //        {
        //            c.DefaultRequestHeaders.Add("X-SignalR-Hub-ConnectionString", signalRConnString);
        //        }
        //    })
        //    .AddHttpMessageHandler<DebugHttpHandler>();

        await b.Build().RunAsync(cts.Token);
    }
}