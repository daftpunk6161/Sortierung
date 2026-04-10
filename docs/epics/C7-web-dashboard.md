# C7: Web-Dashboard

Status: geliefert am 2026-04-01

## Problem

Die GUI ist Windows-only (WPF). NAS-Benutzer und Linux/Mac-Nutzer haben keinen
Zugang zu einer visuellen Oberflaeche. Die API existiert bereits, hat aber kein Frontend.

## Loesungsansatz

Leichtgewichtiges Web-Frontend ueber die bestehende REST-API.
Docker-kompatibel fuer NAS-Deployment.

### Umgesetzte Architektur

**Embedded Static Files**
- Vanilla HTML/CSS/JS oder Preact/Alpine.js
- Statische Dateien unter `src/Romulus.Api/wwwroot/dashboard`
- Kein separater Build-Step, kein Node.js noetig
- Dashboard nutzt nur bestehende API-Endpunkte plus:
  - `GET /dashboard/bootstrap`
  - `GET /dashboard/summary`
- Keine serverseitige KPI- oder Status-Schattenlogik ausserhalb der bestehenden Run-/Index-/DAT-Wahrheit

### Feature-Scope (MVP)

1. **Dashboard** — Sammlungsueberblick
   - letzte Runs, aktiver Run, Trend- und DAT-Zustand
   - Bootstrap fuer AllowedRoots, Remote-Flags und Dashboard-Pfad

2. **Run-Management** — Runs starten/ueberwachen
   - Konfiguration (Roots, Mode, Options)
   - Live-Progress via SSE
   - Ergebnis-Ansicht nach Completion

3. **DAT-Status** — DAT-Verwaltung
   - Status-Uebersicht (via `GET /dats/status`)
   - Update-Trigger (via `POST /dats/update`)

4. **Review-Queue** — Review-Items prüfen und genehmigen
   - Filtert nach Console/MatchLevel
   - Batch-Approve

5. **Completeness** — Pro-Console Completeness
   - Tabelle mit Fortschrittsbalken
   - Missing-Games-Liste

### Deployment-Stand

- Dockerfile: [deploy/docker/api/Dockerfile](../../deploy/docker/api/Dockerfile)
- Compose-Beispiel: [deploy/docker/docker-compose.headless.yml](../../deploy/docker/docker-compose.headless.yml)
- Reverse-Proxy-Beispiel: [deploy/docker/caddy/Caddyfile](../../deploy/docker/caddy/Caddyfile)
- Betriebsleitfaden: [docs/guides/HEADLESS_DEPLOYMENT.md](../guides/HEADLESS_DEPLOYMENT.md)

### Sicherheit

- HTTPS-Terminierung via Reverse Proxy (nginx/Caddy/Traefik)
- API-Key wird ueber den Dashboard-Connect-Flow gesetzt und nie anonym umgangen
- CORS wird im Remote-Modus aus `PublicBaseUrl` abgeleitet
- `AllowedRoots` erzwingt Root-Containment auch fuer Export, DAT, Convert und Rollback
- Rate-Limiting bleibt aktiv

### API-Erweiterungen

- `GET /dashboard/summary` — Aggregierte Sammlungsdaten
- `GET /dashboard/bootstrap` — anonyme Dashboard-Metadaten
- Bestehende Endpoints tragen Run-, Review-, DAT- und Completeness-Flows

## Abhaengigkeiten

- Stabile API (alle B1-B4 Features implementiert)
- Optional: C1 (Persistenter Index) fuer Dashboard-Daten ohne Run

## Risiken

- Frontend-Wartung und Aktualisierung parallel zum Backend
- API-Aenderungen muessen Frontend-kompatibel bleiben
- Docker: Pfad-Mapping und Berechtigungen auf NAS-Systemen
- Non-loopback Binding erfordert HTTPS → Dokumentation noetig

## Teststand

- `ApiReachIntegrationTests`
- `OpenApiReachTests`
- Vollsuite auf Stand 2026-04-01
