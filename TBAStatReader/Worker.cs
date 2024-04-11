namespace TBAStatReader;

using System;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Azure.Identity;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

using TBAAPI.V3Client.Api;
using TBAAPI.V3Client.Client;
using TBAAPI.V3Client.Model;

using TBALLMAgent;

internal class Worker(IConfiguration config, IHttpClientFactory httpClientFactory, ApiClient client, Configuration clientConfig, ILoggerFactory loggerFactory) : IHostedService
{
    private readonly string _apiKey = config["TBA_API_KEY"]!;
    private readonly bool _chatMode = config["chatMode"]?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
    private readonly ApiClient _client = client;
    private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
    private readonly ImmutableDictionary<string, ImmutableArray<string>> _matchesToExclude = (config.GetSection("excludeMatches").GetChildren().Select(i => (i["metric"]!, i["matchKey"]!)) ?? []).GroupBy(i => i.Item1, i => i.Item2, StringComparer.OrdinalIgnoreCase).ToImmutableDictionary(i => i.Key, i => i.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    private readonly int _maxNumEvents = int.Parse(config["maxEvents"] ?? "0");
    private readonly int _targetYear = int.Parse(config["year"] ?? DateTime.Now.Year.ToString());

    private Best _bestStage = new(MetricCategory.Staging, default, string.Empty, string.Empty, 0);
    private Best _bestAllianceAuto = new(MetricCategory.Auto, default, string.Empty, string.Empty, 0);
    private Best _bestTotal = new(MetricCategory.Total, default, string.Empty, string.Empty, 0);
    private Best _bestPureTotal = new(MetricCategory.PureTotal, default, string.Empty, string.Empty, 0);

    private readonly CircularCharArray _spinner = CircularCharArray.ProgressSpinner;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Don't have the code fixes to make this better at this time")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _log.LogDebug("API key: {apiKey}", _apiKey);

        if (_chatMode)
        {
            await ExecuteChatModeAsync(cancellationToken);
            return;
        }

        var events = new EventApi(clientConfig);
        List<Event> yearEvents = await events.GetEventsByYearAsync(_targetYear);
        ArgumentNullException.ThrowIfNull(yearEvents);

        var eventNum = 0;
        var matches = new MatchApi(clientConfig);
        foreach (Event yearEvent in yearEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var eventName = yearEvent.Name;
            List<Match> eventDetailData = await matches.GetEventMatchesAsync(yearEvent.Key!);
            ArgumentNullException.ThrowIfNull(eventDetailData);

            if (eventDetailData.Count is not 0)
            {
                ++eventNum;
                if (_maxNumEvents > 0 && eventNum > _maxNumEvents)
                {
                    _log.LogInformation("Reached max number of events to process");
                    break;
                }

                foreach (Match? match in eventDetailData)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _log.LogDebug("{match}", match);

                    TrackBestStagingScore(yearEvent, match);
                    TrackBestAutoScore(yearEvent, match);
                    TrackBestScore(yearEvent, match);

                    Console.CursorLeft = 0;
                    Console.Write(_spinner.Next());
                }

                Console.CursorLeft = 0;
                Console.WriteLine(' ');
                _log.LogInformation($"{{eventName}} processed ({{matchCount}} {(eventDetailData.Count is 1 ? "match" : "matches")})", eventName, eventDetailData.Count);
            }
        }

