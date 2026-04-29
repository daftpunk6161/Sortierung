# Romulus – Positionierung im Markt

**Stand:** 2026-04-29
**Confidence:** mittel — basiert auf öffentlich verfügbaren Quellen
(GitHub, Projekt-Webseiten) und ist bewusst eng gefasst.

---

## Was diese Datei ist und was sie nicht ist

Diese Datei ist **keine Marketing-Vergleichsmatrix.** Sie soll auch nicht
beweisen, dass Romulus „besser" als ein anderes Tool sei. Sie ordnet Romulus
ehrlich in eine Werkzeuglandschaft ein, in der die meisten Projekte ein
**anderes Problem** lösen als Romulus.

Wer einen Frontend-Launcher, einen Metadaten-Scraper oder einen Web-Manager
sucht, ist mit den unten genannten Tools besser bedient. Romulus ist kein
solches Tool und versucht es nicht zu sein (siehe
[`docs/plan/strategic-reduction-2026/feature-cull-list.md`](../plan/strategic-reduction-2026/feature-cull-list.md)).

---

## Was Romulus löst

Romulus richtet sich an Sammler und Archivare mit grossen ROM-Beständen, die

- ihre Sammlung **deterministisch** aufräumen wollen (gleiche Eingabe → gleiche
  Entscheidung),
- jeden Schritt **nachvollziehen** wollen (signierter Audit-Trail),
- jeden Schritt **rückgängig machen** wollen (Rollback),
- dabei **lokal und ohne Server** arbeiten wollen.

Das Werkzeug bietet dafür sechs Aktionen — Scan, Verify, Plan, Move, Convert,
Rollback — und sonst nichts. Frontend-Export, Scraping, Patch-Anwendung,
In-Browser-Play und ähnliches sind explizit aus dem Scope.

---

## Tools im Umfeld

Die folgende Übersicht zeigt etablierte Werkzeuge, die im weiteren ROM-Umfeld
arbeiten. Sie ist **bewusst auf Hauptzweck reduziert**, nicht auf Feature-Parität
mit Romulus.

| Tool | Hauptzweck | Plattform | Lizenz | Status |
|---|---|---|---|---|
| **Romulus** | Aufräumen + Audit + Rollback grosser Sammlungen | Windows (lokal) | privat | aktive Entwicklung |
| **RomVault** | DAT-Matching und Rebuild | Windows (CLI auch Linux) | Closed Source | aktiv, Community-Standard |
| **Igir** | CLI-ROM-Verwaltung mit grosser Plattform-Output-Vielfalt | Cross-Platform (Node.js) | GPLv3 | aktiv |
| **clrmamepro** | DAT-Matching und Rebuild | Windows | Closed Source | aktiv (legacy) |
| **SabreTools** | DAT-Manipulation (Split, Merge, Convert, Stats) | Cross-Platform (.NET) | MIT | aktiv |
| **RomM** | Web-basierter Sammlungs-Browser mit Metadaten und In-Browser-Play | Docker / self-hosted | AGPL-3 | aktiv |
| **Retool** | 1G1R-Filter (DAT-zu-DAT) | Cross-Platform (Python) | BSD-3 | seit 2026 eingestellt |

---

## Wo Romulus überlappt — und wo nicht

### Überlappung mit RomVault, clrmamepro, Igir, SabreTools

Diese Tools beherrschen **DAT-Matching, Rebuild, 1G1R-Filterung** — das gleiche
Feld, in dem auch Romulus arbeitet. RomVault und clrmamepro sind in diesem
Bereich seit 15 bis 25 Jahren etabliert.

Romulus liefert dort nichts wesentlich Neues. Was Romulus zusätzlich bietet,
ist die **lückenlose Nachweis- und Rückgängig-Spur** über die gesamte
Pipeline (Scan, Plan, Move, Convert), inklusive Sidecar+Ledger-Atomarität und
Path-Traversal-/Reparse-Schutz. Wer auf einen signierten Audit-Trail
keinen Wert legt, gewinnt mit Romulus gegenüber RomVault wenig.

### Überlappung mit RomM

RomM und Romulus klingen ähnlich, lösen aber **konträre Probleme:**

- **RomM** ist ein gehosteter Sammlungs-Browser. Er zeigt eine schöne
  Bibliothek mit Artwork, Metadaten und Spielen im Browser.
- **Romulus** verschiebt, dedupliziert und verifiziert Dateien lokal und
  schreibt darüber ein Audit. Romulus zeigt **keine Bibliothek** und spielt
  **kein Spiel ab.**

