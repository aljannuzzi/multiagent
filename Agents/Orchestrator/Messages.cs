namespace Orchestrator;

using System.Collections.Immutable;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

public class Messages(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, ImmutableList<AgentDefinition> agents)
{
    private readonly ILogger<Messages> _log = loggerFactory.CreateLogger<Messages>();

    private static readonly string TBA_API_KEY = Environment.GetEnvironmentVariable("TBA_API_KEY") ?? throw new ArgumentNullException("Missing TBA_API_KEY environment variable");

    [Function("messages")]
    public async Task<IActionResult> RunAsync([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _log.LogDebug("API key: {apiKey}", TBA_API_KEY);

        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");
        IKernelBuilder builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(loggerFactory);
        //builder.Plugins
        //    //.AddFromObject(new TeamApi(clientConfig))
        //    .AddFromObject(new TBAAgent(clientConfig, loggerFactory));

        //if (config["AzureOpenAIKey"] is not null)
        //{
        //    builder.AddAzureOpenAIChatCompletion(
        //        config["AzureOpenDeployment"]!,
        //        config["AzureOpenAIEndpoint"]!,
        //        config["AzureOpenAIKey"]!,
        //        httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
        //}
        //else
        //{
        //    builder.AddAzureOpenAIChatCompletion(
        //        config["AzureOpenDeployment"]!,
        //        config["AzureOpenAIEndpoint"]!,
        //        new DefaultAzureCredential(),
        //        httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
        //}

        Kernel kernel = builder.Build();

        foreach (var a in agents)
        {
            kernel.CreateFunctionFromMethod(async (string prompt) =>
            {
                HttpClient client = httpClientFactory.CreateClient(a.Name);
                client.BaseAddress = a.Endpoint;
                await client.PostAsync("api/threads", new StringContent(prompt));
            });
        }

        var settings = new OpenAIPromptExecutionSettings
        {
            ChatSystemPrompt = @"You are an AI orchestrator answering questions for users about the FIRST robotics competition by choosing one or more Expert Agents to interact with, given to you as Functions. Use the given agents, alone or together.",
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            User = Environment.MachineName
        };

        using var sr = new StreamReader(req.Body);
        var prompt = await sr.ReadToEndAsync();
        var promptResult = await kernel.InvokePromptAsync(prompt, new(settings));

        return new OkObjectResult(promptResult);
    }
}
