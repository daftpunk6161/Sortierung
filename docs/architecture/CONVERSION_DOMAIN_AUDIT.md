# Conversion Domain Audit – Romulus

Stand: 2026-04-01

Scope: Release-orientierter Stabilization-Audit des aktiven Conversion-Stacks in `consoles.json`, `conversion-registry.json`, `FormatConverterAdapter`, `ConversionExecutor`, `ToolRunnerAdapter` und den Reach-Invokern.

## 1. Executive Verdict

Release-Verdict: `PASS WITH EXPLICIT FAIL-CLOSED BOUNDARIES`

Der produktive Conversion-Kern ist fuer den aktuellen Release-Stand belastbar:

- Disc-basierte Lossless-Pfade (`CHD`, `RVZ`) sind registriert, verifiziert und testseitig abgesichert.
- Cartridge-/Computer-Archivierung laeuft ueber explizite `ArchiveOnly`-Policies statt ueber stille Skips.
- Review-pflichtige oder lossy Quellen (`NKit`, `CSO`, `PBP`) bleiben sichtbar im Sicherheitsmodell.
- Headless-/CLI-/GUI-Pfade nutzen dieselbe Registry- und Planner-Wahrheit.

Die bewusst verbleibenden Grenzen sind ebenfalls klar:

- `ECM` bleibt fail-closed, bis `unecm.exe` kontrolliert gepinnt ist.
- `Arcade` und `NEOGEO` bleiben ausserhalb automatischer Conversion.
- Plattformen mit rechtlich/technisch problematischen Toolketten bleiben `None` oder `ManualOnly`.

## 2. Aktueller Produktstand

### Policy-Snapshot aus `consoles.json`

| Policy | Anzahl |
|---|---:|
| `Auto` | 20 |
| `ArchiveOnly` | 52 |
| `ManualOnly` | 6 |
| `None` | 84 |
| Gesamt | 162 |

### Registry-Snapshot aus `conversion-registry.json`

| Kennzahl | Wert |
|---|---:|
| Capabilities | 18 |
| Abgedeckte Konsolen-Keys | 56 |
| Toolfamilien | 7 (`chdman`, `dolphintool`, `7z`, `ciso`, `psxtract`, `nkit`, `unecm`) |

### Sichere Default-Pfade

- CD-/UMD-/GDI-Pfade -> `CHD`
- GameCube/Wii -> `RVZ`
- ArchiveOnly-Systeme -> `ZIP`
- `PS2` ist auf CD/DVD getrennt registriert
- `NKit`-Expansion bleibt review-/warning-relevant

## 3. Seit dem Maerz-Audit geschlossen

Die folgenden Punkte aus dem frueheren Domain-Audit sind fuer den aktuellen Release-Stand nicht mehr offen:

- `ConversionPolicy` ist explizit im Datenmodell verankert.
- `Arcade`/`NEOGEO` werden nicht mehr als normale repackbare Targets behandelt.
- `PS2` trennt CD- und DVD-Profil ueber Conditions.
- `CSO -> ISO -> CHD` ist als expliziter Zwischenschritt modelliert.
- `NKit` ist ueber Reach-Pipeline, Review-Gating und Hash-Pinning integriert.
- Headless/CLI/API greifen nicht ueber lokale Schattenmappings auf andere Zielentscheidungen zu.

## 4. Aktive Release-Grenzen

| Thema | Status | Release-Entscheidung |
|---|---|---|
| `ECM` | bewusst fail-closed | Keine Freischaltung ohne kontrollierten Hash fuer `unecm.exe` |
| `NKit` | review-pflichtig, hash-pinned | Nur ueber expliziten Review-/Verify-Pfad |
| `Arcade` / `NEOGEO` | blockiert | Keine automatische Repack-/Conversion-Logik |
| `Switch`, `PS3`, `Vita` | `None` | Keine Toolintegration in rechtlich/technisch problematische Bereiche |
| `Xbox`, `X360`, `Wii U`, `PC98`, `X68K` | `ManualOnly` | Keine stille Automatik, nur bewusst operatorisch |
| `WIIU` ohne Preferred Target | bewusst | `ManualOnly` ohne Standardziel, weil kein stabiler allgemeiner Zielformatpfad existiert |

## 5. Tooling- und Sicherheitsmodell

### Gehaertete Regeln

- Toolpfade werden nicht blind aus `PATH` vertraut.
- Hash-Verifikation bleibt Teil des Tool-Checks.
- Argumente werden quotingsicher gebaut.
- Exit-Codes, Timeouts und Cleanup bleiben verpflichtend.
- Teiloutputs duerfen bei Fehlern nicht still stehenbleiben.
- Quellen werden nie vor erfolgreicher Verifikation entfernt.

### Bewusste Sicherheitsgrenze

`tool-hashes.json` enthaelt weiterhin **keinen** Hash fuer `unecm.exe`.
Damit bleibt `ECM` fuer produktiven Auto-Betrieb absichtlich blockiert, obwohl die Reach-Pipeline und der Invoker schon existieren.
Das ist kein fehlendes Finish, sondern eine Fail-Closed-Entscheidung.

## 6. Release-relevante Tests und Smokes

Die Conversion-Domaene ist fuer den Stabilization-Schnitt ueber folgende Pfade abgesichert:

- `ConversionRegistrySchemaTests`
- `ReachInvokerTests`
- `ConversionExecutorHardeningTests`
- `ToolRunnerAdapterTests`
- [RELEASE_SMOKE_MATRIX.md](../guides/RELEASE_SMOKE_MATRIX.md)

Gezielter Smoke-Befehl:

```powershell
dotnet test src/Romulus.Tests/Romulus.Tests.csproj `
  --configuration Release `
  --no-build `
  --filter "FullyQualifiedName~ConversionRegistrySchemaTests|FullyQualifiedName~ReachInvokerTests|FullyQualifiedName~ConversionExecutorHardeningTests|FullyQualifiedName~ToolRunnerAdapterTests"
```

## 7. Re-Open-Trigger

Dieses Audit muss neu aufgemacht werden, wenn mindestens einer der folgenden Punkte eintritt:

- neues produktives Conversion-Tool wird hinzugefuegt
- `ManualOnly`- oder `None`-Plattform bekommt Auto-Pfad
- `ECM` wird produktiv freigeschaltet
- `RVZ`-Verifikationsstrategie wird geaendert
- `consoles.json` oder `conversion-registry.json` aendern die Policy-Verteilung wesentlich

## 8. Fazit

Fuer den aktuellen Release-Stand ist die Conversion-Domaene nicht maximal breit, aber fachlich sauber eingegrenzt.
Die entscheidende Qualitaet liegt jetzt nicht in noch mehr Formaten, sondern darin, dass jede freigeschaltete Konvertierung:

- explizit modelliert,
- kanaluebergreifend identisch,
- review-/verify-faehig,
- und fail-closed abgesichert ist.
