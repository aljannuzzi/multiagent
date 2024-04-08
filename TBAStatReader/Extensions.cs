namespace TBAStatReader;

using System.Text.Json.Nodes;

using TBAAPI.V3Client.Model;

internal static class Extensions
{
    public static JsonNode? GetScoreBreakdownFor(this Match match, string redOrBlue) => JsonNode.Parse(match.ToJson())!["score_breakdown"]?[redOrBlue];
}
