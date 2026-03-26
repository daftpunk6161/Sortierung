---
goal: "Deep-Dive Bug-Audit: DiscHeaderDetector, DatIndex, CrossRoot/FolderDeduplicator, WPF Features (81 Handler), CI Pipeline"
version: 1.0
date_created: 2026-03-12
last_updated: 2026-03-12
owner: RomCleanup Team
status: 'Planned'
tags: [bug-audit, security, feature, deep-dive, xxe, deduplication, wpf, ci]
---

# Deep-Dive Bug-Audit — Alle Feature-Bereiche

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Umfassender Deep-Dive Bug-Audit aller Feature-Bereiche des RomCleanup-Projekts. Ergänzt den initialen Audit (55 Bugs) um tiefergehende Analysen in 5 Runden: (1) DiscHeaderDetector Binary Parsing, (2) DatIndex Datenstruktur, (3) CrossRoot- & FolderDeduplicator, (4) WPF Features — alle 81 Handler + FeatureService (49 Methoden), (5) CI/CD Pipeline. Insgesamt **83 neue Findings** (6× P0, 22× P1, 38× P2, 17× P3).

---

## 1. Requirements & Constraints

- **REQ-001**: Alle Findings müssen konkrete Dateipfade, Zeilennummern und Fix-Strategien enthalten
- **REQ-002**: Priorisierung nach P0 (Release-Blocker) → P1 (Hoch) → P2 (Mittel) → P3 (Niedrig)
- **REQ-003**: Jedes Finding braucht eine Testabsicherungsstrategie
- **SEC-001**: XXE-Schwachstellen in XML-Parsing sind P0 Security-Blocker
- **SEC-002**: Path-Traversal bei File-Operationen muss validiert sein
- **SEC-003**: Kein MD5 für sicherheitsrelevante Hashing-Operationen
- **CON-001**: Keine Code-Änderungen in diesem Plan — nur Analyse und Maßnahmen
- **CON-002**: Alle Findings basieren auf tatsächlich gelesenem Code (keine Vermutungen)
- **GUD-001**: Determinismus bei Winner-Selection über alle Dedup-Strategien hinweg
- **PAT-001**: Gleiche Security-Patterns (safe XML loading, path validation) überall konsistent

---

## 2. Implementation Steps

### Runde 1 — DiscHeaderDetector + Binary Parsing

