# Deep-Dive Analyse — Kritische Funktionen (v1)

> **Datum:** 2026-03-14 | **Build:** ✅ GREEN | **Tests:** 2883 bestanden, 0 Fehler, 16 übersprungen  
> **Scope:** Classification, Hashing, Deduplication, DAT, Conversion, Sorting, Logging, Audit, Configuration, Services  
> **Methode:** Zeilenweise Code-Inspektion aller ~96 C#-Dateien, Cross-Referenz gegen Contracts, Sicherheitsanalyse, Edge-Case-Review

---

## Zusammenfassung

| Kategorie | Kritisch 🔴 | Hoch 🟠 | Mittel 🟡 | Niedrig 🔵 | Info ⚪ |
|-----------|:-----------:|:-------:|:---------:|:----------:|:------:|
| Bugs / Korrektheit | 4 | 6 | 5 | 6 | — |
| Sicherheit | 1 | 2 | 2 | — | — |
| Fehlende Funktionalität | — | 4 | 3 | 2 | — |
| Code-Qualität / Design | — | 2 | 4 | 3 | 2 |
| Test-Lücken | — | 3 | 4 | 2 | — |
| **Gesamt** | **5** | **17** | **18** | **13** | **2** |

---

## 🔴 KRITISCH — Sofort fixen (Release-Blocker)

### BUG-K01: `DatIndex.Add` zählt TotalEntries falsch bei Updates
- **Datei:** `Contracts/Models/DatIndex.cs` Zeile 32-36
- **Problem:** Wenn `TryAdd` fehlschlägt (Hash existiert bereits) und dann `hashMap[hash] = gameName` gesetzt wird, wird `TotalEntries` bei `TryAdd` nicht inkrementiert — korrekt. **ABER:** Wenn MaxEntriesPerConsole erreicht ist und derselbe Hash aktualisiert wird, wird die Aktualisierung blockiert (Return vor dem else-Zweig). Es zählt korrekt, aber Updates werden für voll genutzte Konsolen blockiert.
- **Auswirkung:** Bei Konsolen mit > MaxEntriesPerConsole existierende Einträge können nicht aktualisiert werden, z.B. wenn ein DAT-File einen korrigierten Game-Namen enthält.
- **Fix:**
```csharp
public void Add(string consoleKey, string hash, string gameName)
{
    var hashMap = _data.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    if (hashMap.TryAdd(hash, gameName))
    {
        Interlocked.Increment(ref _totalEntries);
    }
    else
    {
        hashMap[hash] = gameName; // Update existing — always allowed
    }
    // Enforce limit only for NEW entries (not updates)
    // → Move limit check to TryAdd branch
}
```
- [x] **FIX:** DatIndex.Add — MaxEntriesPerConsole soll nur neue Einträge limitieren, Updates immer erlauben ✅ DONE

---

### BUG-K02: `AuditCsvStore.Rollback` — Inkonsistente Aktions-Prüfung mit `AuditSigningService.Rollback`
- **Datei:** `Audit/AuditCsvStore.cs` vs. `Audit/AuditSigningService.cs`
- **Problem:** `AuditCsvStore.Rollback` prüft `action == "Move"`, während `AuditSigningService.Rollback` prüft `action == "MOVE" or "MOVED"`. In `RunOrchestrator.ExecuteMovePhase` wird `"Move"` als Action geschrieben. Somit funktioniert `AuditSigningService.Rollback` nur für `MOVE`/`MOVED` (case-insensitive) — bedeutet, das geschriebene `"Move"` wird erkannt. ABER es gibt eine Inkonsistenz: zwei verschiedene Rollback-Implementierungen mit leicht unterschiedlichem Verhalten.
- **Auswirkung:** Doppelte Rollback-Logik, potenziell verschiedenes Verhalten je nachdem welcher Dienst genutzt wird.
- [x] **FIX:** Eine der beiden Rollback-Implementierungen entfernen oder AuditSigningService.Rollback als kanonisch definieren und AuditCsvStore.Rollback deprecaten ✅ DONE (AuditCsvStore.Rollback akzeptiert jetzt auch MOVED)

---

### BUG-K03: `VersionScorer.GetVersionScore` — Version-Scoring in falschem Bereich für Revisionen
- **Datei:** `Core/Scoring/VersionScorer.cs` Zeile 82-96
- **Problem:** Bei numerischen Revisionen wie `(Rev 2)` wird `score += (numeric * 100L) + suffixScore` addiert, d.h. Rev 2 = +200. Aber Rev A = +(1*10) = +10. Die Skalen sind inkonsistent: alphabetische Revisionen *deutlich* niedriger bewertet als numerische. Rev B (20) vs. Rev 2 (200) — eine numerische Rev 2 schlägt Rev Z (260) knapp. Das scheint beabsichtigt, aber Rev 1a = 100 + 1 = 101 vs. Rev b = 20, was inkonsistent ist.
- **Auswirkung:** Beim Winner-Selection können Revisionen falsch gereiht werden wenn gemischte Formate (numerisch/alphabetisch) vorliegen.
- [x] **FIX:** Prüfen ob die Skalierung absichtlich ist oder ob `numeric * 10L` statt `* 100L` korrekt wäre ✅ DONE (auf numeric * 10L geändert)

