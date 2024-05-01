namespace Common.Extensions;

using Assistants;

using Azure.Identity;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using TBAAPI.V3Client.Client;

public static class HostExtensions
{
    public static HostApplicationBuilder ConfigureExpertDefaults<T>(this HostApplicationBuilder b) where T : notnull, Expert
    {
        b.Services.AddHostedService<T>()
            .AddHttpClient()
            .AddTransient<DebugHttpHandler>()
            .AddLogging(lb =>
            {
                lb.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Enabled;
                    o.IncludeScopes = true;
                });
            });

        return b;
    }

    public static HostApplicationBuilder AddSemanticKernel(this HostApplicationBuilder b, Action<IServiceProvider, OpenAIPromptExecutionSettings>? configurePromptSettings = default, Action<IServiceProvider, IKernelBuilder>? configureKernelBuilder = default, Action<IServiceProvider, Kernel>? configureKernel = default)
    {
        b.Services
            .AddSingleton<PromptExecutionSettings>(sp =>
            {
                var settings = new OpenAIPromptExecutionSettings
                {
                    ChatSystemPrompt = b.Configuration["SystemPrompt"] ?? throw new ArgumentNullException("SystemPrompt", "Missing SystemPrompt environment variable"),
                    Temperature = 0.1,
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                    User = Environment.MachineName
                };

                configurePromptSettings?.Invoke(sp, settings);

                return settings;
            })
            .AddSingleton(sp =>
            {
                IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

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

                configureKernelBuilder?.Invoke(sp, kernelBuilder);

                Kernel kernel = kernelBuilder.Build();
                configureKernel?.Invoke(sp, kernel);

                return kernel;
            });

        return b;
    }

    public static HostApplicationBuilder AddSemanticKernel<TApi>(this HostApplicationBuilder b, Action<IServiceProvider, OpenAIPromptExecutionSettings>? configurePromptSettings = default, Action<IServiceProvider, IKernelBuilder>? configureKernel = default) => AddSemanticKernel(b, configurePromptSettings,
        (sp, kb) =>
        {
            var expert = (TApi)Activator.CreateInstance(typeof(TApi), new Configuration(new Dictionary<string, string>(),
                new Dictionary<string, string>() { { "X-TBA-Auth-Key", b.Configuration["TBA_API_KEY"] ?? throw new ArgumentNullException("TBA_API_KEY", "Missing TBA_API_KEY environment variable") } },
                new Dictionary<string, string>()), sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TApi).Name))!;
            kb.Plugins.AddFromObject(expert);

            configureKernel?.Invoke(sp, kb);
        });
}
