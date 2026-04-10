# R2 Productization Execution

Stand: 2026-04-01

Ziel: Die starke Kernlogik in gefuehrte, reproduzierbare Produktablaeufe ueberfuehren und den operativen Nutzen fuer echte Sammlungen sichtbar steigern.

## Nicht-Scope

- Kein neues fachliches Parallelmodell fuer Wizard, Export oder Profile
- Kein Dashboard vor stabiler Foundation
- Keine exotischen Frontend-Ziele ausser den priorisierten Exportformaten

## Tickets

### [x] R2-T01 Szenario-Katalog und RunOptions-Mapping festziehen

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Die Guided-Workflow-Szenarien fachlich so definieren, dass sie direkt auf bestehende RunOptions und Review-Flows abbildbar sind.

Betroffene Bereiche:
- `docs/epics/C3-guided-workflows.md`
- `src/Romulus.Contracts`
- `src/Romulus.UI.Wpf`

Akzeptanz:
- Quick Clean, Full Audit, DAT Verification, Format Optimization und New Collection Setup sind fachlich definiert
- Jedes Szenario mappt ohne Sonderlogik auf bestehende Optionen
- Expertenmodus und Wizard widersprechen sich fachlich nicht

Abhaengigkeiten:
- R1 abgeschlossen

### [x] R2-T02 Wizard-State-Machine und UI-Integration umsetzen

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Einen gefuehrten Einstieg bereitstellen, der die bestehende Pipeline wiederverwendet statt neu zu berechnen.

Betroffene Bereiche:
- `src/Romulus.UI.Wpf/ViewModels`
- `src/Romulus.UI.Wpf/Views`
- `src/Romulus.UI.Wpf`

Akzeptanz:
- Wizard-Schritte, Ruecknavigation und Cancel-Verhalten sind robust
- Wizard erzeugt dieselben fachlichen Eingaben wie der Expertenmodus
- Keine Businesslogik wandert ins Code-Behind

Abhaengigkeiten:
- R2-T01

### [x] R2-T03 Gemeinsame Workflow-Projection und Summary fuer Wizard und Expertenmodus

Status: Abgeschlossen (Update 2026-04-01)

Ziel: KPI-, Summary- und Decision-Anzeigen fuer beide Modi aus derselben Quelle beziehen.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Orchestration`
- `src/Romulus.UI.Wpf/ViewModels`
- `src/Romulus.Api`

Akzeptanz:
- Wizard und Expertenmodus zeigen dieselben KPIs fuer denselben Run
- Preview-, Summary- und Report-Werte leiten sich aus derselben Projection ab
- Keine lokalen UI-Sonderrechnungen fuer gefuehrte Workflows

Abhaengigkeiten:
- R2-T02

### [x] R2-T04 Exportmodell und gemeinsamer Export-Query-Pfad definieren

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Exportdaten nur einmal fachlich ableiten und fuer alle Frontend-Exporter wiederverwenden.

Betroffene Bereiche:
- `src/Romulus.Contracts`
- `src/Romulus.Infrastructure/Export`
- `src/Romulus.Infrastructure/Analysis`

Akzeptanz:
- `ExportableGame` oder aequivalentes Modell ist zentral definiert
- Exportdaten stammen aus Run-Ergebnissen oder Collection-Index, nicht aus lokaler UI-Logik
- Export-Queries sind deterministisch und testbar

Abhaengigkeiten:
- R1-T05

### [x] R2-T05 RetroArch- und LaunchBox-Exporter produktisieren

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Die ersten zwei priorisierten Frontend-Ziele stabil und reproduzierbar bedienen.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Export`
- `src/Romulus.CLI`
- `src/Romulus.Api`

Akzeptanz:
- RetroArch-Export ist vollstaendig ueber den gemeinsamen Exportpfad angebunden
- LaunchBox-Export erzeugt gueltige Dateien mit sauberem Escaping
- CLI und API nutzen denselben Export-Stack

Abhaengigkeiten:
- R2-T04

### [x] R2-T06 EmulationStation- und Playnite-Exporter bereitstellen

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Das Export-Framework auf die restlichen priorisierten Frontends erweitern.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Export`
- `src/Romulus.CLI`
- `src/Romulus.Api`

Akzeptanz:
- EmulationStation und Playnite sind ueber denselben Exportvertrag angebunden
- Pfad- und Encoding-Regeln sind pro Frontend sauber validiert
- Exportfehler werden konsistent und nachvollziehbar berichtet

Abhaengigkeiten:
- R2-T05

### [x] R2-T07 Profilformat, Validierung und Built-in-Profile definieren

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Einstellungen als versionierbare, importierbare und sichere Profile nutzbar machen.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Profiles`
- `src/Romulus.Contracts`
- `data/`

