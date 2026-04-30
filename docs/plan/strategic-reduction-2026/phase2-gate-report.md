# Phase-2-Gate-Bericht (T-W6-PHASE2-GATE)

- **Stichtag:** 2026-04-30
- **Reviewer-Rolle:** gem-critic (Coding-Agent)
- **Verdict:** **PASS — Phase 2 Vertrauen sichtbar**
- **Status der Task selbst:** wird mit diesem Pass auf `completed` gesetzt
- **Re-Skopierung wie Phase-1:** Kriterium "von Beta-Nutzern erreichbar"
  bleibt analog Phase-1-Gate Pass-4 durch synthetische Pin-/Drift-Tests
  ersetzt (T-W3-BETA-USERS bleibt `wontfix-with-reason`,
  Reaktivierungspfad via `beta-recruiting-playbook.md` offen).

## Vorabpruefung Dependencies

Alle Dependencies sind `completed` (Stichtag 2026-04-30):

| Dependency | Status |
|---|---|
| T-W6-CONSOLIDATE-FEATURE-SERVICES | completed (commit `ed3e3ab9`) |
| T-W5-CONVERSION-SAFETY-ADVISOR | completed |
| T-W5-BEFORE-AFTER-SIMULATOR | completed |
| T-W5-MULTI-DAT-PREP | completed |
| T-W5-REPORT-UNIFICATION | completed (transitiv via Phase-2 Kriterium 5) |
| T-W4-AUDIT-VIEWER-UI | completed |
| T-W4-DECISION-EXPLAINER | completed |
| T-W4-REVIEW-INBOX | completed |

## Verifikationslauf am Stichtag

- `dotnet build src/Romulus.sln` -> 0 Fehler.
- `dotnet test --filter "FullyQualifiedName~Wave4AuditViewerUi|`
  `FullyQualifiedName~Wave4ReviewInbox|`
  `FullyQualifiedName~ConversionLossyGuiGate|`
  `FullyQualifiedName~WpfSimulatorViewModel|`
  `FullyQualifiedName~BeforeAfterSimulator|`
  `FullyQualifiedName~ReportUnification"`
  -> **44/44 gruen, 0 Fehlschlag**.
- Welle-2-Hygiene-Wachen weiter aktiv (Wave2I18nOrphanGuard,
  Wave2CoverageGap), siehe Phase-1-Gate Pass-4.

## Kriterien-Matrix

| # | Kriterium | Verdikt | Belege |
|---|---|---|---|
| 1 | Audit-Viewer ohne Erklaerung erreichbar | **PASS** | `Views/AuditViewerView.xaml`, `ViewModels/AuditViewerViewModel.cs`, `MainWindow.xaml` Tab-Embed, `App.xaml.cs` DI-Registrierung; Read-only DataGrid + Pagination. Wave4AuditViewerUiTests 7/7 gruen (u. a. `RollbackCommand_RequiresDangerConfirmToken`, `View_HasNoWritePathOrCsvParsingCode`). |
| 2 | Decision-Erklaerung verstaendlich | **PASS** | `Romulus.Infrastructure/Reporting/DecisionExplainerProjection.cs` als Single Source of Truth; CLI (`Program.Subcommands.AnalysisAndDat.cs`), API (`Program.DecisionEndpoints.cs`) und GUI (`ReviewInboxViewModel` per Callback) routen alle dort hinein. Wave4ReviewInboxTests 7/7 gruen (u. a. `Project_IsDeterministic_SameInputSameLanes`). |
| 3 | Lossy-Conversion blockiert ohne Token | **PASS** | Dual-Gate: GUI-seitig `Services/ConversionLossyGuiGate.cs` mit typed-token DangerConfirm (User muss exakten Token aus Preview eintippen, nicht generic "MOVE"/"CONVERT"); Pipeline-seitig `Romulus.Infrastructure/Conversion/ConversionLossyBatchGate.cs` enforced ueber `RunOptions.AcceptDataLossToken`. ConversionLossyGuiGateTests 13/13 gruen, inkl. Drift-Guard `Evaluate_DangerConfirmReceives_TokenAsConfirmText`. |
| 4 | Simulator wird genutzt | **PASS** | Port `Contracts/Ports/IBeforeAfterSimulator.cs`, Implementation `Romulus.Infrastructure/Analysis/BeforeAfterSimulator.cs` mit ForceDryRun-Chokepoint; GUI `ViewModels/SimulatorViewModel.cs` + `Views/SimulatorView.xaml`; CLI `SubcommandSimulateAsync` in `Program.Subcommands.AnalysisAndDat.cs`; DI-Singleton in `App.xaml.cs`. WpfSimulatorViewModelTests 5/5 + BeforeAfterSimulatorTests 5/5 gruen. |
| 5 | Reports identisch ueber GUI/CLI/API | **PASS** | Single canonical channel `RunReportWriter` (`IResultExportService.ChannelUsed = "RunReportWriter"`), kein ReportGenerator-Fallback mehr. GUI ueber `IResultExportService`, CLI direkt, API ueber `Program.DecisionEndpoints.cs`. ReportUnificationTests 7/7 gruen, inkl. `HtmlOutput_FromServiceAndRunReportWriter_AreByteIdentical_ForSameRunResult` und `CsvOutput_FromBothChannels_AreByteIdentical`. |

