using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights()
            .AddSignalRCore()
                .AddAzureSignalR()
                .AddJsonProtocol(o => o.PayloadSerializerOptions.WriteIndented = false);
    })
    .Build();

host.Run();
