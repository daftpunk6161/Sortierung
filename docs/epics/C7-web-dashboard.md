# C7: Web-Dashboard

## Problem

Die GUI ist Windows-only (WPF). NAS-Benutzer und Linux/Mac-Nutzer haben keinen
Zugang zu einer visuellen Oberflaeche. Die API existiert bereits, hat aber kein Frontend.

## Loesungsansatz

Leichtgewichtiges Web-Frontend ueber die bestehende REST-API.
Docker-kompatibel fuer NAS-Deployment.

### Technologie-Entscheidung

**Option A: Embedded Static Files (empfohlen)**
- Vanilla HTML/CSS/JS oder Preact/Alpine.js
- Statische Dateien eingebettet in ASP.NET Binary
- Kein separater Build-Step, kein Node.js noetig
- Kleine Bundle-Size (< 500 KB)

**Option B: Separate SPA**
- React/Vue/Svelte
- Separater Build-Step
- Groessere Bundle, mehr Flexibilitaet

### Feature-Scope (MVP)

1. **Dashboard** — Sammlungsueberblick
   - Health-Score, Storage-Nutzung, letzte Runs
   - Console-Verteilung (Chart)

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

### Docker-Setup

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY publish/ /app/
WORKDIR /app
EXPOSE 7878
ENTRYPOINT ["dotnet", "RomCleanup.Api.dll"]
```

```yaml
# docker-compose.yml
services:
  romulus:
    image: romulus:latest
    ports:
      - "7878:7878"
    volumes:
      - /mnt/roms:/data/roms:ro
      - /mnt/config:/app/config
    environment:
      - ROM_CLEANUP_API_KEY=your-key-here
      - BindAddress=0.0.0.0
```

### Sicherheit

- HTTPS-Terminierung via Reverse Proxy (nginx/Caddy/Traefik)
- API-Key wird ueber UI-Login-Flow gesetzt
- CORS auf Dashboard-Origin einschraenken
- Rate-Limiting bleibt aktiv

### API-Erweiterungen

- `GET /dashboard/summary` — Aggregierte Sammlungsdaten
- `GET /dashboard/charts/consoles` — Console-Verteilungsdaten
- Bestehende Endpoints reichen fuer MVP aus

## Abhaengigkeiten

- Stabile API (alle B1-B4 Features implementiert)
- Optional: C1 (Persistenter Index) fuer Dashboard-Daten ohne Run

## Risiken

- Frontend-Wartung und Aktualisierung parallel zum Backend
- API-Aenderungen muessen Frontend-kompatibel bleiben
- Docker: Pfad-Mapping und Berechtigungen auf NAS-Systemen
- Non-loopback Binding erfordert HTTPS → Dokumentation noetig

## Testplan

- Unit: API-Endpunkt-Tests (bereits vorhanden)
- Integration: Docker-Build → Start → API-Zugriff
- E2E: Browser-Tests fuer kritische Flows
- Edge: Langsame Verbindungen, SSE-Reconnect, API-Key-Rotation
