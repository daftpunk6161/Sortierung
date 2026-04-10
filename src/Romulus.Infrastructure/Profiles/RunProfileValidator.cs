using System.Text.RegularExpressions;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Safety;

namespace Romulus.Infrastructure.Profiles;

public static partial class RunProfileValidator
{
    private static readonly IReadOnlySet<string> AllowedHashTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SHA1",
        "SHA256",
        "MD5"
    };

    private static readonly IReadOnlySet<string> AllowedConvertFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "auto",
        "chd",
        "rvz",
        "zip",
        "7z"
    };

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileIdRegex();

    public static IReadOnlyList<string> ValidateDocument(RunProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<string>();

        if (document.Version != 1)
            errors.Add($"Unsupported profile version '{document.Version}'.");

        if (string.IsNullOrWhiteSpace(document.Id) || !ProfileIdRegex().IsMatch(document.Id))
            errors.Add("Profile id must be 1-64 chars from [A-Za-z0-9._-].");

        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Trim().Length > 120)
            errors.Add("Profile name must be between 1 and 120 characters.");

        if (document.Description?.Length > 512)
            errors.Add("Profile description must not exceed 512 characters.");

        errors.AddRange(ValidateSettings(document.Settings));
        return errors;
    }

    public static IReadOnlyList<string> ValidateSettings(RunProfileSettings settings)
        => ValidateSettingsDetailed(settings)
            .Select(static issue => issue.Message)
            .ToArray();

    internal static IReadOnlyList<ConfigurationValidationIssue> ValidateSettingsDetailed(RunProfileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<ConfigurationValidationIssue>();

        if (settings.PreferRegions is { Length: > 0 })
        {
            if (settings.PreferRegions.Length > RunConstants.MaxPreferRegions)
            {
                errors.Add(new ConfigurationValidationIssue(
                    ConfigurationErrorCode.InvalidRegion,
                    $"preferRegions may contain at most {RunConstants.MaxPreferRegions} items."));
            }

            foreach (var region in settings.PreferRegions)
            {
                if (string.IsNullOrWhiteSpace(region) || region.Length > 10 || region.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-')))
                {
                    errors.Add(new ConfigurationValidationIssue(
                        ConfigurationErrorCode.InvalidRegion,
                        $"Invalid region '{region}'."));
                }
            }
        }

        if (settings.Extensions is { Length: > 0 })
        {
            foreach (var extension in settings.Extensions)
            {
                if (string.IsNullOrWhiteSpace(extension))
                {
                    errors.Add(new ConfigurationValidationIssue(
                        ConfigurationErrorCode.InvalidExtension,
                        "extensions must not contain empty values."));
                    continue;
                }

                var normalized = extension.Trim();
                if (!normalized.StartsWith('.'))
                    normalized = "." + normalized;

                if (normalized.Length < 2 || normalized.Length > 20 || !normalized.Skip(1).All(char.IsLetterOrDigit))
                {
                    errors.Add(new ConfigurationValidationIssue(
                        ConfigurationErrorCode.InvalidExtension,
                        $"Invalid extension '{extension}'."));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.HashType) && !AllowedHashTypes.Contains(settings.HashType))
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.InvalidHashType,
                $"Invalid hashType '{settings.HashType}'."));
        }

        if (!string.IsNullOrWhiteSpace(settings.ConvertFormat) && !AllowedConvertFormats.Contains(settings.ConvertFormat))
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.InvalidConvertFormat,
                $"Invalid convertFormat '{settings.ConvertFormat}'."));
        }

        if (!string.IsNullOrWhiteSpace(settings.ConflictPolicy) && !RunConstants.ValidConflictPolicies.Contains(settings.ConflictPolicy))
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.InvalidConflictPolicy,
                $"Invalid conflictPolicy '{settings.ConflictPolicy}'."));
        }

        if (!string.IsNullOrWhiteSpace(settings.Mode) &&
            !string.Equals(settings.Mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.InvalidMode,
                $"Invalid mode '{settings.Mode}'."));
        }

        if (settings.EnableDatAudit == true && settings.EnableDat == false)
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.Unknown,
                "enableDatAudit requires enableDat=true."));
        }

        if (settings.EnableDatRename == true && settings.EnableDat == false)
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.Unknown,
                "enableDatRename requires enableDat=true."));
        }

        if (settings.OnlyGames != true && settings.KeepUnknownWhenOnlyGames == false)
        {
            errors.Add(new ConfigurationValidationIssue(
                ConfigurationErrorCode.Unknown,
                "keepUnknownWhenOnlyGames=false requires onlyGames=true."));
        }

        var datRootError = ValidateOptionalSafePathDetailed(settings.DatRoot, "datRoot");
        if (datRootError is not null)
            errors.Add(datRootError.Value);

        var trashRootError = ValidateOptionalSafePathDetailed(settings.TrashRoot, "trashRoot");
        if (trashRootError is not null)
            errors.Add(trashRootError.Value);

        return errors;
    }

    public static string? ValidateOptionalSafePath(string? path, string label)
        => ValidateOptionalSafePathDetailed(path, label)?.Message;

    internal static ConfigurationValidationIssue? ValidateOptionalSafePathDetailed(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim();
        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return new ConfigurationValidationIssue(
                ConfigurationErrorCode.UncPath,
                $"{label} must not be a UNC path.");
        }

        var normalized = SafetyValidator.NormalizePath(trimmed);
        if (normalized is null)
        {
            return new ConfigurationValidationIssue(
                ConfigurationErrorCode.InvalidPath,
                $"{label} is invalid.");
        }

        if (SafetyValidator.IsProtectedSystemPath(normalized))
        {
            return new ConfigurationValidationIssue(
                ConfigurationErrorCode.ProtectedSystemPath,
                $"{label} points to a protected system path.");
        }

        if (SafetyValidator.IsDriveRoot(normalized))
        {
            return new ConfigurationValidationIssue(
                ConfigurationErrorCode.DriveRoot,
                $"{label} must not point to a drive root.");
        }

        try
        {
            _ = SafetyValidator.EnsureSafeOutputPath(trimmed, allowUnc: false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("reparse-point", StringComparison.OrdinalIgnoreCase))
        {
            return new ConfigurationValidationIssue(
                ConfigurationErrorCode.ReparsePoint,
                $"{label} must not target a reparse point.");
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.InnerException is UnauthorizedAccessException
                ? ConfigurationErrorCode.AccessDenied
                : ex.Message.Contains("traversal", StringComparison.OrdinalIgnoreCase)
                    ? ConfigurationErrorCode.PathTraversal
                    : ConfigurationErrorCode.InvalidPath;

            return new ConfigurationValidationIssue(
                code,
                $"{label} is invalid: {ex.Message}");
        }

        return null;
    }

    internal readonly record struct ConfigurationValidationIssue(
        ConfigurationErrorCode Code,
        string Message);
}
