namespace Romulus.Core.Conversion;

using Romulus.Contracts.Models;

/// <summary>
/// Evaluates runtime conditions for conversion capabilities.
/// </summary>
public sealed class ConversionConditionEvaluator
{
    private readonly Func<string, long> _fileSizeProvider;
    private readonly Func<string, bool>? _encryptedPbpDetector;

    /// <param name="fileSizeProvider">Returns file size in bytes for a given path.</param>
    /// <param name="encryptedPbpDetector">Returns true if a PBP file is encrypted. Optional; defaults to false when null.</param>
    public ConversionConditionEvaluator(Func<string, long> fileSizeProvider, Func<string, bool>? encryptedPbpDetector = null)
    {
        _fileSizeProvider = fileSizeProvider ?? throw new ArgumentNullException(nameof(fileSizeProvider));
        _encryptedPbpDetector = encryptedPbpDetector;
    }

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
            ConversionCondition.IsEncryptedPbp => IsEncryptedPbp(sourcePath, extension),
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

    private bool IsEncryptedPbp(string sourcePath, string extension)
    {
        if (!string.Equals(extension, ".pbp", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_encryptedPbpDetector is not null)
            return _encryptedPbpDetector(sourcePath);

        return false;
    }
}
