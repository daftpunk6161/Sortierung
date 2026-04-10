using System.IO;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class ReportPathResolverTests
{
    [Fact]
    public void Resolve_WhenActualMissingAndPlannedMissing_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ReportResolver_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var missingActual = Path.Combine(tempDir, "actual-missing.html");
            var missingPlanned = Path.Combine(tempDir, "planned-missing.html");

            var resolved = ReportPathResolver.Resolve(missingActual, missingPlanned);

            Assert.Null(resolved);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Resolve_WhenPlannedExists_ReturnsPlannedPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ReportResolver_" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var planned = Path.Combine(tempDir, "planned.html");
            File.WriteAllText(planned, "<html></html>");

            var resolved = ReportPathResolver.Resolve(null, planned);

            Assert.Equal(Path.GetFullPath(planned), resolved);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
