using Romulus.Contracts.Errors;
using Xunit;

namespace Romulus.Tests;

public class ErrorClassifierTests
{
    [Theory]
    [InlineData("SEC-001", ErrorKind.Critical)]
    [InlineData("SEC-PathTraversal", ErrorKind.Critical)]
    [InlineData("AUTH-InvalidKey", ErrorKind.Critical)]
    [InlineData("IO-LOCK-File", ErrorKind.Transient)]
    [InlineData("NET-Timeout", ErrorKind.Transient)]
    public void Classify_ErrorCode_TakesPrecedence(string code, ErrorKind expected)
    {
        var result = ErrorClassifier.Classify(errorCode: code);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Classify_TimeoutException_IsTransient()
    {
        Assert.Equal(ErrorKind.Transient,
            ErrorClassifier.Classify(new TimeoutException()));
    }

    [Fact]
    public void Classify_IOException_IsTransient()
    {
        Assert.Equal(ErrorKind.Transient,
            ErrorClassifier.Classify(new IOException()));
    }

    [Fact]
    public void Classify_UnauthorizedAccess_IsCritical()
    {
        Assert.Equal(ErrorKind.Critical,
            ErrorClassifier.Classify(new UnauthorizedAccessException()));
    }

    [Fact]
    public void Classify_OutOfMemory_IsCritical()
    {
        Assert.Equal(ErrorKind.Critical,
            ErrorClassifier.Classify(new OutOfMemoryException()));
    }

    [Fact]
    public void Classify_ArgumentException_IsRecoverable()
    {
        Assert.Equal(ErrorKind.Recoverable,
            ErrorClassifier.Classify(new ArgumentException("bad")));
    }

    [Fact]
    public void Classify_NullException_ReturnsDefault()
    {
        Assert.Equal(ErrorKind.Recoverable,
            ErrorClassifier.Classify(null));
        Assert.Equal(ErrorKind.Critical,
            ErrorClassifier.Classify(null, defaultKind: ErrorKind.Critical));
    }

    [Fact]
    public void FromException_CreatesCorrectError()
    {
        var ex = new IOException("disk full");
        var error = ErrorClassifier.FromException(ex, "FileOps", "IO-DiskFull");

        Assert.Equal("IO-DiskFull", error.Code);
        Assert.Equal("disk full", error.Message);
        Assert.Equal(ErrorKind.Transient, error.Kind);
        Assert.Equal("FileOps", error.Module);
        Assert.Same(ex, error.InnerException);
    }

    [Fact]
    public void FromException_NoCode_GeneratesFromType()
    {
        var ex = new ArgumentException("bad");
        var error = ErrorClassifier.FromException(ex, "Core");

        Assert.Equal("RUN-ArgumentException", error.Code);
    }
}