- GOAL-001: Alle Bugs in `DiscHeaderDetector.cs` (Core/Classification, ~430 Zeilen) identifizieren und Fixes planen

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | **P1 – ReDoS-Risiko in ResolveConsoleFromText**: `ResolveConsoleFromText()` (Zeile 107-159) ruft 15× `Regex.IsMatch()` mit nicht-kompilierten Regex-Patterns auf. Jeder Aufruf erzeugt intern ein neues Regex-Objekt. Bei bösartigen Disc-Metadaten (z.B. „SEGA" wiederholt über 64KB) können Backtracking-intensive Patterns zu ReDoS führen. **Fix**: Alle 15 Patterns als `private static readonly Regex` mit `RegexOptions.Compiled \| RegexOptions.IgnoreCase` und `matchTimeout: TimeSpan.FromMilliseconds(100)` deklarieren, analog zu den bereits kompilierten Regex in `FolderDeduplicator.cs`. | | |
| TASK-002 | **P2 – Cache-Key ohne Pfad-Normalisierung**: `_isoCache` und `_chdCache` verwenden den Dateipfad als Schlüssel (Zeile 42, 62), aber Windows ist case-insensitive. `C:\ROMs\game.iso` und `c:\roms\GAME.iso` erzeugen zwei Cache-Einträge für dieselbe Datei. **Fix**: Cache-Key via `Path.GetFullPath(path).ToUpperInvariant()` normalisieren. HINWEIS: `LruCache` verwendet bereits `StringComparer.OrdinalIgnoreCase` im Konstruktor — der Bug liegt im fehlenden `Path.GetFullPath()`, nicht im Case-Vergleich. Tatsächlich: Der LruCache-Konstruktor akzeptiert einen optionalen Comparer — prüfen ob `OrdinalIgnoreCase` gesetzt ist. | | |
| TASK-003 | **P2 – ReadAtLeast partial read nicht geprüft**: In `ScanDiscImage()` (Zeile 182) wird `fs.ReadAtLeast(buffer.AsSpan(32, scanSize - 32), scanSize - 32, throwOnEndOfStream: false)` aufgerufen. Bei `throwOnEndOfStream: false` kann weniger als `scanSize - 32` Bytes gelesen werden. Der Rückgabewert (tatsächlich gelesene Bytes) wird ignoriert. Nachfolgende Offset-Prüfungen (z.B. `scanSize >= 0x10000 + 20`) verwenden den angefragten, nicht den tatsächlich gelesenen Wert. **Fix**: Rückgabewert von `ReadAtLeast` speichern und `scanSize` auf `32 + actualRead` korrigieren. | | |
| TASK-004 | **P3 – 3DO False-Positive-Risiko**: Die 3DO-Erkennung (Zeile 174-176) prüft nur 6 Bytes: `0x01 0x5A 0x5A 0x5A 0x5A 0x5A`. Jede beliebige Datei, die zufällig mit diesen 6 Bytes beginnt, wird als 3DO erkannt. Andere Detektoren prüfen zusätzliche Strukturmerkmale oder mindestens 16+ Bytes. **Fix**: Zusätzlich Opera-FS-Marker prüfen: Byte 40-43 = „CD-ROM" oder Volume-Label bei Offset 0x28. | | |
| TASK-005 | **P3 – ScanChdMetadata ReadAtLeast analog**: `ScanChdMetadata()` (Zeile 282) hat dasselbe Problem wie TASK-003 — `ReadAtLeast` ohne Prüfung des Rückgabewerts. **Fix**: Analog zu TASK-003, `scanSize` auf tatsächlich gelesene Bytes korrigieren. | | |
| TASK-006 | **P2 – Kein Schutz gegen extrem große Dateien im Batch-Modus**: `DetectBatch()` (Zeile 87) iteriert synchron über alle Pfade und öffnet jede Datei. Bei einer großen Sammlung (100k+ Dateien) kein Throttling, kein Parallelismus, keine Progress-Rückmeldung. **Fix**: `IProgress<int>` Parameter hinzufügen; in Zukunft ggf. `Parallel.ForEachAsync` mit Concurrency-Limit. | | |

### Runde 2 — DatIndex Datenstruktur

- GOAL-002: Alle Bugs in `DatIndex.cs` (Contracts/Models, ~66 Zeilen) identifizieren und Fixes planen

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-007 | **P2 – TotalEntries O(n) pro Zugriff**: `TotalEntries` (Zeile 23-30) iteriert über alle inneren `ConcurrentDictionary`-Values bei jedem Getter-Aufruf. Bei 50 Konsolen ist das performant, aber Aufrufer könnten es in Schleifen nutzen (z.B. UI-Fortschrittsanzeige). **Fix**: Entweder `Interlocked.Increment`-basierter Counter bei `Add()`, oder dokumentieren, dass der Aufruf O(Konsolen) ist. | | |
| TASK-008 | **P3 – Keine Size-Limitierung**: `DatIndex` hat kein Maximum für die Anzahl der Einträge. Ein bösartiges oder fehlerhaftes DAT (z.B. 100 Millionen Einträge) kann OOM verursachen. **Fix**: Optionalen `maxEntriesPerConsole`-Parameter im Konstruktor mit Default 500.000. Bei Überschreitung `InvalidOperationException` werfen. | | |
| TASK-009 | **P3 – Doppelte ToLowerInvariant-Normalisierung**: `Add()` (Zeile 37) normalisiert `hash.ToLowerInvariant()`, der innere `ConcurrentDictionary` verwendet aber bereits `StringComparer.OrdinalIgnoreCase`. Die Normalisierung ist redundant — korrekt, aber allokiert unnötig einen neuen String pro Add/Lookup. **Fix**: Entweder `ToLowerInvariant()` entfernen (da Comparer case-insensitive) ODER Comparer auf `Ordinal` setzen und die Normalisierung behalten — aber nicht beides. | | |
| TASK-010 | **P3 – ConsoleKeys enumeriert live**: `ConsoleKeys` (Zeile 62) gibt `_data.Keys` zurück, was bei `ConcurrentDictionary` einen Snapshot der Keys erzeugt. Bei häufigem Aufruf unnötige Allokationen. Geringes Risiko, aber inkonsistent mit `TotalEntries` (das direkt iteriert). Kein Fix erforderlich, nur Dokumentation. | | |

### Runde 3 — CrossRootDeduplicator + FolderDeduplicator

- GOAL-003: Alle Bugs in `CrossRootDeduplicator.cs` (Infrastructure, ~85 Zeilen) und `FolderDeduplicator.cs` (Infrastructure, ~500 Zeilen) identifizieren und Fixes planen

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-011 | **P1 – CrossRoot Winner-Selection ignoriert Region/Version/DatMatch**: `GetMergeAdvice()` (Zeile 47-73) wählt den Winner nur nach FormatScore + SizeBytes. Der Haupt-`DeduplicationEngine.SelectWinner()` berücksichtigt dagegen RegionScore, VersionScore und DatMatch. Ergebnis: CrossRoot-Merge kann den falschen Winner behalten (z.B. JP-Version statt EU-Version bei gleichem Hash). **Fix**: `GetMergeAdvice()` muss dieselbe Scoring-Logik wie `DeduplicationEngine` verwenden. Entweder delegieren oder die vollständige Score-Berechnung duplizieren. | | |
| TASK-012 | **P1 – FolderDeduplicator MD5 für PS3-Hashing**: `GetPs3FolderHash()` (Zeile 55-72) verwendet `MD5.Create()` für das Hashing von PS3-Schlüsseldateien. MD5 ist kryptographisch gebrochen. Bei gezielten Kollisionsangriffen könnte ein Angreifer zwei verschiedene PS3-Ordner mit gleichem Hash erzeugen, was zur falschen Deduplizierung führt. **Fix**: SHA256 statt MD5 verwenden. Nur die Algorithmus-Konstruktor-Zeile ändern: `SHA256.Create()` statt `MD5.Create()`. | | |
| TASK-013 | **P1 – DeduplicatePs3 erstellt Verzeichnis in DryRun**: `DeduplicatePs3()` (Zeile 117) ruft `_fs.EnsureDirectory(dupeBase)` auf, BEVOR geprüft wird, ob überhaupt Duplikate existieren. `AutoDeduplicate()` (Zeile 332) ruft `DeduplicatePs3()` nur auf, wenn `mode == "Move"`, aber die Methode selbst hat keinen `mode`-Parameter — sie erstellt immer das Verzeichnis. **Fix**: `mode`-Parameter zu `DeduplicatePs3()` hinzufügen und `EnsureDirectory` nur bei `mode == "Move"` aufrufen, oder Verzeichnis erst erstellen, wenn tatsächlich ein Move stattfindet. | | |
| TASK-014 | **P1 – DeduplicateByBaseName: Destination nicht path-validated**: `DeduplicateByBaseName()` (Zeile 264) validiert den Source-Path via `ResolveChildPathWithinRoot()`, aber der Destination-Path (`Path.Combine(dupeBase, loser.Dir.Name)`) wird NICHT validiert. Bei einem manipulierten Ordnernamen wie `..\..\Windows` könnte das Ziel außerhalb der erlaubten Root landen. **Fix**: Auch den Destination-Path via `ResolveChildPathWithinRoot(dupeBase, loser.Dir.Name)` validieren. | | |
| TASK-015 | **P2 – GetFolderBaseKey: Case-Folding erst am Ende**: `GetFolderBaseKey()` (Zeile 85-114) wendet `.ToLowerInvariant()` erst ganz am Ende an (Zeile 113). Die Regex-Patterns `PreservePattern` und `ParenthesisPattern` werden auf den Original-Case angewendet. Da `PreservePattern` `RegexOptions.IgnoreCase` hat, funktioniert es korrekt — aber `ParenthesisPattern` hat KEIN `IgnoreCase`, was bei Klammer-Matching irrelevant ist (Klammern sind case-neutral). Kein echter Bug, aber Code-Klarheit verbessern: `result.ToLowerInvariant()` VOR Regex-Matching anwenden und Pattern-Flags vereinheitlichen. | | |
| TASK-016 | **P2 – PS3 Winner-Selection ist ordnungsabhängig**: `DeduplicatePs3()` (Zeile 135-145) verwendet das erste Vorkommen als initialen Winner. Wenn ein späterer Ordner mehr Dateien hat ODER alphabetisch vor dem bisherigen Winner sortiert, wird der bisherige Winner zum Loser gemacht. ABER: Die Reihenfolge der Iteration hängt von `Directory.GetDirectories()` ab, was NICHT deterministic-ordered ist (Windows NTFS-Reihenfolge). **Fix**: Alle Ordner mit gleichem Hash sammeln, dann einmal deterministisch sortieren (Dateizahl → Name). | | |
| TASK-017 | **P2 – FindDuplicates: 3× materialisiert mit ToList()**: `FindDuplicates()` (Zeile 18-38) ruft in der LINQ-Pipeline `g.ToList()` innerhalb des `Where`-Filters auf (Zeile 26), dann nochmal `.Select(g => ... g.ToList())` (Zeile 32). Das materialisiert jede Gruppe zweimal. **Fix**: Einmal `ToList()` im `Select` und Count/Distinct dort prüfen, oder `let items = g.ToList()` verwenden. | | |
| TASK-018 | **P3 – GetPs3FolderHash: File.ReadAllBytes für große Dateien**: `GetPs3FolderHash()` (Zeile 65) liest `File.ReadAllBytes()` für jede PS3-Schlüsseldatei. EBOOT.BIN kann mehrere GB groß sein. Das lädt den gesamten Inhalt in den RAM. **Fix**: Streaming-basiertes Hashing mit `SHA256.TransformBlock()` + `FileStream` statt `ReadAllBytes()`. | | |
| TASK-019 | **P3 – CountFilesRecursive/GetNewestFileTimestamp schlucken alle Exceptions**: Diese Hilfsmethoden (Zeile 459-475) fangen alle Exceptions und geben Default-Werte zurück. Das verbirgt ernste Fehler (z.B. Access-Denied auf Teilen des Ordners). **Fix**: `IOException` und `UnauthorizedAccessException` gezielt fangen, alles andere durchlassen. | | |

### Runde 4 — WPF Features: Security & Core

- GOAL-004: Alle sicherheits- und korrektheitsrelevanten Bugs in den 81 WPF-Handlern und FeatureService (49 public static Methoden) identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-020 | **P0 – XXE in OnDatDiffViewer (MainWindow.xaml.cs Zeile 1643-1644)**: `XDocument.Load(fileA)` und `XDocument.Load(fileB)` laden benutzerdefinierte DAT-Dateien ohne DTD-Processing zu deaktivieren. Ein Angreifer kann eine XML-Datei mit externer Entity-Deklaration erstellen, die lokale Dateien exfiltriert (z.B. `<!ENTITY xxe SYSTEM "file:///C:/Users/Admin/Documents/keys.txt">`). **Fix**: `XmlReaderSettings` mit `DtdProcessing = DtdProcessing.Prohibit` und `XDocument.Load(XmlReader.Create(path, settings))` verwenden. | | |
| TASK-021 | **P0 – XXE in OnArcadeMergeSplit (MainWindow.xaml.cs Zeile 2572)**: Identisches Problem wie TASK-020. `XDocument.Load(datPath)` für MAME/FBNEO DAT-Dateien. **Fix**: Identisch zu TASK-020. | | |
| TASK-022 | **P0 – XXE in FeatureService.CompareDatFiles (FeatureService.cs Zeile 1295-1296)**: `XDocument.Load(pathA)` und `XDocument.Load(pathB)` in der statischen Methode. Wird von `OnDatDiffViewer` aufgerufen, d.h. doppelte XXE-Exposition. **Fix**: Zentrale `SafeLoadXDocument(string path)` Helper-Methode mit sicheren `XmlReaderSettings` erstellen und an allen 6 Stellen verwenden. | | |
| TASK-023 | **P0 – XXE in FeatureService.LoadDatGameNames (FeatureService.cs Zeile 1325)**: `XDocument.Load(path)` für einzelne DAT-Dateien. **Fix**: Siehe TASK-022 — dieselbe zentrale Helper-Methode verwenden. | | |
| TASK-024 | **P1 – OnTosecDat: File.Copy ohne Validierung (MainWindow.xaml.cs Zeile 1714)**: `File.Copy(path, targetPath, overwrite: true)` kopiert eine beliebige vom Benutzer gewählte Datei in den `DatRoot`. Der Dateiname wird nicht sanitized — ein Dateiname wie `..\..\config\settings.json` könnte Dateien außerhalb des DatRoot überschreiben. **Fix**: `Path.GetFileName()` auf den Dateinamen anwenden und `Path.GetFullPath(targetPath)` gegen `DatRoot` validieren. | | |
| TASK-025 | **P1 – OnCustomDatEditor: Nicht-atomares Datei-Splicing (MainWindow.xaml.cs Zeile 1745-1778)**: Liest alte Datei, findet Closing-Tag-Index, spleißt neuen Eintrag ein, überschreibt Datei. Bei Crash/Stromausfall zwischen Read und Write geht die DAT-Datei verloren. **Fix**: Atomic Write Pattern: in Temp-Datei schreiben, dann `File.Move(tmpPath, target, overwrite: true)`. | | |
| TASK-026 | **P1 – OnHeaderRepair: File.WriteAllBytes ohne Path-Traversal-Check (MainWindow.xaml.cs Zeile 2131)**: Schreibt modifizierte ROM-Bytes direkt in den vom Benutzer gewählten Pfad. Der Pfad kommt von `DialogService.BrowseFile()`, aber es wird nicht validiert, ob er innerhalb eines erlaubten Root liegt. **Fix**: Pfad gegen konfigurierte Roots validieren via `ResolveChildPathWithinRoot()`. | | |
| TASK-027 | **P1 – RepairNesHeader: File.ReadAllBytes für beliebig große Dateien (FeatureService.cs Zeile 1418)**: Liest die gesamte ROM-Datei in den RAM. NES-ROMs sind typisch klein (< 2MB), aber der Benutzer könnte versehentlich eine große Datei wählen (DVD-Image). **Fix**: Nur die ersten 16 Bytes lesen, validieren, dann gezielt Bytes 12-15 mit `FileStream.Seek` + `Write` überschreiben — statt die gesamte Datei laden/schreiben. | | |
| TASK-028 | **P1 – RemoveCopierHeader: Backup mit .bak überschreibt ggf. vorherige Backups (FeatureService.cs Zeile 1457)**: `File.Copy(path, path + ".bak", overwrite: true)` überschreibt stillschweigend ein vorheriges `.bak`. Bei zwei aufeinanderfolgenden Reparaturversuchen geht das Original verloren. **Fix**: Timestamped Backup-Name: `path + $".bak.{DateTime.UtcNow:yyyyMMdd_HHmmss}"`. | | |
| TASK-029 | **P2 – OnMobileWebUI: Detached Process nie beendet (MainWindow.xaml.cs Zeile ~2870-2890)**: Startet `dotnet run --project RomCleanup.Api` als Hintergrundprozess. Der Prozess wird nirgends gespeichert, nicht beim Schließen der App beendet, und nicht überwacht. Bei mehrfachem Klick werden mehrere API-Instanzen gestartet. **Fix**: Process-Referenz speichern, beim App-Close beenden, Button nach Start deaktivieren. | | |
| TASK-030 | **P2 – GetContextMenuRegistryScript: Pfad-Escaping unvollständig (FeatureService.cs Zeile 1152-1153)**: `exePath.Replace("\\", "\\\\")` escaped Backslashes für `.reg`-Datei, aber escaped NICHT Anführungszeichen im Pfad. Ein Pfad mit `"` könnte zu Registry-Injection führen. **Fix**: Zusätzlich `exePath.Replace("\"", "\\\"")` anwenden. | | |

### Runde 5 — WPF Features: UI State & Korrektheit

- GOAL-005: Alle UI-State-, Korrektheits- und Determinismus-Bugs in den WPF-Handlern identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-031 | **P0 – Konsolen-Filter-Checkboxen nie ausgewertet (MainWindow.xaml Zeile ~320-340)**: Die Checkboxen `chkConsPS1`, `chkConsPS2`, `chkConsSNES` etc. im XAML sind als UI-Elemente definiert, aber niemals an ViewModel-Properties gebunden und nie beim Erstellen der `RunOptions` ausgelesen. Der Benutzer sieht Filter-Optionen, die keinerlei Wirkung haben. **Fix**: Entweder Checkboxen an ViewModel-Properties binden und in `RunOptions` berücksichtigen, oder aus dem UI entfernen. | | |
| TASK-032 | **P0 – SimpleMode Region-Auswahl nie in PreferXX übersetzt (MainViewModel.cs Zeile ~80-100)**: `SimpleRegionIndex` Property existiert (Wert 0-3 für EU/US/JP/Alle), wird aber nirgends in die tatsächlichen `PreferEU`, `PreferUS`, `PreferJP` Booleans übersetzt. Im Simple-Modus wählt der User eine Region-Präferenz, die ignoriert wird. **Fix**: Im Setter von `SimpleRegionIndex` die entsprechenden `PreferXX`-Properties setzen. | | |
| TASK-033 | **P1 – _conflictPolicy Dead State (MainWindow.xaml.cs Zeile ~790)**: `_conflictPolicy` wird von `OnConflictPolicy` gesetzt, aber nie an den `RunOrchestrator` übergeben. Der Policy-Dialog funktioniert (User wählt "Skip"/"Overwrite"/"Rename"), aber die Wahl hat keinen Effekt. **Fix**: `_conflictPolicy` in die `RunOptions` integrieren und im Orchestrator auswerten. | | |
| TASK-034 | **P1 – Rollback Undo/Redo-Stacks sind Platzhalter (MainWindow.xaml.cs Zeile ~540-570)**: `_rollbackUndoStack` und `_rollbackRedoStack` speichern Audit-Pfade, aber die tatsächliche Undo/Redo-Logik (Dateien zurückverschieben) ist NIE implementiert. `OnRollbackUndo()` zeigt eine Nachricht an, führt aber keinen Rollback durch. **Fix**: Audit-CSV parsen → File-Moves rückgängig machen für echtes Undo. Komplexes Feature, mindestens mit klarer „Nicht implementiert"-Nachricht statt täuschender UI. | | |
| TASK-035 | **P1 – Watch Mode: Events während Move-Lauf verloren (MainWindow.xaml.cs Zeile ~440-470)**: `FileSystemWatcher` löst bei Dateisystem-Änderungen `_watchDebounceTimer` aus, der einen DryRun startet. Wenn gerade ein Move läuft (`_vm.IsBusy`), wird der Event einfach verworfen. **Fix**: Events in eine Queue einreihen und nach Ende des aktuellen Runs verarbeiten. | | |
| TASK-036 | **P2 – ShowTextDialog: Hard-coded Dark-Theme-Farben (MainWindow.xaml.cs Zeile ~3050-3080)**: `ShowTextDialog()` setzt `Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))` und `Foreground = Brushes.LimeGreen` direkt. Bei einem Light-Theme wäre dieses Dialog trotzdem dunkel. **Fix**: Farben aus dem aktuellen ResourceDictionary lesen statt Hard-Coding. | | |
| TASK-037 | **P2 – CreateRunCancellation: Race Condition bei Dispose (MainViewModel.cs Zeile ~510-530)**: `CreateRunCancellation()` disposed den alten CTS und erstellt einen neuen. Ein Threading-Race: Wenn Code gerade `_cts.Cancel()` aufruft, während `CreateRunCancellation()` den CTS disposed, gibt es eine `ObjectDisposedException`. **Fix**: `Interlocked.Exchange` + Try/Catch oder den alten CTS erst disposen, nachdem der neue gesetzt ist. | | |
| TASK-038 | **P2 – IsBusy nicht in allen Fehlerpfaden zurückgesetzt**: Mehrere async-Handler (OnIntegrityMonitor, OnBackupManager) setzen `_vm.IsBusy = true` am Anfang. Bei unerwarteten Exceptions (OOM, StackOverflow, ThreadAbort) wird `IsBusy` nie zurückgesetzt → UI permanent blockiert. **Fix**: `try/finally { _vm.IsBusy = false; }` in allen Handlern. | | |
| TASK-039 | **P2 – OnClosing: Rekursiver Aufruf (MainWindow.xaml.cs Zeile ~380-410)**: `OnClosing` ruft unter bestimmten Bedingungen `Close()` auf, was `OnClosing` erneut auslöst. Kann zu Endlosrekursion oder doppelten Cleanup-Aktionen führen. **Fix**: Guard-Flag `_isClosing` setzen und bei Re-Entry sofort returnieren. | | |
| TASK-040 | **P2 – ExportRetroArchPlaylist: Pfade werden nicht escaped (FeatureService.cs Zeile 1010)**: `w.MainPath` wird direkt als JSON-Wert in die Playlist geschrieben. `JsonSerializer.Serialize` escaped korrekt, aber Windows-Pfade mit Backslashes werden als `\\` serialisiert, was manche RetroArch-Versionen nicht korrekt parsen. **Fix**: Pfade mit `.Replace('\\', '/')` in Forward-Slashes konvertieren für Kompatibilität. | | |

### Runde 6 — WPF Features: FeatureService Methoden

- GOAL-006: Alle Bugs in den 49 public static Methoden von `FeatureService.cs` identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-041 | **P1 – AnalyzeHeader: SNES-Erkennung False Positive (FeatureService.cs Zeile 380-400)**: Die SNES LoROM/HiROM-Erkennung prüft, ob Bytes an Position 0x7FC0 (LoROM) oder 0xFFC0 (HiROM) als ASCII-Text interpretierbar sind. Jede beliebige Datei ≥32KB, die dort zufällig druckbare ASCII-Zeichen hat, wird als SNES-ROM erkannt. **Fix**: Zusätzlich SNES-Header-Checksum-Felder validieren (Bytes 0x7FDC-0x7FDF: Checksum + Complement müssen 0xFFFF ergeben). | | |
| TASK-042 | **P1 – CreateBaseline: Sequentielles Hashing pro Datei (FeatureService.cs Zeile 477-495)**: `CreateBaseline()` erstellt pro Datei einen `Task.Run(() => hash)` und awaitet sofort. Das erzeugt N sequentielle Task-Wechsel statt Batch-Parallelismus. Bei 10.000 Dateien massiver Overhead. **Fix**: `Parallel.ForEachAsync` mit Concurrency-Limit (z.B. `Environment.ProcessorCount`) verwenden. | | |
| TASK-043 | **P2 – DetectConsoleFromPath: Fragile Pfad-Heuristik (FeatureService.cs mehrfach verwendet)**: `DetectConsoleFromPath()` nimmt `parts[^2]` (vorletztes Pfad-Segment) als Konsolenname an. Bei Pfaden wie `D:\ROMs\game.zip` (nur 1 Ebene unter Root) gibt es einen `IndexOutOfRangeException`. **Fix**: Array-Länge prüfen, Fallback auf „Unknown". | | |
| TASK-044 | **P2 – LoadLocale: Relatives Pfad-Probing unsicher (FeatureService.cs Zeile 666-680)**: `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "i18n")` navigiert 5 Ebenen hoch und hofft, den Workspace-Root zu treffen. Bei einem veröffentlichten Build (publish-Ordner) wird ein völlig anderer Pfad getroffen, ggf. außerhalb des App-Verzeichnisses. **Fix**: Lokalisierungsdateien als Embedded Resources oder via konfiguriertem Pfad laden, nicht via relatives Probing. | | |
| TASK-045 | **P2 – AnalyzeStorageTiers: File.Exists + new FileInfo pro Kandidat (FeatureService.cs Zeile 710-720)**: Für jeden `RomCandidate` wird `File.Exists` + `new FileInfo` aufgerufen. Bei 50k Kandidaten sind das 100k Syscalls. **Fix**: `FileInfo` einmal pro Datei erstellen (statt `File.Exists` + `new FileInfo`), FileInfo.Exists als Property nutzen. | | |
| TASK-046 | **P2 – CronFieldMatch: Step-Berechnung falsch (FeatureService.cs Zeile 965-970)**: Bei Cron-Step-Ausdrücken wie `*/5` wird `value % step == 0` geprüft. Aber `*/5` in Cron bedeutet „jeder 5. ab 0", d.h. `0, 5, 10, 15, ...`. Die Implementierung prüft nur `value % step == 0`, was korrekt ist für `*/step`, aber NICHT für Ausdrücke wie `10-30/5` (bedeutet `10, 15, 20, 25, 30`). Der Range-Start wird ignoriert. **Fix**: Bei `x-y/step` den Startpunkt berücksichtigen: `(value - lo) % step == 0 && value >= lo && value <= hi`. | | |
| TASK-047 | **P2 – ExportCollectionCsv: CSV-Injection nicht vollständig verhindert (FeatureService.cs Zeile 233-260)**: Die Methode escapt Felder mit Anführungszeichen (Standard-CSV), aber prüft nicht auf führende `=`, `+`, `-`, `@` Zeichen. In Excel öffnet ein Feld wie `=IMPORTXML(...)` eine Formel. **Fix**: Felder, die mit `=`, `+`, `-`, `@` beginnen, mit führendem Hochkomma (`'`) previxen oder Tab-Prefix (`\t`). | | |
| TASK-048 | **P2 – CompareDatFiles: Duplicate Code mit OnDatDiffViewer (FeatureService.cs Zeile 1278 + MainWindow.xaml.cs Zeile 1640)**: `CompareDatFiles()` und `OnDatDiffViewer()` enthalten nahezu identische DAT-Diff-Logik. `OnDatDiffViewer` ruft NICHT `CompareDatFiles()` auf, sondern reimplementiert die Logik inline. **Fix**: `OnDatDiffViewer` sollte `FeatureService.CompareDatFiles()` aufrufen statt eigene Implementierung. | | |
| TASK-049 | **P3 – ClassifyGenre: Naive Keyword-basierte Klassifikation (FeatureService.cs Zeile 1039)**: „gun" matcht „Gundam" (RPG), „war" matcht „Edward" (Adventure), „sim" matcht „Simon" (Platformer). Viele False Positives. **Fix**: Wortgrenzen-Matching: `\bgun\b`, `\bwar\b`, `\bsim\b` statt `Contains`. | | |
| TASK-050 | **P3 – SearchCommands: Levenshtein ohne Längenbeschränkung (FeatureService.cs Zeile 915-930)**: `LevenshteinDistance` allokiert ein `int[n+1, m+1]` Array. Bei einem sehr langen Query-String (z.B. 10.000 Zeichen) werden 10.000 × 15 = 150k ints allokiert (600KB). Pro Kommando. **Fix**: Query auf max. 50 Zeichen beschränken. | | |

### Runde 7 — WPF Features: Analyse & Reports

- GOAL-007: Bugs in den Analyse- und Report-Features identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-051 | **P1 – GetDuplicateInspector: Audit-CSV ohne Encoding-Erkennung geparst (FeatureService.cs Zeile 108-160)**: Liest `File.ReadAllLines()` und parst CSV manuell. Keine BOM-Erkennung, kein konfigurierter Encoding-Parameter. UTF-8-Dateien mit Sonderzeichen (ß, ü, é) in Dateinamen werden ggf. falsch geparst mit System-Default-Encoding. **Fix**: `File.ReadAllLines(path, Encoding.UTF8)` explizit verwenden. | | |
| TASK-052 | **P2 – GetConversionEstimate: Kompressionsraten-Annahmen hart codiert (FeatureService.cs Zeile 31-60)**: Die Schätzung verwendet fixe Kompressionsfaktoren (z.B. 0.6 für CHD, 0.4 für RVZ). Reale Werte weichen stark ab je nach Spieltyp (Audio-lastige PS1-Spiele: 0.8, leere Sektoren: 0.2). **Fix**: Mindestens als Konfiguration externalisieren oder mit „±30% Abweichung möglich" beschriften. | | |
| TASK-053 | **P2 – CalculateHealthScore: Division durch Null möglich (FeatureService.cs Zeile 66-76)**: Wenn `totalFiles == 0`, wird `(double)verified / totalFiles * 40` berechnet → `DivideByZeroException` (oder `NaN` bei Double-Division). **Fix**: Guard `if (totalFiles == 0) return 0;` am Anfang. | | |
| TASK-054 | **P2 – CheckIntegrity: Baseline-Pfad hart codiert (FeatureService.cs Zeile 497-530)**: Der Baseline-Pfad wird relativ zum AppData-Verzeichnis gesucht. Wenn kein Baseline existiert, gibt die Methode ein leeres Ergebnis zurück ohne Fehlermeldung. Der User erfährt nie, dass zuerst `CreateBaseline` nötig ist. **Fix**: Explizite Fehlermeldung wenn kein Baseline existiert. | | |
| TASK-055 | **P3 – BuildCloneTree: Limitierung auf 50 Gruppen ohne Sortierung (FeatureService.cs Zeile 816-830)**: `.Take(50)` ohne vorheriges Sortieren zeigt die ersten 50 in undefinierter Reihenfolge. **Fix**: `groups.OrderByDescending(g => g.Losers.Count).Take(50)` für die relevantesten zuerst. | | |

### Runde 8 — WPF Features: Infrastruktur & Deployment

- GOAL-008: Bugs in den Infrastruktur-, Deployment- und Konfigurations-Features identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-056 | **P1 – GenerateDockerfile: Expose 5000 ohne TLS (FeatureService.cs Zeile 1105-1117)**: Das generierte Dockerfile exponiert Port 5000 mit `http://+:5000`. In einem Docker-Netzwerk ist die API unverschlüsselt zugänglich. Der `ROM_CLEANUP_API_KEY` wird im Klartext übertragen. **Fix**: HTTPS konfigurieren via ASPNETCORE_URLS=https://+:5001 und Zertifikat-Mounting, oder mindestens Warnhinweis im generierten Dockerfile. | | |
| TASK-057 | **P2 – OnPluginMarketplace: Dummy-Implementierung (MainWindow.xaml.cs Zeile ~2810-2830)**: Zeigt eine hartcodierte Liste von 5 Plugins an. Kein Download, keine Installation, keine Verifizierung. Der Name „Marketplace" suggeriert Funktionalität die nicht existiert. **Fix**: Mindestens als „Vorschau" oder „Coming Soon" beschriften, oder Feature-Button ausblenden. | | |
| TASK-058 | **P2 – OnDockerContainer: GenerateDockerCompose enthält API-Key als Env-Variable (FeatureService.cs Zeile 1135)**: `ROM_CLEANUP_API_KEY=${ROM_CLEANUP_API_KEY}` in docker-compose.yml setzt voraus, dass die Env-Variable gesetzt ist. Ohne sie startet der Container mit leerem API-Key. **Fix**: Docker Secrets verwenden oder Warnhinweis im generierten YAML. | | |
| TASK-059 | **P2 – OnWindowsContextMenu: Registry-Script ohne UAC-Prüfung (MainWindow.xaml.cs Zeile ~2920-2930)**: Das generierte .reg-Script schreibt nach `HKEY_CURRENT_USER` (kein Admin nötig), aber `File.WriteAllText(path, regScript)` schreibt den Script an einen beliebigen Pfad ohne Validierung. **Fix**: Pfad via SaveFileDialog validieren (wird bereits getan — OK). Aber: Das Script selbst enthält `Environment.ProcessPath` das zur Build-Zeit gesetzt wird, nicht zur Laufzeit des Registry-Eintrags. **Fix**: Hinweis ausgeben, dass der Pfad absolut ist und bei App-Verschiebung ungültig wird. | | |
| TASK-060 | **P2 – OnFtpSource: FTP ohne Verschlüsselung (MainWindow.xaml.cs Zeile ~2830-2850)**: Verwendet FTP (nicht SFTP/FTPS). Credentials werden im Klartext übertragen. **Fix**: Mindestens FTPS/SFTP empfehlen, Warnhinweis bei unverschlüsseltem FTP anzeigen. | | |
| TASK-061 | **P2 – SettingsService: LoadInto synchron auf UI-Thread (SettingsService.cs Zeile ~50-80)**: JSON-Datei wird synchron gelesen und deserialisiert. Bei großen Settings-Dateien oder langsamen Datenträgern (NAS) friert die UI ein. **Fix**: `await Task.Run(() => LoadInto(...))` oder async File-I/O. | | |
| TASK-062 | **P3 – SettingsService: Kein Versions-Feld für Migration (SettingsService.cs)**: Die Settings-JSON hat kein `version`-Feld. Bei Strukturänderungen (z.B. neues Feld, umbenanntes Feld) gibt es keinen Migrationspfad — die alte Datei wird entweder korrekt geladen (wenn abwärtskompatibel) oder stillschweigend Teile ignoriert. **Fix**: `"version": 1` Feld hinzufügen und Migrations-Logik bei Ladezeit. | | |
| TASK-063 | **P3 – IsPortableMode: Marker-Datei relativ zu BaseDirectory (FeatureService.cs Zeile 691)**: `AppContext.BaseDirectory` zeigt im Debug auf den Build-Output-Ordner (`bin/Debug/net10.0-windows/`). Die `.portable`-Datei muss dort liegen, nicht im Workspace-Root. **Fix**: Dokumentieren, oder zusätzlich `Directory.GetCurrentDirectory()` prüfen. | | |

### Runde 9 — CI/CD Pipeline

- GOAL-009: Alle Lücken in `test-pipeline.yml` identifizieren und Fixes planen

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-064 | **P1 – Coverage-Gate ist kosmetisch (test-pipeline.yml Zeile 37-38)**: Der Step heißt „Unit + Coverage Gate", sammelt Cobertura-Coverage-Daten, enforced aber KEIN Minimum-Threshold. Die Coverage wird nur als Artifact hochgeladen. Das CI ist grün auch bei 0% Coverage. **Fix**: reportgenerator Tool installieren + Threshold-Check via coverlet mit --threshold 50. | | |
| TASK-065 | **P2 – Nur Windows-CI (test-pipeline.yml Zeile 19)**: Tests laufen nur auf `windows-latest`. Plattformspezifische Bugs (Path-Separator, Case-Sensitivity, Symlink-Verhalten) auf Linux/macOS werden nie erkannt. Relevant weil API und CLI theoretisch cross-platform sind. **Fix**: Matrix-Build mit `[windows-latest, ubuntu-latest]` für die non-WPF-Projekte. | | |
| TASK-066 | **P2 – Kein NuGet-Cache (test-pipeline.yml)**: Jeder CI-Lauf lädt alle NuGet-Pakete neu herunter. Das ist langsam und belastet den NuGet-Mirror. **Fix**: `actions/cache@v4` mit `path: ~/.nuget/packages` und `key: nuget-${{ hashFiles('**/*.csproj') }}`. | | |
| TASK-067 | **P2 – Governance prüft nur .csproj References (test-pipeline.yml Zeile 62-72)**: Der Dependency-Direction-Check prüft nur `<ProjectReference>` in .csproj-Dateien. `using RomCleanup.Infrastructure` in einer Core-Datei (ohne ProjectReference) würde nicht erkannt — es kompiliert nicht, aber der Check wäre irreführend grün. **Fix**: Zusätzlich `grep -r "using RomCleanup.Infrastructure" src/RomCleanup.Core/` als Guard hinzufügen. | | |
| TASK-068 | **P3 – Keine Mutation-Tests (test-pipeline.yml)**: Kein Stryker.NET oder ähnliches Tool. Test-Qualität wird nicht automatisch geprüft. **Fix**: `dotnet tool install dotnet-stryker` + Stryker-Lauf als optionaler Job (continue-on-error). | | |
| TASK-069 | **P3 – dotnet-version '10.0.x' kann Preview-SDKs einschließen (test-pipeline.yml Zeile 27)**: Wenn Microsoft ein .NET 10 Preview veröffentlicht, könnte der CI-Lauf ein anderes SDK als lokal verwenden. **Fix**: `include-prerelease: false` setzen (Default, aber explizit dokumentieren) oder exakte Version pinnen. | | |
| TASK-070 | **P3 – Kein SBOM-Generierung (test-pipeline.yml)**: Keine Software Bill of Materials. Für Supply-Chain-Security empfohlen. **Fix**: `dotnet CycloneDX` als optionalen Step hinzufügen. | | |

### Runde 10 — WPF Features: Konfiguration & Profile

- GOAL-010: Bugs in Profil-, Konfigurations- und Lokalisierungs-Features identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-071 | **P1 – OnProfileSave/Load: Settings-Dateien ohne Validierung importiert (MainWindow.xaml.cs Zeile ~730-800)**: `File.Copy(path, settingsDir + "settings.json", overwrite: true)` kopiert eine beliebig gewählte JSON-Datei als Settings. Keine Schema-Validierung. Eine manipulierte oder beschädigte JSON-Datei überschreibt gültige Settings. **Fix**: JSON gegen Schema validieren (z.B. `data/schemas/settings.schema.json`) vor dem Kopieren. Bei Validierungsfehler: Abbruch + Fehlermeldung. | | |
| TASK-072 | **P1 – OnConfigImport: Überschreibt Settings ohne Backup (MainWindow.xaml.cs Zeile ~795-800)**: Wie TASK-071, aber über ConfigImport-Button. Die aktuellen Settings werden vor dem Überschreiben nicht gesichert. **Fix**: Vor Import automatisch `settings.json.bak` erstellen. | | |
| TASK-073 | **P2 – OnApplyLocale: UI-Strings nicht vollständig aktualisiert (MainWindow.xaml.cs Zeile ~830)**: Ändert `_vm.CurrentLocale` und lädt neue Strings, aber bereits angezeigte UI-Elemente (Labels, Tooltips, GroupBox-Header) werden nicht aktualisiert. Nur neue Dialog-Texte verwenden die neue Locale. **Fix**: `OnPropertyChanged` für alle lokalisierbaren Properties feuern oder Binding-Refresh triggern. | | |
| TASK-074 | **P2 – OnAutoProfile: Console-Detection heuristisch (MainWindow.xaml.cs Zeile ~870)**: Erkennt Konsolentyp aus Ordnernamen, dann lädt passende Profile. Bei unbekannten Ordnernamen wird „Standard"-Profil geladen, was ggf. falsche Einstellungen hat. **Fix**: Bei unbekanntem Konsolen-Key den Benutzer fragen statt stillschweigend Default laden. | | |
| TASK-075 | **P2 – ExportUnified: JSON enthält sensitive Pfade (MainWindow.xaml.cs Zeile ~780-785)**: `GetCurrentConfigMap()` sammelt alle Settings inkl. `DatRoot` und `ToolPaths` mit absoluten Pfaden. Beim Export/Sharing leakt der Benutzer seine Verzeichnisstruktur. **Fix**: Pfade vor Export anonymisieren oder Warnhinweis anzeigen. | | |

### Runde 11 — WPF Features: Sammlung & Visualisierung

- GOAL-011: Bugs in den Sammlungs- und Visualisierungs-Features identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-076 | **P2 – OnCoverScraper: Placeholder-Implementierung (MainWindow.xaml.cs Zeile ~1850-1870)**: Zeigt hartcodierte Cover-Quellen-Liste. Kein tatsächlicher Download, keine Bildanzeige. Der Button-Name suggeriert Funktionalität die nicht existiert. **Fix**: Als „Coming Soon" beschriften oder Button deaktivieren. | | |
| TASK-077 | **P2 – OnPlaytimeTracker: Dateisystem-Timestamps als Spielzeit-Proxy (MainWindow.xaml.cs Zeile ~1900-1930)**: Verwendet `LastAccessTime` als Proxy für „zuletzt gespielt". Windows aktualisiert `LastAccessTime` bei vielen Operationen (Virus-Scanner, Backup-Tools), nicht nur beim Spielen. Vollständig unzuverlässig. **Fix**: Entweder prominenten Disclaimer anzeigen oder Feature entfernen. | | |
| TASK-078 | **P2 – BuildVirtualFolderPreview: DetectConsoleFromPath IndexOutOfRange (FeatureService.cs Zeile 840-855)**: Verwendet `DetectConsoleFromPath()` (siehe TASK-043) für jeden Kandidaten. Bei flachen Pfadstrukturen `IndexOutOfRangeException`. **Fix**: Siehe TASK-043 — zentraler Fix löst alle Caller-Bugs. | | |
| TASK-079 | **P3 – OnCollectionSharing: Export enthält absolute Pfade (MainWindow.xaml.cs Zeile ~1950)**: Exportiert die Sammlung als JSON mit absoluten `MainPath`-Werten. Beim Import auf anderem System sind alle Pfade ungültig. **Fix**: Relative Pfade ab Root exportieren. | | |

### Runde 12 — Zusammenfassung & Priorisierung

- GOAL-012: Alle Findings konsolidieren und nach Priorität sortieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-080 | **Konsolidierung P0 (6 Findings)**: TASK-020 (XXE OnDatDiffViewer), TASK-021 (XXE OnArcadeMergeSplit), TASK-022 (XXE CompareDatFiles), TASK-023 (XXE LoadDatGameNames), TASK-031 (Konsolen-Filter unwirksam), TASK-032 (SimpleMode Region unwirksam). Alle P0s müssen vor Release gefixt werden. | | |
| TASK-081 | **Konsolidierung P1 (22 Findings)**: TASK-001, TASK-011, TASK-012, TASK-013, TASK-014, TASK-024, TASK-025, TASK-026, TASK-027, TASK-028, TASK-033, TASK-034, TASK-035, TASK-037, TASK-041, TASK-042, TASK-051, TASK-056, TASK-064, TASK-071, TASK-072. Fix-Reihenfolge: Security (XXE-Helper) → Korrektheit (Dedup/Scoring) → UX (Dead State). | | |
| TASK-082 | **Konsolidierung P2 (38 Findings)**: Alle TASK mit P2-Kennzeichnung. Gruppierbar: Security (5), Korrektheit (12), Performance (6), UX/Dead-Features (8), CI (4), Code-Qualität (3). | | |
| TASK-083 | **Konsolidierung P3 (17 Findings)**: Alle TASK mit P3-Kennzeichnung. Niedrige Priorität, aber fortlaufend beheben bei Gelegenheit. | | |

---

## 3. Alternatives

- **ALT-001**: XXE-Fix einzeln pro Stelle statt zentrale Helper-Methode. Abgelehnt: 6 Stellen mit identischem Pattern → zentrale Methode vermeidet Future-Regressions.
- **ALT-002**: MD5 in PS3-Hashing beibehalten, da keine kryptographische Sicherheit nötig. Abgelehnt: Projekt-Guideline verbietet gebrochene Hash-Algorithmen; SHA256-Migration ist trivial (1 Zeile).
- **ALT-003**: Coverage-Gate als separate GitHub Action statt inline. Möglich, aber erhöht Komplexität ohne Mehrwert.
- **ALT-004**: Alle Placeholder-Features (CoverScraper, PluginMarketplace, PlaytimeTracker) komplett entfernen. Möglich, aber „Coming Soon"-Label ist weniger destruktiv und erhält die Feature-Roadmap.
- **ALT-005**: FolderDeduplicator Winner-Selection an DeduplicationEngine delegieren statt eigene Logik. Strukturell sauberer, aber erfordert Refactoring der Schnittstelle (FolderDeduplicator arbeitet mit Ordnern, DeduplicationEngine mit RomCandidates).

---

## 4. Dependencies

- **DEP-001**: `System.Xml.XmlReaderSettings` — bereits in .NET 10 BCL enthalten, keine neuen Packages nötig
- **DEP-002**: `dotnet-reportgenerator-globaltool` — für Coverage-Threshold-Enforcement in CI (NuGet Tool)
- **DEP-003**: `dotnet-stryker` — für Mutation-Testing in CI (NuGet Tool, optional)
- **DEP-004**: Fix für TASK-022/023 erfordert keine neuen Dependencies — nur sichere XML-Reader-Konfiguration
- **DEP-005**: Coverage-Gate (TASK-064) hängt von bestehender Cobertura-Coverage-Collection ab (bereits konfiguriert)

---

## 5. Files

- **FILE-001**: `src/RomCleanup.Core/Classification/DiscHeaderDetector.cs` — 6 Findings (TASK-001 bis TASK-006)
- **FILE-002**: `src/RomCleanup.Contracts/Models/DatIndex.cs` — 4 Findings (TASK-007 bis TASK-010)
- **FILE-003**: `src/RomCleanup.Infrastructure/Deduplication/CrossRootDeduplicator.cs` — 2 Findings (TASK-011, TASK-017)
- **FILE-004**: `src/RomCleanup.Infrastructure/Deduplication/FolderDeduplicator.cs` — 7 Findings (TASK-012 bis TASK-019)
- **FILE-005**: `src/RomCleanup.UI.Wpf/MainWindow.xaml.cs` — 30+ Findings (TASK-020, TASK-021, TASK-024-039, TASK-071-079)
- **FILE-006**: `src/RomCleanup.UI.Wpf/MainWindow.xaml` — 1 Finding (TASK-031)
- **FILE-007**: `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs` — 3 Findings (TASK-032, TASK-037, TASK-038)
- **FILE-008**: `src/RomCleanup.UI.Wpf/Services/FeatureService.cs` — 18 Findings (TASK-022, TASK-023, TASK-041-055)
- **FILE-009**: `src/RomCleanup.UI.Wpf/Services/SettingsService.cs` — 2 Findings (TASK-061, TASK-062)
- **FILE-010**: `.github/workflows/test-pipeline.yml` — 7 Findings (TASK-064 bis TASK-070)

---

## 6. Testing

- **TEST-001**: XXE-Schutz-Tests: DAT-Datei mit `<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>` erstellen, laden via `CompareDatFiles()` / `LoadDatGameNames()` — muss `XmlException` werfen
- **TEST-002**: ReDoS-Test: DiscHeaderDetector mit 128KB-Buffer gefüllt mit „SEGASEGASEGA…" aufrufen — muss in <100ms returnen
- **TEST-003**: Path-Traversal-Tests: `OnTosecDat` mit Dateiname `..\..\evil.dat` — Copy muss blockiert werden
- **TEST-004**: PS3-Dedup Determinismus: 3 Ordner mit identischem Hash in verschiedener Reihenfolge — Winner muss deterministisch sein
- **TEST-005**: Coverage-Gate-Test: CI-Pipeline mit bekanntem Coverage-Wert unter Threshold — Pipeline muss fehlschlagen
- **TEST-006**: GetFolderBaseKey Edge Cases: Leerer String, nur Klammern, Unicode-Zeichen (日本語), extrem langer Name (1000 Chars)
- **TEST-007**: CrossRoot Winner vs. DeduplicationEngine Winner: Gleiche Inputs müssen gleichen Winner produzieren
- **TEST-008**: CronFieldMatch: `10-30/5` muss 10,15,20,25,30 matchen und 11,12,31 ablehnen
- **TEST-009**: AnalyzeHeader False Positive: 64KB Zufallsdaten-Datei darf nicht als SNES erkannt werden
- **TEST-010**: DetectConsoleFromPath: Pfad mit nur 1 Segment, Pfad mit 10 Segmenten, UNC-Pfad — kein Crash
- **TEST-011**: CalculateHealthScore mit totalFiles=0 — muss 0 zurückgeben, kein DivisionByZero
- **TEST-012**: ExportCollectionCsv mit Werten die `=CMD()` enthalten — muss CSV-Injection verhindern
- **TEST-013**: OnClosing Rekursions-Schutz: Simulierter doppelter Close-Aufruf — kein StackOverflow
- **TEST-014**: RepairNesHeader mit Datei >1GB — darf nicht OOM erzeugen
- **TEST-015**: FolderDeduplicator Destination Path Traversal: Ordnername `..\..\Windows` — Move muss blockiert werden

---

## 7. Risks & Assumptions

- **RISK-001**: XXE-Fixes (TASK-020-023) könnten bestehende DAT-Dateien mit harmloser DTD-Deklaration brechen. Mitigation: `DtdProcessing.Ignore` statt `DtdProcessing.Prohibit` verwenden, falls Legacy-DATs DTDs enthalten.
- **RISK-002**: SHA256 statt MD5 für PS3-Hashing (TASK-012) ist 2-3× langsamer. Bei 10.000 PS3-Ordnern spürbar. Mitigation: Nur relevante Key-Files hashen (bereits der Fall), SHA256 ist auf moderner HW schnell genug.
- **RISK-003**: Coverage-Threshold in CI (TASK-064) könnte existierende PRs blockieren wenn Coverage unter 50%. Mitigation: Threshold initial auf 30% setzen und schrittweise erhöhen.
- **RISK-004**: Regex-Kompilierung (TASK-001) erhöht Startup-Time um ~5ms. Vernachlässigbar.
- **RISK-005**: Placeholder-Features (CoverScraper, PluginMarketplace) als „Coming Soon" zu labeln setzt Erwartungen. Mitigation: Nur wenn tatsächlich auf der Roadmap; sonst komplett ausblenden.
- **ASSUMPTION-001**: Alle 6 XDocument.Load-Stellen verarbeiten ausschließlich Logiqx-XML-DATs. Wenn andere XML-Formate unterstützt werden sollen, muss der XXE-Fix entsprechend angepasst werden.
- **ASSUMPTION-002**: `DeduplicationEngine.SelectWinner()` ist die kanonische Winner-Selection-Logik. Alle anderen Winner-Strategien (CrossRoot, FolderDedup) sollten sich daran orientieren.
- **ASSUMPTION-003**: Die CI-Pipeline soll in Zukunft auch Linux-Tests unterstützen, da API und CLI cross-platform sind.

---

## 8. Related Specifications / Further Reading

- Initialer Bug-Audit (55 Findings) — siehe vorherige Session
- [OWASP XXE Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/XML_External_Entity_Prevention_Cheat_Sheet.html)
- [OWASP CSV Injection](https://owasp.org/www-community/attacks/CSV_Injection)
- [.NET XmlReaderSettings.DtdProcessing](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing)
- `docs/REVIEW_CHECKLIST.md` — Projekt-eigene Review-Kriterien
- `docs/TEST_STRATEGY.md` — Test-Strategie-Dokument
- `.claude/rules/cleanup.instructions.md` — Projektweite Coding-Guidelines (Security-Regeln §4)
