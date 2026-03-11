# PRD: RomCleanup v2.0 — Umfassendes ROM-Management

> **Dokument 2/3 — Product Requirements**  
> Übergeordnetes Strategie-Dokument: → [01 — Produktstrategie & Roadmap](01-PRODUCT-STRATEGY-ROADMAP.md)  
> Implementierungsdetails: → [03 — Feature Implementation Guide](03-FEATURE-IMPLEMENTATION-GUIDE.md)

**Version:** 1.0  
**Datum:** 2026-03-09  
**Status:** Entwurf  
**Owner:** Product & Engineering  

---

## 1. Executive Summary

RomCleanup ist ein Windows-basiertes Tool zur Verwaltung von ROM-Sammlungen für Retro-Gaming-Enthusiasten. Es bietet regionsbasierte Deduplizierung (1G1R), Entrümpelung (Demos, Betas, Hacks), Formatkonvertierung (CUE/BIN→CHD, ISO→RVZ etc.), DAT-basierte Verifizierung und automatische Konsolen-Sortierung.

**Problem:** Das Tool hat sich organisch entwickelt und leidet unter einer unübersichtlichen GUI, fehlenden Kernfunktionen für ganzheitliches ROM-Management und einer PowerShell-Architektur, die langfristig limitiert (kein natives Async, keine Cross-Platform-GUI). Nutzer müssen für alltägliche Aufgaben wie Rename-nach-DAT, Missing-ROM-Tracking, Konvertierungs-Pipelines oder Emulator-Integration auf externe Tools zurückgreifen.

**Ziel:** RomCleanup wird zur **einzigen Anlaufstelle für ROM-Management** — von der Ersterfassung über Deduplizierung, Konvertierung und Verifizierung bis zur Emulator-Integration. Die v2.0 migriert den Core nach C# .NET 8 (Strangler-Fig-Pattern), überarbeitet die GUI grundlegend (Wizard-Flow, progressive Disclosure, retro-modernes Design) und führt 76 neue Features in 4 Phasen ein.

**Erfolgskriterien:**
- Time-to-safe-run (erster DryRun) < 5 Minuten für Neulinge
- 0 Datenverlust-Incidents (kein Move/Delete ohne Audit + Undo)
- UI-Fehlerabbrüche pro Run < 2%
- Scan-Performance: ≥ 5.000 Dateien/Minute auf SSD, ≥ 1.000 auf HDD
- Rollback-Erfolgsrate > 99,5%

---

## 2. Problem Statement

### 2.1 Aktuelle Schmerzpunkte

| Problem | Auswirkung | Betroffene Persona |
|---------|-----------|-------------------|
| GUI ist unübersichtlich, kein klarer Flow | Neue Nutzer verstehen nicht, was zuerst zu tun ist | Solo Curator |
| Keine M3U-Generierung, kein Emulator-Export | User braucht externe Tools für Multi-Disc und Playlists | Alle |
| Keine DAT-Rename-Funktion | ROMs haben inkonsistente Namen trotz DAT-Match | Collector Pro |
| Keine Missing-ROM-Erkennung | User weiß nicht, was fehlt | Collector Pro |
| Keine Konvertierungs-Queue (Pause/Resume) | Lange CHD-Konvertierungen sind nicht unterbrechbar | Alle |
| Keine Cross-Root-Duplikat-Erkennung | Gleiches ROM in 3 Ordnern erkannt | Collector Pro |
| Kein Theme-Toggle (Dark/Light) | Nutzerpräferenz nicht bedient | Alle |
| Keine Keyboard-Shortcuts | Power-User verlieren Zeit | Collector Pro |
| PowerShell-Limitierungen (kein Async, Regex-Performance) | UI friert bei großen Sammlungen | Alle |
| Fehlende Barrierefreiheit | Screen-Reader, High-Contrast nicht unterstützt | Alle |

### 2.2 Marktlücke

Bestehende Tools (clrmamepro, RomVault, Romcenter) sind leistungsfähig, aber komplex und nicht auf Endanwender-UX ausgelegt. RomCleanup adressiert die Lücke zwischen "alles manuell" und "Experten-Only-Tools" mit einem geführten, sicheren Workflow.

---

## 3. Goals / Non-Goals

### 3.1 Primäre Ziele

- **Z-01:** Vollständiges ROM-Lifecycle-Management in einem Tool (Scan → Klassifizieren → Dedupe → Konvertieren → Verifizieren → Sortieren → Export)
- **Z-02:** GUI-Redesign mit Wizard-Flow, progressiver Disclosure und retro-modernem Design
- **Z-03:** C#/.NET 8 Migration (Strangler-Fig) für Performance, Async, Cross-Platform-Readiness
- **Z-04:** Null-Datenverlust-Garantie durch DryRun-Default, Audit-Logs, Rollback
- **Z-05:** Emulator-Ökosystem-Integration (RetroArch, LaunchBox, EmulationStation, Batocera)
- **Z-06:** DAT-Lifecycle komplett abbilden (Auto-Update, Diff, TOSEC, Missing-Tracker)
- **Z-07:** Automatisierungsfähigkeit für Power-User (Rule-Engine, Pipelines, Scheduler, CLI-Export)
- **Z-08:** Skalierung auf 100k+ Dateien ohne UI-Freeze

### 3.2 Non-Goals (explizit nicht in Scope v2.0)

*Keine Einschränkungen durch den User festgelegt — alle Feature-Roadmap-Items sind in Scope. Die folgenden Punkte werden dennoch explizit ausgeschlossen, weil sie über den Tool-Zweck hinausgehen:*

- **NG-01:** RomCleanup ist kein ROM-Downloader oder Scraper für ROM-Dateien selbst
- **NG-02:** Kein DRM-Umgehungstool — nur legale ROM-Verwaltung
- **NG-03:** Kein vollständiger Emulator — nur Management und Launcher-Integration
- **NG-04:** Kein Cloud-Sync von ROM-Dateien (nur Metadaten/Settings)

---

## 4. Personas & Use Cases

### 4.1 Persona A — Solo Curator ("Max")

| Attribut | Detail |
|----------|--------|
| Profil | Einzelanwender, 1–3 ROM-Sammlungen, 500–5.000 ROMs |
| Erfahrung | Moderat; kennt RetroArch, aber nicht clrmamepro |
| Ziel | Sammlung aufräumen, Duplikate entfernen, richtig benennen |
| Frustration | Angst vor Datenverlust, überfordert von zu vielen Optionen |
| KPI | Time-to-safe-run < 5 Minuten |
| Nutzungsmuster | 1× pro Monat, nach ROM-Download-Session |

**Top Use Cases (Max):**
1. Erstmalige Sammlung scannen und DryRun-Preview sehen
2. Duplikate entfernen (EU-Versionen bevorzugen)
3. ROMs nach Konsole sortieren
4. Multi-Disc-Spiele erkennen und M3U generieren
5. RetroArch-Playlist erstellen

### 4.2 Persona B — Collector Pro ("Sarah")

| Attribut | Detail |
|----------|--------|
| Profil | Große Multi-Root-Bestände, 10.000–100.000+ ROMs |
| Erfahrung | Experte; nutzt DATs, kennt Hash-Verifizierung |
| Ziel | Vollständigkeit pro Konsole/Region, regelmäßige Delta-Läufe |
| Frustration | Langsame Scans, keine Automatisierung, kein Missing-Tracker |
| KPI | Laufzeit < 10 Min für 100k Dateien (Delta-Scan) |
| Nutzungsmuster | Wöchentlich, automatisiert via CLI/Scheduler |

**Top Use Cases (Sarah):**
1. DAT-basierte Verifizierung mit Missing-Report
2. Cross-Root-Duplikat-Finder über 5+ Roots
3. Automatische DAT-Updates mit Diff-Anzeige
4. Batch-Konvertierung BIN/CUE→CHD mit Queue
5. Custom Rule-Engine: "JP-Only SNES-ROMs entfernen"
6. CLI-Script aus GUI-Konfiguration exportieren

### 4.3 Persona C — Team/Archiv-Betreiber ("Alex")

| Attribut | Detail |
|----------|--------|
| Profil | Mehrbenutzerbetrieb, NAS-basiert, Compliance-Anforderungen |
| Erfahrung | Administrator-Level |
| Ziel | Nachvollziehbare Operationen, signierte Policies, API-Automation |
| Frustration | Kein Audit-Trail, keine API-Contracts, keine Approval-Workflows |
| KPI | Audit-Replay-Erfolgsrate > 99% |
| Nutzungsmuster | Täglich via API, manuell nur für Freigaben |

**Top Use Cases (Alex):**
1. REST-API-gesteuerte Runs mit Status-Monitoring
2. Signierte Audit-Logs für Compliance
3. NAS/SMB-optimierte Scans mit Retry-Logic
4. Approval-Workflows für Move-Operationen
5. Docker-Container-Deployment für Headless-Server

---

## 5. User Journeys

### 5.1 Happy Path: Erste Sammlung aufräumen (Max)

```
1. Start → Wizard-Willkommensseite ("Was möchtest du tun?")
2. "Sammlung aufräumen" wählen → Root-Ordner hinzufügen (Browse/Drop)
3. Grundeinstellungen → Region-Reihenfolge: EU > US > JP [Annahme: Defaults ausreichend]
4. Preflight-Check → Ampel zeigt grün (Ordner lesbar, Tools gefunden)
5. DryRun starten → Progress-Bar mit ETA
6. Preview-Ansicht → Tabellarische Übersicht: Keep/Move/Junk pro Konsole
7. "Sieht gut aus" bestätigen → Summary-Dialog mit Zahlen + Warnungen
8. Move ausführen → Fortschritt mit Cancel-Option
9. Report anzeigen → HTML mit Diagrammen + Undo-Button
10. Fertig → "Undo jederzeit möglich"-Hinweis
```

### 5.2 Happy Path: DAT-Verifizierung + Missing-Report (Sarah)

```
1. DAT-Tab → Auto-Update prüfen → "3 neue DATs verfügbar" → Update
2. DAT-Mapping konfigurieren → PS1=Redump, SNES=No-Intro
3. Scan starten → Hash-Berechnung mit Parallel-Hashing (multi-threaded)
4. Ergebnis: 95% Verified, 3% Unmatched, 2% Missing
5. Missing-Report → "37 PS1 EU-ROMs fehlen" → Export als CSV
6. Unmatched → Rename-Vorschlag nach DAT-Standard → Preview → Apply
```

### 5.3 Happy Path: Konvertierungs-Pipeline (Sarah)

```
1. Convert-Tab → Konsole wählen: PS1
2. Zielformat: CHD → Speicherplatz-Prognose: "Spart ~45 GB"
3. Queue erstellen → 234 Dateien → Start
4. Pause nach 50 Dateien → PC neustarten → Resume
5. Batch-Verify: alle CHDs gegen Quell-Hashes geprüft
6. Ergebnis: 234/234 erfolgreich → Quell-Dateien in Trash
```

### 5.4 Edge Cases & Failure Modes

| Szenario | Erwartetes Verhalten |
|----------|---------------------|
| Root-Ordner nicht lesbar (Permissions) | Preflight-Ampel rot, klare Fehlermeldung, kein Start möglich |
| Beschädigtes Archiv (ZIP/7z corrupt) | Klassifikation als "Unverified", Warnung im Report, kein Move |
| Symlink in Root | Reparse-Point erkannt, blockiert, Log-Eintrag mit Pfad |
| Path-Traversal in ZIP-Entry (`../../etc/passwd`) | Zip-Slip-Schutz greift, Entry übersprungen, Security-Event geloggt |
| Festplatte voll während Move | Transaktion abbrechen, bereits verschobene Dateien per Rollback zurück |
| chdman Exit-Code ≠ 0 | Konvertierung als fehlgeschlagen markiert, Quelldatei unverändert, Retry optional |
| DAT-Datei nicht parsbar (corrupt XML) | XXE-Schutz aktiv, Warnung, DAT übersprungen, alternatives DAT vorschlagen |
| 500.000+ Dateien in einem Root | Streaming-Enumeration, Progress-Updates, Memory-Guard greift (Soft-Limit → GC, Hard-Limit → Warnung) |
| Gleicher ROM-Name in unterschiedlichen Roots | Cross-Root-Duplikat-Erkennung, Hash-Vergleich, Merge-Vorschlag |
| Netzwerk-Timeout bei NAS/SMB | Retry mit exponential Backoff (max. 3), Throttling-Profil |

