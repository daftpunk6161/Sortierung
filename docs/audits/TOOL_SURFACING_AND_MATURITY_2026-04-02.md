# Tool Surfacing And Maturity Audit

Date: 2026-04-02
Scope: `src/Romulus.UI.Wpf`
Goal: make useful tools visible in the main workflow, prevent half-finished features from presenting as production-ready, and verify that every catalog entry has a real command path.

## 1. Release position

- The tool catalog is now visible in simple mode.
- Contextual recommendations are surfaced in the catalog, the start surface, and the inspector.
- Experimental tools are clearly badged and are no longer auto-recommended.
- "Ready" counts only tools that are executable in the current host and current state.
- Tool launches now update recent usage both from tool cards and from command execution paths.

## 2. Maturity policy

- `Geprueft / Verified`
  - Real execution path exists.
  - Registered command exists.
  - Tool is suitable for normal production use.
- `Gefuehrt / Guided`
  - Real execution path exists.
  - Tool is useful, but intentionally acts as review, planning, estimation, or environment guidance.
  - It is not treated as a primary production shortcut.
- `Experimentell / Experimental`
  - Real execution path exists.
  - Depth, ergonomics, or end-to-end integration are intentionally limited.
  - Never auto-recommended.
  - Must be visually badged as non-production.

## 3. Surfacing policy

- Main workflow surfaces:
  - `ToolsView`
  - `StartView`
  - `ContextPanel`
  - `CommandBar`
- Experimental tools:
  - stay in the catalog
  - remain executable if explicitly chosen
  - are not promoted into recommended surfaces
- Host-only tools:
  - remain visible
  - show explicit unavailability text instead of silently disappearing

## 4. Tool matrix

### Analysis

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `HealthScore` | Verified | registered command, tested command path | recommended after run |
| `DuplicateAnalysis` | Verified | registered command, tested command path | recommended after dedupe run |
| `JunkReport` | Verified | registered command, tested command path | recommended after junk findings |
| `RomFilter` | Verified | registered command, tested command path | catalog and run analysis |
| `MissingRom` | Verified | registered command, tested command path | recommended after DAT gaps |
| `HeaderAnalysis` | Verified | registered command, tested command path | catalog and ad-hoc analysis |
| `Completeness` | Verified | registered command, tested command path | recommended after DAT coverage check |
| `DryRunCompare` | Verified | registered command, tested command path | catalog and history analysis |

### Conversion

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `ConversionPipeline` | Guided | registered command, real estimate flow | catalog only, no primary recommendation |
| `ConversionVerify` | Verified | registered command, tested command path | recommended when conversion is active |
| `FormatPriority` | Guided | registered command, real read-only priority report | setup recommendation before conversion |
| `HeaderRepair` | Verified | registered command, real repair flow with backup | catalog and conversion support |

### DAT and verification

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `DatAutoUpdate` | Verified | registered async command, tested command path | recommended in DAT setup contexts |
| `DatDiffViewer` | Verified | registered command, real diff flow | catalog and DAT review |
| `CustomDatEditor` | Verified | registered command, real DAT entry generation | catalog only |
| `HashDatabaseExport` | Verified | registered command, tested export path | catalog and post-run analysis |

### Collection

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `CollectionManager` | Experimental | registered command, limited summary-style utility | catalog only, never recommended |
| `CloneListViewer` | Verified | registered command, real clone tree report | catalog and group inspection |
| `VirtualFolderPreview` | Experimental | registered command, limited preview utility | catalog only, never recommended |
| `CollectionMerge` | Verified | registered command, real audited merge path | recommended for multi-root setups |

### Security and integrity

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `IntegrityMonitor` | Verified | registered async command, real baseline/check path | recommended after rollback-capable runs |
| `BackupManager` | Verified | registered command, real backup path | catalog only |
| `Quarantine` | Verified | registered command, real quarantine candidate report | catalog and safety review |
| `RuleEngine` | Guided | registered command, real report path | catalog only |
| `RollbackQuick` | Verified | registered command, central rollback path | recommended when rollback is available |

### Workflow and automation

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `CommandPalette` | Verified | registered host command, tested command search | promoted in command bar |
| `FilterBuilder` | Verified | registered command, real filter evaluation path | catalog and analysis workflow |
| `SortTemplates` | Guided | registered command, real read-only template report | catalog only |
| `PipelineEngine` | Guided | registered command, real pipeline summary report | catalog only |
| `RulePackSharing` | Verified | registered command, real import/export path | catalog and setup |
| `ArcadeMergeSplit` | Verified | registered command, real DAT analysis path | catalog only |
| `AutoProfile` | Experimental | registered command, limited advisory profile logic | catalog only, never recommended |

### Export and integration

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `HtmlReport` | Verified | registered command, real report generation | post-run utility |
| `LauncherIntegration` | Verified | registered command, real export path | catalog and integration |
| `DatImport` | Verified | registered command, real import path | catalog and DAT setup |
| `ExportCollection` | Verified | registered command, tested export flows | recommended after run |

### Infrastructure

| Tool key | Maturity | Verification status | Surface policy |
| --- | --- | --- | --- |
| `StorageTiering` | Verified | registered command, real insight/report path | catalog and history review |
| `NasOptimization` | Guided | registered command, environment guidance | catalog only |
| `PortableMode` | Guided | registered command, environment guidance | catalog only |
| `ApiServer` | Verified | registered host command, real process start path | catalog and system workflow |
| `HardlinkMode` | Guided | registered command, estimate plus environment check | catalog only |
| `Accessibility` | Guided | registered host command, real UI settings action | catalog and system workflow |

## 5. Verification basis

- Registration coverage:
  - `src/Romulus.Tests/FeatureCommandServiceTests.cs`
- Command execution coverage:
  - `src/Romulus.Tests/FeatureCommandServiceTests.cs`
  - `src/Romulus.Tests/FcsExecutionAndSettingsTests.cs`
- UI surfacing coverage:
  - `src/Romulus.Tests/WpfNewTests.cs`
  - `src/Romulus.Tests/WpfProductizationTests.cs`
  - `src/Romulus.Tests/GuiViewModelTests.cs`

## 6. Implemented safeguards

- No experimental tool is pushed into recommended surfaces.
- Quick access remains user-driven through pinning.
- Missing host support is shown as unavailable instead of silently hiding features.
- Simple mode keeps the tool catalog visible.
- Retired specialist subtabs are now re-homed into primary surfaces instead of living as parallel navigation:
  - `MissionControl.QuickStart` -> integrated into `StartView`
  - `Config.Filtering` -> integrated into `ConfigOptionsView`
  - `Library.Report` -> integrated into `ResultView`
  - `Tools.DatManagement` -> DAT mapping integrated into `ConfigOptionsView`
  - `Tools.Conversion` -> conversion registry integrated into `ToolsView`
  - `Tools.GameKeyLab` -> GameKey preview integrated into `ConfigOptionsView`

## 7. Remaining intentional limits

- Experimental tools are still executable by explicit user choice.
- Guided tools remain visible because they are useful, but they are not marketed as full production shortcuts.
- This audit does not claim parity with CLI or API for these WPF-only convenience tools; it only classifies and surfaces them honestly in the GUI.
