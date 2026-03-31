# Consolidated Audit

Stand: `2026-04-01`

Diese Zusammenfassung konsolidiert das externe Audit mit einer Code-Pruefung des aktuellen Repos. Sie trennt bewusst zwischen:

- `validiert`: im aktuellen Code direkt belegt
- `korrigiert`: im Audit veraltet, ungenau oder nur als Hypothese belastbar
- `umgesetzt`: in diesem Arbeitsblock bereits geaendert

## Validierte Findings

### P1

- `OpenAPI-Spec hatte Drift`
  Der eingebettete Spec-Stand lag hinter der realen API. Insbesondere fehlte `POST /runs/{runId}/rollback`, und zentrale Run-/Rollback-Schemas waren nicht explizit dokumentiert.

### P2

- `Review-Queue ohne Pagination`
  `GET /runs/{runId}/reviews` lieferte bisher unpaginiert die komplette Queue.

- `Fehlende Response-Hardening-Header`
  `X-Content-Type-Options: nosniff` und `X-Frame-Options: DENY` waren in der API-Antwort nicht gesetzt.

- `Enrichment-Pipeline sequentiell`
  Die aktuelle Pipeline verarbeitet Kandidaten in `EnrichmentPipelinePhase` sequentiell, obwohl ein `ParallelHasher` vorhanden ist. Das ist ein echter Skalierungshebel, aber noch kein in diesem Block implementierter Fix.

- `Thread-Safety-Risiko bei spaeterer Parallelisierung`
  `folderConsoleCache` ist aktuell ein `Dictionary<string, string>` und damit nicht parallelisierungssicher.

- `UI-Dichte in der Shell`
  CommandBar und ResultView enthielten harte Layoutentscheidungen, die auf kleinen Breiten unguenstig waren, unter anderem 7pt-Phasenlabels und ein fest dimensionierter Result-Chart.

### P3

- `Competitive Claims nicht repo-intern verifizierbar`
  Aussagen zu Marktposition, Konkurrenz und Alleinstellungsmerkmalen sind nicht aus dem Code beweisbar und muessen extern belegt werden.

## Korrigierte Audit-Punkte

- `API hat nicht 10 Endpoints`
  Der aktuelle Stand mappt 15 Endpoints, darunter `dats/status`, `dats/update`, `dats/import`, `convert` und `runs/{runId}/completeness`.

- `Kein DAT-Update-Endpoint` ist falsch
  `POST /dats/update` existiert bereits.

- `Performance-Zahlen sind Hypothesen, keine Messwerte`
  Aussagen wie `2-10x`, konkrete Stundenwerte oder genaue Throughput-Schaetzungen sind plausible Einschaetzungen, aber ohne Benchmark-Lauf nicht als Ist-Messung zu behandeln.

- `Verwaiste Views`
  Der Befund ist wahrscheinlich richtig, sollte aber praeziser als `aktuell nicht aktiv referenziert` formuliert werden, solange keine Laufzeitnavigation dagegen belegt ist.

## Bereits umgesetzt

### Batch 1: API-Konsistenz und Härtung

- `OpenAPI-Spec aktualisiert`
  Ergaenzt wurden:
  - `POST /runs/{runId}/rollback`
  - Response-Schemas fuer Run-Status, Run-Result, Cancel und Rollback
  - Pagination-Parameter fuer `GET /runs/{runId}/reviews`
  - Pagination-Metadaten in `ApiReviewQueue`

- `Review-Pagination implementiert`
  `GET /runs/{runId}/reviews` unterstuetzt jetzt optionale Query-Parameter:
  - `offset`
  - `limit` mit Bereich `1..1000`

