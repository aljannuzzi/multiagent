namespace Teams_SignalR;
using Assistants;

using Azure.Identity;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using TBAAPI.V3Client.Api;
using TBAAPI.V3Client.Client;

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

        var signalRConnString = b.Configuration["SignalRConnectionString"];
        if (!string.IsNullOrWhiteSpace(signalRConnString))
        {
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
                .WithLoggerFactory(b.Services.BuildServiceProvider().GetService<ILoggerFactory>())
                .BuildServiceManager();
            ServiceHubContext signalRhub = await signalRserviceManager.CreateHubContextAsync(b.Configuration["SignalRHubName"] ?? "TBABot", cts.Token);
            Microsoft.AspNetCore.Http.Connections.NegotiationResponse userNegotiation = await signalRhub.NegotiateAsync(new NegotiationOptions
            {
                UserId = "Teams",
                EnableDetailedErrors = true
            });

            b.Services.AddSingleton<IServiceHubContext>(signalRhub);
            b.Services.AddSingleton(sp =>
            {
                return new HubConnectionBuilder()
                    .WithUrl(userNegotiation.Url!, options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(userNegotiation.AccessToken);
                        options.HttpMessageHandlerFactory = f => new DebugHttpHandler(sp.GetRequiredService<ILoggerFactory>(), f);
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
            });
        }

        IServiceCollection services = b.Services;

        services.AddLogging(lb =>
        {
            lb
                .AddSimpleConsole(o =>
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