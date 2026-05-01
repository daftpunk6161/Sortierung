# Test-Suite Critique – Romulus.Tests

**Verdict: needs_changes** (3 blocking, 4 warning, 2 suggestion)

## Kurzfazit

Die Test-Suite ist nicht „seit paar Tagen mist", sondern strukturell falsch dimensioniert: **7 004 Tests in einer einzigen Assembly, sequentiell** (`maxParallelThreads: 1`), darin **199 API-Tests, die je eine eigene `WebApplicationFactory<Program>` + SQLite-DB + Profil-Verzeichnis hochziehen**. Der Testhost-Crash bei der Vollsuite ist OOM/Lifetime-Drift — kein einzelner „kaputter Test", sondern Akkumulation. Daneben hat sich eine spürbare Schicht **Alibi-Tests** angesammelt (47 Source-String-Asserts, 379 reine `File.Exists`-Checks), die viel Laufzeit kosten und wenig Schutzwert liefern.

Beobachteter Zustand vor Abbruch: testhost = **4,76 GB RAM, 70 min CPU**, läuft noch — also nicht „stürzt sofort ab", sondern wächst kontinuierlich. Frühere Crashes der Vollsuite passen exakt zu diesem Profil.

## Findings

### 🔴 BLOCKING

#### F-1: Single-Assembly + Single-Thread + 7 004 Tests = OOM-Bombe
- **Kategorie:** complexity / over-engineering
- **Location:** [src/Romulus.Tests/xunit.runner.json](src/Romulus.Tests/xunit.runner.json) (`maxParallelThreads: 1`), [src/Romulus.Tests/Romulus.Tests.csproj](src/Romulus.Tests/Romulus.Tests.csproj#L1-L40)
- **Impact:** Die ganze Suite läuft als **ein Testhost-Prozess**, sequentiell, mit `<TargetFramework>net10.0-windows</TargetFramework>` (zieht WPF-Stack). Jedes der 168 IDisposable-Test-Files akkumuliert temp-Verzeichnisse, jede der 199 `WebApplicationFactory`-Instanzen einen DI-Container. Dass testhost auf 4,76 GB wächst, ist erwartet, nicht überraschend.
- **Beleg:** `WriteAllBytes`-Aufrufe: 212, `GetTempPath`-Verwendungen: 329, `WebApplicationFactory<>`-Files: 14 (199 Tests), STA/WPF-Files: 4. Die 137 Wave7/RunOrchestrator-Tests laufen **gefiltert in 13 s** — also kein einzelner langsamer Test, sondern Skalierungsproblem.
- **Empfehlung:**
  1. Test-Projekt **splitten** in:
     - `Romulus.Tests.Unit` (Core/Contracts, `net10.0`, ohne WPF-Referenz, parallelisierbar)
     - `Romulus.Tests.Api` (`net10.0`, `xunit.runner.json` mit `maxParallelThreads: 1`, eigener Prozess)
     - `Romulus.Tests.Wpf` (`net10.0-windows`, sequentiell, eigener Prozess)
  2. Vor dem Split: `--blame-hang-timeout 60s --blame-crash` über die ganze Suite laufen lassen, dann konkrete Crash-Test-Namen aus `Sequence.xml` ziehen.
- **Alternative:** Wenn Split zu groß ist, mindestens `[CollectionDefinition("Api")]` mit `IClassFixture<SharedApiFactory>`, sodass nicht 199x ein WebHost gebaut wird.

#### F-2: `WebApplicationFactory` pro Test statt geteilte Fixture
- **Kategorie:** over_engineering
- **Location:** [src/Romulus.Tests/ApiTestFactory.cs](src/Romulus.Tests/ApiTestFactory.cs#L17-L46), aufgerufen aus 14 API-Test-Files mit zusammen **199 `[Fact]`/`[Theory]`**
- **Impact:** Jeder Test baut DI-Container, Kestrel-TestServer, eigene SQLite-Datei, eigenes Profil-Verzeichnis. Cleanup nur in `Dispose(disposing)` der Factory — wenn Test wirft, bleibt Temp liegen → Disk-Watcher-Last, Antivirus-Scans, weitere Verlangsamung. Das ist die Hauptursache für den OOM-Drift.
- **Empfehlung:** `IsolatedApiFactory` per `IClassFixture` oder `ICollectionFixture` teilen, wo der Test nur Endpoint-Verhalten prüft (Mehrheit). Echte Isolation nur dort, wo State zwingend separat sein muss (Settings-Mutation, DB-Reset).
- **Alternative:** Pro API-Datei eine Factory + `IClassFixture` — reduziert von 199 Hosts auf 14.

#### F-3: 379 `File.Exists`-Asserts und 47 Source-String-Asserts = Alibi-Schicht
- **Kategorie:** logic_gap (Schutzwert) + complexity (Laufzeit)
- **Location:** Top-Files: [Phase10And11RoundVerificationTests.cs](src/Romulus.Tests/Phase10And11RoundVerificationTests.cs), [Wave7TestGapRegressionTests.cs](src/Romulus.Tests/Wave7TestGapRegressionTests.cs), [Wave5MediumRegressionTests.cs](src/Romulus.Tests/Wave5MediumRegressionTests.cs), [Phase4FixTests.cs](src/Romulus.Tests/Phase4FixTests.cs), Phase13–15RedTests
- **Impact:** Verstößt direkt gegen [.claude/rules/testing.instructions.md](.claude/rules/testing.instructions.md) („Keine Alibi-Tests / Pseudo-Abdeckung"). `Assert.Contains("Application.Current?.Dispatcher", source, ...)` (Wave5MediumRegressionTests:114) prüft, dass ein **String im Quellcode** vorkommt — das fängt keinen Bug, das fixiert nur die exakte Zeichenkette und bricht beim nächsten harmlosen Refactor. Genauso `Assert.True(File.Exists(...))` ohne anschließende Inhaltsprüfung: testet, dass der Test selbst eine Datei angelegt hat.
- **Empfehlung:** Die 47 Source-String-Asserts **ersatzlos streichen oder durch echte Verhaltensassertion ersetzen** (Dispatcher wirklich aufrufen statt Quelle greppen). Die 379 `File.Exists`-Asserts auditieren — viele werden in Kombination mit `ReadAllBytes`/`ReadAllText` legitim sein, aber etliche sind isolierter „Setup-Echo".
- **Alternative:** Pin-Tests mit Hash-Vergleich der relevanten Datei statt String-Grep.

### 🟡 WARNING

#### F-4: `xUnit1030` seit Tagen offen (`ConfigureAwait(false)` in Tests)
- **Kategorie:** logic_gap
- **Location:** [CliSimulateSubcommandTests.cs:136](src/Romulus.Tests/CliSimulateSubcommandTests.cs#L136), [CliSimulateSubcommandTests.cs:184](src/Romulus.Tests/CliSimulateSubcommandTests.cs#L184)
- **Impact:** xUnit-Sync-Context wird umgangen → Test kann auf falschem Thread fortsetzen, in WPF-/STA-Kombinationen latent flaky. Build-Warnung, die in jedem `dotnet test`-Lauf rauscht und echte Warnungen versteckt.
- **Empfehlung:** `.ConfigureAwait(false)` an beiden Stellen entfernen.

#### F-5: `[CollectionDefinition]`/`DisableTestParallelization` faktisch ungenutzt (4 Stellen für 7 004 Tests)
- **Kategorie:** over_engineering / complexity
- **Impact:** Mit `maxParallelThreads: 1` global gibt es ohnehin keine Parallelität — die 4 Collection-Markierungen sind kosmetisch. Sobald F-1 umgesetzt ist (Projekt-Split), müssen die echten Sharing/Isolation-Linien neu gezogen werden.
- **Empfehlung:** Im Zuge von F-1 Collections gezielt einsetzen (z. B. eine API-Collection pro Endpoint-Familie), nicht pauschal.

#### F-6: `--blame-hang-timeout 120s` ist für eine 30+min-Suite zu kurz, um Crashes zu lokalisieren
- **Kategorie:** logic_gap (Diagnose)
- **Impact:** Wenn der Crash nicht durch Hang sondern durch OOM-Drift entsteht, schreibt `--blame-crash` keinen `Sequence.xml`-Hinweis auf einen Einzeltest, sondern bricht ohne Schuldigen ab — exakt das beobachtete Bild.
- **Empfehlung:** Vor dem Split einmal mit `--blame-crash --diag artifacts/test-diag.log` laufen lassen UND zusätzlich `dotnet-counters monitor -p <testhost-pid>` daneben. Wenn Heap monoton wächst, ist es Akkumulation (F-1/F-2), kein Einzeltest.

#### F-7: `WriteAllBytes` 212-mal — vermutlich viele Mock-ROMs ohne Cleanup
- **Kategorie:** complexity
- **Impact:** Bei 329 `GetTempPath`-Verwendungen und nur 168 `IDisposable`-Test-Files bleibt Test-Output liegen, wenn Konstruktor wirft oder ein Test früh `Assert.Fail` macht.
- **Empfehlung:** Konventionspflicht: jeder Test, der `Path.GetTempPath()` benutzt, MUSS in einer `IDisposable`/`IAsyncDisposable`-Klasse leben mit `try/finally`-Cleanup im `Dispose`. Lint-Test (Reflection) als Pin.

### 🔵 SUGGESTION

#### S-1: Test-Projekt-Naming aufräumen — `Phase10And11RoundVerificationTests`, `Block56_StructuralDebtHygieneTests`, `Wave5MediumRegressionTests`, `Block1_ReleaseBlockerTests`
- **Kategorie:** naming
- **Impact:** Die Datei-Namen verraten Audit-Wellen, nicht das geprüfte Verhalten. Bei einem Crash ist „Phase14RedTests" als Schuldiger nutzlos, „GameKeyNormalizer_NoEmptyKeyTests" wäre verwertbar.
- **Empfehlung:** Beim Projekt-Split (F-1) nach **Domain** umbenennen, nicht nach Audit-Welle. Audit-IDs gehören in Test-Methoden-Doc, nicht in Datei-Namen.

#### S-2: STA/WPF-Test-Erkennung präzisieren
- **Kategorie:** assumption
- Der Scan zeigt nur 4 Files mit `Application.Current`-Pattern und davon ist die Hälfte Source-String-Grep (Wave5MediumRegressionTests), nicht echte STA-Konstruktion. **Annahme** „WPF-Tests sind das Problem" stimmt vermutlich **nicht** — das eigentliche Problem ist API-Volumen + Single-Thread (F-1/F-2). Bevor jemand am `[STAFact]`-Setup schraubt: erst F-1 belegen.

## What works well

- `xunit.runner.json` ist überhaupt vorhanden und bewusst konfiguriert (auch wenn die Wahl falsch ist).
- `ApiTestFactory.IsolatedApiFactory.Dispose` räumt Temp-Verzeichnisse auf — die Mechanik ist da, sie wird nur 199-mal pro Lauf bezahlt.
- Gefilterte Subsets (Wave7/RunOrchestrator/Provenance: 137 Tests / 13 s) **sind grün und schnell** → die Tests an sich sind nicht „mist", die **Aggregation** ist es.
- Memory-Note „Split into batches of ~5 files" hat dieselbe Ursache schon empirisch gefunden — F-1 ist die strukturelle Antwort darauf.

## Offene Annahmen / Was ich NICHT verifiziert habe

- Den konkreten Crash-Test-Namen aus `Sequence.xml` — der `--blame`-Lauf wurde nach 30 min abgebrochen, weil testhost nicht crashte sondern wuchs. Empfehlung F-6 zuerst, dann ggf. ergänzendes Finding.
- Ob `dotnet test` mit `/maxcpucount:1` und `--parallel none` denselben Drift zeigt (nochmal mit `--diag`).
- Memory-Notes erwähnen frühere `MSB3021/MSB3027`-Locks (`testhost`, `Romulus.Wpf`) — das ist orthogonal zum jetzigen OOM-Drift, sollte aber bei der Splittung mitberücksichtigt werden (eigener Prozess pro Test-Subassembly = weniger Lock-Konflikte).

## Reihenfolge-Empfehlung für Implementierungs-Pass (nicht hier umgesetzt)

1. **F-4** (5 min, isoliert) — Build-Warning weg, dann ist `dotnet build`-Output ehrlich.
2. **F-6 + Diagnostik-Lauf** (1 Lauf) — Gewissheit ob Drift oder Einzeltest.
3. **F-2** (1–2 Tage) — `IClassFixture<ApiHostFixture>` in den 14 API-Files. Sofortiger 10x-Memory-Effekt.
4. **F-1** Projekt-Split (1 PR pro Sub-Projekt) — strukturelle Lösung. Nur danach lohnt sich F-5/F-7.
5. **F-3** Alibi-Tests streichen — am sichersten **nach** Split, sodass Schaden lokal bleibt.

⚠️ **Wichtig:** F-1 (Projekt-Split) berührt direkt den Punkt aus dem Audit-Moratorium (`AGENTS.md`): es ist KEIN neues Audit-Dokument, sondern ein konkreter Refactor-PR mit klarem Nutzen — passt also durch das Moratorium-Schlupfloch „konkrete Fix-PRs". Trotzdem vorher mit Maintainer abstimmen, weil `.csproj`-Topologie sich ändert. 
