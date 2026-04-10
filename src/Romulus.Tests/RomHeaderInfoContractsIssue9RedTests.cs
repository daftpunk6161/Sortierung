using System.Reflection;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-43): RomHeaderInfo must be owned by Contracts and no duplicate
/// model types should remain in Core/WPF namespaces.
/// </summary>
public sealed class RomHeaderInfoContractsIssue9RedTests
{
    [Fact]
    public void RomHeaderInfo_ShouldExistInContractsAssembly_Issue9A43()
    {
        // Arrange
        var contracts = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Romulus.Contracts", StringComparison.Ordinal));

        // Act
        var type = contracts?.GetType("Romulus.Contracts.Models.RomHeaderInfo", throwOnError: false);

        // Assert
        Assert.NotNull(contracts);
        Assert.NotNull(type);
    }

    [Fact]
    public void RomHeaderInfo_ShouldHaveSingleDefinitionAcrossLoadedAssemblies_Issue9A43()
    {
        // Act
        var candidates = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(GetLoadableTypes)
            .Where(t => string.Equals(t.Name, "RomHeaderInfo", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Where(n => n is not null)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Single(candidates);
        Assert.Equal("Romulus.Contracts.Models.RomHeaderInfo", candidates[0]);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
