using Common;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            .AddTransient<DebugHttpHandler>();

        b.Services.AddHttpClient("Orchestrator", (sp, c) => c.BaseAddress = new(sp.GetRequiredService<IConfiguration>()["OrchestratorEndpoint"] ?? throw new ArgumentNullException("Endpoint missing for 'Orchestrator' configuration options")))
            .AddHttpMessageHandler<DebugHttpHandler>();

        await b.Build().RunAsync(cts.Token);
    }
}
