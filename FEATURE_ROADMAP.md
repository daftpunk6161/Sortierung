# Feature-Roadmap — RomCleanup (Stand: 2026-03-06)

> Priorisiert nach Quick Wins → Medium → Large. Aufwand: S = 1–2 Tage, M = 3–5 Tage, L = 1–2 Wochen, XL = 2+ Wochen.

---

## Phase 1 — Quick Wins (S, sofort umsetzbar)

### Datei-Management
- [ ] **QW-01 (S):** Datei-Rename nach DAT-Standard — ROM-Dateien nach No-Intro/Redump-Nomenklatur umbenennen (`Rename-RomToDatName` in `Dat.ps1`, DryRun-Support, Audit-Log)
- [ ] **QW-02 (S):** ECM-Dekompression — `.ecm`-Dateien automatisch zu `.bin` entpacken via `ecm2bin`-Tool (neuer Eintrag in `Convert.ps1` Strategy-Map + `tool-hashes.json`)
- [ ] **QW-03 (S):** Archiv-Repack — ZIP↔7z Repack-Option in Convert.ps1 (RAR→ZIP, 7z→ZIP, ZIP→7z mit konfigurierbarer Kompression)
- [ ] **QW-04 (S):** Speicherplatz-Prognose — "Konvertierung zu CHD spart ~X GB" als Zusammenfassung im DryRun-Report (`Get-ConversionSavingsEstimate`)
- [ ] **QW-05 (S):** Detaillierter Junk-Report — Pro Datei Grund der Junk-Klassifikation anzeigen (welche Regel, welcher Tag) im Report-Output

### UI/UX
- [ ] **QW-06 (S):** Keyboard-Shortcuts — Ctrl+R=Run, Ctrl+Z=Undo, F5=Refresh, Ctrl+Shift+D=DryRun, Escape=Cancel (WPF InputBindings in `MainWindow.xaml`)
- [ ] **QW-07 (S):** Dark/Light-Theme-Toggle — Umschaltbarer Theme-Modus mit System-Auto-Detect (`SystemParameters.HighContrast` + ResourceDictionary-Swap)
- [ ] **QW-08 (S):** ROM-Suche/Filter in Ergebnisliste — Textbox mit Live-Filter über klassifizierte ROMs im Report-Tab (`CollectionViewSource` mit Filter)
- [ ] **QW-09 (S):** Duplikat-Heatmap — Visualisierung der Duplikat-Verteilung nach Konsole als horizontales Balkendiagramm im Dashboard

### Automatisierung
- [ ] **QW-10 (S):** PowerShell-Script-Generator — GUI-Konfiguration als reproduzierbares CLI-Kommando exportieren (`Export-CliCommand` Button)
- [ ] **QW-11 (S):** Webhook-Benachrichtigung — Discord/Slack Webhook-URL in Settings, POST bei Run-Ende mit Summary-JSON (`Invoke-WebhookNotification` in `Notification.ps1`)
- [ ] **QW-12 (S):** Portable-Modus — `--Portable` Flag: alle Settings/Logs/Caches relativ zum Programmordner statt `%APPDATA%`

### Reporting
- [ ] **QW-13 (S):** Export nach Excel-CSV — Sammlung als strukturiertes CSV mit allen Metadaten (Konsole, Region, Format, DAT-Status, Größe)
- [ ] **QW-14 (S):** Run-History-Browser — Liste aller bisherigen Runs (Datum, Roots, Modus, Ergebnis) mit Link zum jeweiligen Report/Audit

### Integration
- [ ] **QW-15 (S):** M3U-Auto-Generierung für Multi-Disc — Automatisches Erstellen von `.m3u`-Playlists wenn Disc 1/2/3 eines Spiels erkannt werden
- [ ] **QW-16 (S):** RetroArch-Playlist-Export — Sammlungsdaten als `.lpl`-Datei (RetroArch-Format) mit korrekten Core-Zuweisungen pro Konsole exportieren

---

## Phase 2 — Medium Features (M, nächster Sprint)

