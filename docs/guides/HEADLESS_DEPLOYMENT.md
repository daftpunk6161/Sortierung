# Romulus Headless Deployment

Stand: 2026-04-01

Diese Anleitung beschreibt den produktionsnahen Betrieb der API inklusive eingebettetem Dashboard fuer NAS-, Server- und Headless-Szenarien.

## Zielbild

- Ein einzelner `Romulus.Api`-Prozess liefert API und Dashboard aus derselben Binary.
- Das Dashboard nutzt ausschliesslich bestehende API-Endpunkte plus die Read-Modelle `/dashboard/bootstrap` und `/dashboard/summary`.
- Nicht-Loopback-Betrieb ist nur mit expliziter Remote-Freigabe, HTTPS-`PublicBaseUrl`, API-Key und `AllowedRoots` erlaubt.

## Pflichtkonfiguration fuer Remote-Betrieb

| Schluessel | Pflicht | Zweck |
|---|---|---|
| `ROM_CLEANUP_API_KEY` oder `ApiKey` | ja | Auth fuer alle nicht-anonymen Endpunkte |
| `AllowRemoteClients=true` | ja | aktiviert Headless-/Remote-Modus |
| `PublicBaseUrl=https://...` | ja | Herkunft fuer HTTPS- und CORS-Validierung |
| `BindAddress=0.0.0.0` | ja | damit der Container/Server erreichbar ist |
| `AllowedRoots__0..N` | ja | harte Positivliste fuer Roots, Exporte, Rollbacks und DAT-Pfade |
| `DashboardEnabled=true` | empfohlen | liefert `/dashboard/` aus |
| `TrustForwardedFor=false` | empfohlen | nur auf `true`, wenn der Reverse Proxy voll kontrolliert ist |

Ohne diese Kombination startet die API im Remote-Modus nicht.

## Docker-Betrieb

Die Beispielartefakte liegen unter [deploy/docker](../../deploy/docker/).

### Build

```powershell
docker build -f deploy/docker/api/Dockerfile -t romulus-api:local .
```

### Compose

```powershell
$env:ROMULUS_API_KEY="replace-with-strong-secret"
$env:ROMULUS_PUBLIC_BASE_URL="https://romulus.example.com"
docker compose -f deploy/docker/docker-compose.headless.yml up -d --build
```

Der Beispiel-Compose-Stack setzt absichtlich diese Defaults:

- Host-Port nur auf `127.0.0.1:7878`
- ROM-Quelle read-only
- eigener persistenter Volume fuer `collection.db`
- eigener persistenter Volume fuer Profile/Settings unter `Romulus`
- read-only Container-Filesystem plus `tmpfs` fuer temporaere Artefakte

## Reverse Proxy / HTTPS

- TLS-Termination gehoert vor die API.
- `PublicBaseUrl` muss auf die externe HTTPS-URL zeigen.
- Das Beispiel-[Caddyfile](../../deploy/docker/caddy/Caddyfile) proxyt direkt auf `romulus-api:7878`.
- `TrustForwardedFor` bleibt standardmaessig `false`; nur aktivieren, wenn der Proxy lokal und unter eigener Kontrolle laeuft.

## Pfade und Volumes

Empfohlene Host-Pfade:

- ROMs: `/srv/romulus/roms`
- Exporte/Reports: `/srv/romulus/exports`
- Index: Docker-Volume `romulus_index` -> `/app/.romulus`
- Profile/Settings: Docker-Volume `romulus_profiles` -> `/root/.config/Romulus`

Wichtig:

- `AllowedRoots` muss nur die Pfade enthalten, auf denen Romulus wirklich arbeiten darf.
- Ein Pfad ausserhalb von `AllowedRoots` wird fuer Run-Start, Export, DAT-Import, Rollback und Standalone-Convert blockiert.
- Reparse-/Symlink-Pfade werden weiterhin nicht transparent akzeptiert.

## Dashboard

Anonyme Endpunkte:

- `GET /healthz`
- `GET /dashboard/bootstrap`

Authentifizierte Dashboard-Flows:

- `GET /dashboard/summary`
- `GET /runs`, `POST /runs`, `POST /runs/{id}/cancel`, `GET /runs/{id}/stream`
- `GET /runs/{id}/review`, `POST /runs/{id}/review/approve`
- `GET /dats/status`, `POST /dats/update`
- `GET /runs/{id}/completeness`

API-Key-Weitergabe erfolgt ueber `X-Api-Key`.

## ECM / NKit im Headless-Betrieb

- NKit ist ueber den erwarteten SHA256 des `NKitProcessingApp.exe` in der Conversion-Registry gepinnt.
- ECM bleibt fail-closed, bis fuer `unecm.exe` ein erwarteter Hash in [tool-hashes.json](../../data/tool-hashes.json) oder in einer kontrollierten Operator-Variante hinterlegt ist.
- Review-pflichtige Conversion-Plans bleiben ohne explizites `approveConversionReview` blockiert.
- Quellen werden weiterhin nie vor erfolgreicher Verifikation entfernt.

## Betriebschecks

### Liveness

```powershell
curl http://127.0.0.1:7878/healthz
```

### Authentifizierte Zusammenfassung

```powershell
curl `
  -H "X-Api-Key: $env:ROMULUS_API_KEY" `
  http://127.0.0.1:7878/dashboard/summary
```

### Dashboard

Rufe `https://romulus.example.com/dashboard/` im Browser auf und hinterlege dort den API-Key.

## Reproduzierbarer Headless Smoke

Fuer den dokumentierten Betriebsmodus liegt ein echter Smoke unter [deploy/smoke/Invoke-HeadlessSmoke.ps1](../../deploy/smoke/Invoke-HeadlessSmoke.ps1).

Lokaler Durchlauf:

```powershell
pwsh -NoProfile -File deploy/smoke/Invoke-HeadlessSmoke.ps1 -Configuration Release
```

Der Smoke startet die gebaute API mit isoliertem `APPDATA`/`LOCALAPPDATA` und prueft:

- `healthz`
- anonymes Dashboard-Bootstrap
- Dashboard-Shell
- authentifizierte Dashboard-Zusammenfassung
- `AllowedRoots`-Block fuer `/runs`
- `AllowedRoots`-Block fuer `/convert`

Damit bleibt der Headless-Pfad nicht nur dokumentiert, sondern vor Release praktisch verifizierbar.

## Was bewusst nicht getan werden sollte

- Kein Direkt-Expose der API ohne HTTPS.
- Keine Wildcard-`AllowedRoots`.
- Kein `TrustForwardedFor=true` hinter untrusted Netzpfaden.
- Keine unpinned Community-Tools produktiv freischalten.
