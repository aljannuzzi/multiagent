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
        b.Services.AddHostedService<Worker>()
            .AddHttpClient()
            .AddSingleton(sp => new TBAAPI.V3Client.Client.Configuration(new Dictionary<string, string>(), new Dictionary<string, string>() { { "X-TBA-Auth-Key", sp.GetRequiredService<IConfiguration>().GetValue<string>("TBA_API_KEY")! } }, new Dictionary<string, string>()) { DateTimeFormat = "yyyy-MM-dd" })
            .AddSingleton(_ => new TBAAPI.V3Client.Client.ApiClient("https://www.thebluealliance.com/api/v3"))
            .AddLogging(lb =>
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                }));

        await b.Build().RunAsync(cts.Token);
    }
}
