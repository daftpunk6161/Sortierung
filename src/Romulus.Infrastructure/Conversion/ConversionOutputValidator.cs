namespace Romulus.Infrastructure.Conversion;

internal static class ConversionOutputValidator
{
    public static bool TryValidateCreatedOutput(string targetPath, out string failureReason)
    {
        if (!File.Exists(targetPath))
        {
            failureReason = "output-not-created";
            return false;
        }

        try
        {
            if (new FileInfo(targetPath).Length <= 0)
            {
                failureReason = "output-empty";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failureReason = "output-unreadable";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }
}
