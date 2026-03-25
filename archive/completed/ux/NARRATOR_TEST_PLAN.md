# Narrator-Testplan: DryRun-Workflow (A11Y-006)

**Zweck:** Manueller Screen-Reader-Test des kompletten DryRun-Workflows.  
**Tool:** Windows Narrator (Win+Ctrl+Enter) oder NVDA.  
**Voraussetzung:** App gestartet, mindestens 1 ROM-Verzeichnis mit Testdateien vorhanden.

---

## 1. App-Start & Navigation

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 1.1 | App starten, Focus auf Hauptfenster | Fenstertitel "RomCleanup" | `MainWindow.xaml` Title |
| 1.2 | Tab zu erstem Tab | "Sortierung" oder Tab-Name | TabControl TabItem |
| 1.3 | Tab zwischen allen 4 Tabs wechseln | Jeder Tab-Name wird angesagt: Sortierung, Werkzeuge, Einstellungen, Ergebnis | TabControl |

## 2. Sortierung-Tab: Setup

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 2.1 | Focus auf Modus-Toggle | "Einfacher Modus" / "Experten-Modus" Radio-Auswahl | `SortView.xaml` AutomationProperties.Name |
| 2.2 | Alt+I drücken | "Einfacher Modus, ausgewählt" | AccessKey `_I` |
| 2.3 | Alt+E drücken | "Experten-Modus, ausgewählt" | AccessKey `_E` |
| 2.4 | Tab zu "ROM-Ordner hinzufügen" | "ROM-Ordner hinzufügen, Schaltfläche" | AutomationProperties.Name |
| 2.5 | Tab zu "ROM-Ordner entfernen" | "ROM-Ordner entfernen, Schaltfläche" | AutomationProperties.Name |
| 2.6 | Focus auf ROM-Verzeichnisliste | "ROM-Verzeichnisliste" | AutomationProperties.Name |
| 2.7 | Tab zu Region-Checkboxen | "Region Europa bevorzugen, Kontrollkästchen, aktiviert/deaktiviert" | AutomationProperties.Name pro Region |
| 2.8 | Preset-Buttons durchtabben | "Preset: Sichere Vorschau", "Preset: Volle Sortierung", "Preset: Konvertierung" | AutomationProperties.Name |

## 3. DryRun starten

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 3.1 | Focus auf Start/DryRun-Button (Footer) | "DryRun starten, Schaltfläche" | `MainWindow.xaml` Footer |
| 3.2 | Enter/Klick → Run startet | — | — |
| 3.3 | Status-Anzeige ändert sich | "Gesamtstatus: Läuft…" (LiveSetting=Polite) | `MainWindow.xaml` L98 |
| 3.4 | Fortschrittsbalken aktualisiert | "Fortschritt: X%" (LiveSetting=Polite) | `MainWindow.xaml` L114 |
| 3.5 | Fortschrittstext aktualisiert | "Fortschrittstext: Scanning…" o.ä. (LiveSetting=Polite) | `MainWindow.xaml` L117 |

## 4. Pipeline-Phasen (während Run)

Die Pipeline-Stepper-Ellipsen in `SortView.xaml` zeigen visuell den Phasenfortschritt. Für Screen-Reader ist der Status über die `LiveSetting=Polite`-Elemente in MainWindow (Gesamtstatus, Fortschritt, Fortschrittstext) hörbar.

| Phase | Erwartete Status-Änderung (Polite-Announce) |
|-------|---------------------------------------------|
| Preflight | "Preflight…" |
| Scan | "Scanning…" |
| Dedupe | "Deduplizierung…" |
| Sort | "Sortierung…" |
| Move | (übersprungen bei DryRun) |
| Convert | (übersprungen bei DryRun) |
| Fertig | "Abgeschlossen" / "Fertig" |

> **Hinweis:** Die Pipeline-Stepper-Ellipsen haben aktuell kein `AutomationProperties.Name`. Der Phasenfortschritt wird ausschließlich über die LiveSetting-Felder im Status-Bar kommuniziert. Falls gewünscht, können die Ellipsen in einer zukünftigen Iteration mit `AutomationProperties.Name` und `LiveSetting` erweitert werden.

