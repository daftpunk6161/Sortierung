using Xunit;

namespace Romulus.Tests.Benchmark;

[CollectionDefinition("BenchmarkGroundTruth", DisableParallelization = true)]
public sealed class BenchmarkGroundTruthCollectionDefinition
{
}

[CollectionDefinition("BenchmarkEvaluation", DisableParallelization = true)]
public sealed class BenchmarkEvaluationCollectionDefinition
{
}
