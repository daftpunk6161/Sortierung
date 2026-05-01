using System.Runtime.CompilerServices;

// F-1 Project-Split Phase C: Romulus.Tests.Wpf braucht ApiTestFactory
// und OpenApiTestHelper aus Romulus.Tests.Api (KpiChannelParityBacklog,
// AuditCompliance, AuditFindingsRegression, ConversionReportParity,
// Block1_ReleaseBlocker).
[assembly: InternalsVisibleTo("Romulus.Tests.Wpf")]
