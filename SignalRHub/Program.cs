using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureLogging(lb =>
                lb.SetMinimumLevel(LogLevel.Trace)
                    .AddSimpleConsole());

        builder.Build().Run();
    }
}