using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Pipeline;

/// <summary>
/// Conditional multi-step pipeline engine.
/// Mirrors PipelineEngine.ps1.
/// </summary>
public sealed class PipelineEngine
{
    /// <summary>
    /// Executes a single pipeline step with condition checking.
    /// </summary>
    public static void ExecuteStep(PipelineStep step, PipelineStepContext context)
    {
        // Check condition
        if (step.Condition != null && !step.Condition(context))
        {
            step.Status = "Skipped";
            return;
        }

        // DryRun mode
        if (context.Mode == "DryRun")
        {
            step.Status = "DryRun";
            return;
        }

        step.Status = "Running";
        try
        {
            step.Action(context);
            step.Status = "Completed";
        }
        catch (Exception ex)
        {
            step.Status = "Failed";
            step.Error = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// Executes a complete pipeline with OnError mode (stop/continue).
    /// </summary>
    public static PipelineResult Execute(PipelineDefinition pipeline, PipelineStepContext context)
    {
        var result = new PipelineResult
        {
            Name = pipeline.Name,
            TotalSteps = pipeline.Steps.Count,
            Steps = pipeline.Steps
        };

        bool previousSuccess = true;

        foreach (var step in pipeline.Steps)
        {
            context.PreviousSuccess = previousSuccess;

            try
            {
                ExecuteStep(step, context);

                switch (step.Status)
                {
                    case "Completed":
                        result.CompletedSteps++;
                        break;
                    case "DryRun":
                        result.CompletedSteps++;
                        break;
                    case "Skipped":
                        result.SkippedSteps++;
                        break;
                }
            }
            catch
            {
                result.FailedSteps++;
                previousSuccess = false;

                if (pipeline.OnError == "stop")
                {
                    // Mark remaining steps as skipped
                    var idx = pipeline.Steps.IndexOf(step);
                    for (int i = idx + 1; i < pipeline.Steps.Count; i++)
                        pipeline.Steps[i].Status = "Skipped";
                    result.SkippedSteps += pipeline.Steps.Count - idx - 1;
                    result.Status = "Failed";
                    return result;
                }
            }
        }

        result.Status = result.FailedSteps > 0 ? "PartialFailure" : "Completed";
        return result;
    }
}
