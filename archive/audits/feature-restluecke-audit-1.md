---
goal: Restluecken aus konsolidiertem Bug-Audit deterministisch schliessen
version: 1.0
date_created: 2026-03-12
last_updated: 2026-03-12
owner: RomCleanup Team
status: 'Done'
tags: [feature, bug-audit, security, api, wpf, testing, governance]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Dieser Plan schliesst die verbleibenden Audit-Luecken aus Security, API-Integration, WPF-Automation und Governance. Alle Schritte sind atomar, messbar und fuer AI-Agenten oder Menschen direkt ausfuehrbar.

## 1. Requirements & Constraints

- REQ-001: Schließe alle offenen Hochrisiko-Luecken aus dem Audit mit reproduzierbaren Tests.
- REQ-002: Führe echte API-Integrationstests gegen InMemory-Host ein; reine Logik-Proxy-Tests gelten nicht als Abschluss.
- REQ-003: Ersetze Placebo-Assertions in AuditCompliance durch verhaltensbasierte Assertions.
- REQ-004: Reduziere dauerhafte Skip-Tests im WPF/A11y-Bereich durch separaten Windows-UI-Job.
- SEC-001: Tool-Hash-Verifikation muss standardmaessig fail-closed sein.
- SEC-002: Quarantine-Restore darf nur in explizit erlaubte Roots wiederherstellen.
- SEC-003: DAT-Signaturpruefung darf kein sync-over-async enthalten.
- SEC-004: Insights-Winner-Berechnung darf nicht von Core-Dedupe-Logik driften.
- CON-001: Keine Aenderung darf Core-Determinismus verletzen.
- CON-002: Bestehende oeffentliche API-Signaturen nur aendern, wenn Tests und OpenAPI aktualisiert sind.
- CON-003: WPF-UI-Tests laufen nur auf Windows in dediziertem CI-Job.
- GUD-001: Kleine, isolierte Commits pro Task-Gruppe.
- GUD-002: Jeder Security-Fix hat mindestens einen Negativtest und einen Regressionstest.
- PAT-001: Eingabepfade immer rootgebunden validieren via zentralem Pfad-Guard.
- PAT-002: HTTP und IO asynchron ohne blockierende Task.Run().GetAwaiter().GetResult() Muster.

## 2. Implementation Steps

### Implementation Phase 1

- GOAL-001: API-Sicherheits- und Integrationsluecke mit echten Endpunkt-Tests schliessen.

| Task | Description | Completed | Date |
| -------- | --------------------- | --------- | ---------- |
| TASK-001 | Erstelle Datei src/RomCleanup.Tests/ApiIntegrationTests.cs mit TestServer oder WebApplicationFactory. Implementiere echte HTTP-Tests fuer GET /health, POST /runs, GET /runs/{id}, POST /runs/{id}/cancel, GET /runs/{id}/stream. | x | 2026-03-12 |
| TASK-002 | Implementiere in src/RomCleanup.Tests/ApiIntegrationTests.cs Negativtests fuer fehlenden X-Api-Key, ungültige PreferRegions, oversized Request-Body > 1MB, Symlink-Root und Systemverzeichnis-Root. | x | 2026-03-12 |
| TASK-003 | Erweitere src/RomCleanup.Tests/ApiIntegrationTests.cs um Rate-Limit-Test (429) und CORS-Preflight-OPTIONS Test mit Header-Assertions. | x | 2026-03-12 |
| TASK-004 | Ersetze in src/RomCleanup.Tests/AuditComplianceTests.cs den API-Proxy-Test Audit3_Test010 durch Aufruf der echten API-Integrationstests und entferne boolesche Nachbildung der Validierungslogik. | x | 2026-03-12 |
| TASK-005 | Validiere OpenAPI-Sicherheitskonsistenz mit src/RomCleanup.Api/OpenApiSpec.cs: Header-Name X-Api-Key und Security-Requirement muessen in Integrationstest verifiziert sein. | x | 2026-03-12 |

### Implementation Phase 2

- GOAL-002: Sicherheitsluecken in Tooling, DAT und Quarantine deterministisch schliessen.

