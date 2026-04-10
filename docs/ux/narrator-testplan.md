# Keyboard- und Narrator-Spot-Check

Stand: 2026-04-01

Dieses Dokument ist der **operatorische Spot-Check** fuer die kritischen GUI-Flows.
Der reproduzierbare, automatisierte Teil der Accessibility-Pruefung laeuft ueber die Release-Smoke-Matrix.

## 1. Automatisierter Struktur-Gate

Vor einem manuellen Spot-Check muss der strukturelle Accessibility-Gate grün sein:

```powershell
dotnet test src/Romulus.Tests/Romulus.Tests.csproj `
  --configuration Release `
  --no-build `
  --filter "FullyQualifiedName~Phase8ThemeAccessibilityTests|FullyQualifiedName~WpfProductizationTests"
```

Dieser Gate deckt im Repo reproduzierbar ab:

- Theme- und Kontrast-Invarianten
- `AutomationProperties.Name`
- Live-Region-Markup
- produktisierte Wizard-/Workflow-Bindings

## 2. Voraussetzungen fuer den Operator-Spot-Check

- Windows 10/11
- GUI startet lokal
- mindestens ein konfigurierter Root
- Narrator aktivierbar mit `Win+Ctrl+Enter`

## 3. Kritische Flows

### Anwendungsstart

- [ ] Fenstertitel wird sinnvoll vorgelesen
- [ ] Fokus landet kontrolliert auf der ersten primären Interaktionszone
- [ ] CommandBar und Navigation sind als benannte Controls erkennbar

### Navigation

- [ ] Tab-Reihenfolge bleibt konsistent
- [ ] Fokus-Indikator bleibt in allen Hauptbereichen sichtbar
- [ ] Es gibt keinen Focus-Trap

### Wizard / Guided Flows

- [ ] Wizard-Schritte werden mit sinnvollen Namen vorgelesen
- [ ] Add-Root und Regionsauswahl sind per Tastatur und Screen Reader nutzbar
- [ ] Wechsel zwischen Simple/Expert fuehrt nicht zu verlorenem Fokus

### Run / Status / Ergebnis

- [ ] `F5` / Preview ist keyboard-first erreichbar
- [ ] Statusaenderungen werden als Live-Region verstaendlich angesagt
- [ ] Report-/Rollback-Controls haben zugaengliche Namen

### Fehler- und Danger-Faelle

- [ ] Fehlermeldungen werden wahrnehmbar angekuendigt
- [ ] Cancel bleibt keyboard-first erreichbar
- [ ] Danger-Dialoge wirken modal und klar

## 4. Bestanden-Kriterien

- Kein Focus-Trap im Hauptfluss
- Alle kritischen Controls haben einen zugaenglichen Namen
- Status- und Fehleraenderungen sind screen-reader-seitig wahrnehmbar
- Keyboard-Navigation deckt die Hauptpfade ohne Maus ab

## 5. Beziehung zu R4 Stabilization

R4 markiert die GUI-/A11y-Basics nicht ueber eine fingierte Vollautomatik als erledigt.
Stattdessen gilt bewusst:

- strukturelle Accessibility-Smokes sind reproduzierbar automatisiert
- der echte Narrator-/Keyboard-Spot-Check bleibt als operatorische Freigabe dokumentiert

Damit bleibt der Release-Pfad ehrlich und trotzdem reproduzierbar.
