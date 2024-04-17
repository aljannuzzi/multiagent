using Common;

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
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

        b.Services
            .AddSingleton<IUserIdProvider, UserIdProvider>();
            //.AddSignalRCore();
                //.AddAzureSignalR();
        HubConnection hubConn = new HubConnectionBuilder()
            .WithUrl(b.Configuration["SignalREndpoint"]!)
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

class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => Constants.SignalR.Users.EndUser;
}