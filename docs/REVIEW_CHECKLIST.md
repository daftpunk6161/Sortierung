# PR-Review-Checklist — C# .NET 10

> Stand: 2026-03-15 | Ref: `.github/copilot-instructions.md`, `docs/ARCHITECTURE_MAP.md`

---

## Vor jedem PR: Autor prüft

### 1 Naming & Sprache
- [ ] PascalCase für Methoden/Properties, camelCase für lokale Variablen/Parameter
- [ ] Keine deutschen Funktions- oder Variablennamen
- [ ] Neue Log-Nachrichten sind auf Englisch
- [ ] UI-Texte verwenden i18n-Keys aus `data/i18n/de.json`

### 2 Struktur & Architektur
- [ ] Dependency-Richtung eingehalten: Entry Points → Infrastructure → Core → Contracts (nie umgekehrt)
- [ ] Core-Logik ist pure (keine I/O-Abhängigkeiten in `RomCleanup.Core`)
- [ ] Neue Services über Konstruktor-Injection, Interfaces aus `Contracts/Ports/`
- [ ] Datenstrukturen als C# records oder Modelle in `Contracts/Models/`

### 3 Dead Code & Duplikate
- [ ] Keine ungenutzten öffentlichen Methoden eingeführt
- [ ] Keine Kopien bestehender Patterns — stattdessen vorhandene Services nutzen
- [ ] Catch-Blöcke sind nicht leer — mindestens Logging oder Kommentar

### 4 Tests
- [ ] xUnit-Tests für neue/geänderte Klassen vorhanden
- [ ] `dotnet test src/RomCleanup.sln` grün
- [ ] Testbenennung: `<Klasse>Tests.cs`
- [ ] Kein Alibi-Test (`Assert.True(true)` etc.)

### 5 Fehlerbehandlung
- [ ] Fehler verwenden `OperationError` mit Fehlerklasse (`Transient`, `Recoverable`, `Critical`)
- [ ] Fehler-Code-Namespaces beachtet: `GUI-*`, `DAT-*`, `IO-*`, `SEC-*`, `RUN-*`
- [ ] Keine rohen Strings als Fehler geworfen

### 6 Sicherheit
- [ ] Path-Traversal-Schutz via `FileSystemAdapter.ResolveChildPathWithinRoot` vor Move/Copy/Delete
- [ ] Kein direktes Löschen ohne explizite Bestätigung — Standard ist Move in Trash + Audit
- [ ] CSV-Injection verhindert (keine führenden `=`, `+`, `-`, `@`)
- [ ] HTML-Encoding in Report-Outputs
- [ ] Tool-Hash-Verifizierung bei externen Tool-Aufrufen
- [ ] Reparse Points (Symlinks/Junctions) explizit behandelt

### 7 Dokumentation
- [ ] ADR geschrieben bei architekturrelevanten Entscheidungen
- [ ] `docs/ARCHITECTURE_MAP.md` aktualisiert bei neuem Modul/Projekt

---

## Für Reviewer

### Quick-Gates (Block-Kriterien)

| Gate | Tool | Automatisiert? |
|------|------|----------------|
| Build | `dotnet build src/RomCleanup.sln` | ✅ CI |
| Unit-Tests | `dotnet test src/RomCleanup.sln` | ✅ CI |
| Coverage ≥ 50% | CI Coverage-Gate | ✅ CI |
| Naming-Policy | Manuell (Review) | ❌ Nein |

### Review-Fokus
1. **Schichtverletzungen:** Referenziert Core ein Infrastructure-Projekt? UI-Code in Domain?
2. **Silent Catches:** Neuer Catch ohne Logging → nachhaken
3. **Port-Nutzung:** Neue I/O-Zugriffe gehen über Port-Interfaces (`Contracts/Ports/`)
4. **Sicherheit:** Path-Traversal- und Injection-Schutz bei File-Ops und Reports
