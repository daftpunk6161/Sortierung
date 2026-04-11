using System.Globalization;
using Romulus.Contracts;

namespace Romulus.Infrastructure.Orchestration;

internal static class RunProgressLocalization
{
    private static readonly IReadOnlyDictionary<string, string> De = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Preflight.Checking"] = $"{RunConstants.Phases.Preflight} Voraussetzungen pruefen...",
        ["Preflight.Blocked"] = $"{RunConstants.Phases.Preflight} Blockiert: {{0}}",
        ["Preflight.Ok"] = $"{RunConstants.Phases.Preflight} OK - {{0}} Root(s), {{1}} Extension(s), Modus: {{2}}",
        ["Preflight.Warning"] = $"{RunConstants.Phases.Preflight} Warnung: {{0}}",
        ["Pipeline.Error.Aborted"] = "[ERROR] Pipeline abgebrochen: {0}: {1}",

        ["Scan.Start"] = $"{RunConstants.Phases.Scan} Scanne {{0}} Root-Ordner...",
        ["Scan.Root"] = $"{RunConstants.Phases.Scan} Root: {{0}}",
        ["Scan.RootCollecting"] = $"{RunConstants.Phases.Scan} {{0}}: Erfassung laeuft...",
        ["Scan.RootFound"] = $"{RunConstants.Phases.Scan} {{0}}: {{1}} Dateien gefunden",
        ["Scan.ProgressProcessed"] = $"{RunConstants.Phases.Scan} {{0}}/{{1}} Dateien verarbeitet...",
        ["Filter.OnlyGames"] = $"{RunConstants.Phases.Filter} OnlyGames aktiv: {{0}} Nicht-Spiel-Dateien ausgeschlossen (KeepUnknown={{1}})",
        ["Scan.Completed"] = $"{RunConstants.Phases.Scan} Done: {{0}} files in {{1}}ms",
        ["Scan.HashLarge"] = $"{RunConstants.Phases.Scan} Hash: {{0}} ({{1:F0}} MB)...",
        ["Scan.IncompleteWarning"] = "WARNING: Scan unvollstaendig: {0} Verzeichnis(se) nicht zugreifbar",
        ["Scan.HighMemoryWarning"] = "WARNING: {0:N0} Dateien gescannt - hoher Speicherbedarf. Bitte weniger Roots verwenden.",

        ["Move.Start"] = $"{RunConstants.Phases.Move} Move {{0}} duplicate file(s) to trash...",
        ["Move.Completed"] = $"{RunConstants.Phases.Move} Done: {{0}} moved, {{1}} errors",
        ["Move.Abort.OutOfSpace"] = $"{RunConstants.Phases.Move} Abbruch: Zu wenig freier Speicher im Ziel ({{0}} Bytes verfuegbar, {{1}} Bytes benoetigt).",
        ["Move.Progress"] = $"{RunConstants.Phases.Move} Progress: {{0}}/{{1}} (moved={{2}}, skipped={{3}}, failed={{4}})",
        ["Move.SkipConflict"] = $"{RunConstants.Phases.Move} Skip (conflict): {{0}}",

        ["Sort.Start"] = $"{RunConstants.Phases.Sort} Sortiere Dateien nach Konsole...",
        ["Sort.Completed"] = $"{RunConstants.Phases.Sort} Console sort done",

        ["Report.Generate"] = $"{RunConstants.Phases.Report} Generiere HTML-Report...",
        ["Report.Created"] = $"{RunConstants.Phases.Report} Report erstellt: {{0}}",
        ["Report.Failed"] = $"{RunConstants.Phases.Report} Angeforderter Report konnte weder am Zielpfad noch im Fallback geschrieben werden.",

        ["Audit.WriteSidecar"] = "[Audit] Schreibe Audit-Sidecar...",
        ["Audit.SidecarWriteFailed"] = "[Audit] Sidecar write failed: {0}: {1}",
        ["Done.Pipeline"] = $"{RunConstants.Phases.Finished} Pipeline done in {{0}}ms - {{1}} files, {{2}} groups",

