using Microsoft.Extensions.Hosting;


internal class Program
{
    private static void Main(string[] args)
    {
        var builder = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults();

        builder.Build().Run();
    }
}