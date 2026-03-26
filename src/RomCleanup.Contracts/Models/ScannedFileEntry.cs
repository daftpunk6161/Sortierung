namespace RomCleanup.Contracts.Models;

/// <summary>
/// Lightweight scan result entry produced during filesystem enumeration.
/// </summary>
public sealed record ScannedFileEntry(
    string Root,
    string Path,
    string Extension);
