namespace TBAAPI.V3Client.Api;
using System.Text.Json;

using Microsoft.Extensions.Logging;

public partial class DistrictApi
{
    internal ILogger? Log { get; set; }

    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("[]");

}