---

## 6. User Stories

### US-001: Wizard-basierter Erststart
- **Priorität:** P0 | **Aufwand:** M
- **Story:** Als Solo Curator möchte ich beim ersten Start einen geführten Wizard sehen, damit ich sofort weiß, was zu tun ist, ohne das Handbuch zu lesen.
- **Akzeptanzkriterien:**
  - Given: Erster Start (keine Settings-Datei vorhanden), When: App startet, Then: Wizard öffnet sich mit Schritt 1 "Was möchtest du tun?"
  - Given: Wizard aktiv, When: "Sammlung aufräumen" gewählt, Then: Schritt 2 zeigt Root-Ordner-Auswahl mit Drag-Drop-Zone
  - Given: Root hinzugefügt, When: "Weiter" geklickt, Then: Preflight-Check läuft, Ampel zeigt grün/gelb/rot
  - Given: Preflight grün, When: "DryRun starten", Then: Scan startet mit Progress-Bar, ETA und Cancel-Button
  - Given: DryRun fertig, When: Ergebnis angezeigt, Then: Tabellarische Preview mit Keep/Move/Junk-Zahlen pro Konsole
  - Given: User bestätigt, When: "Move ausführen", Then: Bestätigungsdialog mit Summary + "Rückgängig möglich"-Hinweis
- **Edge Cases:**
  - Root-Ordner ist leer → Wizard zeigt "Keine ROMs gefunden" + Hinweis
  - Root-Ordner enthält nur Junk → Warnung "Alle Dateien werden als Junk klassifiziert"
  - User bricht Wizard ab → Keine Settings gespeichert, nächster Start zeigt Wizard erneut
- **Telemetry:** `wizard.started`, `wizard.step_completed(step)`, `wizard.aborted(step)`, `wizard.completed`, `wizard.time_total`

### US-002: Regions-basierte Deduplizierung
- **Priorität:** P0 | **Aufwand:** L (bereits implementiert, Verfeinerung)
- **Story:** Als Collector Pro möchte ich meine Sammlung nach Regionen deduplizieren (1G1R), damit ich pro Spiel nur die beste Version behalte.
- **Akzeptanzkriterien:**
  - Given: Sammlung mit Duplikaten, When: DryRun mit preferredRegions=[EU,US,JP], Then: Winner-Selection ist deterministisch (gleiche Inputs = gleicher Winner)
  - Given: Spiel hat EU+US+JP Versionen, When: EU bevorzugt, Then: EU-Version ist Winner mit höchstem Regions-Score
  - Given: Gleicher Regions-Score, When: Format unterschiedlich, Then: FormatScore entscheidet (CHD > ISO > ZIP)
  - Given: DryRun-Ergebnis, When: Move-Modus, Then: Verlierer in Trash verschoben, Winner bleibt, Audit-CSV geschrieben
  - Given: BIOS-Dateien erkannt, When: Dedupe läuft, Then: BIOS wird nie als Junk klassifiziert, separate Kategorie
  - Given: Junk-Tags (Beta, Demo, Hack), When: aggressiveJunk=true, Then: alle Junk-Dateien markiert und verschiebbar
- **Edge Cases:**
  - Leerer GameKey nach Normalisierung → Fehler loggen, Datei überspringen
  - Alle Varianten eines Spiels sind Junk → Kein Winner, alle als Junk markiert
  - Datei ohne erkennbare Region → WORLD als Fallback
- **Telemetry:** `dedupe.started`, `dedupe.total_files`, `dedupe.winners`, `dedupe.losers`, `dedupe.junk`, `dedupe.duration_ms`

### US-003: DAT-Rename nach Standard
- **Priorität:** P0 | **Aufwand:** S
- **Story:** Als Collector Pro möchte ich meine ROMs nach No-Intro/Redump-Nomenklatur umbenennen, damit meine Dateinamen konsistent und DAT-konform sind.
- **Akzeptanzkriterien:**
  - Given: ROM mit Hash-Match in DAT, When: Rename-DryRun, Then: Vorschau zeigt alter→neuer Name
  - Given: DryRun-Vorschau bestätigt, When: Rename ausgeführt, Then: Datei umbenannt, Audit-Log geschrieben
  - Given: Kein DAT-Match, When: Rename-DryRun, Then: Datei bleibt unverändert, Warnung "Kein Match"
  - Given: Zieldateiname existiert bereits, When: Rename, Then: Konfliktlösung (Skip + Warnung)
- **Edge Cases:**
  - Dateiname enthält ungültige Zeichen nach DAT-Standard → Sanitize
  - Pfad wird zu lang (>260 Zeichen) → Warnung, kein Rename
  - Datei ist gerade gesperrt (anderer Prozess) → Transient-Fehler, Retry
- **Telemetry:** `rename.total`, `rename.matched`, `rename.conflicts`, `rename.duration_ms`

### US-004: M3U-Auto-Generierung für Multi-Disc
- **Priorität:** P0 | **Aufwand:** S
- **Story:** Als Solo Curator möchte ich automatisch M3U-Playlists für Multi-Disc-Spiele erstellen, damit RetroArch alle Discs eines Spiels erkennt.
- **Akzeptanzkriterien:**
  - Given: Spiel mit Disc 1, Disc 2, Disc 3 erkannt, When: M3U-Generierung, Then: `.m3u`-Datei erstellt mit korrekten Pfaden
  - Given: Disc-Erkennung via Dateiname-Pattern `(Disc X)`, When: Scan, Then: korrekte Zuordnung
  - Given: CHD-Dateien, When: M3U-Generierung, Then: `.chd`-Pfade in M3U (nicht `.cue`)
  - Given: Bereits existierende M3U, When: M3U-Generierung, Then: keine Duplikate, Warnung + Skip oder Update
- **Edge Cases:**
  - Disc-Nummern nicht sequentiell (1,3,4 ohne 2) → Warnung "Disc 2 fehlt"
  - Mixed Formate (Disc 1 = CHD, Disc 2 = BIN/CUE) → Warnung, M3U trotzdem erstellt
  - Verschiedene Regionen desselben Spiels → separate M3Us pro Region
- **Telemetry:** `m3u.generated`, `m3u.discs_total`, `m3u.warnings`

### US-005: RetroArch-Playlist-Export
- **Priorität:** P1 | **Aufwand:** S
- **Story:** Als Solo Curator möchte ich meine sortierte Sammlung als RetroArch-Playlist (.lpl) exportieren, damit RetroArch alle ROMs mit korrekten Core-Zuweisungen anzeigt.
- **Akzeptanzkriterien:**
  - Given: Sortierte Sammlung, When: Export, Then: `.lpl`-Datei pro Konsole mit korrektem JSON-Format
  - Given: Konsole bekannt (z.B. SNES), When: Export, Then: Core-Zuweisung aus Mapping-Tabelle (snes9x_libretro)
  - Given: ROM-Pfade, When: Export, Then: Pfade sind absolut und gültig
  - Given: Unbekannte Konsole, When: Export, Then: Core-Feld leer, Warnung
- **Edge Cases:**
  - Sonderzeichen im ROM-Namen → JSON-Escaping korrekt
  - Sehr viele ROMs (>10.000 pro Konsole) → Performance OK, keine Truncation
- **Telemetry:** `playlist.exported`, `playlist.consoles`, `playlist.total_entries`

### US-006: Dark/Light-Theme-Toggle
- **Priorität:** P1 | **Aufwand:** S
- **Story:** Als Benutzer möchte ich zwischen Dark und Light Theme wechseln können, damit die App meiner Systempräferenz entspricht.
- **Akzeptanzkriterien:**
  - Given: App gestartet, When: Theme=Auto, Then: System-Präferenz wird erkannt und angewendet
  - Given: Theme-Toggle geklickt, When: Dark→Light, Then: sofortige Umschaltung ohne Neustart
  - Given: Theme gewechselt, When: App beendet und neu gestartet, Then: gewähltes Theme persistiert
  - Given: High-Contrast-Modus aktiv, When: App startet, Then: kontrastreiche Farben verwendet
- **Edge Cases:**
  - Custom ResourceDictionary von Plugin → Plugin-Theme wird respektiert
  - System-Theme ändert sich während App läuft → Nachführung bei Auto-Detect
- **Telemetry:** `theme.changed(to)`, `theme.auto_detected(theme)`

### US-007: Keyboard-Shortcuts
- **Priorität:** P1 | **Aufwand:** S
- **Story:** Als Collector Pro möchte ich alle Kernfunktionen per Tastatur auslösen können, damit ich schneller arbeite.
- **Akzeptanzkriterien:**
  - Given: App fokussiert, When: Ctrl+R, Then: Run startet (DryRun oder Move je nach Modus)
  - Given: App fokussiert, When: Ctrl+Z, Then: Undo der letzten Operation
  - Given: App fokussiert, When: F5, Then: Refresh/Rescan
  - Given: App fokussiert, When: Ctrl+Shift+D, Then: DryRun starten
  - Given: App fokussiert, When: Escape, Then: laufende Operation abbrechen
  - Given: Shortcut-Liste, When: ? oder F1, Then: Shortcut-Overlay angezeigt
- **Edge Cases:**
  - Shortcut-Konflikt mit System → konfigurierbar machen **[Annahme]**
  - Modal-Dialog offen → Shortcuts deaktiviert (außer Escape)
- **Telemetry:** `shortcut.used(key)`

### US-008: Speicherplatz-Prognose bei Konvertierung
- **Priorität:** P1 | **Aufwand:** S
- **Story:** Als Benutzer möchte ich vor einer Konvertierung sehen, wie viel Speicherplatz gespart (oder benötigt) wird, damit ich meine Festplatte planen kann.
- **Akzeptanzkriterien:**
  - Given: Konvertierung BIN/CUE→CHD geplant, When: DryRun, Then: Anzeige "Geschätzte Einsparung: ~45 GB"
  - Given: Konvertierung ZIP→7z, When: DryRun, Then: Anzeige basierend auf Durchschnitts-Kompressionsratio
  - Given: Prognose angezeigt, When: Platz nicht ausreichend, Then: Warnung "Nicht genug freier Speicher"
- **Edge Cases:**
  - Konvertierung auf anderes Laufwerk → freien Speicher des Ziels prüfen
  - Prognose kann ungenau sein → Anzeige mit "~" Kennzeichnung
- **Telemetry:** `convert.estimate_shown`, `convert.estimate_gb`

### US-009: Missing-ROM-Tracker
- **Priorität:** P1 | **Aufwand:** M
- **Story:** Als Collector Pro möchte ich sehen, welche ROMs laut DAT in meiner Sammlung fehlen, damit ich gezielt vervollständigen kann.
- **Akzeptanzkriterien:**
  - Given: DAT für PS1 geladen, When: Missing-Report, Then: Liste aller fehlenden Titel mit Name, Region, Größe
  - Given: Missing-Report, When: Filter auf Region=EU, Then: nur EU-Missing angezeigt
  - Given: Missing-Report, When: Export, Then: CSV mit allen fehlenden Titeln
  - Given: Neue ROMs hinzugefügt, When: Re-Scan, Then: Missing-Liste aktualisiert