| Task | Description | Completed | Date |
| -------- | --------------------- | --------- | ---- |
| TASK-006 | Aendere src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs: VerifyToolHash fail-closed, wenn tool-hashes.json fehlt oder Tool nicht in Allowlist ist. Erlaube Bypass nur bei explizitem allowInsecureHashBypass=true und logge Security-Warnung. | x | 2026-03-12 |
| TASK-007 | Erweitere src/RomCleanup.Tests/ToolRunnerAdapterTests.cs um Negativtests: fehlende Hash-Datei, unbekanntes Tool, manipulierte Binary-Hash, sowie Positivtest bei korrektem Hash. | x | 2026-03-12 |
| TASK-008 | Refaktoriere src/RomCleanup.Infrastructure/Dat/DatSourceService.cs: Entferne sync-over-async aus VerifyDatSignature, nutze asynchrone API-Endpunkte ohne blockierende Task.Run-Wrappers. | x | 2026-03-12 |
| TASK-009 | Fuege in src/RomCleanup.Tests/DatSourceServiceTests.cs Parallel- und Cancellation-Tests hinzu, die Deadlock-freie Verifikation und Timeout-Verhalten verifizieren. | x | 2026-03-12 |
| TASK-010 | Haerte src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs: Restore nur innerhalb erlaubter Restore-Roots; ersetze bisherigen Vergleichs-Guard durch rootgebundene Pfadaufloesung. | x | 2026-03-12 |
| TASK-011 | Erweitere src/RomCleanup.Tests/QuarantineServiceTests.cs um Traversal-, UNC-, ADS- und Mixed-Separator-Negativtests fuer Restore. | x | 2026-03-12 |

### Implementation Phase 3

- GOAL-003: Konsistenz- und Performance-Luecken in Analytics und WPF-Service schliessen.

| Task | Description | Completed | Date |
| -------- | --------------------- | --------- | ---- |
| TASK-012 | Refaktoriere src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs: Winner-Auswahl ueber zentrale Core-Logik (DeduplicationEngine) ableiten statt lokaler Score-Duplikation. | x | 2026-03-12 |
| TASK-013 | Erweitere src/RomCleanup.Tests/InsightsEngineTests.cs um Drift-Regressionstest mit mehreren Region/Format/Version-Kombinationen und identischer Winner-Erwartung zur Core-Engine. | x | 2026-03-12 |
| TASK-014 | Refaktoriere src/RomCleanup.UI.Wpf/Services/FeatureService.cs Methode RepairNesHeader auf streaming-basiertes Header-Patching ohne Full-File-ReadAllBytes. | x | 2026-03-12 |
| TASK-015 | Erweitere src/RomCleanup.Tests/AuditComplianceTests.cs und/oder neue Datei src/RomCleanup.Tests/FeatureServiceLargeFileTests.cs um Speicherprofil-Negativtest fuer grosse NES-Dateien (kein OOM, nur Header-I/O). | x | 2026-03-12 |

### Implementation Phase 4

- GOAL-004: Testqualitaets-Luecke und WPF/A11y-Skip-Luecke in CI schliessen.

| Task | Description | Completed | Date |
| -------- | --------------------- | --------- | ---- |
| TASK-016 | Ersetze in src/RomCleanup.Tests/AuditComplianceTests.cs alle Assert.True(true, ...) Platzhalter durch echte Assertions gegen produktives Verhalten oder entferne redundante Proxy-Tests. | x | 2026-03-12 |
| TASK-017 | Konvertiere in src/RomCleanup.UI.Wpf/MainWindow.xaml offene Accessibility-Luecke TASK-102: setze AutomationProperties.Name fuer alle interaktiven Controls ohne Name. | x | 2026-03-12 |
| TASK-018 | Implementiere High-Contrast-Unterstuetzung TASK-106 in src/RomCleanup.UI.Wpf/Themes/ und binde Umschaltung in src/RomCleanup.UI.Wpf/Services/ThemeService.cs ein. | x | 2026-03-12 |
| TASK-019 | Lege einen dedizierten Windows-UI-Job in .github/workflows/test-pipeline.yml an, der WPF-Skip-Tests fuer Automation/A11y als ausfuehrbare Stage aktiviert. | x | 2026-03-12 |
| TASK-020 | Schließe Governance-Restpunkte: SBOM-Job TASK-070 und optionalen Mutation-Job TASK-068 in .github/workflows/test-pipeline.yml mit klaren Gate-Flags integrieren. | x | 2026-03-12 |

## 3. Alternatives

