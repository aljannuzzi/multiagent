using TBAAPI.V3Client.Model;

using TBAStatReader;

internal record Best(MetricCategory Category, Event? Event, string Match, string Alliance, int Points)
{
    public override string ToString() => $@"Best {Category}: {(Event?.Name ?? "Undefined")} / {Match} / {Alliance}: {Points} points";
}