- `Security-Header gesetzt`
  Die API setzt jetzt:
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`

- `Regressionstests ergaenzt`
  Neue bzw. erweiterte Tests decken ab:
  - Review-Pagination
  - Security-Header
  - OpenAPI-Rollback- und Pagination-Deklaration

### Batch 2: Enrichment-Parallelisierung

- `Enrichment parallelisiert`
  Die Enrichment-Phase verarbeitet Kandidaten jetzt parallel fuer mittelgrosse und grosse Batches.
  Dabei bleiben Rueckgabe-Reihenfolge und Ergebnisparitaet stabil.

- `Streaming-Enrichment mit begrenzter Parallelitaet`
  `ExecuteStreamingAsync` arbeitet jetzt mit einem begrenzten Task-Fenster statt strikt sequentiell.
  Die Ausgabe bleibt in Eingabereihenfolge.

- `Unnoetigen shared state entfernt`
  Der zuvor nur durchgereichte, aber nicht genutzte `folderConsoleCache` wurde aus der Phase entfernt.

- `Progress-Callbacks serialisiert`
  Fortschrittsmeldungen aus der Enrichment-Phase werden trotz Parallelitaet seriell an den Callback weitergereicht, um unsichere List-/UI-Consumer nicht zu brechen.

- `Regressionstests ergaenzt`
  Neue Tests decken ab:
  - Reihenfolgestabilitaet in `Execute`
  - Reihenfolgestabilitaet in `ExecuteStreamingAsync`
  - Ergebnisparitaet zwischen synchronem und Streaming-Pfad

### Batch 3: Simple/Expert-Modus in der aktiven Shell

- `Aktive Shell an IsSimpleMode gekoppelt`
  `MainViewModel.IsSimpleMode` synchronisiert jetzt in `ShellViewModel`, statt nur in der alten `SortView` zu existieren.

- `Expertenbereiche in der aktiven Navigation ausgeblendet`
  In Simple Mode verschwindet die `Tools`-Top-Level-Navigation, und Expert-Only-Sub-Tabs wie `Decisions`, `DatAudit`, `Filtering`, `Profiles` und `Activity Log` werden in der aktiven Shell nicht mehr angeboten.

- `Ungueltige Shell-Zustaende werden beim Umschalten korrigiert`
  Wechsel von Expert zu Simple coerct jetzt ungueltige Bereiche und Tabs direkt auf gueltige Default-Ziele, zum Beispiel `Tools -> MissionControl` und `Library/Decisions -> Library/Results`.

- `Sichtbarer Modus-Schalter in der CommandBar`
  Die aktive Shell zeigt jetzt einen direkten `Einfach/Experte`-Schalter in der CommandBar statt den Modus nur implizit im Settings-Zustand zu halten.

- `Regressionstests ergaenzt`
  Neue Tests decken ab:
  - Synchronisation `MainViewModel -> Shell`
  - Coercion von Expert-Nav/Sub-Tabs in Simple Mode
  - XAML-Bindings fuer Mode-Toggle und Expert-Only-Sichtbarkeit

### Batch 4: Responsive Header- und Result-Layouts

- `CommandBar bei schmalen Breiten entschlackt`
  In kompakten Breiten blendet die CommandBar jetzt nichtkritische Texte aus, darunter App-Titel, Runtime, Theme-/Report-Labels und die Phasenbeschriftungen. Die Phasenpunkte bleiben sichtbar, die Label-Groesse wurde fuer den nicht-kompakten Zustand angehoben.

- `Quick-Profile-Breite reduziert`
  Der Quick-Profile-Selector in der CommandBar ist schmaler, damit die rechte Header-Seite weniger frueh in Konflikt mit dem Statusbereich geraet.

- `ResultView ohne harten Pie-Chart-Zwang`
  Der Console-Distribution-Bereich verwendet keine feste `620px`-Pie-Spalte mehr. Die Verteilung erfolgt jetzt ueber flexible Sternspalten mit einer moderaten Mindestbreite fuer den Chart-Bereich.

- `Pie-Chart-Hoehe reduziert`
  Der Console-Pie-Chart ist nicht mehr auf `520px` festgenagelt und verwendet jetzt eine kompaktere Hoehe.

- `Regressionstests ergaenzt`
  Neue XAML-Tests decken ab:
  - kompakte Bindings in der CommandBar
  - Wegfall der alten festen Pie-Chart-Masse

### Batch 5: Health/Auth entkoppelt

- `Oeffentlicher Liveness-Pfad eingefuehrt`
  Die API bietet jetzt mit `GET /healthz` einen minimalen unauthentifizierten Liveness-Endpunkt fuer lokale Automation und Health-Probes.

- `Geschuetzter Detail-Healthcheck bleibt bestehen`
  `GET /health` bleibt hinter API-Key und liefert weiterhin den detailreicheren Health-Status inklusive Active-Run-Sicht.

- `Anonymous-Ausnahme bewusst eng gehalten`
  Die Auth-/RateLimit-Ausnahme gilt nur fuer `healthz`, nicht fuer die restliche API.

- `OpenAPI-Spec nachgezogen`
  Die eingebettete Spec deklariert jetzt `healthz` explizit als oeffentlichen Endpunkt ohne Security-Requirement.

- `Regressionstests ergaenzt`
  Neue Tests decken ab:
  - `healthz` ohne API-Key
  - Security-Header auf `healthz`
  - OpenAPI-Deklaration fuer den oeffentlichen Liveness-Pfad

### Batch 6: Legacy-Views aus dem aktiven Projekt entfernt

- `Nicht mehr referenzierte Alt-Views geloescht`
  `SortView`, `SettingsView` und `ConfigAdvancedView` wurden aus dem WPF-Projekt entfernt, weil sie in der aktiven Shell nicht mehr verwendet werden und nur noch tote Parallelpfade dargestellt haben.

- `Testanker auf aktive Ersatz-Views umgezogen`
  Der verbliebene XAML-Regressionscheck prueft jetzt die extrahierten aktiven Views `ConfigOptionsView` und `ConfigRegionsView` statt die geloeschte alte `SortView`.

- `Aktive Code-Kommentare bereinigt`
  Verbleibende Hinweise im produktiven Code verweisen nicht mehr auf entfernte Legacy-Views, sondern auf die aktuelle Hilfslogik.

### Batch 7: Persistentes Hash-Caching vorbereitet

- `FileHashService kann jetzt run-uebergreifend persistieren`
  Der bestehende In-Memory-Hash-Cache unterstuetzt jetzt optional persistente Eintraege auf Basis von `hashType + path + lastWriteUtc + length`, damit unveraenderte Dateien in Folge-Runs nicht erneut gehasht werden muessen.

- `Persistenz sauber im Run-Environment aktiviert`
  Das produktive Run-Environment erzeugt den `FileHashService` jetzt mit einem deterministischen Cachepfad statt nur mit einem fluechtigen Run-Cache.

- `Flush im Orchestrator abgesichert`
  Der Orchestrator schreibt den Hash-Cache im `finally`-Pfad weg, damit erfolgreiche, abgebrochene und fehlgeschlagene Runs die validierten Eintraege gleichermassen hinterlassen koennen.

- `Regressionstests ergaenzt`
  Neue Tests decken ab:
  - Reload ueber Service-Instanzen hinweg
  - Invalidierung bei Dateiaenderung
  - Persistenz-Leerung nach `ClearCache`

### Batch 8: Recognition- und Dedupe-Korrekturen

- `CrossRoot nutzt jetzt dieselbe Winner-Wahrheit wie die Haupt-Dedupe`
  Die separate Vergleichslogik im CrossRoot-Pfad wurde auf `DeduplicationEngine.SelectWinner` zurueckgefuehrt, damit Keep-/Lose-Entscheidungen nicht mehr zwischen Preview, normaler Dedupe und CrossRoot auseinanderlaufen.

- `Descriptor- und Region-Drift korrigiert`
  `.mds` ist jetzt zentral als Set-Descriptor modelliert, und `CN` wird in der Region-Erkennung nicht mehr vorzeitig zu `ASIA` degradiert.

- `Archiv-Erkennung auf informative Entries umgestellt`
  ZIP-/7z-Klassifikation betrachtet nicht mehr nur den groessten oder laengsten Entry, sondern waehlt einen fachlich informativen Repräsentanten. Dadurch sinken False-Negatives bei Multi-File-Disc-Sets und gemischten Archiven.

- `Resolver blockt klare Prioritaeten nicht mehr unnötig`
  `UniqueExtension`-Treffer werden nicht mehr allein durch schwaecheren Folder-Kontext auf `AMBIGUOUS` gekippt.

- `Generische Raw-Binaries konservativer`
  Neutrale `.bin/.img`-Dateien ohne positives ROM-Signal fallen nicht mehr blind auf `Game`.

- `VersionScore versteht jetzt mehrsegmentige Revisionsnummern`
  Formate wie `v1.2.3` tragen jetzt konsistent zur Winner-Selection bei.

- `DAT-BIOS-Flags und DAT-Spielname sauber im Kandidatenmodell`
  `isbios`/`isdevice` aus DAT-Dateien werden jetzt im aktiven Laufzeitpfad beruecksichtigt, und der gematchte DAT-Spielname wird bis in den `RomCandidate` durchgereicht.

### Batch 9: ConsoleSort-Paritaet und Safety

- `ConsoleSort nutzt angereicherte Kandidaten statt Rescan`
  Der redundante zweite Dateisystem-Scan in `ConsoleSorter` wurde durch die bereits im Run bekannten Kandidatenpfade ersetzt.

- `DryRun und Move teilen jetzt dieselbe ConsoleSort-Entscheidungsbasis`
  `ConsoleSort` ist nicht mehr Move-only. Preview und Execute verwenden denselben fachlichen Bestand und dieselben Ausschlussregeln fuer bereits geplante bzw. bereits durchgefuehrte Moves.

- `Rollback-Existenzpruefung laeuft ueber IFileSystem`
  Rohes `File.Exists` im `ConsoleSorter`-Rollback wurde auf die Dateisystemabstraktion gezogen, damit Safety-/Testpfade konsistent bleiben.

- `Move-Ergebnis traegt erfolgreich verschobene Quellpfade`
  Der Move-Pfad liefert jetzt die tatsaechlich bewegten Source-Pfade mit, damit spaetere Phasen keine Schattenlogik oder Heuristiken fuer bereits entfernte Dateien brauchen.

### Batch 10: API-Run-Lifecycle stabilisiert

- `API-Runs laufen als Long-Running-Tasks`
  Der `RunManager` startet produktive Runs nicht mehr ueber den normalen ThreadPool-Standardpfad, sondern als dedizierte Long-Running-Aufgabe. Das reduziert Starvation-/Timeout-Risiken unter Test- und Parallel-Last.

- `Parity-Regressionstests auf expliziten Completion-Wait gezogen`
  API-Kanaltests validieren jetzt den Abschluss eines Runs bewusst ueber `RunWaitDisposition.Completed`, statt implizit von einem festen 5s-Zeitfenster auszugehen.

### Batch 11: Conversion-Pipeline parallelisiert

- `Winner- und ConvertOnly-Konvertierung teilen jetzt einen parallelen Batch-Pfad`
  Die produktiven Konvertierungsphasen laufen nicht mehr strikt sequentiell, sondern ueber eine gemeinsame Batch-Ausfuehrung mit begrenzter Parallelitaet.

- `Parallelitaet ist bewusst konservativ begrenzt`
  Externe Conversion-Tools bleiben auf eine kleine, feste Parallelitaet begrenzt, um Temp-Space-, I/O- und Tool-Stabilitaetsrisiken kontrollierbar zu halten.

- `Ergebnisreihenfolge bleibt deterministisch`
  Trotz paralleler Ausfuehrung bleibt die Reihenfolge der `ConversionResult`-Liste an die Eingabereihenfolge gekoppelt.

- `Kollidierende Zielartefakte werden konservativ serialisiert`
  Dateien mit gleicher Ableitungsbasis im selben Verzeichnis werden nicht parallel konvertiert, damit `target-exists`-, Trash- und Folgeoperationen dieselbe Semantik behalten wie im sequentiellen Pfad.

- `Regressionstests ergaenzt`
  Neue Tests decken ab:
  - echte Parallelitaet fuer unabhaengige Conversion-Jobs
  - stabile Ergebnisreihenfolge trotz unterschiedlicher Abschlusszeiten
  - Serialisierung bei kollidierenden Zielartefakten

### Batch 12: API-History und Artefakt-Downloads

- `GET /runs` liefert jetzt eine deterministische Run-History`
  Die API listet sichtbare Runs in stabiler Reihenfolge `StartedUtc desc, RunId asc` und unterstuetzt paginierte Abfragen ueber `offset` und `limit`.