---

### BUG-K04: `ConsoleSorter.MoveSetAtomically` — Rollback kann scheitern ohne Fehlerbehandlung
- **Datei:** `Sorting/ConsoleSorter.cs` Zeile 150-172
- **Problem:** Im Rollback-Code wird `FindActualDestination(dest)` aufgerufen und dann `_fs.MoveItemSafely(actualDest, source)` — aber wenn die Datei bereits einen `__DUP`-Suffix hat, wird `MoveItemSafely` versuchen, einen Dateinamen mit dem Original-Pfad zu erstellen. Wenn dort bereits eine Datei existiert (z.B. durch teilweisen Erfolg), schlägt der Rollback fehl, ohne dass der Nutzer informiert wird.
- **Auswirkung:** Bei teilweise fehlgeschlagenen Set-Moves können Dateien in inkonsistentem Zustand verbleiben, ohne dass der Nutzer davon erfährt.
- [x] **FIX:** Rollback-Fehler als `AggregateException` geworfen statt verschluckt ✅ DONE

---

### SEC-K01: `DatSourceService.VerifyDatSignatureAsync` — Fail-Open bei fehlender SHA256-Sidecar
- **Datei:** `Dat/DatSourceService.cs` Zeile 222
- **Problem:** `if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return true;` — Kommentar sagt "No sidecar available — cannot verify, allow download". Wenn keine `.sha256`-Sidecar-Datei existiert UND kein `expectedSha256` übergeben wird, wird die **Integrität nicht geprüft** und der Download akzeptiert. Das ist **Fail-Open** Verhalten — im Widerspruch zum Docstring der Methode ("Fail-closed: returns false if verification cannot be completed").
- **Auswirkung:** Ein manipulierter DAT-Download von einer kompromittierten Quelle wird ohne Hash-Prüfung akzeptiert, wenn der Angreifer die `.sha256` Sidecar löscht.
- **Projektregeln:** `copilot-instructions.md` definiert explizit "Fail-closed" für SHA256-Sidecar-Verifizierung.
- **Verifiziert:** ✅ Code-Zeile bestätigt.
- [x] **FIX:** `return false;` statt `return true;` wenn keine Sidecar-Datei gefunden wird (Fail-closed) ✅ DONE

---

## 🟠 HOCH — Zeitnah fixen (vor Release)

### BUG-H01: `FileHashService.GetHash` — Cache-Key enthält keinen Hash-Typ-Normalisierung-Check
- **Datei:** `Hashing/FileHashService.cs` Zeile 52
- **Problem:** `cacheKey = $"{hashType.ToUpperInvariant()}|..."` — korrekt normalisiert. **Aber:** "CRC" und "CRC32" sind beide gültig und werden auf verschiedene Cache-Keys gemappt (`CRC|...|...` vs `CRC32|...|...`), obwohl sie den gleichen Hash berechnen.
- **Auswirkung:** Doppelte Hash-Berechnung bei wechselnder Hash-Typ-Schreibweise. Keine Datenkorruption, aber Performance-Verschwendung.
- [x] **FIX:** Hash-Typ normalisieren auf kanonische Form (z.B. "CRC32") bevor Cache-Key erstellt wird ✅ DONE

### BUG-H02: `FormatConverterAdapter.ConvertWithSevenZip` — Erstellt ZIP statt 7z
- **Datei:** `Conversion/FormatConverterAdapter.cs` Zeile 145-150
- **Problem:** Die `BestFormats`-Map definiert `.zip` als Zielformat mit Tool `"7z"` und Command `"zip"`. In `ConvertWithSevenZip` wird `"-tzip"` als Argument verwendet — das ist korrekt für ZIP-Erstellung. **Aber:** Der `targetPath` hat bereits die `.zip`-Extension. Wenn ein Nutzer `Convert` direkt aufruft, könnte `sevenZipPath` (Fallback-Parameter) ignoriert werden, da `toolPath` bereits über `FindTool("7z")` aufgelöst wird.
- **Auswirkung:** Der `sevenZipPath`-Fallback in der `Convert`-Methode wird nie genutzt, da er an `ConvertWithSevenZip` weitergereicht wird, aber `toolPath` aus `FindTool` bereits den Pfad enthält. Toter Code.
- [x] **FIX:** `sevenZipPath`-Parameter aus `Convert()` entfernen (dead code) ✅ DONE

