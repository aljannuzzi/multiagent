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
        private readonly int _maxNumEvents = int.Parse(config["maxEvents"] ?? "0");

        private Best _bestStage = new(MetricCategory.Staging, [], string.Empty, string.Empty, 0);
        private Best _bestAllianceAuto = new(MetricCategory.Auto, [], string.Empty, string.Empty, 0);
        private Best _bestTotal = new(MetricCategory.Total, [], string.Empty, string.Empty, 0);
        private Best _bestPureTotal = new(MetricCategory.PureTotal, [], string.Empty, string.Empty, 0);

        private readonly CircularCharArray _spinner = new('|', '/', '-', '\\');

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _log.LogDebug("API key: {apiKey}", _apiKey);
            _client.DefaultRequestHeaders.Add("X-TBA-Auth-Key", _apiKey);
            _client.DefaultRequestHeaders.Accept.Add(new("application/json"));

            var response = await _client.GetAsync(GetRelative("events/2024/simple"), cancellationToken);
            var yearEvents = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: cancellationToken);
            ArgumentNullException.ThrowIfNull(yearEvents);

            var eventNum = 0;
            foreach (var yearEvent in yearEvents.Cast<JsonObject>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventKey = yearEvent["key"]!.GetValue<string>();
                var eventName = yearEvent["name"]!.GetValue<string>();

                var eventDetailResponse = await _client.GetAsync(GetRelative($"event/{eventKey}/matches"), cancellationToken);
                var eventDetailData = await eventDetailResponse.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: cancellationToken);
                ArgumentNullException.ThrowIfNull(eventDetailData);

                if (eventDetailData.Count is not 0)
                {
                    ++eventNum;
                    if (_maxNumEvents > 0 && eventNum > _maxNumEvents)
                    {
                        _log.LogInformation("Reached max number of events to process");
                        break;
                    }

                    foreach (var match in eventDetailData.Cast<JsonObject>())
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

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Uri GetRelative(string relativePath) => new("https://www.thebluealliance.com/api/v3/" + relativePath);

        private void TrackBestScore(JsonObject evt, JsonObject match)
        {
            using var log = _log.BeginScope(nameof(TrackBestScore));
            var blueScore = match["alliances"]!["blue"]!["score"]!.GetValue<int>();
            var redScore = match["alliances"]!["red"]!["score"]!.GetValue<int>();

            var matchName = match.GetMatchName();

            _log.LogDebug("{event}/{matchName}\tRed Score: {redScore}\tBlue Score: {blueScore}", evt, matchName, redScore, blueScore);

            var potentialBest = redScore > blueScore ? new Best(MetricCategory.Total, evt, matchName, "Red", redScore) : new Best(MetricCategory.Total, evt, matchName, "Blue", blueScore);
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

            var blueFoulPoints = (match["score_breakdown"]?["blue"]!["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
            var redFoulPoints = (match["score_breakdown"]?["red"]!["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
            var adjustedBlueScore = blueScore - blueFoulPoints;
            var adjustedRedScore = redScore - redFoulPoints;
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

        private void TrackBestStagingScore(JsonObject evt, JsonObject match)
        {
            using var log = _log.BeginScope(nameof(TrackBestStagingScore));
            var blueBreakdown = match.GetScoreBreakdownFor("blue");
            var blueEndgameTotalStagePoints = blueBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();
            var redBreakdown = match.GetScoreBreakdownFor("red");
            var redEndgameTotalStagePoints = redBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();

            var matchName = match.GetMatchName();

            _log.LogDebug("{event}/{matchName}\tRed Stage: {redStage}\tBlue Stage: {blueStage}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

            var potentialBest = redEndgameTotalStagePoints > blueEndgameTotalStagePoints ? new Best(MetricCategory.Staging, evt, matchName, "Red", redEndgameTotalStagePoints.GetValueOrDefault(0)) : new Best(MetricCategory.Staging, evt, matchName, "Blue", blueEndgameTotalStagePoints.GetValueOrDefault(0));
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

        private void TrackBestAutoScore(JsonObject evt, JsonObject match)
        {
            using var log = _log.BeginScope(nameof(TrackBestAutoScore));
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

            var potentialBest = redEndgameTotalStagePoints > blueEndgameTotalStagePoints ? new Best(MetricCategory.Staging, evt, matchName, "Red", redEndgameTotalStagePoints.GetValueOrDefault(0)) : new Best(MetricCategory.Staging, evt, matchName, "Blue", blueEndgameTotalStagePoints.GetValueOrDefault(0));
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
}