- `Run-Listing respektiert Client-Isolation`
  Die History zeigt nur Runs, die fuer die aktuelle Client-Bindung sichtbar sind. Fremde Run-Records werden nicht mitgeliefert.

- `Report- und Audit-Artefakte sind jetzt direkt downloadbar`
  `GET /runs/{runId}/report` und `GET /runs/{runId}/audit` liefern vorhandene HTML-/CSV-Artefakte als Attachment, ohne dass interne Dateisystempfade in Status-DTOs offengelegt werden.

- `Artefakt-Endpunkte bleiben an dieselbe Run-Wahrheit gebunden`
  Die Endpunkte verwenden ausschliesslich die im Run-Lifecycle verifizierten Artefaktpfade. Es wurde keine neue Pfadlogik oder GUI-/CLI-Schattenprojektion eingefuehrt.

- `OpenAPI-Spec nachgezogen`
  Der eingebettete Spec deklariert jetzt:
  - `GET /runs`
  - `GET /runs/{runId}/report`
  - `GET /runs/{runId}/audit`
  - `ApiRunList` als paginierte History-Antwort

- `Regressionstests ergaenzt`
  Neue Tests decken ab:
  - deterministische und paginierte Run-History
  - Client-Isolation fuer Run-Listen und Artefakt-Downloads
  - HTML-/CSV-Downloadvertraege
  - OpenAPI-Deklaration fuer History- und Artefakt-Endpunkte

