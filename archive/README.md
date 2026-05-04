# archive/

Dieses Verzeichnis enthaelt **nur** explizit per ADR archivierte Spike-Codes
oder historische Referenzen mit Reaktivierungspfad.

## Aktiver Inhalt

- [`avalonia-spike/`](avalonia-spike/) — bewusst archivierter Avalonia-GUI-Spike.
  Reaktivierungs-Bedingungen sind in `docs/plan/strategic-reduction-2026/plan.yaml`
  unter `resolved_decisions` (Avalonia-vs-WPF-ADR) dokumentiert.

## Geloeschter Inhalt

Am 2026-05-04 wurden folgende Inhalte aus `archive/` entfernt:

- 30+ lose Audit-/Findings-Tracker-/Roadmap-Dokumente (Pre-Reduktions-Aera)
- `archive/audits/`, `archive/completed/`, `archive/legacy/`
- `archive/powershell/` (PowerShell-Legacy-Pipeline; Tests laufen jetzt
  ueber `dotnet test`, siehe `.vscode/tasks.json`)
- `archive/RomCleanup.*/` (Pre-Rename-Projektreste)

Vollstaendiger Pre-Cleanup-Snapshot liegt im Git-Tag
`archive-snapshot-pre-cleanup-2026-05-04`. Bei Bedarf wieder einsehbar via
`git show archive-snapshot-pre-cleanup-2026-05-04 -- archive/...`.

## Regeln

- Keine neuen losen Audit-/Findings-Dokumente in `archive/`.
- Neue Eintraege brauchen einen ADR mit Reaktivierungs-Bedingung.
- Identitaets-Guardrail (`AGENTS.md`) gilt auch hier.
