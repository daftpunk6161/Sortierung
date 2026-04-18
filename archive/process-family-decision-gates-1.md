---
goal: Kalibrierung der Family-Decision-Gates auf DAT-verifizierbare Benchmarks
version: 1.0
date_created: 2026-04-05
last_updated: 2026-04-05
owner: Romulus Core Team
status: Planned
tags: [process, benchmark, quality-gate, recognition, dat]
---

# Introduction

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Dieser Plan definiert eine schrittweise, deterministische Kalibrierung der Family-Decision-Gates, um die aktuell auf 0.00 gesetzten `minDatVerifiedRate`-Werte wieder kontrolliert anzuheben, ohne False Positives oder Unknown-Rate zu verschlechtern.

## 1. Requirements & Constraints

- **REQ-001**: Jede Erhoehung von `minDatVerifiedRate` muss durch reproduzierbare Benchmark-Ergebnisse je Family begruendet sein.
- **REQ-002**: Gate-Aenderungen duerfen nur erfolgen, wenn `maxFalsePositiveRate` und `maxUnknownRate` weiterhin eingehalten werden.
- **REQ-003**: Alle Entscheidungen muessen in Build + Test + Benchmark nachvollziehbar sein.
- **SEC-001**: Keine Umgehung bestehender Sicherheits- und Safety-Gates (Path/Safety/Determinismus).
- **CON-001**: Keine stille Verhaltensaenderung in produktionskritischen Pfaden ohne passende Tests.
- **CON-002**: Preview/Execute/Report-Paritaet darf nicht verletzt werden.
- **GUD-001**: DAT-first konservativ beibehalten; unsichere Heuristik darf nicht als DatVerified klassifiziert werden.
- **PAT-001**: Änderungen in kleinen ratchet-Schritten (pro Family separat, nicht global).

## 2. Implementation Steps

### Implementation Phase 1

- GOAL-001: Reproduzierbare Baseline fuer alle Families herstellen und Defizite pro Family explizit messen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | In [benchmark/gates.json](../benchmark/gates.json) aktuelle Baseline-Werte dokumentieren und Comment-Note zur Ratchet-Strategie ergaenzen. |  |  |
| TASK-002 | In [src/Romulus.Tests/Benchmark/FamilyDecisionGateTests.cs](../src/Romulus.Tests/Benchmark/FamilyDecisionGateTests.cs) zusaetzliche Ausgabe fuer DatVerified-Zaehler, Totals und Rate je Family standardisieren. |  |  |
| TASK-003 | Einen reproduzierbaren Testlauf mit `dotnet test --filter FullyQualifiedName~FamilyDecisionGateTests` ausfuehren und Ausgabe in `benchmark/reports/` als Referenz ablegen. |  |  |

### Implementation Phase 2

- GOAL-002: DatVerified-Defizite datengetrieben reduzieren (zuerst Datenqualitaet, dann Pipeline-Feinjustierung).

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-004 | Pro Family eine Defizitliste erzeugen: Samples mit erwarteter DAT-Abdeckung aber ohne DatVerified klassifizieren (Arcade, ComputerTOSEC, Hybrid priorisiert). |  |  |
| TASK-005 | Benchmark-Datensaetze in [benchmark/ground-truth/](../benchmark/ground-truth/) und [benchmark/dats/](../benchmark/dats/) gezielt ergaenzen, sodass pro Family mindestens 20 eindeutig DAT-verifizierbare Referenzen vorliegen. |  |  |
| TASK-006 | Name-only/Hash-Policy-Differenzen gegen [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](../src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs) und Family-Strategien in [src/Romulus.Infrastructure/Dat/](../src/Romulus.Infrastructure/Dat/) validieren und nur bei nachweisbarem Fehlmapping anpassen. |  |  |

### Implementation Phase 3

- GOAL-003: Family-Gates kontrolliert anheben (Ratchet), ohne Regressionsrisiko.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-007 | Ratchet-Stufe A setzen: `minDatVerifiedRate` je Family von `0.00` auf `0.01` anheben, Test+Build laufen lassen, Ergebnis protokollieren. |  |  |
| TASK-008 | Bei gruenem Lauf Ratchet-Stufe B setzen: selektiv je Family auf `0.02` bis `0.05` erhoehen, nur wenn 3 aufeinanderfolgende Runs stabil sind. |  |  |
| TASK-009 | Bei Fehlschlag auto-rollback auf letzte stabile Schwelle; Ursache als Defizit-Eintrag mit Family, Sample-Anteil und Root Cause dokumentieren. |  |  |

### Implementation Phase 4

