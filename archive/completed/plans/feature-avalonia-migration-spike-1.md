# Avalonia Migration – Technical Spike

**Date:** 2026-04-18  
**Scope:** WPF → Avalonia feasibility for `Romulus.UI.Wpf`  
**Decision target:** Go / No-Go + effort estimate

---

## 1. Shared Code (nothing to migrate)

| Layer | Migrable as-is |
|---|---|
| `Romulus.Contracts` | ✅ `net10.0`, no WPF |
| `Romulus.Core` | ✅ `net10.0`, no WPF |
| `Romulus.Infrastructure` | ✅ `net10.0-windows` — Windows-only but no WPF |
| `Romulus.CLI` | ✅ untouched |
| `Romulus.Api` | ✅ untouched |

Infrastructure stays `net10.0-windows` because it uses `Microsoft.Win32.Registry` (SettingsLoader) and `System.Windows.Forms` is only in the WPF project itself. No work needed here for Avalonia.

---

## 2. ViewModels — Assessment

All ViewModels inherit `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` (not `DependencyObject`). **CommunityToolkit.Mvvm is fully Avalonia-compatible.**

Commands use `RelayCommand` / `AsyncRelayCommand` from CommunityToolkit — same library works unchanged.

**`SynchronizationContext`** usage in `MainViewModel` (line 40, 98, 876): replaces `Dispatcher`. Avalonia's threading model uses `Dispatcher.UIThread.Post` / `Avalonia.Threading.Dispatcher` — the pattern is identical. The `SynchronizationContext` approach will work with minimal change.

**Verdict: ViewModels are ~95% platform-agnostic. Estimated rework: 1–2 days.**

---

## 3. Views / XAML — What needs rewriting

### 3a. XAML markup (30 files)

Avalonia XAML is ~90% compatible with WPF XAML. Key differences:

| WPF | Avalonia replacement | Effort |
|---|---|---|
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | Avalonia namespace | Automated search & replace |
| `<Window>` base class | `<Window>` (Avalonia) — nearly identical | Minimal |
| `DynamicResource` | `DynamicResource` — **supported** | None |
| `Trigger` / `DataTrigger` | Replaced by `DataTrigger` via Styles | Medium |
| `ControlTemplate` with `Trigger` | `ControlTheme` in Avalonia 11 | Medium |
| `Style TargetType` | Almost identical | Minimal |
| `Storyboard` animations | Avalonia `Animation` / `Transition` | Medium |
| `BitmapImage` from URI | Avalonia `Bitmap` / `IImage` | Low |

**Estimated XAML rework: 8–12 days** for ~30 Views + 6 theme dictionaries.

### 3b. ValueConverters (`Converters.cs`)

All converters implement `IValueConverter` — interface is identical in Avalonia. The implementations use only primitive comparisons and enum checks, no WPF types. **Direct port: ~2 hours.**

### 3c. Code-behind files (30 `.xaml.cs` files)

Most are thin (event wiring + VM delegation). The heavier ones:

- **`LibraryReportView.xaml.cs`**: `FindResource("BrushWarning")` — replace with Avalonia `Resources["BrushWarning"]`. Low effort.
- **`MainWindow.xaml.cs`**: `WindowState`, `Dispatcher.InvokeAsync` — direct equivalents exist in Avalonia. Low-medium effort.
- **`MessageDialog.xaml.cs`**: `Application.Current.TryFindResource(...)` → Avalonia `Application.Current.Resources.TryGetResource(...)`. Low effort.

**Estimated code-behind rework: 3–4 days.**

---

## 4. WPF-Specific Code — Hard Blockers

### 4a. `TrayService` (hard Windows dependency)

```csharp
[DllImport("user32.dll")] DestroyIcon(IntPtr)
System.Windows.Forms.NotifyIcon
System.Drawing.Bitmap / SolidBrush
```

**Avalonia does not provide a cross-platform tray API.** Options:
- Keep `TrayService` as a Windows-only service behind `OperatingSystem.IsWindows()` guard
- Use `Hardcodet.NotifyIcon.Wpf` → `Avalonia.Controls.ApplicationLifetimes` + community `TrayIcon` (Avalonia 11 ships `TrayIcon` control natively)

**Avalonia 11 ships `TrayIcon` natively.** Migration is medium effort (~1 day).

### 4b. `ThemeService`

Uses `Application.Current.Resources.MergedDictionaries` with `pack://application` URIs. Avalonia uses `avares://` URI scheme. The swap logic stays identical; only URI strings and the `Uri` constructor change.

**Effort: 2–4 hours.**

### 4c. `FeatureService.Infra.cs` — `Microsoft.Win32.Registry`

Theme detection reads registry for Windows dark mode. Already has/needs a Windows guard (`OperatingSystem.IsWindows()`). On Avalonia/Linux/macOS this path is simply skipped — fallback to default theme.

**Effort: guard already in place (SettingsLoader has it). Zero extra work for migration.**

