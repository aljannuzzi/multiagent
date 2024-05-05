namespace TBAAPI.V3Client.Api;

using System.ComponentModel;
using System.Text.Json;

using JsonCons.JmesPath;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using Models.Json;

using TBAAPI.V3Client.Client;
using TBAAPI.V3Client.Model;

public partial class EventApi
{
    private ILogger? Log { get; }

    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("[]");

    public EventApi(Configuration config, ILogger logger) : this(config) => this.Log = logger;

    /// <summary>
    /// Searches for teams based on a JMESPath expression.
    /// </summary>
    /// <param name="jmesPathExpression">The JMESPath expression used to filter the teams.</param>
    /// <returns>A list of teams that match the JMESPath expression.</returns>
    [KernelFunction, Description("Searches for events based on a JMESPath expression.")]
    [return: Description("A collection of JSON documents/objects resulting from the JMESPath expression.")]
    public async Task<JsonDocument> SearchEventsAsync(
        int year,
        [Description("The query used to filter a JSON document with a single `events` array of Event objects. Must be a valid JMESPath expression. Use lower-case strings for literals when searching.")] string jmesPathExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);

        JsonTransformer transformer;
        try
        {
            transformer = JsonTransformer.Parse(jmesPathExpression);
        }
        catch (JmesPathParseException)
        {
            throw new ArgumentException("Invalid JMESPath expression", jmesPathExpression);
        }

        JsonDocument results = EmptyJsonDocument;
        List<Event>? teams = await GetEventsByYearDetailedAsync(year).ConfigureAwait(false);
        if (teams?.Count is not null and not 0)
        {
            JsonElement eltToTransform = JsonSerializer.SerializeToElement(new { teams }, JsonSerialzationOptions.Default);
            JsonDocument filteredTeams = JsonCons.JmesPath.JsonTransformer.Transform(eltToTransform, jmesPathExpression);
            this.Log?.LogTrace("JsonCons.JMESPath result: {jsonConsResult}", filteredTeams.RootElement.ToString());

            if (filteredTeams is not null)
            {
                if ((filteredTeams.RootElement.ValueKind is JsonValueKind.Array && filteredTeams.RootElement.EnumerateArray().Any())
                    || (filteredTeams.RootElement.ValueKind is JsonValueKind.Object && filteredTeams.RootElement.EnumerateObject().Any()))
                {
                    results = filteredTeams;
                }
            }
        }

        this.Log?.LogDebug("Resulting document: {searchResults}", JsonSerializer.Serialize(results));

        return results;
    }

    /// <summary>
    /// Searches for match data based on a JMESPath expression.
    /// </summary>
    /// <param name="jmesPathExpression">The JMESPath expression used to filter matches.</param>
    /// <returns>A list of teams that match the JMESPath expression.</returns>
    [KernelFunction, Description("Searches for match data by event based on a JMESPath expression.")]
    [return: Description("A collection of JSON objects with the search results.")]
    public async Task<JsonDocument> SearchEventMatchesAsync(
        [Description("Event Key, eg '2016nytr'")] string eventKey,
        [Description("The query used to filter a JSON document with a single `matches` array of Match objects. Must be a valid JMESPath expression. Use lower-case strings for literals when searching.")] string jmesPathExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);
        if (jmesPathExpression.StartsWith("Matches"))
        {
            jmesPathExpression = jmesPathExpression.Replace("Matches", "matches");
        }

        JsonDocument retVal = EmptyJsonDocument;
        List<Match>? matches = await GetEventMatchesDetailedAsync(eventKey).ConfigureAwait(false);
        if (matches?.Count is not null and not 0)
        {
            JsonElement eltToTransform = JsonSerializer.SerializeToElement(new { matches }, JsonSerialzationOptions.Default);
            JsonDocument filteredMatches = JsonCons.JmesPath.JsonTransformer.Transform(eltToTransform, jmesPathExpression);
            this.Log?.LogTrace("JsonCons.JMESPath result: {jsonConsResult}", filteredMatches.RootElement.ToString());

            if (filteredMatches is not null)
            {
                if ((filteredMatches.RootElement.ValueKind is JsonValueKind.Array && filteredMatches.RootElement.EnumerateArray().Any())
                    || (filteredMatches.RootElement.ValueKind is JsonValueKind.Object && filteredMatches.RootElement.EnumerateObject().Any()))
                {
                    retVal = filteredMatches;
                }
            }
        }

        this.Log?.LogDebug("Resulting document: {searchResults}", JsonSerializer.Serialize(retVal));

        return retVal;
    }
}
