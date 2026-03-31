# C4: ECM/NKit Format-Support

## Problem

ECM (Error Code Modeler) und NKit (Nintendo Kit) sind verbreitete Kompressionsformate
in der ROM-Community. Romulus kann diese Formate weder lesen noch konvertieren.

## Loesungsansatz

Neue Tool-Invoker fuer `ecm` und `nkit`, Integration in die bestehende
Conversion-Pipeline via Registry-Erweiterung.

### Neue Komponenten

1. **EcmInvoker** (`Infrastructure/Tools/EcmInvoker.cs`)
   - `ecm` Tool: ECM → BIN Dekompression
   - `ecm-compress`: BIN → ECM Kompression
   - Verifikation: Source-Size vs. Output-Size Plausibilitaetspruefung

2. **NkitInvoker** (`Infrastructure/Tools/NkitInvoker.cs`)
   - `NKitProcessingApp.exe`: NKit → ISO Konvertierung (GameCube/Wii)
   - Batch-Processing Support
   - Verifikation: NKit-Header-Pruefung, ISO-Size-Validierung

### Registry-Erweiterung (`data/conversion-registry.json`)

```json
{
  "capabilities": [
    {
      "sourceExtension": ".ecm",
      "targetExtension": ".bin",
      "tool": "ecm",
      "command": "decompress",
      "lossless": true,
      "cost": 2,
      "verification": "size-check"
    },
    {
      "sourceExtension": ".nkit.iso",
      "targetExtension": ".iso",
      "tool": "nkit",
      "command": "convert",
      "applicableConsoles": ["GC", "WII"],
      "lossless": true,
      "cost": 5,
      "verification": "hash-verify"
    },
    {
      "sourceExtension": ".nkit.gcz",
      "targetExtension": ".iso",
      "tool": "nkit",
      "command": "convert",
      "applicableConsoles": ["GC", "WII"],
      "lossless": true,
      "cost": 5,
      "verification": "hash-verify"
    }
  ]
}
```

### Tool-Hashes (`data/tool-hashes.json`)

Neue Eintraege fuer ecm.exe und NKitProcessingApp.exe mit SHA256-Hashes
bekannter sicherer Versionen.

### Conversion-Graph

```
.ecm → .bin → .chd (via ecm + chdman)
.nkit.iso → .iso → .rvz (via nkit + dolphintool)
.nkit.gcz → .iso → .rvz (via nkit + dolphintool)
```

Multi-Step-Konvertierung via bestehendem `IConversionPlanner`.

## Abhaengigkeiten

- Bestehende Conversion-Pipeline (A4 Decomposition)
- `IConversionPlanner` und `IConversionExecutor` Interfaces
- Tool-Hash-Verifikation (ToolRunnerAdapter)

## Risiken

- ECM/NKit Tools sind Community-Builds, keine offiziellen Releases → Hash-Pinning schwierig
- NKit-Konvertierung kann sehr langsam sein (Wii: 4-8 GB)
- Multi-Step-Fehlerbehandlung: Cleanup bei Abbruch zwischen Steps

## Testplan

- Unit: EcmInvoker/NkitInvoker mit Mock-ToolRunner
- Integration: ECM → BIN Konvertierung mit echtem ecm-Tool
- Edge: Korrupte ECM-Dateien, fehlende Tools, Timeout
- Regression: Bestehende CHD/RVZ/ZIP-Konvertierungen unberuehrt
