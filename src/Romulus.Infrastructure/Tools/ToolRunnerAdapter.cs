using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;

namespace Romulus.Infrastructure.Tools;

/// <summary>
/// Production implementation of IToolRunner.
/// Port of Tools.ps1 — tool discovery, hash verification, process execution.
/// </summary>
public sealed class ToolRunnerAdapter : IToolRunner
{
    internal const string ConversionToolsRootOverrideEnvVar = "ROMULUS_CONVERSION_TOOLS_ROOT";
    internal const int MaxToolOutputBytes = 100 * 1024 * 1024;
    private static readonly string DefaultConversionToolsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Romulus",
        "tools",
        "conversion");

    // Suppress Windows modal error dialogs (e.g. "Nicht unterstützte 16 Bit-Anwendung")
    // when Process.Start encounters an invalid PE file.
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadErrorMode(uint dwNewMode, out uint lpOldMode);

    private readonly string? _toolHashesPath;
    private readonly bool _allowInsecureHashBypass;
    private readonly int _timeoutMinutes;
    private Dictionary<string, string>? _toolHashes;
    private readonly object _toolHashLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Hash, DateTime LastWriteUtc, long Length)> _hashCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ToolRunnerAdapter>? _logger;

    /// <param name="toolHashesPath">Path to data/tool-hashes.json for SHA256 verification.</param>
    /// <param name="allowInsecureHashBypass">Skip hash check (NOT recommended for production).</param>
    public ToolRunnerAdapter(
        string? toolHashesPath = null,
        bool allowInsecureHashBypass = false,
        int timeoutMinutes = 30,
        Action<string>? log = null,
        ILogger<ToolRunnerAdapter>? logger = null)
    {
        _toolHashesPath = toolHashesPath;
        _allowInsecureHashBypass = allowInsecureHashBypass;
        _timeoutMinutes = timeoutMinutes > 0 ? timeoutMinutes : 30;
        _log = log;
        _logger = logger;
    }

    private readonly Action<string>? _log;

    // Wave-2 F-11: thread-safe cache for FindTool results. The previous implementation
    // recomputed candidate paths and ran File.Exists on dozens of locations on every
    // invocation (FormatScore, ConversionPlanner, ToolsViewModel each call this in
    // tight loops). The cache stores the resolved path (or null) keyed by lowercased
    // tool name and self-invalidates when the cached executable no longer exists,
    // so disk-side changes (e.g. tool installation) eventually heal automatically.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _findToolCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Wave-2 F-11: clears the FindTool resolution cache. Call after the user
    /// installs/removes a conversion tool to force the next FindTool to re-scan.
    /// </summary>
    public void InvalidateToolCache() => _findToolCache.Clear();

    public string? FindTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        var name = toolName.ToLowerInvariant();

        // Cache fast-path: only trust the cached hit if the file still exists.
        // null entries are kept to avoid re-scanning for tools that aren't installed
        // (which is the more common case during typical sessions).
        if (_findToolCache.TryGetValue(name, out var cached))
        {
            if (cached is null) return null;
            if (File.Exists(cached) && IsSafeToolExecutablePath(cached))
                return cached;
            // Stale entry: a previously found tool has been moved/removed.
            _findToolCache.TryRemove(name, out _);
        }

        var resolved = ResolveToolUncached(name);
        _findToolCache[name] = resolved;
        return resolved;
    }

    private string? ResolveToolUncached(string name)
    {
        // 1. Search known safe locations first (never user-writable) to prevent
        //    tool-hijacking via PATH poisoning with a rogue executable.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Build candidate paths using environment-based folders (no hardcoded drive letters)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseCandidates = name switch
        {
            "chdman" => new[]
            {
                Path.Combine(programFiles, "MAME", "chdman.exe"),
                Path.Combine(programFilesX86, "MAME", "chdman.exe"),
                Path.Combine(localAppData, "MAME", "chdman.exe"),
                Path.Combine(programFiles, "chdman", "chdman.exe"),
                Path.Combine(programFilesX86, "chdman", "chdman.exe"),
                Path.Combine(programFiles, "chdman.exe"),
            },
            "dolphintool" => new[]
            {
                Path.Combine(programFiles, "Dolphin", "DolphinTool.exe"),
                Path.Combine(programFilesX86, "Dolphin", "DolphinTool.exe"),
                Path.Combine(programFiles, "DolphinTool.exe"),
            },
            "7z" => new[]
            {
                Path.Combine(programFiles, "7-Zip", "7z.exe"),
                Path.Combine(programFilesX86, "7-Zip", "7z.exe"),
            },
            "psxtract" => new[]
            {
                Path.Combine(localAppData, "Romulus", "tools", "psxtract.exe"),
                Path.Combine(programFiles, "psxtract", "psxtract.exe"),
                Path.Combine(programFilesX86, "psxtract", "psxtract.exe"),
                Path.Combine(programFiles, "psxtract.exe"),
            },
            "ciso" => new[]
            {
                Path.Combine(localAppData, "Romulus", "tools", "ciso.exe"),
                Path.Combine(programFiles, "ciso", "ciso.exe"),
                Path.Combine(programFilesX86, "ciso", "ciso.exe"),
                Path.Combine(programFiles, "ciso.exe"),
            },
            "unecm" => new[]
            {
                Path.Combine(localAppData, "Romulus", "tools", "unecm.exe"),
                Path.Combine(localAppData, "Romulus", "tools", "ecm", "unecm.exe"),
                Path.Combine(programFiles, "ecm", "unecm.exe"),
                Path.Combine(programFilesX86, "ecm", "unecm.exe"),
                Path.Combine(programFiles, "unecm.exe"),
            },
            "nkit" => new[]
            {
                Path.Combine(localAppData, "Romulus", "tools", "NKit", "NKitProcessingApp.exe"),
                Path.Combine(localAppData, "Romulus", "tools", "NKitProcessingApp.exe"),
                Path.Combine(programFiles, "NKit", "NKitProcessingApp.exe"),
                Path.Combine(programFilesX86, "NKit", "NKitProcessingApp.exe"),
                Path.Combine(programFiles, "NKitProcessingApp.exe"),
            },
            _ => Array.Empty<string>()
        };
        var programFilesConversionCandidates = GetProgramFilesConversionCandidates(name, programFiles, programFilesX86);
        var overrideCandidates = GetConversionToolsRootCandidates(name, out var hasExplicitOverride);
        var orderedCandidates = new List<string>(baseCandidates.Length + programFilesConversionCandidates.Length + overrideCandidates.Length);
        if (hasExplicitOverride)
        {
            orderedCandidates.AddRange(overrideCandidates);
            orderedCandidates.AddRange(baseCandidates);
            orderedCandidates.AddRange(programFilesConversionCandidates);
        }
        else
        {
            orderedCandidates.AddRange(baseCandidates);
            orderedCandidates.AddRange(programFilesConversionCandidates);
            orderedCandidates.AddRange(overrideCandidates);
        }
        var candidates = orderedCandidates.ToArray();

        foreach (var c in candidates)
        {
            if (File.Exists(c) && IsSafeToolExecutablePath(c))
                return c;
        }

        // 2. Fallback: check PATH (lower priority — user-writable dirs may be in PATH)
        var pathResult = FindOnPath(name + ".exe");
        if (pathResult is not null)
            return pathResult;

        return null;
    }

    private static string[] GetProgramFilesConversionCandidates(string toolName, string programFiles, string programFilesX86)
    {
        var roots = new[]
        {
            Path.Combine(programFiles, "conversion"),
            Path.Combine(programFiles, "tools", "conversion"),
            Path.Combine(programFilesX86, "conversion"),
            Path.Combine(programFilesX86, "tools", "conversion")
        };

        var candidates = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            candidates.AddRange(GetRootToolCandidates(toolName, root));
        }

        return candidates.ToArray();
    }

    private static string[] GetConversionToolsRootCandidates(string toolName, out bool hasExplicitOverride)
    {
        var root = ResolveConversionToolsRoot(out hasExplicitOverride);
        if (string.IsNullOrWhiteSpace(root))
            return Array.Empty<string>();

        return GetRootToolCandidates(toolName, root);
    }

    private static string[] GetRootToolCandidates(string toolName, string root)
    {
        return toolName switch
        {
            "chdman" => new[]
            {
                Path.Combine(root, "chdman.exe"),
                Path.Combine(root, "chdman", "chdman.exe"),
                Path.Combine(root, "mame", "chdman.exe")
            },
            "dolphintool" => new[]
            {
                Path.Combine(root, "DolphinTool.exe"),
                Path.Combine(root, "dolphintool", "DolphinTool.exe"),
                Path.Combine(root, "dolphin", "DolphinTool.exe")
            },
            "7z" => new[]
            {
                Path.Combine(root, "7z.exe"),
                Path.Combine(root, "7zip", "7z.exe"),
                Path.Combine(root, "7-Zip", "7z.exe")
            },
            "psxtract" => new[]
            {
                Path.Combine(root, "psxtract.exe"),
                Path.Combine(root, "psxtract", "psxtract.exe")
            },
            "ciso" => new[]
            {
                Path.Combine(root, "ciso.exe"),
                Path.Combine(root, "maxcso.exe"),
                Path.Combine(root, "ciso", "ciso.exe"),
                Path.Combine(root, "maxcso", "maxcso.exe")
            },
            "unecm" => new[]
            {
                Path.Combine(root, "unecm.exe"),
                Path.Combine(root, "ecm", "unecm.exe")
            },
            "nkit" => new[]
            {
                Path.Combine(root, "NKitProcessingApp.exe"),
                Path.Combine(root, "nkit", "NKitProcessingApp.exe")
            },
            _ => Array.Empty<string>()
        };
    }

    private static string ResolveConversionToolsRoot(out bool hasExplicitOverride)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(ConversionToolsRootOverrideEnvVar);
        hasExplicitOverride = !string.IsNullOrWhiteSpace(overrideRoot);
        if (!hasExplicitOverride)
            return DefaultConversionToolsRoot;

        var trimmed = overrideRoot!.Trim();
        return IsSafeConversionToolsRootOverride(trimmed) ? Path.GetFullPath(trimmed) : string.Empty;
    }

    internal static bool IsSafeConversionToolsRootOverride(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            if (!Path.IsPathFullyQualified(fullPath))
                return false;

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            if (root.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            var normalizedFull = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(normalizedFull, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Directory.Exists(fullPath))
                return false;

            var current = new DirectoryInfo(fullPath);
            while (current is not null)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;

                current = current.Parent;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsSafeToolExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Path.IsPathFullyQualified(fullPath))
                return false;

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            if (!File.Exists(fullPath))
                return false;

            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                return false;

            var directory = new FileInfo(fullPath).Directory;
            while (directory is not null)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return false;

                directory = directory.Parent;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
    {
        return InvokeProcess(filePath, arguments, requirement: null, errorLabel, timeout: null, cancellationToken: CancellationToken.None);
    }

    public ToolResult InvokeProcess(
        string filePath,
        string[] arguments,
        string? errorLabel,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return InvokeProcess(filePath, arguments, requirement: null, errorLabel, timeout, cancellationToken);
    }

    public ToolResult InvokeProcess(
        string filePath,
        string[] arguments,
        ToolRequirement? requirement,
        string? errorLabel,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var label = errorLabel ?? "Tool";

        if (!File.Exists(filePath))
            return new ToolResult(-1, $"{label}: executable not found at '{filePath}'", false);

        if (!VerifyToolHash(filePath, requirement))
            return new ToolResult(-1, $"{label}: hash verification failed for '{filePath}'", false);

        return RunProcessWithRetry(filePath, arguments, label, timeout, cancellationToken);
    }

    public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
    {
        if (!File.Exists(sevenZipPath))
            return new ToolResult(-1, "7z: executable not found", false);

        if (!VerifyToolHash(sevenZipPath, requirement: new ToolRequirement { ToolName = "7z" }))
            return new ToolResult(-1, "7z: hash verification failed", false);

        return RunProcessWithRetry(sevenZipPath, arguments, "7z", timeout: null, cancellationToken: CancellationToken.None);
    }

    private ToolResult RunProcessWithRetry(
        string exePath,
        string[] arguments,
        string label,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        ToolResult lastResult = new(-1, string.Empty, false);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            lastResult = RunProcess(exePath, arguments, label, timeout, cancellationToken);
            if (lastResult.Success)
                return lastResult;

            if (attempt >= maxAttempts || !IsRetryableProcessFailure(lastResult, cancellationToken))
                return lastResult;

            var delay = TimeSpan.FromMilliseconds(200 * attempt);
            _logger?.LogWarning("Tool process {Label} failed on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs} ms.", label, attempt, maxAttempts, delay.TotalMilliseconds);
            _log?.Invoke($"[WARN] {label}: retry {attempt}/{maxAttempts}");

            if (cancellationToken.WaitHandle.WaitOne(delay))
                return lastResult;
        }

        return lastResult;
    }

    private static bool IsRetryableProcessFailure(ToolResult result, CancellationToken cancellationToken)
    {
        if (result.Success || cancellationToken.IsCancellationRequested)
            return false;

        if (result.ExitCode != -1)
            return false;

        var output = result.Output ?? string.Empty;
        return output.Contains("ERROR_SHARING_VIOLATION", StringComparison.OrdinalIgnoreCase)
            || output.Contains("sharing violation", StringComparison.OrdinalIgnoreCase)
            || output.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Win32Exception: The process cannot access the file", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Win32Exception (32)", StringComparison.OrdinalIgnoreCase);
    }

    private ToolResult RunProcess(
        string exePath,
        string[] arguments,
        string label,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var invocation = BuildInvocationSummary(exePath, arguments);
        Process? process = null;
        Task? stderrTask = null;
        Task? stdoutTask = null;
        IDisposable? processTrackingLease = null;
        CancellationTokenRegistration cancellationRegistration = default;
        string? stderr = null;
        string? stdout = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            // Use ArgumentList instead of Arguments string to prevent argument injection
            // via filenames containing quotes, spaces, or shell metacharacters.
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            // Suppress Windows error dialogs for invalid executables (corrupt PE, 16-bit stubs).
            // SetThreadErrorMode is per-thread and safe for concurrent use.
            SetThreadErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX, out var previousErrorMode);
            try
            {
                process = Process.Start(psi);
            }
            finally
            {
                SetThreadErrorMode(previousErrorMode, out _);
            }
            if (process is null)
                return new ToolResult(
                    -1,
                    BuildFailureOutput(label, "failed to start process", invocation, null, null),
                    false);

            processTrackingLease = ExternalProcessGuard.Track(process, label, _log);
            cancellationRegistration = cancellationToken.Register(() =>
            {
                ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log);
            });

            // Read both stdout and stderr asynchronously to prevent pipe deadlock.
            // If either pipe fills its 4KB OS buffer while the parent blocks reading the other,
            // both processes deadlock indefinitely.
            stderrTask = Task.Run(() =>
            {
                stderr = ReadToEndWithByteBudget(
                    process.StandardError,
                    MaxToolOutputBytes,
                    out _,
                    cancellationToken,
                    () => ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log));
            });
            stdoutTask = Task.Run(() =>
            {
                stdout = ReadToEndWithByteBudget(
                    process.StandardOutput,
                    MaxToolOutputBytes,
                    out _,
                    cancellationToken,
                    () => ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log));
            });

            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(_timeoutMinutes);
            var deadlineUtc = DateTime.UtcNow + effectiveTimeout;
            var completed = false;

            while (!completed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                completed = Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromMilliseconds(200));
                if (completed)
                    break;

                if (DateTime.UtcNow >= deadlineUtc)
                    break;
            }

            if (!completed)
            {
                ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log);

                TryDrainProcessTasks(stdoutTask, stderrTask);

                if (DateTime.UtcNow >= deadlineUtc)
                {
                    return new ToolResult(
                        -1,
                        BuildFailureOutput(
                            label,
                            $"process timed out after {effectiveTimeout.TotalMinutes:0.##} minutes",
                            invocation,
                            stdout,
                            stderr),
                        false);
                }

                return new ToolResult(
                    -1,
                    BuildFailureOutput(label, "process cancelled", invocation, stdout, stderr),
                    false);
            }

            if (!process.WaitForExit(10_000))
            {
                ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log);
                return new ToolResult(
                    -1,
                    BuildFailureOutput(label, "process did not exit cleanly after output streams completed", invocation, stdout, stderr),
                    false);
            }

            var output = CombineToolOutput(stdout, stderr);
            if (cancellationToken.IsCancellationRequested)
            {
                return new ToolResult(
                    -1,
                    BuildFailureOutput(label, "process cancelled", invocation, stdout, stderr),
                    false);
            }

            if (process.ExitCode != 0)
            {
                return new ToolResult(
                    process.ExitCode,
                    BuildFailureOutput(label, $"process exited with code {process.ExitCode}", invocation, stdout, stderr),
                    false);
            }

            return new ToolResult(process.ExitCode, output, true);
        }
        catch (OperationCanceledException)
        {
            if (process is not null)
                ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log);
            TryDrainProcessTasks(stdoutTask, stderrTask);
            return new ToolResult(
                -1,
                BuildFailureOutput(label, "process cancelled", invocation, stdout, stderr),
                false);
        }
        catch (Exception ex)
        {
            if (process is not null)
                ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log);
            TryDrainProcessTasks(stdoutTask, stderrTask);
            return new ToolResult(
                -1,
                BuildFailureOutput(label, ex.Message, invocation, stdout, stderr),
                false);
        }
        finally
        {
            cancellationRegistration.Dispose();
            processTrackingLease?.Dispose();
            process?.Dispose();
        }
    }

    private static void TryDrainProcessTasks(Task? stdoutTask, Task? stderrTask)
    {
        if (stdoutTask is null || stderrTask is null)
            return;

        try
        {
            Task.WaitAll([stdoutTask, stderrTask], TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Best effort only.
        }
    }

    internal static string ReadToEndWithByteBudget(StreamReader reader, int maxBytes, out bool wasTruncated)
        => ReadToEndWithByteBudget(reader, maxBytes, out wasTruncated, CancellationToken.None);

    internal static string ReadToEndWithByteBudget(
        StreamReader reader,
        int maxBytes,
        out bool wasTruncated,
        CancellationToken cancellationToken)
        => ReadToEndWithByteBudget(reader, maxBytes, out wasTruncated, cancellationToken, onBudgetExceeded: null);

    internal static string ReadToEndWithByteBudget(
        StreamReader reader,
        int maxBytes,
        out bool wasTruncated,
        CancellationToken cancellationToken,
        Action? onBudgetExceeded)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var buffer = new char[4096];
        var builder = new StringBuilder();
        var writtenBytes = 0;
        wasTruncated = false;
        var signaledBudgetExceeded = false;

        void SignalBudgetExceeded()
        {
            if (signaledBudgetExceeded)
                return;

            signaledBudgetExceeded = true;
            onBudgetExceeded?.Invoke();
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;

            if (writtenBytes >= maxBytes)
            {
                wasTruncated = true;
                SignalBudgetExceeded();
                break;
            }

            var chunk = new string(buffer, 0, read);
            var chunkBytes = Encoding.UTF8.GetByteCount(chunk);
            if (writtenBytes + chunkBytes <= maxBytes)
            {
                builder.Append(chunk);
                writtenBytes += chunkBytes;
                continue;
            }

            var remaining = maxBytes - writtenBytes;
            if (remaining > 0)
            {
                var prefixLength = FindUtf8PrefixLength(chunk, remaining);
                if (prefixLength > 0)
                {
                    var prefix = chunk[..prefixLength];
                    builder.Append(prefix);
                    writtenBytes += Encoding.UTF8.GetByteCount(prefix);
                }
            }

            wasTruncated = true;
            SignalBudgetExceeded();
            break;
        }

        if (wasTruncated)
        {
            builder.AppendLine();
            builder.Append($"[output truncated after {maxBytes} bytes]");
        }

        return builder.ToString();
    }

    private static int FindUtf8PrefixLength(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
            return 0;

        var low = 0;
        var high = value.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var bytes = Encoding.UTF8.GetByteCount(value.AsSpan(0, mid));
            if (bytes <= maxBytes)
                low = mid;
            else
                high = mid - 1;
        }

        return low;
    }

    private static string BuildFailureOutput(
        string label,
        string reason,
        string invocation,
        string? stdout,
        string? stderr)
    {
        var builder = new StringBuilder();
        builder.Append(label).Append(": ").Append(reason);
        builder.AppendLine();
        builder.Append("Invocation: ").Append(RedactAbsolutePaths(invocation));

        var output = CombineToolOutput(stdout, stderr);
        if (!string.IsNullOrWhiteSpace(output))
        {
            builder.AppendLine();
            builder.Append("Tool output:").AppendLine();
            builder.Append(TruncateForDiagnostics(RedactAbsolutePaths(output), 4096));
        }

        return builder.ToString();
    }

    /// <summary>
    /// R5-015: Redact absolute paths from tool output to prevent path leakage in error messages and reports.
    /// Replaces Windows absolute paths (C:\...) with relative-only filename portions.
    /// </summary>
    private static string RedactAbsolutePaths(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Redact Windows absolute paths: drive letter + colon + backslash
        return System.Text.RegularExpressions.Regex.Replace(
            input,
            """[A-Za-z]:\\(?:[^\s"'<>|*?]+\\)*""",
            @"[path]\");
    }

    private static string BuildInvocationSummary(string exePath, string[] arguments)
    {
        var builder = new StringBuilder();
        builder.Append('"').Append(exePath).Append('"');

        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(RenderArgument(argument));
        }

        return builder.ToString();
    }

    private static string RenderArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return "\"\"";

        var requiresQuotes = false;
        foreach (var ch in argument)
        {
            if (char.IsWhiteSpace(ch) || ch == '"')
            {
                requiresQuotes = true;
                break;
            }
        }

        if (!requiresQuotes)
            return argument;

        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string CombineToolOutput(string? stdout, string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(stderr))
            return stdout!.Trim();

        if (string.IsNullOrWhiteSpace(stdout))
            return stderr.Trim();

        return $"{stdout.Trim()}{Environment.NewLine}{stderr.Trim()}";
    }

    private static string TruncateForDiagnostics(string value, int maxChars)
    {
        if (value.Length <= maxChars)
            return value;

        const int headLength = 2300;
        const int tailLength = 1400;
        var omitted = value.Length - headLength - tailLength;
        var head = value[..headLength];
        var tail = value[^tailLength..];
        return $"{head}{Environment.NewLine}...[truncated {omitted} chars]...{Environment.NewLine}{tail}";
    }

    private bool VerifyToolHash(string toolPath, ToolRequirement? requirement)
    {
        if (_allowInsecureHashBypass)
            return true;

        var fileName = Path.GetFileName(toolPath).ToLowerInvariant();
        string? expectedHash = null;

        if (_toolHashesPath is not null && File.Exists(_toolHashesPath))
        {
            EnsureToolHashesLoaded();
            if (_toolHashes is not null && _toolHashes.TryGetValue(fileName, out var configuredHash))
                expectedHash = configuredHash;
        }

        if (string.IsNullOrWhiteSpace(expectedHash) && !string.IsNullOrWhiteSpace(requirement?.ExpectedHash))
            expectedHash = requirement.ExpectedHash;

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            _log?.Invoke($"[SECURITY] Kein erwarteter Hash fuer {fileName} gefunden — blockiere Tool-Ausfuehrung");
            return false;
        }

        // Issue #11: Reject marker hashes — these are not real SHA256 checksums.
        if (expectedHash.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            || expectedHash.StartsWith("PENDING-VERIFY", StringComparison.OrdinalIgnoreCase))
        {
            _log?.Invoke($"[SECURITY] Marker-Hash fuer {fileName} ({expectedHash}) — ersetze durch echten SHA256-Hash in data/tool-hashes.json");
            return false;
        }

        var fullPath = Path.GetFullPath(toolPath);

        try
        {
            // PERF-02: Cache tool hash with LastWriteTime + FileLength check (Issue #22)
            if (_hashCache.TryGetValue(fullPath, out var cached))
            {
                var cachedLastWrite = File.GetLastWriteTimeUtc(fullPath);
                // F22: Use FileInfo.Length instead of opening a separate FileStream
                // just to read .Length — avoids a redundant file handle for the
                // cache-hit fast path. The subsequent main hash open below uses
                // FileShare.Read and re-validates Length+LastWrite under the
                // hash handle for TOCTOU detection (TH-04).
                var cachedLength = new FileInfo(fullPath).Length;

                if (cached.LastWriteUtc == cachedLastWrite && cached.Length == cachedLength)
                    return ToolInvokerSupport.FixedTimeHashEquals(cached.Hash, expectedHash);
            }

            // TH-04: Open the file first with FileShare.Read and hash that specific handle.
            // This reduces file-swap TOCTOU risk between metadata check and hash read.
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            var lengthBeforeHash = stream.Length;
            var lastWriteBeforeHash = File.GetLastWriteTimeUtc(fullPath);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            var actualHash = Convert.ToHexStringLower(hashBytes);

            var lastWriteAfterHash = File.GetLastWriteTimeUtc(fullPath);
            var lengthAfterHash = new FileInfo(fullPath).Length;
            if (lastWriteAfterHash != lastWriteBeforeHash || lengthAfterHash != lengthBeforeHash)
            {
                _log?.Invoke($"[SECURITY] Tool binary changed during hash verification: {fileName}");
                return false;
            }

            _hashCache[fullPath] = (actualHash, lastWriteAfterHash, lengthAfterHash);
            return ToolInvokerSupport.FixedTimeHashEquals(actualHash, expectedHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"[SECURITY] Tool hash verification failed for {fileName}: {ex.Message}");
            return false;
        }
    }

    private void EnsureToolHashesLoaded()
    {
        if (_toolHashes is not null || _toolHashesPath is null)
            return;

        lock (_toolHashLock)
        {
            if (_toolHashes is not null)
                return;

            try
            {
                var json = File.ReadAllText(_toolHashesPath);
                using var doc = JsonDocument.Parse(json);

                var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (doc.RootElement.TryGetProperty("Tools", out var toolsElement))
                {
                    foreach (var prop in toolsElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != JsonValueKind.String)
                        {
                            _log?.Invoke($"[SECURITY] Invalid non-string tool hash for {prop.Name}; entry ignored");
                            continue;
                        }

                        var value = prop.Value.GetString();
                        if (value is not null)
                            hashes[prop.Name.ToLowerInvariant()] = value;
                    }
                }

                _toolHashes = hashes;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
            {
                _toolHashes = new Dictionary<string, string>();
            }
        }
    }

    private static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var fullPath = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(fullPath) && IsSafeToolExecutablePath(fullPath))
                return fullPath;
        }

        return null;
    }
}
