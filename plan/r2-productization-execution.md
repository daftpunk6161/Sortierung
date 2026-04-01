# R2 Productization Execution

Stand: 2026-04-01

Ziel: Die starke Kernlogik in gefuehrte, reproduzierbare Produktablaeufe ueberfuehren und den operativen Nutzen fuer echte Sammlungen sichtbar steigern.

## Nicht-Scope

- Kein neues fachliches Parallelmodell fuer Wizard, Export oder Profile
- Kein Dashboard vor stabiler Foundation
- Keine exotischen Frontend-Ziele ausser den priorisierten Exportformaten

## Tickets

### [ ] R2-T01 Szenario-Katalog und RunOptions-Mapping festziehen

Ziel: Die Guided-Workflow-Szenarien fachlich so definieren, dass sie direkt auf bestehende RunOptions und Review-Flows abbildbar sind.

Betroffene Bereiche:
- `docs/epics/C3-guided-workflows.md`
- `src/RomCleanup.Contracts`
- `src/RomCleanup.UI.Wpf`

Akzeptanz:
- Quick Clean, Full Audit, DAT Verification, Format Optimization und New Collection Setup sind fachlich definiert
- Jedes Szenario mappt ohne Sonderlogik auf bestehende Optionen
- Expertenmodus und Wizard widersprechen sich fachlich nicht

Abhaengigkeiten:
- R1 abgeschlossen

### [ ] R2-T02 Wizard-State-Machine und UI-Integration umsetzen

Ziel: Einen gefuehrten Einstieg bereitstellen, der die bestehende Pipeline wiederverwendet statt neu zu berechnen.

Betroffene Bereiche:
- `src/RomCleanup.UI.Wpf/ViewModels`
- `src/RomCleanup.UI.Wpf/Views`
- `src/RomCleanup.UI.Wpf`

Akzeptanz:
- Wizard-Schritte, Ruecknavigation und Cancel-Verhalten sind robust
- Wizard erzeugt dieselben fachlichen Eingaben wie der Expertenmodus
- Keine Businesslogik wandert ins Code-Behind

Abhaengigkeiten:
- R2-T01

### [ ] R2-T03 Gemeinsame Workflow-Projection und Summary fuer Wizard und Expertenmodus

Ziel: KPI-, Summary- und Decision-Anzeigen fuer beide Modi aus derselben Quelle beziehen.

Betroffene Bereiche:
- `src/RomCleanup.Infrastructure/Orchestration`
- `src/RomCleanup.UI.Wpf/ViewModels`
- `src/RomCleanup.Api`

Akzeptanz:
- Wizard und Expertenmodus zeigen dieselben KPIs fuer denselben Run
- Preview-, Summary- und Report-Werte leiten sich aus derselben Projection ab
- Keine lokalen UI-Sonderrechnungen fuer gefuehrte Workflows

Abhaengigkeiten:
- R2-T02

### [ ] R2-T04 Exportmodell und gemeinsamer Export-Query-Pfad definieren

Ziel: Exportdaten nur einmal fachlich ableiten und fuer alle Frontend-Exporter wiederverwenden.

Betroffene Bereiche:
- `src/RomCleanup.Contracts`
- `src/RomCleanup.Infrastructure/Export`
- `src/RomCleanup.Infrastructure/Analysis`

Akzeptanz:
- `ExportableGame` oder aequivalentes Modell ist zentral definiert
- Exportdaten stammen aus Run-Ergebnissen oder Collection-Index, nicht aus lokaler UI-Logik
- Export-Queries sind deterministisch und testbar

Abhaengigkeiten:
- R1-T05

### [ ] R2-T05 RetroArch- und LaunchBox-Exporter produktisieren

Ziel: Die ersten zwei priorisierten Frontend-Ziele stabil und reproduzierbar bedienen.

Betroffene Bereiche:
- `src/RomCleanup.Infrastructure/Export`
- `src/RomCleanup.CLI`
- `src/RomCleanup.Api`

Akzeptanz:
- RetroArch-Export ist vollstaendig ueber den gemeinsamen Exportpfad angebunden
- LaunchBox-Export erzeugt gueltige Dateien mit sauberem Escaping
- CLI und API nutzen denselben Export-Stack

