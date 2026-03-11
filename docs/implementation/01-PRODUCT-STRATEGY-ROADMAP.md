# RomCleanup — Produktstrategie & Feature-Roadmap

> **Dokument 1/3 — Master Strategy (Single Source of Truth)**  
> **Stand:** 2026-03-10 | **Owner:** Product Management  
> **Letztes Code-Review:** 2026-03-10 | **Nächstes Review:** Ende Phase A (voraussichtlich Ende Q2 2026)
>
> Zugehörige Dokumente:  
> → [02 — Product Requirements (PRD)](02-PRODUCT-REQUIREMENTS.md)  
> → [03 — Feature Implementation Guide](03-FEATURE-IMPLEMENTATION-GUIDE.md)

---

## 1) Produktvision

RomCleanup ist das **vertrauenswürdigste Tool** zur Pflege von ROM-Sammlungen. Es existiert, weil Retro-Gaming-Enthusiasten heute vor einem fragmentierten Werkzeug-Ökosystem stehen: clrmamepro für DAT-Verifizierung, manuelle Skripte für Deduplizierung, separate Tools für Konvertierung, keine einheitliche Oberfläche — und überall die Angst, durch einen falschen Klick jahrelang gesammelte ROMs zu verlieren.

RomCleanup löst das, indem es den gesamten **ROM-Lifecycle** in einem Tool abbildet:  
**Scan → Klassifizieren → Deduplizieren → Konvertieren → Verifizieren → Sortieren → Exportieren**

Dabei steht **Sicherheit als Produktmerkmal** an erster Stelle: DryRun-Default, Move-to-Trash statt Delete, vollständiges Audit-Log, Ein-Klick-Rollback. Jede Operation ist transparenter Vorschau unterworfen, bevor sie wirkt.

### Was RomCleanup besser macht als "nur ein Script"

- **Vertrauensarchitektur:** Kein Move ohne Preview, kein Delete ohne Audit, kein Run ohne Undo-Möglichkeit.
- **Intelligente Deduplizierung:** Regionsbasiert, DAT-verifiziert, deterministisch — nicht ein Zufallslöscher, sondern ein kuratierter Entscheider mit nachvollziehbarem Scoring.
- **Drei Zugänge:** GUI für Einsteiger, CLI für Automation, REST-API für Integration — ein Kern, drei Interfaces.
- **Progressive Disclosure:** Wizard für Neulinge (4 Entscheidungen), Experten-Modus für volle Kontrolle, 76 Feature-Module für Power-User.
- **Zukunftssicher:** Clean Architecture (Ports & Adapters), entkoppelte Core-Engine (pure functions), klarer Migrationspfad nach C#/.NET 8.

**Langfristig** wird RomCleanup zur Plattform: Community-Regelpakete, Plugin-Marketplace, Emulator-Integration (RetroArch, LaunchBox, Batocera), NAS-Deployment — ein lebendiges Ökosystem rund um digitale Spielesammlungen.

---

## 2) Zielgruppen & Use Cases

### Zielgruppe A — Solo Curator ("Max")

| Attribut | Detail |
|----------|--------|
| **Profil** | Einzelanwender, 1–3 Sammlungen, 500–5.000 ROMs |
| **Erfahrung** | Moderat; kennt RetroArch, nicht clrmamepro |
| **Nutzung** | 1× pro Monat nach Download-Session |

**Top 3 Jobs-to-be-done:**
1. **Sammlung aufräumen** — Duplikate entfernen, EU bevorzugen, Junk raus
2. **ROMs sortieren** — nach Konsole in Unterordner, Multi-Disc erkennen (M3U)
3. **Sicher ausprobieren** — DryRun sehen, verstehen was passiert, erst dann ausführen

**Friktionen (heute):**
- **Angst vor Datenverlust** — "Was passiert wenn ich auf Start drücke?" → Braucht Preview + Undo-Prominenz
- **Überfordert von Optionen** — 65 Feature-Buttons sichtbar, aber Grundflow unklar → Wizard muss erster Kontaktpunkt bleiben
- **Kein visuelles Feedback** — Nach dem Run: "Hat es geklappt?" → Report/Summary muss sofort und verständlich sein

### Zielgruppe B — Collector Pro ("Sarah")

| Attribut | Detail |
|----------|--------|
| **Profil** | Große Multi-Root-Bestände, 10k–100k+ ROMs |
| **Erfahrung** | Experte; nutzt DATs, kennt Hash-Verifizierung |
| **Nutzung** | Wöchentlich, automatisiert via CLI/Scheduler |

**Top 3 Jobs-to-be-done:**
1. **DAT-basierte Verifizierung + Missing-Report** — Welche ROMs fehlen nach Redump/No-Intro?
2. **Cross-Root-Deduplizierung** — Gleiches ROM in 5 Ordnern finden und konsolidieren
3. **Batch-Konvertierung** — BIN/CUE→CHD für 2.000 PS1-ISOs, mit Queue + Pause/Resume

**Friktionen (heute):**
- **Keine Automatisierung** — Kein Scheduler, keine wiederholbaren Pipelines → Rule-Engine + CLI-Export vorhanden, aber Discoverability fehlt
- **Performance bei Großbeständen** — 100k+ Dateien scannen dauert; kein Delta-Scan → Inkrementeller Scan + Caching nötig
- **DAT-Lifecycle fragmentiert** — DAT-Download/Update/Diff manuell → Auto-Update + Diff-Viewer vorhanden, aber Integration in Hauptflow fehlt

### Zielgruppe C — Team/Archiv-Betreiber ("Alex")

| Attribut | Detail |
|----------|--------|
| **Profil** | Mehrbenutzerbetrieb, NAS-basiert, Compliance |
| **Erfahrung** | Administrator-Level |
| **Nutzung** | Täglich via API, manuell nur für Freigaben |

**Top 3 Jobs-to-be-done:**
1. **API-gesteuerte Runs** — Automated Scans via REST-API mit Status-Monitoring + SSE-Streaming
2. **Nachvollziehbare Operationen** — Signierte Audit-Logs, Replay-Fähigkeit, Approval-Workflows
3. **NAS-optimierter Betrieb** — SMB-Retry-Logic, Docker-Deployment, Multi-Instance-Koordination

**Friktionen (heute):**
- **Kein Approval-Workflow** — Move-Operationen brauchen keine Freigabe → Für Teams kritisch
- **Audit-Replay nicht produktionsreif** — Rollback existiert, aber Replay-Erfolgsrate nicht gemessen
- ~~**API-Security-Gaps** — API-001 Root-Pfad-Validierung, API-002 Body-Size-Limit~~ → ✅ **Alle gefixt** (BUG-TRACKER Stand 2026-03-09, 50/50 Bugs geschlossen)

### Zielgruppe D — Gelegenheits-Nostalgiker ("Lena")

| Attribut | Detail |
|----------|--------|
| **Profil** | Hat eine Handvoll ROMs, will "einfach spielen" |
| **Erfahrung** | Minimal; weiß nicht was ein DAT ist |
| **Nutzung** | 1–2× im Jahr |

**Top 3 Jobs-to-be-done:**
1. **Chaos beseitigen** — "Ich hab 200 Dateien in einem Ordner, mach Ordnung"
2. **Verstehen** — "Was ist ein Duplikat? Was ist Junk? Warum wird das entfernt?"
3. **Nichts kaputt machen** — "Ich will nichts Falsches löschen"

**Friktionen (heute):**
- **Einstiegshürde zu hoch** — Selbst der Wizard setzt Grundwissen voraus (Region-Präferenz, Trash-Pfad)
- **Fachbegriffe** — "Deduplizierung", "GameKey", "DAT-Verifizierung" ohne Erklärung
- **Kein "Just Fix It"-Modus** — Braucht einen 1-Klick-Modus mit sicheren Defaults

