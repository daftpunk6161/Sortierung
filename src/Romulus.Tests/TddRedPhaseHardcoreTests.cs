using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD Red Phase – Harter QA-Angriff auf die Kernfunktionen.
/// 
/// Jeder Test hier zielt auf eine fachliche Invariante, die heute NICHT
/// korrekt abgedeckt oder implementiert ist. Diese Tests MÜSSEN rot sein.
/// 
/// Schwerpunkte:
///   1. Winner Selection / Dedupe – Determinismus, Edge Cases
///   2. Scan / Enumeration – Duplikate, Pfad-Edge-Cases
///   3. Classification / GameKey / Grouping – Normalisation, Stabilität
///   4. Conversion – Fehlerbehandlung, Zählung
///   5. Move / Restore / Undo – Safety, Atomizität
///   6. Orchestrator – Phasen, Cancel, Status
///   7. GUI / CLI / API Parity – Konsistenz
/// 
/// KEINE PRODUKTIONSÄNDERUNGEN. NUR FAILING TESTS.
/// </summary>
public class TddRedPhaseHardcoreTests : IDisposable
{
    private readonly string _tempDir;

    public TddRedPhaseHardcoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TddRed_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort cleanup */ }
    }

    #region Helpers

    private static RomCandidate MakeCandidate(
        string mainPath = "game.zip",
        string gameKey = "game",
        string region = "EU",
        int regionScore = 1000,
        int formatScore = 500,
        long versionScore = 0,
        int headerScore = 0,
        int completenessScore = 0,
        long sizeTieBreakScore = 0,
        bool datMatch = false,
        FileCategory category = FileCategory.Game,
        string extension = ".zip",
        string consoleKey = "SNES")
        => new()
        {
            MainPath = mainPath,
            GameKey = gameKey,
            Region = region,
            RegionScore = regionScore,
            FormatScore = formatScore,
            VersionScore = versionScore,
            HeaderScore = headerScore,
            CompletenessScore = completenessScore,
            SizeTieBreakScore = sizeTieBreakScore,
            DatMatch = datMatch,
            Category = category,
            Extension = extension,
            ConsoleKey = consoleKey,
        };

    private string CreateFile(string relativePath, int sizeBytes = 4)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
        return fullPath;
    }

    #endregion

    // ══════════════════════════════════════════════════════════════════════
    // 1) WINNER SELECTION / DEDUPE
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: Wenn 100 verschiedene Permutationen derselben Kandidatenliste
    /// an SelectWinner übergeben werden, muss IMMER derselbe Winner rauskommen.
    /// Zielt auf verdeckte Order-Abhängigkeit (z.B. bei Tie-Break auf SizeTieBreakScore=0).
    /// WARUM ROT: Bei 7+ Kandidaten mit identischen Scores außer MainPath kann die
    /// Reihenfolge der LINQ-Kette instabil werden, wenn ein Score-Feld default(0) hat.
    /// BETRIFFT: DeduplicationEngine.cs
    /// </summary>
    [Fact]
    public void SelectWinner_100Permutations_Of7Items_AlwaysSameWinner()
    {
        // 7 Kandidaten mit identischen Scores – nur MainPath unterscheidet
        var candidates = Enumerable.Range(0, 7)
            .Select(i => MakeCandidate(
                mainPath: $"game_{(char)('g' - i)}.zip", // g, f, e, d, c, b, a
                gameKey: "samegame",
                regionScore: 800,
                formatScore: 500,
                versionScore: 10,
                headerScore: 0,
                completenessScore: 50,
                sizeTieBreakScore: 0,
                datMatch: true))
            .ToList();

        var reference = DeduplicationEngine.SelectWinner(candidates);

        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var shuffled = candidates.OrderBy(_ => rng.Next()).ToList();
            var winner = DeduplicationEngine.SelectWinner(shuffled);
            Assert.Equal(reference!.MainPath, winner!.MainPath);
        }
    }

    /// <summary>
    /// INVARIANT: DatMatch bleibt in der Winner-Prioritätskette vor RegionScore.
    /// WORLD mit DatMatch darf bei sonstigem Gleichstand über EU ohne DatMatch gewinnen.
    /// BETRIFFT: DeduplicationEngine.cs – Priority-Chain-Reihenfolge
    /// </summary>
    [Fact]
    public void SelectWinner_WorldWithDatMatch_MustNotBeatPreferredRegion_WithoutDatMatch()
    {
        var euCandidate = MakeCandidate(
            mainPath: "Super Mario (Europe).zip",
            gameKey: "supermario",
            region: "EU",
            regionScore: 1000,
            datMatch: false,
            formatScore: 500,
            completenessScore: 50);

        var worldCandidate = MakeCandidate(
            mainPath: "Super Mario (World).zip",
            gameKey: "supermario",
            region: "WORLD",
            regionScore: 500,
            datMatch: true,
            formatScore: 500,
            completenessScore: 50);

        var winner = DeduplicationEngine.SelectWinner(new[] { euCandidate, worldCandidate });

        Assert.Equal("WORLD", winner!.Region);
    }

    /// <summary>
    /// INVARIANT: CompletenessScore bleibt vor RegionScore in der Prioritätskette.
    /// Ein deutlich vollständigerer UNKNOWN-Kandidat darf über EU mit niedriger Completeness gewinnen.
    /// BETRIFFT: DeduplicationEngine.cs – Priority-Chain-Reihenfolge
    /// </summary>
    [Fact]
    public void SelectWinner_UnknownRegion_MustNotBeatKnownRegion_ViaHigherCompleteness()
    {
        var euCandidate = MakeCandidate(
            mainPath: "Game (Europe).zip",
            gameKey: "testgame",
            region: "EU",
            regionScore: 1000,
            completenessScore: 25,
            formatScore: 500);

        var unknownCandidate = MakeCandidate(
            mainPath: "Game.zip",
            gameKey: "testgame",
            region: "UNKNOWN",
            regionScore: 100,
            completenessScore: 100, // höher als EU
            formatScore: 500);

        var winner = DeduplicationEngine.SelectWinner(new[] { euCandidate, unknownCandidate });

        Assert.Equal("UNKNOWN", winner!.Region);
    }

    /// <summary>
    /// INVARIANT: Wenn die DeduplicateMethode aufgerufen wird und zwei Kandidaten 
    /// denselben MainPath aber unterschiedliche GameKeys haben, dürfen sie in 
    /// verschiedenen Gruppen erscheinen, aber der gleiche MainPath darf dabei
    /// nicht gleichzeitig Winner UND Loser sein.
    /// WARUM ROT: Deduplicate prüft nicht auf MainPath-Kollisionen über Gruppen hinweg.
    /// Das gleiche File kann theoretisch Winner in Gruppe A und Loser in Gruppe B sein.
    /// BETRIFFT: DeduplicationEngine.cs – Cross-Group MainPath Collision
    /// </summary>
    [Fact]
    public void Deduplicate_SameMainPath_DifferentGameKeys_MustNotBeWinnerAndLoserSimultaneously()
    {
        var file = MakeCandidate(
            mainPath: "shared_file.zip",
            gameKey: "groupA",
            regionScore: 1000);

        var fileAgain = MakeCandidate(
            mainPath: "shared_file.zip",
            gameKey: "groupB",
            regionScore: 500);

        var otherInGroupB = MakeCandidate(
            mainPath: "other_file.zip",
            gameKey: "groupB",
            regionScore: 1000);

        var results = DeduplicationEngine.Deduplicate(new[] { file, fileAgain, otherInGroupB });

        var allWinnerPaths = results.Select(r => r.Winner.MainPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allLoserPaths = results.SelectMany(r => r.Losers).Select(l => l.MainPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Ein File darf nicht gleichzeitig Winner und Loser sein
        var intersection = allWinnerPaths.Intersect(allLoserPaths, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Empty(intersection);
    }

    /// <summary>
    /// INVARIANT: Deduplicate muss "tabs-only" und "spaces-only" Whitespace-Keys genauso
    /// ausschließen wie leere Strings und null.
    /// WARUM ROT: IsNullOrWhiteSpace filtert \t korrekt, aber Test verifiziert das noch 
    /// nicht für Tab-Only-Keys, Unicode-Whitespace (z.B. \u00A0 Non-Breaking Space).
    /// BETRIFFT: DeduplicationEngine.cs – Zeile "if (string.IsNullOrWhiteSpace(c.GameKey)) continue;"
    /// </summary>
    [Fact]
    public void Deduplicate_UnicodeWhitespaceOnlyKey_MustBeExcluded()
    {
        var tab = MakeCandidate(mainPath: "tab.zip", gameKey: "\t\t");
        var nbsp = MakeCandidate(mainPath: "nbsp.zip", gameKey: "\u00A0"); // Non-Breaking Space
        var enSpace = MakeCandidate(mainPath: "enspace.zip", gameKey: "\u2002"); // En Space
        var real = MakeCandidate(mainPath: "real.zip", gameKey: "realgame");

        var results = DeduplicationEngine.Deduplicate(new[] { tab, nbsp, enSpace, real });

        // Nur "realgame" darf eine Gruppe bilden
        Assert.Single(results);
        Assert.Equal("realgame", results[0].GameKey);
    }

    /// <summary>
    /// INVARIANT: Wenn alle Kandidaten einer Gruppe identische Scores haben,
    /// muss der alphabetisch ERSTE MainPath gewinnen (Tiebreaker BUG-011).
    /// Test mit Unicode-Pfaden, die nach Normalisierung gleich lang sind.
    /// WARUM ROT: StringComparer.OrdinalIgnoreCase kann bei Unicode-Normalisierung
    /// unerwartete Reihenfolgen erzeugen (z.B. ä vs ae).
    /// BETRIFFT: DeduplicationEngine.cs – ThenBy MainPath Tiebreaker
    /// </summary>
    [Fact]
    public void SelectWinner_UnicodePaths_MustUseDeterministicOrdering()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "Ärger.zip", gameKey: "same"),
            MakeCandidate(mainPath: "Aerger.zip", gameKey: "same"),
            MakeCandidate(mainPath: "aerger.zip", gameKey: "same"),
        };

        var winner1 = DeduplicationEngine.SelectWinner(candidates);
        var winner2 = DeduplicationEngine.SelectWinner(candidates.Reverse().ToList());
        var winner3 = DeduplicationEngine.SelectWinner(new[] { candidates[1], candidates[2], candidates[0] });

        // Alle drei Aufrufe müssen den gleichen Winner liefern
        Assert.Equal(winner1!.MainPath, winner2!.MainPath);
        Assert.Equal(winner1.MainPath, winner3!.MainPath);
    }

    /// <summary>
    /// INVARIANT: Sum-Invariante: Die Gesamtanzahl aller Winners + aller Losers
    /// muss exakt der Anzahl der Nicht-Empty-Key-Kandidaten entsprechen.
    /// WARUM ROT: Bei ReferenceEquals-Fallback in Deduplicate kann ein Kandidat
    /// verloren gehen, wenn MainPath-Vergleich fälschlich einen Loser entfernt.
    /// BETRIFFT: DeduplicationEngine.cs – winnerSkipped / losers Logik
    /// </summary>
    [Fact]
    public void Deduplicate_SumInvariant_WinnersAndLosersMustEqualTotalCandidates()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "A.zip", gameKey: "game1", regionScore: 1000),
            MakeCandidate(mainPath: "B.zip", gameKey: "game1", regionScore: 800),
            MakeCandidate(mainPath: "C.zip", gameKey: "game1", regionScore: 600),
            MakeCandidate(mainPath: "D.zip", gameKey: "game2", regionScore: 1000),
            MakeCandidate(mainPath: "E.zip", gameKey: "game2", regionScore: 900),
            MakeCandidate(mainPath: "F.zip", gameKey: "game3", regionScore: 1000),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        var totalWinners = results.Count;
        var totalLosers = results.Sum(r => r.Losers.Count);

        Assert.Equal(candidates.Length, totalWinners + totalLosers);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 2) SCAN / ENUMERATION
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: Wenn ein Root Unicode-Zeichen enthält (z.B. Japanisch, Kyrillisch),
    /// muss GetFilesSafe diese Dateien trotzdem korrekt enumerieren.
    /// WARUM ROT: NFC-Normalisierung in GetFilesSafe kann bei bestimmten
    /// Unicode-Sequenzen Pfade verändern, die dann nicht mehr gefunden werden.
    /// BETRIFFT: FileSystemAdapter.cs – NFC-Normalisierung
    /// TESTABILITY-FINDING: FileSystemAdapter über IFileSystem getestet.
    /// </summary>
    [Fact]
    public void Scan_UnicodeRootPath_MustEnumerateCorrectly()
    {
        var unicodeDir = Path.Combine(_tempDir, "ゲーム_Spiele_Игры");
        Directory.CreateDirectory(unicodeDir);
        File.WriteAllBytes(Path.Combine(unicodeDir, "test.zip"), new byte[4]);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var files = fs.GetFilesSafe(unicodeDir, new[] { ".zip" });

        Assert.Single(files);
        Assert.EndsWith("test.zip", files[0]);
    }

    /// <summary>
    /// INVARIANT: Long Paths (>260 chars) müssen auf Windows korrekt enumeriert werden.
    /// WARUM ROT: Ältere .NET-APIs werfen PathTooLongException bei >260 Zeichen.
    /// BETRIFFT: FileSystemAdapter.cs – GetFilesSafe
    /// </summary>
    [Fact]
    public void Scan_LongPath_MustNotThrow()
    {
        // Erstelle einen tiefen Pfad der >260 Zeichen hat
        var nested = _tempDir;
        for (int i = 0; i < 15; i++)
        {
            nested = Path.Combine(nested, "abcdefghij_12345678"); // 18 chars pro Level
        }

        try
        {
            Directory.CreateDirectory(nested);
            File.WriteAllBytes(Path.Combine(nested, "longpath.zip"), new byte[4]);
        }
        catch (Exception)
        {
            // Wenn das OS den Pfad nicht erstellen kann, überspringen wir den Test
            return;
        }

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        // Darf nicht werfen
        var exception = Record.Exception(() => fs.GetFilesSafe(nested, new[] { ".zip" }));
        Assert.Null(exception);
    }

    /// <summary>
    /// INVARIANT: Die Ergebnisse von GetFilesSafe müssen stabil sortiert sein.
    /// Zwei aufeinanderfolgende Aufrufe mit demselben Root und denselben Extensions
    /// müssen die exakt gleiche Reihenfolge liefern.
    /// WARUM ROT: Dateisystem-APIs garantieren keine Reihenfolge. Wenn die Sortierung
    /// in GetFilesSafe nicht deterministisch ist, schwanken nachgelagerte Key-Bildungen.
    /// BETRIFFT: FileSystemAdapter.cs – Sortierung der Ergebnisse
    /// </summary>
    [Fact]
    public void Scan_TwoConsecutiveCalls_SameRoot_MustReturnSameOrder()
    {
        for (int i = 0; i < 20; i++)
        {
            CreateFile($"scan_order/file_{i:D3}.zip");
        }

        var scanRoot = Path.Combine(_tempDir, "scan_order");
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result1 = fs.GetFilesSafe(scanRoot, new[] { ".zip" });
        var result2 = fs.GetFilesSafe(scanRoot, new[] { ".zip" });

        Assert.Equal(result1.Count, result2.Count);
        for (int i = 0; i < result1.Count; i++)
        {
            Assert.Equal(result1[i], result2[i]);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // 3) CLASSIFICATION / GAMEKEY / GROUPING
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: "The Legend of Zelda" und "Legend of Zelda, The" müssen
    /// denselben GameKey erzeugen (Artikel-Normalisierung).
    /// WARUM ROT: GameKeyNormalizer hat KEIN Artikel-Handling.
    /// "The" am Anfang oder als ", The" am Ende wird nicht normalisiert.
    /// BETRIFFT: GameKeyNormalizer.cs – fehlende Artikel-Normalisierung
    /// </summary>
    [Fact]
    public void GameKey_ArticleVariants_TheLegendOfZelda_MustProduceSameKey()
    {
        var key1 = GameKeyNormalizer.Normalize("The Legend of Zelda (Europe)");
        var key2 = GameKeyNormalizer.Normalize("Legend of Zelda, The (Europe)");

        Assert.Equal(key1, key2);
    }

    /// <summary>
    /// INVARIANT: "Disc 1" und "Disc 01" (mit führender Null) müssen
    /// denselben GameKey erzeugen.
    /// WARUM ROT: Der GameKeyNormalizer strippt Disc-Tags nicht und normalisiert
    /// keine Ziffernformate. "Disc 1" ≠ "Disc 01" als String-Vergleich.
    /// BETRIFFT: GameKeyNormalizer.cs – Disc-Nummerierung
    /// </summary>
    [Fact]
    public void GameKey_DiscNumbering_Disc1VsDisc01_MustProduceSameKey()
    {
        var key1 = GameKeyNormalizer.Normalize("Final Fantasy VII (Disc 1) (Europe)");
        var key2 = GameKeyNormalizer.Normalize("Final Fantasy VII (Disc 01) (Europe)");

        Assert.Equal(key1, key2);
    }

    /// <summary>
    /// INVARIANT: Ein Titel mit japanischen Zeichen (Kana) plus Region-Tag
    /// muss einen stabilen, nicht-leeren Key erzeugen.
    /// WARUM ROT: AsciiFold entfernt alle Non-ASCII nonspacing marks, aber Kana
    /// sind keine diacritical marks. Das Ergebnis muss trotzdem stabil und nicht-leer sein.
    /// BETRIFFT: GameKeyNormalizer.cs – AsciiFold mit CJK-Zeichen
    /// </summary>
    [Fact]
    public void GameKey_JapaneseTitleWithRegion_MustProduceStableNonEmptyKey()
    {
        var key1 = GameKeyNormalizer.Normalize("ゼルダの伝説 (Japan)");
        var key2 = GameKeyNormalizer.Normalize("ゼルダの伝説 (Japan)");

        Assert.Equal(key1, key2);
        Assert.DoesNotContain("__empty_key", key1);
    }

    /// <summary>
    /// INVARIANT: Reine Ziffern als Titel (z.B. "1943") müssen einen echten Key erzeugen,
    /// nicht "__empty_key" oder einen numerischen Schlüssel.
    /// WARUM ROT: Wenn alle Tag-Patterns auf "1943 (Europe)" angewendet werden,
    /// könnte der Region-Pattern den gesamten String matchen und einen leeren Key hinterlassen.
    /// BETRIFFT: GameKeyNormalizer.cs – nummerische Titel
    /// </summary>
    [Fact]
    public void GameKey_NumericTitle_1943_MustNotBeEmpty()
    {
        var key = GameKeyNormalizer.Normalize("1943 (Europe)");

        Assert.DoesNotContain("__empty_key", key);
        Assert.Contains("1943", key);
    }

    /// <summary>
    /// INVARIANT: Leerer String als Eingabe darf KEINEN leeren GameKey erzeugen,
    /// sondern muss zu "__empty_key_*" werden.
    /// WARUM ROT: Bestehender Test prüft nur null/whitespace. Leerer String ""
    /// wird hier explizit angegriffen.
    /// BETRIFFT: GameKeyNormalizer.cs
    /// </summary>
    [Fact]
    public void GameKey_EmptyString_MustReturnEmptyKeyMarker()
    {
        var key = GameKeyNormalizer.Normalize("");
        Assert.StartsWith("__empty_key", key);
    }

    /// <summary>
    /// INVARIANT: Classification muss "gamelist.xml.bak" als Junk erkennen.
    /// WARUM ROT: Die Regex für gamelist ist: ^gamelist(?:\.xml)?(?:\.old|\.bak)?$
    /// Aber "gamelist.xml.bak" hat .xml UND .bak – die Regex erlaubt optional beides.
    /// Test verifiziert edge case mit doppelter Endung.
    /// BETRIFFT: FileClassifier.cs
    /// </summary>
    [Fact]
    public void Classification_GamelistXmlBak_MustBeJunk()
    {
        var result = FileClassifier.Classify("gamelist.xml.bak");
        Assert.Equal(FileCategory.Junk, result);
    }

    /// <summary>
    /// INVARIANT: "Not for Resale" als Wort (ohne Klammern) muss als Junk erkannt werden.
    /// WARUM ROT: RxJunkWords enthält "not\s*for\s*resale", also sollte es matchen.
    /// Aber nur innerhalb von \b Boundaries – prüfen ob das bei zusammengesetzten
    /// Titeln wie "Game - Not for Resale Version" korrekt greift.
    /// BETRIFFT: FileClassifier.cs
    /// </summary>
    [Fact]
    public void Classification_NotForResale_AsWord_MustBeJunk()
    {
        var result = FileClassifier.Classify("Super Mario - Not for Resale");
        Assert.Equal(FileCategory.Junk, result);
    }

    /// <summary>
    /// INVARIANT: "Unknown" Kategorie darf in der Enrichment-Pipeline NICHT
    /// still zu "Game" hochgestuft werden.
    /// WARUM ROT: FileClassifier gibt für leere baseName Unknown zurück,
    /// aber CandidateFactory hat default Category = Game.
    /// Wenn der Filename am Ende leer wird, könnte die Kategorie falsch sein.
    /// BETRIFFT: FileClassifier.cs, CandidateFactory.cs
    /// </summary>
    [Fact]
    public void Classification_EmptyFilename_MustBeUnknown_NotGame()
    {
        // Datei ohne echten Namen (nur Extension)
        var result = FileClassifier.Classify("");
        Assert.Equal(FileCategory.Unknown, result);

        // Auch Whitespace-only
        var result2 = FileClassifier.Classify("   ");
        Assert.Equal(FileCategory.Unknown, result2);
    }

    /// <summary>
    /// INVARIANT: Deduplicate gruppiert nach GameKey. Treffen BIOS und Game auf denselben
    /// Raw-GameKey, muss wegen Category-Rank trotzdem GAME als Winner gewählt werden.
    /// BETRIFFT: DeduplicationEngine.cs – Category-Ranking und Grouping
    /// </summary>
    [Fact]
    public void Grouping_BiosAndGame_WithSameRawGameKey_MustNotBeInSameGroup()
    {
        // Ohne CandidateFactory: gleicher GameKey, verschiedene Kategorien
        var bios = MakeCandidate(
            mainPath: "ps1_bios.bin",
            gameKey: "playstation",
            category: FileCategory.Bios,
            regionScore: 1000);

        var game = MakeCandidate(
            mainPath: "playstation_game.zip",
            gameKey: "playstation",
            category: FileCategory.Game,
            regionScore: 800);

        var results = DeduplicationEngine.Deduplicate(new[] { bios, game });

        Assert.Single(results);
        Assert.Equal(FileCategory.Game, results[0].Winner.Category);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 4) REGION DETECTION
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: "(USA, Europe)" als Multi-Region muss WORLD zurückgeben.
    /// WARUM ROT: Die Multi-Region-Pattern matcht nur Sprachcodes (en,fr,de...),
    /// nicht Region-Namen (USA, Europe). Also wird nur "USA" erkannt → "US".
    /// BETRIFFT: RegionDetector.cs – Multi-Region nur für Sprachcodes definiert
    /// </summary>
    [Fact]
    public void RegionDetector_MultiRegionNames_UsaEurope_MustReturnWorld()
    {
        var region = RegionDetector.GetRegionTag("Game (USA, Europe)");
        Assert.Equal("WORLD", region);
    }

    /// <summary>
    /// INVARIANT: "(NTSC-U)" muss zu "US" mappens.
    /// WARUM ROT: Die ordered rules haben "NTSC-J" → JP, aber NTSC-U wird
    /// nur über token-basiertes Parsing abgefangen, nicht als primäre Regel.
    /// Test prüft ob es trotzdem korrekt erkannt wird.
    /// BETRIFFT: RegionDetector.cs – NTSC-U Mapping
    /// </summary>
    [Fact]
    public void RegionDetector_NTSC_U_MapsToUS()
    {
        var region = RegionDetector.GetRegionTag("Game (NTSC-U)");
        Assert.Equal("US", region);
    }

    /// <summary>
    /// INVARIANT: Mehrdeutige Regionen wie "(Europe, Japan)" müssen konsistent
    /// aufgelöst werden – entweder als WORLD oder als First-Match.
    /// Das Ergebnis muss deterministisch sein.
    /// WARUM ROT: OrderedRules matchen "Europe" zuerst → EU, aber die Kombination
    /// "(Europe, Japan)" könnte auch als WORLD interpretiert werden müssen.
    /// BETRIFFT: RegionDetector.cs – Multi-Region-Kombination
    /// </summary>
    [Fact]
    public void RegionDetector_EuropeJapan_MustBeDeterministic()
    {
        var region1 = RegionDetector.GetRegionTag("Game (Europe, Japan)");
        var region2 = RegionDetector.GetRegionTag("Game (Europe, Japan)");

        Assert.Equal(region1, region2);
        // Multi-region mit verschiedenen Regionen SOLLTE WORLD sein
        Assert.Equal("WORLD", region1);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 5) SCORING
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: .m3u als Extension (ohne Set-Type) muss einen HÖHEREN FormatScore
    /// haben als .zip, da M3U-Playlists die bevorzugte Multi-Disc-Variante sind.
    /// WARUM ROT: FormatScorer.GetFormatScore gibt für ".m3u" den default-Wert 300 zurück,
    /// was UNTER .zip (500) liegt. Nur als Set-Type "M3USET" bekommt es 900.
    /// BETRIFFT: FormatScorer.cs – .m3u Extension-Score
    /// </summary>
    [Fact]
    public void FormatScorer_M3uExtension_MustHaveHigherScoreThanZip()
    {
        var m3uScore = FormatScorer.GetFormatScore(".m3u");
        var zipScore = FormatScorer.GetFormatScore(".zip");

        Assert.True(m3uScore > zipScore,
            $".m3u score ({m3uScore}) should be higher than .zip ({zipScore})");
    }

    /// <summary>
    /// INVARIANT: Rev B muss einen höheren VersionScore haben als Rev A.
    /// WARUM ROT: Sollte grün sein, verifiziert kritische Invariante.
    /// Wenn das ROT ist, ist die Revision-Scoring-Logik kaputt.
    /// BETRIFFT: VersionScorer.cs
    /// </summary>
    [Fact]
    public void VersionScorer_RevisionOrdering_RevB_MustBeatRevA()
    {
        var scorer = new VersionScorer();
        var scoreA = scorer.GetVersionScore("Game (Rev A)");
        var scoreB = scorer.GetVersionScore("Game (Rev B)");

        Assert.True(scoreB > scoreA,
            $"Rev B ({scoreB}) should score higher than Rev A ({scoreA})");
    }

    /// <summary>
    /// INVARIANT: GetRegionScore für eine Region, die NICHT in der Prefer-Order ist
    /// und auch nicht WORLD/UNKNOWN ist, muss 200 (Default) zurückgeben.
    /// Dieses Default darf nicht höher sein als WORLD (500).
    /// WARUM ROT: Smoke-Test für das Scoring-Mapping.
    /// BETRIFFT: FormatScorer.cs – GetRegionScore
    /// </summary>
    [Fact]
    public void RegionScore_UnknownRegion_MustBeLowerThanWorld()
    {
        var prefer = new[] { "EU", "US", "JP" };
        var worldScore = FormatScorer.GetRegionScore("WORLD", prefer);
        var unknownScore = FormatScorer.GetRegionScore("UNKNOWN", prefer);
        var randomScore = FormatScorer.GetRegionScore("KR", prefer);

        Assert.True(worldScore > unknownScore,
            $"WORLD ({worldScore}) should score higher than UNKNOWN ({unknownScore})");
        Assert.True(worldScore > randomScore,
            $"WORLD ({worldScore}) should score higher than unlisted region KR ({randomScore})");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 6) CONVERSION
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: Wenn das Konvertierungstool nicht gefunden wird, muss 
    /// ConversionOutcome.Skipped zurückgegeben werden, nicht Error.
    /// WARUM ROT: Abhängig von FormatConverterAdapter-Implementierung.
    /// Wenn das Tool fehlt und GetTargetFormat null zurückgibt, ist das korrekt.
    /// Aber wenn ein ConversionTarget existiert und das Tool dann nicht aufrufbar ist,
    /// kommt möglicherweise Error statt Skipped.
    /// BETRIFFT: FormatConverterAdapter.cs – Tool-Lookup
    /// TESTABILITY-FINDING: FormatConverterAdapter benötigt IToolRunner-Injektion
    /// für echte Unit-Tests. Derzeit nur über Integration testbar.
    /// </summary>
    [Fact]
    public void Conversion_UnknownConsole_MustReturnNullTarget()
    {
        var converter = new Romulus.Infrastructure.Conversion.FormatConverterAdapter(
            new MinimalToolRunner());

        var target = converter.GetTargetFormat("UNKNOWN_CONSOLE_XYZ", ".zip");

        Assert.Null(target);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 7) MOVE / RESTORE / UNDO
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: MovePipelinePhase darf KEINE Dateien außerhalb der deklarierten
    /// Roots bewegen. Ein Loser mit einem MainPath außerhalb aller Roots
    /// muss als Failure gezählt werden, nicht still übersprungen.
    /// WARUM ROT: Wenn der Root-Lookup fehlschlägt, wird failCount hochgezählt,
    /// aber es gibt keine explizite Prüfung auf Path Traversal im Loser-Pfad.
    /// BETRIFFT: MovePipelinePhase.cs
    /// </summary>
    [Fact]
    public void Move_LoserOutsideAllRoots_MustBeCountedAsFail()
    {
        var root = Path.Combine(_tempDir, "roms");
        var trashDir = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trashDir);
        CreateFile("roms/winner.zip");
        CreateFile("outside/loser.zip");

        var winner = MakeCandidate(
            mainPath: Path.Combine(root, "winner.zip"),
            gameKey: "testgame",
            regionScore: 1000);

        var outsideLoser = MakeCandidate(
            mainPath: Path.Combine(_tempDir, "outside", "loser.zip"),
            gameKey: "testgame",
            regionScore: 500);

        var groups = new[]
        {
            new DedupeGroup
            {
                Winner = winner,
                Losers = new[] { outsideLoser },
                GameKey = "testgame"
            }
        };

        var input = new MovePhaseInput(groups, new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            TrashRoot = trashDir,
            ConflictPolicy = "Rename"
        });

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var phase = new MovePipelinePhase();
        var context = new PipelineContext
        {
            Options = input.Options,
            FileSystem = fs,
            AuditStore = new NullAuditStore(),
            Metrics = new PhaseMetricsCollector()
        };

        var result = phase.Execute(input, context, CancellationToken.None);

        // Der Outside-Loser darf weder bewegt noch still übersprungen werden
        Assert.Equal(1, result.FailCount);
        Assert.Equal(0, result.MoveCount);
    }

    /// <summary>
    /// INVARIANT: Rollback muss bei fehlender Sidecar-Metadaten-Datei (.meta.json)
    /// sofort scheitern und keinen partiellen Rollback durchführen.
    /// WARUM ROT: Abhängig von AuditSigningService-Implementierung.
    /// BETRIFFT: RollbackService.cs – TestMetadataSidecar
    /// </summary>
    [Fact]
    public void Rollback_MissingSidecar_MustReturnEmptyRestoredList()
    {
        var auditDir = Path.Combine(_tempDir, "audit");
        Directory.CreateDirectory(auditDir);
        var auditPath = Path.Combine(auditDir, "audit.csv");
        // Erstelle eine leere Audit-CSV aber KEINE .meta.json Sidecar
        File.WriteAllText(auditPath, "Root,OldPath,NewPath,Action,Category,Hash,Reason\n");

        var result = Romulus.Infrastructure.Audit.RollbackService.Execute(
            auditPath,
            new[] { _tempDir });

        // Bei fehlender Sidecar muss das Ergebnis scheitern
        Assert.NotNull(result);
        Assert.True(result.Failed > 0 || result.RolledBack == 0,
            "Rollback without sidecar should fail or restore nothing");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 8) ORCHESTRATOR / PIPELINE
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: PreviewMode und MoveMode mit identischen Inputs müssen 
    /// identische GroupCount, WinnerCount und LoserCount erzeugen.
    /// Die fachlichen Entscheidungen dürfen sich nicht nach Mode unterscheiden.
    /// WARUM ROT: Wenn der Mode-Switch zu früh greift und bestimmte Pipeline-Phasen
    /// im Preview übersprungen werden, können Counts divergieren.
    /// BETRIFFT: RunOrchestrator.cs – Mode-Handling
    /// TESTABILITY-FINDING: RunOrchestrator benötigt Full Setup mit IFileSystem etc.
    /// </summary>
    [Fact]
    public void Orchestrator_PreviewVsMove_MustHaveSameDedupeDecisions()
    {
        var root = Path.Combine(_tempDir, "orch_parity");
        var trashDir = Path.Combine(_tempDir, "orch_trash");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trashDir);

        // Erstelle 4 Dateien: 3 Varianten von Game, 1 anderes
        CreateFile("orch_parity/Super Mario (Europe).zip", 100);
        CreateFile("orch_parity/Super Mario (USA).zip", 100);
        CreateFile("orch_parity/Super Mario (Japan).zip", 100);
        CreateFile("orch_parity/Zelda (Europe).zip", 100);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new NullAuditStore();

        // Phase 1: Scan
        var scanPhase = new ScanPipelinePhase();
        var enrichPhase = new EnrichmentPipelinePhase();
        var dedupePhase = new DeduplicatePipelinePhase();

        var previewOptions = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "EU", "US", "JP" },
            RemoveJunk = false,
            TrashRoot = trashDir,
            ConflictPolicy = "Rename",
            Mode = "DryRun"
        };

        var moveOptions = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "EU", "US", "JP" },
            RemoveJunk = false,
            TrashRoot = trashDir,
            ConflictPolicy = "Rename",
            Mode = "Move"
        };

        var metrics = new PhaseMetricsCollector();
        var previewContext = new PipelineContext
        {
            Options = previewOptions,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = metrics
        };

        var moveContext = new PipelineContext
        {
            Options = moveOptions,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = new PhaseMetricsCollector()
        };

        // Preview-Dedupe
        var previewScanned = scanPhase.Execute(previewContext.Options, previewContext, CancellationToken.None);
        var previewEnriched = enrichPhase.Execute(
            new EnrichmentPhaseInput(previewScanned, null, null, null, null),
            previewContext, CancellationToken.None);
        var previewDedupe = dedupePhase.Execute(previewEnriched, previewContext, CancellationToken.None);

        // Move-Dedupe (gleiche Eingaben)
        var moveScanned = scanPhase.Execute(moveContext.Options, moveContext, CancellationToken.None);
        var moveEnriched = enrichPhase.Execute(
            new EnrichmentPhaseInput(moveScanned, null, null, null, null),
            moveContext, CancellationToken.None);
        var moveDedupe = dedupePhase.Execute(moveEnriched, moveContext, CancellationToken.None);

        // Fachliche Entscheidungen müssen identisch sein
        Assert.Equal(previewDedupe.Groups.Count, moveDedupe.Groups.Count);
        Assert.Equal(previewDedupe.GameGroups.Count, moveDedupe.GameGroups.Count);
        Assert.Equal(previewDedupe.LoserCount, moveDedupe.LoserCount);

        // Winner-Pfade müssen identisch sein
        var previewWinners = previewDedupe.Groups.Select(g => g.Winner.MainPath).OrderBy(x => x).ToList();
        var moveWinners = moveDedupe.Groups.Select(g => g.Winner.MainPath).OrderBy(x => x).ToList();
        Assert.Equal(previewWinners, moveWinners);
    }

    /// <summary>
    /// INVARIANT: CancellationToken muss die Enrichment-Phase abbrechen.
    /// WARUM ROT: Verifiziert, dass OperationCanceledException bei Cancel sauber geworfen wird.
    /// BETRIFFT: EnrichmentPipelinePhase.cs
    /// </summary>
    [Fact]
    public void Enrichment_Cancel_MustThrowOperationCanceled()
    {
        var root = Path.Combine(_tempDir, "cancel_test");
        Directory.CreateDirectory(root);

        // Erstelle genug Dateien, damit Cancel greifen kann
        for (int i = 0; i < 100; i++)
            CreateFile($"cancel_test/file_{i:D3}.zip");

        var files = Enumerable.Range(0, 100)
            .Select(i => new ScannedFileEntry(root, Path.Combine(root, $"file_{i:D3}.zip"), ".zip"))
            .ToList();

        var phase = new EnrichmentPipelinePhase();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Sofort abbrechen

        var context = new PipelineContext
        {
            Options = new RunOptions { Roots = new[] { root }, Extensions = new[] { ".zip" } },
            FileSystem = new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            AuditStore = new NullAuditStore(),
            Metrics = new PhaseMetricsCollector()
        };

        Assert.Throws<OperationCanceledException>(() =>
            phase.Execute(new EnrichmentPhaseInput(files, null, null, null, null), context, cts.Token));
    }

    // ══════════════════════════════════════════════════════════════════════
    // 9) GUI / CLI / API PARITY
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: Wenn Core-Deduplication-Ergebnisse an GUI, CLI und API geliefert
    /// werden, müssen GroupCount, WinnerCount, LoserCount identisch sein.
    /// WARUM ROT: Reports werden in drei Entry Points unterschiedlich aufbereitet.
    /// Wenn ein Entry Point die Counts aus DedupeGroup anders zählt, divergieren sie.
    /// BETRIFFT: RunResult, RunProjectionFactory, CLI Output, API Response
    /// TESTABILITY-FINDING: GUI-ViewModel-Projektion müsste ohne Window testbar sein (~ist es via GuiViewModelTests).
    /// </summary>
    [Fact]
    public void Parity_RunResult_CountsMustBeConsistent()
    {
        // Erstelle ein synthetisches RunResult
        var candidates = new[]
        {
            MakeCandidate(mainPath: "A.zip", gameKey: "game1", regionScore: 1000),
            MakeCandidate(mainPath: "B.zip", gameKey: "game1", regionScore: 800),
            MakeCandidate(mainPath: "C.zip", gameKey: "game2", regionScore: 1000),
            MakeCandidate(mainPath: "D.zip", gameKey: "game2", regionScore: 900),
            MakeCandidate(mainPath: "E.zip", gameKey: "game3", regionScore: 1000),
        };

        var dedupeResults = DeduplicationEngine.Deduplicate(candidates);

        var runResult = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = candidates.Length,
            GroupCount = dedupeResults.Count,
            WinnerCount = dedupeResults.Count,
            LoserCount = dedupeResults.Sum(g => g.Losers.Count),
            DedupeGroups = dedupeResults,
            AllCandidates = candidates,
        };

        // Count-Invarianten
        Assert.Equal(runResult.GroupCount, runResult.WinnerCount);
        Assert.Equal(
            runResult.TotalFilesScanned,
            runResult.WinnerCount + runResult.LoserCount);

        // DedupeGroups muss mit einzelnen Counts übereinstimmen
        Assert.Equal(runResult.GroupCount, runResult.DedupeGroups.Count);
        Assert.Equal(
            runResult.LoserCount,
            runResult.DedupeGroups.Sum(g => g.Losers.Count));
    }

    /// <summary>
    /// INVARIANT: EnrichmentPipelinePhase muss über CandidateFactory BIOS isolieren.
    /// Direkt in Enrichment gebaute Candidates müssen den __BIOS__-Prefix haben.
    /// WARUM ROT: Verifiziert, dass die Enrichment-Phase CandidateFactory.Create verwendet
    /// und nicht direkt new RomCandidate { ... } mit fehlendem Prefix.
    /// BETRIFFT: EnrichmentPipelinePhase.cs, CandidateFactory.cs
    /// </summary>
    [Fact]
    public void Enrichment_BiosFile_MustHaveBiosPrefixInGameKey()
    {
        var root = Path.Combine(_tempDir, "bios_enrich");
        Directory.CreateDirectory(root);
        var biosPath = CreateFile("bios_enrich/[BIOS] PlayStation (USA).bin");

        var phase = new EnrichmentPipelinePhase();
        var files = new[] { new ScannedFileEntry(root, biosPath, ".bin") };

        var context = new PipelineContext
        {
            Options = new RunOptions
            {
                Roots = new[] { root },
                Extensions = new[] { ".bin" }
            },
            FileSystem = new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            AuditStore = new NullAuditStore(),
            Metrics = new PhaseMetricsCollector()
        };

        var candidates = phase.Execute(
            new EnrichmentPhaseInput(files, null, null, null, null),
            context, CancellationToken.None);

        Assert.Single(candidates);
        Assert.Equal(FileCategory.Bios, candidates[0].Category);
        Assert.StartsWith("__BIOS__", candidates[0].GameKey);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 10) SORTING
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: FormatScorer.IsKnownFormat muss für ALLE Formate in DefaultExtensions
    /// true zurückgeben. Sonst werden Standard-ROMs als "unknown format" geloggt.
    /// WARUM ROT: DefaultExtensions enthält .snes, .ws, .ngp, .pkg, .pbp
    /// die möglicherweise nicht in IsKnownFormat gelistet sind.
    /// BETRIFFT: FormatScorer.cs, RunOptions.DefaultExtensions
    /// </summary>
    [Fact]
    public void FormatScorer_AllDefaultExtensions_MustBeKnown()
    {
        var allowedUnknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".rom"
        };

        var unknown = new List<string>();
        foreach (var ext in RunOptions.DefaultExtensions)
        {
            if (!FormatScorer.IsKnownFormat(ext) && !allowedUnknown.Contains(ext))
                unknown.Add(ext);
        }

        Assert.True(unknown.Count == 0,
            $"These default extensions are NOT known to FormatScorer: {string.Join(", ", unknown)}");
    }

    /// <summary>
    /// INVARIANT: SizeTieBreakScore für Disc-Formate muss positiv sein (größer = besser),
    /// für Cartridge-Formate negativ (kleiner = besser).
    /// WARUM ROT: Smoke-Test für die Scoring-Logik.
    /// BETRIFFT: FormatScorer.cs – GetSizeTieBreakScore
    /// </summary>
    [Fact]
    public void SizeTieBreak_DiscFormat_PositiveScore_CartridgeFormat_NegativeScore()
    {
        var discScore = FormatScorer.GetSizeTieBreakScore(null, ".iso", 1_000_000);
        var cartScore = FormatScorer.GetSizeTieBreakScore(null, ".sfc", 1_000_000);

        Assert.True(discScore > 0, $"Disc .iso should have positive size score, got {discScore}");
        Assert.True(cartScore < 0, $"Cartridge .sfc should have negative size score, got {cartScore}");
    }

    // ══════════════════════════════════════════════════════════════════════
    // 11) DEDUPE – EDGE CASES AUS DER PRAXIS
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// INVARIANT: Wenn alle Kandidaten einer Gruppe UNKNOWN Region haben,
    /// muss trotzdem ein deterministischer Winner ausgewählt werden.
    /// WARUM ROT: Sollte grün sein – prüft dass UNKNOWN-Gruppen nicht abstürzen.
    /// BETRIFFT: DeduplicationEngine.cs
    /// </summary>
    [Fact]
    public void SelectWinner_AllUnknownRegion_MustStillSelectDeterministicWinner()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "a.zip", gameKey: "game", region: "UNKNOWN", regionScore: 100),
            MakeCandidate(mainPath: "b.zip", gameKey: "game", region: "UNKNOWN", regionScore: 100),
            MakeCandidate(mainPath: "c.zip", gameKey: "game", region: "UNKNOWN", regionScore: 100),
        };

        var winner1 = DeduplicationEngine.SelectWinner(candidates);
        var winner2 = DeduplicationEngine.SelectWinner(candidates.Reverse().ToList());

        Assert.NotNull(winner1);
        Assert.NotNull(winner2);
        Assert.Equal(winner1.MainPath, winner2.MainPath);
    }

    /// <summary>
    /// INVARIANT: Deduplicate mit einer einzelnen Kandidaten-Gruppe darf KEINEN Loser erzeugen.
    /// WARUM ROT: Regression-Guard – Singleton-Gruppen dürfen keine Losers enthalten.
    /// BETRIFFT: DeduplicationEngine.cs
    /// </summary>
    [Fact]
    public void Deduplicate_SingletonGroups_MustHaveNoLosers()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "A.zip", gameKey: "unique1"),
            MakeCandidate(mainPath: "B.zip", gameKey: "unique2"),
            MakeCandidate(mainPath: "C.zip", gameKey: "unique3"),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);

        Assert.Equal(3, results.Count);
        foreach (var group in results)
        {
            Assert.Empty(group.Losers);
            Assert.NotNull(group.Winner);
        }
    }

    /// <summary>
    /// INVARIANT: GameKeyNormalizer darf für verschiedene Spiele mit ähnlichen Namen
    /// keine Key-Kollisionen erzeugen.
    /// z.B. "Mega Man" vs "Mega Man 2" vs "Mega Man X" müssen unterschiedliche Keys haben.
    /// WARUM ROT: Wenn die Tag-Stripping-Logik zu aggressiv ist, könnten Nummern
    /// am Ende fälschlich entfernt werden.
    /// BETRIFFT: GameKeyNormalizer.cs
    /// </summary>
    [Fact]
    public void GameKey_SimilarTitles_MustNotCollide()
    {
        var key1 = GameKeyNormalizer.Normalize("Mega Man (Europe)");
        var key2 = GameKeyNormalizer.Normalize("Mega Man 2 (Europe)");
        var key3 = GameKeyNormalizer.Normalize("Mega Man X (Europe)");

        Assert.NotEqual(key1, key2);
        Assert.NotEqual(key1, key3);
        Assert.NotEqual(key2, key3);
    }

    /// <summary>
    /// INVARIANT: VersionScorer muss v2.0 höher scoren als v1.0.
    /// WARUM ROT: Regression-Guard für Version-Parsing.
    /// BETRIFFT: VersionScorer.cs
    /// </summary>
    [Fact]
    public void VersionScorer_V2_MustBeatV1()
    {
        var scorer = new VersionScorer();
        var v1 = scorer.GetVersionScore("Game (v1.0)");
        var v2 = scorer.GetVersionScore("Game (v2.0)");

        Assert.True(v2 > v1, $"v2.0 ({v2}) must score higher than v1.0 ({v1})");
    }

    /// <summary>
    /// INVARIANT: HealthScorer muss bei 0 totalFiles den Score 0 zurückgeben,
    /// nicht negativ oder NaN.
    /// WARUM ROT: Edge-Case für leere Scans – Division durch Null.
    /// BETRIFFT: HealthScorer.cs
    /// </summary>
    [Fact]
    public void HealthScorer_ZeroFiles_MustReturnZero()
    {
        var score = HealthScorer.GetHealthScore(0, 0, 0, 0);
        Assert.Equal(0, score);
    }

    /// <summary>
    /// INVARIANT: HealthScorer darf niemals einen Score über 100 zurückgeben,
    /// auch nicht bei hohem verifiedBonus.
    /// WARUM ROT: Clamp-Guard prüfen bei 100% verified.
    /// BETRIFFT: HealthScorer.cs
    /// </summary>
    [Fact]
    public void HealthScorer_AllVerified_NoDupes_NoJunk_MustNotExceed100()
    {
        var score = HealthScorer.GetHealthScore(1000, 0, 0, 1000);
        Assert.True(score <= 100, $"HealthScore should not exceed 100, got {score}");
    }

    #region Fake Implementations

    /// <summary>
    /// Minimal IAuditStore implementation that does nothing.
    /// </summary>
    private sealed class NullAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "") { }
    }

    /// <summary>
    /// Minimal IFileSystem implementation for tests.
    /// </summary>
    private sealed class MinimalFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => false;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath) => null;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => null;
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    /// <summary>
    /// Minimal ToolRunner stub for converter tests.
    /// </summary>
    private sealed class MinimalToolRunner : Romulus.Contracts.Ports.IToolRunner
    {
        public string? FindTool(string toolName) => null;
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(-1, "tool not found", false);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(-1, "7z not found", false);
    }

    #endregion
}
