# Romulus Release Smoke Matrix

Stand: 2026-04-01

Dieses Dokument definiert die kleine, wiederholbare Release-Matrix fuer den Stabilization-Schnitt nach `R1-R3`.
Die Matrix nutzt nur bestehende Build-, Test- und API-Pfade. Sie fuehrt keine neue Produktlogik ein.

## Ziel

- Vor einem Release dieselben kritischen Kanaele immer gleich pruefen
- API-, Dashboard-, Conversion-, Benchmark- und Accessibility-Risiken nicht nur implizit ueber die Vollsuite abdecken
- Einen CI-faehigen Smoke-Pfad bereitstellen, der auch dann noch Wert hat, wenn Build und Vollsuite bereits vorher gelaufen sind

## Ausfuehrbare Artefakte

- [deploy/smoke/Invoke-ReleaseSmoke.ps1](../../deploy/smoke/Invoke-ReleaseSmoke.ps1)
- [deploy/smoke/Invoke-HeadlessSmoke.ps1](../../deploy/smoke/Invoke-HeadlessSmoke.ps1)

## Release-Matrix

| Bereich | Zweck | Befehl | Erwartung |
|---|---|---|---|
| Solution Build | Release-Binaries fuer alle Entry Points bauen | `dotnet build src/Romulus.sln --configuration Release -nologo -clp:ErrorsOnly` | Exit `0` |
| Vollsuite | Vollstaendige Regressionen ueber alle Schichten | `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --configuration Release --no-build --nologo` | Exit `0` |
| Manifest Integrity | `benchmark/manifest.json` gegen echte JSONL-Dateien pruefen | `pwsh -NoProfile -File benchmark/tools/Test-ManifestIntegrity.ps1` | Exit `0` |
| Coverage Gate | Release-Basis fuer Ground-Truth-/Coverage-Schwellen validieren | `pwsh -NoProfile -File benchmark/tools/Invoke-CoverageGate.ps1 -NoBuild` | Exit `0` |
| Reach Slice | Dashboard-/AllowedRoots-/OpenAPI-Reach-Vertrag gezielt pruefen | `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ApiReachIntegrationTests|FullyQualifiedName~OpenApiReachTests"` | Exit `0` |
| Conversion Slice | Aktive Conversion-/Tooling-Invarianten gezielt pruefen | `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ConversionRegistrySchemaTests|FullyQualifiedName~ReachInvokerTests|FullyQualifiedName~ConversionExecutorHardeningTests|FullyQualifiedName~ToolRunnerAdapterTests"` | Exit `0` |
| Accessibility Smoke | Strukturelle GUI-/A11y-Basics fuer die kritischen Flows pruefen | `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~Phase8ThemeAccessibilityTests|FullyQualifiedName~WpfProductizationTests"` | Exit `0` |
| Real Headless Smoke | Dokumentierten Headless-Betrieb praktisch gegen echte HTTP-Endpunkte pruefen | `pwsh -NoProfile -File deploy/smoke/Invoke-HeadlessSmoke.ps1 -Configuration Release -SkipBuild` | Exit `0` |

## Ein-Kommando-Variante

Lokaler kompletter Durchlauf:

```powershell
pwsh -NoProfile -File deploy/smoke/Invoke-ReleaseSmoke.ps1 -Configuration Release
```

CI-/Release-Variante nach bereits gelaufenem Build und Vollsuite:

```powershell
pwsh -NoProfile -File deploy/smoke/Invoke-ReleaseSmoke.ps1 `
  -Configuration Release `
  -SkipBuild `
  -SkipFullTests
```

## Headless-Scope

`Invoke-HeadlessSmoke.ps1` prueft bewusst nur release-kritische Headless-Basics:

- `GET /healthz`
- `GET /dashboard/bootstrap`
- `GET /dashboard/`
- `GET /dashboard/summary` mit API-Key
- `POST /runs` ausserhalb `AllowedRoots` -> `SEC-OUTSIDE-ALLOWED-ROOTS`
- `POST /convert` ausserhalb `AllowedRoots` -> `SEC-OUTSIDE-ALLOWED-ROOTS`

Der Smoke laeuft mit isoliertem `APPDATA`/`LOCALAPPDATA`, damit kein produktiver Index oder kein produktives Profilverzeichnis beruehrt wird.

## Accessibility-Scope

Der Release-Smoke automatisiert die strukturellen GUI-/A11y-Pruefungen, die im Repo reproduzierbar sind:

- Theme- und Kontrast-Invarianten
- `AutomationProperties.Name`
- Live-Region-Markup
- Wizard-/Workflow-Bindings der produktisierten Flows

Der manuelle Bedien-Spot-Check fuer Tastatur und Windows Narrator bleibt als operatorische Freigabe in [narrator-testplan.md](../ux/narrator-testplan.md) dokumentiert.
Er wird bewusst nicht als schein-automatisierter CI-Schritt nachgebaut.

## CI-Integration

Der Tag-/Release-Workflow nutzt diese Matrix nach Build und Vollsuite ueber:

```powershell
pwsh -NoProfile -File deploy/smoke/Invoke-ReleaseSmoke.ps1 -Configuration Release -SkipBuild -SkipFullTests
```

Damit bleibt genau ein Release-Smoke-Pfad bestehen statt mehrerer konkurrierender Checklisten.
