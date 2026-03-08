# API- und Schema-Versionpolicy

Stand: 2026-03-03

## 1) API-Versionierung

- Aktive Hauptversion: `v1`.
- Kompatibilität:
  - Bestehende MVP-Routen bleiben kompatibel (`/health`, `/runs`, ...).
  - Versionierte Aliase sind gültig (`/v1/health`, `/v1/runs`, ...).
- Breaking Changes:
  - Nur mit neuer Hauptversion (`/v2/...`).
  - `v1` erhält nur additive Felder oder bugfixes ohne Contract-Bruch.

## 2) OpenAPI-Contract Governance

- Quelle: `docs/openapi.yaml`.
- Generiertes Artefakt: `docs/openapi.generated.json`.
- CI-Regel:
  - Test `dev/tests/Api.OpenApiDrift.Tests.ps1` validiert Versionsheader und MVP-Endpoints.
  - Bei Drift schlägt die Unit-Pipeline fehl.

## 3) Config-Schema-Versionen & Migration

- Alle persistierten Artefakte führen `schemaVersion`.
- Migrationsprinzip:
  1. Ältere Version lesen.
  2. In-Memory auf aktuelle Version transformieren.
  3. Beim nächsten Persist auf aktuelle `schemaVersion` schreiben.
- Rückwärtskompatibilität:
  - Nicht unterstützte Altversionen liefern klaren Fehler + Handlungshinweis.

## 4) Profile als First-Class Entity

Ein Profil enthält mindestens:
- `id`
- `schemaVersion`
- `version`
- `capabilities`
- `provenance` (Quelle/Herkunft)
- `checksum` (Integritätsnachweis)

## 5) SemVer-Regeln

- API-/Contract-Artefakte folgen `MAJOR.MINOR.PATCH`.
- MAJOR: inkompatible Änderung.
- MINOR: additive, rückwärtskompatible Erweiterung.
- PATCH: Bugfix ohne Contract-Änderung.
