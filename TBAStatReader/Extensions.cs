namespace TBAStatReader
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;

    internal static class Extensions
    {
        public static JsonNode? GetScoreBreakdownFor(this JsonObject match, string redOrBlue) => match["score_breakdown"]?[redOrBlue];

        public static string GetMatchName(this JsonObject match) => match["key"]!.GetValue<string>();
    }
}
