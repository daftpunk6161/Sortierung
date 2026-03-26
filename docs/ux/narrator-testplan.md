# Narrator DryRun-Testplan (TASK-133)

Dieser Testplan definiert die manuelle Verifikation der Screen-Reader-Kompatibilität
mit Windows Narrator für einen vollständigen DryRun-Durchlauf in Romulus.

## Voraussetzungen

- Windows 10/11 mit aktiviertem Narrator (Win+Ctrl+Enter)
- Romulus GUI gestartet
- Mindestens ein Root konfiguriert

## Testschritte

### 1. Anwendungsstart
- [ ] Narrator liest Fenstertitel "Romulus" vor
- [ ] Fokus liegt auf CommandBar (TabIndex 1)
- [ ] Navbar-Items sind als Buttons mit Name erkennbar

### 2. Navigation
- [ ] Tab-Reihenfolge folgt: CommandBar → SubTabBar → ContextPanel → NavigationRail → SmartActionBar
- [ ] Jede Navbar-Sektion wird mit korrektem Namen vorgelesen
- [ ] Fokus-Indikator (Neon-Border) ist sichtbar

### 3. Wizard / Setup
- [ ] Wizard-Schritte (Ellipsen) werden als "Schritt 1/2/3" vorgelesen
- [ ] Add-Root-Button hat Namen "Ordner hinzufügen"
- [ ] Region-Checkboxen lesen ihren Zustand vor (aktiviert/deaktiviert)

### 4. Einstellungen
- [ ] Theme-Dropdown liest "Theme umschalten" vor
- [ ] Alle 6 Theme-Items werden einzeln vorgelesen
- [ ] Lokale-ComboBox liest "Sprache" vor
- [ ] Trash-Pfad-Textfeld liest seinen Label/Name vor

### 5. DryRun starten
- [ ] Run-Button (F5) wird als "Vorschau starten" vorgelesen
- [ ] Kein Focus-Trap während des Runs
- [ ] Fortschrittsanzeige wird als LiveRegion aktualisiert (Assertive)
- [ ] Phasen-Status (Scanning, Deduplicating) wird angekündigt

### 6. Ergebnis
- [ ] Abschluss-Status wird vorgelesen (Completed / Preview complete)
- [ ] Report-Button (Ctrl+R) hat zugänglichen Namen
- [ ] Rollback-Button (Ctrl+Z) hat zugänglichen Namen

### 7. Fehlerfall
- [ ] Fehlermeldungen werden als Assertive-LiveRegion angekündigt
- [ ] Cancel-Button wird korrekt vorgelesen
- [ ] Danger-Dialoge werden als modale Warnung erkannt

### 8. Themes
- [ ] Alle 6 Themes beibehalten Narrator-Kompatibilität
- [ ] HighContrast-Theme: 3px Fokusring sichtbar
- [ ] Kein visueller Informationsverlust bei Theme-Wechsel

## Bestanden-Kriterien

- Kein Focus-Trap in der gesamten Anwendung
- Alle interaktiven Controls haben AutomationProperties.Name
- LiveRegions melden Status-Updates korrekt
- Keyboard-Navigation funktioniert vollständig (kein Maus-Zwang)
- WCAG AA Kontrast in allen Themes (AAA in HighContrast)