---

## 3) North Star Metric + 6 Supporting Metrics

### North Star Metric

> **"Successful Safe Runs"** — Anzahl abgeschlossener Runs (DryRun oder Move), die ohne Fehler, ohne Datenverlust und mit positivem User-Outcome (≥1 sinnvolle Aktion) endeten.

**Warum diese Metrik?** Sie vereint Vertrauen (kein Fehler), Nutzen (etwas Sinnvolles passiert) und Engagement (Tool wird tatsächlich benutzt). Ein Safe Run bedeutet: der Nutzer hat dem Tool vertraut, das Tool hat geliefert, der Nutzer kommt wieder.

### 6 Supporting Metrics

| # | Metrik | Was sie misst | Zielwert | Datenquelle |
|---|--------|---------------|----------|-------------|
| 1 | Trust Score (Undo-Rate) | Wie oft Nutzer Undo verwenden → niedriger = mehr Vertrauen | < 5% der Move-Runs | `rollback.executed / run.move_completed` |
| 2 | Time-to-Safe-Run | Sekunden von App-Start bis erster abgeschlossener DryRun | < 300s (Neulinge), < 60s (Wiederkehrende) | `wizard.completed → run.dryrun_completed` |
| 3 | Task-Completion-Rate | % der gestarteten DryRuns, die zu Move-Bestätigung führen | > 80% | `run.dryrun_completed → run.move_confirmed` |
| 4 | Scan-Throughput | Dateien pro Minute (SSD/HDD) | ≥ 5.000 (SSD), ≥ 1.000 (HDD) | PhaseMetrics |
| 5 | Unknown-Rate | % der Dateien, die keiner Konsole zugeordnet werden können | < 3% | `classification.unknown / scan.total_files` |
| 6 | 4-Wochen-Retention | % der Nutzer, die innerhalb von 28 Tagen erneut einen Run starten | > 60% | Run-History-Timestamps |

### Guardrail-Metrics (dürfen nie verletzt werden)

| Metrik | Absoluter Grenzwert |
|--------|---------------------|
| Datenverlust (verschwundene Dateien ohne Audit) | **0** |
| Move außerhalb erlaubter Roots | **0** |
| Unhandled Exception → App-Crash | **0** pro Release |
| Security-Vulnerability (Path-Traversal, Zip-Slip, XSS) | **0** |

---

## 4) Feature-Roadmap

### Top 10 (priorisiert nach Impact × Effort × Risk)

| Rang | Feature | Kategorie | UI-Platzierung | Kompl. | Risiko | Abhängigkeiten |
|------|---------|-----------|----------------|--------|--------|----------------|
| 1 | Smart Preview Dashboard | Trust | Sortieren-Tab → nach DryRun | M | Low | — |
| 2 | One-Click Undo mit Timeline | Trust | Footer-Bar + Run-History | M | Low | Audit-CSV |
| 3 | Inline-Hilfe & Tooltip-System | UX/Joy | Global (alle Tabs) | S | Low | — |
| 4 | Preflight-Ampel mit Aktions-Hints | Trust | Sortieren-Tab → vor Start | S | Low | Settings, Tools |
| 5 | Delta-Scan (Inkrementell) | Performance | Sortieren-Tab → Scan-Optionen | L | Med | FileOps, USN-Journal |
| 6 | Guided Mode ("Just Fix It") | Workflow | Wizard Step 1 | M | Low | Wizard, Core |
| 7 | Sammlungs-Dashboard (Library) | Library | Dashboard-Tab (neuer Haupt-Tab) | M | Low | Core, Classification |
| 8 | Konvertierungs-Wizard | Format | Konvertierung-Tab → Wizard | M | Med | Tools (chdman, 7z, dolphintool) |
| 9 | Emulator-Quick-Launch | Ecosystem | Sammlungs-Ansicht → Rechtsklick | M | Med | Emulator-Pfade in Settings |
| 10 | Conflict-Resolver (interaktiv) | ROM-Intelligence | Preview-Dialog nach DryRun | M | Med | Dedupe, Core |

#### F-01: Smart Preview Dashboard

**User Value:** Nutzer sehen vor jedem Run eine visuelle Zusammenfassung: was bleibt, was geht, warum — mit Drill-Down pro Konsole/Spiel. Baut Vertrauen auf.

**Akzeptanzkriterien:**
- [ ] Tabellarische Preview mit Keep/Move/Junk pro Konsole
- [ ] Drill-Down: Klick auf Konsole zeigt alle betroffenen Dateien
- [ ] Jede Zeile zeigt Grund der Entscheidung (Region/Score/Junk-Tag)
- [ ] "Sieht gut aus"-Button → Move-Bestätigung
- [ ] Export als CSV/PDF möglich

#### F-02: One-Click Undo mit Timeline

**User Value:** Jeder Move-Run bekommt einen Timeline-Eintrag; Undo per Klick mit visueller Bestätigung. Eliminiert Datenverlust-Angst.

**Akzeptanzkriterien:**
- [ ] Undo-Button mit Puls-Animation nach Move-Run
- [ ] Timeline zeigt letzte 50 Runs mit Datum, Aktion, Dateizahl
- [ ] Selektiver Undo: einzelne Dateien oder ganzer Run
- [ ] Undo verifiziert: Zieldatei unverändert vor Rück-Move
- [ ] Undo-Log in Audit-CSV erfasst

#### F-03: Inline-Hilfe & Tooltip-System

**User Value:** Jeder Fachbegriff (GameKey, DAT, Region-Score) hat ein erklärendes Tooltip. Neue Nutzer verstehen das Tool ohne Doku.

**Akzeptanzkriterien:**
- [ ] Mindestens 30 Begriffe mit Tooltip-Erklärung
- [ ] "?"-Icon neben komplexen Optionen öffnet Kontexthilfe
- [ ] Erklärungen max. 2 Sätze, nicht technisch
- [ ] Tooltips via ResourceDictionary zentral gepflegt
- [ ] Abschaltbar via Settings

#### F-04: Preflight-Ampel mit Aktions-Hints

**User Value:** Vor jedem Run zeigt eine Ampel: Roots lesbar? Tools vorhanden? Genug Speicher? DAT konfiguriert? Mit konkreten Lösungsvorschlägen.

**Akzeptanzkriterien:**
- [ ] Grün/Gelb/Rot-Ampel für 5+ Checks (Roots, Tools, DAT, Disk-Space, Schreibrechte)
- [ ] Gelb/Rot: konkreter Aktions-Hint ("Klicke hier um 7z zu konfigurieren")
- [ ] Ampel blockiert Move bei Rot (nicht DryRun)
- [ ] Ampel aktualisiert sich live bei Settings-Änderung
- [ ] Tooltip pro Check zeigt Details

#### F-05: Delta-Scan (Inkrementell)

**User Value:** Nur geänderte/neue Dateien scannen statt alles. Reduziert Re-Scan-Zeit von Minuten auf Sekunden bei großen Sammlungen.

**Akzeptanzkriterien:**
- [ ] Fingerprint-DB (Pfad + Größe + LastWrite) persistent gespeichert
- [ ] Re-Scan erkennt nur Änderungen/Neue/Gelöschte
- [ ] Vollscan weiterhin manuell auslösbar
- [ ] Delta-Scan zeigt "X neue, Y geänderte, Z gelöschte" vor Run
- [ ] Performance: 100k-Datei-Re-Scan < 30s auf SSD

#### F-06: Guided Mode ("Just Fix It")

**User Value:** Ein-Klick-Modus für Zielgruppe D (Lena): sichere Defaults, keine Entscheidungen, automatischer DryRun → Preview → Bestätigung.