### Batch 13: OpenAPI-Generierung aus der laufenden API

- `Manuell gepflegter Spec-Pfad entfernt`
  `OpenApiSpec.cs` enthaelt keine eingebettete JSON-Spezifikation mehr, sondern konfiguriert die OpenAPI-Generierung direkt ueber `Microsoft.AspNetCore.OpenApi`.

- `OpenAPI wird aus der echten Endpoint-Metadatenlage erzeugt`
  Dokument-, Operation- und Schema-Details werden jetzt aus den real gemappten API-Endpunkten erzeugt und nur noch ueber gezielte Transformer ergaenzt, zum Beispiel fuer:
  - API-Key-Security
  - `healthz` ohne Security-Requirement
  - `X-Client-Id`- und `X-Idempotency-Key`-Header
  - Request-Body-Schemas fuer `POST /runs` und `POST /runs/{runId}/reviews/approve`

- `Stabiler Alias fuer bestehende /openapi-Consumer`
  `GET /openapi` bleibt als bestehender Einstieg erhalten und leitet kontrolliert auf den generierten Paket-Standard `GET /openapi/v1.json` weiter.

- `Schema-Semantik abgesichert`
  Die OpenAPI-Regressionen validieren jetzt die live generierte Spezifikation aus dem Testhost statt einen statischen String. Integer-Felder werden ueber die vom Generator gelieferte Integer-Semantik abgesichert.

## Naechste sinnvolle Umsetzungsbloecke

1. `Build-time Export der generierten OpenAPI-Spec`
   Die Laufzeit-Spezifikation ist jetzt die Quelle der Wahrheit. Falls ein versioniertes Artefakt fuer externe Consumer benoetigt wird, sollte dieses kuenftig aus derselben Generierung exportiert werden statt separat gepflegt zu werden.
