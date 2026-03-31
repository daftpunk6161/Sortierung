# C5: Frontend-Metadaten-Export

## Problem

Benutzer muessen Metadaten-Dateien fuer Emulatoren/Frontends manuell erstellen.
Romulus kennt bereits Konsole, GameKey und Pfade — kann diese Informationen aber
nicht in Frontend-spezifische Formate exportieren.

## Loesungsansatz

Pro Frontend ein Exporter-Service, der aus den Run-Ergebnissen oder dem
Collection-Index Metadaten-Dateien generiert.

### Unterstuetzte Frontends

1. **LaunchBox** — `LaunchBox/Data/Platforms/<Console>.xml`
   - XML-Format mit Game-Entries (Title, Path, Platform, DateAdded)
   - Import-kompatibel mit LaunchBox Bulk Import

2. **EmulationStation** — `<system>/gamelist.xml`
   - XML: `<gameList><game path="..." name="..." /></gameList>`
   - Pro System eine gamelist.xml

3. **RetroArch** — `playlists/<console>.lpl`
   - JSON: Array von `{ path, label, core_path, core_name, crc32, db_name }`
   - Bereits teilweise in CollectionAnalysisService.ExportRetroArchPlaylist

4. **Playnite** — `library/games/*.json`
   - JSON pro Spiel mit Metadaten
   - Optional: Playnite SDK Plugin

### Architektur

```csharp
// In Infrastructure/Export/
public interface IFrontendExporter
{
    string FrontendName { get; }
    string FileExtension { get; }
    Task ExportAsync(IReadOnlyList<ExportableGame> games, string outputPath, CancellationToken ct);
}

public sealed record ExportableGame(
    string Path, string GameName, string ConsoleKey,
    string? Region, bool DatVerified, string? Hash);

public sealed class LaunchBoxExporter : IFrontendExporter { ... }
public sealed class EmulationStationExporter : IFrontendExporter { ... }
public sealed class RetroArchExporter : IFrontendExporter { ... }
public sealed class PlayniteExporter : IFrontendExporter { ... }
```

### CLI

```
romulus export --roots <path> --format launchbox|es|retroarch|playnite -o <output>
```

### API

```
POST /export/frontend
{
  "roots": ["D:\\Roms"],
  "frontend": "launchbox",
  "outputPath": "D:\\LaunchBox\\Data"
}
```

## Abhaengigkeiten

- Run-Ergebnisse oder Collection-Index (C1)
- Console-Maps (`data/consoles.json`) fuer Frontend-spezifische System-Names

## Risiken

- Frontend-Formate koennen sich aendern → Versionierung noetig
- Pfad-Konventionen (relativ vs. absolut) variieren pro Frontend
- XML-Injection in LaunchBox/ES-Export verhindern

## Testplan

- Unit: Pro Exporter: Correct XML/JSON Output, Encoding, Escaping
- Integration: Export → Import in Frontend (manuell)
- Edge: Sonderzeichen in GameNames, Leere Sammlungen
- Regression: Bestehender RetroArch-Export unberuehrt