## 5. Ergebnis-Tab: Dashboard (nach Abschluss)

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 5.1 | Tab zu Ergebnis | "Ergebnis" Tab-Name | TabControl |
| 5.2 | Focus auf Dashboard-Karten | Jede Karte wird angesagt (LiveSetting=Polite): | `ResultView.xaml` |
| 5.2a | — | "Aktueller Modus: DryRun" | L75 |
| 5.2b | — | "Anzahl Behalten: N" | L82 |
| 5.2c | — | "Anzahl Duplikate: N" | L89 |
| 5.2d | — | "Anzahl Junk: N" | L96 |
| 5.2e | — | "Laufzeit: Xs" | L103 |
| 5.2f | — | "Sammlungs-Qualität: N%" | L110 |
| 5.2g | — | "Anzahl Spiele: N" | L117 |
| 5.2h | — | "Verifizierte Dateien: N" | L124 |
| 5.2i | — | "Bereinigungs-Quote: N%" | L131 |
| 5.3 | Focus auf Log-Bereich | Log-Einträge mit Level und Nachricht | LogEntryTemplate |
| 5.4 | Focus auf Report-Vorschau Button | "Report-Vorschau aktualisieren, Schaltfläche" | AutomationProperties.Name |

## 6. Simple-Mode Zusammenfassung

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 6.1 | Im Einfach-Modus: Ergebnis-Tab | Zusammenfassung mit DashDupes, DashJunk, DashWinners | `ResultView.xaml` Simple-Mode Panel |
| 6.2 | Focus auf "Jetzt als Move ausführen" | "Jetzt als Move ausführen, Schaltfläche" | StartMoveCommand Button |

## 7. Abbruch-Szenario

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 7.1 | Während Run: Abbrechen-Button | "Abbrechen, Schaltfläche" | CancelCommand |
| 7.2 | Abbruch bestätigt | "Gesamtstatus: Abgebrochen" (LiveSetting=Polite) | MainWindow Status |

## 8. Fehler-Szenario

| # | Aktion | Erwartete Ansage | XAML-Quelle |
|---|--------|-----------------|-------------|
| 8.1 | Run ohne ROM-Verzeichnis starten | Preflight-Fehler, Status "Fehler" | Gesamtstatus LiveSetting |
| 8.2 | ErrorSummary sichtbar | Fehlerbeschreibung im ErrorSummary-Panel | `ResultView.xaml` ErrorSummary |

---

## Checkliste

Vor jedem Release einmal durchspielen:

- [ ] **Alle Tabs** per Tastatur erreichbar (Tab / Shift+Tab / Pfeiltasten)
- [ ] **Modus-Wechsel** (Alt+I / Alt+E) wird angesagt
- [ ] **ROM-Ordner hinzufügen/entfernen** Buttons per Tastatur aktivierbar
- [ ] **Region-Checkboxen** werden mit Name + Zustand angesagt
- [ ] **DryRun-Start** — Status-Änderung wird live angesagt (Polite)
- [ ] **Fortschrittsbalken** — Prozentwert wird bei Änderung angesagt
- [ ] **Fortschrittstext** — Phasenname wird bei Änderung angesagt
- [ ] **Dashboard-Karten** — alle 9 Werte nach Abschluss per Tab erreichbar und benannt
- [ ] **Log-Einträge** — Level + Nachricht lesbar
- [ ] **Abbruch** — Status "Abgebrochen" wird angesagt
- [ ] **Fehler** — ErrorSummary wird angesagt
- [ ] **Kein Focus-Trap** — aus jedem Bereich per Tab herausnavigierbar

---

## Bekannte Limitierungen

1. **Pipeline-Stepper-Ellipsen** haben kein `AutomationProperties.Name` — Phasenfortschritt nur über Status-Bar (LiveSetting) kommuniziert.
2. **WebView2 Report-Vorschau** — WebView2-Inhalt ist für Narrator nicht vollständig zugänglich. HTML-Report sollte alternativ im Browser geöffnet werden.
3. **ToolItems in Werkzeuge-Tab** — dynamisch generierte Buttons erben `AutomationProperties.Name` aus dem ToolItem-Model (`ToolItem.Label`).

---

## Verwandte Artefakte

| Artefakt | Pfad |
|----------|------|
| XAML Accessibility Tests (automatisiert) | `src/RomCleanup.Tests/GuiViewModelTests.cs` |
| Audit Compliance Tests | `src/RomCleanup.Tests/AuditComplianceTests.cs` |
| Audit-Dokument A11Y-001 bis A11Y-006 | `plan/refactor-wpf-gui-ux-audit-1.md` |
| Theme/Contrast WCAG AA | `src/RomCleanup.UI.Wpf/Themes/SynthwaveDark.xaml` |
