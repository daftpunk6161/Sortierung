# C3: Guided Workflows / Wizard-Mode

## Problem

Neue Benutzer sind von der Vielzahl der Optionen ueberfordert. Kein geführter Einstieg,
kein Szenario-basierter Workflow, keine kontextuelle Hilfe.

## Loesungsansatz

Wizard-UI in WPF mit 3-5 Standard-Szenarien, die den Benutzer schrittweise
durch die Konfiguration fuehren.

### Standard-Szenarien

1. **Quick Clean** — Schnelle Deduplizierung mit Standardeinstellungen
   - Root waehlen → Region-Prioritaet → DryRun → Review → Move
   - Minimalste Konfiguration, beste Defaults

2. **Full Audit** — Komplette Sammlung analysieren und bereinigen
   - Roots waehlen → DAT aktivieren → Junk entfernen → Konvertierung → DryRun → Summary → Move
   - Alle Features aktiviert, ausfuehrlicher Report

3. **DAT Verification** — Nur DAT-basierte Verifizierung
   - Root + DatRoot waehlen → Scan → Completeness Report → Missing List

4. **Format Optimization** — Nur Konvertierung
   - Root waehlen → Format-Analyse → ConvertOnly → Report

5. **New Collection Setup** — Ersteinrichtung
   - DATs herunterladen → Roots einrichten → Quick Scan → Empfehlungen

### UX-Konzept

```
┌─────────────────────────────────────────┐
│  Welcome to Romulus                     │
│                                         │
│  What would you like to do?             │
│                                         │
│  [🔍] Quick Clean                       │
│  [📊] Full Audit                        │
│  [✓]  DAT Verification                  │
│  [⚡] Format Optimization               │
│  [🆕] New Collection Setup              │
│                                         │
│  [ Advanced Mode → ]                    │
└─────────────────────────────────────────┘
```

Jedes Szenario hat 3-5 Schritte mit:
- Fortschrittsanzeige (Step 1/4)
- Kontextuelle Erklaerung
- Sinnvolle Defaults
- Zurueck-Button
- Abbrechen jederzeit

### Implementierung

- Rein GUI-seitig, keine Backend-Aenderungen
- Neue WPF Views: `WizardView.xaml`, `WizardStepView.xaml`
- ViewModel: `WizardViewModel.cs` mit Step-State-Machine
- Mapping: Wizard-Output → bestehende RunOptions

## Abhaengigkeiten

- Keine Backend-Abhaengigkeiten
- Bestehende ViewModels und RunOptions

## Risiken

- UX-Design erfordert Benutzerfeedback/Iteration
- Szenarien muessen mit neuen Features aktuell gehalten werden
- Wizard darf nicht mit Advanced-Mode kollidieren

## Testplan

- Unit: Step-Navigation, Option-Mapping
- UI: Manuelles Durchklicken aller Szenarien
- Regression: Wizard-Output erzeugt identische RunOptions wie Advanced-Mode
