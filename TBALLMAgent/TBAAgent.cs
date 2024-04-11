namespace TBALLMAgent;

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

using DevLab.JmesPath;
using DevLab.JmesPath.Expressions;

using Microsoft.Extensions.Logging;
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
public class TBAAgent(Configuration configuration, ILoggerFactory loggerFactory)
{
    private readonly EventApi _eventApi = new(configuration);
    private readonly MatchApi _matchApi = new(configuration);
    private readonly TeamApi _teamApi = new(configuration);
    private readonly ILogger _logger = loggerFactory.CreateLogger<TBAAgent>();

    private static readonly JsonDocument EmptyDoc = JsonDocument.Parse("{}");
    private static readonly JmesPath _jmesPath = new();

    /// <summary>
    /// Retrieves a list of events for a specific season.
    /// </summary>
    /// <param name="year">The year of the season.</param>
    /// <returns>A task representing the asynchronous operation that returns a list of events.</returns>
    [KernelFunction, Description("Retrieves a list of events for a specific season. Events do not contain scoring data.")]
    [return: Description("A list of events for the specified season with limited detail.")]
    public async Task<IReadOnlyList<EventSimple>> GetEventsForSeasonAsync(int year) => await _eventApi.GetEventsByYearSimpleAsync(year);

    /// <summary>
    /// Retrieves a list of events that started or ended within the given dates.
    /// </summary>
    /// <returns>A list of events with limited detail.</returns>
    /// <param name="startDate">Possible start date for the event.</param>
    /// <param name="endDate">Possible end date for the event.</param>
    [KernelFunction, Description("Retrieves a list of events that started or ended within the given dates.")]
    [return: Description("A list of events with limited detail.")]
    public async Task<IReadOnlyList<EventSimple>> GetEventsByDateRangeAsync([Description("Possible start date for the event")] DateTime startDate, [Description("Possible end date for the event")] DateTime endDate)
    {
        using IDisposable? s = _logger.BeginScope(nameof(GetEventsByDateRangeAsync));
        _logger.LogDebug("Dates: {startDate} - {endDate}", startDate.ToShortDateString(), endDate.ToShortDateString());
        List<EventSimple> seasonEvents = await _eventApi.GetEventsByYearSimpleAsync(startDate.Year);

        return seasonEvents.FindAll(e => (e.StartDate >= startDate && e.StartDate <= endDate) || (e.EndDate >= startDate && e.EndDate <= endDate));
    }

    ///// <summary>
    ///// Retrieves a list of events that started or ended within the given dates.
    ///// </summary>
    ///// <returns>A list of events with limited detail.</returns>
    ///// <param name="startDate">Possible start date for the event.</param>
    ///// <param name="endDate">Possible end date for the event.</param>
    //[KernelFunction, Description("Filters all matches that started or ended within the given dates.")]
    //[return: Description("A list of matches, with detail, filtered by the JMESPath given.")]
    //public async Task<IReadOnlyList<EventSimple>> FilterAllMatchesInDateRangeAsync([Description("Possible start date for the match")] DateTime startDate, [Description("Possible end date for the match")] DateTime endDate)
    //{
    //    var matches = await GetMatchDataForSeasonAsync(startDate.Year);
    //    List<EventSimple> seasonEvents = await _eventApi.GetEventsByYearSimpleAsync(startDate.Year);
    //    var allMat

    //    return seasonEvents.FindAll(e => (e.StartDate >= startDate && e.StartDate <= endDate) || (e.EndDate >= startDate && e.EndDate <= endDate));
    //}

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
        using IDisposable? s = _logger.BeginScope(nameof(FilterEventsAsync));
        jmesPath = SanitizeJMESPath(jmesPath).Replace("'events'", "events");
        _logger.LogDebug("JMESPath: {jmesPath}", jmesPath);
        List<Event> events = await _eventApi.GetEventsByYearAsync(year).ConfigureAwait(false);

        if (events.Count is 0)
        {
            return EmptyDoc;
        }

