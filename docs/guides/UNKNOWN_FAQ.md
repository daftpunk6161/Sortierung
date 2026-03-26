# Warum werden Dateien als UNBEKANNT eingestuft? (FAQ)

> Implementierung: `src/RomCleanup.Core/Classification/ConsoleDetector.cs`

## 1. Keine DAT-Dateien konfiguriert

**Problem:** Ohne DAT-Dateien kann kein Hash-Abgleich stattfinden (hoechste Erkennungssicherheit: 100%).

**Loesung:** Laden Sie DAT-Dateien fuer Ihre Konsolen herunter:
- **No-Intro** (Cartridge-basierte Systeme)
- **Redump** (Disc-basierte Systeme)
- Konfigurieren Sie den DAT-Ordner in `settings.json` unter `dat.datRoot`

## 2. Mehrdeutige Dateiendung

**Problem:** Endungen wie `.iso`, `.bin`, `.cue`, `.chd`, `.img` werden von vielen Konsolen genutzt (PS1, PS2, Saturn, Dreamcast, Sega CD, PC Engine CD, 3DO, etc.). Ohne zusaetzliche Hinweise kann die Konsole nicht bestimmt werden.

**Loesung:** Verschieben Sie die Datei in einen Ordner mit dem Konsolennamen:
```
ROMs/
  PS1/
    game.bin
    game.cue
  dreamcast/
    game.chd
  saturn/
    game.iso
```
Die Ordner-Erkennung ordnet Dateien anhand des Ordnernamens zu.

## 3. Disc-Header nicht lesbar (IO-Fehler)

**Problem:** Die Datei konnte nicht geoeffnet werden -- moegliche Ursachen:
- Datei ist von einem anderen Prozess gesperrt
- Keine Leseberechtigung
- Datei ist beschaedigt / korrupter Header
- Netzlaufwerk nicht erreichbar

**Loesung:**
- Schliessen Sie andere Programme, die die Datei verwenden
- Pruefen Sie die Dateiberechtigungen
- Testen Sie die Datei mit einem Emulator

## 4. 7-Zip nicht verfuegbar (Archive)

**Problem:** Archive (`.zip`, `.7z`) koennen nur mit dem 7z-Tool analysiert werden. Ohne 7z werden Archive als UNBEKANNT eingestuft.

**Loesung:** Installieren Sie 7-Zip und konfigurieren Sie den Pfad in `settings.json` unter `toolPaths.7z`.

## 5. DolphinTool fehlt (GameCube/Wii)

**Problem:** Formate wie `.rvz`, `.gcz`, `.wia`, `.wbf1` koennen nur mit DolphinTool zwischen GameCube und Wii unterschieden werden.

**Loesung:**
- Installieren Sie Dolphin Emulator (enthaelt `dolphintool`)
- Alternativ: Datei in `GC/` oder `wii/` Ordner verschieben
- Oder: Disc-ID manuell dem Dateinamen hinzufuegen: `game [RZDE01].rvz`

## Erkennungs-Reihenfolge (Prioritaet)

| Stufe | Methode | Konfidenz | Beispiel |
|-------|---------|-----------|---------|
| 1 | DAT Hash Match | 100% | SHA1-Hash stimmt mit DAT-Eintrag ueberein |
| 1b | ZIP PS1/PS2 | 75% | ZIP enthaelt PS1/PS2-typische Dateien |
| 2 | Archive Content | 70% | ZIP enthaelt nur `.gba`-Dateien |
| 3 | Archive Disc Header | 95% | ISO innerhalb ZIP hat PS2-Header |
| 4 | DolphinTool | 90% | Disc-ID identifiziert als Wii |
| 4b | Disc-ID im Dateinamen | 85% | `[RZDE01]` im Dateinamen |
| 5a | Ordner-Name | 50% | Datei liegt in `PS1/` |
| 5b | Disc Header (direkt) | 95% | ISO-Datei hat SEGA-SATURN-Header |
| 5c | Eindeutige Extension | 60% | `.gba` = Game Boy Advance |
| 5d | Dateiname-Regex | 30% | Dateiname enthaelt `[NES]` |
| 5e | Mehrdeutige Extension | 40% | `.cso` = PSP (meistens) |

## 6. Konsolen ohne eindeutige Extension (DATA-01/DATA-02)

**Problem:** 26 Konsolen haben keine eindeutige Dateiendung. Die Erkennung erfolgt ausschließlich ueber Ordnernamen oder DAT-Hash-Abgleich. Betroffen sind u.a.: 3DO, Arcade, Dreamcast, GameCube, PS1, PS2, PS3, PSP, Saturn, Sega CD, Xbox, Xbox 360.

8 davon sind zusaetzlich Nicht-Disc-Systeme (Arcade, Channel F, CPC, Neo Geo, NGPC, PC-98, Supervision, X68000) — hier hilft auch kein Disc-Header.

**Loesung:**
- DAT-Dateien fuer diese Systeme konfigurieren (hoechste Sicherheit)
- Dateien in klar benannte Ordner verschieben (z.B. `Dreamcast/`, `PS2/`, `Arcade/`)
- Fuer Disc-Systeme: Disc-Header-Erkennung funktioniert bei `.iso`/`.bin`-Dateien

## 7. Extension-Kollision `.md` (Mega Drive vs. Markdown) (DATA-03)

**Problem:** Die Endung `.md` wird sowohl fuer Sega Mega Drive ROMs als auch fuer Markdown-Textdateien verwendet. In gemischten Verzeichnissen kann es zu Falscherkennung kommen.

**Loesung:**
- Bevorzugen Sie `.gen` als Endung fuer Mega Drive ROMs
- Die Erkennung prueft automatisch, ob `.md`-Dateien Textinhalt haben (< 512 KB, kein Null-Byte → Markdown, kein ROM)
- Alternativ: Mega Drive ROMs in eigenen Ordner `Mega Drive/` verschieben

## 8. Systeme in DAT-Katalog ohne Konsolen-Eintrag (DATA-05)

**Problem:** 28 Systeme sind im DAT-Katalog aufgefuehrt, haben aber keinen Eintrag in `consoles.json`. DAT-Abgleich fuer diese Systeme ist nicht moeglich. Betroffen: FDS, CDTV, PS5, MAME, SuperGrafx, Atomiswave u.a.

**Loesung:** Diese Systeme werden in kuenftigen Versionen ergaenzt. Aktuell koennen Sie die Dateien manuell in passend benannte Ordner sortieren.
