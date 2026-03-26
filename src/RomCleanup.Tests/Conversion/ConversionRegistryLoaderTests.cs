using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Conversion;
using Xunit;

namespace RomCleanup.Tests.Conversion;

public sealed class ConversionRegistryLoaderTests
{
    [Fact]
    public void Constructor_LoadsCapabilitiesAndPolicies()
    {
        using var fixture = new LoaderFixture();
        fixture.WriteValidRegistry();
        fixture.WriteConsoles([("PS1", "Auto", ".chd", Array.Empty<string>())]);

        var loader = new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath);

        Assert.Single(loader.GetCapabilities());
        Assert.Equal(ConversionPolicy.Auto, loader.GetPolicy("PS1"));
        Assert.Equal(".chd", loader.GetPreferredTarget("PS1"));
    }

    [Fact]
    public void Constructor_UnknownConsoleInCapability_Throws()
    {
        using var fixture = new LoaderFixture();
        fixture.WriteRegistry([
            Cap(".iso", ".chd", new[] { "NOT_EXISTING" })
        ]);
        fixture.WriteConsoles([("PS1", "Auto", ".chd", Array.Empty<string>())]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath));

        Assert.Contains("Unknown console key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_MissingCapabilities_Throws()
    {
        using var fixture = new LoaderFixture();
        File.WriteAllText(fixture.RegistryPath, "{\"schemaVersion\":\"x\"}");
        fixture.WriteConsoles([("PS1", "Auto", ".chd", Array.Empty<string>())]);

        Assert.Throws<InvalidOperationException>(() => new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath));
    }

    [Fact]
    public void Constructor_InvalidPolicy_Throws()
    {
        using var fixture = new LoaderFixture();
        fixture.WriteValidRegistry();
        fixture.WriteConsoles([("PS1", "NotARealPolicy", ".chd", Array.Empty<string>())]);

        Assert.Throws<InvalidOperationException>(() => new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath));
    }

        [Fact]
        public void Constructor_UnexpectedCapabilityProperty_Throws()
        {
                using var fixture = new LoaderFixture();
                fixture.WriteRegistryWithRawJson(
                        """
                        {
                            "schemaVersion": "conversion-registry-v1",
                            "capabilities": [
                                {
                                    "sourceExtension": ".iso",
                                    "targetExtension": ".chd",
                                    "tool": { "toolName": "chdman" },
                                    "command": "createcd",
                                    "resultIntegrity": "Lossless",
                                    "lossless": true,
                                    "cost": 0,
                                    "verification": "ChdmanVerify",
                                    "condition": "None",
                                    "unexpected": "boom"
                                }
                            ]
                        }
                        """);
                fixture.WriteConsoles([("PS1", "Auto", ".chd", Array.Empty<string>())]);

                var ex = Assert.Throws<InvalidOperationException>(() =>
                        new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath));

                Assert.Contains("Unexpected property", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Constructor_UnexpectedConsoleProperty_Throws()
        {
                using var fixture = new LoaderFixture();
                fixture.WriteValidRegistry();
                fixture.WriteConsolesWithRawJson(
                        """
                        {
                            "consoles": [
                                {
                                    "key": "PS1",
                                    "conversionPolicy": "Auto",
                                    "preferredConversionTarget": ".chd",
                                    "alternativeTargets": [],
                                    "unknownField": true
                                }
                            ]
                        }
                        """);

                var ex = Assert.Throws<InvalidOperationException>(() =>
                        new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath));

                Assert.Contains("Unexpected property", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

    [Fact]
    public void GetAlternativeTargets_ReturnsConfiguredValues()
    {
        using var fixture = new LoaderFixture();
        fixture.WriteValidRegistry();
        fixture.WriteConsoles([("PS1", "Auto", ".chd", new[] { ".zip", ".rvz" })]);

        var loader = new ConversionRegistryLoader(fixture.RegistryPath, fixture.ConsolesPath);

        var alternatives = loader.GetAlternativeTargets("PS1");
        Assert.Equal(2, alternatives.Count);
        Assert.Contains(".zip", alternatives);
        Assert.Contains(".rvz", alternatives);
    }

    private static object Cap(string source, string target, IReadOnlyList<string> consoles)
    {
        return new
        {
            sourceExtension = source,
            targetExtension = target,
            tool = new { toolName = "chdman" },
            command = "createcd",
            applicableConsoles = consoles,
            requiredSourceIntegrity = (string?)null,
            resultIntegrity = "Lossless",
            lossless = true,
            cost = 0,
            verification = "ChdmanVerify",
            description = "test",
            condition = "None"
        };
    }

    private sealed class LoaderFixture : IDisposable
    {
        private readonly string _root;
        public string RegistryPath { get; }
        public string ConsolesPath { get; }

        public LoaderFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "RomCleanup.LoaderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            RegistryPath = Path.Combine(_root, "conversion-registry.json");
            ConsolesPath = Path.Combine(_root, "consoles.json");
        }

        public void WriteValidRegistry() => WriteRegistry([Cap(".iso", ".chd", new[] { "PS1" })]);

        public void WriteRegistry(object[] capabilities)
        {
            var payload = new
            {
                schemaVersion = "conversion-registry-v1",
                capabilities
            };
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(payload));
        }

        public void WriteRegistryWithRawJson(string json)
        {
            File.WriteAllText(RegistryPath, json);
        }

        public void WriteConsoles((string Key, string Policy, string? Preferred, string[] Alternatives)[] consoles)
        {
            var payload = new
            {
                consoles = consoles.Select(c => new
                {
                    key = c.Key,
                    conversionPolicy = c.Policy,
                    preferredConversionTarget = c.Preferred,
                    alternativeTargets = c.Alternatives
                }).ToArray()
            };
            File.WriteAllText(ConsolesPath, JsonSerializer.Serialize(payload));
        }

        public void WriteConsolesWithRawJson(string json)
        {
            File.WriteAllText(ConsolesPath, json);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
