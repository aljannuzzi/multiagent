using System.Collections.Immutable;
using System.Text.Json;

using Common;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddSingleton<ImmutableList<AgentDefinition>>(JsonSerializer.Deserialize<List<AgentDefinition>>(Environment.GetEnvironmentVariable("Agents") ?? throw new ArgumentNullException("Missing Agents environment variable"))?.ToImmutableList() ?? throw new ArgumentException("Unable to deserialize 'Agents' environment variable"));
    })
    .Build();

host.Run();