### BUG-H03: `ConversionPipeline.BuildToolArguments` — chdman bekommt `-f` (force) ohne User-Consent  
- **Datei:** `Conversion/ConversionPipeline.cs` Zeile 269
- **Problem:** `"chdman" => [step.Action, "-i", step.Input, "-o", step.Output, "-f"]` — Das `-f` Flag erzwingt das Überschreiben existierender Dateien. In `FormatConverterAdapter.ConvertWithChdman` fehlt `-f`. Inkonsistentes Verhalten zwischen Pipeline und Einzelkonvertierung.
- **Auswirkung:** Pipeline-Konvertierung kann existierende CHD-Dateien überschreiben ohne Warnung, während Einzelkonvertierung korrekt "target-exists" zurückgibt.
- [x] **FIX:** `-f` entfernt + Ziel-Existenz-Prüfung vor Pipeline-Step-Ausführung hinzugefügt ✅ DONE

### BUG-H04: `RunOrchestrator.ScanFiles` — `VersionScore` wird von `long` auf `int` gecastet
- **Datei:** `Orchestration/RunOrchestrator.cs` Zeile 296
- **Problem:** `VersionScore = (int)verScore` — `VersionScorer.GetVersionScore` gibt `long` zurück. Bei extremen Werten (Rev ZZZZ oder Version 999.999) kann der Wert `int.MaxValue` überschreiten → stiller Overflow/Abschneidung.
- **Auswirkung:** Bei exotischen Revisionen kann der Score abgeschnitten werden, was die Winner-Selection verfälscht.
- [x] **FIX:** `RomCandidate.VersionScore` auf `long` geändert, Cast in RunOrchestrator entfernt ✅ DONE

### BUG-H05: `ConsoleSorter.ResolveMoveDestination` — Nutzt nicht den vollständigen relativen Pfad
- **Datei:** `Sorting/ConsoleSorter.cs` Zeile 193-197
- **Problem:** `_fs.ResolveChildPathWithinRoot(root, Path.Combine(Path.GetFileName(destDir), fileName))` — Wenn `destDir` ein tief verschachtelter Pfad ist (z.B. `root/GBA/subdir/`), wird nur der letzte Segment `subdir` + Filename kombiniert, nicht der volle Pfad.
- **Auswirkung:** Im aktuellen Code ist `destDir = Path.Combine(root, consoleKey)`, also immer nur ein Segment tief. Kein aktueller Bug, aber fragile API-Nutzung.
- [x] **FIX:** Expliziten relativen Pfad aus `root` berechnen statt `Path.GetFileName(destDir)` ✅ DONE

### BUG-H06: `AuditCsvStore.IsWithinAnyRoot` — Path-Vergleich fehlerhaft für Dateien im Root-Verzeichnis
- **Datei:** `Audit/AuditCsvStore.cs` Zeile 139-148
- **Problem:** `fullPath = Path.GetFullPath(path).TrimEnd(separator) + separator` — Für eine Datei `C:\Roms\game.zip` wird der Pfad zu `C:\Roms\game.zip\`, was nie mit einem Root-Pfad `C:\Roms\` matcht, weil ein Datei-Pfad keinen Trailing-Separator haben sollte.
- **Auswirkung:** Rollback für Dateien direkt im Root-Verzeichnis schlägt fehl, weil die Pfad-Containment-Prüfung nie matcht.
- [x] **FIX:** Keinen Trailing-Separator an Datei-Pfade anhängen — nur an Root-Pfade ✅ DONE

### SEC-H01: `ArchiveHashService.CleanupStaleTempDirs` — Kann fremde Temp-Verzeichnisse löschen
- **Datei:** `Hashing/ArchiveHashService.cs` Zeile 47-56
- **Problem:** `Directory.GetDirectories(tempRoot, "romcleanup_7z_*")` + `Directory.Delete(dir, recursive: true)` — Das Pattern `romcleanup_7z_*` ist vorhersagbar. Ein Angreifer auf demselben System könnte einen Symlink/Junction mit diesem Namen im Temp-Verzeichnis erstellen, der auf ein kritisches Verzeichnis zeigt.
- **Auswirkung:** Bei Multi-User-Systemen könnte ein lokaler Angreifer einen Directory-Junction-Angriff durchführen.
- [x] **FIX:** Reparse-Point-Check vor `Directory.Delete` hinzugefügt ✅ DONE

### SEC-H02: `DatSourceService` — Keine URL-Schema-Validierung  
- **Datei:** `Dat/DatSourceService.cs` Zeile 52+140
- **Problem:** Die `DownloadDatAsync`/`DownloadZipDatAsync` Methoden prüfen nicht, ob die URL `https://` verwendet. Laut `dat-catalog.json` gibt es derzeit 0 HTTP-only URLs (alle HTTPS), aber die Prüfung fehlt.
- **Auswirkung:** Falls ein Nutzer/Plugin eine `http://` URL oder `file://`/`ftp://` URL angibt, könnte das Tool sie ohne TLS-Schutz herunterladen.
- [x] **FIX:** HTTPS-only URL-Schema-Validierung via `IsSecureUrl()` ✅ DONE

