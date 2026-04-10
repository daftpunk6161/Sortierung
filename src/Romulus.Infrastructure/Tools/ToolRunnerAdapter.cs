using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const string DefaultConversionToolsRoot = @"C:\tools\conversion";

    private readonly string? _toolHashesPath;
    private readonly bool _allowInsecureHashBypass;
    private readonly int _timeoutMinutes;
    private Dictionary<string, string>? _toolHashes;
    private readonly object _toolHashLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Hash, DateTime LastWriteUtc, long Length)> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="toolHashesPath">Path to data/tool-hashes.json for SHA256 verification.</param>
    /// <param name="allowInsecureHashBypass">Skip hash check (NOT recommended for production).</param>
    public ToolRunnerAdapter(string? toolHashesPath = null, bool allowInsecureHashBypass = false, int timeoutMinutes = 30, Action<string>? log = null)
    {
        _toolHashesPath = toolHashesPath;
        _allowInsecureHashBypass = allowInsecureHashBypass;
        _timeoutMinutes = timeoutMinutes > 0 ? timeoutMinutes : 30;
        _log = log;
    }

    private readonly Action<string>? _log;

    public string? FindTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        var name = toolName.ToLowerInvariant();

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
            "flips" => new[]
            {
                Path.Combine(localAppData, "Romulus", "tools", "flips.exe"),
                Path.Combine(programFiles, "flips", "flips.exe"),
                Path.Combine(programFilesX86, "flips", "flips.exe"),
                Path.Combine(programFiles, "flips.exe"),
            },
            "xdelta3" => new[]
            {
                Path.Combine(localAppData, "Romulus", "tools", "xdelta3.exe"),
                Path.Combine(programFiles, "xdelta3", "xdelta3.exe"),
                Path.Combine(programFilesX86, "xdelta3", "xdelta3.exe"),
                Path.Combine(programFiles, "xdelta3.exe"),
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
            if (File.Exists(c))
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
            "flips" => new[]
            {
                Path.Combine(root, "flips.exe"),
                Path.Combine(root, "flips", "flips.exe")
            },
            "xdelta3" => new[]
            {
                Path.Combine(root, "xdelta3.exe"),
                Path.Combine(root, "xdelta3", "xdelta3.exe")
            },
            _ => Array.Empty<string>()
        };
    }

    private static string ResolveConversionToolsRoot(out bool hasExplicitOverride)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(ConversionToolsRootOverrideEnvVar);
        hasExplicitOverride = !string.IsNullOrWhiteSpace(overrideRoot);
        return hasExplicitOverride ? overrideRoot!.Trim() : DefaultConversionToolsRoot;
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

        return RunProcess(filePath, arguments, label, timeout, cancellationToken);
    }

    public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
    {
        if (!File.Exists(sevenZipPath))
            return new ToolResult(-1, "7z: executable not found", false);

        if (!VerifyToolHash(sevenZipPath, requirement: null))
            return new ToolResult(-1, "7z: hash verification failed", false);

        return RunProcess(sevenZipPath, arguments, "7z", timeout: null, cancellationToken: CancellationToken.None);
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

            process = Process.Start(psi);
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
                stderr = ReadToEndWithByteBudget(process.StandardError, MaxToolOutputBytes, out _);
            });
            stdoutTask = Task.Run(() =>
            {
                stdout = ReadToEndWithByteBudget(process.StandardOutput, MaxToolOutputBytes, out _);
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

            process.WaitForExit();

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
    {
        ArgumentNullException.ThrowIfNull(reader);

        var buffer = new char[4096];
        var builder = new StringBuilder();
        var writtenBytes = 0;
        wasTruncated = false;

        while (true)
        {
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;

            if (writtenBytes >= maxBytes)
            {
                wasTruncated = true;
                continue;
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
        builder.Append("Invocation: ").Append(invocation);

        var output = CombineToolOutput(stdout, stderr);
        if (!string.IsNullOrWhiteSpace(output))
        {
            builder.AppendLine();
            builder.Append("Tool output:").AppendLine();
            builder.Append(TruncateForDiagnostics(output, 4096));
        }

        return builder.ToString();
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

        // Issue #11: Reject PLACEHOLDER hashes — these are not real SHA256 checksums
        if (expectedHash.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            _log?.Invoke($"[SECURITY] PLACEHOLDER-Hash fuer {fileName} — ersetze durch echten SHA256-Hash in data/tool-hashes.json");
            return false;
        }

        // PERF-02: Cache tool hash with LastWriteTime + FileLength check (Issue #22)
        var fullPath = Path.GetFullPath(toolPath);
        var fileInfo = new FileInfo(fullPath);
        var lastWrite = fileInfo.LastWriteTimeUtc;
        var fileLength = fileInfo.Length;
        if (_hashCache.TryGetValue(fullPath, out var cached)
            && cached.LastWriteUtc == lastWrite
            && cached.Length == fileLength)
        {
            return ToolInvokerSupport.FixedTimeHashEquals(cached.Hash, expectedHash);
        }

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(toolPath);
        var hashBytes = sha256.ComputeHash(stream);
        var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        _hashCache[fullPath] = (actualHash, lastWrite, fileLength);

        return ToolInvokerSupport.FixedTimeHashEquals(actualHash, expectedHash);
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
                        var value = prop.Value.GetString();
                        if (value is not null)
                            hashes[prop.Name.ToLowerInvariant()] = value;
                    }
                }

                _toolHashes = hashes;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
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
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
