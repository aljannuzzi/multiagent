namespace Orchestrator_SignalR;
using System.Collections.Immutable;
using System.Text.Json;

using Assistants;

using Azure.Identity;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using TBAStatReader;

internal partial class Program
{
    private static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        cts.Token.Register(() => Console.WriteLine("Cancellation requested. Exiting..."));

        HostApplicationBuilder b = Host.CreateApplicationBuilder(args);
        b.Services.AddHostedService<Worker>()
            .AddTransient<DebugHttpHandler>()
            .AddLogging(lb =>
            {
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            })
            .AddHttpLogging(o => o.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestBody | Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponseBody);

        ILoggerFactory loggerFactory = b.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

        var signalRConnString = b.Configuration["SignalRConnectionString"];
        var options = new ServiceManagerOptions
        {
            ConnectionString = signalRConnString
        };

        ServiceManager signalRserviceManager = new ServiceManagerBuilder()
            .WithOptions(o =>
            {
                o.ConnectionString = options.ConnectionString;
                o.ServiceTransportType = ServiceTransportType.Persistent;
            })
            .WithLoggerFactory(loggerFactory)
            .BuildServiceManager();
        ServiceHubContext signalRhub = await signalRserviceManager.CreateHubContextAsync(b.Configuration["SignalRHubName"] ?? "TBABot", cts.Token);
        Microsoft.AspNetCore.Http.Connections.NegotiationResponse userNegotiation = await signalRhub.NegotiateAsync(new NegotiationOptions
        {
            UserId = "orchestrator",
            EnableDetailedErrors = true
        });

        b.Services.AddSingleton<IServiceHubContext>(signalRhub);

        HubConnection hubConn = new HubConnectionBuilder()
            .WithUrl(userNegotiation.Url!, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(userNegotiation.AccessToken);
                options.HttpMessageHandlerFactory = f => new DebugHttpHandler(loggerFactory, f);
            })
            .ConfigureLogging(lb =>
            {
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            })
            .Build();

        b.Services.AddSingleton(hubConn);

        IServiceCollection services = b.Services;
        ImmutableList<AgentDefinition> agents = JsonSerializer.Deserialize<List<AgentDefinition>>(b.Configuration["Agents"] ?? throw new ArgumentNullException("Agents", "Missing Agents environment variable"))?.ToImmutableList() ?? throw new ArgumentException("Unable to deserialize 'Agents' environment variable");
        services.AddSingleton(agents);
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
            ChatSystemPrompt = b.Configuration["SystemPrompt"] ?? throw new ArgumentNullException("SystemPrompt", "Missing SystemPrompt environment variable"),
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
            IServiceHubContext sender = sp.GetRequiredService<IServiceHubContext>();

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton(loggerFactory);
            kernelBuilder.Plugins.AddFromType<Calendar>();

            if (b.Configuration["AzureOpenAIKey"] is not null)
            {
                kernelBuilder.AddAzureOpenAIChatCompletion(
                    b.Configuration["AzureOpenDeployment"]!,
                    b.Configuration["AzureOpenAIEndpoint"]!,
                    b.Configuration["AzureOpenAIKey"]!,
                    httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
            }
            else
            {
                kernelBuilder.AddAzureOpenAIChatCompletion(
                    b.Configuration["AzureOpenDeployment"]!,
                    b.Configuration["AzureOpenAIEndpoint"]!,
                    new DefaultAzureCredential(),
                    httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
            }

            Kernel kernel = kernelBuilder.Build();
            return kernel;
        });

        await b.Build().RunAsync(cts.Token);

    }
}