- **Edge Cases:**
  - DAT enthält 50.000+ Einträge → Performance OK, paginiert/virtualisiert
  - Kein DAT konfiguriert → Hinweis "DAT zuweisen für Missing-Report"
- **Telemetry:** `missing.report_generated`, `missing.total`, `missing.by_region`, `missing.filtered`

### US-010: Cross-Root-Duplikat-Finder
- **Priorität:** P1 | **Aufwand:** M
- **Story:** Als Collector Pro möchte ich Duplikate über verschiedene Root-Verzeichnisse hinweg erkennen, damit ich meine Sammlung konsolidieren kann.
- **Akzeptanzkriterien:**
  - Given: 3 Roots konfiguriert, When: Cross-Root-Scan, Then: Hash-basierter Vergleich findet identische Dateien
  - Given: Duplikate gefunden, When: Merge-Vorschlag, Then: User wählt Ziel-Root, Duplikate in Trash
  - Given: Gleiches ROM in Root A (CHD) und Root B (ISO), When: Scan, Then: FormatScore-basierter Vorschlag
  - Given: DryRun-Modus, When: Cross-Root, Then: nur Report, keine Verschiebung
- **Edge Cases:**
  - Sehr große Sammlungen → Parallel-Hashing nutzen
  - Root auf NAS nicht erreichbar → Timeout + Warnung, andere Roots weiter scannen
- **Telemetry:** `crossroot.duplicates_found`, `crossroot.roots_scanned`, `crossroot.merge_applied`

### US-011: Konvertierungs-Queue mit Pause/Resume
- **Priorität:** P1 | **Aufwand:** M
- **Story:** Als Benutzer möchte ich Konvertierungen pausieren und fortsetzen können, damit ich meinen PC zwischendurch nutzen oder neustarten kann.
- **Akzeptanzkriterien:**
  - Given: Queue mit 500 Dateien läuft, When: Pause geklickt, Then: aktuelle Datei wird fertig, Queue pausiert
  - Given: Queue pausiert, When: Resume geklickt, Then: Queue setzt an korrekter Position fort
  - Given: Queue pausiert + App geschlossen, When: App neu gestartet, Then: Queue-Status aus Datei geladen, Resume möglich
  - Given: Konvertierung einer Datei fehlschlägt, When: Queue läuft, Then: Fehler geloggt, nächste Datei bearbeitet
  - Given: Queue komplett, When: Batch-Verify aktiviert, Then: automatischer Hash-Vergleich vor/nach
- **Edge Cases:**
  - PC-Absturz während Konvertierung → teilweise Datei erkennen und bereinigen
  - Quelldatei im Queue gelöscht → Skip + Warnung
- **Telemetry:** `convert.queue_started`, `convert.queue_paused`, `convert.queue_resumed`, `convert.queue_completed`, `convert.failures`

### US-012: DAT-Auto-Update
- **Priorität:** P1 | **Aufwand:** M
- **Story:** Als Collector Pro möchte ich automatisch benachrichtigt werden, wenn neue DAT-Versionen verfügbar sind, damit ich immer gegen die aktuellsten Daten verifiziere.
- **Akzeptanzkriterien:**
  - Given: DAT konfiguriert, When: Update-Check (manuell oder per Intervall), Then: Popup "3 neue DATs verfügbar"
  - Given: Update verfügbar, When: "Jetzt aktualisieren", Then: Download + SHA256-Verifizierung
  - Given: Update installiert, When: Diff-Viewer, Then: "5 neue Einträge, 2 umbenannt, 1 entfernt"
  - Given: Kein Internet, When: Update-Check, Then: Warnung, letzte DAT-Version weiter nutzbar
- **Edge Cases:**
  - DAT-Download beschädigt → SHA256-Mismatch → Rollback auf vorherige Version
  - Changelog enthält Breaking-Changes → Prominente Warnung
- **Telemetry:** `dat.update_checked`, `dat.update_available`, `dat.update_installed`, `dat.update_failed`

### US-013: Rule-Engine
- **Priorität:** P1 | **Aufwand:** M
- **Story:** Als Collector Pro möchte ich eigene Regeln definieren können ("Wenn Region=JP UND Konsole≠PS1 → entferne"), damit ich mein Sammlungs-Profil automatisiert pflege.
- **Akzeptanzkriterien:**
  - Given: Rule-Editor, When: Regel erstellt (JSON oder GUI), Then: Regel validiert und gespeichert
  - Given: Regeln aktiv, When: DryRun, Then: Regeln angewendet, Ergebnis in Preview sichtbar
  - Given: Regel "Region=JP AND Konsole != PS1 → JUNK", When: Scan, Then: alle nicht-PS1 JP-ROMs als Junk markiert
  - Given: Regel-Konflikt (Regel A = Keep, Regel B = Junk für dasselbe ROM), Then: Prioritätenreihenfolge + Warnung
- **Edge Cases:**
  - Ungültige Regel-Syntax → Validierungsfehler mit klarer Meldung
  - Regel trifft alle Dateien → Warnung "Diese Regel betrifft 100% aller ROMs"
- **Telemetry:** `rules.created`, `rules.applied`, `rules.matches`, `rules.conflicts`

### US-014: Command-Palette
- **Priorität:** P1 | **Aufwand:** M
- **Story:** Als Power-User möchte ich eine VSCode-artige Command-Palette (Ctrl+Shift+P) mit Fuzzy-Suche, damit ich jede Funktion schnell erreiche.
- **Akzeptanzkriterien:**
  - Given: App fokussiert, When: Ctrl+Shift+P, Then: Overlay mit Suchfeld erscheint
  - Given: Suchfeld offen, When: "conv" getippt, Then: Fuzzy-Match zeigt "Konvertierung starten", "Konvertierungs-Queue"
  - Given: Ergebnis ausgewählt, When: Enter, Then: Funktion ausgeführt oder Tab/Dialog geöffnet
  - Given: Palette offen, When: Escape, Then: Palette geschlossen
- **Edge Cases:**
  - Kein Match → "Keine Ergebnisse" anzeigen
  - Funktion nicht verfügbar (z.B. kein Root konfiguriert) → Deaktiviert mit Hinweis
- **Telemetry:** `palette.opened`, `palette.search(query)`, `palette.selected(command)`

### US-015: Emulator-Launcher-Integration
- **Priorität:** P2 | **Aufwand:** L
- **Story:** Als Benutzer möchte ich meine Sammlung in verschiedene Emulator-Formate exportieren (RetroArch, LaunchBox, EmulationStation, Batocera), damit ich ohne manuelle Arbeit spielen kann.
- **Akzeptanzkriterien:**
  - Given: Sammlung sortiert, When: Export → RetroArch, Then: `.lpl`-Dateien pro Konsole mit Core-Mapping
  - Given: Export → LaunchBox, When: ausgeführt, Then: XML-Import-Dateien generiert
  - Given: Export → EmulationStation, When: ausgeführt, Then: `gamelist.xml` pro System
  - Given: Export → Batocera, When: ausgeführt, Then: korrekte Ordnerstruktur + Metadaten
- **Edge Cases:**
  - Emulator nicht installiert → Export trotzdem möglich, Warnung
  - Custom Core-Mapping → konfigurierbar in Settings
- **Telemetry:** `export.format(type)`, `export.consoles`, `export.total_entries`

### US-016: ROM-Thumbnail/Cover-Scraping
- **Priorität:** P2 | **Aufwand:** L
- **Story:** Als Benutzer möchte ich Boxart und Screenshots automatisch herunterladen können, damit meine Sammlung im Dashboard visuell ansprechend dargestellt wird.
- **Akzeptanzkriterien:**
  - Given: ROM identifiziert, When: Scraping aktiviert, Then: Cover von ScreenScraper.fr/IGDB heruntergeladen
  - Given: Cover vorhanden, When: Dashboard, Then: Thumbnail neben ROM-Name angezeigt
  - Given: API-Rate-Limit erreicht, When: Scraping, Then: Queue pausiert, Warnung angezeigt
  - Given: Kein Internet, When: Scraping, Then: Cached-Thumbnails genutzt, Warnung
- **Edge Cases:**
  - ROM nicht in Scraping-DB → Platzhalter-Icon
  - Mehrere Cover-Varianten → User wählt Quelle (ScreenScraper vs. IGDB)
- **Telemetry:** `scraping.started`, `scraping.found`, `scraping.notfound`, `scraping.cached`

### US-017: Patch-Engine (IPS/BPS/UPS)
- **Priorität:** P2 | **Aufwand:** L
- **Story:** Als Benutzer möchte ich Patches (Übersetzungen, Bugfixes) automatisch auf ROMs anwenden können, damit ich gepatchte Versionen erstellen kann.
- **Akzeptanzkriterien:**
  - Given: ROM + IPS/BPS/UPS-Patch, When: Patch anwenden, Then: gepatchte Kopie erstellt, Original unverändert
  - Given: Gepatchte Datei, When: Verify, Then: Hash-Check gegen erwarteten Ziel-Hash
  - Given: Patch nicht kompatibel (falsche CRC), When: Patch-Versuch, Then: Fehler "CRC-Mismatch" + kein Patching
  - Given: Mehrere Patches für ein ROM, When: Auswahl, Then: Liste mit Patch-Beschreibungen
- **Edge Cases:**
  - Patch > 50 MB → Performance-Warnung
  - ROM bereits gepatcht → Warnung "Bereits modifiziert"
- **Telemetry:** `patch.applied`, `patch.failed`, `patch.type(ips|bps|ups)`

### US-018: Arcade-ROM-Merge/Split
- **Priorität:** P2 | **Aufwand:** L
- **Story:** Als Arcade-Collector möchte ich zwischen Non-Merged, Split und Merged Sets konvertieren können, damit meine MAME/FBNEO-Sammlung dem gewünschten Layout entspricht.
- **Akzeptanzkriterien:**
  - Given: Non-Merged-Set, When: → Split-Set konvertieren, Then: Parent/Clone aufgeteilt, gemeinsame ROMs nur in Parent
  - Given: Split-Set, When: → Merged-Set, Then: alle Clone-ROMs in Parent-ZIP zusammengeführt
  - Given: Konvertierung, When: DryRun, Then: Preview mit Datei-/Größen-Änderungen
  - Given: DAT-Referenz, When: Merge/Split, Then: Verifizierung gegen DAT
- **Edge Cases:**
  - Fehlende Parent-ROMs → Fehler "Parent ROM X fehlt für Clone Y"
  - Unbekanntes Set-Format → Analyse-Modus mit Report
- **Telemetry:** `arcade.merge`, `arcade.split`, `arcade.errors`

### US-019: C#/.NET 8 Migration (Strangler-Fig)
- **Priorität:** P0 | **Aufwand:** XL
- **Story:** Als Entwickler möchte ich die Core-Engine nach C# migrieren, damit die App async-fähig, performant und Cross-Platform-ready wird.
- **Akzeptanzkriterien:**
  - Given: Core.ps1-Logik, When: Migration, Then: `RomCleanup.Core` C#-Projekt mit identischen Unit-Tests
  - Given: Port-Interfaces, When: Migration, Then: C#-Interface-Definitionen mit identischen Contracts
  - Given: PowerShell-Entry-Points, When: Migration Phase 1, Then: PS-Layer ruft C#-DLLs auf (Hybrid-Modus)
  - Given: Alle Unit-Tests grün in C#, When: Integration, Then: Feature-Parität nachgewiesen
  - Given: WPF-UI, When: Phase 2, Then: WPF bleibt Windows-only, Avalonia optional für Cross-Platform