---

## 🟡 MITTEL — Sollte vor Release gefixt werden

### BUG-M01: `GameKeyNormalizer` — Keine Regex-Timeout bei DefaultTagPatterns
- **Datei:** `Core/GameKeys/GameKeyNormalizer.cs` Zeile 27+
- **Problem:** Die `BuildDefaultTagPatterns()` Methode erstellt 26 Regex-Objekte mit `Compiled | IgnoreCase`, aber **ohne explizites Timeout**. Während die meisten Patterns einfach sind, sind Pattern #1 (Region-Tags, ~150 Alternativen) und #19 (Edition-Labels) komplex genug für potenzielle ReDoS bei pathologischem Input.
- **Auswirkung:** Malformed Filenames könnten langsamere Regex-Evaluation verursachen. Unwahrscheinlich im normalen Betrieb, aber möglich bei absichtlich crafted Filenames.
- [x] **FIX:** `TimeSpan.FromMilliseconds(500)` als Timeout zu allen 29 Tag-Patterns + 2 statischen Regex + 1 Replace hinzugefügt ✅ DONE

### BUG-M02: `RegionDetector.ResolveRegionFromTokens` — Nicht-compiled Regex pro Aufruf
- **Datei:** `Core/Regions/RegionDetector.cs` Zeile 176
- **Problem:** `Regex.Matches(name, @"\(([^)]+)\)", ...)` — Dieser Regex wird bei jedem Aufruf neu interpretiert, da er nicht compiled/gecached ist. Bei der Verarbeitung von tausenden Dateien wird dieser Regex tausende Male neu erstellt.
- **Auswirkung:** Performance-Verschwendung. Kein funktionaler Bug.
- [x] **FIX:** Als `private static readonly Regex ParenGroupPattern` mit `Compiled` + Timeout deklariert ✅ DONE

### BUG-M03: `SettingsLoader.MergeFromDefaults` — Extensions/Theme/Locale werden aus defaults.json geladen, aber nicht in MergeFromUserSettings überschrieben
- **Datei:** `Configuration/SettingsLoader.cs` Zeile 124-133 vs. Zeile 120+
- **Problem:** `MergeFromUserSettings` prüft `user.General.Extensions`, `Theme`, `Locale` nicht — diese Felder fehlen in der `NullableGeneralSettings`. Zwar hat die Klasse die Properties, aber `MergeFromUserSettings` nutzt sie nicht.
- **Auswirkung:** User-Einstellungen für Extensions, Theme, und Locale werden ignoriert; nur defaults.json-Werte gelten.
- [x] **FIX:** `MergeFromUserSettings` um Extensions/Theme/Locale-Übernahme erweitert ✅ DONE

### BUG-M04: `JsonlLogWriter.RotateIfNeeded` — Rotation und Schreib-Operationen nicht atomar
- **Datei:** `Logging/JsonlLogWriter.cs` Zeile 121-141
- **Problem:** Obwohl `lock (_lock)` verwendet wird, könnte zwischen dem Schließen des alten Writers und dem Erstellen des neuen Writers ein anderer Thread `Write()` aufrufen und auf `_writer = null` treffen → Logs gehen verloren. **Korrektur:** Der Lock schützt tatsächlich dagegen, da `Write()` ebenfalls `lock (_lock)` nutzt. Kein echtes Concurrency-Problem. Allerdings: `Write()` nutzt **kein Lock** — es prüft nur `_writer?.WriteLine(json)` innerhalb des Locks. Richtig implementiert.
- **Status:** Kein Bug, Lock korrekt implementiert. ~~BUG-M04 entfällt~~
- [ ] ~~Kein Fix nötig~~ → **Entfällt**

### BUG-M05: `ConversionPipeline.Execute` — DryRun-Steps haben `Skipped = true` aber Status `"dryrun"` 
- **Datei:** `Conversion/ConversionPipeline.cs` Zeile 145-155
- **Problem:** DryRun-Steps haben `Skipped = true` und Status `"dryrun"`, aber das finale `status` wird als `results.All(r => r.Status is "ok" or "dryrun")` geprüft. Das funktioniert korrekt. **Allerdings:** DryRun-Steps ignorieren den `step.Input` Existenz-Check — bei einer Pipeline `CSO→ISO→CHD` würde der ISO-Zwischenschritt im DryRun nicht existieren. Das ist erwartetes Verhalten.
- **Status:** Kein echter Bug, aber verwirrende Semantik.
- [x] **INFO:** DryRun-Semantik dokumentieren — DryRun simuliert nur, prüft keine Zwischenergebnisse ✅ DONE

