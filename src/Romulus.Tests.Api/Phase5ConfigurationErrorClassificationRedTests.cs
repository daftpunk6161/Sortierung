using System.Reflection;
using Romulus.Api;
using Romulus.Contracts;
using Xunit;
using Romulus.Contracts.Errors;

namespace Romulus.Tests;

public sealed class Phase5ConfigurationErrorClassificationRedTests
{
    private static readonly Assembly ContractsAssembly = typeof(RunConstants).Assembly;
    private static readonly Assembly ApiAssembly = typeof(RunManager).Assembly;

    [Fact]
    public void Contracts_MustExpose_ConfigurationErrorCode_Enum_WithRequiredMembers()
    {
        var enumType = ContractsAssembly.GetType("Romulus.Contracts.ConfigurationErrorCode", throwOnError: false);

        Assert.NotNull(enumType);
        Assert.True(enumType!.IsEnum);

        var memberNames = Enum.GetNames(enumType);
        Assert.Contains("ProtectedSystemPath", memberNames);
        Assert.Contains("DriveRoot", memberNames);
        Assert.Contains("UncPath", memberNames);
        Assert.Contains("InvalidRegion", memberNames);
        Assert.Contains("InvalidConsole", memberNames);
        Assert.Contains("MissingDatRoot", memberNames);
        Assert.Contains("MissingTrashRoot", memberNames);
        Assert.Contains("InvalidPath", memberNames);
        Assert.Contains("PathTraversal", memberNames);
        Assert.Contains("ReparsePoint", memberNames);
        Assert.Contains("AccessDenied", memberNames);
        Assert.Contains("Unknown", memberNames);
    }

    [Fact]
    public void Contracts_MustExpose_ConfigurationValidationException_WithCodeProperty()
    {
        var exceptionType = ContractsAssembly.GetType("Romulus.Contracts.ConfigurationValidationException", throwOnError: false);

        Assert.NotNull(exceptionType);

        var codeProperty = exceptionType!.GetProperty("Code", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(codeProperty);
        Assert.Equal("ConfigurationErrorCode", codeProperty!.PropertyType.Name);
    }

    [Theory]
    [InlineData("RUN", "InvalidRegion", ApiErrorCodes.RunInvalidRegion)]
    [InlineData("WATCH", "InvalidRegion", "WATCH-INVALID-REGION")]
    [InlineData("RUN", "ProtectedSystemPath", "SEC-SYSTEM-DIRECTORY")]
    [InlineData("RUN", "DriveRoot", "SEC-DRIVE-ROOT")]
    [InlineData("RUN", "Unknown", ApiErrorCodes.RunInvalidConfig)]
    public void Program_MustMap_TypedConfigurationErrors_WithoutMessageParsing(
        string prefix,
        string codeName,
        string expectedApiCode)
    {
        var programType = ApiAssembly.GetType("Program", throwOnError: true)!;
        var exceptionType = ContractsAssembly.GetType("Romulus.Contracts.ConfigurationValidationException", throwOnError: true)!;
        var codeType = ContractsAssembly.GetType("Romulus.Contracts.ConfigurationErrorCode", throwOnError: true)!;

        var mapper = programType.GetMethod(
            "MapConfigurationError",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(mapper);

        var parameters = mapper!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(exceptionType, parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);

        var code = Enum.Parse(codeType, codeName);
        var exceptionInstance = Activator.CreateInstance(exceptionType, code, "synthetic validation message");
        Assert.NotNull(exceptionInstance);

        var mapped = mapper.Invoke(null, [exceptionInstance!, prefix]);
        Assert.NotNull(mapped);

        var item1 = mapped!.GetType().GetField("Item1")!.GetValue(mapped) as string;
        Assert.Equal(expectedApiCode, item1);
    }

    [Fact]
    public void Program_MustNotExpose_DuplicateStringBasedConfigurationMappers()
    {
        var programType = ApiAssembly.GetType("Program", throwOnError: true)!;

        var runMapper = programType.GetMethod(
            "MapRunConfigurationError",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var watchMapper = programType.GetMethod(
            "MapWatchConfigurationError",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Null(runMapper);
        Assert.Null(watchMapper);
    }
}