**Akzeptanzkriterien:**
- [ ] Wählt automatisch: EU>US>JP, DryRun, Standard-Junk-Regeln
- [ ] Zeigt nur: "X Duplikate gefunden, Y Junk, Z bleiben"
- [ ] Ein Button: "Aufräumen" (mit Undo-Hinweis)
- [ ] Keine sichtbaren Experten-Optionen
- [ ] Fallback auf Wizard wenn Guided Mode nicht möglich (z.B. keine Roots)

#### F-07: Sammlungs-Dashboard (Library Overview)

**User Value:** Startseite nach Login: Wie groß ist meine Sammlung? Welche Konsolen? Wie viele verifiziert? Duplikat-Heatmap.

**Akzeptanzkriterien:**
- [ ] Kacheln: Gesamtzahl, Konsolen-Anzahl, Verifiziert-%, Top-5-Konsolen
- [ ] Duplikat-Heatmap als Balkendiagramm
- [ ] Speicherverbrauch pro Konsole (Pie-Chart)
- [ ] Quick-Actions: "Aufräumen", "Verifizieren", "Konvertieren"
- [ ] Refresht bei App-Start und nach jedem Run

#### F-08: Konvertierungs-Wizard

**User Value:** Statt versteckter Optionen: ein geführter 3-Schritt-Flow für Formatkonvertierung mit Speicherplatz-Prognose.

**Akzeptanzkriterien:**
- [ ] Schritt 1: Quellformat + Konsole wählen
- [ ] Schritt 2: Zielformat (mit Empfehlung + Kompressionsvergleich)
- [ ] Schritt 3: Speicherplatz-Prognose + Start
- [ ] Queue mit Pause/Resume/Cancel
- [ ] Fehlerhafte Konvertierungen: Retry oder Skip mit Log

#### F-09: Emulator-Quick-Launch

**User Value:** Aus der Sammlungs-Ansicht direkt ein ROM im konfigurierten Emulator starten. Verbindet Aufräum-Tool mit Spielerlebnis.

**Akzeptanzkriterien:**
- [ ] Konsole→Emulator-Mapping in Settings konfigurierbar
- [ ] Doppelklick/Button startet ROM im gemappten Emulator
- [ ] Fallback: "Kein Emulator konfiguriert" mit Link zu Settings
- [ ] Letzte 10 gestartete ROMs als Quick-Access
- [ ] RetroArch-Core-Auswahl wenn RetroArch konfiguriert

#### F-10: Conflict-Resolver (interaktiv)

**User Value:** Bei mehrdeutigen Dedupe-Entscheidungen (Score-Gleichstand, unbekannte Region) den Nutzer fragen statt zu raten.

**Akzeptanzkriterien:**
- [ ] Nur bei echten Konflikten (Score-Differenz < 50)
- [ ] Dialog zeigt: beide Kandidaten mit Metadaten (Region, Format, Größe, DAT-Status)
- [ ] User wählt: "A behalten", "B behalten", "Beide behalten", "Regel merken"
- [ ] "Regel merken" speichert Entscheidung für zukünftige Runs
- [ ] Max. 20 Konflikte pro Run angezeigt (Rest: Default-Logik)

### Priorisierungs-Begründung

1. **Smart Preview** — Höchster Trust-Impact: Nutzer verstehen und kontrollieren, was passiert. Grundlage für alles.
2. **One-Click Undo** — Eliminiert die #1-Angst (Datenverlust). Existiert technisch, braucht UX-Upgrade.
3. **Inline-Hilfe** — Kleiner Aufwand, riesiger Effekt für Neulinge + Gelegenheitsnutzer. Unlocks Retention.
4. **Preflight-Ampel** — Verhindert fehlgeschlagene Runs. Aktions-Hints reduzieren Support-Aufwand.
5. **Delta-Scan** — Performance-Game-Changer für Collector Pro (Sarah). Enabler für wöchentliche Nutzung.
6. **Guided Mode** — Öffnet das Tool für Zielgruppe D (Lena). Einfachster Einstieg.
7. **Sammlungs-Dashboard** — Emotionaler Mehrwert: "Meine Sammlung auf einen Blick." Erhöht Retention.
8. **Konvertierungs-Wizard** — Macht Konvertierung zugänglich statt versteckt. Hohe Nachfrage.
9. **Emulator-Quick-Launch** — Verbindet Aufräumen mit Spielen. "Delight"-Feature.
10. **Conflict-Resolver** — Reduziert falsche Dedupe-Entscheidungen. Trust + Korrektheit.

### Feature-Backlog (11–35)

| # | Feature | User Value | Kategorie | UI-Platzierung | Kompl. | Risiko | Abhängigkeiten |
|---|---------|-----------|-----------|----------------|--------|--------|----------------|
| 11 | DAT-Diff-Viewer | Unterschiede zwischen DAT-Versionen zeigen | ROM-Intelligence | DAT-Tab → nach Update | M | Low | Dat, DatSources |
| 12 | Run-Comparison (Vorher/Nachher) | Zwei Runs visuell vergleichen, Regression erkennen | Trust | Reports-Tab | M | Low | Run-History |
| 13 | Notification-Center | Gesammelte Hinweise statt modale Dialoge | UX/Joy | Top-Right → Glocken-Icon | S | Low | — |
| 14 | Custom Region-Priority per Konsole | JP für SNES, EU für PS1 — pro Konsole | ROM-Intelligence | Settings → Erweitert | M | Low | Core, Settings |
| 15 | Batch-Rename nach DAT | ROMs automatisch nach DAT-Nomenklatur umbenennen | Library | Sammlungs-Tab → Aktion | M | Med | Dat |
| 16 | Sammlungs-Suche + Filter | Volltextsuche + Filter (Konsole, Region, Format, Status, Größe) | Library | Sammlungs-Tab → Suchleiste | M | Low | Classification |
| 17 | Speicherplatz-Prognose (global) | "Du sparst ~45 GB" / "Du brauchst ~12 GB mehr" | Trust | Sortieren-Tab → vor Start | S | Low | Core |
| 18 | Keyboard-Shortcut-Overlay | Ctrl+/ zeigt alle verfügbaren Shortcuts | UX/Joy | Global Overlay | S | Low | — |
| 19 | Plugin-Discoverability | Installierte + verfügbare Community-Plugins als Liste | Ecosystem | Settings → Plugins | M | Med | Plugin-System |
| 20 | Integritäts-Monitor (Bit-Rot) | Regelmäßige Hash-Prüfung: "3 Dateien verändert seit letztem Scan" | Trust | Dashboard-Kachel + Alert | L | Med | Dat, FileOps |
| 21 | RetroArch-Playlist-Export (erweitert) | Export mit Cover-Pfaden, Core-Mapping, Sortierung | Ecosystem | Export-Tab | M | Low | Classification |
| 22 | Watch-Mode (Filesystem-Watcher) | Automatischer Re-Scan bei neuen Dateien in Roots | Workflow | Footer-Bar → Toggle | L | Med | FileOps |
| 23 | Profile (Settings-Presets) | "Retro-Purismus", "Alles behalten", Custom | Workflow | Settings → Profil-Dropdown | M | Low | Settings |
| 24 | Ordnerstruktur-Vorlagen | Templates: "Nach Region/Konsole", "Flat" etc. | Library | Sortierung → Template-Wähler | M | Low | ConsoleSort |
| 25 | NAS/SMB-optimierter Scan | Retry-Logic, Batch-I/O, Netzwerk-Failover | Performance | Transparent (Settings-Flag) | L | Med | FileOps |
| 26 | CLI-Export aus GUI-Config | GUI-Einstellungen als CLI-Befehl exportieren | Workflow | Settings → "Als CLI-Befehl kopieren" | S | Low | CLI |
| 27 | M3U-Auto-Generierung (verbessert) | Multi-Disc erkennen + M3U erstellen | Library | Sammlungs-Tab → Aktion | S | Low | Sets |
| 28 | Cover-Art-Scraping | Automatisches Herunterladen von Cover-Bildern | Joy | Sammlungs-Ansicht → Cover-Grid | L | High | Externe API |
| 29 | PDF-Report (Professional) | Sammlungsbericht als PDF mit Diagrammen, Cover-Art | Library | Reports → Export als PDF | L | Med | Report |
| 30 | Rule-Engine UI (Visual Builder) | Regeln visuell zusammenklicken statt JSON editieren | ROM-Intelligence | Settings → Regeln → Builder | L | Med | Rule-Engine |
| 31 | Arcade-Merge/Split | Non-Merged ↔ Split ↔ Merged-Set-Konvertierung (MAME/FBNEO) | Format | Konvertierung → Arcade-Tab | L | High | Dat, Tools |
| 32 | Parallel-Hashing | Multi-threaded SHA1/SHA256-Hashing | Performance | Transparent (auto) | M | Med | PS7 |
| 33 | Theme-Marketplace | Community-Themes installieren und teilen | Joy | Settings → Themes → Browse | L | Med | Theme-Engine |
| 34 | Context-Menu-Integration | Windows-Rechtsklick → "Mit RomCleanup scannen" | Ecosystem | Windows Shell | L | High | Shell-Extension |
| 35 | Docker-Container | CLI + API als Docker-Image für Headless-Server | Ecosystem | DevOps | XL | Med | API |

