namespace TBALLMAgent;

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

using Json.More;

using Microsoft.SemanticKernel;

public class Helpers
{
    [KernelFunction, Description("Counts the number of items in a JSON array")]
    public int CountItemsInJsonArray(string jsonArray)
    {
        try
        {
            return JsonNode.Parse(jsonArray)?.AsArray().Count ?? 0;
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(jsonArray).RootElement[0].AsNode()?.AsArray().Count ?? 0;
        }
    }
}