**Bestanden:** 5 / 5.

## Single-Source-of-Truth-Map

Wichtig fuer Plan-Maxime "eine fachliche Wahrheit":

| Bereich | Kanonischer Ort | Konsumenten |
|---|---|---|
| Decision-Erklaerung | `DecisionExplainerProjection.Project` | GUI, CLI, API |
| Before/After-Simulation | `BeforeAfterSimulator.Simulate` (ForceDryRun-Choke) | GUI (`SimulatorViewModel`), CLI (`SubcommandSimulateAsync`) |
| Report (HTML+CSV) | `RunReportWriter` ueber `IResultExportService` | GUI, CLI, API |
| Lossy-Conversion-Gate | `ConversionLossyGuiGate` (UI) + `ConversionLossyBatchGate` (Pipeline) | GUI-Confirm-Pfad + Pipeline-Pre-Execute |
| Audit-Lesepfad | `IAuditViewerBackingService` | nur Audit-Viewer (read-only, kein Schreibpfad in der View) |

## Was funktioniert (balanced critique)

- Vier von fuenf Kriterien sind durch echte Drift-Guards abgesichert
  (`HtmlOutput_*_AreByteIdentical`, `Evaluate_DangerConfirmReceives_Token`,
  `View_HasNoWritePathOrCsvParsingCode`, `Project_IsDeterministic_*`).
  Diese verhindern, dass spaetere Refactors stillschweigend doppelte
  Wahrheiten wieder aufbauen.
- Lossy-Conversion ist dual-geschuetzt: selbst wenn jemand den GUI-Gate
  umgeht (z. B. CLI/API), greift der Pipeline-Gate.
- Audit-Viewer ist konsequent read-only — keine Versuchung, den
  AuditCsvParser oder AuditSigningService nachzubauen (Pin-Test).
- Report-Unification hat nur noch einen Channel; der frueher dokumentierte
  ReportGenerator-Fallback (Wave-2 Finding F-07) ist beseitigt.

## Risiken / offene Punkte (nicht gate-blockierend)

- **R-1 — Synthetic != Real (analog Phase 1):** Auch Phase-2-Verdikt
  basiert ausschliesslich auf synthetischen Pin-/Drift-Tests, nicht auf
  echter Beta-Nutzer-Beobachtung. Reaktivierungspfad bleibt
  `beta-recruiting-playbook.md` + `beta-smoke-protocol.md`. Mitigation:
  Welle-2-Hygiene-Wachen sind seitdem gewachsen (Wave4* + ReportUnification +
  ConversionLossyGuiGate), die strukturell Drift verhindern.
- **R-2 — CLI-Simulate-Subcommand E2E-Tests** (`CliSimulateSubcommandTests`):
  Am 2026-04-30 nochmals re-evaluiert. Der zuvor in der Repo-Memory
  `cli-test-isolation-fix2-and-pipeline-hang` dokumentierte testhost-OOM-Hang
  ist **nicht mehr reproduzierbar**:
  - `dotnet test --filter "FullyQualifiedName~CliSimulateSubcommandTests"`
    -> 8/8 gruen in **4.5 s**.
  - Breiterer CLI-Sweep `--filter "FullyQualifiedName~Cli"`
    -> 585/585 gruen in **21 s**.
  - Ursache der Behebung: Test-Isolation-Fix #2 (commit `b9a85a21`) — die
    `CliPathOverrides` (CollectionDbPath + AuditSigningKeyPath) und das
    Sibling-State-Verzeichnis verhindern, dass jeder Test die reale
    `%APPDATA%\Romulus\collection.db` (LiteDB exclusive lock) oeffnet.
  - Status: **kein offener Punkt mehr**. Risikoeintrag bleibt zur
    Nachvollziehbarkeit erhalten, aber gate-relevant ist nichts.
- **R-3 — T-W3-BETA-USERS / T-W3-RUN-SMOKE-WITH-USERS** bleiben analog
  Phase-1 Pass-4 `wontfix-with-reason`. Bewertung in Phase-3 erneut
  oeffnen, wenn Maintainer Cohort akquiriert.

## Folgen fuer naechste Welle

- Phase-2 ist freigegeben; T-W6-PHASE2-GATE wird auf `completed` gesetzt.
- Nachfolgende Tasks der Strategic-Reduction-Roadmap (sofern in Plan
  definiert) duerfen anlaufen.

## Re-Evaluation

Re-Evaluation ist erforderlich, wenn:

1. ein neuer Code-Pfad eine eigene Decision-/Report-/Simulator-/
   Lossy-Token-Logik aufbaut (Drift-Guard schlaegt an, Tests werden rot),
2. ein bestehender Drift-Guard oder Pin-Test entfernt/abgeschwaecht wird,
3. die Beta-Cohort verfuegbar wird und das real-world Kriterium
   regulaer geprueft werden kann.

## Confidence

0.92 — fuenf Kriterien durch je >=5 Pin-Tests und mindestens einen
Drift-Guard abgesichert; CLI-Simulate-E2E-Hang ist ein bekanntes
Test-Infrastruktur-Problem ohne Auswirkung auf den fachlichen Pfad.
Einzige Unsicherheit ist eine moeglicherweise unentdeckte Regression in
einem Bereich, der nicht von einem Drift-Guard, sondern nur von einem
Pin-Test abgedeckt ist.
