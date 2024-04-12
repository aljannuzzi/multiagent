using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

using Azure.Identity;

using Common;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

IHost host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights()
            .AddHttpClient()
            .AddSingleton<ImmutableList<AgentDefinition>>(JsonSerializer.Deserialize<List<AgentDefinition>>(Environment.GetEnvironmentVariable("Agents") ?? throw new ArgumentNullException("Agents", "Missing Agents environment variable"))?.ToImmutableList() ?? throw new ArgumentException("Unable to deserialize 'Agents' environment variable"));

        services.AddLogging(lb =>
        {
            lb.AddFilter("Microsoft.SemanticKernel", LogLevel.Trace);
            lb.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                o.IncludeScopes = true;
            });
        })
        .AddHttpLogging(o => o.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestBody);

        services.AddSingleton<PromptExecutionSettings>(new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = Environment.GetEnvironmentVariable("SystemPrompt") ?? throw new ArgumentNullException("SystemPrompt", "Missing SystemPrompt environment variable"),
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            User = Environment.MachineName
        });

        services.AddSingleton(sp =>
        {
            IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            ImmutableList<AgentDefinition> agents = sp.GetRequiredService<ImmutableList<AgentDefinition>>();

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton(loggerFactory);
            kernelBuilder.Plugins.AddFromType<Calendar>();

            if (Environment.GetEnvironmentVariable("AzureOpenAIKey") is not null)
            {
                kernelBuilder.AddAzureOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("AzureOpenDeployment")!,
                    Environment.GetEnvironmentVariable("AzureOpenAIEndpoint")!,
                    Environment.GetEnvironmentVariable("AzureOpenAIKey")!,
                    httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
            }
            else
            {
                kernelBuilder.AddAzureOpenAIChatCompletion(
                    Environment.GetEnvironmentVariable("AzureOpenDeployment")!,
                    Environment.GetEnvironmentVariable("AzureOpenAIEndpoint")!,
                    new DefaultAzureCredential(),
                    httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
            }

            Kernel kernel = kernelBuilder.Build();

            var expertFunctions = new List<KernelFunction>(agents.Count);
            foreach (AgentDefinition a in agents)
            {
                expertFunctions.Add(kernel.CreateFunctionFromMethod(async (string prompt) =>
                {
                    HttpClient client = httpClientFactory.CreateClient(a.Name);
                    client.BaseAddress = a.Endpoint;
                    await client.PostAsync("api/threads", new StringContent(prompt));
                }, a.Name, a.Description, [new("prompt") { IsRequired = true, ParameterType = typeof(string) }])
                );
            }

            kernel.ImportPluginFromFunctions("Experts", expertFunctions);
            return kernel;
        });
    })
    .Build();

host.Run();
