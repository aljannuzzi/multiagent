namespace TBAAPI.V3Client.Api;

using Agent.Core.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

using Common;

using JsonCons.JmesPath;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using Models.Json;

using TBAAPI.V3Client.Client;
using TBAAPI.V3Client.Model;

public partial class TeamApi
{
    private ILogger? Log { get; }

    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("[]");

    public TeamApi(Configuration config, ILogger logger) : this(config)
    {
        this.Log = logger;
    }

    /// <summary>
    /// Searches for teams based on a JMESPath expression.
    /// </summary>
    /// <param name="jmesPathExpression">The JMESPath expression used to filter the teams.</param>
    /// <returns>A list of teams that match the JMESPath expression.</returns>
    [KernelFunction, Description("Searches for teams based on a JMESPath expression. String literals used in jmesPathExpression must always be lower-case.")]
    [return: Description("A collection of JSON documents/objects resulting from the JMESPath expression.")]
    public async Task<List<JsonDocument>> SearchTeamsAsync([Description("The query used to filter a JSON document with a single `teams` array of Team objects. Must be a valid JMESPath expression. Always use lower-case strings for all literals when searching.")] string jmesPathExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);

        JsonTransformer transformer = jmesPathExpression.ToJmesPathForSearch(this.Log);

        this.Log?.LogTrace("Executing JMESPath expression: {jmesPathExpression}", transformer);
        List<JsonDocument> results = [];
        for (var i = 0; ; i++)
        {
            List<Team>? teams = await GetTeamsDetailedAsync(i).ConfigureAwait(false);
            if (teams?.Count is null or 0)
            {
                break;
            }

            JsonElement eltToTransform = JsonSerializer.SerializeToElement(new { teams }, JsonSerialzationOptions.Default);
            JsonDocument filteredTeams = transformer.Transform(eltToTransform);
            this.Log?.LogTrace("JsonCons.JMESPath result: {jsonConsResult}", filteredTeams.RootElement.ToString());

            if (filteredTeams is not null)
            {
                if ((filteredTeams.RootElement.ValueKind is JsonValueKind.Array && filteredTeams.RootElement.EnumerateArray().Any())
                    || (filteredTeams.RootElement.ValueKind is JsonValueKind.Object && filteredTeams.RootElement.EnumerateObject().Any()))
                {
                    results.Add(filteredTeams);
                }
            }
        }

        this.Log?.LogDebug("Resulting document: {searchResults}", JsonSerializer.Serialize(results));

        return results;
    }

    /// <summary>
    /// Searches for teams within a district based on a JMESPath expression.
    /// </summary>
    /// <param name="districtKey">The key of the district to search within.</param>
    /// <param name="jmesPathExpression">The JMESPath expression used to filter the teams.</param>
    /// <returns>A list of teams that match the JMESPath expression.</returns>
    [KernelFunction, Description("Searches for teams within a district based on a JMESPath expression.")]
    [return: Description("A list of (non-detailed) teams that match the JMESPath expression.")]
    public async Task<List<TeamSimple>> SearchDistrictTeamsAsync(
        [Description("The key of the district to search within.")] string districtKey,
        [Description("The JMESPath expression used to filter the teams.")] string jmesPathExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);

        List<TeamSimple>? matches = await GetDistrictTeamsAsync(districtKey).ConfigureAwait(false);

        var transformer = jmesPathExpression.ToJmesPathForSearch(this.Log);

        JsonDocument filteredTeams = transformer.Transform(JsonSerializer.SerializeToElement(matches, JsonSerialzationOptions.Default));
        matches = JsonSerializer.Deserialize<List<TeamSimple>>(filteredTeams, JsonSerialzationOptions.Default) ?? [];

        this.Log?.LogDebug("Resulting document: {searchResults}", JsonSerializer.Serialize(matches));

        return matches;
    }

    private Team? _sampleTeam;

    [KernelFunction, Description("Gets a JSON representation of a sample object for schema inference. Use to formulate valid JMESPath queries for Search functions.")]
    public async Task<string> GetSampleTeamObjectAsync()
    {
        _sampleTeam ??= await GetTeamDetailedAsync("frc2046").ConfigureAwait(false);

        return JsonSerializer.Serialize(_sampleTeam, Constants.SchemaSerializeOptions);
    }
}
