# C6: Community-Profile / Rule-Packs

## Problem

Benutzer koennen Settings nicht einfach teilen. Keine Moeglichkeit,
optimierte Konfigurationen fuer bestimmte Szenarien (z.B. "Retro-Purist",
"Space-Saver", "DAT-Verifier") als Profile zu exportieren und zu importieren.

## Loesungsansatz

Shareable Settings-Profile mit Validierung, Import/Export und Versionierung.

### Profil-Format

```json
{
  "profileVersion": 1,
  "name": "Retro Purist",
  "description": "Maximale DAT-Konformitaet, keine Konvertierung, strenge Junk-Erkennung",
  "author": "community",
  "createdUtc": "2026-01-15T10:00:00Z",
  "settings": {
    "preferRegions": ["EU", "US", "JP"],
    "removeJunk": true,
    "aggressiveJunk": false,
    "enableDat": true,
    "enableDatRename": true,
    "convertFormat": null,
    "sortConsole": true,
    "onlyGames": true,
    "keepUnknownWhenOnlyGames": true
  },
  "rules": {
    "customJunkPatterns": [],
    "regionOverrides": {},
    "extensionFilter": []
  }
}
```

### Komponenten

1. **ProfileService** (`Infrastructure/Profiles/ProfileService.cs`)
   - `Export(settings) → ProfileJson`
   - `Import(profileJson) → Settings` (mit Validierung)
   - `Validate(profile) → ValidationResult`
   - Schema-Versioning mit Forward/Backward-Kompatibilitaet

2. **ProfileValidator** (`Infrastructure/Profiles/ProfileValidator.cs`)
   - JSON-Schema-Validierung
   - Security: Keine Script-Injection, keine Pfad-Manipulation
   - Werte-Validierung gegen bekannte Enums/Ranges

3. **ProfileStore** (`Infrastructure/Profiles/ProfileStore.cs`)
   - Lokaler Speicher: `%APPDATA%\Romulus\profiles\`
   - Built-in Profiles: mitgeliefert in `data/profiles/`
   - Import von URL oder Datei

### Integration

- GUI: Profil-Dropdown in Settings, Import/Export Buttons
- CLI: `romulus --profile "Retro Purist"` oder `--profile-file path.json`
- API: `GET /profiles`, `POST /profiles/import`, `POST /runs` mit `profileId` Feld

### Built-in Profile

- **Default** — Aktuelle Standardeinstellungen
- **Retro Purist** — DAT-Maximum, keine Konvertierung
- **Space Saver** — Aggressive Konvertierung, Junk-Entfernung
- **Quick Scan** — Nur DryRun, minimale Optionen

## Abhaengigkeiten

- Bestehende Settings-Infrastruktur
- RunOptions Mapping

## Risiken

- Profile-Versioning bei Settings-Aenderungen
- Security: Importierte Profile duerfen keine gefaehrlichen Pfade/Optionen setzen
- Naming-Konflikte bei Community-Profilen

## Testplan

- Unit: Export/Import Roundtrip, Validation, Schema-Versioning
- Integration: Profil → RunOptions → Identische Ergebnisse
- Security: Injection-Versuche in Profil-Feldern
- Edge: Veraltete Profile, fehlende Felder, unbekannte Felder