### 4d. `DialogService.cs` + `ResultDialog`

Uses `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog`. Avalonia 11 ships `StorageProvider` API with equivalent open/save dialogs — cross-platform and Sandboxed.

**Effort: 1 day to replace OpenFileDialog/SaveFileDialog calls.**

### 4e. XAML resource URI scheme

All `pack://application:,,,/Themes/*.xaml` URIs must become `avares://Romulus.UI.Avalonia/Themes/*.xaml`.  
Automated replacement: ~1 hour.

---

## 5. Missing Features in Avalonia vs. WPF

| Feature | Status |
|---|---|
| `DataGrid` | ✅ Avalonia ships DataGrid |
| `TreeView` | ✅ supported |
| `ListView` / `VirtualizingPanel` | ✅ `ListBox` + `ItemsRepeater` |
| `FlowDocument` / `RichTextBox` | ❌ Not in Avalonia — used? No (Romulus uses plain `TextBlock`) |
| `WindowsFormsHost` | ❌ N/A for cross-platform — `TrayIcon` replaces the only WinForms usage |
| Single-instance Mutex | ✅ handled in `App.xaml.cs`, no Avalonia equivalent needed |

No blocking missing features found.

---

## 6. Effort Estimate

| Area | Days |
|---|---|
| ViewModels (dispatcher / sync context tweaks) | 1–2 |
| XAML views + control templates | 8–12 |
| ValueConverters | 0.25 |
| Code-behind files | 3–4 |
| TrayService → Avalonia TrayIcon | 1 |
| ThemeService URI + ResourceDictionary | 0.5 |
| DialogService → StorageProvider | 1 |
| App startup / DI wiring | 1 |
| Theme dictionaries (6 × XAML) | 3–4 |
| Test/smoke pass | 2 |
| **Total** | **~21–27 days** |

A parallel approach (keep WPF for Windows, add Avalonia for Linux/macOS, share everything above Infrastructure) would reduce total effort to ~15–20 days by reusing ~60% of WPF XAML as a reference rather than a migration target.

---

## 7. Recommendation

**GO — with staged approach:**

1. **Phase A (now possible):** Introduce `Romulus.UI.Avalonia` project targeting `net10.0`. Port ViewModels first (they're already 95% agnostic). Run headless tests to verify ViewModel logic cross-platform.

2. **Phase B:** Port views one screen at a time, starting with `StartView`, `ProgressView`, `ResultView` (simplest). WPF version stays as-is and remains the shipping build.

3. **Phase C:** Switch default build to Avalonia once smoke tests pass on Linux/macOS. Deprecate WPF.

**Blocking prerequisites:**
- No MAUI consideration needed — MAUI requires iOS/Android focus and has no Linux support. Avalonia is the correct choice.
- `CommunityToolkit.Mvvm` 8.4.2 is fully Avalonia-compatible — no ViewModel changes needed for the toolkit itself.

**Risk:** Low. The architecture (MVVM + service interfaces) is already well-prepared. The biggest work is mechanical XAML translation, not logic migration.

---

## 8. Umsetzungstand (2026-04-18)

Die initiale Stufenmigration fuer Avalonia wurde fuer die Kern-Workflows umgesetzt und testseitig abgesichert.

### Abgeschlossene Slices

1. Phase B: Workflow-Grundgeruest in Avalonia ViewModels.
2. Phase C: Adapter-/Service-Verdrahtung fuer den UI-Startpfad.
3. Phase D: Root-Auswahl ueber Dialog mit Cancel-/Duplikat-Guards.
4. Phase E: Asynchrone Folder-Picker-Integration (`AsyncRelayCommand`).
5. Phase F: Import von Root-Listen ueber File-Picker.
6. Phase G: Produktiver Summary-Export aus dem Result-Screen.
7. Phase H: Strukturierter Metrics-CSV-Export inkl. CSV-Escaping und Formel-Praefix-Haertung.
8. Guard-Slice: Single-Instance-Guard mit Mutex-Acquire-/Release-Verhalten.

### Solution- und Testverdrahtung

- `Romulus.UI.Avalonia` ist in `src/Romulus.sln` enthalten.
- `Romulus.Tests` referenziert `Romulus.UI.Avalonia` fuer ViewModel-/Runtime-Tests.

### Verifikation

- `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --filter FullyQualifiedName~AvaloniaPhaseHMetricsCsvExportTests` -> gruen.
- `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --filter FullyQualifiedName~Avalonia` -> gruen.
- `dotnet build src/Romulus.sln` -> gruen.

### Aktueller Zustand

Die Avalonia-Basis fuer Start/Progress/Result inklusive Exportpfaden und Guard-Verhalten ist lauffaehig, testbar und in der Solution integriert. Weitere Arbeit betrifft primär die schrittweise Portierung weiterer WPF-Screens und Themenressourcen gemaess Phase-B/Phase-C-Roadmap.