Die beiden Tools sind komplementär nutzbar: Romulus räumt auf, RomM zeigt
das Ergebnis an.

### Überlappung mit Retool

Retool war der spezialisierteste 1G1R-Filter (DAT zu DAT) und ist seit 2026
eingestellt. Romulus ersetzt Retool nicht direkt — Retool arbeitete auf
DAT-Ebene, Romulus arbeitet auf Datei-Ebene.

---

## Was Romulus heute kann, das in dieser Liste sonst kein Tool kann

Diese Punkte sind **die Existenzberechtigung von Romulus.** Wer nichts
davon braucht, hat keinen Grund Romulus zu nutzen.

1. **Signierter Audit-Trail mit Append-only-Ledger** — jede Move/Convert-Aktion
   ist mit SHA-256 protokolliert und nachträglich auf Manipulation prüfbar.
2. **Vollständiger Rollback eines Laufs** — solange die Quell-Dateien noch
   vom Sidecar referenziert werden.
3. **Preview ≡ Execute ≡ Report** — Plan, Ausführung und Report verwenden
   nachweislich dieselbe fachliche Wahrheit; GUI, CLI und API können nicht
   auseinanderlaufen (durch Pin-Tests abgesichert).
4. **Lokale REST-API mit SSE-Fortschritt** — gebunden an `127.0.0.1`, nicht
   für Multi-User gedacht, aber nützlich für Skript-Automation.

Punkt 1 und 2 sind die wichtigsten. Punkt 3 ist eine technische Garantie,
keine Marketing-Aussage.

---

## Was Romulus heute nicht kann (und nicht will)

Damit das Versprechen schmal und einlösbar bleibt, wurden folgende Bereiche
gestrichen und nicht wieder aufgenommen:

| Funktion | Empfehlung |
|---|---|
| Frontend-Export (RetroArch, ES-DE, LaunchBox, Playnite, MiSTer …) | Igir oder LaunchBox |
| Metadaten-/Artwork-Scraping (ScreenScraper, IGDB, MobyGames …) | RomM oder Skraper |
| ROM-Patching (IPS/BPS/UPS) | Igir oder dedizierte Patcher |
| MAME-Set-Building (split/merge/non-merged) | clrmamepro oder Igir |
| RetroAchievements-Compliance-Check | RomM oder dedizierte RA-Tools |
| In-Browser-Play | RomM (EmulatorJS) |
| Plugin- oder Marketplace-Mechanik | nicht im Scope |
| Cross-Platform-GUI | nicht im Scope (CLI/API laufen unter Docker) |

Diese Liste ist nicht „Roadmap" oder „kommt später". Sie ist eine bewusste
Entscheidung gegen Feature-Wucherung.

---

## Risiken in dieser Positionierung

| Risiko | Wahrscheinlichkeit | Impact | Antwort |
|---|---|---|---|
| Nutzer erwarten ein RomM-artiges Bibliotheks-Frontend und sind enttäuscht | mittel | mittel | README + Onboarding sagen sofort, was Romulus nicht ist |
| Nutzer wollen Frontend-Export und finden keinen Ersatz | hoch | gering | README verlinkt explizit auf Igir / LaunchBox |
| Audit/Rollback wirken „zu enterprise" für die Zielgruppe | mittel | gering | Defaults bleiben einfach; Audit ist still im Hintergrund |
| Sammler wollen Metadaten und springen weiter zu RomM | hoch | positiv neutral | RomM ist komplementär, nicht konkurrierend |
| Romulus wird mit RomVault verglichen und verliert beim DAT-Matching | mittel | mittel | DAT-Matching ist gleichwertig genug; differenzierender Wert ist Audit/Rollback |

---

## Kommunikations-Leitplanken

Wenn Romulus nach aussen beschrieben wird (README, Issues, Releases), gilt:

- **Eine GUI** (WPF). Keine zweite GUI mehr im Sprachgebrauch.
- **Eine Persona** (Sammler / Archivare mit grossen Sammlungen).
- **Sechs Hauptaktionen.** Nicht mehr.
- **Drei USPs:** Audit, Rollback, deterministisches Cleanup. Kein „Top-5"-,
  „Top-10"-, „Top-N"-Ranking.
- **Keine Vergleichs-Superlative.** Aussagen wie „einzigartig", „mathematisch
  garantiert" oder Behauptungen, kein anderes Werkzeug könne X, sind nicht
  zulässig. Sachliche Aussagen reichen.
- **Konsolen-Abdeckung wird ehrlich kommuniziert:** 163 erkannt, 30 als „core"
  garantiert sinnvoll, 133 Best-Effort.
