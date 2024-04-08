using System.Text.Json.Nodes;

using TBAStatReader;

internal record Best(MetricCategory Category, JsonObject Event, string Match, string Alliance, int Points)
{
    public override string ToString() => $@"Best {Category}: {Event["name"]} / {Match} / {Alliance}: {Points} points";
}