Akzeptanz:
- Profilformat enthaelt Version, Metadaten und validierte Settings
- Built-in-Profile fuer Default, Retro Purist, Space Saver und Quick Scan sind definiert
- Profilimporte blockieren ungueltige oder gefaehrliche Werte

Abhaengigkeiten:
- R1 abgeschlossen

### [x] R2-T08 Profile in GUI, CLI und API ohne Schattenlogik verdrahten

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Dasselbe Profilmodell in allen Kanaelen nutzbar machen.

Betroffene Bereiche:
- `src/Romulus.UI.Wpf`
- `src/Romulus.CLI`
- `src/Romulus.Api`
- `src/Romulus.Infrastructure/Profiles`

Akzeptanz:
- Profile koennen importiert, ausgewaehlt und fuer Runs verwendet werden
- Alle Kanaele mappen Profile auf dieselben RunOptions
- Profil-basierte Runs bleiben preview- und report-paritaetisch

Abhaengigkeiten:
- R2-T07

### [x] R2-T09 Run-Diff, Trend-Reports und Storage-Insights liefern

Status: Abgeschlossen (Update 2026-04-01)

Ziel: Historische Aenderungen, Sammlungsentwicklung und Speicherwirkung sichtbar machen.

Betroffene Bereiche:
- `src/Romulus.Infrastructure/Reporting`
- `src/Romulus.Infrastructure/Analysis`
- `src/Romulus.Api`
- `src/Romulus.UI.Wpf`

Akzeptanz:
- Zwei Runs lassen sich fachlich vergleichen
- Trend-Reports basieren auf Run-Historie statt auf neuen Full-Scans
- Speicher- und Konvertierungseffekte sind nachvollziehbar darstellbar

Abhaengigkeiten:
- R1-T04
- R2-T04

### [x] R2-T10 Regressionstest-Matrix fuer Wizard, Export und Profile erweitern

Ziel: Die neue Produktisierung gegen Paritaetsfehler und fehlerhafte Importe absichern.

Status: Abgeschlossen (Update 2026-04-01)

Aktueller Fortschritt:
- Neue Regressionstests fuer Workflow/Profile-Aufloesung inkl. expliziter Override-Prioritaet und ungueltigem Profilimport sind vorhanden (`RunConfigurationResolverRegressionTests`)
- Neue Regressionstests fuer Frontend-Export validieren XML-Escaping und Root-Containment bei EmulationStation-Ordnerexport (`FrontendExportRegressionTests`)
- Wizard-Output-Paritaet ist als direkte Materialisierungs-Regression gegen Expert-Input abgesichert (`RunConfigurationMaterializer_WizardAndExpertInputs_MaterializeToEquivalentOptions`)
- Negative CSV-Injection-Faelle sind im Frontend-Exportpfad als dedizierte Regression verankert (`FrontendExportService_Csv_QuotesDangerousFormulaPrefixes`)
- API-Integrationstests fuer Run-Diff und Trends sind ergaenzt (`ApiIntegrationTests`: `/runs/compare`, `/runs/trends`)
- Neue API-Regressionen fuer `/profiles`, `/workflows`, workflow-/profilbasierte Runs, Watch-Automation und `/export/frontend` sind vorhanden (`ApiProductizationIntegrationTests`)
- Neue WPF-Regressionen sichern Kataloge, gemeinsame Materialisierung, Profil-Save/Load und Wizard-Bindings (`WpfProductizationTests`)
- Neue CLI-Regressionen decken Usage-Text und Produktisierungs-Subcommands ab (`CliProductizationTests`)
- Neue OpenAPI-Regression stellt die typisierten R2-Vertraege sicher (`OpenApiProductizationTests`)
- Gezielter Testlauf auf diese Produktisierungsfaelle ist gruen
- Vollsuite (`dotnet test`) ist gruen: `7133/7133` erfolgreich

Betroffene Bereiche:
- `src/Romulus.Tests`
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

- [x] Guided Workflows sind fachlich deckungsgleich mit dem Expertenmodus
- [x] Export nach RetroArch, LaunchBox, EmulationStation und Playnite ist produktiv abbildbar
- [x] Profile sind validiert, versioniert und kanaluebergreifend nutzbar
- [x] Trend- und Diff-Reports basieren auf der persistierten Run-Historie
- [x] Produktisierungs-Tests fuer Wizard, Export und Profile sind vorhanden
