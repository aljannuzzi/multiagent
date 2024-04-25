namespace TBAAPI.V3Client.Api;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using TBAAPI.V3Client.Client;

public partial class DistrictApi
{
    private ILogger? Log { get; }

    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("[]");

    public DistrictApi(Configuration config, ILogger logger) : this(config) => this.Log = logger;
}
