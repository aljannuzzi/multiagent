using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


internal class Program
{
    private static void Main(string[] args)
    {
        var builder = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureLogging(lb =>
                lb.SetMinimumLevel(LogLevel.Debug)
                    .AddSimpleConsole());

        builder.Build().Run();
    }
}