---

## 5) UX/GUI Leitplanken (Design Principles)

### 10 Design-Regeln

| # | Prinzip | Konkrete Anwendung |
|---|---------|-------------------|
| DP-01 | **DryRun-First** | Jede destruktive Operation startet als DryRun. Move/Delete erst nach expliziter Bestätigung auf einem Preview-Screen. |
| DP-02 | **Progressive Disclosure** | Standard-Ansicht zeigt 4–6 Entscheidungen. Alles Weitere unter "Erweitert ▼". Expander-Sektionen für Kategorien. |
| DP-03 | **Safety Gates** | Jede Move-Operation durchläuft: Preflight → DryRun → Preview → Bestätigung → Move → Undo-Hinweis. |
| DP-04 | **Transparency** | Jede Entscheidung zeigt warum: "Region EU (Score 1000) > US (Score 999)" oder "Junk-Tag: [Beta]". |
| DP-05 | **Undo Everywhere** | Undo-Button im Footer immer sichtbar. Puls-Animation nach Move-Runs. Timeline für selektiven Undo. |
| DP-06 | **Wizard → Tabs** | Erststart: Wizard (Intent → Roots → Optionen → Preflight → DryRun → Ergebnis). Danach: Tab-basiert mit Dashboard. |
| DP-07 | **Information Hierarchy** | Headings 18px+ Bold → Subheadings 14px SemiBold → Body 13px → Caption 11px Muted. 16px Section-Spacing. |
| DP-08 | **Status as First-Class** | Dedizierte UI für: Progress (Bar + ETA + Cancel), Warnungen (Ampel), Ergebnisse (Tabelle), Fehler (roter Banner). |
| DP-09 | **Keyboard-First** | Alle Aktionen per Keyboard. Tab-Reihenfolge logisch. Ctrl+Shift+P Command-Palette. Focus-Ring sichtbar. |
| DP-10 | **Retro-Modern** | Dark Theme Default. Abgerundete Cards (8px), Neon-Border bei Hover, pulsierendes Glow bei Aktivität. |

### Styleguide (Kurzfassung)

#### Farbpalette (Dark Primary)

| Rolle | Hex | Verwendung |
|-------|-----|-----------|
| Background | `#0F0F23` | App-Hintergrund |
| Surface | `#1A1A2E` | Cards, Panels |
| Accent | `#00D4AA` | Aktive Elemente, CTAs, Highlights |
| Danger | `#FF6B6B` | Lösch-Aktionen, Fehler |
| Warning | `#FFD93D` | Preflight-Warnungen |
| Success | `#6BCB77` | Bestätigungen, Verifiziert-Status |
| Text Primary | `#E0E0E0` | Haupt-Text |
| Text Muted | `#A0A0A0` | Sekundär-Text, Hints |

#### Typografie

- **Headings:** Segoe UI Semibold, 18px+
- **Body:** Segoe UI Regular, 13px
- **Code/Paths:** Cascadia Code / Consolas, 12px
- **Badges:** 10px, Uppercase, Rounded Corners
- **Spacing:** 16px Section-Margin, 12px Card-Padding, 8px Element-Gap. Buttons min. 32×32px.

---

## 6) Kritische Product Risks & Mitigations

| # | Risiko | Impact | Wahrscheinl. | Mitigation | Test/Monitoring |
|---|--------|--------|--------------|-----------|-----------------|
| R-01 | Falsches Dedupe (Winner-Fehler) | Kritisch | Niedrig | Deterministischer Scoring-Algorithmus, DryRun-Preview mit Begründung, Conflict-Resolver | Property-based Tests, Mutation-Tests, Edge-Case-Fixtures |
| R-02 | Datenverlust bei Move | Kritisch | Niedrig | Move-to-Trash, Audit-CSV mit SHA256-Sidecar, Verify-before-Delete (BUG-009 ✅), inkrementelle Audit-Schreibung (RUN-001 ✅), Rollback-Wizard | Guardrail: 0 verschwundene Dateien. Integration-Tests mit TempDirs |
| R-03 | Falsche Konsolen-Erkennung | Hoch | Mittel | 100+ Konsolen in consoles.json, Classification-Tests, Unknown-Rate-Monitoring | Unknown-Rate < 3%, Regressionstests |
| R-04 | Path-Traversal / Zip-Slip | Kritisch | Niedrig | `Resolve-ChildPathWithinRoot`, Archiv-Entry-Pfade validieren, Reparse-Point-Blocking | Negative Tests, CI-Gate: 0 Out-of-Root-Moves |
| R-05 | UI-Freeze bei großen Sammlungen | Hoch | Mittel | Off-UI-thread, `Dispatcher.Invoke`, VirtualizingStackPanel, MemoryGuard (500MB/1GB) | Benchmark: 100k Dateien ohne Freeze |
| R-06 | Externe Tools nicht verfügbar | Mittel | Niedrig | Tool-Discovery mit Fallback, SHA256-Hash-Verifizierung, Exit-Code-Prüfung, Timeout + Retry | Preflight-Check, Mock-Tool-Tests |
| R-07 | DAT-Quellen-Ausfall | Mittel | Niedrig | Plugin-basierte Sources, Fallback auf Cache, DAT-Fingerprinting | DAT-Download-Erfolgsrate, Sidecar-Verifizierung |
| R-08 | CSV-Injection in Reports | Mittel | Niedrig | Kein `=`, `+`, `-`, `@` am Feldanfang, `HtmlEncode` in HTML-Reports | Unit-Tests mit Injection-Payloads, CI-Gate |
| R-09 | Feature Overload (76 Features) | Hoch | Hoch | Progressive Disclosure, Guided Mode, Feature-Adoption-Tracking | Wizard-Abbruchrate < 20%, Usability-Tests |
| R-10 | Performance-Regression bei Re-Scans | Mittel | Mittel | Delta-Scan, LRU-Cache (50k/20k), Parallel-Hashing (PS7), Benchmark-Gate | ≥ 5.000 Dateien/Min (SSD), CI-Benchmark |
| R-11 | Audit-CSV beschädigt → Undo unmöglich | Hoch | Niedrig | SHA256-Sidecar, inkrementelle Schreibung, Backup der letzten 10 Audit-CSVs | Replay-Erfolgsrate > 99%, Crash-Simulation |
| R-12 | API-Security | Hoch | ~~Mittel~~ → Niedrig | ~~API-001/API-002 offen~~ → ✅ **Alle gefixt** (Stand 2026-03-09), Rate-Limiting, API-Key-Auth, 127.0.0.1-only | Negative API-Tests, Security-Review |