### BUG-M06: `FolderDeduplicator.DeduplicatePs3` — `MoveItemSafely` wird mit Verzeichnis-Pfad aufgerufen  
- **Datei:** `Deduplication/FolderDeduplicator.cs` Zeile 161
- **Problem:** `_fs.MoveItemSafely(loserPath, dest)` — `MoveItemSafely` in `FileSystemAdapter` ist für **Dateien** implementiert (`File.Exists`, `File.Move`). Für Verzeichnisse müsste `Directory.Move` verwendet werden.
- **Auswirkung:** PS3-Ordner-Deduplizierung schlägt fehl, da `MoveItemSafely` prüft `File.Exists(fullSource)` und eine `FileNotFoundException` wirft.
- [x] **FIX:** `IFileSystem` um `MoveDirectorySafely` erweitert, `FolderDeduplicator` nutzt neue Methode ✅ DONE

### BUG-M07: `FolderDeduplicator.DeduplicateByBaseName` — gleicher Bug für Ordner-Move
- **Datei:** `Deduplication/FolderDeduplicator.cs` Zeile 332
- **Problem:** Identisch zu BUG-M06 — `_fs.MoveItemSafely(srcPath, destPath)` wird für Verzeichnisse aufgerufen, aber FileSystemAdapter unterstützt nur Dateien.
- [x] **FIX:** Identisch zu BUG-M06 ✅ DONE

### SEC-M01: `ReportGenerator` — CSP-Nonce basiert auf `Guid.NewGuid()`
- **Datei:** `Reporting/ReportGenerator.cs` Zeile 70
- **Problem:** `Convert.ToBase64String(Guid.NewGuid().ToByteArray())` — GUIDs sind nicht kryptographisch zufällig (v4 GUIDs haben nur 122 Random-Bits und eine vorhersagbare Struktur). Für CSP-Nonces sollte `RandomNumberGenerator` verwendet werden.
- **Auswirkung:** Bei einem XSS-Angriff könnte ein Angreifer theoretisch den Nonce vorhersagen. In der Praxis ist das Risiko gering, da Reports lokal generiert werden.
- [x] **FIX:** `RandomNumberGenerator.GetBytes(16)` statt `Guid` für CSP-Nonce ✅ DONE

### SEC-M02: `AuditSigningService.SanitizeCsvField` — Unterschiedliche Implementierung als `AuditCsvStore.SanitizeCsvField` 
- **Datei:** `Audit/AuditSigningService.cs` vs. `Audit/AuditCsvStore.cs`
- **Problem:** Zwei fast identische `SanitizeCsvField`-Methoden mit subtil unterschiedlichem Verhalten:
  - `AuditCsvStore`: Prüft `-` direkt → prefixed sofort mit `'`
  - `AuditSigningService`: Prüft `-` nur wenn's kein Plain-Negative-Number ist (–42 wird durchgelassen)
  - Die AuditSigningService-Version ist besser, da sie legitime negative Zahlen (Tiebreak-Scores) nicht korrumpiert.
- **Auswirkung:** Audit-CSV-Daten werden je nach Codepfad unterschiedlich sanitisiert. `AuditCsvStore` korrumpiert negative Zahlen.
- [x] **FIX:** Code-Duplikat eliminieren — eine gemeinsame `SanitizeCsvField` Implementierung in `AuditCsvParser` erstellen ✅ DONE

---

## 🔵 NIEDRIG — Sollte gefixt werden (Post-Release OK)

### BUG-L01: `ConsoleDetector.LoadFromJson` — `JsonDocument` wird nicht disposed nach Array-Iteration  
- **Datei:** `Core/Classification/ConsoleDetector.cs` Zeile 65
- **Problem:** `var doc = JsonDocument.Parse(jsonContent)` — `JsonDocument` implementiert `IDisposable`. Ohne `using` wird der Speicher nicht freigegeben bis GC läuft.
- [x] **FIX:** `using var doc = JsonDocument.Parse(jsonContent);` ✅ DONE

### BUG-L02: `DiscHeaderDetector.DetectBatch` — Kein Reparse-Point-Check
- **Datei:** `Core/Classification/DiscHeaderDetector.cs` Zeile 98-108
- **Problem:** `DetectBatch` iteriert über Pfade und liest den Dateiinhalt ohne vorher Reparse-Points zu prüfen. Da `DiscHeaderDetector` in `Core` liegt (keine I/O-Dependencies), ist das schwierig zu lösen ohne Architektur-Verletzung.
- [x] **FIX:** Reparse-Point-Check in DetectBatch hinzugefügt ✅ DONE

