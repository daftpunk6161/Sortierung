using System.Diagnostics;
using System.Text.RegularExpressions;
using Romulus.Core;
using Xunit;

namespace Romulus.Tests;

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

    // ── Timeout fallback invariant tests (F3) ───────────────────────

    [Fact]
    public void IsMatch_Timeout_ReturnsFalse_NotException()
    {
        var evilRegex = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        // Must return false, never throw
        var result = SafeRegex.IsMatch(evilRegex, evilInput);
        Assert.False(result);
    }

    [Fact]
    public void Match_Timeout_ReturnsEmptyMatch_NotException()
    {
        var evilRegex = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        var m = SafeRegex.Match(evilRegex, evilInput);
        Assert.False(m.Success);
        Assert.Same(System.Text.RegularExpressions.Match.Empty, m);
    }

    [Fact]
    public void Replace_Compiled_Timeout_ReturnsOriginalInput_NotException()
    {
        var evilRegex = new Regex(@"(a+)+", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        var result = SafeRegex.Replace(evilRegex, evilInput, "X");
        Assert.Equal(evilInput, result);
    }

    [Fact]
    public void Replace_AdHoc_Timeout_ReturnsOriginalInput_NotException()
    {
        var evilInput = new string('a', 30) + "!";

        var result = SafeRegex.Replace(evilInput, @"^(a+)+$", "X", RegexOptions.None, TimeSpan.FromTicks(1));
        Assert.Equal(evilInput, result);
    }

    [Fact]
    public void IsMatch_Timeout_WritesTraceOutput()
    {
        var evilRegex = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        var listener = new StringTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            SafeRegex.IsMatch(evilRegex, evilInput);
            Assert.Contains("[SafeRegex]", listener.Output);
            Assert.Contains("IsMatch timeout", listener.Output);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void Match_Timeout_WritesTraceOutput()
    {
        var evilRegex = new Regex(@"^(a+)+$", RegexOptions.None, TimeSpan.FromTicks(1));
        var evilInput = new string('a', 30) + "!";

        var listener = new StringTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            SafeRegex.Match(evilRegex, evilInput);
            Assert.Contains("[SafeRegex]", listener.Output);
            Assert.Contains("Match timeout", listener.Output);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    private sealed class StringTraceListener : TraceListener
    {
        private readonly System.Text.StringBuilder _sb = new();
        public string Output => _sb.ToString();
        public override void Write(string? message) => _sb.Append(message);
        public override void WriteLine(string? message) => _sb.AppendLine(message);
    }
}
