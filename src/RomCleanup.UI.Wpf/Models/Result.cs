using System.Diagnostics.CodeAnalysis;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// GUI-042: Discriminated result type — replaces (string?, bool), null-returning methods,
/// and raw string error returns throughout the UI layer.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly UiError? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(UiError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T? Value => _value;
    public UiError? Error => _error;

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(UiError error) => new(error);
    public static Result<T> Fail(string code, string message, UiErrorSeverity severity = UiErrorSeverity.Error, string? fixHint = null)
        => new(new UiError(code, message, severity, fixHint));

    /// <summary>Match on success/failure with separate handlers.</summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<UiError, TResult> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    /// <summary>Transform the success value.</summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> map)
        => IsSuccess ? Result<TNew>.Ok(map(_value!)) : Result<TNew>.Fail(_error!);

    public static implicit operator Result<T>(T value) => Ok(value);

    public override string ToString()
        => IsSuccess ? $"Ok({_value})" : $"Fail({_error})";
}
