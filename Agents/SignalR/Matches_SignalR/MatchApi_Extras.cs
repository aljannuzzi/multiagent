namespace TBAAPI.V3Client.Api;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

using Models.Json;

using TBAAPI.V3Client.Client;
using TBAAPI.V3Client.Model;

public partial class MatchApi
{
    private ILogger? Log { get; }

    private static readonly JsonDocument EmptyJsonDocument = JsonDocument.Parse("[]");

    public MatchApi(Configuration config, ILogger logger) : this(config) => this.Log = logger;
}