### ROM-Sammlung & Bibliothek
- [ ] **MF-01 (M):** Missing-ROM-Tracker — Pro Konsole zeigen welche Spiele laut DAT fehlen, filterbar nach Region. Neue Sektion im Dashboard (`Get-DatMissingGames`)
- [ ] **MF-02 (M):** Cross-Root-Duplikat-Finder — Gleiche ROMs über verschiedene Root-Verzeichnisse hinweg erkennen (Hash-basiert) + Merge-Vorschlag
- [ ] **MF-03 (M):** ROM-Header-Analyse — Interne Header auslesen für NES (iNES/NES2.0), SNES (LoROM/HiROM), GBA, N64 → Erkennung von Bad-Dumps und Header-Anomalien
- [ ] **MF-04 (M):** Sammlung-Completeness-Ziel — User-definierte Zielsets ("100% EU PS1 RPGs") mit Fortschrittsbalken und Fehlende-Liste
- [ ] **MF-05 (M):** Smart-Collections / Auto-Playlists — Dynamische Filter ("Alle PAL RPGs", "Top-Rated", "Ungespielte") aus DAT-Metadaten + User-Tags

### Format-Konvertierung
- [ ] **MF-06 (M):** CSO/ZSO→ISO→CHD-Pipeline — Komprimierte PSP/PS1-Images in einem Schritt konvertieren ohne manuellen Zwischenschritt
- [ ] **MF-07 (M):** NKit→ISO-Rückkonvertierung — NKit-Images zurück zu vollem ISO (via NKit-Tool) vor RVZ-Konvertierung
- [ ] **MF-08 (M):** Konvertierungs-Queue mit Pause/Resume — Lange Konvertierungen pausierbar+fortsetzbar, Status persistiert in `reports/convert-queue.json`
- [ ] **MF-09 (M):** Batch-Verify nach Konvertierung — Automatischer CRC/SHA1-Vergleich vor/nach Konvertierung als Verifizierungsschritt
- [ ] **MF-10 (M):** Konvertierungs-Prioritätsliste — User definiert Zielformat-Hierarchie pro Konsole (z.B. PS1: CHD > BIN/CUE > PBP > CSO)

### DAT-Management
- [ ] **MF-11 (M):** DAT-Auto-Update — Automatischer Check + Download neuer DAT-Versionen mit Changelog-Popup ("3 neue Einträge für SNES")
- [ ] **MF-12 (M):** DAT-Diff-Viewer — Was hat sich zwischen zwei DAT-Versionen geändert? Neue/entfernte/umbenannte Einträge
- [ ] **MF-13 (M):** TOSEC-DAT-Support — Zusätzlich zu No-Intro/Redump auch TOSEC-Kataloge als DAT-Quelle unterstützen
- [ ] **MF-14 (M):** Parallel-Hashing — Multi-threaded SHA1/SHA256-Berechnung via RunspacePool für große Sammlungen (10x Speed bei >5k Dateien)

### UI/UX
- [ ] **MF-15 (M):** Command-Palette — VSCode-artiges Ctrl+Shift+P mit Fuzzy-Suche über alle Funktionen
- [ ] **MF-16 (M):** Split-Panel-Vorschau — Quell-/Zielverzeichnis nebeneinander (Norton-Commander-Stil) für DryRun-Preview
- [ ] **MF-17 (M):** Filter-Builder — Visueller Query-Builder: "Zeige alle PS1 ROMs > 500MB ohne DAT-Match"
- [ ] **MF-18 (M):** Mini-Modus / System-Tray — Minimiert im Tray mit Status-Icon + Tooltip bei Watch-Mode

### Automatisierung
- [ ] **MF-19 (M):** Rule-Engine — User-definierbare Regeln in JSON/GUI: "Wenn Region=JP UND Konsole≠PS1 → entferne"
- [ ] **MF-20 (M):** Conditional-Pipelines — Mehrstufige Aktionske: Sortieren → Konvertieren → Verifizieren → Umbenennen als konfigurierbare Kette
- [ ] **MF-21 (M):** Dry-Run-Vergleich — Zwei DryRuns mit unterschiedlichen Settings Side-by-Side vergleichen
- [ ] **MF-22 (M):** Ordnerstruktur-Vorlagen — Vordefinierte Layouts: "RetroArch", "EmulationStation", "LaunchBox", "Batocera", "Custom"
- [ ] **MF-23 (M):** Run-Scheduler mit Kalender-UI — Visueller Kalender: "Jeden Sonntag 03:00 → Full Scan + Cleanup"

### Sicherheit & Integrität
- [ ] **MF-24 (M):** Integritäts-Monitor — Periodischer Hash-Check: "Haben sich ROMs seit dem letzten Scan verändert?" (Bit-Rot-Erkennung)
- [ ] **MF-25 (M):** Automatische Backup-Strategie — Konfigurierbare inkrementelle Backups vor jeder Operation mit Retention-Policy
- [ ] **MF-26 (M):** ROM-Quarantäne — Verdächtige/unbekannte Dateien in isolierten Quarantäne-Ordner verschieben statt mischen

---

## Phase 3 — Large Features (L, Meilensteine)

