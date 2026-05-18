using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Core.Policy;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Policy;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave9PolicyGovernanceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "romulus-w9-policy-" + Guid.NewGuid().ToString("N"));

    public Wave9PolicyGovernanceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void PolicyDocumentLoader_ParsesYamlAndJsonExamples()
    {
        var yaml = """
            id: eu-preferred
            name: EU bevorzugt
            preferredRegions:
              - EU
            deniedTitleTokens: [Demo]
            """;
        var json = """
            {
              "id": "all-zip",
              "name": "Alle ZIP",
              "allowedExtensions": [".zip"]
            }
            """;

        var yamlPolicy = PolicyDocumentLoader.Parse(yaml);
        var jsonPolicy = PolicyDocumentLoader.Parse(json);

        Assert.Equal("eu-preferred", yamlPolicy.Id);
        Assert.Equal(["EU"], yamlPolicy.PreferredRegions);
        Assert.Equal(["Demo"], yamlPolicy.DeniedTitleTokens);
        Assert.Equal("all-zip", jsonPolicy.Id);
        Assert.Equal([".zip"], jsonPolicy.AllowedExtensions);
    }

    [Fact]
    public void PolicyDocumentLoader_CreatesAndVerifiesDetachedSignature()
    {
        var keyPath = Path.Combine(_tempDir, "policy-signing.key");
        var signing = new AuditSigningService(new Romulus.Infrastructure.FileSystem.FileSystemAdapter(), keyFilePath: keyPath);
        var policyPath = Path.Combine(_tempDir, "policy.yaml");
        var policyText = """
            id: signed
            name: Signed Policy
            allowedExtensions: [.zip]
            """;
        File.WriteAllText(policyPath, policyText);

        var signaturePath = PolicyDocumentLoader.WriteSignatureFile(
            policyPath,
            policyText,
            signing,
            new DateTime(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc));
        var status = PolicyDocumentLoader.VerifySignatureFile(policyPath, policyText, signing);
        var tampered = PolicyDocumentLoader.VerifySignatureFile(policyPath, policyText + "\n# changed", signing);

        Assert.Equal(policyPath + ".sig.json", signaturePath);
        Assert.True(status.IsPresent);
        Assert.True(status.IsValid);
        Assert.False(tampered.IsValid);
        Assert.Contains("fingerprint", tampered.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolicyDocumentLoader_LoadFromFile_ParsesNestedYamlListsAndQuotedComments()
    {
        var policyPath = Path.Combine(_tempDir, "nested-policy.yaml");
        File.WriteAllText(policyPath, """
            id: nested
            name: 'Nested Policy'
            description: "Keeps # inside quotes"
            preferredRegions:
              - EU
            allowedExtensions:
              - .zip
            deniedTitleTokens:
              - 'Demo'
            requiredExtensionsByConsole:
              SNES:
                - .sfc
                - ".smc"
              NES: [.nes, .zip, .nes]
            """);

        var policy = PolicyDocumentLoader.LoadFromFile(policyPath);

        Assert.Equal("nested", policy.Id);
        Assert.Equal("Nested Policy", policy.Name);
        Assert.Equal("Keeps # inside quotes", policy.Description);
        Assert.Equal(["EU"], policy.PreferredRegions);
        Assert.Equal([".zip"], policy.AllowedExtensions);
        Assert.Equal(["Demo"], policy.DeniedTitleTokens);
        Assert.Equal(["NES", "SNES"], policy.RequiredExtensionsByConsole.Keys.ToArray());
        Assert.Equal([".nes", ".zip"], policy.RequiredExtensionsByConsole["NES"]);
        Assert.Equal([".sfc", ".smc"], policy.RequiredExtensionsByConsole["SNES"]);
    }

    [Fact]
    public void PolicyDocumentLoader_Parse_RejectsMalformedOrIncompletePolicies()
    {
        Assert.Throws<ArgumentException>(() => PolicyDocumentLoader.LoadFromFile(" "));
        Assert.Throws<ArgumentException>(() => PolicyDocumentLoader.GetSignaturePath(" "));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse(""));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("{not-json"));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("- orphan"));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("id without separator"));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("id: missing-name\nallowedExtensions: [.zip]"));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("id: no-rules\nname: No Rules"));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("id: x\nname: X\nunknown: y"));
        Assert.Throws<FormatException>(() => PolicyDocumentLoader.Parse("""
            id: bad-required-list
            name: Bad Required List
            requiredExtensionsByConsole:
              - .sfc
            """));
    }

    [Fact]
    public void PolicyDocumentLoader_VerifySignatureText_ReportsInvalidSignatureStates()
    {
        var keyPath = Path.Combine(_tempDir, "policy-signing-extra.key");
        var signing = new AuditSigningService(new Romulus.Infrastructure.FileSystem.FileSystemAdapter(), keyFilePath: keyPath);
        var policyText = "id: signed\nname: Signed\nallowedExtensions: [.zip]\n";
        var valid = PolicyDocumentLoader.CreateSignature(
            policyText,
            " ",
            signing,
            new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc),
            signer: " ");

        Assert.Equal("policy.yaml", valid.PolicyFileName);
        Assert.Equal("local-audit-key", valid.Signer);

        var missingFile = PolicyDocumentLoader.VerifySignatureFile(
            Path.Combine(_tempDir, "missing-policy.yaml"),
            policyText,
            signing);
        var blank = PolicyDocumentLoader.VerifySignatureText(policyText, " ", signing, "blank.sig.json");
        var nullJson = PolicyDocumentLoader.VerifySignatureText(policyText, "null", signing, "null.sig.json");
        var invalidJson = PolicyDocumentLoader.VerifySignatureText(policyText, "{not-json", signing, "bad.sig.json");
        var badVersion = PolicyDocumentLoader.VerifySignatureText(
            policyText,
            JsonSerializer.Serialize(valid with { Version = "other-version" }),
            signing,
            "version.sig.json");
        var badKey = PolicyDocumentLoader.VerifySignatureText(
            policyText,
            JsonSerializer.Serialize(valid with { KeyId = "wrong-key" }),
            signing,
            "key.sig.json");
        var badHmac = PolicyDocumentLoader.VerifySignatureText(
            policyText,
            JsonSerializer.Serialize(valid with { HmacSha256 = "00" + valid.HmacSha256 }),
            signing,
            "hmac.sig.json");

        Assert.False(missingFile.IsPresent);
        Assert.Equal(Path.Combine(_tempDir, "missing-policy.yaml") + ".sig.json", missingFile.SignaturePath);
        Assert.False(blank.IsPresent);
        Assert.False(nullJson.IsValid);
        Assert.Contains("object", nullJson.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(invalidJson.IsValid);
        Assert.Equal("JsonException", invalidJson.Error);
        Assert.False(badVersion.IsValid);
        Assert.Contains("version", badVersion.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(badKey.IsValid);
        Assert.Contains("key id", badKey.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(badHmac.IsValid);
        Assert.Contains("HMAC", badHmac.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolicyEngine_ValidatesTargetState_WithDeterministicViolationOrder()
    {
        var snapshot = new LibrarySnapshot
        {
            GeneratedUtc = new DateTime(2026, 5, 3, 10, 0, 0, DateTimeKind.Utc),
            Roots = [@"C:\roms"],
            Entries =
            [
                new LibrarySnapshotEntry
                {
                    Path = @"C:\roms\SNES\Zelda Demo.sfc",
                    FileName = "Zelda Demo.sfc",
                    Extension = ".sfc",
                    ConsoleKey = "SNES",
                    GameKey = "zelda",
                    Region = "US"
                },
                new LibrarySnapshotEntry
                {
                    Path = @"C:\roms\SNES\Mario.zip",
                    FileName = "Mario.zip",
                    Extension = ".zip",
                    ConsoleKey = "SNES",
                    GameKey = "mario",
                    Region = "EU"
                }
            ],
            Summary = new LibrarySnapshotSummary { TotalEntries = 2 }
        };
        var policy = new LibraryPolicy
        {
            Id = "strict",
            Name = "Strict",
            PreferredRegions = ["EU"],
            AllowedExtensions = [".zip"],
            DeniedTitleTokens = ["Demo"]
        };

        var report = new PolicyEngine().Validate(snapshot, policy, "abc");

        Assert.False(report.IsCompliant);
        Assert.Equal("abc", report.PolicyFingerprint);
        Assert.Equal(["allowed-extensions", "denied-title-token", "preferred-regions"],
            report.Violations.Select(static v => v.RuleId).ToArray());
        Assert.Equal(3, report.Summary.Total);
        Assert.Equal(2, report.Summary.BySeverity["error"]);
        Assert.Equal(1, report.Summary.BySeverity["warning"]);
    }

    [Fact]
    public void PolicyReportCsvExport_SanitizesSpreadsheetFormulaPrefixes()
    {
        var report = new PolicyValidationReport
        {
            PolicyId = "=policy",
            PolicyName = "Policy",
            Violations =
            [
                new PolicyRuleViolation
                {
                    RuleId = "denied-title-token",
                    Severity = "error",
                    Path = "=cmd|calc",
                    Message = "+formula"
                }
            ]
        };

        var csv = PolicyValidationReportExporter.ToCsv(report);

        Assert.Contains("'=policy", csv, StringComparison.Ordinal);
        Assert.Contains("'=cmd|calc", csv, StringComparison.Ordinal);
        Assert.Contains("'+formula", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatePolicyParser_RequiresPolicyAndRoots()
    {
        var missingPolicy = CliArgsParser.Parse(["validate-policy", "--roots", _tempDir]);
        var missingRoots = CliArgsParser.Parse(["validate-policy", "--policy", Path.Combine(_tempDir, "policy.yaml")]);

        Assert.Equal(3, missingPolicy.ExitCode);
        Assert.Contains(missingPolicy.Errors, static error => error.Contains("--policy", StringComparison.Ordinal));
        Assert.Equal(3, missingRoots.ExitCode);
        Assert.Contains(missingRoots.Errors, static error => error.Contains("--roots", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatePolicyParser_AcceptsSignPolicyFlag()
    {
        var result = CliArgsParser.Parse(["validate-policy", "--policy", Path.Combine(_tempDir, "policy.yaml"), "--roots", _tempDir, "--sign"]);

        Assert.Equal(CliCommand.ValidatePolicy, result.Command);
        Assert.True(result.Options!.SignPolicy);
    }

    [Fact]
    public async Task ValidatePolicySubcommand_UsesPersistedCollectionIndex_AndWritesJsonReport()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var policyPath = Path.Combine(_tempDir, "policy.yaml");
        await File.WriteAllTextAsync(policyPath, """
            id: all-zip
            name: Alle ZIP
            allowedExtensions: [.zip]
            """);
        var dbPath = Path.Combine(_tempDir, "collection.db");
        using (var index = new LiteDbCollectionIndex(dbPath))
        {
            await index.UpsertEntriesAsync(
            [
                new CollectionIndexEntry
                {
                    Path = Path.Combine(root, "bad.sfc"),
                    Root = root,
                    FileName = "bad.sfc",
                    Extension = ".sfc",
                    ConsoleKey = "SNES",
                    GameKey = "bad",
                    Region = "EU"
                }
            ]);
        }

        using var overrides = Romulus.CLI.Program.SetTestPathOverrides(new CliPathOverrides
        {
            CollectionDbPath = dbPath,
            AuditSigningKeyPath = Path.Combine(_tempDir, "audit.key")
        });

        var result = await ProgramTestRunner.RunSubcommandAsync(() =>
            Romulus.CLI.Program.SubcommandValidatePolicyAsync(new CliRunOptions
            {
                PolicyPath = policyPath,
                Roots = [root],
                OutputPath = Path.Combine(_tempDir, "report.json"),
                SignPolicy = true
            }));

        Assert.Equal(4, result.ExitCode);
        Assert.True(File.Exists(policyPath + ".sig.json"));
        var reportPath = Path.Combine(_tempDir, "report.json");
        Assert.True(File.Exists(reportPath));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.False(doc.RootElement.GetProperty("isCompliant").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("signature").GetProperty("isPresent").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("signature").GetProperty("isValid").GetBoolean());
        Assert.Equal("allowed-extensions", doc.RootElement.GetProperty("violations")[0].GetProperty("ruleId").GetString());
    }
}
