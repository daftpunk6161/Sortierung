namespace RomCleanup.Api;

/// <summary>
/// Embedded OpenAPI 3.0.3 specification for the Romulus API.
/// </summary>
public static class OpenApiSpec
{
    public const string Json = """
{
  "openapi": "3.0.3",
  "info": {
    "title": "Romulus API",
    "version": "1.0.0",
    "description": "Romulus REST API — Your Collection, Perfected. Region deduplication, junk removal, format conversion."
  },
  "servers": [{ "url": "http://127.0.0.1:7878" }],
  "paths": {
    "/health": {
      "get": {
        "summary": "Health check",
        "responses": {
          "200": { "description": "Server status" }
        }
      }
    },
    "/runs": {
      "post": {
        "summary": "Create and execute a deduplication run",
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/RunRequest" }
            }
          }
        },
        "parameters": [
          { "name": "wait", "in": "query", "schema": { "type": "boolean" }, "description": "Wait for completion without cancelling the server-side run on client disconnect" },
          { "name": "waitTimeoutMs", "in": "query", "schema": { "type": "integer", "minimum": 1, "maximum": 1800000 }, "description": "Maximum wait time before returning 202 with the current run state" },
          { "name": "X-Idempotency-Key", "in": "header", "schema": { "type": "string" }, "description": "Reuse the same run for retries of the same request" },
          { "name": "X-Client-Id", "in": "header", "schema": { "type": "string", "maxLength": 64 }, "description": "Optional logical client binding used for run ownership checks" }
        ],
        "responses": {
          "202": { "description": "Run created (async)" },
          "200": { "description": "Run completed or reused" },
          "400": { "description": "Validation error" },
          "403": { "description": "Run belongs to another client" },
          "409": { "description": "Run conflict" }
        }
      }
    },
    "/runs/{runId}": {
      "get": {
        "summary": "Get run status",
        "responses": {
          "200": { "description": "Run status" },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" }
        }
      }
    },
    "/runs/{runId}/result": {
      "get": {
        "summary": "Get completed run result",
        "responses": {
          "200": { "description": "Full result" },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" },
          "409": { "description": "Run still in progress" }
        }
      }
    },
    "/runs/{runId}/reviews": {
      "get": {
        "summary": "Get review queue for a run",
        "responses": {
          "200": {
            "description": "Review queue",
            "content": {
              "application/json": {
                "schema": { "$ref": "#/components/schemas/ApiReviewQueue" }
              }
            }
          },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" }
        }
      }
    },
    "/runs/{runId}/reviews/approve": {
      "post": {
        "summary": "Approve review items for a run",
        "requestBody": {
          "required": false,
          "content": {
            "application/json": {
              "schema": { "$ref": "#/components/schemas/ApiReviewApprovalRequest" }
            }
          }
        },
        "responses": {
          "200": { "description": "Approval applied" },
          "400": { "description": "Validation error" },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" }
        }
      }
    },
    "/runs/{runId}/cancel": {
      "post": {
        "summary": "Cancel a run idempotently",
        "responses": {
          "200": { "description": "Cancel accepted or no-op for an already terminal run" },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" }
        }
      }
    },
    "/runs/{runId}/stream": {
      "get": {
        "summary": "SSE progress stream",
        "responses": {
          "200": { "description": "Server-Sent Events stream" },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" }
        }
      }
    },
    "/dats/status": {
      "get": {
        "summary": "Get DAT collection status and statistics",
        "responses": {
          "200": { "description": "DAT status including file counts per console, age, and catalog info" }
        }
      }
    },
    "/dats/update": {
      "post": {
        "summary": "Trigger DAT file download/update from catalog sources",
        "requestBody": {
          "required": false,
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "force": { "type": "boolean", "description": "Re-download even if target file already exists" }
                }
              }
            }
          }
        },
        "responses": {
          "200": { "description": "Update results with download/skip/fail counts" },
          "400": { "description": "DatRoot not configured or catalog empty" },
          "404": { "description": "dat-catalog.json not found" }
        }
      }
    },
    "/dats/import": {
      "post": {
        "summary": "Import a local DAT file into the DatRoot",
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "path": { "type": "string", "description": "Absolute path to the DAT file to import" }
                },
                "required": ["path"]
              }
            }
          }
        },
        "responses": {
          "200": { "description": "Import successful" },
          "400": { "description": "Validation error (path, format, security)" },
          "404": { "description": "Source file not found" }
        }
      }
    },
    "/convert": {
      "post": {
        "summary": "Convert ROM files to optimal format (CHD/RVZ/ZIP)",
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "type": "object",
                "properties": {
                  "input": { "type": "string", "description": "Absolute path to file or directory" },
                  "consoleKey": { "type": "string", "nullable": true, "description": "Console key for format selection (auto-detected if omitted)" },
                  "target": { "type": "string", "nullable": true, "description": "Target format: chd, rvz, zip, 7z (auto if omitted)" }
                },
                "required": ["input"]
              }
            }
          }
        },
        "responses": {
          "200": { "description": "Conversion result with per-file outcomes" },
          "400": { "description": "Validation error" },
          "404": { "description": "Input not found" },
          "500": { "description": "No converter available" }
        }
      }
    },
    "/runs/{runId}/completeness": {
      "get": {
        "summary": "Get collection completeness report per console",
        "responses": {
          "200": { "description": "Per-console completeness data comparing DAT entries to collection files" },
          "400": { "description": "No roots or DAT not available" },
          "403": { "description": "Run belongs to another client" },
          "404": { "description": "Run not found" },
          "409": { "description": "Run still in progress" }
        }
      }
    }
  },
  "components": {
    "schemas": {
      "OperationError": {
        "type": "object",
        "properties": {
          "code": { "type": "string" },
          "message": { "type": "string" },
          "kind": { "type": "string", "enum": ["Transient", "Recoverable", "Critical"] },
          "module": { "type": "string", "nullable": true }
        }
      },
      "RunRequest": {
        "type": "object",
        "properties": {
          "roots": { "type": "array", "items": { "type": "string" } },
          "mode": { "type": "string", "enum": ["DryRun", "Move"] },
          "preferRegions": { "type": "array", "items": { "type": "string" } },
          "removeJunk": { "type": "boolean" },
          "aggressiveJunk": { "type": "boolean" },
          "sortConsole": { "type": "boolean" },
          "enableDat": { "type": "boolean" },
          "enableDatAudit": { "type": "boolean" },
          "enableDatRename": { "type": "boolean" },
          "datRoot": { "type": "string", "nullable": true },
          "onlyGames": { "type": "boolean" },
          "keepUnknownWhenOnlyGames": { "type": "boolean" },
          "hashType": { "type": "string", "enum": ["SHA1", "SHA256", "MD5"] },
          "convertFormat": { "type": "string", "enum": ["auto", "chd", "rvz", "zip", "7z"] },
          "convertOnly": { "type": "boolean" },
          "approveReviews": { "type": "boolean" },
          "conflictPolicy": { "type": "string", "enum": ["Rename", "Skip", "Overwrite"] },
          "trashRoot": { "type": "string", "nullable": true },
          "extensions": { "type": "array", "items": { "type": "string" } }
        },
        "required": ["roots"]
      },
      "ApiRunResult": {
        "type": "object",
        "properties": {
          "orchestratorStatus": { "type": "string" },
          "exitCode": { "type": "integer" },
          "totalFiles": { "type": "integer" },
          "candidates": { "type": "integer" },
          "groups": { "type": "integer" },
          "winners": { "type": "integer" },
          "losers": { "type": "integer" },
          "games": { "type": "integer" },
          "unknown": { "type": "integer" },
          "junk": { "type": "integer" },
          "bios": { "type": "integer" },
          "datMatches": { "type": "integer" },
          "healthScore": { "type": "integer" },
          "convertedCount": { "type": "integer" },
          "convertErrorCount": { "type": "integer" },
          "convertSkippedCount": { "type": "integer" },
          "convertBlockedCount": { "type": "integer" },
          "convertReviewCount": { "type": "integer" },
          "convertLossyWarningCount": { "type": "integer" },
          "convertVerifyPassedCount": { "type": "integer" },
          "convertVerifyFailedCount": { "type": "integer" },
          "convertSavedBytes": { "type": "integer", "format": "int64" },
          "datHaveCount": { "type": "integer" },
          "datHaveWrongNameCount": { "type": "integer" },
          "datMissCount": { "type": "integer" },
          "datUnknownCount": { "type": "integer" },
          "datAmbiguousCount": { "type": "integer" },
          "datRenameProposedCount": { "type": "integer" },
          "datRenameExecutedCount": { "type": "integer" },
          "datRenameSkippedCount": { "type": "integer" },
          "datRenameFailedCount": { "type": "integer" },
          "junkRemovedCount": { "type": "integer" },
          "filteredNonGameCount": { "type": "integer" },
          "junkFailCount": { "type": "integer" },
          "moveCount": { "type": "integer" },
          "skipCount": { "type": "integer" },
          "consoleSortMoved": { "type": "integer" },
          "consoleSortFailed": { "type": "integer" },
          "consoleSortReviewed": { "type": "integer" },
          "consoleSortBlocked": { "type": "integer" },
          "failCount": { "type": "integer" },
          "savedBytes": { "type": "integer", "format": "int64" },
          "durationMs": { "type": "integer", "format": "int64" },
          "preflightWarnings": { "type": "array", "items": { "type": "string" }, "nullable": true },
          "phaseMetrics": { "$ref": "#/components/schemas/ApiPhaseMetrics", "nullable": true },
          "dedupeGroups": { "type": "array", "items": { "$ref": "#/components/schemas/ApiDedupeGroup" }, "nullable": true },
          "conversionPlans": { "type": "array", "items": { "$ref": "#/components/schemas/ApiConversionPlan" }, "nullable": true },
          "conversionBlocked": { "type": "array", "items": { "$ref": "#/components/schemas/ApiConversionBlocked" }, "nullable": true },
          "error": { "$ref": "#/components/schemas/OperationError", "nullable": true }
        }
      },
      "ApiPhaseMetrics": {
        "type": "object",
        "properties": {
          "runId": { "type": "string", "nullable": true },
          "startedAt": { "type": "string", "format": "date-time", "nullable": true },
          "totalDurationMs": { "type": "integer", "format": "int64" },
          "phases": { "type": "array", "items": { "$ref": "#/components/schemas/ApiPhaseMetric" } }
        }
      },
      "ApiPhaseMetric": {
        "type": "object",
        "properties": {
          "phase": { "type": "string" },
          "startedAt": { "type": "string", "format": "date-time" },
          "durationMs": { "type": "integer", "format": "int64" },
          "itemCount": { "type": "integer" },
          "itemsPerSec": { "type": "number", "format": "double" },
          "percentOfTotal": { "type": "number", "format": "double" },
          "status": { "type": "string" }
        }
      },
      "ApiDedupeGroup": {
        "type": "object",
        "properties": {
          "gameKey": { "type": "string" },
          "winner": { "$ref": "#/components/schemas/RomCandidate" },
          "losers": { "type": "array", "items": { "$ref": "#/components/schemas/RomCandidate" } }
        }
      },
      "ApiConversionPlan": {
        "type": "object",
        "properties": {
          "sourcePath": { "type": "string" },
          "targetExtension": { "type": "string", "nullable": true },
          "safety": { "type": "string" },
          "outcome": { "type": "string" },
          "verification": { "type": "string" }
        }
      },
      "ApiConversionBlocked": {
        "type": "object",
        "properties": {
          "sourcePath": { "type": "string" },
          "reason": { "type": "string" },
          "safety": { "type": "string" }
        }
      },
      "ApiReviewItem": {
        "type": "object",
        "properties": {
          "mainPath": { "type": "string" },
          "fileName": { "type": "string" },
          "consoleKey": { "type": "string" },
          "sortDecision": { "type": "string" },
          "decisionClass": { "type": "string" },
          "evidenceTier": { "type": "string" },
          "primaryMatchKind": { "type": "string" },
          "platformFamily": { "type": "string" },
          "matchLevel": { "type": "string" },
          "matchReasoning": { "type": "string" },
          "detectionConfidence": { "type": "integer" },
          "approved": { "type": "boolean" }
        }
      },
      "ApiReviewQueue": {
        "type": "object",
        "properties": {
          "runId": { "type": "string" },
          "total": { "type": "integer" },
          "items": { "type": "array", "items": { "$ref": "#/components/schemas/ApiReviewItem" } }
        }
      },
      "ApiReviewApprovalRequest": {
        "type": "object",
        "properties": {
          "consoleKey": { "type": "string", "nullable": true },
          "matchLevel": { "type": "string", "nullable": true },
          "paths": { "type": "array", "items": { "type": "string" }, "nullable": true }
        }
      },
      "MatchEvidence": {
        "type": "object",
        "properties": {
          "level": { "type": "string", "enum": ["None", "Weak", "Probable", "Strong", "Exact", "Ambiguous"] },
          "reasoning": { "type": "string" },
          "sources": { "type": "array", "items": { "type": "string" } },
          "hasHardEvidence": { "type": "boolean" },
          "hasConflict": { "type": "boolean" },
          "datVerified": { "type": "boolean" },
          "tier": { "type": "string" },
          "primaryMatchKind": { "type": "string" }
        }
      },
      "RomCandidate": {
        "type": "object",
        "properties": {
          "mainPath": { "type": "string" },
          "gameKey": { "type": "string" },
          "region": { "type": "string" },
          "regionScore": { "type": "integer" },
          "formatScore": { "type": "integer" },
          "versionScore": { "type": "integer", "format": "int64" },
          "headerScore": { "type": "integer" },
          "completenessScore": { "type": "integer" },
          "sizeTieBreakScore": { "type": "integer", "format": "int64" },
          "sizeBytes": { "type": "integer", "format": "int64" },
          "extension": { "type": "string" },
          "consoleKey": { "type": "string" },
          "datMatch": { "type": "boolean" },
          "hash": { "type": "string", "nullable": true },
          "headerlessHash": { "type": "string", "nullable": true },
          "datGameName": { "type": "string", "nullable": true },
          "datAuditStatus": { "type": "string" },
          "category": { "type": "string" },
          "classificationReasonCode": { "type": "string" },
          "classificationConfidence": { "type": "integer" },
          "detectionConfidence": { "type": "integer" },
          "detectionConflict": { "type": "boolean" },
          "hasHardEvidence": { "type": "boolean" },
          "isSoftOnly": { "type": "boolean" },
          "sortDecision": { "type": "string" },
          "decisionClass": { "type": "string" },
          "matchEvidence": { "$ref": "#/components/schemas/MatchEvidence" },
          "evidenceTier": { "type": "string" },
          "primaryMatchKind": { "type": "string" },
          "platformFamily": { "type": "string" }
        }
      }
    },
    "securitySchemes": {
      "ApiKey": {
        "type": "apiKey",
        "in": "header",
        "name": "X-Api-Key"
      }
    }
  },
  "security": [{ "ApiKey": [] }]
}
""";
}