Abhaengigkeiten:
- R2-T04

### [ ] R2-T06 EmulationStation- und Playnite-Exporter bereitstellen

Ziel: Das Export-Framework auf die restlichen priorisierten Frontends erweitern.

Betroffene Bereiche:
- `src/RomCleanup.Infrastructure/Export`
- `src/RomCleanup.CLI`
- `src/RomCleanup.Api`

Akzeptanz:
- EmulationStation und Playnite sind ueber denselben Exportvertrag angebunden
- Pfad- und Encoding-Regeln sind pro Frontend sauber validiert
- Exportfehler werden konsistent und nachvollziehbar berichtet

Abhaengigkeiten:
- R2-T05

### [ ] R2-T07 Profilformat, Validierung und Built-in-Profile definieren

Ziel: Einstellungen als versionierbare, importierbare und sichere Profile nutzbar machen.

Betroffene Bereiche:
- `src/RomCleanup.Infrastructure/Profiles`
- `src/RomCleanup.Contracts`
- `data/`

Akzeptanz:
- Profilformat enthaelt Version, Metadaten und validierte Settings
- Built-in-Profile fuer Default, Retro Purist, Space Saver und Quick Scan sind definiert
- Profilimporte blockieren ungueltige oder gefaehrliche Werte

Abhaengigkeiten:
- R1 abgeschlossen

### [ ] R2-T08 Profile in GUI, CLI und API ohne Schattenlogik verdrahten

Ziel: Dasselbe Profilmodell in allen Kanaelen nutzbar machen.

Betroffene Bereiche:
- `src/RomCleanup.UI.Wpf`
- `src/RomCleanup.CLI`
- `src/RomCleanup.Api`
- `src/RomCleanup.Infrastructure/Profiles`

Akzeptanz:
- Profile koennen importiert, ausgewaehlt und fuer Runs verwendet werden
- Alle Kanaele mappen Profile auf dieselben RunOptions
- Profil-basierte Runs bleiben preview- und report-paritaetisch

Abhaengigkeiten:
- R2-T07

### [ ] R2-T09 Run-Diff, Trend-Reports und Storage-Insights liefern

Ziel: Historische Aenderungen, Sammlungsentwicklung und Speicherwirkung sichtbar machen.

Betroffene Bereiche:
- `src/RomCleanup.Infrastructure/Reporting`
- `src/RomCleanup.Infrastructure/Analysis`
- `src/RomCleanup.Api`
- `src/RomCleanup.UI.Wpf`

Akzeptanz:
- Zwei Runs lassen sich fachlich vergleichen
- Trend-Reports basieren auf Run-Historie statt auf neuen Full-Scans
- Speicher- und Konvertierungseffekte sind nachvollziehbar darstellbar

Abhaengigkeiten:
- R1-T04
- R2-T04

### [ ] R2-T10 Regressionstest-Matrix fuer Wizard, Export und Profile erweitern

Ziel: Die neue Produktisierung gegen Paritaetsfehler und fehlerhafte Importe absichern.

Betroffene Bereiche:
- `src/RomCleanup.Tests`
- `docs/architecture/TEST_STRATEGY.md`

Akzeptanz:
- Tests decken Wizard-Output-Paritaet, Export-Validitaet und Profil-Import ab
- Negative Faelle fuer ungueltige Profile, XML/CSV-Injection und Pfadprobleme sind enthalten
- Bestehende kanaluebergreifende KPI- und Result-Tests bleiben gruen

Abhaengigkeiten:
- R2-T03
- R2-T06
- R2-T08
- R2-T09

## Release-Exit

- [ ] Guided Workflows sind fachlich deckungsgleich mit dem Expertenmodus
- [ ] Export nach RetroArch, LaunchBox, EmulationStation und Playnite ist produktiv abbildbar
- [ ] Profile sind validiert, versioniert und kanaluebergreifend nutzbar
- [ ] Trend- und Diff-Reports basieren auf der persistierten Run-Historie
- [ ] Produktisierungs-Tests fuer Wizard, Export und Profile sind vorhanden
