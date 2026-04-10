namespace Romulus.Contracts;

/// <summary>
/// Exception for validation failures in run/watch/profile configuration materialization.
/// Carries a typed error code so API/CLI layers can map deterministically.
/// </summary>
public sealed class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException(ConfigurationErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ConfigurationValidationException(ConfigurationErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public ConfigurationErrorCode Code { get; }
}