namespace TBALLMAgent;

using System.ComponentModel;
using System.Text.Json;

using DevLab.JmesPath;

using Microsoft.SemanticKernel;

using Newtonsoft.Json.Linq;

using TBAAPI.V3Client.Api;
using TBAAPI.V3Client.Client;
using TBAAPI.V3Client.Model;

/// <summary>
/// Represents an agent that interacts with the Blue Alliance API to retrieve information about events and matches.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TBAAgent"/> class with the specified configuration.
/// </remarks>
/// <param name="configuration">The configuration to use for the TBAAgent.</param>
public class TBAAgent(Configuration configuration)
{
    private readonly EventApi _eventApi = new(configuration);
    private readonly MatchApi _matchApi = new(configuration);

    private static readonly JmesPath _jmesPath = new();

    /// <summary>
    /// Retrieves a list of events for a specific season.
    /// </summary>
    /// <param name="year">The year of the season.</param>
    /// <returns>A task representing the asynchronous operation that returns a list of events.</returns>
    [KernelFunction, Description("Retrieves a list of events for a specific season. Events do not contain scoring data.")]
    [return: Description("A list of events for the specified season with limited detail.")]
    public Task<List<EventSimple>> GetEventsForSeasonAsync(int year) => _eventApi.GetEventsByYearSimpleAsync(year);

    /// <summary>
    /// Filters the events for a specific year using a JMESPath expression.
    /// </summary>
    /// <param name="year">The year of the events.</param>
    /// <param name="jmesPath">The JMESPath expression used to filter the events.</param>
    /// <returns>A <see cref="JsonDocument"/> representing the filtered events.</returns>
    [KernelFunction, Description("Filters the events for a specific year using a JMESPath expression. Events do not contain scoring data.")]
    [return: Description("The filtered events for the specified year.")]
    public async Task<JsonDocument> FilterEventsAsync(int year, string jmesPath)
    {
        List<Event> events = await _eventApi.GetEventsByYearAsync(year).ConfigureAwait(false);

        var obj = JToken.FromObject(events);
        JToken result = await _jmesPath.TransformAsync(obj, jmesPath).ConfigureAwait(false);
        return JsonDocument.Parse(result.ToString());
    }

    ///// <summary>
    ///// Retrieves a list of matches asynchronously for a specified event.
    ///// </summary>
    ///// <param name="eventKey">The key of the event for which to retrieve matches.</param>
    ///// <returns>A task that represents the asynchronous operation. The task result contains a list of Match objects representing the matches of the event.</returns>
    //[KernelFunction, Description("Retrieves a list of matches asynchronously for a specified event.")]
    //[return: Description("A list of matches for the specified event with limited detail.")]
    //public Task<List<MatchSimple>> GetMatchesAsync(string eventKey) => _eventApi.GetEventMatchesSimpleAsync(eventKey);

    /// <summary>
    /// Retrieves the details of a match asynchronously for a specified match key.
    /// </summary>
    /// <param name="matchKey">The key of the match for which to retrieve details.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a Match object representing the details of the match.</returns>
    [KernelFunction, Description("Retrieves the details of a match, including scores, winning alliances, etc. asynchronously for a specified match key.")]
    [return: Description("The details of the specified match.")]
    public Task<Match> GetMatchDetailAsync(string matchKey) => _matchApi.GetMatchAsync(matchKey);

    /// <summary>
    /// Retrieves match data asynchronously for a specified event key and applies a JMESPath transformation to the data.
    /// </summary>
    /// <param name="eventKey">The key of the event for which match data is retrieved.</param>
    /// <param name="jmesPath">The JMESPath expression used to transform the match data.</param>
    /// <returns>A <see cref="JsonDocument"/> representing the transformed match data.</returns>
    [KernelFunction, Description("Retrieves match data asynchronously for a specified year/season and applies a JMESPath transformation to the data.")]
    [return: Description("The filtered match data.")]
    public async Task<JsonDocument> GetMatchDataForSeasonAsync(int year, string jmesPath)
    {
        List<string> events = await _eventApi.GetEventsByYearKeysAsync(year).ConfigureAwait(false);
        IEnumerable<Match> matches = events.SelectMany(e => _eventApi.GetEventMatches(e));

        JToken result = await _jmesPath.TransformAsync(JToken.FromObject(new { matches }), jmesPath).ConfigureAwait(false);
        return JsonDocument.Parse(result.ToString());
    }

    /// <summary>
    /// Retrieves match data asynchronously for a specified event key and applies a JMESPath transformation to the data.
    /// </summary>
    /// <param name="eventKey">The key of the event for which match data is retrieved.</param>
    /// <param name="jmesPath">The JMESPath expression used to transform the match data.</param>
    /// <returns>A <see cref="JsonDocument"/> representing the transformed match data.</returns>
    [KernelFunction, Description("Retrieves match data asynchronously for a specified event key and applies a JMESPath transformation to the data.")]
    [return: Description("The filtered match data.")]
    public async Task<JsonDocument> GetMatchDataForEventAsync(string eventKey, string jmesPath)
    {
        List<Match> matches = await _eventApi.GetEventMatchesAsync(eventKey).ConfigureAwait(false);

        JToken result = await _jmesPath.TransformAsync(JToken.FromObject(new { matches }), jmesPath).ConfigureAwait(false);
        return JsonDocument.Parse(result.ToString());
    }
}
