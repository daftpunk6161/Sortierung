# Journey Test Matrix

Diese Matrix definiert die produktiven Journey-Gates fuer Release-Hardening ueber GUI, CLI und API.

## Prioritaetskette

- Persona 1: Anfaenger
- Persona 2: Power-User / Automation
- Entry-Points: GUI, CLI, API
- Gate-Regel: Die unten referenzierten Testgruppen muessen im CI-Schritt `Journey Matrix Gate` gruen sein.

## Matrix

| Journey | Persona | GUI | CLI | API | Referenztests |
|---|---|---|---|---|---|
| Startkonfiguration / Defaults | Anfaenger, Power-User | Ja | Ja | Ja | `RunServiceAndSettingsTests`, `SettingsLoaderTests`, `CliProgramTests` |
| DryRun Start | Anfaenger | Ja | Ja | Ja | `GuiViewModelTests`, `CliProgramTests`, `ApiIntegrationTests` |
| Preview / Report | Anfaenger | Ja | Teilweise | Ja | `WpfNewTests`, `GuiViewModelTests`, `ApiIntegrationTests`, `RunServiceAndSettingsTests` |
| Confirm / Apply | Anfaenger | Ja | Ja | Ja | `GuiViewModelTests`, `WpfNewTests`, `ApiIntegrationTests` |
| Move / Trash / Audit | Power-User | Ja | Ja | Ja | `AuditCsvStoreTests`, `AuditSigningServiceTests`, `RunServiceAndSettingsTests`, `ApiIntegrationTests` |
| Undo / Restore / Rollback | Power-User | Ja | Ja | Ja | `FeatureCommandServiceTests`, `AuditCsvStoreTests`, `AuditSigningServiceTests`, `RunManagerTests` |
| Cancel | Anfaenger, Power-User | Ja | Ja | Ja | `GuiViewModelTests`, `RunManagerAdvancedTests`, `ApiIntegrationTests` |
| Neustart / Recovery | Power-User | Ja | Ja | Ja | `RunManagerTests`, `ApiIntegrationTests` |
| Fehlerhafte Inputs | Anfaenger, Power-User | Ja | Ja | Ja | `GuiViewModelTests`, `CliProgramTests`, `ApiIntegrationTests` |
| Fehlende Tools / degradierte Umgebung | Power-User | Ja | Ja | Ja | `RunOrchestratorTests`, `FeatureCommandServiceTests`, `RunServiceAndSettingsTests` |

## Release Gate

Der CI-Gate `Journey Matrix Gate` deckt die Matrix ueber die folgenden Testklassen ab:

- `ApiIntegrationTests`
- `CliProgramTests`
- `GuiViewModelTests`
- `WpfNewTests`
- `FeatureCommandServiceTests`
- `RunServiceAndSettingsTests`
- `RunManagerTests`
- `RunManagerAdvancedTests`
- `AuditCsvStoreTests`
- `AuditSigningServiceTests`
- `SettingsLoaderTests`

## Designentscheidung

- Die Matrix ist bewusst entry-point-orientiert statt komponentenorientiert.
- Bereits vorhandene Journey-nahe Tests werden als Release-Gate zusammengezogen, statt eine zweite parallele Testwelt aufzubauen.
- Neue Persona-Journeys muessen kuenftig entweder in eine bestehende Gate-Klasse eingeordnet oder der Matrix explizit hinzugefuegt werden.