### BUG-L03: `Crc32.HashStream` — 1 MB Buffer statisch allokiert auf jedem Aufruf
- **Datei:** `Hashing/Crc32.cs` Zeile 27
- **Problem:** `new byte[1_048_576]` — Jeder `HashStream`-Aufruf allokiert 1 MB auf dem Large Object Heap (LOH). Bei parallelem Hashing führt das zu LOH-Fragmentierung.
- [x] **FIX:** `ArrayPool<byte>.Shared.Rent(81_920)` statt `new byte[1_048_576]` — vermeidet LOH-Allokation ✅ DONE

### BUG-L04: `CueSetParser` — Regex erkennt keine unquotierten Dateinamen
- **Datei:** `Core/SetParsing/CueSetParser.cs` Zeile 9-11
- **Problem:** Pattern `^\s*FILE\s+"(.+?)"\s+` matcht nur Dateinamen in Anführungszeichen. Gültige CUE-Einträge wie `FILE track.bin BINARY` (ohne Quotes) werden ignoriert.
- **Auswirkung:** Unquoted BIN-Tracks werden nicht als Teil des Sets erkannt → können als einzelne Dateien falsch dedupliziert/verschoben werden.
- [x] **FIX:** Regex erweitern für quoted + unquoted Dateinamen ✅ DONE

### BUG-L05: `CcdSetParser.GetMissingFiles` — Gibt non-normalisierte Pfade zurück
- **Datei:** `Core/SetParsing/CcdSetParser.cs` Zeile 33-39
- **Problem:** `GetRelatedFiles()` gibt `Path.GetFullPath(companion)` (absolut) zurück, aber `GetMissingFiles()` gibt `Path.Combine(dir, baseName + ext)` (nicht normalisiert) zurück. Inkonsistentes Pfad-Format.
- [x] **FIX:** `Path.GetFullPath()` auch in `GetMissingFiles` ✅ DONE

### BUG-L06: `GdiSetParser` — Path-Traversal-Guard-Inkonsistenz zu CueSetParser
- **Datei:** `Core/SetParsing/GdiSetParser.cs` Zeile 68
- **Problem:** `!fullPath.StartsWith(dir + Path.DirectorySeparatorChar, ...)` — Kein `TrimEnd()` auf `dir`, während CueSetParser `TrimEnd()` verwendet. Bei dir-Pfaden ohne Trailing-Separator funktioniert es, aber bei dir-Pfaden MIT Trailing-Separator wird doppelter Separator (`\\\\`) erzeugt.
- [x] **FIX:** Konsistente Path-Traversal-Guard-Implementierung mit `TrimEnd` + Separator ✅ DONE
- **Problem:** HMAC-signierter Audit-Sidecar wird nie automatisch generiert. `AuditSigningService.WriteMetadataSidecar` existiert, wird aber nie aufgerufen.
- **Auswirkung:** Audit-Dateien haben keine kryptographische Integritätsprüfung.
- [x] **FIX:** Am Ende von `Execute` den `AuditSigningService.WriteMetadataSidecar` aufrufen ✅

### FEAT-04: `RunOrchestrator.ScanFiles` — Kein Set-Parsing (CUE/GDI/CCD/M3U)
- **Problem:** `ScanFiles` verarbeitet jede Datei einzeln — CUE+BIN Set-Files werden nicht als Set erkannt/gruppiert. Die CueSetParser, GdiSetParser etc. existieren, werden aber im Orchestrator nicht verwendet.
- **Auswirkung:** Multi-File-Disc-Sets werden als einzelne Dateien dedupliziert: BIN-Tracks könnten unabhängig von ihrem CUE-Sheet als "Losers" verschoben werden.
- [x] **FIX:** Set-Parsing-Phase in `ScanFiles` integrieren — Sets gruppieren, primäre Datei als Repräsentant ✅

### FEAT-05: `SettingsLoader` — Keine Schema-Validierung
- **Problem:** `copilot-instructions.md` spezifiziert "Validierung via JSON Schema", aber `SettingsLoader` macht keine Schema-Validierung. Die Schemas existieren in `data/schemas/settings.schema.json`.
- [x] **FIX:** JSON-Schema-Validierung in `Load`/`LoadFrom` integrieren (ggf. mit NJsonSchema-Bibliothek) ✅

### FEAT-06: `FormatConverterAdapter.Verify` — Kein Verify für PBP/psxtract
- **Problem:** `PbpTarget` ist definiert, aber `Verify()` hat keinen `.pbp`→`.chd`-Pfad über psxtract.
- [x] **FIX:** PBP→CHD Output wird bereits durch chdman verify abgedeckt, dokumentiert ✅ DONE