- **Edge Cases:**
  - Behavioral-Differences PS→C# (z.B. String-Vergleiche, Regex-Flags) → umfangreiche Regression-Tests
  - Inline-C#-Blöcke (CRC32, INotifyPropertyChanged) → direkte Übernahme in C#-Projekte
- **Telemetry:** `migration.module_ported`, `migration.test_parity`, `migration.regression_count`

### US-020: Docker-Container
- **Priorität:** P2 | **Aufwand:** XL
- **Story:** Als Team-Admin möchte ich RomCleanup als Docker-Container deployen, damit ich es headless auf meinem NAS (TrueNAS/Unraid) betreiben kann.
- **Akzeptanzkriterien:**
  - Given: Dockerfile, When: Build, Then: Image mit CLI + REST-API funktional
  - Given: Container gestartet, When: API-Health-Check, Then: `/health` antwortet mit 200
  - Given: ROM-Volume gemountet, When: API-Run, Then: Scan + Dedupe auf gemounteten Pfaden
  - Given: Container, When: Config via Env-Variablen, Then: alle Settings konfigurierbar
- **Edge Cases:**
  - Volume-Permissions → klare Dokumentation, Health-Check prüft Schreibrechte
  - Container-Restart → Queue-Status persistiert via Volume
- **Telemetry:** `docker.started`, `docker.runs`, `docker.errors`

---

## 7. Functional Requirements

### 7.1 Datei-Management

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-001 | DAT-Rename: ROMs nach No-Intro/Redump-Standard umbenennen | P0 | ROM-Datei + DAT-Index + Hash | Umbenannte Datei + Audit-Log | Kein DAT-Match → Skip + Warnung |
| FR-002 | ECM-Dekompression: `.ecm` → `.bin` via `ecm2bin` | P0 | .ecm-Datei | .bin-Datei | Tool nicht gefunden → Hinweis installieren |
| FR-003 | Archiv-Repack: ZIP↔7z mit konfigurierbarer Kompression | P0 | Archiv-Datei + Zielformat + Kompressionslevel | Neues Archiv + Audit-Log | Beschädigtes Archiv → Skip + Warnung |
| FR-004 | Speicherplatz-Prognose im DryRun | P1 | Quelldateien + Zielformat | Schätzung in GB/MB | Prognose immer mit "~" markieren |
| FR-005 | Detaillierter Junk-Report (welche Regel, welcher Tag) | P0 | Klassifizierte Dateien | Report mit Regel-ID pro Datei | Keine Klassifikation → "UNKNOWN" |

### 7.2 UI/UX

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-006 | Keyboard-Shortcuts (Ctrl+R, Ctrl+Z, F5, Ctrl+Shift+D, Esc) | P1 | Tasteneingabe | Funktion ausgeführt | Modal offen → ignorieren (außer Esc) |
| FR-007 | Dark/Light-Theme-Toggle mit System-Auto-Detect | P1 | Toggle/System-Setting | Theme gewechselt | High-Contrast → Fallback auf OS-Theme |
| FR-008 | ROM-Suche/Filter in Ergebnisliste (Live-Filter) | P1 | Suchtext | Gefilterte Liste | Leerer Suchtext → alle anzeigen |
| FR-009 | Duplikat-Heatmap pro Konsole (Balkendiagramm) | P1 | Scan-Ergebnis | Visualisierung | Keine Duplikate → leeres Diagramm + Hinweis |
| FR-010 | Command-Palette (Ctrl+Shift+P, Fuzzy-Suche) | P1 | Suchtext | Fuzzy-Ergebnisliste | Kein Match → "Keine Ergebnisse" |
| FR-011 | Split-Panel-Vorschau (Norton-Commander-Stil) | P1 | DryRun-Ergebnis | Quell+Ziel nebeneinander | Zu schmales Fenster → Fallback auf Tabs |
| FR-012 | Filter-Builder (visueller Query-Builder) | P1 | Filter-Kriterien | Gefilterte ROM-Liste | Ungültiger Filter → Validierungsfehler |
| FR-013 | Mini-Modus / System-Tray mit Watch-Mode | P1 | Minimieren-Aktion | Tray-Icon + Status-Tooltip | Tray nicht verfügbar → minimiert in Taskbar |

### 7.3 Automatisierung

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-014 | PowerShell-Script-Generator aus GUI-Config | P0 | Aktuelle GUI-Settings | Copy-pastable CLI-Kommando | Keine gültige Config → Button deaktiviert |
| FR-015 | Webhook-Benachrichtigung (Discord/Slack) | P1 | Webhook-URL + Run-Ergebnis | HTTP POST mit Summary-JSON | URL nicht erreichbar → Retry (3×) + Warnung |
| FR-016 | Portable-Modus (`--Portable`) | P1 | CLI-Flag | Settings/Logs/Caches relativ zum Programmordner | Kein Schreibrecht → Fehler |
| FR-017 | Rule-Engine (JSON/GUI-Definition) | P1 | Regeldefinition | Angewendete Klassifikation | Ungültige Regel → Validierungsfehler |
| FR-018 | Conditional-Pipelines (Sortieren→Konvertieren→Verifizieren→Umbenennen) | P1 | Pipeline-Definition | Sequenzielle Ausführung + Report | Step-Fehler → Pipeline-Stopp + Rollback |
| FR-019 | DryRun-Vergleich (Side-by-Side) | P1 | 2 DryRun-Ergebnisse | Diff-Ansicht | Inkompatible Ergebnisse → Warnung |
| FR-020 | Ordnerstruktur-Vorlagen (RetroArch, ES, LaunchBox etc.) | P1 | Vorlagen-Auswahl | Sortierte Ordnerstruktur | Vorlage unvollständig → Default-Fallback |
| FR-021 | Run-Scheduler mit Kalender-UI | P1 | Zeitplan-Definition | Automatisierte Runs | PC aus zum Zeitpunkt → nächster Start nachholen |

### 7.4 Reporting & Export

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-022 | Excel-kompatibles CSV-Export (alle Metadaten) | P0 | Scan-Ergebnis | CSV-Datei | CSV-Injection-Schutz (kein `=+@-` am Anfang) |
| FR-023 | Run-History-Browser (Datum, Roots, Modus, Ergebnis) | P1 | RunIndex | Tabellarische Übersicht mit Links | Kein Run vorhanden → leere Liste + Hinweis |
| FR-024 | PDF-Report-Export (mit Diagrammen, Statistiken) | P2 | Scan-/Dedupe-Ergebnis | PDF-Datei | PDF-Renderer-Fehler → Fallback auf HTML |

### 7.5 DAT-Management

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-025 | Missing-ROM-Tracker pro Konsole/Region | P1 | DAT-Index + Scan-Ergebnis | Fehlende-Titel-Liste | Kein DAT → Hinweis "DAT zuweisen" |
| FR-026 | DAT-Auto-Update mit Changelog | P1 | DAT-Quellen | Aktualisierte DATs + Diff-Report | Netzwerkfehler → letzte Version nutzen |
| FR-027 | DAT-Diff-Viewer (neue/entfernte/umbenannte Einträge) | P1 | 2 DAT-Versionen | Diff-Anzeige | DAT inkompatibel → Vollansicht statt Diff |
| FR-028 | TOSEC-DAT-Support | P1 | TOSEC-DAT-Datei | Index wie No-Intro/Redump | Parsing-Fehler → Warnung + DAT überspringen |
| FR-029 | Parallel-Hashing (multi-threaded) | P1 | ROM-Dateien | Hashes | Thread-Pool-Exhaustion → Fallback single-threaded |
| FR-030 | Custom-DAT-Editor für private Sammlungen | P2 | User-Input | Custom-DAT-Datei | Ungültige DAT → Validierung + Fehler |

### 7.6 Format-Konvertierung

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-031 | CSO/ZSO→ISO→CHD in einem Schritt | P1 | .cso/.zso-Datei | .chd-Datei | Zwischenformat-Fehler → Cleanup + Fehler |
| FR-032 | NKit→ISO-Rückkonvertierung vor RVZ | P1 | .nkit.iso-Datei | .rvz-Datei | NKit-Tool nicht gefunden → Hinweis |
| FR-033 | Konvertierungs-Queue mit Pause/Resume | P1 | Queue-Definition | Konvertierte Dateien + Status-Datei | App-Crash → Queue aus Datei wiederherstellbar |
| FR-034 | Batch-Verify nach Konvertierung (CRC/SHA1) | P1 | Quell-Hash + konvertierte Datei | Verify-Report | Hash-Mismatch → Warnung + Quelldatei behalten |
| FR-035 | Format-Prioritätsliste pro Konsole | P1 | Prioritätskonfiguration | Deterministische Formatwahl | Zielformat nicht konvertierbar → Skip |

### 7.7 ROM-Bibliothek & Analyse

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-036 | ROM-Header-Analyse (NES, SNES, GBA, N64) | P1 | ROM-Datei | Header-Info + Anomalie-Warnung | Header nicht lesbar → "Unknown Header" |
| FR-037 | Sammlung-Completeness-Ziel (100% EU PS1 RPGs) | P1 | Zielset-Definition + DAT | Fortschrittsbalken + Fehlende-Liste | Zielset nicht definierbar → Hinweis |
| FR-038 | Smart-Collections / Auto-Playlists | P1 | Filter-Definition | Dynamische ROM-Liste | Leere Collection → Hinweis |
| FR-039 | Cross-Root-Duplikat-Finder (hash-basiert) | P1 | Multiple Roots | Duplikat-Report + Merge-Vorschlag | Hash-Berechnung zu langsam → Skip-Option |
| FR-040 | Genre-/Tag-Klassifikation (DAT-basiert/Scraping) | P2 | DAT-Metadaten oder Scraping-API | Genre-Tags | API nicht erreichbar → Cached Daten |

### 7.8 Sicherheit & Integrität

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-041 | Integritäts-Monitor (Bit-Rot-Erkennung) | P1 | Gespeicherte Hashes + aktuelle Hashes | Änderungs-Report | Datei geändert → Warnung "Integritäts-Verletzung" |
| FR-042 | Automatische Backup-Strategie (inkrementell) | P1 | Backup-Policy + Quelldateien | Backup-Kopien | Backup-Ziel nicht erreichbar → Warnung |
| FR-043 | ROM-Quarantäne (verdächtige Dateien isolieren) | P1 | Unbekannte/verdächtige Dateien | Quarantäne-Ordner | Quarantäne-Ordner nicht beschreibbar → Fallback Trash |

### 7.9 Netzwerk & Community

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-044 | NAS/SMB-Optimierung (adaptive Batches, Retry) | P2 | NAS-Pfade | Optimierte Transfers | NAS-Timeout → Retry mit Backoff |
| FR-045 | FTP/SFTP-Source (Download→Process→Upload) | P2 | FTP/SFTP-URL | Verarbeitete Dateien | Verbindungsfehler → Retry + Warnung |
| FR-046 | Plugin-Marketplace-UI (Install/Update/Bewertung) | P2 | Plugin-Katalog | Installiertes Plugin | Signatur-Prüfung fehlgeschlagen → Block |
| FR-047 | Rule-Pack-Sharing (signiert) | P2 | Rule-Pack-Datei | Importierte Regeln | Ungültige Signatur → Abgelehnt |
| FR-048 | Theme-Engine (Custom WPF-Themes als Plugins) | P2 | Theme-ResourceDictionary | Angewendetes Theme | Theme inkompatibel → Fallback auf Default |

### 7.10 Plattform & Distribution

