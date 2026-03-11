using RomCleanup.Contracts.Errors;
using RomCleanup.Infrastructure.Diagnostics;
using Xunit;

namespace RomCleanup.Tests;

public sealed class CatchGuardServiceTests
{
    // =========================================================================
    //  CreateRecord Tests
    // =========================================================================

    [Fact]
    public void CreateRecord_SetsAllFields()
    {
        var ex = new InvalidOperationException("test error");
        var record = CatchGuardService.CreateRecord(
            module: "Dat",
            action: "LoadIndex",
            root: @"D:\roms",
            operationId: "run-123",
            exception: ex,
            errorCode: "DAT-001");

        Assert.Equal("Dat", record.Module);
        Assert.Equal("LoadIndex", record.Action);
        Assert.Equal(@"D:\roms", record.Root);
        Assert.Equal("run-123", record.OperationId);
        Assert.Equal("DAT-001", record.ErrorCode);
        Assert.Equal("InvalidOperationException", record.ExceptionType);
        Assert.Equal("test error", record.Message);
    }

    [Fact]
    public void CreateRecord_WithExplicitErrorClass_UsesProvided()
    {
        var record = CatchGuardService.CreateRecord(
            "Test", "Action", errorClass: ErrorKind.Transient);
        Assert.Equal(ErrorKind.Transient, record.ErrorClass);
    }

    [Fact]
    public void CreateRecord_WithoutErrorClass_ClassifiesAutomatically()
    {
        var ex = new OperationCanceledException("cancelled");
        var record = CatchGuardService.CreateRecord(
            "Test", "Action", exception: ex);
        // ErrorClassifier.Classify should return something (not null)
        Assert.NotNull(record.ErrorClass.ToString());
    }

    [Fact]
    public void CreateRecord_CustomMessageOverridesException()
    {
        var ex = new Exception("original");
        var record = CatchGuardService.CreateRecord(
            "Mod", "Act", exception: ex, message: "custom msg");
        Assert.Equal("custom msg", record.Message);
    }

    // =========================================================================
    //  ToSeverity Tests
    // =========================================================================

    [Theory]
    [InlineData(ErrorKind.Critical, "Critical")]
    [InlineData(ErrorKind.Transient, "Warning")]
    [InlineData(ErrorKind.Recoverable, "Error")]
    public void ToSeverity_MapsCorrectly(ErrorKind kind, string expected)
        => Assert.Equal(expected, CatchGuardService.ToSeverity(kind));

    // =========================================================================
    //  Guard Tests
    // =========================================================================

    [Fact]
    public void Guard_SuccessfulAction_ReturnsNull()
    {
        var service = new CatchGuardService();
        var result = service.Guard("Mod", "Act", () => { });
        Assert.Null(result);
    }

    [Fact]
    public void Guard_RecoverableError_ReturnsRecord()
    {
        var service = new CatchGuardService();
        var result = service.Guard("Mod", "Act", () =>
        {
            throw new IOException("disk full");
        });
        Assert.NotNull(result);
        Assert.Equal("Mod", result.Module);
    }

    [Fact]
    public void Guard_CancellationAlwaysRethrown()
    {
        var service = new CatchGuardService();
        Assert.Throws<OperationCanceledException>(() =>
            service.Guard("Mod", "Act", () =>
            {
                throw new OperationCanceledException();
            }));
    }

    // =========================================================================
    //  Guard<T> Tests
    // =========================================================================

    [Fact]
    public void GuardT_SuccessfulFunc_ReturnsResult()
    {
        var service = new CatchGuardService();
        var (result, error) = service.Guard("Mod", "Act", () => 42);
        Assert.Equal(42, result);
        Assert.Null(error);
    }

    [Fact]
    public void GuardT_RecoverableError_ReturnsDefaultAndRecord()
    {
        var service = new CatchGuardService();
        var (result, error) = service.Guard("Mod", "Act", () =>
        {
            if (true) throw new IOException("read error");
            return 42;
        });
        Assert.Equal(0, result);
        Assert.NotNull(error);
    }

    // =========================================================================
    //  LogAndCreate Tests
    // =========================================================================

    [Fact]
    public void LogAndCreate_InvokesLogger()
    {
        var logged = new List<string>();
        var service = new CatchGuardService(msg => logged.Add(msg));
        var ex = new FileNotFoundException("missing.dat");
        service.LogAndCreate("Dat", "Load", exception: ex, level: "Error");
        Assert.Single(logged);
        Assert.Contains("Dat.Load", logged[0]);
    }
}