- GOAL-004: Dauerhafte Absicherung in CI und Release-Hygiene.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-010 | CI-Pipeline um Family-Gate-Auswertung erweitern (harte Fehlermeldung inkl. Family, Ist/Soll-Wert, Delta). |  |  |
| TASK-011 | Regressionstests fuer Family-Konflikte und DAT-Policies finalisieren: [src/Romulus.Tests/EnrichmentFamilyIntegrationTests.cs](../src/Romulus.Tests/EnrichmentFamilyIntegrationTests.cs), [src/Romulus.Tests/FamilyDatStrategyTests.cs](../src/Romulus.Tests/FamilyDatStrategyTests.cs), [src/Romulus.Tests/FamilyPipelineSelectorTests.cs](../src/Romulus.Tests/FamilyPipelineSelectorTests.cs). |  |  |
| TASK-012 | Ergebnisbericht in [docs/audits/](../docs/audits/) erstellen: erreichte Schwellen, Restdefizite, naechste Ratchet-Stufe. |  |  |

## 3. Alternatives

- **ALT-001**: Sofortige Anhebung aller Families auf hohe Zielwerte (z. B. 0.10+) wurde verworfen, da dies bei aktuellem Korpus nicht stabil ist.
- **ALT-002**: Dauerhaft `minDatVerifiedRate=0.00` wurde verworfen, da damit keine qualitative Verbesserung erzwungen wird.
- **ALT-003**: Nur False-Positive/Unknown-Gates ohne DatVerified-Gate wurde verworfen, da DAT-Abdeckung sonst nicht aktiv verbessert wird.

## 4. Dependencies

- **DEP-001**: Benchmark-Datensaetze in [benchmark/ground-truth/](../benchmark/ground-truth/) muessen konsistent gepflegt sein.
- **DEP-002**: Family-Gate-Test in [src/Romulus.Tests/Benchmark/FamilyDecisionGateTests.cs](../src/Romulus.Tests/Benchmark/FamilyDecisionGateTests.cs) muss stabil reproduzierbar laufen.
- **DEP-003**: DAT-Policy-Implementierungen in [src/Romulus.Infrastructure/Dat/](../src/Romulus.Infrastructure/Dat/) bleiben Single Source of Truth.

## 5. Files

- **FILE-001**: [benchmark/gates.json](../benchmark/gates.json) – Family-Grenzwerte und Ratchet-Stufen.
- **FILE-002**: [src/Romulus.Tests/Benchmark/FamilyDecisionGateTests.cs](../src/Romulus.Tests/Benchmark/FamilyDecisionGateTests.cs) – Gate-Auswertung je Family.
- **FILE-003**: [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](../src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs) – DAT-/Detector-Interaktion fuer DatVerified-Pfade.
- **FILE-004**: [src/Romulus.Infrastructure/Dat/FamilyDatStrategyResolver.cs](../src/Romulus.Infrastructure/Dat/FamilyDatStrategyResolver.cs) – Family-Policy-Resolution.
- **FILE-005**: [src/Romulus.Tests/EnrichmentFamilyIntegrationTests.cs](../src/Romulus.Tests/EnrichmentFamilyIntegrationTests.cs) – End-to-End-Absicherung Family-Konflikte.

## 6. Testing

- **TEST-001**: `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --filter FullyQualifiedName~FamilyDecisionGateTests.FamilyDecisionGates_MeetThresholdsFromGatesJson`
- **TEST-002**: `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --filter FullyQualifiedName~EnrichmentFamilyIntegrationTests`
- **TEST-003**: `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --filter FullyQualifiedName~FamilyDatStrategyTests`
- **TEST-004**: `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --filter FullyQualifiedName~FamilyPipelineSelectorTests`
- **TEST-005**: `dotnet build src/Romulus.sln`

## 7. Risks & Assumptions

- **RISK-001**: DatVerified steigt pro Family unterschiedlich schnell; globale Zielwerte koennen instabil werden.
- **RISK-002**: Datenqualitaet (fehlende/inkonsistente DAT-Referenzen) limitiert Gate-Anhebung trotz korrekter Pipeline.
- **RISK-003**: Zu aggressive Policy-Aenderungen koennen False Positives erhoehen.
- **ASSUMPTION-001**: Der aktuelle Benchmark-Korpus ist repraesentativ genug fuer ratchet-basierte Erhoehungen.
- **ASSUMPTION-002**: Family-Konfliktregeln (CrossFamily -> Blocked) bleiben unveraendert.

## 8. Related Specifications / Further Reading

[ADR-0021 DAT-First Conservative Recognition Architecture](../docs/adrs/0021-dat-first-conservative-recognition-architecture.md)
[Romulus Projektregeln](../.claude/rules/project.instructions.md)
[Romulus Testregeln](../.claude/rules/testing.instructions.md)
