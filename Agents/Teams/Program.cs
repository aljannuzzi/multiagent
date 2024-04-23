using Assistants;

using Azure.Identity;

using Common;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using TBAAPI.V3Client.Api;
using TBAAPI.V3Client.Client;

IHost host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((ctx, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($@"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true);
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService()
            .ConfigureFunctionsApplicationInsights()
            .AddTransient<DebugHttpHandler>()
            .AddHttpClient("AzureOpenAi").AddHttpMessageHandler<DebugHttpHandler>();

        services.AddLogging(lb =>
            lb.AddConfiguration(ctx.Configuration.GetSection("Logging"))
                .AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.IncludeScopes = true;
                }));

        services.AddSingleton<PromptExecutionSettings>(new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = Environment.GetEnvironmentVariable("SystemPrompt") ?? throw new ArgumentNullException("SystemPrompt", "Missing SystemPrompt environment variable"),
            Temperature = 0.1,
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            User = Environment.MachineName
        });

        services.AddSingleton(sp =>
        {
            IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton(loggerFactory);
            kernelBuilder.Plugins.AddFromType<Calendar>();
            kernelBuilder.Plugins.AddFromObject(new TeamApi(new Configuration(new Dictionary<string, string>(),
                new Dictionary<string, string>() { { "X-TBA-Auth-Key", sp.GetRequiredService<IConfiguration>().GetValue<string>("TBA_API_KEY")! } },
                new Dictionary<string, string>())));

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

            return kernel;
        });
    })
    .Build();

host.Run();
