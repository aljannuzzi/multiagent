namespace TBAAPI.V3Client.Api;

using System.ComponentModel;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using Models.Json;

using TBAAPI.V3Client.Client;
using TBAAPI.V3Client.Model;

public partial class DistrictApi
{
    private ILogger? Log { get; }

    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("[]");

    public DistrictApi(Configuration config, ILogger logger) : this(config) => this.Log = logger;

    /// <summary>
    /// Searches for teams within a district based on a JMESPath expression.
    /// </summary>
    /// <param name="districtKey">The key of the district to search within.</param>
    /// <param name="jmesPathExpression">The JMESPath expression used to filter the teams.</param>
    /// <returns>A list of teams that match the JMESPath expression.</returns>
    [KernelFunction, Description("Searches for teams within a district based on a JMESPath expression.")]
    [return: Description("A list of teams that match the JMESPath expression.")]
    public async Task<List<Team>> SearchDistrictTeamsAsync(
        [Description("The key of the district to search within.")] string districtKey,
        [Description("The JMESPath expression used to filter the teams.")] string jmesPathExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);

        List<Team>? matches = await GetDistrictTeamsAsync(districtKey).ConfigureAwait(false);

        JsonDocument filteredTeams = JsonCons.JmesPath.JsonTransformer.Transform(JsonSerializer.SerializeToElement(matches, JsonSerialzationOptions.Default), jmesPathExpression);
        matches = JsonSerializer.Deserialize<List<Team>>(filteredTeams, JsonSerialzationOptions.Default) ?? [];

        this.Log?.LogDebug("Resulting document: {searchResults}", JsonSerializer.Serialize(matches));

        return matches;
    }
}
