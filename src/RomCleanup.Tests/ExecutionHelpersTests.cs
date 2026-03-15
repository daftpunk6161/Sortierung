using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

public sealed class ExecutionHelpersTests
{
    // =========================================================================
    //  GetDiscExtensions Tests
    // =========================================================================

    [Fact]
    public void GetDiscExtensions_ContainsCommonFormats()
    {
        var exts = ExecutionHelpers.GetDiscExtensions();
        Assert.Contains(".chd", exts);
        Assert.Contains(".iso", exts);
        Assert.Contains(".cue", exts);
        Assert.Contains(".rvz", exts);
        Assert.Contains(".pbp", exts);
    }

    [Fact]
    public void GetDiscExtensions_CaseInsensitive()
    {
        var exts = ExecutionHelpers.GetDiscExtensions();
        Assert.Contains(".CHD", exts);
        Assert.Contains(".Iso", exts);
    }

    // =========================================================================
    //  GetDefaultBlocklist Tests
    // =========================================================================

    [Fact]
    public void GetDefaultBlocklist_ContainsTrashFolder()
    {
        var bl = ExecutionHelpers.GetDefaultBlocklist();
        Assert.Contains("_TRASH_REGION_DEDUPE", bl);
        Assert.Contains("_FOLDER_DUPES", bl);
        Assert.Contains("_QUARANTINE", bl);
        Assert.Contains("PS3_DUPES", bl);
        Assert.Contains("_BACKUP", bl);
        Assert.Contains("_TRASH_JUNK", bl);
    }

    // =========================================================================
    //  IsBlocklisted Tests
    // =========================================================================

    [Theory]
    [InlineData(@"D:\roms\_TRASH_REGION_DEDUPE\game.zip", true)]
    [InlineData(@"D:\roms\_QUARANTINE\bad.rom", true)]
    [InlineData(@"D:\roms\NES\game.zip", false)]
    [InlineData(@"D:\roms\SNES\Zelda.sfc", false)]
    public void IsBlocklisted_DefaultBlocklist(string path, bool expected)
        => Assert.Equal(expected, ExecutionHelpers.IsBlocklisted(path));

    [Fact]
    public void IsBlocklisted_CustomBlocklist()
    {
        Assert.True(ExecutionHelpers.IsBlocklisted(
            @"D:\roms\CUSTOM_BAN\file.zip",
            ["CUSTOM_BAN"]));
    }

    [Theory]
    [InlineData(@"D:\roms\_TRASH_JUNK\game.zip", true)]
    [InlineData(@"D:\roms\_TRASH_JUNK\subfolder\game.zip", true)]
    [InlineData(@"D:\roms\NES\_TRASH_JUNK\game.zip", true)]
    [InlineData(@"D:\roms\NES\game.zip", false)]
    public void IsBlocklisted_TrashJunkFolder(string path, bool expected)
        => Assert.Equal(expected, ExecutionHelpers.IsBlocklisted(path));

    // =========================================================================
    //  BuildAuditFileName Tests
    // =========================================================================

    [Fact]
    public void BuildAuditFileName_EmptyRoots_ReturnsOriginal()
    {
        var result = ExecutionHelpers.BuildAuditFileName("audit.csv", []);
        Assert.Equal("audit.csv", result);
    }

    [Fact]
    public void BuildAuditFileName_SingleRoot_AppendsHash()
    {
        var result = ExecutionHelpers.BuildAuditFileName("audit.csv", [@"D:\roms"]);
        Assert.StartsWith("audit_", result);
        Assert.EndsWith(".csv", result);
        Assert.NotEqual("audit.csv", result);
    }

    [Fact]
    public void BuildAuditFileName_SameRoots_SameHash()
    {
        var r1 = ExecutionHelpers.BuildAuditFileName("audit.csv", [@"D:\roms"]);
        var r2 = ExecutionHelpers.BuildAuditFileName("audit.csv", [@"D:\roms"]);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void BuildAuditFileName_DifferentRoots_DifferentHash()
    {
        var r1 = ExecutionHelpers.BuildAuditFileName("audit.csv", [@"D:\roms"]);
        var r2 = ExecutionHelpers.BuildAuditFileName("audit.csv", [@"E:\games"]);
        Assert.NotEqual(r1, r2);
    }

    // =========================================================================
    //  GetConversionPreview Tests
    // =========================================================================

    [Fact]
    public void GetConversionPreview_EmptyRoots_EmptyResult()
    {
        var fs = new StubFs();
        var result = ExecutionHelpers.GetConversionPreview(fs, []);
        Assert.Equal(0, result.CandidateCount);
    }

    [Fact]
    public void GetConversionPreview_BlockedRoot_Excluded()
    {
        var fs = new StubFs();
        var result = ExecutionHelpers.GetConversionPreview(
            fs,
            roots: [@"D:\roms"],
            allowedRoots: [@"E:\allowed"]);
        Assert.Contains(@"D:\roms", result.BlockedRoots);
        Assert.Empty(result.AcceptedRoots);
    }

    [Fact]
    public void GetConversionPreview_NoAllowedFilter_AcceptsAll()
    {
        var fs = new StubFs(files: [@"D:\roms\game.iso", @"D:\roms\game2.chd"]);
        var result = ExecutionHelpers.GetConversionPreview(fs, [@"D:\roms"]);
        Assert.Single(result.AcceptedRoots);
        Assert.Equal(2, result.CandidateCount);
    }

    // Fakes
    private sealed class StubFs : IFileSystem
    {
        private readonly IReadOnlyList<string> _files;
        public StubFs(IReadOnlyList<string>? files = null) => _files = files ?? [];
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => _files;
        public string? MoveItemSafely(string src, string dest) => dest;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
