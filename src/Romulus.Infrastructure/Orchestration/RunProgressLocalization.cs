using System.Globalization;

namespace Romulus.Infrastructure.Orchestration;

internal static class RunProgressLocalization
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Preflight.Checking"] = "[Preflight] Checking prerequisites...",
        ["Preflight.Blocked"] = "[Preflight] Blocked: {0}",
        ["Preflight.Ok"] = "[Preflight] OK - {0} root(s), {1} extension(s), mode: {2}",
        ["Preflight.Warning"] = "[Preflight] Warning: {0}",
        ["Pipeline.Error.Aborted"] = "[ERROR] Pipeline aborted: {0}: {1}",

        ["Scan.Start"] = "[Scan] Scanning {0} root folder(s)...",
        ["Scan.Root"] = "[Scan] Root: {0}",
        ["Filter.OnlyGames"] = "[Filter] OnlyGames active: {0} non-game file(s) excluded (KeepUnknown={1})",
        ["Scan.Completed"] = "[Scan] Completed: {0} files in {1}ms",
        ["Scan.HashLarge"] = "[Scan] Hash: {0} ({1:F0} MB)...",

        ["Move.Start"] = "[Move] Moving {0} duplicate(s) to trash...",
        ["Move.Completed"] = "[Move] Completed: {0} moved, {1} error(s)",
        ["Move.Abort.OutOfSpace"] = "[Move] Aborted: not enough free space at destination ({0} bytes available, {1} bytes required).",
        ["Move.Progress"] = "[Move] Progress: {0}/{1} (moved={2}, skipped={3}, failed={4})",

        ["Sort.Start"] = "[Sort] Sorting files by console...",
        ["Sort.Completed"] = "[Sort] Console sorting completed",

        ["Report.Generate"] = "[Report] Generating HTML report...",
        ["Report.Created"] = "[Report] Report created: {0}",
        ["Report.Failed"] = "[Report] Requested report could not be written to target or fallback path.",

        ["Audit.WriteSidecar"] = "[Audit] Writing audit sidecar...",
        ["Done.Pipeline"] = "[Done] Pipeline completed in {0}ms - {1} file(s), {2} group(s)"
    };

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Preflight.Checking"] = "[Preflight] Verification des prerequis...",
        ["Preflight.Blocked"] = "[Preflight] Bloque: {0}",
        ["Preflight.Ok"] = "[Preflight] OK - {0} racine(s), {1} extension(s), mode: {2}",
        ["Preflight.Warning"] = "[Preflight] Avertissement: {0}",
        ["Pipeline.Error.Aborted"] = "[ERREUR] Pipeline interrompu: {0}: {1}",

        ["Scan.Start"] = "[Scan] Analyse de {0} dossier(s) racine...",
        ["Scan.Root"] = "[Scan] Racine: {0}",
        ["Filter.OnlyGames"] = "[Filter] OnlyGames actif: {0} fichier(s) non-jeu exclus (KeepUnknown={1})",
        ["Scan.Completed"] = "[Scan] Termine: {0} fichier(s) en {1}ms",
        ["Scan.HashLarge"] = "[Scan] Hash: {0} ({1:F0} MB)...",

        ["Move.Start"] = "[Move] Deplacement de {0} doublon(s) vers la corbeille...",
        ["Move.Completed"] = "[Move] Termine: {0} deplace(s), {1} erreur(s)",
        ["Move.Abort.OutOfSpace"] = "[Move] Arret: espace disque insuffisant ({0} octets disponibles, {1} octets requis).",
        ["Move.Progress"] = "[Move] Progression: {0}/{1} (moved={2}, skipped={3}, failed={4})",

        ["Sort.Start"] = "[Sort] Tri des fichiers par console...",
        ["Sort.Completed"] = "[Sort] Tri des consoles termine",

        ["Report.Generate"] = "[Report] Generation du rapport HTML...",
        ["Report.Created"] = "[Report] Rapport cree: {0}",
        ["Report.Failed"] = "[Report] Impossible d'ecrire le rapport au chemin cible ou de secours.",

        ["Audit.WriteSidecar"] = "[Audit] Ecriture du sidecar d'audit...",
        ["Done.Pipeline"] = "[Done] Pipeline termine en {0}ms - {1} fichier(s), {2} groupe(s)"
    };

    public static string Format(string key, string germanTemplate, params object[] args)
    {
        var template = ResolveTemplate(key, germanTemplate);
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

    private static string ResolveTemplate(string key, string germanTemplate)
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

        return germanTemplate;
    }
}