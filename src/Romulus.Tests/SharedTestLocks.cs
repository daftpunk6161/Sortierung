namespace Romulus.Tests;

/// <summary>
/// Shared locks for test infrastructure to prevent race conditions
/// between parallel xUnit test classes that mutate global state.
/// </summary>
internal static class SharedTestLocks
{
    /// <summary>
    /// Serialize access to Console.SetOut / Console.SetError across all test classes.
    /// Any test method that redirects Console must wrap its redirect+restore block with this lock.
    /// </summary>
    internal static readonly object ConsoleLock = new();
}
