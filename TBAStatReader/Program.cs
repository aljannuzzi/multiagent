using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using TBAStatReader;

var apiKey = Environment.GetEnvironmentVariable("TBA_API_KEY");
Debug.WriteLine("API key: " + apiKey);
var client = new HttpClient();
client.DefaultRequestHeaders.Add("X-TBA-Auth-Key", apiKey);
client.DefaultRequestHeaders.Accept.Add(new("application/json"));

var response = await client.GetAsync(GetRelative("events/2024/simple"));
var yearEvents = await response.Content.ReadFromJsonAsync<JsonArray>();

bool first = true;
Best? bestStage = null;
Best? bestAllianceAuto = null;
Best? bestTotal = null;
Best? bestPureTotal = null;

var i = 0;
foreach (JsonObject yearEvent in yearEvents)
{
    var eventKey = yearEvent["key"]!.GetValue<string>();
    var eventName = yearEvent["name"]!.GetValue<string>();

    var eventDetailResponse = await client.GetAsync(GetRelative($"event/{eventKey}/matches"));
    var eventDetailData = await eventDetailResponse.Content.ReadFromJsonAsync<JsonArray>();
    foreach (JsonObject match in eventDetailData!)
    {
        Debug.WriteLine(match);

        TrackBestStagingScore(first, ref bestStage, yearEvent, match!);
        TrackBestAutoScore(first, ref bestAllianceAuto, yearEvent, match!);
        TrackBestScore(first, ref bestTotal, ref bestPureTotal, yearEvent, match!);

        Console.Write('.');
        first = false;
    }

    Console.WriteLine($"\n{eventName} processed.\n");
}

Console.WriteLine("DONE!\n");
Console.WriteLine("Best Overall: {0}", bestTotal);
Console.WriteLine("Best Pure: {0}", bestPureTotal);
Console.WriteLine("Best Alliance Auto: {0}", bestAllianceAuto);
Console.WriteLine("Best Staging: {0}", bestStage);

static void TrackBestAutoScore(bool first, ref Best? bestAuto, JsonObject evt, JsonObject match)
{
    var blueBreakdown = match.GetScoreBreakdownFor("blue");
    var blueEndgameTotalStagePoints = blueBreakdown?["autoPoints"]?.GetValue<int>();
    var redBreakdown = match.GetScoreBreakdownFor("red");
    var redEndgameTotalStagePoints = redBreakdown?["autoPoints"]?.GetValue<int>();

    var matchName = match.GetMatchName();

    Debug.WriteLine("{0}/{1}\tRed Auto: {2}\tBlue Auto: {3}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

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

Uri GetRelative(string relativePath) => new("https://www.thebluealliance.com/api/v3/" + relativePath);

static void TrackBestScore(bool first, ref Best? bestScore, ref Best? bestPureScore, JsonObject evt, JsonObject match)
{
    var blueScore = match["alliances"]!["blue"]!["score"]!.GetValue<int>();
    var redScore = match["alliances"]!["red"]!["score"]!.GetValue<int>();

    var matchName = match.GetMatchName();

    Debug.WriteLine("{0}/{1}\tRed Score: {2}\tBlue Score: {3}", evt, matchName, redScore, blueScore);

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

static void TrackBestStagingScore(bool first, ref Best? bestStage, JsonObject evt, JsonObject match)
{
    var blueBreakdown = match.GetScoreBreakdownFor("blue");
    var blueEndgameTotalStagePoints = blueBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();
    var redBreakdown = match.GetScoreBreakdownFor("red");
    var redEndgameTotalStagePoints = redBreakdown?["endGameTotalStagePoints"]?.GetValue<int>();

    var matchName = match.GetMatchName();

    Debug.WriteLine("{0}/{1}\tRed Stage: {2}\tBlue Stage: {3}", evt, matchName, !redEndgameTotalStagePoints.HasValue ? "NULL" : redEndgameTotalStagePoints.Value, !blueEndgameTotalStagePoints.HasValue ? "NULL" : blueEndgameTotalStagePoints.Value);

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

record Best(JsonObject Event, string Match, string Alliance, int Points);
