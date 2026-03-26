using System.Text.RegularExpressions;
using RomCleanup.Core;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Tests for the centralised SafeRegex utility, which provides regex-timeout-safe
/// wrappers used by GameKeyNormalizer, VersionScorer, and FileClassifier.
/// </summary>
public sealed class SafeRegexTests
{
    private static readonly Regex SimplePattern = new(@"hello", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    [Fact]
    public void IsMatch_MatchingInput_ReturnsTrue()
    {
        Assert.True(SafeRegex.IsMatch(SimplePattern, "say hello world"));
    }

    [Fact]
    public void IsMatch_NonMatchingInput_ReturnsFalse()
    {
        Assert.False(SafeRegex.IsMatch(SimplePattern, "goodbye"));
    }

    [Fact]
    public void IsMatch_TimeoutRegex_ReturnsFalse()
    {
        // Crafted pattern that causes catastrophic backtracking with a very short timeout
        var evilRegex = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        // Long input that triggers backtracking
        var evilInput = new string('a', 30) + "!";

        Assert.False(SafeRegex.IsMatch(evilRegex, evilInput));
    }

    [Fact]
    public void Match_MatchingInput_ReturnsMatch()
    {
        var m = SafeRegex.Match(SimplePattern, "say hello world");
        Assert.True(m.Success);
        Assert.Equal("hello", m.Value);
    }

    [Fact]
    public void Match_NonMatchingInput_ReturnsEmpty()
    {
        var m = SafeRegex.Match(SimplePattern, "goodbye");
        Assert.False(m.Success);
    }

    [Fact]
    public void Match_TimeoutRegex_ReturnsEmpty()
    {
        var evilRegex = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        var m = SafeRegex.Match(evilRegex, evilInput);
        Assert.False(m.Success);
    }

    [Fact]
    public void Replace_CompiledRegex_ReplacesMatch()
    {
        var result = SafeRegex.Replace(SimplePattern, "say hello world", "hi");
        Assert.Equal("say hi world", result);
    }

    [Fact]
    public void Replace_CompiledRegex_TimeoutReturnsOriginal()
    {
        var evilRegex = new Regex(@"(a+)+", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        var result = SafeRegex.Replace(evilRegex, evilInput, "X");
        Assert.Equal(evilInput, result);
    }

    [Fact]
    public void Replace_AdHocPattern_ReplacesMatch()
    {
        var result = SafeRegex.Replace("hello world", @"\s+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void Replace_AdHocPattern_TimeoutReturnsOriginal()
    {
        var evilInput = new string('a', 30) + "!";
        var result = SafeRegex.Replace(evilInput, @"^(a+)+$", "X", RegexOptions.None, TimeSpan.FromTicks(1));
        Assert.Equal(evilInput, result);
    }
}
