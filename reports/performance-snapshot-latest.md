# Performance Snapshot

Timestamp: 2026-02-28 16:41:08
Host: ASUSTOWER
PowerShell: 7.5.4

## GameKey
- Samples: 50000
- Standard: 10068.79 ms (4965.8/s)
- AliasMode: 9765.62 ms (5120/s)

## Console Detection
- Samples: 50000
- Cold cache: 12122.98 ms
- Warm cache: 11705.99 ms
- Speedup: 1.04x

## 100k Candidate Set
- Samples: 100000
- Runtime: 9508.71 ms
- Memory delta: 132 MB
- Memory gate: <= 450 MB (PASS)
- Grouped keys: 20000
- Sorted rows: 100000

## DryRun Scan
- Files requested: 6000
- Runtime: 88891.47 ms
- Winners: 6000

JSON: C:\Code\Sortierung\reports\performance-snapshot-20260228-164344.json
MD: C:\Code\Sortierung\reports\performance-snapshot-20260228-164344.md
