using System.Collections.Immutable;
using System.Text.Json;

using Assistants;

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
        ImmutableList<AgentDefinition> agents = JsonSerializer.Deserialize<List<AgentDefinition>>(Environment.GetEnvironmentVariable("Agents") ?? throw new ArgumentNullException("Agents", "Missing Agents environment variable"))?.ToImmutableList() ?? throw new ArgumentException("Unable to deserialize 'Agents' environment variable");
        services.AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights()
            .AddTransient<DebugHttpHandler>()
            .AddHttpClient()
            .AddSignalR()
                .AddAzureSignalR(o => o.ConnectionString = Environment.GetEnvironmentVariable("SignalRConnectionString") ?? throw new ArgumentNullException("SignalRConnectionString is missing", default(Exception)));

        services.AddSingleton<AgentHub>();

        services.AddLogging(lb =>
        {
            lb.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                o.IncludeScopes = true;
            });
        });

        services.AddSingleton<PromptExecutionSettings>(new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = Environment.GetEnvironmentVariable("SystemPrompt") ?? throw new ArgumentNullException("SystemPrompt", "Missing SystemPrompt environment variable"),
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            User = Environment.MachineName
        });

        foreach (AgentDefinition? a in agents)
        {
            services.AddHttpClient(a.Name, c => c.BaseAddress = a.Endpoint)
                .AddHttpMessageHandler<DebugHttpHandler>();
        }

        services.AddSingleton(sp =>
        {
            IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

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
                    HttpResponseMessage response = await client.PostAsync("api/threads", new StringContent(prompt));
                    return await response.Content.ReadAsStringAsync();
                }, a.Name, a.Description, [new("prompt") { IsRequired = true, ParameterType = typeof(string) }], new() { Description = "Prompt response as a JSON object or array to be inferred upon.", ParameterType = typeof(string) })
                );
            }

            kernel.ImportPluginFromFunctions("Experts", expertFunctions);
            return kernel;
        });
    })
    .Build();

host.Run();