        var obj = JToken.Parse(JsonSerializer.Serialize(new { events }));
        try
        {
            JToken result = await _jmesPath.TransformAsync(obj, jmesPath).ConfigureAwait(false);
            return JsonSerializer.SerializeToDocument(result);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($@"There was an error running the JMESPath over the data. Fix the expression and try again. {ex.Message}");
        }
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
    /// Filters matches found for a specified year/season using a JMESPath transformation on the match collection.
    /// </summary>
    /// <returns>The filtered match data.</returns>
    /// <param name="year">Year or Season to narrow down Match collection</param>
    /// <param name="jmesPath">JMES path query to run on matches found in the season</param>
    [KernelFunction, Description("Filters matches found for a specified year/season using a JMESPath transformation.")]
    [return: Description("The filtered match data.")]
    public JsonDocument FilterMatchesInDateRange(
        [Description("Possible start date for the match's hosting event")] DateTime startDate,
        [Description("Possible end date for the match's hosting event")] DateTime endDate,
        [Description("JMES path query to run on `matches` collection returned for the date range. Must be a fully-qualified JMESPath transformation expression using as much of the schema as possible in the filter, surrounding literal values with '")] string jmesPath)
    {
        using IDisposable? s = _logger.BeginScope(nameof(FilterMatchesInDateRange));

        jmesPath = SanitizeJMESPath(jmesPath).Replace("'matches'", "matches");
        JmesPathExpression jmesExpression;
        try
        {
            jmesExpression = _jmesPath.Parse(jmesPath);
        }
        catch (Exception ex) // Yeah it's dumb but this is what JmesPath.Net throws if parsing fails.
        {
            throw new ArgumentException($@"The JMESPath isn't valid: {ex.Message}");
        }

        if (startDate.Year != endDate.Year)
        {
            throw new ArgumentException("Start and End dates must be within the same year");
        }

        _logger.LogDebug("Dates: {startDate} - {endDate}", startDate.ToShortDateString(), endDate.ToShortDateString());
        _logger.LogDebug("JMESPath: {jmesPath}", jmesPath);

        IList<EventSimple> response = _eventApi.GetEventsByYearSimple(startDate.Year) ?? [];
        IEnumerable<Match> matches = response.Where(e => (e.StartDate >= startDate && e.StartDate <= e.EndDate) || (e.EndDate >= startDate && e.EndDate <= endDate)).SelectMany(e => _eventApi.GetEventMatches(e.Key!) ?? []);

        if (!matches.Any())
        {
            _logger.LogInformation(@"No matches found from {startDate} - {endDate}", startDate.ToShortDateString(), endDate.ToShortDateString());
            return EmptyDoc;
        }

        var obj = JToken.Parse(JsonSerializer.Serialize(new { matches }));
        try
        {
            JmesPathArgument result = jmesExpression.Transform(new(obj));
            var retVal = JsonDocument.Parse(result.AsJToken().ToString());
            _logger.LogTrace("Filtered Results: {filterResults}", retVal.RootElement.ToString());

            return retVal;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($@"There was an error running the JMESPath over the data. Fix the expression and try again. {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves match data asynchronously for a specified event key and applies a JMESPath transformation to the data.
    /// </summary>
    /// <param name="eventKey">The key of the event for which match data is retrieved.</param>
    /// <param name="jmesPath">The JMESPath expression used to transform the match data.</param>
    /// <returns>A <see cref="JsonDocument"/> representing the transformed match data.</returns>
    [KernelFunction, Description("Retrieves match data asynchronously for a specified event key and applies a JMESPath transformation to the data.")]
    [return: Description("The filtered match data.")]
    public async Task<JsonDocument> FilterMatchDataForEventAsync(
        string eventKey,
        [Description("JMES path query to run on `matches` collection returned for the date range. Must be a fully-qualified JMESPath transformation expression using as much of the schema as possible in the filter, surrounding literal values with '")] string jmesPath)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
        {
            throw new ArgumentException($"'{nameof(eventKey)}' cannot be null or whitespace.", nameof(eventKey));
        }

        using IDisposable? s = _logger.BeginScope(nameof(FilterMatchDataForEventAsync));
        jmesPath = SanitizeJMESPath(jmesPath).Replace("'matches'", "matches");
        _logger.LogDebug("Event Key: {eventKey}", eventKey);
        _logger.LogDebug("JMESPath: {jmesPath}", jmesPath);

        List<Match> matches = await _eventApi.GetEventMatchesAsync(eventKey).ConfigureAwait(false);

        if (matches.Count is 0)
        {
            return EmptyDoc;
        }

        var obj = JToken.Parse(JsonSerializer.Serialize(new { matches }));
        try
        {
            JToken result = await _jmesPath.TransformAsync(obj, jmesPath).ConfigureAwait(false);
            var retVal = JsonDocument.Parse(result.ToString());
            _logger.LogTrace("Filtered Results: {filterResults}", retVal.RootElement.ToString());

            return retVal;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($@"There was an error running the JMESPath over the data. Fix the expression and try again. {ex.Message}");
        }
    }

    private static string SanitizeJMESPath(string jmesPath) => jmesPath.TrimStart(['|', ' ']).Replace('`', '\'');

    /// <summary>
    /// Looks up the team key for a given team name.
    /// </summary>
    /// <param name="search">The name of the team or school to search for</param>
    /// <returns>The team key if found, otherwise null.</returns>
    [Description("Looks up the team key for a given team name.")]
    [return: Description("The key for the specified team.")]
    public async Task<string> GetTeamKeyAsync([Description("The name of the team or school to search for")] string search)
    {
        using IDisposable? s = _logger.BeginScope(nameof(GetTeamKeyAsync));
        _logger.LogDebug("Team Name: {teamName}", search);

        for (var i = 0; ; i++)
        {
            List<Team> teams = await _teamApi.GetTeamsAsync(i++).ConfigureAwait(false);
            if (teams.Count is 0)
            {
                break;
            }

            Team? targetTeam = teams.FirstOrDefault(t => t.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) is true || t.SchoolName?.Contains(search, StringComparison.OrdinalIgnoreCase) is true);
            if (targetTeam is not null)
            {
                return targetTeam.Key!;
            }
        }

        throw new KeyNotFoundException($"No team was found matching {search}");
    }

    /// <summary>
    /// Looks up the team name for a given team key.
    /// </summary>
    /// <param name="teamKey">The key of the team.</param>
    /// <returns>The team name if found, otherwise null.</returns>
    [Description("Looks up the team name for a given team key.")]
    [return: Description("The name of the specified team.")]
    public async Task<string> GetTeamNameAsync([Description("The key of the team.")] string teamKey)
    {
        using IDisposable? s = _logger.BeginScope(nameof(GetTeamNameAsync));
        _logger.LogDebug("Team Key: {teamKey}", teamKey);

        return (await _teamApi.GetTeamAsync(teamKey).ConfigureAwait(false))?.Name!;
    }
}