        ["Analyze.Skipped.CrossRoot"] = "[CrossRoot] Analyse ausgelassen: {0}",
        ["Analyze.Skipped.FolderDedupe"] = "[FolderDedupe] Analyse ausgelassen: {0}",
        ["Analyze.Skipped.Quarantine"] = "[Quarantine] Analyse ausgelassen: {0}",
        ["Analyze.Skipped.Hardlink"] = "[Hardlink] Analyse ausgelassen: {0}",
        ["Preview.FolderDedupeSummary"] = "[FolderDedupe] Preview: {0} Analyse-Ergebnis(se), PS3-Roots={1}, BaseName-Roots={2}",
        ["Preview.CrossRootSummary"] = "[CrossRoot] Preview: {0} root-uebergreifende Hash-Gruppen (Sample={1})",
        ["Preview.QuarantineSummary"] = "[Quarantine] Preview: {0} verdaechtige Datei(en) im Sample von {1} Kandidaten",
        ["Preview.HardlinkSummary"] = "[Hardlink] NTFS-Hardlink support: {0}/{1} Root(s)",
        ["CollectionIndex.DeltaLookupsDisabled"] = "[CollectionIndex] Delta lookups disabled for this run: {0}",

        ["Dedupe.Start"] = $"{RunConstants.Phases.Dedupe} Gruppiere {{0}} Dateien nach GameKey...",
        ["Dedupe.Completed"] = $"{RunConstants.Phases.Dedupe} Done in {{0}}ms: {{1}} groups, Keep={{2}}, Move={{3}}, Junk={{4}}",

        ["Junk.Start"] = $"{RunConstants.Phases.Junk} Entferne Junk-Dateien...",
        ["Junk.Completed"] = $"{RunConstants.Phases.Junk} {{0}} Junk-Datei(en) entfernt",

        ["Convert.OnlyStart"] = $"{RunConstants.Phases.Convert} Convert-only: {{0}} files...",
        ["Convert.StartGroups"] = $"{RunConstants.Phases.Convert} Start format conversion for {{0}} groups...",
        ["Convert.Completed"] = $"{RunConstants.Phases.Convert} Done: {{0}} converted, {{1}} skipped, {{2}} blocked, {{3}} errors",
        ["Convert.Progress"] = $"{RunConstants.Phases.Convert} Progress: {{0}}/{{1}} {{2}} (ok={{3}}, skip={{4}}, blocked={{5}}, err={{6}})",
        ["Convert.FileTarget"] = $"{RunConstants.Phases.Convert} {{0}} -> {{1}}",
        ["Convert.StepDone"] = $"{RunConstants.Phases.Convert} {{0}} step {{1}} of {{2}} done",

