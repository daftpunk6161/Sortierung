# R3 Reach Execution

Stand: 2026-04-01

Ziel: Die bestehende lokale Plattform sicher auf Headless-, NAS- und Non-Windows-Szenarien ausdehnen, ohne die Kerninvarianten oder den Safety-Standard zu verlieren.

## Nicht-Scope

- Kein Ersatz der bestehenden WPF-GUI
- Kein Cloud-Backend
- Keine Conversion-Pfade ohne Verify-, Review- und Rollback-Vertrag

## Tickets

### [x] R3-T01 Dashboard-Architektur und Sicherheitsrahmen festziehen

Ziel: Vor UI-Implementierung klar definieren, welche API-Contracts, Auth- und Deployment-Regeln gelten.

Betroffene Bereiche:
- `docs/epics/C7-web-dashboard.md`
- `src/Romulus.Api`
- `docs/architecture/openapi.yaml`

Akzeptanz:
- Dashboard nutzt ausschliesslich die bestehende API als fachliche Quelle
- Bindings, API-Key-Flows und CORS-Regeln sind fuer Headless-Betrieb definiert
- Keine neue Server-seitige Schattenlogik fuer KPIs oder Status

Abhaengigkeiten:
- R1 abgeschlossen

Stand:
- `HeadlessApiOptions` erzwingt fuer Remote-Betrieb API-Key, HTTPS-`PublicBaseUrl`, `AllowRemoteClients` und `AllowedRoots`
- Dashboard bleibt an die bestehende API gebunden; Read-Modelle sind auf `bootstrap` und `summary` begrenzt
- Root-Containment wird fuer Run, Export, DAT, Convert und Rollback zentral geprueft

### [x] R3-T02 Embedded Dashboard Shell und Auth-Bootstrap umsetzen

Ziel: Ein minimales, deploybares Frontend bereitstellen, das ohne zweiten Produkt-Stack auskommt.

Betroffene Bereiche:
- `src/Romulus.Api`
- `src/Romulus.Api/wwwroot` oder aequivalenter Static-File-Bereich

Akzeptanz:
- Dashboard-Shell ist ohne Node-Zwang buildbar
- API-Key-Initialisierung und Session-Bootstrap sind nachvollziehbar
- Basisnavigation fuer Dashboard, Runs und DAT-Status existiert

Abhaengigkeiten:
- R3-T01

Stand:
- Embedded Static Files unter `src/Romulus.Api/wwwroot/dashboard`
- anonymer Bootstrap via `/dashboard/bootstrap`
- API-Key-Connect-Flow, Run-Liste, DAT-Status und Detail-Panels ohne zweiten Frontend-Stack

### [x] R3-T03 Run-Management und Live-Progress ueber API anbinden

Ziel: Runs im Browser starten, beobachten und auswerten koennen.

Betroffene Bereiche:
- `src/Romulus.Api`
- Dashboard-Frontend

Akzeptanz:
- Start, Verlauf, Abbruch und Ergebnisanzeige laufen ueber bestehende Run-Endpunkte und SSE
- Dashboard zeigt denselben Run-Status wie CLI und GUI
- Fehler- und Cancel-Zustaende sind sichtbar und konsistent

Abhaengigkeiten:
- R3-T02

Stand:
- Dashboard startet Runs, zeigt Verlauf und nutzt `/runs/{id}/stream` fuer Live-Progress
- Run-Details, Cancel und Ergebnisdarstellung basieren auf bestehenden Run-Endpunkten
- API-/CLI-/GUI-Paritaet bleibt ueber dieselben RunModelle gewahrt

### [x] R3-T04 DAT-Status, Review-Queue und Completeness im Dashboard bereitstellen

Ziel: Die wichtigsten operativen Oberflaechen fuer Headless-Nutzung verfuegbar machen.

Betroffene Bereiche:
- `src/Romulus.Api`
- Dashboard-Frontend

Akzeptanz:
- DAT-Status und DAT-Update sind im Dashboard verfuegbar
- Review-Queue und Approvals nutzen denselben Review-Store wie andere Kanaele
- Completeness basiert auf derselben zentralen Datenbasis wie Reports

Abhaengigkeiten:
- R1-T05
- R1-T06
- R3-T03

Stand:
- Dashboard-Read-Model zeigt DAT-Status, Review-Queue und Completeness aus den bestehenden API-Pfaden
- Approvals laufen ueber denselben persistierten Review-Store wie GUI und CLI
- keine separate Dashboard-Berechnungslogik fuer Completeness oder Review

### [x] R3-T05 Headless-Paketierung und Deployment-Guides bereitstellen

Ziel: Sicheren Betrieb auf NAS, Servern und ueber Reverse Proxy reproduzierbar machen.

Betroffene Bereiche:
- `docs/guides`
- `docs/architecture`
- Deployment-Artefakte im Repo

Akzeptanz:
- Reverse-Proxy-, HTTPS- und Bind-Address-Empfehlungen sind dokumentiert
- Docker- oder aequivalente Paketierung ist reproduzierbar beschrieben
- Betriebsgrenzen fuer lokale Pfade, Volumes und Secrets sind klar dokumentiert

Abhaengigkeiten:
- R3-T01

