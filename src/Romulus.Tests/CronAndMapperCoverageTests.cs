using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Watch;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for CronScheduleEvaluator (TestCronMatch, CronFieldMatch)
/// and CollectionIndexCandidateMapper.NormalizeHashType.
/// </summary>
public sealed class CronAndMapperCoverageTests
{
    // ═══ CronFieldMatch ═══════════════════════════════════════════════

    [Fact]
    public void CronFieldMatch_Wildcard_AlwaysTrue()
    {
        Assert.True(CronScheduleEvaluator.CronFieldMatch("*", 0));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("*", 59));
    }

    [Fact]
    public void CronFieldMatch_ExactValue_Matches()
    {
        Assert.True(CronScheduleEvaluator.CronFieldMatch("5", 5));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("5", 6));
    }

    [Fact]
    public void CronFieldMatch_Range_MatchesWithin()
    {
        Assert.True(CronScheduleEvaluator.CronFieldMatch("1-5", 1));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("1-5", 3));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("1-5", 5));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("1-5", 0));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("1-5", 6));
    }

    [Fact]
    public void CronFieldMatch_WildcardStep_MatchesSteps()
    {
        // */15 matches 0, 15, 30, 45
        Assert.True(CronScheduleEvaluator.CronFieldMatch("*/15", 0));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("*/15", 15));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("*/15", 30));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("*/15", 45));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("*/15", 10));
    }

    [Fact]
    public void CronFieldMatch_RangeWithStep_Matches()
    {
        // 0-30/10 matches 0, 10, 20, 30
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0-30/10", 0));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0-30/10", 10));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0-30/10", 20));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0-30/10", 30));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("0-30/10", 5));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("0-30/10", 40));
    }

    [Fact]
    public void CronFieldMatch_NumericStep_MatchesFromStart()
    {
        // 5/10 matches 5, 15, 25, 35, 45, 55
        Assert.True(CronScheduleEvaluator.CronFieldMatch("5/10", 5));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("5/10", 15));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("5/10", 25));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("5/10", 0));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("5/10", 10));
    }

    [Fact]
    public void CronFieldMatch_List_MatchesAny()
    {
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0,15,30,45", 0));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0,15,30,45", 15));
        Assert.True(CronScheduleEvaluator.CronFieldMatch("0,15,30,45", 45));
        Assert.False(CronScheduleEvaluator.CronFieldMatch("0,15,30,45", 10));
    }

    [Fact]
    public void CronFieldMatch_InvalidStep_NoMatch()
    {
        // Step of 0 is invalid
        Assert.False(CronScheduleEvaluator.CronFieldMatch("*/0", 5));
    }

    [Fact]
    public void CronFieldMatch_NonNumeric_NoMatch()
    {
        Assert.False(CronScheduleEvaluator.CronFieldMatch("abc", 5));
    }

    // ═══ TestCronMatch ═══════════════════════════════════════════════

    [Fact]
    public void TestCronMatch_Null_ReturnsFalse()
    {
        Assert.False(CronScheduleEvaluator.TestCronMatch(null!, DateTime.UtcNow));
    }

    [Fact]
    public void TestCronMatch_Empty_ReturnsFalse()
    {
        Assert.False(CronScheduleEvaluator.TestCronMatch("", DateTime.UtcNow));
    }

    [Fact]
    public void TestCronMatch_TooFewFields_ReturnsFalse()
    {
        Assert.False(CronScheduleEvaluator.TestCronMatch("* * *", DateTime.UtcNow));
    }

    [Fact]
    public void TestCronMatch_TooManyFields_ReturnsFalse()
    {
        Assert.False(CronScheduleEvaluator.TestCronMatch("* * * * * *", DateTime.UtcNow));
    }

    [Fact]
    public void TestCronMatch_AllWildcards_AlwaysTrue()
    {
        Assert.True(CronScheduleEvaluator.TestCronMatch("* * * * *", new DateTime(2025, 6, 15, 14, 30, 0)));
    }

    [Fact]
    public void TestCronMatch_SpecificMinute_Matches()
    {
        var dt = new DateTime(2025, 6, 15, 14, 30, 0);
        Assert.True(CronScheduleEvaluator.TestCronMatch("30 * * * *", dt));
        Assert.False(CronScheduleEvaluator.TestCronMatch("15 * * * *", dt));
    }

    [Fact]
    public void TestCronMatch_SpecificHour_Matches()
    {
        var dt = new DateTime(2025, 6, 15, 14, 0, 0);
        Assert.True(CronScheduleEvaluator.TestCronMatch("0 14 * * *", dt));
        Assert.False(CronScheduleEvaluator.TestCronMatch("0 10 * * *", dt));
    }

    [Fact]
    public void TestCronMatch_SpecificDayOfMonth_Matches()
    {
        var dt = new DateTime(2025, 6, 15, 0, 0, 0);
        Assert.True(CronScheduleEvaluator.TestCronMatch("0 0 15 * *", dt));
        Assert.False(CronScheduleEvaluator.TestCronMatch("0 0 1 * *", dt));
    }

    [Fact]
    public void TestCronMatch_SpecificMonth_Matches()
    {
        var dt = new DateTime(2025, 6, 15, 0, 0, 0);
        Assert.True(CronScheduleEvaluator.TestCronMatch("0 0 * 6 *", dt));
        Assert.False(CronScheduleEvaluator.TestCronMatch("0 0 * 1 *", dt));
    }

    [Fact]
    public void TestCronMatch_SpecificDayOfWeek_Matches()
    {
        // 2025-06-15 is Sunday = DayOfWeek.Sunday = 0
        var dt = new DateTime(2025, 6, 15, 0, 0, 0);
        Assert.True(CronScheduleEvaluator.TestCronMatch("0 0 * * 0", dt));
        Assert.False(CronScheduleEvaluator.TestCronMatch("0 0 * * 1", dt));
    }

    [Fact]
    public void TestCronMatch_ComplexExpression()
    {
        // Every 15 minutes, 9-17, weekdays (Mon-Fri = 1-5)
        var mondayAt9 = new DateTime(2025, 6, 16, 9, 0, 0); // Monday
        Assert.True(CronScheduleEvaluator.TestCronMatch("*/15 9-17 * * 1-5", mondayAt9));

        var mondayAt9_10 = new DateTime(2025, 6, 16, 9, 10, 0);
        Assert.False(CronScheduleEvaluator.TestCronMatch("*/15 9-17 * * 1-5", mondayAt9_10));

        var sundayAt9 = new DateTime(2025, 6, 15, 9, 0, 0); // Sunday
        Assert.False(CronScheduleEvaluator.TestCronMatch("*/15 9-17 * * 1-5", sundayAt9));
    }

    // ═══ NormalizeHashType ═══════════════════════════════════════════

    [Fact]
    public void NormalizeHashType_Null_ReturnsSHA1()
    {
        Assert.Equal("SHA1", CollectionIndexCandidateMapper.NormalizeHashType(null!));
    }

    [Fact]
    public void NormalizeHashType_Empty_ReturnsSHA1()
    {
        Assert.Equal("SHA1", CollectionIndexCandidateMapper.NormalizeHashType(""));
    }

    [Fact]
    public void NormalizeHashType_Whitespace_ReturnsSHA1()
    {
        Assert.Equal("SHA1", CollectionIndexCandidateMapper.NormalizeHashType("   "));
    }

    [Fact]
    public void NormalizeHashType_CRC_ReturnsCRC32()
    {
        Assert.Equal("CRC32", CollectionIndexCandidateMapper.NormalizeHashType("CRC"));
    }

    [Fact]
    public void NormalizeHashType_CRCLowercase_ReturnsCRC32()
    {
        Assert.Equal("CRC32", CollectionIndexCandidateMapper.NormalizeHashType("crc"));
    }

    [Fact]
    public void NormalizeHashType_SHA1_Preserves()
    {
        Assert.Equal("SHA1", CollectionIndexCandidateMapper.NormalizeHashType("SHA1"));
    }

    [Fact]
    public void NormalizeHashType_SHA256_Preserves()
    {
        Assert.Equal("SHA256", CollectionIndexCandidateMapper.NormalizeHashType("sha256"));
    }

    [Fact]
    public void NormalizeHashType_MD5_UpperCased()
    {
        Assert.Equal("MD5", CollectionIndexCandidateMapper.NormalizeHashType("md5"));
    }

    [Fact]
    public void NormalizeHashType_CustomValue_UpperCased()
    {
        Assert.Equal("XXHASH", CollectionIndexCandidateMapper.NormalizeHashType("xxhash"));
    }

    [Fact]
    public void NormalizeHashType_Trimmed()
    {
        Assert.Equal("SHA256", CollectionIndexCandidateMapper.NormalizeHashType("  sha256  "));
    }
}
