# Romulus — API- und Schema-Versionpolicy

Stand: 2026-03-11

## 1) API-Versionierung

- Aktive Hauptversion: `v1`.
- Implementierung: `src/Romulus.Api/Program.cs` (ASP.NET Core Minimal API).
- Kompatibilität:
  - Bestehende MVP-Routen bleiben kompatibel (`/health`, `/runs`, ...).
- Breaking Changes:
  - Nur mit neuer Hauptversion.
  - `v1` erhält nur additive Felder oder Bugfixes ohne Contract-Bruch.

## 2) OpenAPI-Contract Governance

- Quelle: `docs/openapi.yaml`.
- Embedded Spec: `src/Romulus.Api/OpenApiSpec.cs` (JSON, ausgeliefert unter `/openapi`).
- Beide Specs müssen synchron gehalten werden.

## 3) Config-Schema-Versionen & Migration

- Alle persistierten Artefakte führen `schemaVersion`.
- Migrationsprinzip:
  1. Ältere Version lesen.
  2. In-Memory auf aktuelle Version transformieren.
  3. Beim nächsten Persist auf aktuelle `schemaVersion` schreiben.
- Rückwärtskompatibilität:
  - Nicht unterstützte Altversionen liefern klaren Fehler + Handlungshinweis.

## 4) SemVer-Regeln

- API-/Contract-Artefakte folgen `MAJOR.MINOR.PATCH`.
- MAJOR: inkompatible Änderung.
- MINOR: additive, rückwärtskompatible Erweiterung.
- PATCH: Bugfix ohne Contract-Änderung.
