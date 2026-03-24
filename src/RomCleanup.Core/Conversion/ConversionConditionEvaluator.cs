namespace RomCleanup.Core.Conversion;

using RomCleanup.Contracts.Models;

/// <summary>
/// Evaluates runtime conditions for conversion capabilities.
/// </summary>
public sealed class ConversionConditionEvaluator(Func<string, long> fileSizeProvider)
{
    private readonly Func<string, long> _fileSizeProvider = fileSizeProvider ?? throw new ArgumentNullException(nameof(fileSizeProvider));

    /// <summary>
    /// Evaluates whether a condition holds for a given source path.
    /// </summary>
    public bool Evaluate(ConversionCondition condition, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        var fileName = Path.GetFileName(sourcePath);

        return condition switch
        {
            ConversionCondition.None => true,
            ConversionCondition.FileSizeLessThan700MB => SafeSize(sourcePath) is > 0 and < ConversionThresholds.CdImageThresholdBytes,
            ConversionCondition.FileSizeGreaterEqual700MB => SafeSize(sourcePath) >= ConversionThresholds.CdImageThresholdBytes,
            ConversionCondition.IsNKitSource => fileName.Contains(".nkit.", StringComparison.OrdinalIgnoreCase),
            ConversionCondition.IsWadFile => string.Equals(extension, ".wad", StringComparison.OrdinalIgnoreCase),
            ConversionCondition.IsCdiSource => string.Equals(extension, ".cdi", StringComparison.OrdinalIgnoreCase),
            ConversionCondition.IsEncryptedPbp => false,
            _ => false
        };
    }

    private long SafeSize(string sourcePath)
    {
        try
        {
            return _fileSizeProvider(sourcePath);
        }
        catch (IOException)
        {
            return -1;
        }
    }
}