### ROM-Bibliothek
- [ ] **LF-01 (L):** ROM-Thumbnail/Cover-Scraping — Boxart/Screenshots via ScreenScraper.fr/IGDB API automatisch herunterladen, im Dashboard anzeigen
- [ ] **LF-02 (L):** Genre-/Tag-Klassifikation — Automatische Genre-Erkennung aus DAT-Metadaten oder Scraping-APIs, filterbar
- [ ] **LF-03 (L):** Emulator-Launcher-Integration — RetroArch `.lpl`, LaunchBox XML, EmulationStation `gamelist.xml`, Playnite Config-Export
- [ ] **LF-04 (L):** Spielzeit-Tracking-Import — Import von RetroAchievements/RetroArch-Spielzeiten → Anzeige welche ROMs tatsächlich genutzt werden

### Format & Datei
- [ ] **LF-05 (L):** IPS/BPS/UPS-Patch-Engine — Automatische Patch-Anwendung mit Backup + Verifizierung (für Übersetzungs-/Bugfix-Patches)
- [ ] **LF-06 (L):** ROM-Header-Reparatur — Bekannte Header-Probleme automatisch fixen (NES-Header, SNES-Copier-Header entfernen)
- [ ] **LF-07 (L):** Arcade ROM-Merge/Split — Non-Merged ↔ Split ↔ Merged-Set-Konvertierung für MAME/FBNEO (wie clrmamepro)
- [ ] **LF-08 (L):** Intelligent Storage Tiering — Häufig genutzte ROMs auf SSD, Rest auf HDD/NAS automatisch verschieben (via Nutzungsstatistik)

### DAT & Verifizierung
- [ ] **LF-09 (L):** Custom-DAT-Editor — Eigene DAT-Dateien erstellen/editieren in der GUI für private Sammlungen/Homebrew
- [ ] **LF-10 (L):** Clone-List-Visualisierung — Parent/Clone-Beziehungen als interaktiver Baum mit Expand/Collapse
- [ ] **LF-11 (L):** Hash-Datenbank-Export — Alle Hashes als portable SQLite-DB oder JSON exportieren (für Tool-Interop)

### UI/UX
- [ ] **LF-12 (L):** Virtuelle Ordner-Vorschau — Treemap/Sunburst-Diagramm: Sammlungsgröße visualisiert nach Konsole/Region/Format
- [ ] **LF-13 (L):** Barrierefreiheit (Accessibility) — Screen-Reader-Support, High-Contrast, UI Automation Peers, skalierbare Schrift
- [ ] **LF-14 (L):** PDF-Report-Export — Professioneller Sammlungs-Report als PDF mit Diagrammen, Statistiken, Cover-Art

### Netzwerk & Cloud
- [ ] **LF-15 (L):** NAS/SMB-Optimierung — Adaptive Batch-Größen, parallele SMB-Transfers, Retry bei Timeouts, Throttling-Profil
- [ ] **LF-16 (L):** FTP/SFTP-Source — ROM-Roots können FTP/SFTP-Pfade sein (Download → Process → Upload-Back)
- [ ] **LF-17 (L):** Cloud-Settings-Sync — Sammlungs-Metadaten (nicht ROMs!) via OneDrive/Dropbox synchronisieren

### Community & Erweiterbarkeit
- [ ] **LF-18 (L):** Plugin-Marketplace-UI — In-App-Browser für Community-Plugins mit Install/Update/Bewertung
- [ ] **LF-19 (L):** Rule-Pack-Sharing — Community Regions-/Junk-/Alias-Regeln teilen und importieren (signiert)
- [ ] **LF-20 (L):** Theme-Engine — Custom WPF-Themes als installierbare Plugins (ResourceDictionary-basiert)

---

## Phase 4 — XL / Strategische Features (v2.0 / C#-Migration)

### Plattform
- [ ] **XL-01 (XL):** Docker-Container — CLI + REST-API als Docker-Image für Headless-Server (TrueNAS, Unraid, Synology)
- [ ] **XL-02 (XL):** Mobile-Web-UI — Responsive Web-Frontend für die REST-API (React/Vue), Read-Only-Monitoring vom Handy
- [ ] **XL-03 (XL):** Windows-Context-Menu — Shell-Extension: Rechtsklick auf Ordner → "Mit RomCleanup scannen/sortieren"
- [ ] **XL-04 (XL):** PSGallery-Modul — `Install-Module RomCleanup` mit Auto-Update via PowerShell Gallery
- [ ] **XL-05 (XL):** Winget/Scoop-Paket — Paketmanager-Integration für automatische Installation und Updates

