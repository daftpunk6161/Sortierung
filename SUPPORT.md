# Support

## Hilfe bekommen

Wenn du Hilfe mit Romulus benötigst, gibt es mehrere Möglichkeiten:

### Dokumentation

Bitte prüfe zuerst die vorhandene Dokumentation:

- **README.md**: Projektübersicht, Features, Quick Start
- **CONTRIBUTING.md**: Setup, Architektur, Coding Standards
- **docs/architecture/**: Detaillierte Architektur-Dokumentation
- **docs/guides/**: Benutzeranleitungen und How-Tos

### Häufige Fragen

#### Installation & Setup

**F: Welche Voraussetzungen benötige ich?**
- .NET 10 SDK (net10.0, LangVersion 14)
- Windows 10/11 (für die WPF-GUI)
- Optional: Docker für API-Deployment

**F: Wie baue ich das Projekt?**
```bash
dotnet build src/Romulus.sln
```

**F: Wie führe ich die Tests aus?**
```bash
dotnet test src/Romulus.sln
```

**F: Wo werden die Einstellungen gespeichert?**
- `%APPDATA%\Romulus\settings.json`
- `%APPDATA%\Romulus\collection.db` (LiteDB für Review-Decisions)

#### Verwendung

**F: Was macht Romulus genau?**

Romulus ist ein Aufräum- und Verifikations-Werkzeug für ROM-Sammlungen. Es hilft dir:
- Duplikate zu erkennen und zu entfernen
- Junk-Files zu identifizieren
- ROMs nach Konsole zu sortieren
- Gegen DAT-Quellen (No-Intro, Redump) zu verifizieren
- Formate zu konvertieren (CUE/BIN → CHD, ISO → RVZ)

**F: Werden meine Dateien gelöscht?**

Nein! Der Standard ist **Move to Trash**. Alle Aktionen:
- Werden in einem signierten Audit-Trail protokolliert
- Können rückgängig gemacht werden (Rollback)
- Erfordern explizite Bestätigung bei riskanten Operationen

**F: Welche Entry Points gibt es?**
- **GUI** (WPF): `Romulus.UI.Wpf`
- **CLI**: `romulus.exe` (headless, für Automation/CI)
- **REST API**: `Romulus.Api` (ASP.NET Core Minimal API)

#### Probleme

**F: Die GUI startet nicht / stürzt ab**
- Prüfe `%APPDATA%\Romulus\crash.log`
- Stelle sicher, dass .NET 10 Runtime installiert ist
- Bei WebView2-Problemen: Installiere Microsoft Edge WebView2 Runtime

**F: Tests hängen / dauern sehr lange**
- `Romulus.Tests.Benchmark` benötigt ~23 Minuten (2923 Tests)
- Verwende Filter für schnellere Teilläufe: `dotnet test --filter "FullyQualifiedName~GameKey"`
- Siehe Memory "Romulus.Tests.Benchmark Laufzeit + Hang-Diagnose" für Details

**F: CLI-Tests hängen**
- Stelle sicher, dass `EnableDat=false` und `EnableDatExplicit=true` in Tests gesetzt ist
- Siehe Memory "CLI subcommand tests must force EnableDat=false"

### GitHub Issues

Für Bugs, Feature-Requests und Fragen:

1. **Durchsuche existierende Issues**: Vielleicht wurde dein Problem schon gemeldet
   - https://github.com/daftpunk6161/Romulus/issues

2. **Erstelle ein neues Issue**:
   - Verwende die Issue-Templates (Bug Report / Feature Request)
   - Füge relevante Informationen hinzu:
     - Romulus-Version
     - .NET-Version
     - Betriebssystem
     - Reproduktionsschritte
     - Erwartetes vs. tatsächliches Verhalten
     - Log-Dateien wenn verfügbar

### Pull Requests

Beiträge sind willkommen! Bitte:

1. Lies `CONTRIBUTING.md` für Coding Standards
2. Erstelle einen Fork des Repositories
3. Mache deine Änderungen in einem Feature-Branch
4. Stelle sicher, dass alle Tests grün sind
5. Erstelle einen Pull Request mit dem PR-Template

### Sicherheitsprobleme

**Melde Sicherheitslücken NICHT über öffentliche Issues!**

Siehe `SECURITY.md` für Details zum sicheren Melden von Schwachstellen.

### Community

- **GitHub Discussions**: Für allgemeine Fragen und Diskussionen
  - https://github.com/daftpunk6161/Romulus/discussions

- **Funding**: Wenn du das Projekt unterstützen möchtest:
  - GitHub Sponsors: [@daftpunk6161](https://github.com/sponsors/daftpunk6161)
  - Patreon: [romulus_rom](https://patreon.com/romulus_rom)

## Wichtige Hinweise

### Nicht verhandelbare Regeln

Bei allen Änderungen gelten (siehe `AGENTS.md`):

1. **Release-Fähigkeit geht vor**: Korrektheit, Determinismus, Sicherheit, Testbarkeit
2. **Kein Datenverlust**: Standard ist Move to Trash mit Audit-Trail
3. **Determinismus ist Pflicht**: Gleiche Inputs → gleiche Outputs
4. **Keine doppelte Logik**: Geschäftslogik nicht in mehreren Entry Points duplizieren
5. **Eine fachliche Wahrheit**: GUI/CLI/API/Reports müssen konsistent sein
6. **Keine halben Lösungen**: Kompilierbarer, integrierbarer Code

### Architektur

```
Entry Points → Infrastructure → Core → Contracts
```

- **Contracts**: Interfaces, Models, Error-Contracts
- **Core**: Pure Domain Logic (keine I/O)
- **Infrastructure**: I/O-Adapter (FileSystem, Tools, DAT, Reports)
- **CLI / API / GUI**: Entry Points

## Keine Antwort erhalten?

Falls du nach 7 Tagen keine Antwort erhalten hast:
- Kommentiere nochmal im Issue
- Prüfe, ob du alle nötigen Informationen angegeben hast
- Bei dringenden Sicherheitsproblemen: Siehe `SECURITY.md`

## Lizenz

Romulus ist unter der GNU General Public License v3.0 lizenziert.
Siehe `LICENSE` für Details.