---

## 7) Code-Review Pre-Assessment (Stand 2026-03-10)

> Ergebnisse der Code-Analyse vor Beginn der Feature-Arbeit. Muss vor Phase A (Hygiene Sprint) abgearbeitet werden.

### Bug-Status

| Quelle | Gefunden | Gefixt | Offen | Status |
|--------|----------|--------|-------|--------|
| BUG-TRACKER (5 Iterationen) | 50 | **50** | **0** | ✅ Alle geschlossen (Stand 2026-03-09) |

### Toter Code

| ID | Datei / Bereich | Problem | Empfehlung |
|----|-----------------|---------|------------|
| DEAD-001 | `Ps3Dedupe.ps1` | Leerer Stub — Logik ist in `FolderDedupe.ps1` konsolidiert. Wird aber noch in `RomCleanupLoader.ps1` (Zeile 42) geladen | Stub entfernen, Loader bereinigen |
| DEAD-211 | `AppState.ps1` `Save-AppStoreRecovery` / `Restore-AppStoreRecovery` | Implementiert (ab Zeile 240+), aber nirgends automatisch aufgerufen | Auto-Save bei App-Exit triggern oder entfernen |
| DEAD-PIPE | `dev/tools/pipeline/Invoke-TestPipeline.ps1` | Reiner Proxy zu `dev/tools/Invoke-TestPipeline.ps1` — leitet nur weiter | Konsolidieren auf eine Datei |

### Duplikate

| ID | Bereich | Problem | Empfehlung |
|----|---------|---------|------------|
| DUP-006/008 | `Tools.ps1` | 4 separate Process-Runner-Implementierungen: `Invoke-ExternalToolProcess`, `Start-ProcessWithTimeout`, `Invoke-7z`, `Invoke-DolphinToolInfoLines` | Auf gemeinsamen Runner konsolidieren |
| DUP-100 | `WpfMainViewModel.ps1` | `Set-WpfViewModelProperty` 2× definiert | Ladereihenfolge-Risiko — deduplizieren |
| DUP-ROADMAP | ~~`docs/archive/FEATURE_ROADMAP.md` vs. `docs/implementation/FEATURE_ROADMAP.md`~~ | ~~Zwei Versionen mit unterschiedlichem Stand~~ | ✅ Konsolidiert in PRODUCT_STRATEGY_ROADMAP.md Appendix A. Alte Dateien archiviert. |

### Fehlende Implementierungen (Gaps)

| ID | Bereich | Problem | Relevanz |
|----|---------|---------|----------|
| GAP-TELEMETRY | `Logging.ps1` | Telemetrie-Events (`preview.shown`, `undo.executed`, `guided_mode.used`) nicht implementiert | Phase A Metriken benötigen diese Events |
| GAP-APPSTORE | `AppState.ps1` | Auto-Save/Recovery nie automatisch getriggert | Undo-Timeline über App-Neustarts (F-02) braucht Persistenz |
| GAP-CSP | `Report.ps1` | CSP-Header fehlt in HTML-Reports (MISS-001) | Security-Lücke — vor Release fixen |

---

## 8) Release Plan (3 Phasen)

### Phase H: Hygiene Sprint (vor Phase A)

> **Zeitrahmen:** 1–2 Wochen  
> **Motto:** "Erst aufräumen, dann bauen"  
> **Ziel:** Technische Schulden bereinigen, bevor neue Features auf instabiler Basis gebaut werden.

| Woche | Task | Referenz | Risiko |
|-------|------|----------|--------|
| H-1 | `Ps3Dedupe.ps1` Stub entfernen + Loader-Referenz bereinigen | DEAD-001 | Niedrig |
| H-1 | Pipeline-Wrapper konsolidieren (1 Datei statt 2) | DEAD-PIPE | Niedrig |
| H-1 | ~~`FEATURE_ROADMAP.md` Duplikat auflösen~~ ✅ | ~~DUP-ROADMAP~~ | Erledigt (2026-03-10) |
| H-1 | `Set-WpfViewModelProperty` Duplikat entfernen | DUP-100 | Niedrig |
| H-2 | 4 Process-Runner in `Tools.ps1` konsolidieren | DUP-006/008 | Mittel |
| H-2 | AppStore-Recovery Auto-Trigger einbauen oder entfernen | DEAD-211 | Mittel |
| H-2 | CSP-Header in `Report.ps1` HTML-Reports ergänzen | GAP-CSP / MISS-001 | Niedrig |

**Definition of Done — Phase H:**
- [ ] Keine toten Stubs mehr im Loader
- [ ] Ein einziger Pipeline-Wrapper
- [ ] Eine `FEATURE_ROADMAP.md` (Root = SoT, docs-Version gelöscht oder Redirect)
- [ ] Process-Runner konsolidiert (max. 2 Varianten: sync + async)
- [ ] CSP-Header in allen HTML-Reports
- [ ] Alle bestehenden Tests grün nach Cleanup
- [ ] Coverage ≥ 34% gehalten

### Phase A: Trust & Safety Foundation

> **Zeitrahmen:** Q2 2026 (6–8 Wochen)  
> **Motto:** "Vertrauen aufbauen, Angst abbauen"

**Ziele:**
- Time-to-Safe-Run < 5 Minuten für Neulinge
- 0 Datenverlust-Incidents
- Undo-Nutzung messbar, aber < 5% (= Vertrauen)
- UI-Fehlerabbrüche pro Run < 2%

**Deliverables:**