### Analyse (erweitert)
- [ ] **XL-06 (XL):** Historische Trendanalyse — Sammlungsgröße/Qualität über Zeit als interaktiver Graph (nach jedem Run)
- [ ] **XL-07 (XL):** Emulator-Kompatibilitäts-Report — ROM↔Emulator-Kompatibilitätsmatrix (basierend auf Community-Listen)
- [ ] **XL-08 (XL):** Sammlungs-Sharing — Export der Sammlungsliste (ohne ROMs!) als teilbare HTML/JSON-Seite

### Performance
- [ ] **XL-09 (XL):** GPU-beschleunigtes Hashing — SHA1/SHA256 via GPU (OpenCL/CUDA) für massive Sammlungen
- [ ] **XL-10 (XL):** USN-Journal-basierter Differential-Scan — NTFS-Journal statt FileSystemWatcher für blitzschnelle Änderungserkennung
- [ ] **XL-11 (XL):** Hardlink/Symlink-Modus — Alternative Ordnerstrukturen (nach Konsole UND Genre) ohne Extra-Speicher via Hardlinks

### Import/Export & Interop
- [ ] **XL-12 (XL):** clrmamepro/RomVault-Import — Datenbank-Import von anderen ROM-Management-Tools
- [ ] **XL-13 (XL):** Multi-Instance-Koordination — Mehrere RomCleanup-Instanzen auf verschiedenen Rechnern synchron halten
- [ ] **XL-14 (XL):** Telemetrie (Opt-in) — Anonyme Nutzungsstatistiken: Feature-Nutzung, populäre Konsolen, Error-Patterns

---

## Empfohlene Umsetzungsreihenfolge

### Sprint 1: Quick Wins Welle 1 (1–2 Wochen)
Fokus: Sofort sichtbarer Mehrwert, minimaler Aufwand.

1. QW-06 — Keyboard-Shortcuts
2. QW-10 — Script-Generator
3. QW-15 — M3U-Auto-Generierung
4. QW-01 — DAT-Rename
5. QW-05 — Detaillierter Junk-Report
6. QW-04 — Speicherplatz-Prognose
7. QW-13 — Excel-CSV-Export

### Sprint 2: Quick Wins Welle 2 (1–2 Wochen)
Fokus: Integration + UX-Polish.

8. QW-16 — RetroArch-Playlist-Export
9. QW-11 — Webhook-Benachrichtigung
10. QW-07 — Dark/Light-Theme
11. QW-08 — ROM-Suche/Filter
12. QW-12 — Portable-Modus
13. QW-02 — ECM-Dekompression
14. QW-03 — Archiv-Repack
15. QW-09 — Duplikat-Heatmap
16. QW-14 — Run-History-Browser

### Sprint 3: Medium Welle 1 (2–3 Wochen)
Fokus: DAT-Power-Features + Konvertierung.

17. MF-14 — Parallel-Hashing
18. MF-11 — DAT-Auto-Update
19. MF-06 — CSO→CHD-Pipeline
20. MF-01 — Missing-ROM-Tracker
21. MF-09 — Batch-Verify
22. MF-08 — Konvertierungs-Queue

### Sprint 4: Medium Welle 2 (2–3 Wochen)
Fokus: Automatisierung + UI-Erweiterung.

23. MF-19 — Rule-Engine
24. MF-22 — Ordnerstruktur-Vorlagen
25. MF-15 — Command-Palette
26. MF-02 — Cross-Root-Duplikate
27. MF-24 — Integritäts-Monitor
28. MF-20 — Conditional-Pipelines

### Sprint 5+: Large Features (nach Bedarf)
29. LF-01 — Cover-Scraping
30. LF-03 — Multi-Emulator-Export
31. LF-05 — Patch-Engine
32. LF-07 — Arcade Merge/Split
33. LF-15 — NAS-Optimierung
…

---

## Metriken & Erfolgskriterien

| Phase | Anzahl Features | Geschätzter Aufwand | Ziel |
|-------|----------------|--------------------:|------|
| Phase 1 (Quick Wins) | 16 | ~20 Tage | Sofort sichtbarer Mehrwert |
| Phase 2 (Medium) | 26 | ~100 Tage | Feature-Parität mit clrmamepro |
| Phase 3 (Large) | 20 | ~200 Tage | Best-in-Class ROM-Management |
| Phase 4 (XL/v2.0) | 14 | ~300+ Tage | Plattform-Tool |
| **Gesamt** | **76** | | |

---

*Erstellt: 2026-03-06 | Nächstes Review: nach Sprint 1*