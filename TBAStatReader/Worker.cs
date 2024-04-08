namespace TBAStatReader
{
    using System;
    using System.Collections.Immutable;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    using TBAAPI.V3Client.Api;
    using TBAAPI.V3Client.Client;
    using TBAAPI.V3Client.Model;

    internal class Worker(IConfiguration config, ApiClient client, Configuration clientConfig, ILoggerFactory loggerFactory) : IHostedService
    {
        private readonly string _apiKey = config["TBA_API_KEY"]!;
        private readonly ApiClient _client = client;
        private readonly ILogger _log = loggerFactory.CreateLogger<Worker>();
        private readonly ImmutableDictionary<string, ImmutableArray<string>> _matchesToExclude = (config.GetSection("excludeMatches").GetChildren().Select(i => (i["metric"]!, i["matchKey"]!)) ?? []).GroupBy(i => i.Item1, i => i.Item2, StringComparer.OrdinalIgnoreCase).ToImmutableDictionary(i => i.Key, i => i.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
        private readonly int _maxNumEvents = int.Parse(config["maxEvents"] ?? "0");
        private readonly int _targetYear = int.Parse(config["year"] ?? DateTime.Now.Year.ToString());

        private Best _bestStage = new(MetricCategory.Staging, default, string.Empty, string.Empty, 0);
        private Best _bestAllianceAuto = new(MetricCategory.Auto, default, string.Empty, string.Empty, 0);
        private Best _bestTotal = new(MetricCategory.Total, default, string.Empty, string.Empty, 0);
        private Best _bestPureTotal = new(MetricCategory.PureTotal, default, string.Empty, string.Empty, 0);

        private readonly CircularCharArray _spinner = new('|', '/', '-', '\\');

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _log.LogDebug("API key: {apiKey}", _apiKey);

            var events = new EventApi(clientConfig);
            var yearEvents = await events.GetEventsByYearAsync(_targetYear);
            ArgumentNullException.ThrowIfNull(yearEvents);

            var eventNum = 0;
            var matches = new MatchApi(clientConfig);
            foreach (var yearEvent in yearEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var eventName = yearEvent.Name;
                var eventDetailData = await matches.GetEventMatchesAsync(yearEvent.Key);
                ArgumentNullException.ThrowIfNull(eventDetailData);

                if (eventDetailData.Count is not 0)
                {
                    ++eventNum;
                    if (_maxNumEvents > 0 && eventNum > _maxNumEvents)
                    {
                        _log.LogInformation("Reached max number of events to process");
                        break;
                    }

                    foreach (var match in eventDetailData)
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

        private void TrackBestScore(Event evt, Match match)
        {
            using var log = _log.BeginScope(nameof(TrackBestScore));
            var blueScore = match.Alliances.Blue.Score;
            var redScore = match.Alliances.Red.Score;

            var matchName = match.Key;

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

            var blueFoulPoints = (match.GetScoreBreakdownFor("blue")?["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
            var redFoulPoints = (match.GetScoreBreakdownFor("red")?["foulPoints"]!.GetValue<int>()).GetValueOrDefault(0);
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

        private void TrackBestStagingScore(Event evt, Match match)
        {
            using var log = _log.BeginScope(nameof(TrackBestStagingScore));
            var blueBreakdown = match.GetScoreBreakdownFor("blue");
            var blueEndgameTotalStagePoints = blueBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();
            var redBreakdown = match.GetScoreBreakdownFor("red");
            var redEndgameTotalStagePoints = redBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();

            var matchName = match.Key;

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

        private void TrackBestAutoScore(Event evt, Match match)
        {
            using var log = _log.BeginScope(nameof(TrackBestAutoScore));
            string matchKey = match.Key;
            if (_matchesToExclude["auto"].Contains(matchKey, StringComparer.OrdinalIgnoreCase))
            {
                _log.LogWarning("Skipped {match} due to exclusion", matchKey);
                return;
            }

            var blueBreakdown = match.GetScoreBreakdownFor("blue");
            var blueEndgameTotalStagePoints = blueBreakdown?["autoPoints"]?.GetValue<int>();
            var redBreakdown = match.GetScoreBreakdownFor("red");
            var redEndgameTotalStagePoints = redBreakdown?["autoPoints"]?.GetValue<int>();

            var matchName = match.Key;

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
