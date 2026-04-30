# ADR-0030: Telemetry Opt-In (Local-Only, DSGVO-konform)

- **Status:** accepted
- **Datum:** 2026-04-30
- **Wave/Task:** Wave 4 — T-W4-TELEMETRY-OPT-IN
- **Covers:** TOP-14

## Kontext

Romulus ist ein Desktop-Tool fuer ROM-Sammlungen. Einige Verbesserungen in
Welle 6 (Heuristik-Tuning, Tool-Kartenreduktion, Workflow-Friction-Backlog)
profitieren von aggregierten Nutzungsdaten. Gleichzeitig gilt:

- DSGVO-konforme Datenverarbeitung ist Pflicht.
- Romulus hat keinen Beta-Cohort (siehe T-W3-BETA-USERS `wontfix-with-reason`),
  also auch keine alternative Quelle aggregierter Nutzungsdaten.
- Eine versehentliche Aktivierung oder ein Leak von Pfaden/Hostnames ist ein
  Release-Blocker (vgl. T-W4-TELEMETRY-OPT-IN failure_modes).

## Entscheidung

1. **Opt-In, default OFF.** Der `TelemetryService.IsEnabled`-Default ist
   `false`. Es gibt keinen automatischen Wechsel auf `true`. Der Benutzer
   muss die Telemetrie aktiv im Setup-Bereich aktivieren.
2. **Lokale Persistenz, kein Auto-Send.** In dieser Iteration werden
   Events nur in einem In-Memory-Ring gehalten; der Toggle wird in
   `%APPDATA%\Romulus\telemetry.json` persistiert. Es gibt **keinen**
   Netzwerk-Code. Endpoint und Retention werden hier dokumentiert,
   aber erst in einer spaeteren Welle implementiert.
3. **Allow-List am Recording-Boundary.** Jeder Feldname wird gegen
   `TelemetryEventAllowList.AllowedFields` gepruft. Felder ausserhalb der
   Liste werden still verworfen. Die Liste enthaelt **keinen** Schluessel,
   der `path`, `host`, `user` oder `ip` enthalten kann. Hinzufuegen eines
   Feldes erfordert ADR-Update + Test-Update.
4. **Aggregate-only Felder.** Erlaubt sind ausschliesslich Counts,
   Dauern, kanonische Konsolen-Keys (z.B. `"NES"`) und Outcome-Strings
   (`"Move"`/`"Skip"`/`"Convert"`/`"Error"`).
5. **Kein Backdoor.** Eine bewusste Default-OFF-Aenderung erfordert
   gleichzeitig ein ADR-Update (dieses Dokument) **und** eine Anpassung
   der Pin-Tests in `Wave4TelemetryOptInTests`. Der Source-Inspection-Pin
   `Source_DefaultEnabledLiteral_IsFalse` schlaegt sonst fehl.

## Geplanter Endpoint und Retention (zukuenftig, NICHT in dieser Iteration)

- **Endpoint:** TBD (eigene Domain, TLS only). Wird vor Aktivierung in einer
  weiteren ADR festgelegt.
- **Retention:** maximal 90 Tage rolling, ausschliesslich aggregierte
  Counts. Keine Speicherung von Roh-Events laenger als 24 Stunden.
- **Anonymisierungs-Pflicht:** Server seitig zusaetzliche Drop-Rule fuer
  jegliche Felder, die wieder dazu kommen koennten — defense-in-depth
  ergaenzend zur Client-seitigen Allow-List.
- **Loeschanspruch:** ohne stabile Nutzer-ID nicht erforderlich; das
  Telemetry-Schema enthaelt keine ID, ueber die ein Datensatz auf einen
  Nutzer zurueckfuehrbar waere.

## Konsequenzen

- Welle 6 hat ohne Cohort und ohne Telemetrie weiterhin nur die
  synthetischen Smokes als Datenquelle. Das ist bewusst akzeptiert,
  weil eine still aktivierte Telemetrie den Reduktions-Anspruch des Plans
  verletzen wuerde.
- Der `TelemetryService` ist eine bewusst kleine Abstraktion. Eine spaetere
  Netzwerk-Implementierung kommt in einer separaten ADR (`ADR-0031`).
- Die GUI-Setup-Toggle wird in einem spaeteren Wave-4-Hygiene-Pass
  oder in Wave 6 aufgesetzt. Die Implementierung des Services ist heute
  fertig; das Pin-Test-Set verhindert ein versehentliches Default-Flip.

## Verifikation

- `dotnet test --filter "FullyQualifiedName~Wave4TelemetryOptInTests"` -> 8/8 gruen.
- `Source_DefaultEnabledLiteral_IsFalse` greift, wenn jemand
  `TelemetryService.DefaultEnabled` auf `true` setzt.
- `AllowList_DoesNotPermitPathOrIdentityKeys` greift, wenn jemand ein
  Feld mit `path|host|user|ip` zur Allow-List hinzufuegt.

## Verweise

- `src/Romulus.Contracts/Ports/ITelemetryService.cs`
- `src/Romulus.Infrastructure/Telemetry/TelemetryService.cs`
- `src/Romulus.Infrastructure/Telemetry/TelemetryEventAllowList.cs`
- `src/Romulus.Tests/Wave4TelemetryOptInTests.cs`
- `docs/plan/strategic-reduction-2026/plan.yaml` -> T-W4-TELEMETRY-OPT-IN