        _log.LogInformation("{bestTotal}", _bestTotal);
        _log.LogInformation("{bestPureTotal}", _bestPureTotal);
        _log.LogInformation("{bestAllianceAuto}", _bestAllianceAuto);
        _log.LogInformation("{bestStage}", _bestStage);
    }

    private async Task ExecuteChatModeAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");
        IKernelBuilder builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(loggerFactory);
        builder.Plugins
            .AddFromObject(new TBAAgent(clientConfig, loggerFactory))
            .AddFromObject(new Calendar());

        if (config["AzureOpenAIKey"] is not null)
        {
            builder.AddAzureOpenAIChatCompletion(
                config["AzureOpenDeployment"]!,
                config["AzureOpenAIEndpoint"]!,
                config["AzureOpenAIKey"]!,
                httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
        }
        else
        {
            builder.AddAzureOpenAIChatCompletion(
                config["AzureOpenDeployment"]!,
                config["AzureOpenAIEndpoint"]!,
                new DefaultAzureCredential(),
                httpClient: httpClientFactory.CreateClient("AzureOpenAi"));
        }

        Kernel kernel = builder.Build();

        var baseSystemPrompt = $@"You are an information assistant for users asking questions about the FIRST Robotics competition.
In these competitions, two ""alliances"" - red & blue - each consisting of 3 ""teams"" compete against one another to score points based on the rules defined for the season's game. A given ""event"" consists of many ""matches"" (individual scoring rounds between alliances).
Many events are held around the world at any given time, with a season consisting of 6 weeks of competition followed by the World Championships and some ""off-season"" events following.
You have been given access to the data catalog of FIRST via API calls, use these to your advantage to answer questions about past and current events and matches in the competition.
If you aren't able to figure out how to answer the question, tell the user in a polite way and ask them for another question.

Here some some samples of the results you can expect from the API:

### SAMPLE MATCH SEARCH RESULT
{{
  ""matches"": [
    {{
      ""comp_level"": 5,
      ""winning_alliance"": 0,
      ""key"": ""2024alhu_f1m1"",
      ""set_number"": 1,
      ""match_number"": 1,
      ""alliances"": {{
        ""red"": {{
          ""score"": 120,
          ""team_keys"": [
            ""frc5002"",
            ""frc4635"",
            ""frc2481""
          ],
          ""surrogate_team_keys"": [],
          ""dq_team_keys"": []
        }},
        ""blue"": {{
          ""score"": 60,
          ""team_keys"": [
            ""frc7111"",
            ""frc4265"",
            ""frc6517""
          ],
          ""surrogate_team_keys"": [],
          ""dq_team_keys"": []
        }}
      }},
      ""event_key"": ""2024alhu"",
      ""time"": 1712436840,
      ""actual_time"": 1712438011,
      ""predicted_time"": 1712438103,
      ""post_result_time"": 1712438219,
      ""videos"": [
        {{
          ""type"": ""youtube"",
          ""key"": ""K3Jv2DdIN1A""
        }}
      ]
    }}
  ]
}}

### SAMPLE EVENTS SEARCH RESULT OBJECT
{{
  ""events"": [
    {{
      ""address"": ""700 Monroe St SW, Huntsville, AL 35801, USA"",
      ""city"": ""Huntsville"",
      ""country"": ""USA"",
      ""district"": null,
      ""division_keys"": [],
      ""end_date"": ""2024-04-06"",
      ""event_code"": ""alhu"",
      ""event_type"": 0,
      ""event_type_string"": ""Regional"",
      ""first_event_code"": ""alhu"",
      ""first_event_id"": null,
      ""gmaps_place_id"": ""ChIJBwUw_FdrYogRMsP0V0W5Zqk"",
      ""gmaps_url"": ""https://maps.google.com/?q=700+Monroe+St+SW,+Huntsville,+AL+35801,+USA&ftid=0x88626b57fc300507:0xa966b94557f4c332"",
      ""key"": ""2024alhu"",
      ""lat"": 34.72671,
      ""lng"": -86.5903795,
      ""location_name"": ""700 Monroe St SW"",
      ""name"": ""Rocket City Regional"",
      ""parent_event_key"": null,
      ""playoff_type"": 10,
      ""playoff_type_string"": ""Double Elimination Bracket (8 Alliances)"",
      ""postal_code"": ""35801"",
      ""short_name"": ""Rocket City"",
      ""start_date"": ""2024-04-03"",
      ""state_prov"": ""AL"",
      ""timezone"": ""America/Chicago"",
      ""webcasts"": [
        {{
          ""channel"": ""firstinspires5"",
          ""type"": ""twitch""
        }}
      ],
      ""website"": ""http://firstinalabama.org/events/frc-events/"",
      ""week"": 5,
      ""year"": 2024
    }}
  ]
}}

When constructing a JMESPath for the JSON structures above, act as follows:
1. Use exact references to the item(s) you need from within the structure, (avoid wildcards, recursive search, etc.)
2. Do not use JMESPath expression syntax ('&' operator)
3. Surround literal values with single quotes (e.g. 'red' or '5')

This is the chat history so far in JSON format, use this as context into the conversation.

### CHAT SO FAR
";

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            User = Environment.MachineName
        };

        var chatHistory = new ChatHistory();
        chatHistory.AddAssistantMessage("Welcome to the TBA Chat bot! What would you like to know about FIRST competitions, past or present?");

        do
        {
            Console.Write("> ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            StringBuilder assistantResponse = new();
            CircularCharArray progress = CircularCharArray.ProgressSpinner;
            try
            {
                do
                {
                    try
                    {
                        settings.ChatSystemPrompt = $@"{baseSystemPrompt}{chatHistory.Serialize()}";
                        await foreach (StreamingKernelContent token in kernel.InvokePromptStreamingAsync(question, new(settings), cancellationToken: cancellationToken))
                        {
                            var tokenString = token.ToString();
                            if (string.IsNullOrEmpty(tokenString))
                            {
                                Console.Write(progress.Next());
                                Console.CursorLeft--;

                                continue;
                            }

                            Console.Write(tokenString);
                            assistantResponse.Append(tokenString);
                        }

                        break;
                    }
                    catch (HttpOperationException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests && ex.InnerException is Azure.RequestFailedException rex)
                    {
                        Azure.Response? resp = rex.GetRawResponse();
                        if (resp?.Headers.TryGetValue("Retry-After", out var waitTime) is true)
                        {
                            _log.LogWarning("Responses Throttled! Must wait {retryAfter} seconds to try again...", waitTime);
                            var waitTimeRemaining = int.Parse(waitTime);
                            while (waitTimeRemaining-- >= 0)
                            {
                                Console.WriteLine($"{waitTimeRemaining}s");
                                Console.CursorTop--;

                                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                } while (true);

                chatHistory.AddUserMessage(question);
                chatHistory.AddAssistantMessage(assistantResponse.ToString());

                Console.WriteLine();
            }
            catch (Exception e)
            {

            }
        }
        while (!cancellationToken.IsCancellationRequested);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Uri GetRelative(string relativePath) => new("https://www.thebluealliance.com/api/v3/" + relativePath);

    private void TrackBestScore(Event evt, Match match)
    {
        using IDisposable? log = _log.BeginScope(nameof(TrackBestScore));
        var blueScore = match.Alliances?.Blue?.Score;
        var redScore = match.Alliances?.Red?.Score;

        if (blueScore is null || redScore is null)
        {
            return;
        }

        var matchName = match.Key!;

        _log.LogDebug("{event}/{matchName}\tRed Score: {redScore}\tBlue Score: {blueScore}", evt, matchName, redScore, blueScore);

        Best potentialBest = redScore > blueScore ? new Best(MetricCategory.Total, evt, matchName, "Red", redScore.Value) : new Best(MetricCategory.Total, evt, matchName, "Blue", blueScore.Value);
        if (potentialBest.Points > -_bestTotal!.Points)
        {
            _bestTotal = _bestTotal with
            {
                Alliance = potentialBest.Alliance,
                Event = potentialBest.Event,
                Match = potentialBest.Match,
                Points = potentialBest.Points,
            };
        }

        var blueFoulPoints = (match.GetScoreBreakdownFor("blue")?["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
        var redFoulPoints = (match.GetScoreBreakdownFor("red")?["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
        var adjustedBlueScore = blueScore.Value - blueFoulPoints;
        var adjustedRedScore = redScore.Value - redFoulPoints;
        if (adjustedRedScore > adjustedBlueScore && adjustedRedScore > _bestPureTotal!.Points)
        {
            _bestPureTotal = _bestPureTotal with
            {
                Alliance = "Red",
                Event = evt,
                Match = matchName,
                Points = adjustedRedScore,
            };
        }
        else if (adjustedBlueScore > adjustedRedScore && adjustedBlueScore > _bestPureTotal!.Points)
        {
            _bestPureTotal = _bestPureTotal with
            {
                Alliance = "Blue",
                Event = evt,
                Match = matchName,
                Points = adjustedBlueScore,
            };
        }
    }

    private void TrackBestStagingScore(Event evt, Match match)
    {
        using IDisposable? log = _log.BeginScope(nameof(TrackBestStagingScore));
        System.Text.Json.Nodes.JsonNode? blueBreakdown = match.GetScoreBreakdownFor("blue");
        var blueEndgameTotalStagePoints = blueBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();
        System.Text.Json.Nodes.JsonNode? redBreakdown = match.GetScoreBreakdownFor("red");
        var redEndgameTotalStagePoints = redBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();

        var matchName = match.Key!;

        _log.LogDebug("{event}/{matchName}\tRed Stage: {redStage}\tBlue Stage: {blueStage}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

        Best potentialBest = redEndgameTotalStagePoints > blueEndgameTotalStagePoints ? new Best(MetricCategory.Staging, evt, matchName, "Red", redEndgameTotalStagePoints.GetValueOrDefault(0)) : new Best(MetricCategory.Staging, evt, matchName, "Blue", blueEndgameTotalStagePoints.GetValueOrDefault(0));
        if (potentialBest.Points > _bestStage.Points)
        {
            _bestStage = _bestStage with
            {
                Alliance = potentialBest.Alliance,
                Event = potentialBest.Event,
                Match = potentialBest.Match,
                Points = potentialBest.Points
            };
        }
    }

    private void TrackBestAutoScore(Event evt, Match match)
    {
        using IDisposable? log = _log.BeginScope(nameof(TrackBestAutoScore));
        var matchKey = match.Key!;
        if (_matchesToExclude["auto"].Contains(matchKey, StringComparer.OrdinalIgnoreCase))
        {
            _log.LogWarning("Skipped {match} due to exclusion", matchKey);
            return;
        }

        System.Text.Json.Nodes.JsonNode? blueBreakdown = match.GetScoreBreakdownFor("blue");
        var blueEndgameTotalStagePoints = blueBreakdown?["autoPoints"]?.GetValue<int>();
        System.Text.Json.Nodes.JsonNode? redBreakdown = match.GetScoreBreakdownFor("red");
        var redEndgameTotalStagePoints = redBreakdown?["autoPoints"]?.GetValue<int>();

        var matchName = match.Key!;

        _log.LogDebug("{event}/{matchName}\tRed Auto: {redAuto}\tBlue Auto: {blueAuto}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

        Best potentialBest = redEndgameTotalStagePoints > blueEndgameTotalStagePoints ? new Best(MetricCategory.Staging, evt, matchName, "Red", redEndgameTotalStagePoints.GetValueOrDefault(0)) : new Best(MetricCategory.Staging, evt, matchName, "Blue", blueEndgameTotalStagePoints.GetValueOrDefault(0));
        if (potentialBest.Points > _bestAllianceAuto!.Points)
        {
            _bestAllianceAuto = _bestAllianceAuto with
            {
                Alliance = potentialBest.Alliance,
                Event = potentialBest.Event,
                Match = potentialBest.Match,
                Points = potentialBest.Points,
            };
        }
    }
}
