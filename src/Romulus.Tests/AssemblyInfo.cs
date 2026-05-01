using System.Runtime.CompilerServices;

// F-1 Project-Split (Wave-2): Romulus.Tests.Benchmark referenziert
// internal-Helper aus Romulus.Tests (TestClassificationIo). Dieser
// InternalsVisibleTo-Eintrag haelt die Helper-Sichtbarkeit auf das
// Test-Subsystem beschraenkt, ohne sie auf "public" anzuheben (was
// echte Konsumenten irrefuehren wuerde).
[assembly: InternalsVisibleTo("Romulus.Tests.Benchmark")]

// F-1 Project-Split Phase B: Romulus.Tests.Wpf erbt Cross-Ref auf
// Romulus.Tests fuer Helper wie StubDialogService, OpenApiTestHelper
// und ApiTestFactory.
[assembly: InternalsVisibleTo("Romulus.Tests.Wpf")]
