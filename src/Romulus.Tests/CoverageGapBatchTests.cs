using Xunit;
using Romulus.Infrastructure.Audit;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Tests;

/// <summary>
/// Covers gaps in: AuditCsvParser (69.2%), AuditSecurityPaths (33.3%),
/// DecisionClassExtensions (81%), MatchKindExtensions (78%), RunOutcomeExtensions (77.7%),
/// RomulusSettingsValidator (56.5%), RunResultValidator (58.8%),
/// SafetyValidator statics (77%), FileSystemAdapter.IsWindowsReservedDeviceName.
/// </summary>
public class CoverageGapBatchTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  AuditCsvParser.ParseCsvLine — RFC 4180
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseCsvLine_SimpleFields()
    {
        var result = AuditCsvParser.ParseCsvLine("a,b,c");
        Assert.Equal(new[] { "a", "b", "c" }, result);
    }

    [Fact]
    public void ParseCsvLine_EmptyString_ReturnsSingleEmptyField()
    {
        var result = AuditCsvParser.ParseCsvLine("");
        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void ParseCsvLine_QuotedFieldWithComma()
    {
        var result = AuditCsvParser.ParseCsvLine("\"hello, world\",b");
        Assert.Equal(2, result.Length);
        Assert.Equal("hello, world", result[0]);
        Assert.Equal("b", result[1]);
    }

    [Fact]
    public void ParseCsvLine_EscapedDoubleQuotes()
    {
        var result = AuditCsvParser.ParseCsvLine("\"he said \"\"hi\"\"\",done");
        Assert.Equal(2, result.Length);
        Assert.Equal("he said \"hi\"", result[0]);
        Assert.Equal("done", result[1]);
    }

    [Fact]
    public void ParseCsvLine_QuotedFieldWithNewline()
    {
        var result = AuditCsvParser.ParseCsvLine("\"line1\nline2\",b");
        Assert.Equal("line1\nline2", result[0]);
    }

    [Fact]
    public void ParseCsvLine_OnlyCommas_ReturnsEmptyFields()
    {
        var result = AuditCsvParser.ParseCsvLine(",,");
        Assert.Equal(3, result.Length);
        Assert.All(result, f => Assert.Equal("", f));
    }

    [Fact]
    public void ParseCsvLine_SingleQuotedField()
    {
        var result = AuditCsvParser.ParseCsvLine("\"only\"");
        Assert.Single(result);
        Assert.Equal("only", result[0]);
    }

    [Fact]
    public void ParseCsvLine_MixedQuotedAndUnquoted()
    {
        var result = AuditCsvParser.ParseCsvLine("plain,\"quoted,field\",end");
        Assert.Equal(3, result.Length);
        Assert.Equal("plain", result[0]);
        Assert.Equal("quoted,field", result[1]);
        Assert.Equal("end", result[2]);
    }

    [Fact]
    public void ParseCsvLine_EmptyQuotedField()
    {
        var result = AuditCsvParser.ParseCsvLine("\"\",b");
        Assert.Equal(2, result.Length);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void ParseCsvLine_QuotedFieldConsecutiveDoubleQuotes()
    {
        // Four double-quotes inside quoted field = two literal double-quotes
        var result = AuditCsvParser.ParseCsvLine("\"a\"\"\"\"b\"");
        Assert.Single(result);
        Assert.Equal("a\"\"b", result[0]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AuditCsvParser.SanitizeCsvField — CSV Injection (OWASP)
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void SanitizeCsvField_NullOrEmpty_ReturnsSame(string? input, string? expected)
    {
        Assert.Equal(expected, AuditCsvParser.SanitizeCsvField(input!));
    }

    [Fact]
    public void SanitizeCsvField_SafeString_Unchanged()
    {
        Assert.Equal("hello", AuditCsvParser.SanitizeCsvField("hello"));
    }

    [Theory]
    [InlineData("=SUM(A1)", "\"=SUM(A1)\"")]
    [InlineData("+cmd", "\"+cmd\"")]
    [InlineData("@import", "\"@import\"")]
    public void SanitizeCsvField_FormulaPrefix_Quoted(string input, string expected)
    {
        Assert.Equal(expected, AuditCsvParser.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_DangerousDash_NonNumber_Quoted()
    {
        Assert.Equal("\"-cmd\"", AuditCsvParser.SanitizeCsvField("-cmd"));
    }

    [Theory]
    [InlineData("-42", "-42")]
    [InlineData("-3.14", "-3.14")]
    [InlineData("-0", "-0")]
    public void SanitizeCsvField_NegativeNumber_NotQuoted(string input, string expected)
    {
        Assert.Equal(expected, AuditCsvParser.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_ContainsComma_QuotedRfc4180()
    {
        Assert.Equal("\"hello,world\"", AuditCsvParser.SanitizeCsvField("hello,world"));
    }

    [Fact]
    public void SanitizeCsvField_ContainsDoubleQuote_EscapedAndQuoted()
    {
        Assert.Equal("\"he said \"\"hi\"\"\"", AuditCsvParser.SanitizeCsvField("he said \"hi\""));
    }

    [Fact]
    public void SanitizeCsvField_ControlChars_NormalizedAndQuoted()
    {
        // Starts with \t which is a dangerous control prefix
        var result = AuditCsvParser.SanitizeCsvField("\tdata");
        Assert.Equal("\" data\"", result);
    }

    [Fact]
    public void SanitizeCsvField_CarriageReturn_Stripped()
    {
        var result = AuditCsvParser.SanitizeCsvField("\rdata");
        Assert.Equal("\"data\"", result);
    }

    [Fact]
    public void SanitizeCsvField_Newline_Stripped()
    {
        var result = AuditCsvParser.SanitizeCsvField("\ndata");
        Assert.Equal("\"data\"", result);
    }

    [Fact]
    public void SanitizeCsvField_CustomDelimiter_Semicolon()
    {
        Assert.Equal("\"hello;world\"", AuditCsvParser.SanitizeCsvField("hello;world", ';'));
    }

    [Fact]
    public void SanitizeCsvField_CustomDelimiter_NoQuotingNeeded()
    {
        Assert.Equal("hello,world", AuditCsvParser.SanitizeCsvField("hello,world", ';'));
    }

    [Fact]
    public void SanitizeCsvField_DashSingleChar_Quoted()
    {
        // Just "-" alone has length < 2, so IsPlainNegativeNumber returns false
        Assert.Equal("\"-\"", AuditCsvParser.SanitizeCsvField("-"));
    }

    [Fact]
    public void SanitizeCsvField_DashWithLettersAndDigits_Quoted()
    {
        Assert.Equal("\"-1a\"", AuditCsvParser.SanitizeCsvField("-1a"));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AuditSecurityPaths
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetDefaultSigningKeyPath_ContainsSecurityAndKey()
    {
        var path = AuditSecurityPaths.GetDefaultSigningKeyPath();
        Assert.Contains("security", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("audit-signing.key", path);
    }

    [Fact]
    public void GetDefaultAuditDirectory_ContainsAudit()
    {
        var path = AuditSecurityPaths.GetDefaultAuditDirectory();
        Assert.Contains("audit", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDefaultReportDirectory_ContainsReports()
    {
        var path = AuditSecurityPaths.GetDefaultReportDirectory();
        Assert.Contains("reports", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditSecurityPaths_AllReturnAbsolutePaths()
    {
        Assert.True(Path.IsPathRooted(AuditSecurityPaths.GetDefaultSigningKeyPath()));
        Assert.True(Path.IsPathRooted(AuditSecurityPaths.GetDefaultAuditDirectory()));
        Assert.True(Path.IsPathRooted(AuditSecurityPaths.GetDefaultReportDirectory()));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DecisionClassExtensions — full enum coverage
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(DecisionClass.Sort, SortDecision.Sort)]
    [InlineData(DecisionClass.DatVerified, SortDecision.DatVerified)]
    [InlineData(DecisionClass.Review, SortDecision.Review)]
    [InlineData(DecisionClass.Blocked, SortDecision.Blocked)]
    [InlineData(DecisionClass.Unknown, SortDecision.Unknown)]
    public void ToSortDecision_AllKnownValues(DecisionClass decision, SortDecision expected)
    {
        Assert.Equal(expected, decision.ToSortDecision());
    }

    [Fact]
    public void ToSortDecision_UndefinedEnumValue_ReturnsUnknown()
    {
        var bogus = (DecisionClass)999;
        Assert.Equal(SortDecision.Unknown, bogus.ToSortDecision());
    }

    [Theory]
    [InlineData(SortDecision.Sort, DecisionClass.Sort)]
    [InlineData(SortDecision.DatVerified, DecisionClass.DatVerified)]
    [InlineData(SortDecision.Review, DecisionClass.Review)]
    [InlineData(SortDecision.Blocked, DecisionClass.Blocked)]
    [InlineData(SortDecision.Unknown, DecisionClass.Unknown)]
    public void ToDecisionClass_AllKnownValues(SortDecision decision, DecisionClass expected)
    {
        Assert.Equal(expected, decision.ToDecisionClass());
    }

    [Fact]
    public void ToDecisionClass_UndefinedEnumValue_ReturnsUnknown()
    {
        var bogus = (SortDecision)999;
        Assert.Equal(DecisionClass.Unknown, bogus.ToDecisionClass());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MatchKindExtensions.GetTier — all enum values
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(MatchKind.ExactDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.ArchiveInnerExactDat, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.HeaderlessDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.ChdRawDatHash, EvidenceTier.Tier0_ExactDat)]
    [InlineData(MatchKind.DiscHeaderSignature, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.CartridgeHeaderMagic, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.SerialNumberMatch, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.ChdMetadataTag, EvidenceTier.Tier1_Structural)]
    [InlineData(MatchKind.UniqueExtensionMatch, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.ArchiveContentExtension, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.DatNameOnlyMatch, EvidenceTier.Tier2_StrongHeuristic)]
    [InlineData(MatchKind.FolderNameMatch, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.FilenameKeywordMatch, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.AmbiguousExtensionSingle, EvidenceTier.Tier3_WeakHeuristic)]
    [InlineData(MatchKind.FilenameGuess, EvidenceTier.Tier3_WeakHeuristic)]
    public void GetTier_AllKnownMatchKinds(MatchKind kind, EvidenceTier expected)
    {
        Assert.Equal(expected, kind.GetTier());
    }

    [Fact]
    public void GetTier_None_ReturnsTier4Unknown()
    {
        Assert.Equal(EvidenceTier.Tier4_Unknown, MatchKind.None.GetTier());
    }

    [Fact]
    public void GetTier_UndefinedEnumValue_ReturnsTier4Unknown()
    {
        var bogus = (MatchKind)999;
        Assert.Equal(EvidenceTier.Tier4_Unknown, bogus.GetTier());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RunOutcomeExtensions — ToStatusString + ParseRunOutcome
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RunOutcome.Ok, "ok")]
    [InlineData(RunOutcome.CompletedWithErrors, "completed_with_errors")]
    [InlineData(RunOutcome.Blocked, "blocked")]
    [InlineData(RunOutcome.Cancelled, "cancelled")]
    [InlineData(RunOutcome.Failed, "failed")]
    public void ToStatusString_AllKnownOutcomes(RunOutcome outcome, string expected)
    {
        Assert.Equal(expected, outcome.ToStatusString());
    }

    [Fact]
    public void ToStatusString_UndefinedValue_ReturnsFailed()
    {
        var bogus = (RunOutcome)999;
        Assert.Equal("failed", bogus.ToStatusString());
    }

    [Theory]
    [InlineData("ok", RunOutcome.Ok)]
    [InlineData("completed_with_errors", RunOutcome.CompletedWithErrors)]
    [InlineData("blocked", RunOutcome.Blocked)]
    [InlineData("cancelled", RunOutcome.Cancelled)]
    [InlineData("failed", RunOutcome.Failed)]
    public void ParseRunOutcome_AllKnownStrings(string status, RunOutcome expected)
    {
        Assert.Equal(expected, RunOutcomeExtensions.ParseRunOutcome(status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("OK")]
    public void ParseRunOutcome_UnknownOrNull_ReturnsFailed(string? status)
    {
        Assert.Equal(RunOutcome.Failed, RunOutcomeExtensions.ParseRunOutcome(status));
    }

    [Fact]
    public void ParseRunOutcome_Roundtrips_AllValues()
    {
        foreach (var outcome in Enum.GetValues<RunOutcome>())
        {
            var str = outcome.ToStatusString();
            Assert.Equal(outcome, RunOutcomeExtensions.ParseRunOutcome(str));
        }
    }

    [Theory]
    [InlineData(RunOutcome.Ok, 0)]
    [InlineData(RunOutcome.Failed, 1)]
    [InlineData(RunOutcome.Cancelled, 2)]
    [InlineData(RunOutcome.Blocked, 3)]
    [InlineData(RunOutcome.CompletedWithErrors, 4)]
    public void ToExitCode_AllKnownOutcomes(RunOutcome outcome, int expected)
    {
        Assert.Equal(expected, outcome.ToExitCode());
    }

    [Fact]
    public void ToExitCode_UndefinedValue_ReturnsFailedCode()
    {
        var bogus = (RunOutcome)999;
        Assert.Equal(1, bogus.ToExitCode());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RomulusSettingsValidator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_ValidSettings_NoErrors()
    {
        var settings = new RomulusSettings();
        // defaults include ["EU", "US", "JP", "WORLD"] and SHA1
        var errors = RomulusSettingsValidator.Validate(settings);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmptyPreferredRegions_ReturnsError()
    {
        var settings = new RomulusSettings();
        settings.General.PreferredRegions.Clear();
        var errors = RomulusSettingsValidator.Validate(settings);
        Assert.Contains(errors, e => e.Contains("preferredRegions must contain at least one region"));
    }

    [Fact]
    public void Validate_WhitespaceRegion_ReturnsError()
    {
        var settings = new RomulusSettings();
        settings.General.PreferredRegions = [" "];
        var errors = RomulusSettingsValidator.Validate(settings);
        Assert.Contains(errors, e => e.Contains("must not contain empty values"));
    }

    [Fact]
    public void Validate_InvalidRegionChars_ReturnsError()
    {
        var settings = new RomulusSettings();
        settings.General.PreferredRegions = ["EUR@PE"];
        var errors = RomulusSettingsValidator.Validate(settings);
        Assert.Contains(errors, e => e.Contains("invalid value"));
    }

    [Fact]
    public void Validate_HyphenInRegion_Allowed()
    {
        var settings = new RomulusSettings();
        settings.General.PreferredRegions = ["EU-1"];
        var errors = RomulusSettingsValidator.Validate(settings);
        Assert.DoesNotContain(errors, e => e.Contains("invalid value"));
    }

    [Fact]
    public void Validate_InvalidHashType_ReturnsError()
    {
        var settings = new RomulusSettings();
        settings.Dat.HashType = "CRC32";
        var errors = RomulusSettingsValidator.Validate(settings);
        Assert.Contains(errors, e => e.Contains("hashType"));
    }

    [Fact]
    public void Validate_ValidHashTypes_NoHashError()
    {
        foreach (var hashType in new[] { "SHA1", "SHA256", "MD5" })
        {
            var settings = new RomulusSettings();
            settings.Dat.HashType = hashType;
            var errors = RomulusSettingsValidator.Validate(settings);
            Assert.DoesNotContain(errors, e => e.Contains("hashType"));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RunResultValidator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RunResultValidator_ValidResult_NoErrors()
    {
        var result = new RunResult();
        Assert.Empty(RunResultValidator.Validate(result));
    }

    [Fact]
    public void RunResultValidator_NegativeMoveCount_ReturnsError()
    {
        var result = new RunResult
        {
            MoveResult = new MovePhaseResult(-1, 0, 0)
        };
        var errors = RunResultValidator.Validate(result);
        Assert.Contains(errors, e => e.Contains("negative"));
    }

    [Fact]
    public void RunResultValidator_NegativeFailCount_ReturnsError()
    {
        var result = new RunResult
        {
            MoveResult = new MovePhaseResult(0, -1, 0)
        };
        var errors = RunResultValidator.Validate(result);
        Assert.Contains(errors, e => e.Contains("negative"));
    }

    [Fact]
    public void RunResultValidator_NegativeSkipCount_ReturnsError()
    {
        var result = new RunResult
        {
            MoveResult = new MovePhaseResult(0, 0, 0, SkipCount: -1)
        };
        var errors = RunResultValidator.Validate(result);
        Assert.Contains(errors, e => e.Contains("negative"));
    }

    [Fact]
    public void RunResultValidator_MovedSourcePathsMismatch_ReturnsError()
    {
        var paths = new HashSet<string> { "a.rom", "b.rom" };
        var result = new RunResult
        {
            MoveResult = new MovePhaseResult(3, 0, 0, MovedSourcePaths: paths) // count=3, paths=2
        };
        var errors = RunResultValidator.Validate(result);
        Assert.Contains(errors, e => e.Contains("MovedSourcePaths count must equal MoveCount"));
    }

    [Fact]
    public void RunResultValidator_DatSummaryMismatch_ReturnsError()
    {
        var result = new RunResult
        {
            DatAuditResult = new DatAuditResult(
                Entries: new List<DatAuditEntry>
                {
                    new("C:\\roms\\a.rom", "hash-a", DatAuditStatus.Have, "Game A", "game-a.rom", "SNES", 100)
                },
                HaveCount: 5,
                HaveWrongNameCount: 0,
                MissCount: 0,
                UnknownCount: 0,
                AmbiguousCount: 0)
        };
        var errors = RunResultValidator.Validate(result);
        Assert.Contains(errors, e => e.Contains("Entries.Count"));
    }

    [Fact]
    public void RunResultValidator_ConsistentDatAudit_NoErrors()
    {
        var result = new RunResult
        {
            DatAuditResult = new DatAuditResult(
                Entries: new List<DatAuditEntry>
                {
                    new("C:\\roms\\a.rom", "hash-a", DatAuditStatus.Have, "Game A", "game-a.rom", "SNES", 100)
                },
                HaveCount: 1,
                HaveWrongNameCount: 0,
                MissCount: 0,
                UnknownCount: 0,
                AmbiguousCount: 0)
        };
        Assert.Empty(RunResultValidator.Validate(result));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SafetyValidator — statics
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SafetyValidator_NormalizePath_NullOrWhiteSpace_ReturnsNull(string? input)
    {
        Assert.Null(SafetyValidator.NormalizePath(input));
    }

    [Fact]
    public void SafetyValidator_NormalizePath_ValidPath_ReturnsFullPath()
    {
        var result = SafetyValidator.NormalizePath(@"C:\Temp\test");
        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void SafetyValidator_NormalizePath_TrailingDotSegment_ReturnsNull()
    {
        // Segments ending in dot are blocked (Windows silently strips them)
        var result = SafetyValidator.NormalizePath(@"C:\Temp\evil.");
        Assert.Null(result);
    }

    [Fact]
    public void SafetyValidator_NormalizePath_TrailingSpaceInMiddleSegment_ReturnsNull()
    {
        // Middle segment with trailing space is blocked (Windows silently strips it)
        var result = SafetyValidator.NormalizePath(@"C:\evil \test");
        Assert.Null(result);
    }

    [Fact]
    public void SafetyValidator_IsProtectedSystemPath_WindowsDir_True()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(winDir))
        {
            Assert.True(SafetyValidator.IsProtectedSystemPath(Path.Combine(winDir, "System32", "test")));
        }
    }

    [Fact]
    public void SafetyValidator_IsProtectedSystemPath_TempDir_False()
    {
        // A temp directory should not be protected
        var temp = Path.Combine(Path.GetTempPath(), "romulus_test");
        Assert.False(SafetyValidator.IsProtectedSystemPath(temp));
    }

    [Fact]
    public void SafetyValidator_IsDriveRoot_DriveLetterWithBackslash()
    {
        Assert.True(SafetyValidator.IsDriveRoot(@"C:\"));
    }

    [Fact]
    public void SafetyValidator_IsDriveRoot_DriveLetterWithoutBackslash()
    {
        Assert.True(SafetyValidator.IsDriveRoot("C:"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Temp")]
    [InlineData("AB")]
    public void SafetyValidator_IsDriveRoot_NonDriveRoots_False(string? input)
    {
        Assert.False(SafetyValidator.IsDriveRoot(input!));
    }

    [Fact]
    public void SafetyValidator_EnsureSafeOutputPath_Empty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(""));
    }

    [Fact]
    public void SafetyValidator_EnsureSafeOutputPath_UncBlocked_WhenDisallowed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(@"\\server\share\file.csv", allowUnc: false));
    }

    [Fact]
    public void SafetyValidator_EnsureSafeOutputPath_DriveRoot_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(@"C:\"));
    }

    [Fact]
    public void SafetyValidator_EnsureSafeOutputPath_ProtectedPath_Throws()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(winDir))
        {
            Assert.Throws<InvalidOperationException>(() =>
                SafetyValidator.EnsureSafeOutputPath(Path.Combine(winDir, "test.csv")));
        }
    }

    [Fact]
    public void SafetyValidator_EnsureSafeOutputPath_ValidTemp_ReturnsNormalized()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "romulus_safe_test", "report.csv");
        var result = SafetyValidator.EnsureSafeOutputPath(tempPath);
        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void SafetyValidator_GetProfile_Known_ReturnsProfile()
    {
        var balanced = SafetyValidator.GetProfile("Balanced");
        Assert.NotNull(balanced);
    }

    [Fact]
    public void SafetyValidator_GetProfile_Unknown_ReturnsBalancedDefault()
    {
        var profile = SafetyValidator.GetProfile("NonExistent");
        var balanced = SafetyValidator.GetProfile("Balanced");
        Assert.Equal(balanced.Name, profile.Name);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FileSystemAdapter.IsWindowsReservedDeviceName
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CON", true)]
    [InlineData("PRN", true)]
    [InlineData("AUX", true)]
    [InlineData("NUL", true)]
    [InlineData("con", true)]
    [InlineData("Con", true)]
    [InlineData("COM0", true)]
    [InlineData("COM1", true)]
    [InlineData("COM9", true)]
    [InlineData("LPT0", true)]
    [InlineData("LPT1", true)]
    [InlineData("LPT9", true)]
    [InlineData("lpt3", true)]
    public void IsWindowsReservedDeviceName_Reserved_True(string name, bool expected)
    {
        Assert.Equal(expected, FileSystemAdapter.IsWindowsReservedDeviceName(name));
    }

    [Theory]
    [InlineData("NUL.txt", true)]  // With extension — still reserved
    [InlineData("CON.log", true)]
    [InlineData("COM1.dat", true)]
    public void IsWindowsReservedDeviceName_WithExtension_StillReserved(string name, bool expected)
    {
        Assert.Equal(expected, FileSystemAdapter.IsWindowsReservedDeviceName(name));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("hello", false)]
    [InlineData("CONX", false)]
    [InlineData("COM", false)]
    [InlineData("LPT", false)]
    [InlineData("COMA", false)]    // COM + non-digit
    [InlineData("COMX1", false)]   // 5 chars
    [InlineData("game.rom", false)]
    public void IsWindowsReservedDeviceName_NotReserved_False(string name, bool expected)
    {
        Assert.Equal(expected, FileSystemAdapter.IsWindowsReservedDeviceName(name));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FileSystemAdapter.NormalizePathNfc
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void NormalizePathNfc_ReturnsAbsolutePath()
    {
        var result = FileSystemAdapter.NormalizePathNfc(@"C:\Temp\test.rom");
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void NormalizePathNfc_IsFormC()
    {
        var result = FileSystemAdapter.NormalizePathNfc(@"C:\Temp\test.rom");
        Assert.True(result.IsNormalized(System.Text.NormalizationForm.FormC));
    }

    [Fact]
    public void NormalizePathNfc_ResolvesRelativeSegments()
    {
        var result = FileSystemAdapter.NormalizePathNfc(@"C:\Temp\..\Temp\test.rom");
        Assert.DoesNotContain("..", result);
    }
}
