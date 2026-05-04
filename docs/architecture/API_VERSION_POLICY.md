# Romulus — API- und Schema-Versionpolicy

Stand: 2026-05-03

## 1) API-Versionierung

- Aktive Hauptversion: `v1-experimental`.
- Implementierung: `src/Romulus.Api/Program.cs` (ASP.NET Core Minimal API).
- Kompatibilität:
  - Produktive Consumer verwenden ausschliesslich den Prefix `/v1-experimental/`.
  - Legacy-Routen bleiben aktuell als technische Kompatibilitaets-Aliase erreichbar, sind aber nicht der dokumentierte Contract.
- Stabilitaets-Pins:
  - HTTP-Header `X-Romulus-API-Status: experimental`.
  - OpenAPI-Dokument und Operationen enthalten `x-stability: experimental`.
  - Der dokumentierte Pfad-Prefix ist `/v1-experimental/`; OpenAPI `servers[0].url` bleibt die lokale Base-URL `http://127.0.0.1:7878`.
- Breaking Changes:
  - Nur mit neuer Hauptversion.
  - `v1-experimental` erhält nur additive Felder oder Bugfixes ohne Contract-Bruch, solange der strategische Reduktionsplan diesen Status festlegt.

## 2) OpenAPI-Contract Governance

- Quelle: `docs/architecture/openapi.yaml`.
- Embedded Spec: `src/Romulus.Api/OpenApiSpec.cs` (JSON, dokumentiert unter `/v1-experimental/openapi`; Legacy-Alias `/openapi` bleibt erreichbar).
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