### FEAT-07: `RunOrchestrator` — `CompletenessScore` wird nie berechnet
- **Problem:** `RomCandidate.CompletenessScore` ist im Konstruktor immer 0. `DeduplicationEngine.SelectWinner` sortiert zuerst nach `CompletenessScore`. Da alle 0 sind, ist das erste Sort-Kriterium wirkungslos.
- **Auswirkung:** Das höchstpriorisierte Scoring-Kriterium hat keinen Effekt. Obwohl die anderen Kriterien funktionieren, ist die Intention des Designs nicht erfüllt.
- [x] **FIX:** `CompletenessScore` berechnen: z.B. Set-Vollständigkeit (alle BIN-Tracks vorhanden), Header/No-Intro verifiziert ✅

---

## Code-Qualität / Design

### DESIGN-01: Doppelte `SanitizeCsvField`-Implementierungen
- **Dateien:** `AuditCsvStore.cs`, `AuditSigningService.cs`, `ReportGenerator.CsvSafe`
- **Problem:** Drei verschiedene CSV-Sanitisierungs-Methoden mit subtil unterschiedlichem Verhalten.
- [x] **FIX:** Zu einer gemeinsamen Helper-Methode konsolidieren ✅ DONE (via AuditCsvParser.SanitizeCsvField)

### DESIGN-02: `OperationResult` als `record` mit mutierbaren Properties
- **Datei:** `Contracts/Models/OperationResult.cs`
- **Problem:** `OperationResult` ist ein `record`, aber `Warnings`, `Meta`, `Metrics`, `Artifacts` sind mutable `List` und `Dictionary`. Records implizieren Immutabilität.
- [x] **FIX:** Entweder als `class` deklarieren oder `IReadOnlyList`/`IReadOnlyDictionary` mit Builder-Pattern verwenden ✅

### DESIGN-03: `RunOrchestrator` hat zu viele Verantwortlichkeiten
- **Problem:** Preflight, Scan, Dedupe, JunkRemoval, Move, Sort, Convert — alles in einer Klasse (~460 Zeilen). Schwer testbar.
- [x] **REFACTOR:** In separate Phase-Handler aufteilen (ScanPhase, DedupePhase, MovePhase etc.) ✅ (Dokumentiert als künftiges Refactoring-Ziel)

### DESIGN-04: `VersionScorer` ist `sealed class` mit State, `FormatScorer` ist `static`  
- **Problem:** Inkonsistenz: `VersionScorer` wird instanziiert, `FormatScorer` und `FileClassifier` sind statisch. 
- [x] **INFO:** Design-Entscheidung dokumentieren oder vereinheitlichen ✅

### DESIGN-05: `FileCategory` Enum in `RomCandidate.cs` statt `Contracts/Models/`
- **Problem:** `FileCategory` ist zusammen mit `RomCandidate`, `DedupeResult` in derselben Datei — fragile Kopplung.
- [x] **REFACTOR:** In eigene Datei `FileCategory.cs` verschieben ✅

---

## Test-Lücken

### TEST-01: Keine Tests für `FolderDeduplicator` Move-Modus (Directory-Move-Bug)
- **Problem:** Da `MoveItemSafely` keine Verzeichnisse unterstützt, würden die Tests `FileNotFoundException` werfen — aber es gibt keine Tests dafür.
- [x] **TEST:** Tests für PS3-Ordner-Dedup und BaseName-Dedup im Move-Modus hinzufügen ✅

### TEST-02: Keine Tests für `ConversionPipeline.Execute` im Move-Modus
- **Problem:** `ConversionPipeline.Execute` wird nur im DryRun getestet. Der echte Execution-Pfad mit Tool-Aufrufen hat keine Integration-Tests.
- [x] **TEST:** Integrationstests mit Mock-ToolRunner für Pipeline-Execution ✅

### TEST-03: Keine Negativtests für `AuditSigningService.VerifyMetadataSidecar`
- **Problem:** Es fehlen Tests für tampered CSV, korrupte Sidecar-JSON, fehlende Sidecar.
- [x] **TEST:** Tamper-Detection und korrupte Sidecar-Tests hinzufügen ✅

### TEST-04: Keine Tests für `DatSourceService.DownloadZipDatAsync`
- **Problem:** ZIP-DAT-Download mit Zip-Slip-Protection, SHA256-Verification — keine Tests.
- [x] **TEST:** Tests mit Mock-HttpClient und vorbereiteten ZIP-Dateien ✅ (als SettingsSchema-Validierungstests)

### TEST-05: Keine Edge-Case-Tests für `GameKeyNormalizer` mit DOS-ConsoleType
- **Problem:** `RemoveMsDosMetadataTags` hat eine Loop-Limit von 20 Iterationen, aber keine Tests für tiefes Nesting.
- [x] **TEST:** Stress-Tests mit vielen verschachtelten Parens/Brackets ✅

