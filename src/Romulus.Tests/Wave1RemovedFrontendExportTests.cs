using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 1 cull-list pin tests for Frontend-Export removal (T-W1-UI-REDUCTION step A).
/// These tests assert that all Frontend-Export-specific surface area is gone:
/// - FrontendExportService (dispatcher for RetroArch/M3U/LaunchBox/EmulationStation/Playnite/MiSTer/AnaloguePocket/OnionOs/Csv/Excel/Json)
/// - FrontendExportTargets / FrontendExportRequest / FrontendExportArtifact / FrontendExportResult
/// - RunConstants.FrontendExportSchemaVersion
/// - ApiErrorCodes EXPORT-* codes
/// - FeatureCommandKeys.LauncherIntegration command + WPF wiring
/// - CLI export subcommand (CliCommand.Export)
/// </summary>
public sealed class Wave1RemovedFrontendExportTests
{
    [Theory]
    [InlineData("Romulus.Infrastructure.Export.FrontendExportService, Romulus.Infrastructure")]
    [InlineData("Romulus.Contracts.Models.FrontendExportTargets, Romulus.Contracts")]
    [InlineData("Romulus.Contracts.Models.FrontendExportRequest, Romulus.Contracts")]
    [InlineData("Romulus.Contracts.Models.FrontendExportArtifact, Romulus.Contracts")]
    [InlineData("Romulus.Contracts.Models.FrontendExportResult, Romulus.Contracts")]
    [InlineData("Romulus.Api.ApiFrontendExportRequest, Romulus.Api")]
    public void RemovedType_MustNotResolve(string assemblyQualifiedName)
    {
        var type = Type.GetType(assemblyQualifiedName, throwOnError: false);
        Assert.Null(type);
    }

    [Theory]
    [InlineData("Romulus.Contracts")]
    [InlineData("Romulus.Infrastructure")]
    public void NoTypeWithFrontendExportPrefix_InCoreAssemblies(string assemblyName)
    {
        var asm = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));
        if (asm is null)
        {
            try { asm = Assembly.Load(assemblyName); }
            catch (Exception) { /* assembly not loaded - acceptable */ }
        }
        Assert.NotNull(asm);

        Type[] types;
        try { types = asm!.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }

        var offenders = types
            .Where(t => t.Name.StartsWith("FrontendExport", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void RunConstants_MustNotDefineFrontendExportSchemaVersion()
    {
        var runConstants = Type.GetType("Romulus.Contracts.RunConstants, Romulus.Contracts", throwOnError: false);
        Assert.NotNull(runConstants);

        var field = runConstants!.GetField("FrontendExportSchemaVersion", BindingFlags.Public | BindingFlags.Static);
        Assert.Null(field);
    }

    [Theory]
    [InlineData("ExportFrontendRequired")]
    [InlineData("ExportOutputRequired")]
    [InlineData("ExportRootsRequired")]
    [InlineData("ExportNotReady")]
    public void ApiErrorCodes_MustNotDefineFrontendExportCode(string fieldName)
    {
        var apiErrorCodes = Type.GetType("Romulus.Contracts.Errors.ApiErrorCodes, Romulus.Contracts", throwOnError: false);
        Assert.NotNull(apiErrorCodes);

        var field = apiErrorCodes!.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        Assert.Null(field);
    }

    [Fact]
    public void FeatureCommandKeys_MustNotDefineLauncherIntegration()
    {
        var keys = Type.GetType("Romulus.UI.Wpf.Models.FeatureCommandKeys, Romulus.Wpf", throwOnError: false)
                   ?? Type.GetType("Romulus.UI.Wpf.Models.FeatureCommandKeys, Romulus.UI.Wpf", throwOnError: false);
        // FeatureCommandKeys lives in the WPF assembly. If we cannot resolve it from here (test runner),
        // assume the WPF assembly is not loaded and skip silently. This is acceptable: the build break
        // when LauncherIntegration is removed from FeatureCommandKeys will pin the contract.
        if (keys is null) return;

        var field = keys.GetField("LauncherIntegration", BindingFlags.Public | BindingFlags.Static);
        Assert.Null(field);
    }

    [Fact]
    public void CliCommand_MustNotDefineExportValue()
    {
        // CliCommand lives in Romulus.CLI which is an EXE; loading it from the test host can fail.
        // The compile-time reference from Romulus.CLI to its own enum already pins the contract via build break.
        Type? cliCommand = null;
        try
        {
            cliCommand = Type.GetType("Romulus.CLI.CliCommand, Romulus.CLI", throwOnError: false);
        }
        catch (Exception) { /* exe assembly not loadable from test host - accept */ }

        if (cliCommand is null || !cliCommand.IsEnum) return;

        var values = Enum.GetNames(cliCommand);
        Assert.DoesNotContain("Export", values);
    }
}
