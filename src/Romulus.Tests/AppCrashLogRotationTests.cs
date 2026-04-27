using System.IO;
using System.Text;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-16: <c>App.RotateCrashLogIfTooLarge</c> must rotate <c>crash.log</c> to
/// <c>crash.log.1</c> when the file exceeds 1 MB and must be a no-op for small
/// or non-existent files. Rotation must be idempotent (overwrites existing .1).
/// </summary>
public sealed class AppCrashLogRotationTests : IDisposable
{
    private readonly string _tempDir;

    public AppCrashLogRotationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomulusCrashLogRotation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Rotate_NonExistentFile_DoesNothing()
    {
        var path = Path.Combine(_tempDir, "crash.log");
        Romulus.UI.Wpf.App.RotateCrashLogIfTooLarge(path);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void Rotate_SmallFile_DoesNotRotate()
    {
        var path = Path.Combine(_tempDir, "crash.log");
        File.WriteAllText(path, "small");
        Romulus.UI.Wpf.App.RotateCrashLogIfTooLarge(path);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void Rotate_OversizedFile_MovesToDotOne()
    {
        var path = Path.Combine(_tempDir, "crash.log");
        // 1.1 MB of bytes — exceeds the 1 MB threshold.
        var payload = new byte[(int)(Romulus.UI.Wpf.App.CrashLogMaxBytes + 1024)];
        File.WriteAllBytes(path, payload);

        Romulus.UI.Wpf.App.RotateCrashLogIfTooLarge(path);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".1"));
        Assert.Equal(payload.Length, new FileInfo(path + ".1").Length);
    }

    [Fact]
    public void Rotate_OversizedTwice_OverwritesExistingDotOne()
    {
        var path = Path.Combine(_tempDir, "crash.log");
        var firstPayload = new byte[(int)(Romulus.UI.Wpf.App.CrashLogMaxBytes + 1)];
        var secondPayload = Encoding.UTF8.GetBytes(new string('B', (int)Romulus.UI.Wpf.App.CrashLogMaxBytes + 1));

        File.WriteAllBytes(path, firstPayload);
        Romulus.UI.Wpf.App.RotateCrashLogIfTooLarge(path);

        File.WriteAllBytes(path, secondPayload);
        Romulus.UI.Wpf.App.RotateCrashLogIfTooLarge(path);

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".1"));
        Assert.Equal(secondPayload.Length, new FileInfo(path + ".1").Length);
    }
}
