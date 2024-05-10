namespace Agent.Core.Extensions;
using System;

using JsonCons.JmesPath;

using Microsoft.Extensions.Logging;

public static class StringExtensions
{
    public static JsonTransformer ToJmesPathForSearch(this string jmesPathExpression, ILogger? log)
    {
        var before = jmesPathExpression;
        jmesPathExpression = jmesPathExpression.ToLowerInvariant();

        if (before != jmesPathExpression)
        {
            log?.LogWarning("JMESPath expression was modified to lowercase: {before} -> {after}", before, jmesPathExpression);
        }

        JsonTransformer transformer;
        try
        {
            transformer = JsonTransformer.Parse(jmesPathExpression);
            return transformer;
        }
        catch (JmesPathParseException)
        {
            throw new ArgumentException("Invalid JMESPath expression", jmesPathExpression);
        }
    }
}