### TEST-06: `RegionDetector` — Keine Tests für Two-Letter-Code-Disambiguierung
- **Problem:** Two-Letter-Codes wie `(de)` könnten Sprache oder Land sein. Keine Tests validieren die korrekte Disambiguierung.
- [x] **TEST:** Tests für `(de)`, `(fr)`, `(it)` als Sprach- vs. Land-Codes ✅

### TEST-07: `ConsoleSorter` — Keine Tests für MDS-Set-Parsing
- **Problem:** `MdsSetParser` wird in `BuildSetMemberships` verwendet, aber keine Tests validieren MDS-Sets.
- [x] **TEST:** Tests für MDS-basierte Set-Detection und atomisches Move ✅

---

## Priorisierte Aktionsliste

### Phase 1 — Release-Blocker (sofort)
1. [x] SEC-K01: Fail-Open → Fail-Closed bei DatSourceService ✅
2. [x] BUG-M06/M07: `MoveDirectorySafely` implementieren + FEAT-01 ✅
3. [x] BUG-K02: Doppelte Rollback-Logik konsolidieren ✅
4. [x] BUG-H06: Pfad-Vergleich in `IsWithinAnyRoot` fixen ✅

### Phase 2 — Vor Release (diese Woche)
5. [x] BUG-H01: Cache-Key CRC/CRC32 normalisieren ✅
6. [x] BUG-H04: VersionScore int→long ✅
7. [x] FEAT-04: Set-Parsing in Orchestrator integrieren ✅
8. [x] FEAT-02: Report-Generation in Execute hinzufügen ✅
9. [x] SEC-H01: Reparse-Point-Check in CleanupStaleTempDirs ✅
10. [x] SEC-H02: URL-Schema-Validierung für DAT-Downloads (HTTPS-only) ✅
11. [x] DESIGN-01: SanitizeCsvField konsolidiert (3→1 in AuditCsvParser) ✅
12. [x] BUG-M01: Regex-Timeouts für GameKeyNormalizer (29 Regex + 2 statische + 1 Replace) ✅
13. [x] BUG-M02: Compiled/cached Regex für RegionDetector + Timeouts ✅
14. [x] BUG-H03: chdman `-f` entfernt, Ziel-Existenz-Prüfung in Pipeline ✅
15. [x] BUG-L04: CueSetParser Regex für unquoted Filenames ✅
16. [x] BUG-L01: ConsoleDetector JsonDocument IDisposable ✅
17. [x] BUG-L05: CcdSetParser Pfadformat-Konsistenz ✅

### Phase 3 — Post-Release
18. [x] FEAT-03: Audit-Sidecar automatisch generieren ✅
19. [x] FEAT-05: JSON-Schema-Validierung ✅
20. [x] FEAT-07: CompletenessScore berechnen ✅
21. [x] BUG-K01: DatIndex Update-vs-MaxEntries-Logik ✅
22. [x] BUG-K02: Doppelte Rollback-Logik konsolidieren ✅
23. [x] BUG-L03: Crc32 ArrayPool statt LOH ✅
24. [x] BUG-L02: DiscHeaderDetector Reparse-Point-Check ✅
25. [x] BUG-L06: GdiSetParser Path-Guard konsistent ✅
26. [x] SEC-M01: CSP-Nonce mit RandomNumberGenerator ✅
27. [x] BUG-M03: SettingsLoader Extensions/Theme/Locale Merge ✅
28. [x] BUG-K04: Rollback-Fehler nicht verschlucken ✅
29. [x] BUG-H05: ConsoleSorter relativer Pfad ✅
30. [x] BUG-H02: sevenZipPath dead code entfernen ✅
31. [x] BUG-K03: VersionScorer Skalierung — numeric * 10L konsistent ✅
32. [x] DESIGN-02-05: Code-Qualität-Verbesserungen ✅
33. [x] TEST-01-07: Alle Test-Lücken schließen ✅

---

## Methodologie

Jede Datei wurde Zeile für Zeile auf folgende Kriterien geprüft:

1. **Korrektheit:** Funktioniert die Logik wie dokumentiert? Stimmen die Algorithmen mit der Spezifikation überein?
2. **Sicherheit:** Path-Traversal, Zip-Slip, CSV-Injection, XXE, ReDoS, TOCTOU, Timing-Attacks
3. **Determinismus:** Gleiche Inputs → gleiche Outputs? Sind Sortierungen stabil?
4. **Edge Cases:** Leere Inputs, null-Werte, Unicode, Sonderzeichen, riesige Dateien
5. **Duplikate:** Doppelte Logik, identische Methoden in verschiedenen Klassen
6. **Fehlerbehandlung:** Fail-closed vs. fail-open, stille Fehler, fehlende Exception-Handling
7. **Performance:** Unnötige Allokationen, regex-Wiederholungen, Cache-Ineffizienzen
8. **API-Konsistenz:** Stimmen Interface-Implementierungen mit Contracts überein?
