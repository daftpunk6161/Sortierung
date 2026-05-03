# Sicherheitsrichtlinie

## Unterstützte Versionen

Romulus befindet sich in aktiver Entwicklung. Sicherheitsupdates werden für die folgenden Versionen bereitgestellt:

| Version | Unterstützt          |
| ------- | -------------------- |
| main    | :white_check_mark:   |
| < 1.0   | :x:                  |

## Sicherheitsrelevante Features

Romulus implementiert mehrere sicherheitskritische Mechanismen:

- **Path Traversal Protection**: Alle Dateisystemoperationen werden gegen `FileSystemAdapter.ResolveChildPathWithinRoot` validiert
- **Reparse Point Blocking**: Symlinks und Junctions werden explizit blockiert und niemals transparent gefolgt
- **CSV Injection Prevention**: Audit-Felder werden gegen führende Formel-Präfixe (`=`, `+`, `-`, `@`) geschützt
- **HTML Encoding**: Alle HTML-Reports verwenden konsequentes Encoding
- **Tool Hash Verification**: SHA-256 Checksums aus `data/tool-hashes.json` werden vor Tool-Ausführung verifiziert
- **XXE Protection**: DAT-XML-Parsing verwendet sichere Parser-Konfiguration
- **Audit Trail**: Signierter, manipulationssicherer Audit-Trail für alle Move/Convert-Aktionen

## Eine Sicherheitslücke melden

**Bitte melden Sie Sicherheitslücken NICHT über öffentliche GitHub Issues.**

Stattdessen:

1. **Privater Report**: Erstellen Sie einen privaten Security Advisory über GitHub:
   - Gehen Sie zu: https://github.com/daftpunk6161/Romulus/security/advisories/new
   - Beschreiben Sie die Schwachstelle detailliert
   - Fügen Sie wenn möglich einen Proof-of-Concept hinzu

2. **E-Mail**: Alternativ können Sie eine E-Mail an den Maintainer senden (siehe GitHub-Profil)

3. **Erwartete Antwortzeit**:
   - Erste Rückmeldung innerhalb von 48 Stunden
   - Detaillierte Analyse innerhalb von 7 Tagen
   - Fix für kritische Schwachstellen innerhalb von 14 Tagen

## Sicherheitsrelevante Bereiche

Besonders kritische Code-Bereiche, die sorgfältig geprüft werden sollten:

### Kritisch (P1)
- `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` - Path Traversal Protection
- `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` - Audit-Trail Integrität
- `src/Romulus.Infrastructure/Safety/` - Alle Safety-Gates und Validierungen
- `src/Romulus.Infrastructure/Tools/ToolRunner.cs` - Tool-Ausführung und Hash-Verifizierung

### Hoch (P2)
- `src/Romulus.Core/Deduplication/DeduplicationEngine.cs` - Winner-Selection Logik
- `src/Romulus.Infrastructure/Conversion/` - Formatkonvertierung
- `src/Romulus.Infrastructure/Dat/` - DAT-Parsing und -Validierung
- `src/Romulus.Infrastructure/Reporting/` - Report-Generierung (HTML/CSV-Injection)

### Mittel (P3)
- `src/Romulus.Api/` - REST API Endpoints (Authentication, Rate-Limiting)
- `src/Romulus.CLI/` - Command-Line Interface
- `src/Romulus.UI.Wpf/` - GUI (besonders Danger-Confirm-Dialoge)

## Bekannte Sicherheitsmaßnahmen

### Datenverlust-Schutz
- Standard-Verhalten ist **Move to Trash**, nicht direktes Löschen
- Alle riskanten Operationen erfordern explizite Bestätigung
- Lossy Conversions erfordern Token-basierte Bestätigung
- Audit-Trail ermöglicht vollständigen Rollback

### Eingabevalidierung
- Alle Benutzereingaben werden validiert
- Pfade werden gegen erlaubte Roots geprüft
- DAT-Dateien werden gegen XML External Entity (XXE) Angriffe geschützt
- Tool-Outputs werden validiert bevor Folgeentscheidungen getroffen werden

### Externe Tools
- Tools werden nur mit verifizierten Hashes ausgeführt (`data/tool-hashes.json`)
- Argument-Quoting verhindert Command Injection
- Timeouts und Exit-Code-Prüfung
- Cleanup bei Fehlern

## Disclosure Policy

Wenn eine Sicherheitslücke gemeldet und bestätigt wird:

1. Der Reporter wird über den Fix-Fortschritt informiert
2. Ein Security Advisory wird vorbereitet
3. Der Fix wird implementiert und getestet
4. Ein Release mit dem Fix wird erstellt
5. Der Security Advisory wird veröffentlicht
6. Der Reporter wird im Advisory erwähnt (wenn gewünscht)

## Sicherheits-Checkliste für Contributors

Bei Pull Requests, die sicherheitsrelevante Bereiche berühren:

- [ ] Path-Traversal-Schutz implementiert (`ResolveChildPathWithinRoot`)
- [ ] Reparse Points werden blockiert oder sicher behandelt
- [ ] CSV-Injection verhindert (keine Formel-Präfixe)
- [ ] HTML-Encoding angewendet
- [ ] Tool-Hash-Verifizierung für externe Tools
- [ ] Eingabevalidierung implementiert
- [ ] Fehlerbehandlung verhindert Information Disclosure
- [ ] Tests für Security-relevante Änderungen vorhanden

## Weitere Informationen

- Architektur-Dokumentation: `docs/architecture/`
- Coding Standards: `CONTRIBUTING.md`
- Agent Instructions: `AGENTS.md`
