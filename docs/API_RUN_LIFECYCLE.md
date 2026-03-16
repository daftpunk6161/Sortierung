# API Run Lifecycle

Diese Datei beschreibt den produktiven Vertrag fuer Run-Erstellung, Warten, Cancel und Recovery in der lokalen API.

## Idempotente Run-Erstellung

- `POST /runs` unterstuetzt optional den Header `X-Idempotency-Key`.
- Derselbe Key mit derselben fachlichen Anfrage erzeugt keinen zweiten Run.
- Wiederholte Requests mit identischem Key liefern denselben Run zurueck:
  - laufend: `202 Accepted`
  - bereits abgeschlossen: `200 OK`
- Derselbe Key mit anderer Anfrage ist ein Konflikt und liefert `409 Conflict`.
- Ohne `X-Idempotency-Key` bleibt die API bei genau einem aktiven Run und liefert bei Parallelstart `409 Conflict`.

## Wait-Semantik

- `wait=true` wartet auf Beobachtungsebene auf das Ende des Runs.
- `wait=true` beendet den serverseitigen Run nicht, wenn der Client die Verbindung schliesst.
- `waitTimeoutMs` begrenzt nur das Warten des HTTP-Requests.
- Wenn der Timeout erreicht wird und der Run weiter laeuft, liefert die API `202 Accepted` mit dem aktuellen Run-Zustand.
- Ein spaeterer Retry mit demselben `X-Idempotency-Key` bindet wieder an denselben Run.

## Cancel-Semantik

- `POST /runs/{runId}/cancel` ist idempotent.
- Laufender Run: Cancel wird angenommen.
- Bereits abgeschlossener, fehlgeschlagener oder bereits gecancelter Run: kein Fehler, sondern `200 OK` mit aktuellem Zustand.

## Recovery-Modell

Die API verwendet explizit das Modell `audit-rollback-only`.

- `ResumeSupported = false`
- laufender Prozess-Neustart stellt keinen Run automatisch wieder her
- Recovery ueber Prozessgrenzen hinweg ist nicht persistiert (`RestartRecovery = not-persisted`)
- wenn eine Audit-Datei existiert, ist rollbackbasierte Wiederherstellung moeglich

## Recovery-State

- `in-progress`: Run laeuft noch
- `not-required`: abgeschlossener Run ohne Rollback-Bedarf, typischerweise DryRun
- `rollback-available`: abgeschlossener Move-Run mit Audit-Datei
- `partial-rollback-available`: Cancel oder Fehler mit vorhandener Audit-Datei
- `manual-cleanup-may-be-required`: Cancel oder Fehler ohne Audit-Datei

## Failure-Szenarien

- Cancel mitten im Run: Wenn bereits ein Audit existiert, ist der Zustand `partial-rollback-available`.
- Fehler oder Crash mitten im Move: Wenn bereits ein Audit existiert, ist der Zustand ebenfalls `partial-rollback-available`.
- Neustart nach Fehler oder Crash: Der vorherige Run wird nicht automatisch wieder aufgenommen; Recovery ueber Prozessgrenzen ist bewusst `not-persisted`.

## Retry-Empfehlung fuer Clients

1. `X-Idempotency-Key` pro fachlichem Run vergeben.
2. Bei `202 Accepted` den `runId` pollen oder SSE verwenden.
3. Bei Timeout denselben Request mit gleichem `X-Idempotency-Key` wiederholen.
4. Bei terminalem Zustand das Ergebnis ueber `/runs/{runId}` oder `/runs/{runId}/result` lesen.