- ALT-001: API nur mit Unit-Tests auf RunManager-Ebene testen. Verworfen, da Middleware, Header, CORS, RateLimit und SSE nicht abgedeckt werden.
- ALT-002: Tool-Hash fail-open beibehalten und nur warnen. Verworfen, da Supply-Chain-Risiko.
- ALT-003: WPF-Skip-Tests dauerhaft akzeptieren. Verworfen, da Accessibility-Regressionen unerkannt bleiben.
- ALT-004: Insights-Scoring lokal dupliziert lassen. Verworfen, da Drift-Risiko zu Core-Winner-Logik.

## 4. Dependencies

- DEP-001: Microsoft.AspNetCore.Mvc.Testing fuer API-Integrationstests.
- DEP-002: Windows-basierter CI-Runner fuer WPF/A11y-Automation.
- DEP-003: Vorhandene Testdaten in data/ und test-fixtures fuer DAT, Conversion und Path-Security.
- DEP-004: Bestehende Contracts-Schnittstellen in src/RomCleanup.Contracts/Ports fuer Mocking und InMemory-Tests.

## 5. Files

- FILE-001: src/RomCleanup.Tests/ApiIntegrationTests.cs
- FILE-002: src/RomCleanup.Tests/AuditComplianceTests.cs
- FILE-003: src/RomCleanup.Tests/ToolRunnerAdapterTests.cs
- FILE-004: src/RomCleanup.Tests/DatSourceServiceTests.cs
- FILE-005: src/RomCleanup.Tests/QuarantineServiceTests.cs
- FILE-006: src/RomCleanup.Tests/InsightsEngineTests.cs
- FILE-007: src/RomCleanup.Tests/FeatureServiceLargeFileTests.cs
- FILE-008: src/RomCleanup.Api/Program.cs
- FILE-009: src/RomCleanup.Api/OpenApiSpec.cs
- FILE-010: src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs
- FILE-011: src/RomCleanup.Infrastructure/Dat/DatSourceService.cs
- FILE-012: src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs
- FILE-013: src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs
- FILE-014: src/RomCleanup.UI.Wpf/Services/FeatureService.cs
- FILE-015: src/RomCleanup.UI.Wpf/MainWindow.xaml
- FILE-016: src/RomCleanup.UI.Wpf/Services/ThemeService.cs
- FILE-017: src/RomCleanup.UI.Wpf/Themes/SynthwaveDark.xaml
- FILE-018: src/RomCleanup.UI.Wpf/Themes/Light.xaml
- FILE-019: .github/workflows/test-pipeline.yml

## 6. Testing

- TEST-001: API Auth/CORS/RateLimit/SSE Integration Suite muss deterministisch gruen laufen.
- TEST-002: Tool-Hash-Negativtests muessen fail-closed Verhalten verifizieren.
- TEST-003: DAT-Verifikation muss unter Parallel-Last ohne Deadlock laufen.
- TEST-004: Quarantine-Restore-Traversaltests muessen alle Escape-Varianten blocken.
- TEST-005: Insights-vs-Dedupe Drift-Test muss identischen Winner liefern.
- TEST-006: RepairNesHeader Large-File-Test muss ohne Full-File-Buffering bestehen.
- TEST-007: AuditCompliance darf keine Placebo-Assertions mehr enthalten.
- TEST-008: WPF Accessibility und High-Contrast Smoke-Stage muss im Windows-Job laufen.
- TEST-009: CI Governance Stage muss SBOM und optional Mutation-Reporting erzeugen.

## 7. Risks & Assumptions

- RISK-001: Fail-closed Tool-Hash kann bestehende lokale Entwickler-Setups blockieren.
- RISK-002: WPF-Automation kann in CI flakey sein, falls Dispatcher-Synchronisation instabil ist.
- RISK-003: API-SSE Tests koennen timing-sensitiv sein und robuste Timeouts benoetigen.
- ASSUMPTION-001: Der aktuelle Branch enthaelt bereits net10.0-windows Testkonfiguration fuer UI-nahe Tests.
- ASSUMPTION-002: Team akzeptiert dedizierten Windows-UI-Job als Gate fuer Accessibility.
- ASSUMPTION-003: Sicherheitspriorisierung erlaubt Breaking-Behavior fuer unsichere Tool-Konfigurationen.

## 8. Related Specifications / Further Reading

- plan/consolidated-bug-audit.md
- plan/feature-deep-dive-bug-audit-1.md
- plan/feature-deep-dive-ux-ui-audit-2.md
- plan/feature-deep-dive-remaining-audit-3.md
- docs/TEST_STRATEGY.md
- docs/REVIEW_CHECKLIST.md