Stand:
- Dockerfile, Compose-Stack und Caddy-Beispiel liegen unter `deploy/docker`
- [HEADLESS_DEPLOYMENT.md](../docs/guides/HEADLESS_DEPLOYMENT.md) dokumentiert Volumes, AllowedRoots, Reverse Proxy und Betriebschecks
- Container-Image nutzt `.portable` fuer einen klaren persistenten Indexpfad

### [x] R3-T06 Headless-spezifische API-Haertung abschliessen

Ziel: Den sicheren Betrieb ausserhalb von Loopback-Only sauber absichern.

Betroffene Bereiche:
- `src/Romulus.Api`
- `src/Romulus.Infrastructure/Safety`

Akzeptanz:
- Nicht-Loopback-Betrieb ist explizit konfigurationspflichtig und abgesichert
- Rate-Limits, Request-Groessen, Pfadvalidierung und Logging sind fuer Headless-Betrieb geprueft
- Keine Gefahr, ausserhalb erlaubter Roots zu operieren

Abhaengigkeiten:
- R3-T01

Stand:
- Nicht-Loopback-Betrieb ist explizit opt-in
- Root-Allowlist wird fuer mutierende Pfade und Downloads durchgezogen
- OpenAPI und API-Integration decken Remote-/AllowedRoots-Regeln mit Negativfaellen ab

### [x] R3-T07 ECM- und NKit-Sicherheitsmodell sowie Tooling vorbereiten

Ziel: Vor jeder Format-Erweiterung die Sicherheits-, Verifikations- und Tool-Vertraege festziehen.

Betroffene Bereiche:
- `docs/epics/C4-ecm-nkit-format-support.md`
- `src/Romulus.Infrastructure/Conversion`
- `data/tool-hashes.json`
- `data/conversion-registry.json`

Akzeptanz:
- Neue Tools sind ueber Hash-Pinning, Timeout, Exit-Code und Cleanup abgesichert
- Sicherheitsklassifikation fuer lossy, rebuild- oder review-pflichtige Pfade ist dokumentiert
- Kein neuer Conversion-Pfad ohne explizite Risiko- und Rollback-Regel

Abhaengigkeiten:
- R1 abgeschlossen

Stand:
- `EcmInvoker` und `NkitInvoker` sind an den zentralen ToolRunner mit Timeout, Exit-Code und Hash-Pruefung angebunden
- `NKitProcessingApp.exe` ist gepinnt; `unecm.exe` bleibt fail-closed ohne hinterlegten Hash
- Compound Extensions `.nkit.iso` und `.nkit.gcz` werden zentral erkannt

### [x] R3-T08 ECM- und NKit-Pipeline mit Review, Verify und Rollback integrieren

Ziel: Nur kontrollierte, nachvollziehbare Erweiterungen in die Conversion-Pipeline aufnehmen.

Betroffene Bereiche:
- `src/Romulus.Core/Conversion`
- `src/Romulus.Infrastructure/Conversion`
- `src/Romulus.Infrastructure/Orchestration`
- `src/Romulus.Infrastructure/Audit`

Akzeptanz:
- Neue Conversion-Pfade laufen durch Preview, Review, Execute, Verify und Rollback
- Fehlerhafte Zwischenprodukte werden sauber bereinigt
- Quellmaterial wird nie vor erfolgreicher Verifikation entfernt

Abhaengigkeiten:
- R3-T07

Stand:
- NKit/ECM laufen ueber `ConversionPlanner`/`ConversionExecutor` in Preview, Execute und Report
- review-pflichtige Conversion-Plans bleiben ohne explizite Freigabe blockiert
- Verify, Intermediate-Cleanup und Source-Schutz bleiben auf der bestehenden Conversion-Pipeline

### [x] R3-T09 Integrations- und Negativtests fuer Headless und Conversion-Erweiterung vervollstaendigen

Ziel: Die Reichweiten-Erweiterung gegen Deployment-, Security- und Datenintegritaetsfehler absichern.

Betroffene Bereiche:
- `src/Romulus.Tests`
- `docs/architecture/TEST_STRATEGY.md`

Akzeptanz:
- Tests decken Dashboard-API-Flows, Headless-Konfiguration und Conversion-Fehler-Cleanup ab
- Negative Faelle fuer Netzbetrieb, ungueltige Bindings und defekte Conversion-Outputs sind enthalten
- Safety-Invarianten bleiben fuer neue Betriebsmodi gruen

Abhaengigkeiten:
- R3-T04
- R3-T06
- R3-T08

Stand:
- `ApiReachIntegrationTests` deckt Dashboard-, AllowedRoots- und Remote-Startup-Pfade ab
- `OpenApiReachTests` sichert Reach-Vertraege im Spec
- `ReachConversionTests` und `ReachInvokerTests` decken Compound-NKit, Review-Blockierung und Tool-Aufrufe ab

## Release-Exit

- [x] Web-Dashboard nutzt ausschliesslich die bestehende API als fachliche Quelle
- [x] Headless-Betrieb ist dokumentiert, paketierbar und sicher abgesichert
- [x] ECM/NKit bleibt review-pflichtig, verifizierbar und rollback-sicher
- [x] Headless- und Conversion-Regressionstests sind vorhanden