| ID | Anforderung | Priorität | Inputs | Outputs | Fehlerfall |
|----|-------------|-----------|--------|---------|------------|
| FR-049 | Docker-Container (CLI + REST-API) | P2 | Dockerfile | Laufender Container | Build-Fehler → CI-Feedback |
| FR-050 | Mobile-Web-UI (Read-Only-Monitoring) | P2 | REST-API | Responsive Web-Frontend | API nicht erreichbar → Offline-Hinweis |
| FR-051 | Windows-Context-Menu (Shell-Extension) | P2 | Rechtsklick-Aktion | RomCleanup-Scan gestartet | UAC-Elevation → Hinweis |
| FR-052 | Winget/Scoop-Paket | P2 | Paketmanifeste | Installierbare Pakete | Signatur-Prüfung |

---

## 8. Non-Functional Requirements

### 8.1 Performance

| ID | Anforderung | Grenzwert | Messmethode |
|----|-------------|-----------|-------------|
| NFR-001 | Scan-Geschwindigkeit (SSD) | ≥ 5.000 Dateien/Minute | Benchmark-Test mit 50k-Dummy-Dateien |
| NFR-002 | Scan-Geschwindigkeit (HDD) | ≥ 1.000 Dateien/Minute | Benchmark-Test mit 10k-Dummy-Dateien |
| NFR-003 | UI-Responsiveness während Scan | UI-Thread friert nie > 200ms | PhaseMetrics + UI-Thread-Monitoring |
| NFR-004 | GameKey-LRU-Cache: 50k Einträge | Lookup < 1ms (p99) | LruCache.Perf.Tests |
| NFR-005 | Hash-Berechnung (SHA1, 100 MB Datei) | < 2 Sekunden | Benchmark-Test |
| NFR-006 | Startup-Zeit (bis GUI interaktiv) | < 3 Sekunden (SSD) | PhaseMetrics |
| NFR-007 | Memory-Budget | < 500 MB RAM für 100k Dateien | MemoryGuard Soft-Limit |
| NFR-008 | Parallel-Hashing Speedup | ≥ 4× bei 8 Cores | Benchmark vs. single-threaded |

### 8.2 Reliability

| ID | Anforderung | Grenzwert | Messmethode |
|----|-------------|-----------|-------------|
| NFR-009 | Keine unhandled Exceptions | 0 Crashes pro 100 Runs | CatchGuard-Compliance + Logging |
| NFR-010 | Rollback-Erfolgsrate | > 99,5% | Rollback-Tests mit realen Szenarien |
| NFR-011 | Konvertierungs-Erfolgsrate | > 99% (bei gültigen Quelldateien) | Batch-Verify nach Konvertierung |
| NFR-012 | Deterministisches Verhalten | Identische Inputs = identische Outputs | Property-Tests (Winner-Selection, GameKey) |

### 8.3 Security

| ID | Anforderung | Grenzwert | Messmethode |
|----|-------------|-----------|-------------|
| NFR-013 | Path-Traversal-Schutz | 0 Out-of-Root-Moves | Negative Tests mit `../../`-Pfaden |
| NFR-014 | Zip-Slip-Schutz | 0 Extraktionen außerhalb Root | Negative Tests mit manipulierten ZIPs |
| NFR-015 | CSV-Injection-Schutz | Kein führendes `=+@-` in CSV-Feldern | Unit-Tests + Report-Analyse |
| NFR-016 | HTML-XSS-Schutz | Alle Report-Outputs HTML-escaped | Unit-Tests + Security-Review |
| NFR-017 | XXE-Schutz beim DAT-Parsing | Keine externen Entity-Auflösungen | Negative Tests mit XXE-Payloads |
| NFR-018 | Tool-Hash-Verifizierung | SHA256-Check vor jedem externen Tool-Aufruf | Unit-Tests + CI-Gate |
| NFR-019 | Reparse-Point-Schutz | Symlinks/Junctions erkannt und blockiert | Integration-Tests |
| NFR-020 | API-Security (Auth + Rate-Limit) | API-Key erforderlich, 120 Req/Min | API-Integration-Tests |

### 8.4 Usability

| ID | Anforderung | Grenzwert | Messmethode |
|----|-------------|-----------|-------------|
| NFR-021 | Time-to-safe-run (Neuling) | < 5 Minuten | Usability-Test mit 5 Testpersonen **[Annahme]** |
| NFR-022 | Keine irreversible Aktion ohne Bestätigung | 100% Compliance | UI-Review + E2E-Tests |
| NFR-023 | Klare Fehlermeldungen (kein Stack-Trace in GUI) | User-freundliche Meldung + "Details"-Expander | Manuelle QA |
| NFR-024 | Tooltips für alle nicht-offensichtlichen Optionen | 100% Coverage im Experten-Modus | UI-Review |
| NFR-025 | Undo für alle Move/Rename-Operationen | 100% der State-ändernden Aktionen | Integration-Tests |

### 8.5 Maintainability

| ID | Anforderung | Grenzwert | Messmethode |
|----|-------------|-----------|-------------|
| NFR-026 | Modul-Coupling | Keine verbotenen Layer-Übergriffe | Governance-Tests in CI |
| NFR-027 | Test-Coverage | ≥ 50% (Interim), Ziel 70% | Pester Coverage-Gate |
| NFR-028 | Code-Duplikation | Keine neuen Duplikate (DUP-*) | PSScriptAnalyzer + Review |
| NFR-029 | Keine `$script:`-Globals in neuen Modulen | 100% der neuen Module über Port-Interfaces | Code-Review |
| NFR-030 | Core-Module pure (keine UI/IO-Dependencies) | Core, Dedupe, Classification, FormatScoring side-effect-free | Unit-Tests ohne Mocks |

### 8.6 Accessibility

| ID | Anforderung | Grenzwert | Messmethode |
|----|-------------|-----------|-------------|
| NFR-031 | Screen-Reader-Kompatibilität | Alle UI-Elemente via UI Automation Peers erreichbar | Accessibility-Audit |
| NFR-032 | High-Contrast-Modus | Vollständig nutzbar | Manueller Test unter Windows High-Contrast |
| NFR-033 | Skalierbare Schrift | Lesbar bei 150% DPI | DPI-Scale-Test |
| NFR-034 | Tastatur-Navigation | Alle Funktionen ohne Maus erreichbar | Tab-Order-Test |

---

## 9. UX/UI Requirements

### 9.1 Informationsarchitektur / Navigation

**Prinzip:** Progressive Disclosure — Grundfunktionen prominent, Expertenfunktionen in "Erweitert".

**Navigationsstruktur:**

```
┌──────────────────────────────────────────────────────────┐
│  [Logo] RomCleanup                    [Theme] [?] [⚙]  │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─ Sidebar (Icon + Label) ─────────┐  ┌─ Content ────┐│
│  │                                   │  │              ││
│  │  🏠 Dashboard                     │  │   (aktiver   ││
│  │  📁 Sammlung                      │  │    Bereich)  ││
│  │  🔍 Scan & Dedupe                 │  │              ││
│  │  🔄 Konvertierung                 │  │              ││
│  │  📊 DAT-Verwaltung                │  │              ││
│  │  📋 Reports                       │  │              ││
│  │  ⚙  Einstellungen                │  │              ││
│  │  🧩 Plugins                       │  │              ││
│  │  ─────────────                    │  │              ││
│  │  ▶ Quick-Actions                  │  │              ││
│  │                                   │  │              ││
│  └───────────────────────────────────┘  └──────────────┘│
│                                                          │
│  ┌─ Footer: Status-Bar ────────────────────────────────┐│
│  │  Phase: Idle │ 0 Dateien │ 0 Fehler │ ETA: -- │ 🟢 ││
│  └──────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────┘
```

**Dashboard** zeigt auf einen Blick:
- Sammlungsgröße (Dateien, GB) pro Konsole
- Letzte Run-Ergebnisse (Keep/Move/Junk)
- Duplikat-Heatmap
- Missing-ROMs-Zusammenfassung (wenn DAT aktiv)
- Quick-Actions: "DryRun starten", "Letzte Operation rückgängig"

### 9.2 Layout & Spacing Regeln

| Regel | Wert |
|-------|------|
| Minimum Margin zwischen Sections | 16px |
| Minimum Padding innerhalb Cards | 12px |
| Button-Mindestgröße | 32×32px (Touch-friendly) |
| Zeilenabstand in Listen | 1.4× Schriftgröße |
| Max. Elemente pro Zeile | 3 (Grid-Layout) |
| ScrollViewer wenn Inhalt > Viewport | Immer mit Scrollbar-Indikator |
| Minimum Schriftgröße | 12px (Body), 10px (Caption) |
| Responsive Breakpoints | MinWidth 800px (Single-Column), 1200px (Sidebar) |

### 9.3 Design System (retro-modern)

**Farbpalette (Dark Theme — Primary):**

| Rolle | Farbe | Hex |
|-------|-------|-----|
| Background | Fast-Schwarz | `#1A1A2E` |
| Surface | Dunkelblau-Grau | `#16213E` |
| Card/Panel | Mittel-Dunkel | `#0F3460` |
| Primary Accent | Neon Cyan | `#00D4FF` |
| Secondary Accent | Neon Magenta | `#E94560` |
| Success | Neon Grün | `#00E676` |
| Warning | Neon Orange | `#FFAB00` |
| Error | Neon Rot | `#FF1744` |
| Text Primary | Weiß | `#F0F0F0` |
| Text Secondary | Grau | `#A0A0B0` |
| Disabled | Dunkelgrau | `#4A4A5A` |

**[Annahme]** Light-Theme wird als Inversion mit angepassten Kontrasten gestaltet.

**Typografie:**
- Headings: `Segoe UI Semibold` (oder Mono-Font für Retro-Label-Optik)
- Body: `Segoe UI`, 13px
- Mono/Code: `Cascadia Code` / `Consolas`, 12px
- Badges/Tags: 10px, uppercase, rounded corners

**Komponenten-Bibliothek:**
- `Card`: Abgerundete Ecken (8px), leichter Schatten, Neon-Border bei Hover
- `Button`: Filled (Primary), Outlined (Secondary), Danger (Rot+Outline)
- `Badge`: Kleine Tags für Status (Verified ✓, Junk ✕, Missing ?)
- `ProgressBar`: Neon-Gradient-Fill, pulsierendes Glow bei Aktivität
- `Ampel-Dot`: Preflight-Status (Grün/Gelb/Rot) mit Tooltip
- `Expander`: Für "Erweiterte Optionen" — eingeklappt = simpel, ausgeklappt = voll

### 9.4 Accessibility & Usability

- **Alle interaktiven Elemente** haben `AutomationProperties.Name` und `AutomationProperties.HelpText`
- **Tab-Reihenfolge** logisch (links→rechts, oben→unten)
- **Focus-Indicator**: Deutlich sichtbar (Neon-Outline), nicht nur durch Farbe unterscheidbar
- **Fehlermeldungen** mit Icon + Farbe + Text (nicht nur Farbe)
- **Lange Operationen**: Progress-Ring + Text + Cancel-Button, nie "hängt einfach"
- **Bestätigungsdialoge** für destruktive Aktionen: "Möchten Sie X Dateien verschieben?" + Summary
- **Keine Doppelklick-Fallen**: Buttons nach Klick deaktiviert bis Operation abgeschlossen

### 9.5 Error States & Confirmations

**Error-State-Prinzipien:**
1. **Inline-Errors** statt Modal-Dialoge wo möglich (z.B. rotes Label unter dem Eingabefeld)
2. **Error-Banner** oben im Content-Bereich für globale Fehler (z.B. "Tool nicht gefunden")
3. **Fehlermeldungen** immer mit: Was ist passiert + Was kann der User tun
4. **Kein Stack-Trace in der GUI** — Details hinter "Mehr anzeigen"-Expander

**Bestätigungsdialog-Hierarchie:**

