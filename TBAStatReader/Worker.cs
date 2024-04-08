namespace TBAStatReader
{
    using System;
    using System.Collections.Immutable;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    internal class Worker(IConfiguration config, IHttpClientFactory httpFactory, ILoggerFactory loggerFactory) : IHostedService
    {
        private readonly string _apiKey = config["TBA_API_KEY"]!;
        private readonly HttpClient _client = httpFactory.CreateClient(nameof(Worker));
        private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
        private readonly ImmutableDictionary<string, ImmutableArray<string>> _matchesToExclude = (config.GetSection("excludeMatches").GetChildren().Select(i => (i["metric"]!, i["matchKey"]!)) ?? []).GroupBy(i => i.Item1, i => i.Item2, StringComparer.OrdinalIgnoreCase).ToImmutableDictionary(i => i.Key, i => i.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);

        private Best? _bestStage = null;
        private Best? _bestAllianceAuto = null;
        private Best? _bestTotal = null;
        private Best? _bestPureTotal = null;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _log.LogDebug("API key: {apiKey}", _apiKey);
            _client.DefaultRequestHeaders.Add("X-TBA-Auth-Key", _apiKey);
            _client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            var response = await _client.GetAsync(GetRelative("events/2024/simple"));
            var yearEvents = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: cancellationToken);
            ArgumentNullException.ThrowIfNull(yearEvents);

            bool first = true;

            var i = 0;
            foreach (JsonObject yearEvent in yearEvents.Where(i => i is not null).Select(i => i!))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventKey = yearEvent["key"]!.GetValue<string>();
                var eventName = yearEvent["name"]!.GetValue<string>();

                var eventDetailResponse = await _client.GetAsync(GetRelative($"event/{eventKey}/matches"));
                var eventDetailData = await eventDetailResponse.Content.ReadFromJsonAsync<JsonArray>();
                ArgumentNullException.ThrowIfNull(eventDetailData);

                foreach (JsonObject match in eventDetailData.Where(i => i is not null).Select(i => i!))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _log.LogDebug("{match}", match);

                    TrackBestStagingScore(first, ref _bestStage, yearEvent, match!);
                    TrackBestAutoScore(first, ref _bestAllianceAuto, yearEvent, match!);
                    TrackBestScore(first, ref _bestTotal, ref _bestPureTotal, yearEvent, match!);

                    Console.Write('.');
                    first = false;
                }

                Console.WriteLine();
                _log.LogInformation("\n{eventName} processed.\n", eventName);
            }

            _log.LogInformation("DONE!\n");
            _log.LogInformation("Best Overall: {bestTotal}", _bestTotal);
            _log.LogInformation("Best Pure: {bestPureTotal}", _bestPureTotal);
            _log.LogInformation("Best Alliance Auto: {bestAllianceAuto}", _bestAllianceAuto);
            _log.LogInformation("Best Staging: {bestStage}", _bestStage);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Uri GetRelative(string relativePath) => new("https://www.thebluealliance.com/api/v3/" + relativePath);

        private void TrackBestScore(bool first, ref Best? bestScore, ref Best? bestPureScore, JsonObject evt, JsonObject match)
        {
            var blueScore = match["alliances"]!["blue"]!["score"]!.GetValue<int>();
            var redScore = match["alliances"]!["red"]!["score"]!.GetValue<int>();

            var matchName = match.GetMatchName();

            _log.LogDebug("{event}/{matchName}\tRed Score: {redScore}\tBlue Score: {blueScore}", evt, matchName, redScore, blueScore);

            var potentialBest = redScore > blueScore ? new Best(evt, matchName, "Red", redScore) : new Best(evt, matchName, "Blue", blueScore);
            if (first)
            {
                bestScore = potentialBest;
            }
            else if (potentialBest.Points > bestScore!.Points)
            {
                bestScore = potentialBest;
            }

            var blueFoulPoints = (match["score_breakdown"]?["blue"]!["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
            var redFoulPoints = (match["score_breakdown"]?["red"]!["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
            var adjustedBlueScore = blueScore - blueFoulPoints;
            var adjustedRedScore = redScore - redFoulPoints;
            if (first)
            {
                bestPureScore = adjustedRedScore > adjustedBlueScore ? new Best(evt, matchName, "Red", adjustedRedScore) : new Best(evt, matchName, "Blue", adjustedBlueScore);
            }
            else if (adjustedRedScore > adjustedBlueScore && adjustedRedScore > bestPureScore!.Points)
            {
                bestPureScore = new Best(evt, matchName, "Red", adjustedRedScore);
            }
            else if (adjustedBlueScore > adjustedRedScore && adjustedBlueScore > bestPureScore!.Points)
            {
                bestPureScore = new Best(evt, matchName, "Blue", adjustedBlueScore);
            }
        }

        private void TrackBestStagingScore(bool first, ref Best? bestStage, JsonObject evt, JsonObject match)
        {
            var blueBreakdown = match.GetScoreBreakdownFor("blue");
            var blueEndgameTotalStagePoints = blueBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();
            var redBreakdown = match.GetScoreBreakdownFor("red");
            var redEndgameTotalStagePoints = redBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();

            var matchName = match.GetMatchName();

            _log.LogDebug("{event}/{matchName}\tRed Stage: {redStage}\tBlue Stage: {blueStage}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

            var potentialBest = redEndgameTotalStagePoints > blueEndgameTotalStagePoints ? new Best(evt, matchName, "Red", redEndgameTotalStagePoints.GetValueOrDefault(0)) : new Best(evt, matchName, "Blue", blueEndgameTotalStagePoints.GetValueOrDefault(0));
            if (first)
            {
                bestStage = potentialBest;
            }
            else if (potentialBest.Points > bestStage!.Points)
            {
                bestStage = potentialBest;
            }
        }

        private void TrackBestAutoScore(bool first, ref Best? bestAuto, JsonObject evt, JsonObject match)
        {
            string matchKey = match["key"]!.GetValue<string>();
            if (_matchesToExclude["auto"].Contains(matchKey, StringComparer.OrdinalIgnoreCase))
            {
                _log.LogWarning("Skipped {match} due to exclusion", matchKey);
                return;
            }

            var blueBreakdown = match.GetScoreBreakdownFor("blue");
            var blueEndgameTotalStagePoints = blueBreakdown?["autoPoints"]?.GetValue<int>();
            var redBreakdown = match.GetScoreBreakdownFor("red");
            var redEndgameTotalStagePoints = redBreakdown?["autoPoints"]?.GetValue<int>();

            var matchName = match.GetMatchName();

            _log.LogDebug("{event}/{matchName}\tRed Auto: {redAuto}\tBlue Auto: {blueAuto}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

            var potentialBest = redEndgameTotalStagePoints > blueEndgameTotalStagePoints ? new Best(evt, matchName, "Red", redEndgameTotalStagePoints.GetValueOrDefault(0)) : new Best(evt, matchName, "Blue", blueEndgameTotalStagePoints.GetValueOrDefault(0));
            if (first)
            {
                bestAuto = potentialBest;
            }
            else if (potentialBest.Points > bestAuto!.Points)
            {
                bestAuto = potentialBest;
            }
        }
    }
}
