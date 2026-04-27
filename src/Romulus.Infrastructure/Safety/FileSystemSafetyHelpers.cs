namespace Romulus.Infrastructure.Safety;

/// <summary>
/// Sicherheits-Helfer fuer Dateisystem-Operationen, die in mehreren Adaptern
/// noetig sind (Hashing, DAT, Conversion). Single Source of Truth fuer:
///   - Reparse-Point-sichere Verzeichnisaufzaehlung
///   - Verfuegbarer Tempspace-Check
///
/// Frueher dupliziert in <c>ArchiveHashService</c> und <c>DatRepositoryAdapter</c>;
/// konsolidiert per Deep Dive Audit (Safety / FileSystem / Security).
/// </summary>
internal static class FileSystemSafetyHelpers
{
    /// <summary>
    /// Aufzaehlung aller Verzeichnisse unter <paramref name="root"/>, ohne
    /// Reparse-Points zu folgen (Junction-/Symlink-Schutz). Reparse-Point-Verzeichnisse
    /// selbst werden geyielded (damit Aufrufer sie pruefen koennen), ihre Kinder aber
    /// nicht weiter besucht.
    /// </summary>
    /// <remarks>
    /// Sortierung: OrdinalIgnoreCase fuer Determinismus (Preview/Execute-Paritaet).
    /// Bei IO-Fehler endet die Aufzaehlung still (best-effort).
    /// </remarks>
    public static IEnumerable<string> EnumerateDirectoriesWithoutFollowingReparsePoints(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            foreach (var child in children)
            {
                yield return child;
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attrs & FileAttributes.ReparsePoint) == 0)
                    stack.Push(child);
            }
        }
    }

    /// <summary>
    /// Prueft, ob auf dem Volume von <paramref name="tempDir"/> mindestens
    /// <paramref name="requiredBytes"/> verfuegbar sind. Ein Wert &lt;= 0 wird
    /// als "kein Bedarf" interpretiert und liefert true.
    /// </summary>
    /// <remarks>
    /// Konservativ: bei IO-/Permission-/Argument-Fehlern => false, damit Aufrufer
    /// die Operation nicht spekulativ starten.
    /// </remarks>
    public static bool HasAvailableTempSpace(string tempDir, long requiredBytes)
    {
        if (requiredBytes <= 0)
            return true;

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(tempDir));
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace > requiredBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
