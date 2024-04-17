using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureLogging(lb =>
        lb.SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole())
    .ConfigureFunctionsWebApplication(c =>
    {
        c.Services.AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights()
            .AddHttpClient();
            //.AddSignalRCore().AddAzureSignalR();
    })
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
