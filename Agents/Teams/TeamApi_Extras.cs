namespace TBAAPI.V3Client.Api;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

using Microsoft.SemanticKernel;

using Models.Json;

using TBAAPI.V3Client.Model;

public partial class TeamApi
{
    /// <summary>
    /// Searches for teams based on a JMESPath expression.
    /// </summary>
    /// <param name="jmesPathExpression">The JMESPath expression used to filter the teams.</param>
    /// <returns>A list of teams that match the JMESPath expression.</returns>
    [KernelFunction, Description("Searches for teams based on a JMESPath expression.")]
    [return: Description("A list of teams that match the JMESPath expression.")]
    public async Task<List<Team>> SearchTeamsAsync([Description("The query used to filter a JSON document with a single `teams` array of Team objects. Must be a valid JMESPath expression. Use lower-case strings for literals when searching, all literal values must be surrounded in single quotes (')")] string jmesPathExpression)

    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);

        List<Team> matches = [];
        for (var i = 0; ; i++)
        {
            List<Team> pageTeams = await GetTeamsAsync(i);
            if (pageTeams?.Count is null or 0)
            {
                break;
            }

            JsonDocument filteredTeams = JsonCons.JmesPath.JsonTransformer.Transform(JsonSerializer.SerializeToElement(pageTeams, JsonSerialzationOptions.Default), jmesPathExpression);
            matches.AddRange(JsonSerializer.Deserialize<List<Team>>(filteredTeams, JsonSerialzationOptions.Default) ?? []);
        }

        return matches;
    }

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
        [Description("The query used to filter a JSON document with a single `teams` array of Team objects. Must be a valid JMESPath expression. Use lower-case strings for literals when searching, all literal values must be surrounded in single quotes (')")] string jmesPathExpression)

    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jmesPathExpression);

        List<Team> matches = await GetDistrictTeamsAsync(districtKey);

        JsonDocument filteredTeams = JsonCons.JmesPath.JsonTransformer.Transform(JsonSerializer.SerializeToElement(matches, JsonSerialzationOptions.Default), jmesPathExpression);
        matches = JsonSerializer.Deserialize<List<Team>>(filteredTeams, JsonSerialzationOptions.Default) ?? [];

        return matches;
    }
}
