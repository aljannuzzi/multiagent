namespace TBAStatReader;

using System.Threading;
using System.Threading.Tasks;

using Common;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

internal class Agent(IConfiguration appConfig, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, Kernel sk, PromptExecutionSettings promptSettings) : Expert(appConfig, loggerFactory, httpClientFactory, sk, promptSettings)
{
    protected override async Task<string> GetAnswerInternalAsync(string prompt, CancellationToken cancellationToken)
    {
        await this.SignalR.SendAsync(Constants.SignalR.Functions.WriteToResponseStream, FakeStream(), cancellationToken);

        return await base.GetAnswerInternalAsync(prompt, cancellationToken);
    }
    private async IAsyncEnumerable<string> FakeStream()
    {
        for (int i = 0; i < 3; i++)
        {
            yield return "Hello";
            await Task.Delay(1000);
            yield return "from";
            await Task.Delay(1000);
            yield return "Teams";
            await Task.Delay(1000);
            yield return "Expert";
            await Task.Delay(1000);
            yield return "!\n";
            await Task.Delay(1000);
        }
    }
}
