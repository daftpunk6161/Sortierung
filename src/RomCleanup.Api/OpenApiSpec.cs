namespace RomCleanup.Api;

/// <summary>
/// Embedded OpenAPI 3.0.3 specification for the ROM Cleanup API.
/// </summary>
public static class OpenApiSpec
{
    public const string Json = @"{
  ""openapi"": ""3.0.3"",
  ""info"": {
    ""title"": ""ROM Cleanup API"",
    ""version"": ""1.0.0"",
    ""description"": ""REST API for ROM collection management: region deduplication, junk removal, format conversion.""
  },
  ""servers"": [{ ""url"": ""http://127.0.0.1:7878"" }],
  ""paths"": {
    ""/health"": {
      ""get"": {
        ""summary"": ""Health check"",
        ""responses"": {
          ""200"": { ""description"": ""Server status"" }
        }
      }
    },
    ""/runs"": {
      ""post"": {
        ""summary"": ""Create and execute a deduplication run"",
        ""parameters"": [
          { ""name"": ""wait"", ""in"": ""query"", ""schema"": { ""type"": ""boolean"" }, ""description"": ""Wait for completion without cancelling the server-side run on client disconnect"" },
          { ""name"": ""waitTimeoutMs"", ""in"": ""query"", ""schema"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 1800000 }, ""description"": ""Maximum wait time before returning 202 with the current run state"" },
          { ""name"": ""X-Idempotency-Key"", ""in"": ""header"", ""schema"": { ""type"": ""string"" }, ""description"": ""Reuse the same run for retries of the same request"" }
        ],
        ""requestBody"": {
          ""required"": true,
          ""content"": {
            ""application/json"": {
              ""schema"": {
                ""type"": ""object"",
                ""required"": [""roots""],
                ""properties"": {
                  ""roots"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
                  ""mode"": { ""type"": ""string"", ""enum"": [""DryRun"", ""Move""], ""default"": ""DryRun"" },
                  ""preferRegions"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
                }
              }
            }
          }
        },
        ""responses"": {
          ""202"": { ""description"": ""Run created (async)"" },
          ""200"": { ""description"": ""Run completed or an existing completed idempotent run was reused"" },
          ""400"": { ""description"": ""Validation error"" },
          ""409"": { ""description"": ""Run already active"" }
        }
      }
    },
    ""/runs/{runId}"": {
      ""get"": {
        ""summary"": ""Get run status"",
        ""parameters"": [{ ""name"": ""runId"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }],
        ""responses"": {
          ""200"": { ""description"": ""Run status"" },
          ""404"": { ""description"": ""Run not found"" }
        }
      }
    },
    ""/runs/{runId}/result"": {
      ""get"": {
        ""summary"": ""Get completed run result"",
        ""parameters"": [{ ""name"": ""runId"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }],
        ""responses"": {
          ""200"": { ""description"": ""Full result"" },
          ""404"": { ""description"": ""Run not found"" },
          ""409"": { ""description"": ""Run still in progress"" }
        }
      }
    },
    ""/runs/{runId}/cancel"": {
      ""post"": {
        ""summary"": ""Cancel a run idempotently"",
        ""parameters"": [{ ""name"": ""runId"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }],
        ""responses"": {
          ""200"": { ""description"": ""Cancel accepted or no-op for an already terminal run"" },
          ""404"": { ""description"": ""Run not found"" }
        }
      }
    },
    ""/runs/{runId}/stream"": {
      ""get"": {
        ""summary"": ""SSE progress stream"",
        ""parameters"": [{ ""name"": ""runId"", ""in"": ""path"", ""required"": true, ""schema"": { ""type"": ""string"" } }],
        ""responses"": {
          ""200"": { ""description"": ""Server-Sent Events stream"" },
          ""404"": { ""description"": ""Run not found"" }
        }
      }
    }
  },
  ""components"": {
    ""securitySchemes"": {
      ""ApiKey"": {
        ""type"": ""apiKey"",
        ""in"": ""header"",
        ""name"": ""X-Api-Key""
      }
    }
  },
  ""security"": [{ ""ApiKey"": [] }]
}";
}