        ["Watch.Error"] = "FileSystemWatcher error: {0}",
        ["Watch.UnknownError"] = "Unknown watcher error"
    };

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Preflight.Checking"] = $"{RunConstants.Phases.Preflight} Checking prerequisites...",
        ["Preflight.Blocked"] = $"{RunConstants.Phases.Preflight} Blocked: {{0}}",
        ["Preflight.Ok"] = $"{RunConstants.Phases.Preflight} OK - {{0}} root(s), {{1}} extension(s), mode: {{2}}",
        ["Preflight.Warning"] = $"{RunConstants.Phases.Preflight} Warning: {{0}}",
        ["Pipeline.Error.Aborted"] = "[ERROR] Pipeline aborted: {0}: {1}",

        ["Scan.Start"] = $"{RunConstants.Phases.Scan} Scanning {{0}} root folder(s)...",
        ["Scan.Root"] = $"{RunConstants.Phases.Scan} Root: {{0}}",
        ["Scan.RootCollecting"] = $"{RunConstants.Phases.Scan} {{0}}: Collecting files...",
        ["Scan.RootFound"] = $"{RunConstants.Phases.Scan} {{0}}: {{1}} files found",
        ["Scan.ProgressProcessed"] = $"{RunConstants.Phases.Scan} {{0}}/{{1}} files processed...",
        ["Filter.OnlyGames"] = $"{RunConstants.Phases.Filter} OnlyGames active: {{0}} non-game file(s) excluded (KeepUnknown={{1}})",
        ["Scan.Completed"] = $"{RunConstants.Phases.Scan} Completed: {{0}} files in {{1}}ms",
        ["Scan.HashLarge"] = $"{RunConstants.Phases.Scan} Hash: {{0}} ({{1:F0}} MB)...",
        ["Scan.IncompleteWarning"] = "WARNING: Scan incomplete: {0} directories inaccessible",
        ["Scan.HighMemoryWarning"] = "WARNING: {0:N0} files scanned - high memory usage. Consider scanning fewer roots.",

        ["Move.Start"] = $"{RunConstants.Phases.Move} Moving {{0}} duplicate(s) to trash...",
        ["Move.Completed"] = $"{RunConstants.Phases.Move} Completed: {{0}} moved, {{1}} error(s)",
        ["Move.Abort.OutOfSpace"] = $"{RunConstants.Phases.Move} Aborted: not enough free space at destination ({{0}} bytes available, {{1}} bytes required).",
        ["Move.Progress"] = $"{RunConstants.Phases.Move} Progress: {{0}}/{{1}} (moved={{2}}, skipped={{3}}, failed={{4}})",
        ["Move.SkipConflict"] = $"{RunConstants.Phases.Move} Skip (conflict): {{0}}",

        ["Sort.Start"] = $"{RunConstants.Phases.Sort} Sorting files by console...",
        ["Sort.Completed"] = $"{RunConstants.Phases.Sort} Console sorting completed",

        ["Report.Generate"] = $"{RunConstants.Phases.Report} Generating HTML report...",
        ["Report.Created"] = $"{RunConstants.Phases.Report} Report created: {{0}}",
        ["Report.Failed"] = $"{RunConstants.Phases.Report} Requested report could not be written to target or fallback path.",

        ["Audit.WriteSidecar"] = "[Audit] Writing audit sidecar...",
        ["Audit.SidecarWriteFailed"] = "[Audit] Sidecar write failed: {0}: {1}",
        ["Done.Pipeline"] = $"{RunConstants.Phases.Finished} Pipeline completed in {{0}}ms - {{1}} file(s), {{2}} group(s)",

        ["Analyze.Skipped.CrossRoot"] = "[CrossRoot] Analysis skipped: {0}",
        ["Analyze.Skipped.FolderDedupe"] = "[FolderDedupe] Analysis skipped: {0}",
        ["Analyze.Skipped.Quarantine"] = "[Quarantine] Analysis skipped: {0}",
        ["Analyze.Skipped.Hardlink"] = "[Hardlink] Analysis skipped: {0}",
        ["Preview.FolderDedupeSummary"] = "[FolderDedupe] Preview: {0} analysis result(s), PS3 roots={1}, basename roots={2}",
        ["Preview.CrossRootSummary"] = "[CrossRoot] Preview: {0} cross-root hash groups (sample={1})",
        ["Preview.QuarantineSummary"] = "[Quarantine] Preview: {0} suspicious file(s) in sample of {1} candidates",
        ["Preview.HardlinkSummary"] = "[Hardlink] NTFS hardlink support: {0}/{1} root(s)",
        ["CollectionIndex.DeltaLookupsDisabled"] = "[CollectionIndex] Delta lookups disabled for this run: {0}",

        ["Dedupe.Start"] = $"{RunConstants.Phases.Dedupe} Grouping {{0}} files by GameKey...",
        ["Dedupe.Completed"] = $"{RunConstants.Phases.Dedupe} Completed in {{0}}ms: {{1}} groups, Keep={{2}}, Move={{3}}, Junk={{4}}",

        ["Junk.Start"] = $"{RunConstants.Phases.Junk} Removing junk files...",
        ["Junk.Completed"] = $"{RunConstants.Phases.Junk} {{0}} junk file(s) removed",

        ["Convert.OnlyStart"] = $"{RunConstants.Phases.Convert} Convert-only: {{0}} files...",
        ["Convert.StartGroups"] = $"{RunConstants.Phases.Convert} Starting format conversion for {{0}} groups...",
        ["Convert.Completed"] = $"{RunConstants.Phases.Convert} Completed: {{0}} converted, {{1}} skipped, {{2}} blocked, {{3}} error(s)",
        ["Convert.Progress"] = $"{RunConstants.Phases.Convert} Progress: {{0}}/{{1}} {{2}} (ok={{3}}, skip={{4}}, blocked={{5}}, err={{6}})",
        ["Convert.FileTarget"] = $"{RunConstants.Phases.Convert} {{0}} -> {{1}}",
        ["Convert.StepDone"] = $"{RunConstants.Phases.Convert} {{0}} step {{1}} of {{2}} completed",

        ["Watch.Error"] = "FileSystemWatcher error: {0}",
        ["Watch.UnknownError"] = "Unknown watcher error"
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Preflight.Checking"] = $"{RunConstants.Phases.Preflight} Verification des prerequis...",
        ["Preflight.Blocked"] = $"{RunConstants.Phases.Preflight} Bloque: {{0}}",
        ["Preflight.Ok"] = $"{RunConstants.Phases.Preflight} OK - {{0}} racine(s), {{1}} extension(s), mode: {{2}}",
        ["Preflight.Warning"] = $"{RunConstants.Phases.Preflight} Avertissement: {{0}}",
        ["Pipeline.Error.Aborted"] = "[ERREUR] Pipeline interrompu: {0}: {1}",

        ["Scan.Start"] = $"{RunConstants.Phases.Scan} Analyse de {{0}} dossier(s) racine...",
        ["Scan.Root"] = $"{RunConstants.Phases.Scan} Racine: {{0}}",
        ["Filter.OnlyGames"] = $"{RunConstants.Phases.Filter} OnlyGames actif: {{0}} fichier(s) non-jeu exclus (KeepUnknown={{1}})",
        ["Scan.Completed"] = $"{RunConstants.Phases.Scan} Termine: {{0}} fichier(s) en {{1}}ms",
        ["Scan.HashLarge"] = $"{RunConstants.Phases.Scan} Hash: {{0}} ({{1:F0}} MB)...",

        ["Move.Start"] = $"{RunConstants.Phases.Move} Deplacement de {{0}} doublon(s) vers la corbeille...",
        ["Move.Completed"] = $"{RunConstants.Phases.Move} Termine: {{0}} deplace(s), {{1}} erreur(s)",
        ["Move.Abort.OutOfSpace"] = $"{RunConstants.Phases.Move} Arret: espace disque insuffisant ({{0}} octets disponibles, {{1}} octets requis).",
        ["Move.Progress"] = $"{RunConstants.Phases.Move} Progression: {{0}}/{{1}} (moved={{2}}, skipped={{3}}, failed={{4}})",

        ["Sort.Start"] = $"{RunConstants.Phases.Sort} Tri des fichiers par console...",
        ["Sort.Completed"] = $"{RunConstants.Phases.Sort} Tri des consoles termine",

        ["Report.Generate"] = $"{RunConstants.Phases.Report} Generation du rapport HTML...",
        ["Report.Created"] = $"{RunConstants.Phases.Report} Rapport cree: {{0}}",
        ["Report.Failed"] = $"{RunConstants.Phases.Report} Impossible d'ecrire le rapport au chemin cible ou de secours.",

        ["Audit.WriteSidecar"] = "[Audit] Ecriture du sidecar d'audit...",
        ["Done.Pipeline"] = $"{RunConstants.Phases.Finished} Pipeline termine en {{0}}ms - {{1}} fichier(s), {{2}} groupe(s)",

        ["Watch.Error"] = "Erreur FileSystemWatcher: {0}",
        ["Watch.UnknownError"] = "Erreur watcher inconnue"
    };

    public static string Format(string key, params object[] args)
        => FormatTemplate(ResolveTemplate(key, key), args);

    public static string Format(string key, string germanTemplate, params object[] args)
        => FormatTemplate(ResolveTemplate(key, germanTemplate), args);

    private static string FormatTemplate(string template, params object[] args)
    {
        if (args.Length == 0)
            return template;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static string ResolveTemplate(string key, string fallbackTemplate)
    {
        var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (locale.Equals("en", StringComparison.OrdinalIgnoreCase)
            && En.TryGetValue(key, out var enTemplate))
        {
            return enTemplate;
        }

        if (locale.Equals("fr", StringComparison.OrdinalIgnoreCase)
            && Fr.TryGetValue(key, out var frTemplate))
        {
            return frTemplate;
        }

        if (De.TryGetValue(key, out var deTemplate))
            return deTemplate;

        return fallbackTemplate;
    }
}