| # | Feature | Referenz |
|---|---------|----------|
| 1 | Smart Preview Dashboard (Top-1) | Sortieren-Tab |
| 2 | One-Click Undo mit Timeline (Top-2) | Footer-Bar |
| 3 | Inline-Hilfe & Tooltip-System (Top-3) | Global |
| 4 | Preflight-Ampel mit Aktions-Hints (Top-4) | Sortieren-Tab |
| 5 | Guided Mode "Just Fix It" (Top-6) | Wizard |
| 6 | Speicherplatz-Prognose (global) (#17) | Sortieren-Tab |
| 7 | Telemetrie-Events implementieren | Logging.ps1 |

**Definition of Done — Phase A:**
- [ ] Alle 7 Deliverables implementiert und im GUI integriert
- [ ] Wizard → DryRun → Preview → Move → Undo: E2E-Test grün
- [ ] Jede Dedupe-Entscheidung zeigt Begründung in Preview
- [ ] Undo funktioniert für letzte 50 Runs (selektiv + vollständig)
- [ ] Preflight-Ampel blockiert Move bei kritischen Fehlern
- [ ] Guided Mode: 0 Entscheidungen nötig, sicheres Ergebnis
- [ ] Coverage ≥ 50%, alle P0-Bugs gefixt
- [ ] Usability-Test mit 3 Neulingen: Time-to-Safe-Run < 5min

### Phase B: Library Experience

> **Zeitrahmen:** Q3 2026 (8–10 Wochen)  
> **Motto:** "Deine Sammlung, dein Überblick"

**Ziele:**
- 4-Wochen-Retention > 60%
- Feature-Adoption > 30% je neuem Feature
- Scan-Throughput ≥ 5.000/Min (SSD)
- Unknown-Rate < 3%

**Deliverables:**

| # | Feature | Referenz |
|---|---------|----------|
| 1 | Sammlungs-Dashboard (Top-7) | Neuer Dashboard-Tab |
| 2 | Delta-Scan (Top-5) | Sortieren-Tab |
| 3 | Konvertierungs-Wizard (Top-8) | Konvertierung-Tab |
| 4 | Sammlungs-Suche + Filter (#16) | Sammlungs-Tab |
| 5 | Batch-Rename nach DAT (#15) | Sammlungs-Tab |
| 6 | Run-Comparison Vorher/Nachher (#12) | Reports-Tab |
| 7 | Notification-Center (#13) | Top-Right |
| 8 | Profile/Settings-Presets (#23) | Settings |
| 9 | Custom Region-Priority per Konsole (#14) | Settings → Erweitert |
| 10 | CLI-Export aus GUI-Config (#26) | Settings |

**Definition of Done — Phase B:**
- [ ] Dashboard zeigt Sammlungsübersicht (Kacheln + Charts) korrekt
- [ ] Delta-Scan: Re-Scan 100k Dateien < 30s auf SSD
- [ ] Konvertierungs-Wizard: 3-Schritt-Flow mit Speicherplatz-Prognose
- [ ] Suche: Volltextsuche + Filter (Konsole, Region, Format, Status)
- [ ] DAT-Rename: Preview → Bestätigung → Rename → Undo
- [ ] Notification-Center statt modale Dialoge für nicht-kritische Meldungen
- [ ] 3 Settings-Profile mitgeliefert + Custom-Profile speichern
- [ ] CLI-Export: GUI-Config als kopierbarer CLI-Befehl

### Phase C: ROM Intelligence & Extensibility

> **Zeitrahmen:** Q4 2026–Q1 2027 (10–12 Wochen)  
> **Motto:** "Intelligent, erweiterbar, vernetzt"

**Ziele:**
- Audit-Replay-Erfolgsrate > 99%
- Plugin-Adoption: >20% der Nutzer installieren mindestens 1 Plugin
- DAT-Update-Automatisierung: >50% der DAT-Nutzer nutzen Auto-Update
- Emulator-Integration: >30% der Nutzer exportieren Playlists

**Deliverables:**

| # | Feature | Referenz |
|---|---------|----------|
| 1 | Conflict-Resolver interaktiv (Top-10) | Preview-Dialog |
| 2 | Emulator-Quick-Launch (Top-9) | Sammlungs-Ansicht |
| 3 | Rule-Engine UI (Visual Builder) (#30) | Settings → Regeln |
| 4 | DAT-Diff-Viewer (#11) | DAT-Tab |
| 5 | Integritäts-Monitor Bit-Rot (#20) | Dashboard |
| 6 | RetroArch-Playlist-Export erweitert (#21) | Export-Tab |
| 7 | Watch-Mode Filesystem-Watcher (#22) | Footer-Bar |
| 8 | Plugin-Discoverability (#19) | Settings → Plugins |
| 9 | Cover-Art-Scraping (#28) | Sammlungs-Ansicht |
| 10 | Parallel-Hashing (#32) | Transparent |

**Definition of Done — Phase C:**
- [ ] Conflict-Resolver: max. 20 interaktive Konflikte, Rest Default-Logik
- [ ] Emulator-Quick-Launch: Doppelklick startet ROM im konfigurierten Emulator
- [ ] Rule-Engine UI: Regeln visuell erstellen, validieren, testen (DryRun)
- [ ] DAT-Diff: zeigt nach Update: X neue, Y umbenannt, Z entfernt
- [ ] Bit-Rot-Monitor: Dashboard-Alert wenn Hashes sich ändern
- [ ] Watch-Mode: automatischer Re-Scan bei Dateisystem-Änderungen
- [ ] Plugin-Liste: installierte + verfügbare Plugins mit Install/Update
- [ ] Cover-Scraping: Rate-Limited, Caching, Fallback auf Platzhalter
- [ ] Parallel-Hashing: messbar schnellere Verifizierung auf PS7

---

## 9) Tracking Checklist

### Phase A — Trust & Safety Foundation

#### Top-10 Features

- [ ] **F-01: Smart Preview Dashboard implementieren**
  - [ ] Tabellarische Preview-Komponente (Keep/Move/Junk pro Konsole)
  - [ ] Drill-Down: Klick auf Konsole → Dateiliste mit Grund
  - [ ] "Sieht gut aus"-Button → Move-Bestätigungs-Dialog
  - [ ] Export-Option (CSV/PDF)
  - [ ] Unit-Tests: Preview-Daten korrekt aggregiert
  - [ ] Integration-Test: DryRun → Preview → Move E2E
- [ ] **F-02: One-Click Undo mit Timeline**
  - [ ] Undo-Button in Footer-Bar (Puls-Animation nach Move)
  - [ ] Timeline-Ansicht: letzte 50 Runs
  - [ ] Selektiver Undo (einzelne Dateien + ganzer Run)
  - [ ] Undo-Verifizierung (Zieldatei unverändert)
  - [ ] Undo-Log in Audit-CSV
  - [ ] Integration-Test: Move → Undo → Dateien am Ursprungsort
- [ ] **F-03: Inline-Hilfe & Tooltip-System**
  - [ ] 30+ Begriffe mit Tooltip-Text definieren
  - [ ] "?"-Icon-Komponente für komplexe Optionen
  - [ ] Tooltips via ResourceDictionary zentral
  - [ ] Abschaltbar via Settings
  - [ ] Smoke-Test: alle Tooltips sichtbar
- [ ] **F-04: Preflight-Ampel mit Aktions-Hints**
  - [ ] 5+ Checks (Roots, Tools, DAT, Disk-Space, Schreibrechte)
  - [ ] Aktions-Hints bei Gelb/Rot
  - [ ] Ampel blockiert Move bei Rot
  - [ ] Live-Aktualisierung bei Settings-Änderung
  - [ ] Unit-Tests: jeder Check-Status-Übergang
- [ ] **F-06: Guided Mode ("Just Fix It")**
  - [ ] Intent-Option im Wizard Step 1
  - [ ] Sichere Defaults (EU>US>JP, DryRun, Standard-Junk)
  - [ ] Vereinfachte Ergebnis-Anzeige
  - [ ] Ein-Button-Workflow
  - [ ] E2E-Test: Guided Mode → DryRun → Preview → Move

#### UX/GUI Tasks

- [ ] Footer-Bar mit persistentem Undo-Button designen
- [ ] Preview-Tabelle: Sortierung, Filterung, Spalten-Layout
- [ ] Tooltip-ResourceDictionary anlegen
- [ ] Preflight-Ampel-Komponente (wiederverwendbar)
- [ ] Guided-Mode-UI (vereinfachter Wizard-Flow)
- [ ] Bestätigungs-Dialog standardisieren (Summary + Risiko + Undo-Hinweis)
- [ ] Progress-Bar-Redesign (ETA + Cancel + Phase-Indikator)

#### Safety/Undo/Preview Tasks

- [ ] Audit-CSV inkrementelle Schreibung verifizieren (RUN-001 Fix)
- [ ] Verify-before-Delete (TOCTOU) Coverage erweitern
- [ ] Rollback-Wizard E2E-Test (50 Runs, selektiv + vollständig)
- [ ] Undo-Timeline-Persistenz (über App-Neustarts hinweg)
- [ ] Preflight: Schreibrechte-Check für Trash-Ordner
- [ ] Disk-Space-Check vor Move (nicht nur DryRun)

#### Telemetry/Metrics Tasks

- [ ] `preview.shown` Event implementieren
- [ ] `undo.executed` + `undo.type` (selektiv/vollständig) loggen
- [ ] `preflight.result` (grün/gelb/rot + fehlgeschlagene Checks)
- [ ] `guided_mode.used` vs `expert_mode.used`
- [ ] Time-to-Safe-Run berechnen und in PhaseMetrics speichern
- [ ] Trust-Score (Undo-Rate) Dashboard-Kachel

#### QA/Tests Tasks

- [ ] Property-based Test: Winner-Selection deterministisch
- [ ] Negative Tests: Path-Traversal mit `..` (CI-Gate)
- [ ] Negative Tests: Zip-Slip mit manipuliertem Archiv
- [ ] Negative Tests: CSV-Injection in Report-Output
- [ ] Integration-Test: Rollback nach simuliertem Crash
- [ ] Performance-Benchmark: 100k Dateien Scan ohne UI-Freeze
- [ ] Usability-Test: 3 Neulinge, Time-to-Safe-Run messen
- [ ] Mutation-Tests: Score-Logik (Region, Format, Version)

### Phase B — Library Experience

#### Features

- [ ] **F-07: Sammlungs-Dashboard**
  - [ ] Kacheln: Gesamtzahl, Konsolen, Verifiziert-%, Top-5
  - [ ] Duplikat-Heatmap (Balkendiagramm)
  - [ ] Speicherverbrauch pro Konsole (Pie-Chart)
  - [ ] Quick-Actions (Aufräumen, Verifizieren, Konvertieren)
  - [ ] Auto-Refresh nach Runs
- [ ] **F-05: Delta-Scan**
  - [ ] Fingerprint-DB (Pfad + Größe + LastWrite)
  - [ ] Diff-Erkennung (neu/geändert/gelöscht)
  - [ ] Vollscan-Fallback-Option
  - [ ] Performance-Test: 100k Re-Scan < 30s (SSD)
- [ ] **F-08: Konvertierungs-Wizard**
  - [ ] 3-Schritt-Flow (Quelle → Ziel → Bestätigung)
  - [ ] Format-Empfehlung + Kompressionsvergleich
  - [ ] Speicherplatz-Prognose
  - [ ] Queue-Integration (Pause/Resume/Cancel)
- [ ] **F-16: Sammlungs-Suche + Filter**
  - [ ] Volltextsuche (Fuzzy)
  - [ ] Filter: Konsole, Region, Format, Status, Größe
  - [ ] Ergebnis-Liste mit Direktaktion (umbenennen, konvertieren, Details)
- [ ] F-15: Batch-Rename nach DAT
- [ ] F-12: Run-Comparison (Vorher/Nachher)
- [ ] F-13: Notification-Center
- [ ] F-23: Profile/Settings-Presets
- [ ] F-14: Custom Region-Priority per Konsole
- [ ] F-26: CLI-Export aus GUI-Config

#### UX/GUI Tasks (Phase B)

- [ ] Dashboard-Tab als neuen Haupt-Tab anlegen (Landing-Page)
- [ ] Suche/Filter-Leiste in Sammlungs-Tab
- [ ] Notification-Glocke (Top-Right) mit Badge-Counter
- [ ] Profil-Dropdown in Settings
- [ ] Konvertierungs-Wizard Schritt-Indikator (3 Steps)

#### QA Tasks (Phase B)

- [ ] Benchmark: Delta-Scan vs Full-Scan (100k Dateien)
- [ ] Integration-Test: DAT-Rename → Preview → Undo
- [ ] Suche: Fuzzy-Match für Titel mit Sonderzeichen
- [ ] Profile: Wechsel zwischen Profilen ohne Datenverlust

### Phase C — ROM Intelligence & Extensibility

#### Features

- [ ] **F-10: Conflict-Resolver (interaktiv)**
  - [ ] Erkennung: Score-Differenz < 50
  - [ ] Dialog: Kandidaten-Vergleich mit Metadaten
  - [ ] "Regel merken"-Option
  - [ ] Max. 20 Konflikte pro Run
- [ ] **F-09: Emulator-Quick-Launch**
  - [ ] Konsole→Emulator-Mapping in Settings
  - [ ] Doppelklick/Button startet ROM
  - [ ] Quick-Access: letzte 10 gestartete ROMs
- [ ] F-30: Rule-Engine UI (Visual Builder)
- [ ] F-11: DAT-Diff-Viewer
- [ ] F-20: Integritäts-Monitor (Bit-Rot)
- [ ] F-21: RetroArch-Playlist-Export (erweitert)
- [ ] F-22: Watch-Mode (Filesystem-Watcher)
- [ ] F-19: Plugin-Discoverability
- [ ] F-28: Cover-Art-Scraping
- [ ] F-32: Parallel-Hashing

#### UX/GUI Tasks (Phase C)

- [ ] Conflict-Resolver-Dialog (Side-by-Side-Vergleich)
- [ ] Emulator-Config-Panel in Settings
- [ ] Rule-Builder: Drag-and-Drop Condition-Blöcke
- [ ] Plugin-Browser-UI (Liste + Install/Update)
- [ ] Cover-Grid-View in Sammlungs-Ansicht

#### QA Tasks (Phase C)

- [ ] Conflict-Resolver: deterministische Ergebnisse bei "Regel merken"
- [ ] Watch-Mode: kein Doppel-Scan bei schnellen Dateiänderungen
- [ ] Cover-Scraping: Rate-Limit eingehalten, Fallback bei API-Ausfall
- [ ] Parallel-Hashing: identische Ergebnisse wie Single-threaded
- [ ] Plugin-Security: Trust-Modi getestet (compat/trusted/signed)

### Querschnitts-Tasks (alle Phasen)

#### Security

- [x] ~~API-001 Root-Pfad-Validierung~~ ✅ Gefixt (BUG-TRACKER 2026-03-09)
- [x] ~~API-002 Body-Size-Limit~~ ✅ Gefixt (BUG-TRACKER 2026-03-09)
- [x] ~~DATSRC-001 SSRF-Schutz~~ ✅ Gefixt (BUG-TRACKER 2026-03-09)
- [x] ~~TOOLS-001/003 Tool-Hash für alle Aufrufe~~ ✅ Gefixt (BUG-TRACKER 2026-03-09)
- [ ] Security-Review vor jedem Phase-Release

#### Dokumentation

- [ ] User-Handbook für Phase A Features aktualisieren
- [ ] Inline-Hilfe-Texte schreiben (30+ Begriffe)
- [ ] CHANGELOG pro Phase pflegen
- [ ] API-Dokumentation (OpenAPI) aktuell halten

#### CI/CD

- [ ] Coverage-Gate auf ≥ 50% halten
- [ ] Benchmark-Gate für Scan-Performance aktivieren
- [ ] Mutation-Testing-Report pro Release reviewen
- [ ] Release-Checklist pro Phase durchlaufen

---

## Appendix A: Bereits implementierte Features (v1.x — 76/76 ✅)

> Konsolidiert aus `FEATURE_ROADMAP.md` (Stand 2026-03-10). Alle 76 Features sind implementiert, getestet und im GUI integriert (65 Buttons im Features-Tab, Handler in `WpfSlice.AdvancedFeatures.ps1`). ISS-001 First-Start Wizard fertig. 1300+ Tests bestanden.

### Phase 1 — Quick Wins (S) — 16 Features ✅

| ID | Feature | Kategorie |
|----|---------|-----------|
| QW-01 | Datei-Rename nach DAT-Standard | Datei-Management |
| QW-02 | ECM-Dekompression (.ecm → .bin) | Datei-Management |
| QW-03 | Archiv-Repack (ZIP↔7z, RAR→ZIP) | Datei-Management |
| QW-04 | Speicherplatz-Prognose (Konvertierung) | Datei-Management |
| QW-05 | Detaillierter Junk-Report (Grund pro Datei) | Datei-Management |
| QW-06 | Keyboard-Shortcuts (Ctrl+R, Ctrl+Z, F5, Ctrl+Shift+D) | UI/UX |
| QW-07 | Dark/Light-Theme-Toggle + System-Auto-Detect | UI/UX |
| QW-08 | ROM-Suche/Filter in Ergebnisliste | UI/UX |
| QW-09 | Duplikat-Heatmap (Balkendiagramm) | UI/UX |
| QW-10 | PowerShell-Script-Generator (GUI→CLI-Export) | Automatisierung |
| QW-11 | Webhook-Benachrichtigung (Discord/Slack) | Automatisierung |
| QW-12 | Portable-Modus (--Portable) | Automatisierung |
| QW-13 | Export nach Excel-CSV | Reporting |
| QW-14 | Run-History-Browser | Reporting |
| QW-15 | M3U-Auto-Generierung für Multi-Disc | Integration |
| QW-16 | RetroArch-Playlist-Export (.lpl) | Integration |

### Phase 2 — Medium Features (M) — 26 Features ✅

| ID | Feature | Kategorie |
|----|---------|-----------|
| MF-01 | Missing-ROM-Tracker (DAT-basiert) | ROM-Sammlung |
| MF-02 | Cross-Root-Duplikat-Finder (Hash-basiert) | ROM-Sammlung |
| MF-03 | ROM-Header-Analyse (NES/SNES/GBA/N64) | ROM-Sammlung |
| MF-04 | Sammlung-Completeness-Ziel + Fortschrittsbalken | ROM-Sammlung |
| MF-05 | Smart-Collections / Auto-Playlists | ROM-Sammlung |
| MF-06 | CSO/ZSO→ISO→CHD-Pipeline | Format-Konvertierung |
| MF-07 | NKit→ISO-Rückkonvertierung | Format-Konvertierung |
| MF-08 | Konvertierungs-Queue mit Pause/Resume | Format-Konvertierung |
| MF-09 | Batch-Verify nach Konvertierung | Format-Konvertierung |
| MF-10 | Konvertierungs-Prioritätsliste pro Konsole | Format-Konvertierung |
| MF-11 | DAT-Auto-Update + Changelog | DAT-Management |
| MF-12 | DAT-Diff-Viewer | DAT-Management |
| MF-13 | TOSEC-DAT-Support | DAT-Management |
| MF-14 | Parallel-Hashing (RunspacePool) | DAT-Management |
| MF-15 | Command-Palette (Ctrl+Shift+P) | UI/UX |
| MF-16 | Split-Panel-Vorschau (Norton-Commander-Stil) | UI/UX |
| MF-17 | Filter-Builder (visueller Query-Builder) | UI/UX |
| MF-18 | Mini-Modus / System-Tray | UI/UX |
| MF-19 | Rule-Engine (JSON/GUI) | Automatisierung |
| MF-20 | Conditional-Pipelines | Automatisierung |
| MF-21 | Dry-Run-Vergleich (Side-by-Side) | Automatisierung |
| MF-22 | Ordnerstruktur-Vorlagen (RetroArch, Batocera etc.) | Automatisierung |
| MF-23 | Run-Scheduler mit Kalender-UI | Automatisierung |
| MF-24 | Integritäts-Monitor (Bit-Rot-Erkennung) | Sicherheit |
| MF-25 | Automatische Backup-Strategie | Sicherheit |
| MF-26 | ROM-Quarantäne | Sicherheit |

### Phase 3 — Large Features (L) — 20 Features ✅

| ID | Feature | Kategorie |
|----|---------|-----------|
| LF-01 | ROM-Thumbnail/Cover-Scraping (ScreenScraper/IGDB) | ROM-Bibliothek |
| LF-02 | Genre-/Tag-Klassifikation | ROM-Bibliothek |
| LF-03 | Emulator-Launcher-Integration (RetroArch, LaunchBox, Playnite) | ROM-Bibliothek |
| LF-04 | Spielzeit-Tracking-Import (RetroAchievements) | ROM-Bibliothek |
| LF-05 | IPS/BPS/UPS-Patch-Engine | Format & Datei |
| LF-06 | ROM-Header-Reparatur (NES/SNES) | Format & Datei |
| LF-07 | Arcade ROM-Merge/Split (MAME/FBNEO) | Format & Datei |
| LF-08 | Intelligent Storage Tiering (SSD↔HDD) | Format & Datei |
| LF-09 | Custom-DAT-Editor | DAT & Verifizierung |
| LF-10 | Clone-List-Visualisierung (Baum) | DAT & Verifizierung |
| LF-11 | Hash-Datenbank-Export (SQLite/JSON) | DAT & Verifizierung |
| LF-12 | Virtuelle Ordner-Vorschau (Treemap/Sunburst) | UI/UX |
| LF-13 | Barrierefreiheit (Screen-Reader, High-Contrast) | UI/UX |
| LF-14 | PDF-Report-Export | UI/UX |
| LF-15 | NAS/SMB-Optimierung (Retry, Batch-I/O) | Netzwerk |
| LF-16 | FTP/SFTP-Source | Netzwerk |
| LF-17 | Cloud-Settings-Sync (OneDrive/Dropbox) | Netzwerk |
| LF-18 | Plugin-Marketplace-UI | Community |
| LF-19 | Rule-Pack-Sharing (signiert) | Community |
| LF-20 | Theme-Engine (ResourceDictionary-Plugins) | Community |

### Phase 4 — XL / Strategische Features — 14 Features ✅

| ID | Feature | Kategorie |
|----|---------|-----------|
| XL-01 | Docker-Container (CLI + API) | Plattform |
| XL-02 | Mobile-Web-UI (React/Vue, Read-Only) | Plattform |
| XL-03 | Windows-Context-Menu (Shell-Extension) | Plattform |
| XL-04 | PSGallery-Modul (Install-Module) | Plattform |
| XL-05 | Winget/Scoop-Paket | Plattform |
| XL-06 | Historische Trendanalyse (Graph) | Analyse |
| XL-07 | Emulator-Kompatibilitäts-Report | Analyse |
| XL-08 | Sammlungs-Sharing (HTML/JSON) | Analyse |
| XL-09 | GPU-beschleunigtes Hashing (OpenCL/CUDA) | Performance |
| XL-10 | USN-Journal-basierter Differential-Scan | Performance |
| XL-11 | Hardlink/Symlink-Modus | Performance |
| XL-12 | clrmamepro/RomVault-Import | Import/Export |
| XL-13 | Multi-Instance-Koordination | Import/Export |
| XL-14 | Telemetrie (Opt-in) | Import/Export |

### Zusammenfassung v1.x

| Phase | Features | Status | Tests |
|-------|----------|--------|-------|
| Phase 1 (Quick Wins) | 16 | ✅ Alle implementiert + GUI | 16 Testdateien |
| Phase 2 (Medium) | 26 | ✅ Alle implementiert + GUI | 26 Testdateien |
| Phase 3 (Large) | 20 | ✅ Alle implementiert + GUI | 20 Testdateien |
| Phase 4 (XL/v2.0) | 14 | ✅ Alle implementiert + GUI | 14 Testdateien |
| **Gesamt** | **76** | **✅ 100%** | **76+ Testdateien** |

---

> **Dieses Dokument ist die lebende Produktstrategie für RomCleanup.**  
> Review-Zyklus: nach jeder Phase. Nächstes Review: Ende Phase A (voraussichtlich Ende Q2 2026).  
> *Konsolidiert am 2026-03-10 aus: PRODUCT_STRATEGY_ROADMAP.md + FEATURE_ROADMAP.md (archive + implementation).*
