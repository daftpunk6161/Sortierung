using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Deterministic resolver for multiple DAT matches of the same ROM identity.
/// Ranking order is intentionally fixed: preferred source, match strength,
/// then lexicographic candidate identity.
/// </summary>
public sealed class MultiDatConflictResolver
{
    public MultiDatResolution Resolve(
        IReadOnlyList<DatMatch>? matches,
        MultiDatResolutionContext? context = null)
    {
        if (matches is null || matches.Count == 0)
            return MultiDatResolution.NoMatch;

        var effectiveContext = context ?? MultiDatResolutionContext.Empty;
        var distinct = matches
            .Where(static match => !string.IsNullOrWhiteSpace(match.ConsoleKey))
            .Distinct()
            .ToArray();

        if (distinct.Length == 0)
            return MultiDatResolution.NoMatch;

        var ranked = distinct
            .Select(match => Rank(match, effectiveContext))
            .OrderBy(static candidate => candidate.PreferredSourceRank)
            .ThenByDescending(static candidate => candidate.MatchStrength)
            .ThenBy(static candidate => candidate.RankKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static candidate => candidate.RankKey, StringComparer.Ordinal)
            .ToArray();

        var selected = ranked[0].Match;
        var isConflict = distinct.Length > 1;
        var reason = BuildReason(isConflict, ranked);

        return new MultiDatResolution(
            SelectedMatch: selected,
            Candidates: distinct
                .OrderBy(static match => match.ConsoleKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static match => match.SourceId ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(static match => match.GameName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static match => match.RomFileName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IsConflict: isConflict,
            Reason: reason,
            RankedCandidates: ranked);
    }

    private static MultiDatResolutionCandidate Rank(DatMatch match, MultiDatResolutionContext context)
    {
        var preferredSourceRank = ResolvePreferredSourceRank(match, context.PreferredSources);
        var matchStrength = ResolveMatchStrength(match, context);
        var rankKey = string.Join(
            '\u001f',
            match.ConsoleKey,
            match.SourceId ?? "",
            match.GameName,
            match.RomFileName ?? "",
            match.HashType);

        return new MultiDatResolutionCandidate(match, preferredSourceRank, matchStrength, rankKey);
    }

    private static int ResolvePreferredSourceRank(DatMatch match, IReadOnlyList<string> preferredSources)
    {
        if (preferredSources.Count == 0)
            return int.MaxValue;

        for (var i = 0; i < preferredSources.Count; i++)
        {
            var preferred = preferredSources[i];
            if (string.IsNullOrWhiteSpace(preferred))
                continue;

            if (EqualsToken(preferred, match.SourceId)
                || EqualsToken(preferred, match.ConsoleKey)
                || EqualsToken(preferred, match.HashType)
                || ContainsToken(match.SourceId, preferred))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int ResolveMatchStrength(DatMatch match, MultiDatResolutionContext context)
    {
        var strength = MatchKindStrength(context.MatchKind) + HashTypeStrength(match.HashType);

        if (!string.IsNullOrWhiteSpace(context.ExpectedConsoleKey)
            && string.Equals(context.ExpectedConsoleKey, match.ConsoleKey, StringComparison.OrdinalIgnoreCase))
        {
            strength += 1000;
        }

        if (context.ConsoleStrengths.TryGetValue(match.ConsoleKey, out var consoleStrength))
            strength += Math.Clamp(consoleStrength, 0, 100);

        if (!string.IsNullOrWhiteSpace(match.ParentGameName))
            strength -= 5;

        if (match.IsBios)
            strength -= 2;

        return strength;
    }

    private static int MatchKindStrength(MatchKind kind) => kind switch
    {
        MatchKind.ExactDatHash or MatchKind.ArchiveInnerExactDat or MatchKind.HeaderlessDatHash
            or MatchKind.ChdRawDatHash or MatchKind.ChdDataSha1DatHash => 10_000,
        MatchKind.CrossConsoleExactDatHash or MatchKind.CrossConsoleArchiveInnerExactDat
            or MatchKind.CrossConsoleHeaderlessDatHash or MatchKind.CrossConsoleChdRawDatHash
            or MatchKind.CrossConsoleChdDataSha1DatHash => 9_500,
        MatchKind.DatNameOnlyMatch => 7_000,
        _ => 0
    };

    private static int HashTypeStrength(string? hashType) => hashType?.Trim().ToUpperInvariant() switch
    {
        "SHA256" => 400,
        "SHA1" => 300,
        "MD5" => 200,
        "CRC32" or "CRC" => 100,
        _ => 0
    };

    private static string BuildReason(bool isConflict, IReadOnlyList<MultiDatResolutionCandidate> ranked)
    {
        if (!isConflict)
            return "single-match";

        var first = ranked[0];
        var second = ranked.Count > 1 ? ranked[1] : first;
        if (first.PreferredSourceRank != second.PreferredSourceRank)
            return "preferred-source";
        if (first.MatchStrength != second.MatchStrength)
            return "match-strength";
        return "lexicographic-tiebreak";
    }

    private static bool EqualsToken(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsToken(string? value, string? token)
        => !string.IsNullOrWhiteSpace(value)
           && !string.IsNullOrWhiteSpace(token)
           && value.Contains(token.Trim(), StringComparison.OrdinalIgnoreCase);
}