| Risiko-Stufe | UI-Element | Beispiele |
|-------------|-----------|-----------|
| Niedrig (reversibel) | Inline-Toast | "DryRun abgeschlossen. 42 Duplikate gefunden." |
| Mittel (reversibel mit Aufwand) | Modal-Dialog mit Summary | "X Dateien werden verschoben. Rückgängig möglich." |
| Hoch (schwer umkehrbar) | Danger-Zone-Dialog | "X Dateien werden GELÖSCHT. Tippen Sie 'DELETE' zur Bestätigung." |
| Kritisch (irreversibel) | Multi-Step-Confirm | Summary → Checkbox "Ich verstehe" → Texteingabe-Bestätigung |

### 9.6 Wizard-Flow (Erststart & wiederkehrend)

```
Schritt 1: Was möchtest du tun?
  [ ] Sammlung aufräumen (Dedupe)
  [ ] ROMs sortieren (nach Konsole)
  [ ] Formate konvertieren (CHD/RVZ)
  [ ] DAT-Verifizierung
  [ ] Alles oben

Schritt 2: ROM-Ordner wählen
  [Drag-Drop-Zone oder Browse]
  + Ordner hinzufügen
  [Vorschau: "3 Ordner, ~12.000 Dateien"]

Schritt 3: Grundeinstellungen
  Bevorzugte Region: [EU ▼] > [US ▼] > [JP ▼]
  Modus: (●) DryRun (Vorschau)  ( ) Move (ausführen)
  [ ] DAT-Verifizierung aktivieren
  [Erweitert ▼] (ausgeklappt: Junk-Aggressivität, Extensions, Tool-Pfade)

Schritt 4: Preflight-Check
  ✅ Ordner lesbar
  ✅ Tools gefunden (chdman, 7z)
  ⚠️ Kein DAT konfiguriert (optional)
  ✅ Genug Speicherplatz

Schritt 5: DryRun / Ausführen
  [Progress] ████████░░░░░░ 62% | 7.800/12.400 Dateien | ETA: 2:14

Schritt 6: Ergebnis
  [Tabellarische Preview] [Report] [Undo-Button]
```

---

## 10. Technical Considerations

### 10.1 Architektur: UI vs. Engine Trennung

**Aktuelle Architektur (PowerShell, v1.x):**
```
Entry Points → Adapter (WPF/CLI/API) → ApplicationServices → Port-Interfaces → Domain Engine → Infrastructure
```

