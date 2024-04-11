using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using TBAStatReader;

internal class Program
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
            .AddSingleton(sp => new TBAAPI.V3Client.Client.Configuration(new Dictionary<string, string>(),
                new Dictionary<string, string>() { { "X-TBA-Auth-Key", sp.GetRequiredService<IConfiguration>().GetValue<string>("TBA_API_KEY")! } },
                new Dictionary<string, string>())
            { DateTimeFormat = "yyyy-MM-dd" })
            .AddSingleton(_ => new TBAAPI.V3Client.Client.ApiClient("https://www.thebluealliance.com/api/v3"))
            .AddLogging(lb =>
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                }));

        b.Services
            .AddTransient<DebugHandler>();

        b.Services.AddHttpClient("Orchestrator", (sp, c) => c.BaseAddress = new(sp.GetRequiredService<IConfiguration>()["OrchestratorEndpoint"] ?? throw new ArgumentNullException("Endpoint missing for 'Orchestrator' configuration options")))
            .AddHttpMessageHandler<DebugHandler>();

        await b.Build().RunAsync(cts.Token);
    }

    class DebugHandler(ILoggerFactory loggerFactory) : DelegatingHandler
    {
        private readonly ILogger _log = loggerFactory.CreateLogger<DebugHandler>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_log.IsEnabled(LogLevel.Trace) && request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _log.LogTrace("{requestBody}", body);
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
