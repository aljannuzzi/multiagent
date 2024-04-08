using TBAAPI.V3Client.Model;

using TBAStatReader;

internal record Best(MetricCategory Category, Event? Event, string Match, string Alliance, int Points)
{
    public override string ToString() => $@"Best {this.Category}: {this.Event?.Name ?? "Undefined"} / {this.Match} / {this.Alliance}: {this.Points} points";
}
