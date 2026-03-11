using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Tools;

/// <summary>
/// Production implementation of IToolRunner.
/// Port of Tools.ps1 — tool discovery, hash verification, process execution.
/// </summary>
public sealed class ToolRunnerAdapter : IToolRunner
{
    private readonly string? _toolHashesPath;
    private readonly bool _allowInsecureHashBypass;
    private Dictionary<string, string>? _toolHashes;

    /// <param name="toolHashesPath">Path to data/tool-hashes.json for SHA256 verification.</param>
    /// <param name="allowInsecureHashBypass">Skip hash check (NOT recommended for production).</param>
    public ToolRunnerAdapter(string? toolHashesPath = null, bool allowInsecureHashBypass = false)
    {
        _toolHashesPath = toolHashesPath;
        _allowInsecureHashBypass = allowInsecureHashBypass;
    }

    public string? FindTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        var name = toolName.ToLowerInvariant();

        // 1. Check PATH via where.exe
        var pathResult = FindOnPath(name + ".exe");
        if (pathResult is not null)
            return pathResult;

        // 2. Search known safe locations (never user-writable)
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = name switch
        {
            "chdman" => new[]
            {
                Path.Combine(programFiles, "MAME", "chdman.exe"),
                Path.Combine(programFilesX86, "MAME", "chdman.exe"),
                @"C:\MAME\chdman.exe"
            },
            "dolphintool" => new[]
            {
                Path.Combine(programFiles, "Dolphin", "DolphinTool.exe"),
                Path.Combine(programFilesX86, "Dolphin", "DolphinTool.exe")
            },
            "7z" => new[]
            {
                Path.Combine(programFiles, "7-Zip", "7z.exe"),
                Path.Combine(programFilesX86, "7-Zip", "7z.exe")
            },
            "psxtract" => new[] { @"C:\tools\conversion\psxtract.exe" },
            "ciso" => new[] { @"C:\tools\conversion\ciso.exe" },
            _ => Array.Empty<string>()
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }

    public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
    {
        var label = errorLabel ?? "Tool";

        if (!File.Exists(filePath))
            return new ToolResult(-1, $"{label}: executable not found at '{filePath}'", false);

        if (!VerifyToolHash(filePath))
            return new ToolResult(-1, $"{label}: hash verification failed for '{filePath}'", false);

        return RunProcess(filePath, arguments, label);
    }

    public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
    {
        if (!File.Exists(sevenZipPath))
            return new ToolResult(-1, "7z: executable not found", false);

        if (!VerifyToolHash(sevenZipPath))
            return new ToolResult(-1, "7z: hash verification failed", false);

        return RunProcess(sevenZipPath, arguments, "7z");
    }

    private ToolResult RunProcess(string exePath, string[] arguments, string label)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new ToolResult(-1, $"{label}: failed to start process", false);

            // Read output streams (avoid deadlock by reading both)
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            var output = string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}".Trim();
            var success = process.ExitCode == 0;

            return new ToolResult(process.ExitCode, output, success);
        }
        catch (Exception ex)
        {
            return new ToolResult(-1, $"{label}: {ex.Message}", false);
        }
    }

    private bool VerifyToolHash(string toolPath)
    {
        if (_allowInsecureHashBypass)
            return true;

        if (_toolHashesPath is null || !File.Exists(_toolHashesPath))
            return _allowInsecureHashBypass; // false if no bypass

        EnsureToolHashesLoaded();

        var fileName = Path.GetFileName(toolPath).ToLowerInvariant();

        if (_toolHashes is null || !_toolHashes.TryGetValue(fileName, out var expectedHash))
            return _allowInsecureHashBypass;

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(toolPath);
        var hashBytes = sha256.ComputeHash(stream);
        var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureToolHashesLoaded()
    {
        if (_toolHashes is not null || _toolHashesPath is null)
            return;

        try
        {
            var json = File.ReadAllText(_toolHashesPath);
            using var doc = JsonDocument.Parse(json);

            _toolHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.TryGetProperty("Tools", out var toolsElement))
            {
                foreach (var prop in toolsElement.EnumerateObject())
                {
                    var value = prop.Value.GetString();
                    if (value is not null)
                        _toolHashes[prop.Name.ToLowerInvariant()] = value;
                }
            }
        }
        catch
        {
            _toolHashes = new Dictionary<string, string>();
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