**Zielarchitektur (C# .NET 8, v2.0):**
```
RomCleanup.Core/           → Pure Domain Logic (GameKey, Scoring, Dedupe)
RomCleanup.Contracts/      → Port-Interfaces, ErrorContracts, DTOs
RomCleanup.Infrastructure/ → FileOps, Tools, Dat, Logging
RomCleanup.UI.Wpf/         → WPF-only (Windows) — oder Avalonia
RomCleanup.CLI/            → Headless Entry Point
RomCleanup.Api/            → ASP.NET Core Minimal API
```

**Migrationsstrategie (Strangler-Fig):**
1. **Phase A (sofort):** PowerShell stabilisieren, alle neuen Features als PS-Module, Tests als Migrationssicherung
2. **Phase B (Q3 2026):** Core-Engine nach C# portieren (Core.ps1, Dedupe.ps1, Classification.ps1, FormatScoring.ps1) — PS-Entry-Points rufen C#-DLLs auf
3. **Phase C (Q4 2026):** Infrastructure nach C# (FileOps, Tools, Dat)
4. **Phase D (Q1 2027):** UI nach WPF/C# oder Avalonia, API nach ASP.NET Core

### 10.2 File-IO Safety

| Schutzmaßnahme | Implementierung | Test |
|----------------|-----------------|------|
| Path-Traversal | `Resolve-ChildPathWithinRoot` vor jedem Move/Copy/Delete | Negative Tests mit `../../` |
| Zip-Slip | Archiv-Entry-Pfade gegen Root validieren | Manipulierte ZIP-Fixtures |
| Reparse Points | `FileAttributes.ReparsePoint` prüfen, blockieren | Test mit Symlink-Fixtures |
| Atomare Moves | Move in Temp → Verify → Rename an Ziel | Abbruch-Simulation |
| Audit-Trail | SHA256-signierte CSV nach jeder Operation | Integritäts-Tests |
| Disk-Space-Check | Vor Move: Freier Speicher prüfen | Mock-Test mit vollem Laufwerk |

### 10.3 Performance Hotspots

| Hotspot | Problem | Mitigation |
|---------|---------|------------|
| Regex-Kompilierung in Classification.ps1 | 120+ compiled Regex beim Laden | Lazy-Loading, nur benutzte Patterns kompilieren |
| Hash-Berechnung großer Dateien | SHA1 auf 4 GB ISO = ~30s | Parallel-Hashing via RunspacePool, Skip bei >Threshold |
| DAT-XML-Parsing (50+ MB DATs) | Hoher Speicherverbrauch | Streaming-XML-Parser (XmlReader statt XmlDocument) |
| LRU-Cache-Eviction | ArrayList O(n²) bei 50k Einträgen | LinkedList-basierte Implementierung (bereits optimiert) |
| WPF CollectionView bei 100k+ Items | UI friert bei Filterung | Virtualisierung (`VirtualizingStackPanel`), Paginierung |
| Datei-Enumeration auf NAS | SMB-Latenz | Batch-Enumeration, adaptive Größe, Caching |

### 10.4 Tooling (extern)

| Tool | Zweck | Verifizierung |
|------|-------|--------------|
| `chdman` | BIN/CUE→CHD, DVD→CHD | SHA256 aus `tool-hashes.json` |
| `dolphintool` | ISO→RVZ (GameCube/Wii) | SHA256 aus `tool-hashes.json` |
| `7z` | ZIP/7z-Archivierung | SHA256 aus `tool-hashes.json` |
| `psxtract` | PBP→CHD (PSP) | SHA256 aus `tool-hashes.json` |
| `ecm2bin` | ECM→BIN (neue Integration) | SHA256 aus `tool-hashes.json` |
| `ciso` | CSO↔ISO | SHA256 aus `tool-hashes.json` |
| `nkit` | NKit→ISO | SHA256 aus `tool-hashes.json` **[Annahme]** |

**Alle Tools** werden vor Ausführung gegen `data/tool-hashes.json` verifiziert. Unbekannte oder modifizierte Binaries werden blockiert (Bypass nur via `AllowInsecureToolHashBypass`).

### 10.5 DAT-Indexing / Hashing

- **LRU-Cache:** 20k Einträge für Datei-Hashes, 50k für GameKeys
- **Hash-Typen:** SHA1 (Standard), SHA256, MD5, CRC32
- **DAT-Quellen:** No-Intro, Redump, FBNEO, TOSEC (neu)
- **DAT-Index-Fingerprint:** `consoleKey + hashType + datRoot` → Cache-Invalidierung bei Änderung
- **XXE-Schutz:** `XmlReaderSettings.DtdProcessing = Prohibit`
- **Streaming-Parser:** `XmlReader` für DATs > 10 MB
- **Parent/Clone-Mapping:** Vollständig aufgelöst, in Index gespeichert

### 10.6 Risiken & Mitigations (Top 10)

| # | Risiko | Impact | Wahrscheinlichkeit | Mitigation |
|---|--------|--------|--------------------|-----------:|
| R-01 | Datenverlust bei Move-Operation | Kritisch | Niedrig | DryRun-Default, Audit-CSV, Rollback, Trash statt Delete |
| R-02 | C#-Migration bricht Behavioral-Compatibility | Hoch | Mittel | Umfangreiche Regressions-Tests, Property-Tests |
| R-03 | UI-Freeze bei großen Sammlungen | Mittel | Mittel | Off-UI-Thread, VirtualizingStackPanel, MemoryGuard |
| R-04 | Externe Tools nicht verfügbar/verändert | Mittel | Niedrig | Hash-Verifizierung, Fallback-Meldungen, Tool-Discovery |
| R-05 | DAT-Source-Änderung (URL/Format) | Niedrig | Mittel | Plugin-basierte DAT-Sources, Fallback auf letzte Version |
| R-06 | Security-Vulnerability (Path-Traversal, Zip-Slip) | Kritisch | Niedrig | Defense-in-depth, Negative Tests, CI-Gates |
| R-07 | NAS-Netzwerk-Instabilität | Mittel | Hoch | Retry mit Backoff, Throttling, Batch-Verarbeitung |
| R-08 | Feature-Creep (76 Features sind viele) | Mittel | Hoch | Phasen-basierte Roadmap, MVP-First, P0 vor P2 |
| R-09 | PowerShell-Performance-Limit (vor C#-Migration) | Mittel | Mittel | RunspacePool, LazyLoading, LRU-Cache, Parallel-Hashing |
| R-10 | Plugin-Security (Trust-Modi) | Hoch | Niedrig | `signed-only` als Default, Manifest-Validierung, Sandbox |

---

## 11. Data & Reporting

### 11.1 Report-Formate

| Format | Zweck | Security-Maßnahme |
|--------|-------|-------------------|
| **Audit-CSV** | Vollständiger Move/Rename-Log | SHA256-HMAC-Signatur, CSV-Injection-Schutz |
| **HTML-Report** | Visueller Übersichtsbericht mit Diagrammen | CSP-Header, HTML-Escaping (`HtmlEncode`) |
| **JSON-Summary** | Maschinen-lesbarer Status | Schema-Validierung |
| **JSONL-Logs** | Strukturiertes Logging (eine Zeile pro Event) | Correlation-ID, Rotation, optional GZIP |
| **PDF-Report** | Professioneller Sammlungsbericht | **[Annahme]** via HTML→PDF-Renderer |
| **CSV-Export** | Excel-kompatible Sammlungsdaten | `=+@-` Zeichen am Zeilenanfang escaped |

### 11.2 Audit-Log-Spalten

```
RootPath, OldPath, NewPath, Action, Category, Hash, Reason, Timestamp, CorrelationId
```

- Jede Zeile repräsentiert eine atomare Datei-Operation
- `Action`: Move, Rename, Delete, Convert, Quarantine
- `Category`: GAME, JUNK, BIOS, UNKNOWN
- `Hash`: SHA256 der Quelldatei
- `Reason`: Menschenlesbarer Grund (z.B. "Duplikat: Region=JP, Winner=EU")
- `CorrelationId`: 128-bit GUID für Run-Zuordnung

### 11.3 Persistierte Daten

| Datei | Ort | Zweck | Rotation |
|-------|-----|-------|----------|
| `settings.json` | `%APPDATA%\RomCleanupRegionDedupe\` | User-Settings | Versionsbasierte Migration |
| `dat-index-cache.json` | `reports/` | DAT-Hash-Cache | Invalidierung bei DAT-Änderung |
| `convert-queue.json` | `reports/` | Konvertierungs-Queue-Status | Nach Completion gelöscht |
| `run-index.json` | `reports/` | Run-History | Max. 100 Einträge, älteste entfernen |
| `move-plan-*.json` | `reports/` | DryRun-Ergebnisse | Max. 50 Dateien, Rotation |
| `*.audit.csv` | `audit-logs/` | Audit-Trail | Archivierung nach 90 Tagen **[Annahme]** |
| Thumbnails/Covers | `%APPDATA%\RomCleanupRegionDedupe\covers\` | Cover-Cache | LRU, max. 500 MB **[Annahme]** |

---

## 12. Metrics / Success Criteria

### 12.1 User-centric Metrics

| Metrik | Zielwert | Datenquelle |
|--------|----------|-------------|
| Time-to-safe-run (Erstnutzung) | < 5 Minuten | `wizard.completed` → `run.dryrun_completed` Zeitdifferenz |
| Task-Completion-Rate (DryRun→Move) | > 80% | `run.dryrun_completed` / `run.move_completed` |
| Undo-Nutzung | < 5% der Runs | `rollback.executed` / `run.move_completed` |
| Error-Rate pro Run | < 2% der Dateien | `run.errors` / `run.total_files` |
| Wizard-Abbruchrate | < 20% | `wizard.aborted` / `wizard.started` |

### 12.2 Business Metrics

| Metrik | Zielwert | Datenquelle |
|--------|----------|-------------|
| Aktive Nutzer (Runs > 3/Woche) | Wachsend (Baseline noch zu messen) | Opt-in-Telemetrie **[Annahme]** |
| 4-Wochen-Retention | > 60% | Opt-in-Telemetrie |
| Feature-Adoption (neue Features genutzt) | > 30% der Nutzer je Feature | Opt-in-Telemetrie |
| CLI-vs-GUI-Nutzung | Tracking | Entry-Point-Logging |

### 12.3 Technical Metrics

| Metrik | Zielwert | Datenquelle |
|--------|----------|-------------|
| Scan-Performance (Dateien/Min) | ≥ 5.000 (SSD) | PhaseMetrics pro Run |
| Memory-Peak (100k Dateien) | < 500 MB | MemoryGuard-Logging |
| Crash-Rate | 0 pro 100 Runs | Error-Logging |
| CI-Pipeline-Durchlaufzeit | < 5 Minuten (Unit) | GitHub Actions |
| Test-Coverage | ≥ 50% (Interim), Ziel 70% | Pester Coverage-Gate |

### 12.4 Guardrail-Metrics (dürfen nie verletzt werden)

| Metrik | Absoluter Grenzwert |
|--------|---------------------|
| Datenverlust (verschwundene Dateien ohne Audit) | **0** |
| Move außerhalb erlaubter Roots | **0** |
| Unhandled Exception → App-Crash | **0** (pro Release) |
| Security-Vulnerability (Path-Traversal, Zip-Slip, XSS) | **0** |
| Silent-Catch (leerer catch außerhalb WPF-Events) | **0** (CI-Gate) |

### 12.5 Messplan

| Datenquelle | Implementierung | Opt-in/Default |
|-------------|-----------------|----------------|
| JSONL-Logs (lokal) | `Logging.ps1` → `Write-OperationLog` | Default (lokal, kein Upload) |
| PhaseMetrics | `PhaseMetrics.ps1` → pro Run gespeichert | Default |
| UI-Telemetrie | `UiTelemetry.ps1` → EventBus → JSONL | Default (lokal) |
| Opt-in-Telemetrie (anonym) | XL-14 (anonyme Nutzungsstatistiken) | Opt-in, explizit |
| Manuelle QA | Usability-Tests mit Testpersonen | Ad-hoc |

---

## 13. Risks & Mitigations (erweitert)

| ID | Risiko | Beschreibung | Mitigation |
|----|--------|-------------|------------|
| R-01 | **Datenverlust** | Dateien werden versehentlich gelöscht/überschrieben | DryRun-Default, Move-to-Trash (kein Delete), Audit-CSV, Rollback-Wizard, Disk-Space-Check vor Move |
| R-02 | **Migration-Regression** | C#-Port erzeugt subtil anderes Verhalten (String-Vergleiche, Regex) | Property-Based-Testing, 1:1 Test-Migration, Feature-Flag-basierte schrittweise Umstellung |
| R-03 | **UI-Freeze** | Große Sammlungen blockieren UI-Thread | Alle Scans off-UI-thread, Dispatcher.Invoke für Updates, VirtualizingStackPanel, MemoryGuard |
| R-04 | **Feature-Creep** | 76 Features → Scope-Explosion, Release verzögert | Phasen-Modell strikt einhalten, P0 zuerst, MVP-Ansatz pro Feature |
| R-05 | **Tool-Verfügbarkeit** | chdman/dolphintool nicht installiert oder verändert | Auto-Discovery, SHA256-Verifizierung, klare UI-Hinweise, Deaktivierung nicht-verfügbarer Features |
| R-06 | **NAS-Instabilität** | Netzwerk-Timeouts bei SMB-Zugriff | Adaptive Batch-Größen, Retry mit exponential Backoff, Throttling-Profil |
| R-07 | **Plugin-Security** | Malicious Plugins | Trust-Modi (compat/trusted/signed), Manifest-Validierung, Sandbox-Execution **[Annahme]** |
| R-08 | **Scraping-API-Abhängigkeit** | ScreenScraper/IGDB API-Änderungen | Caching, Fallback auf Platzhalter, API-Versioning |
| R-09 | **Lizenzrisiken** | Nutzung proprietärer Formate/Tools | Nur Open-Source-Tools, keine proprietären Algorithmen, klare Lizenz-Dokumentation |
| R-10 | **Test-Instabilität** | Flaky Tests blockieren CI | FlakyRetries in Pipeline, deterministische Fixtures, keine Timing-Dependencies |

---

## 14. Test & QA Strategy

### 14.1 Test-Pyramide

```
        ┌──────────────────────────────┐
        │     E2E (GUI-Live)           │  ~10 Tests
        │  Echtes Dateisystem, WPF     │
        ├──────────────────────────────┤
        │     Integration              │  ~30 Tests
        │  Mehrere Module zusammen     │
        ├──────────────────────────────┤
        │     Unit                     │  ~200+ Tests
        │  Ein Modul, gemockte Deps    │
        └──────────────────────────────┘
```

### 14.2 Test-Kategorien (keine Alibi-Tests!)

| Kategorie | Beschreibung | Beispiele |
|-----------|-------------|----------|
| **Happy Path** | Standard-Workflow funktioniert | DryRun→Move→Report→Undo |
| **Negative Tests** | Ungültige Inputs werden korrekt abgefangen | Leerer Root, beschädigtes ZIP, Path-Traversal |
| **Edge Cases** | Grenzfälle (leere Sammlung, 1 Datei, 500k Dateien) | Alle Dateien = Junk, alle = BIOS, kein Winner |
| **Regression** | Reale Bugfixes als Fixtures | Beschädigte CUE-Tracks, Sonderzeichen im Pfad |
| **Property/Fuzz** | Random Inputs, Invarianten prüfen | Winner-Selection deterministisch, keine leeren GameKeys |
| **Security** | Angriffsvektoren testen | Zip-Slip, Path-Traversal, CSV-Injection, XXE |
| **Performance** | Benchmarks gegen Baseline | LRU-Cache < 500ms für 10k Evictions, Scan ≥ 5k/Min |
| **Mutation** | Code-Mutationen → Tests müssen rot werden | Mutation-Testing-Framework (CI-Report) |

### 14.3 Failure-First-Regel

> Jeder Test hat eine **Failure-First-Anforderung**: Er muss ohne den zu testenden Code rot werden. Tests die immer grün sind, werden entfernt.

### 14.4 Test-Daten & Fixtures

| Fixture | Beschreibung | Ort |
|---------|-------------|-----|
| 0-Byte-ROMs | Bekannte Hashes, Konsolen-Klassifikation | `dev/tests/fixtures/` |
| Beschädigte Archive | Corrupt ZIP, unvollständige CUE | `dev/tests/fixtures/corrupt/` |
| Sonderzeichen-Dateien | `Ü`, `ß`, `日本語`, `(v1.0) [!]` | `dev/tests/fixtures/special-chars/` |
| Multi-Disc-Sets | Disc 1/2/3 als BIN/CUE und CHD | `dev/tests/fixtures/multi-disc/` |
| Symlink-Fixtures | Reparse-Points für Schutz-Tests | Dynamisch erstellt in `BeforeAll` |
| Zip-Slip-Archive | Manipulierte ZIP mit `../../`-Entries | `dev/tests/fixtures/security/` |
| Große DAT-Dateien | 50k+ Einträge (generiert) | Dynamisch in Benchmark-Tests |

### 14.5 Coverage-Ziele

| Modul-Gruppe | Ziel | Begründung |
|-------------|------|-----------|
| Core-Engine (Core, Dedupe, Classification, FormatScoring) | ≥ 70% | Pure Logic, gut testbar |
| Infrastructure (FileOps, Tools, Dat) | ≥ 60% | IO-lastig, Mocks nötig |
| WPF-Module | ≥ 40% | GUI-Handling schwer zu testen |
| Contracts/Observability | ≥ 80% | Kritisch für Korrektheit |
| Gesamt | ≥ 50% (Interim), Ziel 70% | CI-Gate |

### 14.6 CI-Pipeline

| Stage | Gate | Failures = |
|-------|------|-----------|
| Unit-Tests | ≥ 50% Coverage | Block |
| PSScriptAnalyzer | Warning + Error | Block |
| Governance (Modul-Grenzen) | Keine Layer-Verletzungen | Warn |
| CatchGuard (TD-002) | Keine Silent-Catches | Block |
| Integration-Tests | Alle grün | Block |
| Mutation-Testing | Reporting only | Continue |
| Benchmark-Gate | Keine Regression | Continue |
| E2E | Alle grün (vor Release) | Block |

---

## 15. Rollout Plan / Milestones

### 15.1 Phase 1: Quick Wins (Sprint 1+2, ~4 Wochen)

**Ziel:** Sofort sichtbarer Mehrwert, minimaler Aufwand.

| Sprint | Features | Deliverables |
|--------|----------|-------------|
| Sprint 1 | QW-01 DAT-Rename, QW-04 Speicherplatz-Prognose, QW-05 Junk-Report, QW-06 Shortcuts, QW-10 Script-Generator, QW-13 CSV-Export, QW-15 M3U-Auto | 7 Features, alle S-Aufwand |
| Sprint 2 | QW-02 ECM, QW-03 Repack, QW-07 Theme, QW-08 Filter, QW-09 Heatmap, QW-11 Webhook, QW-12 Portable, QW-14 Run-History, QW-16 RetroArch-Export | 9 Features, alle S-Aufwand |

**Gate:** Alle Unit-Tests grün, Coverage ≥ 50%, PSScriptAnalyzer clean.

### 15.2 Phase 2: Medium Features (Sprint 3+4, ~5 Wochen)

**Ziel:** Feature-Parität mit clrmamepro-Level, DAT-Power, Automatisierung.

| Sprint | Features | Deliverables |
|--------|----------|-------------|
| Sprint 3 | MF-01 Missing-Tracker, MF-06 CSO→CHD, MF-08 Queue, MF-09 Batch-Verify, MF-11 DAT-Update, MF-14 Parallel-Hashing | 6 Features, DAT+Convert-Fokus |
| Sprint 4 | MF-02 Cross-Root, MF-15 Command-Palette, MF-19 Rule-Engine, MF-20 Pipelines, MF-22 Ordner-Vorlagen, MF-24 Integritäts-Monitor | 6 Features, Automatisierung+UI |

**Gate:** Integration-Tests grün, Feature-Parität-Checkliste abgehakt, Performance-Benchmarks bestanden.

### 15.3 Phase 3: Large Features (~8 Wochen)

**Ziel:** Best-in-Class ROM-Management (Cover-Scraping, Emulator-Export, Arcade, NAS).

| Batch | Features | Deliverables |
|-------|----------|-------------|
| Batch A | LF-01 Covers, LF-03 Emulator-Export, LF-05 Patch-Engine | 3 Features |
| Batch B | LF-07 Arcade Merge/Split, LF-13 Accessibility, LF-15 NAS | 3 Features |
| Batch C | Verbleibende LF-Features nach Priorisierung | Flexibel |

**Gate:** E2E-Tests grün, Accessibility-Audit bestanden, NAS-Performance-Tests bestanden.

### 15.4 Phase 4: v2.0 C#-Migration + XL-Features

**Ziel:** Plattform-Tool mit C#-Core, Docker, Cross-Platform-Readiness.

| Milestone | Beschreibung | Deliverable |
|-----------|-------------|------------|
| M1: Core-Port | Core.ps1, Dedupe.ps1, Classification.ps1, FormatScoring.ps1 → C# | `RomCleanup.Core.dll` + identische Tests |
| M2: Infra-Port | FileOps, Tools, Dat, Logging → C# | `RomCleanup.Infrastructure.dll` |
| M3: API-Port | REST-API → ASP.NET Core Minimal API | `RomCleanup.Api` |
| M4: UI-Port | WPF → C#/WPF oder Avalonia | `RomCleanup.UI.Wpf` |
| M5: Docker | CLI + API als Container | Docker-Image |
| M6: Platform | Winget/Scoop, Shell-Extension, Mobile-Web | Distribution |

**Gate:** Feature-Parität PS→C# (alle Tests grün), Performance-Benchmarks ≥ PS-Baseline, kein Behavioral-Regression.

### 15.5 Release-Checklist (pro Phase)

- [ ] Alle P0/P1-Tests grün
- [ ] Coverage-Gate bestanden (≥ 50%)
- [ ] PSScriptAnalyzer clean
- [ ] CatchGuard-Compliance
- [ ] Keine bekannten Security Issues
- [ ] CHANGELOG aktualisiert
- [ ] User-Handbook aktualisiert
- [ ] Settings-Migration getestet (Upgrade-Pfad)
- [ ] DryRun-Default verifiziert
- [ ] Rollback-Funktionalität getestet
- [ ] Performance-Benchmarks bestanden
- [ ] `test-pipeline.yml` grün

---

## 16. Open Questions (max. 8)

| # | Frage | Impact | Default-Annahme |
|---|-------|--------|-----------------|
| OQ-1 | Welche Scraping-API soll primär verwendet werden — ScreenScraper.fr oder IGDB? | Cover-Feature | **[Annahme]** ScreenScraper.fr als Primary (größere Retro-Datenbank) |
| OQ-2 | Soll die Theme-Engine Community-Themes aus dem Internet laden können, oder nur lokale? | Security-Risiko | **[Annahme]** Nur lokale Themes + manuell installierte |
| OQ-3 | Welcher PDF-Renderer soll verwendet werden (iTextSharp, QuestPDF, HTML→PDF)? | Lizenzierung + Dependency | **[Annahme]** QuestPDF (MIT-Lizenz, .NET-native) |
| OQ-4 | Sollen Telemetrie-Daten optional an einen Server gesendet werden, oder sind lokale Logs ausreichend? | Privacy, GDPR | **[Annahme]** Nur lokal, Opt-in-Upload als XL-Feature |
| OQ-5 | Wie sollen Arcade-ROM-Sets (MAME/FBNEO) validiert werden, wenn kein DAT vorhanden ist? | Arcade-Feature | **[Annahme]** Nur mit DAT, ohne DAT = Skip + Warnung |
| OQ-6 | Soll der Docker-Container ARM64 unterstützen (Raspberry Pi / Apple Silicon)? | Build-Komplexität | **[Annahme]** Nur x64 initial, ARM64 als Follow-up |
| OQ-7 | Gibt es eine Preis-Strategie (Free/Pro/Team) oder bleibt alles kostenlos? | Feature-Gates, Monetarisierung | **[Annahme]** Open-Source, Feature-Gates als Config-Option für Enterprise |
| OQ-8 | Soll der Wizard auch für wiederkehrende Nutzer optional angezeigt werden, oder nur beim Erststart? | UX-Design | **[Annahme]** Erststart + "Wizard erneut starten"-Button in Einstellungen |

---

## 17. Appendix

### A. Glossary

| Begriff | Definition |
|---------|-----------|
| **1G1R** | One Game, One ROM — Deduplizierungsprinzip: pro Spiel nur die beste Version behalten |
| **DAT** | Datendatei (XML) mit Referenz-Hashes und Namen (von No-Intro, Redump, TOSEC etc.) |
| **DryRun** | Vorschau-Modus: analysiert, zeigt Ergebnisse, verschiebt nichts |
| **GameKey** | Normalisierter Spielname für Gruppierung (ASCII-Fold, Tags entfernt) |
| **Winner** | Die "beste" Version eines Spiels nach Scoring (Region, Format, Version) |
| **Loser** | Duplikat-Versionen, die in den Trash verschoben werden |
| **Junk** | Dateien ohne Spielwert (Demos, Betas, Hacks, Homebrew) |
| **CHD** | Compressed Hunks of Data — komprimiertes Disc-Image-Format |
| **RVZ** | Revolution Virtual Zone — GameCube/Wii-optimiertes Kompressionsformat |
| **Preflight** | Vorab-Checks vor einem Run (Ordner lesbar, Tools da, Speicherplatz) |
| **Port-Interface** | Abstraktionsschicht für IO/Tools (testbar, migrierbar) |
| **Strangler-Fig** | Migrationsmuster: alte Komponenten schrittweise durch neue ersetzen |
| **ECM** | Error Code Modeler — Kompressionsformat für Disc-Images |
| **Parent/Clone** | Arcade-Konzept: Hauptversion (Parent) und Varianten (Clones) |
| **RunspacePool** | PowerShell-Threading-Mechanismus für Parallelisierung |
| **Reparse Point** | NTFS-Dateisystem-Feature (Symlinks, Junctions) |
| **Zip-Slip** | Sicherheitslücke: ZIP-Entry mit `../`-Pfaden kann außerhalb des Zielordners extrahieren |
| **CSV-Injection** | Sicherheitslücke: Formelzeichen in CSV führen zu Code-Ausführung in Excel |

### B. Beispiel-Akzeptanzkriterien (Given/When/Then)

**DAT-Rename:**
```
Given: ROM "Super Mario World (U) [!].sfc" mit SHA1-Match in No-Intro-DAT
       DAT-Name = "Super Mario World (USA).sfc"
When:  Rename-DryRun ausgeführt
Then:  Preview zeigt:
       Alt: "Super Mario World (U) [!].sfc"
       Neu: "Super Mario World (USA).sfc"
       Status: "Bereit zum Umbenennen"

Given: Rename bestätigt und ausgeführt
When:  Datei umbenannt
Then:  Datei heißt "Super Mario World (USA).sfc"
       Audit-CSV enthält Eintrag mit OldPath, NewPath, Action=Rename, Hash
       Undo ist möglich (Button aktiv)
```

**Zip-Slip-Schutz:**
```
Given: ZIP-Archiv mit Entry "../../etc/passwd"
When:  Archiv wird zur Extraktion vorbereitet
Then:  Entry wird übersprungen
       Security-Event "ZIP_SLIP_BLOCKED" geloggt mit Pfad und Archiv-Name
       Warnung in UI: "Potenziell gefährlicher Archiv-Eintrag blockiert"
       Restliche Entries werden normal verarbeitet
```

**Winner-Selection-Determinismus:**
```
Given: 3 Versionen von "Sonic the Hedgehog":
       (Europe).zip  — FormatScore 500, RegionScore 1000
       (USA).zip     — FormatScore 500, RegionScore 999
       (Japan).zip   — FormatScore 500, RegionScore 998
When:  Dedupe mit preferredRegions=[EU,US,JP] 10× hintereinander
Then:  Alle 10 Runs liefern identischen Winner = (Europe).zip
       Ergebnis ist byte-identisch
```

### C. Issue-Backlog (Checkboxes)

> **Hinweis:** GitHub Issues werden erst nach expliziter Bestätigung erstellt. Die folgende Liste dient als Übersicht.

#### P0 — Must Have

- [ ] **ISS-001:** Wizard-basierter Erststart (US-001) — M
- [ ] **ISS-002:** Regions-Dedupe Verfeinerung + deterministische Tests (US-002) — L
- [ ] **ISS-003:** DAT-Rename nach No-Intro/Redump (US-003, FR-001) — S
- [ ] **ISS-004:** M3U-Auto-Generierung (US-004, QW-15) — S
- [ ] **ISS-005:** ECM-Dekompression (FR-002, QW-02) — S
- [ ] **ISS-006:** Archiv-Repack ZIP↔7z (FR-003, QW-03) — S
- [ ] **ISS-007:** Detaillierter Junk-Report (FR-005, QW-05) — S
- [ ] **ISS-008:** PowerShell-Script-Generator (FR-014, QW-10) — S
- [ ] **ISS-009:** Excel-CSV-Export (FR-022, QW-13) — S
- [ ] **ISS-010:** C#-Core-Migration Phase A (US-019) — XL

#### P1 — Should Have

- [ ] **ISS-011:** Keyboard-Shortcuts (US-007, QW-06) — S
- [ ] **ISS-012:** Dark/Light-Theme-Toggle (US-006, QW-07) — S
- [ ] **ISS-013:** ROM-Suche/Filter (FR-008, QW-08) — S
- [ ] **ISS-014:** Duplikat-Heatmap (FR-009, QW-09) — S
- [ ] **ISS-015:** RetroArch-Playlist-Export (US-005, QW-16) — S
- [ ] **ISS-016:** Webhook-Benachrichtigung (FR-015, QW-11) — S
- [ ] **ISS-017:** Portable-Modus (FR-016, QW-12) — S
- [ ] **ISS-018:** Run-History-Browser (FR-023, QW-14) — S
- [ ] **ISS-019:** Speicherplatz-Prognose (US-008, FR-004) — S
- [ ] **ISS-020:** Missing-ROM-Tracker (US-009, FR-025) — M
- [ ] **ISS-021:** Cross-Root-Duplikat-Finder (US-010, FR-039) — M
- [ ] **ISS-022:** Konvertierungs-Queue Pause/Resume (US-011, FR-033) — M
- [ ] **ISS-023:** DAT-Auto-Update + Diff (US-012, FR-026, FR-027) — M
- [ ] **ISS-024:** Rule-Engine (US-013, FR-017) — M
- [ ] **ISS-025:** Command-Palette (US-014, FR-010) — M
- [ ] **ISS-026:** TOSEC-Support (FR-028) — M
- [ ] **ISS-027:** Parallel-Hashing (FR-029) — M
- [ ] **ISS-028:** CSO→CHD-Pipeline (FR-031) — M
- [ ] **ISS-029:** Batch-Verify (FR-034) — M
- [ ] **ISS-030:** Conditional-Pipelines (FR-018) — M
- [ ] **ISS-031:** Ordnerstruktur-Vorlagen (FR-020) — M
- [ ] **ISS-032:** Integritäts-Monitor (FR-041) — M
- [ ] **ISS-033:** ROM-Quarantäne (FR-043) — M

#### P2 — Nice to Have

- [ ] **ISS-034:** Emulator-Launcher-Integration (US-015, LF-03) — L
- [ ] **ISS-035:** Cover-Scraping (US-016, LF-01) — L
- [ ] **ISS-036:** Patch-Engine IPS/BPS/UPS (US-017, LF-05) — L
- [ ] **ISS-037:** Arcade Merge/Split (US-018, LF-07) — L
- [ ] **ISS-038:** Accessibility (LF-13, NFR-031–034) — L
- [ ] **ISS-039:** NAS/SMB-Optimierung (FR-044, LF-15) — L
- [ ] **ISS-040:** Docker-Container (US-020, FR-049) — XL
- [ ] **ISS-041:** Mobile-Web-UI (FR-050) — XL
- [ ] **ISS-042:** Plugin-Marketplace (FR-046) — L
- [ ] **ISS-043:** Theme-Engine (FR-048) — L
- [ ] **ISS-044:** PDF-Report (FR-024) — M
- [ ] **ISS-045:** Custom-DAT-Editor (FR-030) — L
- [ ] **ISS-046:** Winget/Scoop (FR-052) — M
- [ ] **ISS-047:** Shell-Extension (FR-051) — L
- [ ] **ISS-048:** Run-Scheduler + Kalender (FR-021) — M
