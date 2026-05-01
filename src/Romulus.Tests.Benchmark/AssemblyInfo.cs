using System.Runtime.CompilerServices;

// F-1 Project-Split (Wave-2): Romulus.Tests verbleibt nach dem Benchmark-
// Splitt eine Handvoll Tests (Phase2/Phase3/BlockD), die internal-Helper
// aus Romulus.Tests.Benchmark (DatasetExpander, StubGeneratorDispatch,
// GroundTruthLoader, ...) nutzen. Statt diese Helper auf "public" zu
// heben - was eine echte API-Aussage waere - wird die Sichtbarkeit
// gezielt auf das benachbarte Test-Assembly geoeffnet.
[assembly: InternalsVisibleTo("Romulus.Tests")]
