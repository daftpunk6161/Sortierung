using System.Net;
using System.Text;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Writes a comprehensive HTML Benchmark Dashboard for CI artifacts (Phase D1).
/// Includes: summary KPIs, aggregate metrics M4-M16, per-system P/R/F1,
/// confusion heatmap, category confusion, and calibration info.
/// </summary>
internal static class BenchmarkHtmlReportWriter
{
    public static void Write(BenchmarkReport report, string path,
        IReadOnlyDictionary<string, SystemMetrics>? perSystemMetrics = null,
        IReadOnlyList<ConsoleConfusionPair>? confusionPairs = null,
        IReadOnlyList<CategoryConfusionEntry>? categoryConfusion = null,
        ConfidenceCalibrationResult? calibration = null,
        RegressionReport? regression = null)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var html = BuildHtml(report, perSystemMetrics, confusionPairs, categoryConfusion, calibration, regression);
        File.WriteAllText(path, html, Encoding.UTF8);
    }

    public static string BuildHtml(BenchmarkReport report,
        IReadOnlyDictionary<string, SystemMetrics>? perSystemMetrics = null,
        IReadOnlyList<ConsoleConfusionPair>? confusionPairs = null,
        IReadOnlyList<CategoryConfusionEntry>? categoryConfusion = null,
        ConfidenceCalibrationResult? calibration = null,
        RegressionReport? regression = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("<title>Romulus Benchmark Dashboard</title>");
        AppendStyles(sb);
        sb.AppendLine("</head><body>");

        sb.AppendLine("<h1>Romulus Benchmark Dashboard</h1>");
        sb.AppendLine($"<p class=\"muted\">Generated: {E(report.Timestamp.ToString("u"))} | GroundTruth: {E(report.GroundTruthVersion)} | Samples: {report.TotalSamples}</p>");

        // === Summary KPI Cards ===
        AppendKpiCards(sb, report);

        // === Aggregate Metrics Table (M4-M16) ===
        AppendAggregateMetrics(sb, report);

        // === Trend / Regression Delta ===
        if (regression is { HasBaseline: true })
            AppendRegressionDelta(sb, regression);

        // === Per-System Verdict Table ===
        AppendPerSystemVerdicts(sb, report);

        // === Per-System Precision/Recall/F1 ===
        if (perSystemMetrics is { Count: > 0 })
            AppendPerSystemPrecisionRecall(sb, perSystemMetrics);

        // === Console Confusion Pairs (M10) ===
        if (confusionPairs is { Count: > 0 })
            AppendConsoleConfusionPairs(sb, confusionPairs);

        // === Console Confusion Matrix ===
        if (report.ConfusionMatrix is { Count: > 0 })
            AppendConfusionMatrix(sb, report.ConfusionMatrix);

        // === Category Confusion (M9) ===
        if (categoryConfusion is { Count: > 0 })
            AppendCategoryConfusion(sb, categoryConfusion);

        // === Confidence Calibration (M16) ===
        if (calibration is not null)
            AppendCalibration(sb, calibration);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendStyles(StringBuilder sb)
    {
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', system-ui, sans-serif; margin: 24px; color: #1f2937; background: #f9fafb; }");
        sb.AppendLine("h1 { margin: 0 0 8px 0; color: #111827; }");
        sb.AppendLine("h2 { margin: 24px 0 12px 0; color: #374151; border-bottom: 2px solid #e5e7eb; padding-bottom: 6px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }");
        sb.AppendLine("th, td { border: 1px solid #d1d5db; padding: 6px 10px; text-align: left; font-size: 0.9em; }");
        sb.AppendLine("th { background: #f3f4f6; font-weight: 600; }");
        sb.AppendLine(".muted { color: #6b7280; font-size: 0.85em; }");
        sb.AppendLine(".kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; margin: 16px 0 24px 0; }");
        sb.AppendLine(".kpi-card { background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px; text-align: center; }");
        sb.AppendLine(".kpi-value { font-size: 1.8em; font-weight: 700; color: #111827; }");
        sb.AppendLine(".kpi-label { font-size: 0.8em; color: #6b7280; margin-top: 4px; }");
        sb.AppendLine(".ok { color: #059669; }");
        sb.AppendLine(".warn { color: #d97706; }");
        sb.AppendLine(".bad { color: #dc2626; }");
        sb.AppendLine(".heat-0 { background: #f0fdf4; } .heat-1 { background: #fef9c3; } .heat-2 { background: #fed7aa; } .heat-3 { background: #fecaca; }");
        sb.AppendLine(".delta-ok { color: #059669; } .delta-bad { color: #dc2626; }");
        sb.AppendLine("</style>");
    }

    private static void AppendKpiCards(StringBuilder sb, BenchmarkReport report)
    {
        sb.AppendLine("<div class=\"kpi-grid\">");
        AppendCard("Correct", report.Correct.ToString(), "ok");
        AppendCard("Acceptable", report.Acceptable.ToString(), "ok");
        AppendCard("Wrong", report.Wrong.ToString(), report.Wrong == 0 ? "ok" : "bad");
        AppendCard("Missed", report.Missed.ToString(), report.Missed == 0 ? "ok" : "warn");
        AppendCard("True Negative", report.TrueNegative.ToString(), "ok");
        AppendCard("Junk Classified", report.JunkClassified.ToString(), "ok");
        AppendCard("False Positive", report.FalsePositive.ToString(), report.FalsePositive == 0 ? "ok" : "bad");
        AppendCard("Wrong Match Rate", report.WrongMatchRate.ToString("P3"), report.WrongMatchRate <= 0.005 ? "ok" : "bad");
        sb.AppendLine("</div>");

        void AppendCard(string label, string value, string cls)
        {
            sb.AppendLine($"<div class=\"kpi-card\"><div class=\"kpi-value {cls}\">{E(value)}</div><div class=\"kpi-label\">{E(label)}</div></div>");
        }
    }

    private static void AppendAggregateMetrics(StringBuilder sb, BenchmarkReport report)
    {
        if (report.AggregateMetrics is not { Count: > 0 }) return;

        sb.AppendLine("<h2>Aggregate Metrics (M4\u2013M16)</h2>");
        sb.AppendLine("<table><thead><tr><th>Metric</th><th>Value</th><th>Target</th><th>Status</th></tr></thead><tbody>");

        var thresholds = new Dictionary<string, (double Target, string Op, string Label)>(StringComparer.OrdinalIgnoreCase)
        {
            ["wrongMatchRate"] =        (0.005,  "<=", "M4 Wrong Match Rate"),
            ["unknownRate"] =           (0.15,   "<=", "M5 Unknown Rate"),
            ["falseConfidenceRate"] =   (0.05,   "<=", "M6 False Confidence Rate"),
            ["unsafeSortRate"] =        (0.003,  "<=", "M7 Unsafe Sort Rate"),
            ["safeSortCoverage"] =      (0.80,   ">=", "M8 Safe Sort Coverage"),
            ["categoryConfusionRate"] = (0.05,   "<=", "M9 Category Confusion Rate"),
            ["gameAsJunkRate"] =        (0.001,  "<=", "M9a Game-as-Junk Rate"),
            ["biosAsGameRate"] =        (0.005,  "<=", "M9b BIOS-as-Game Rate"),
            ["maxConsoleConfusionRate"]= (0.02,   "<=", "M10 Max Console Confusion"),
            ["datExactMatchRate"] =     (0.90,   ">=", "M11 DAT Exact Match Rate"),
            ["ambiguousMatchRate"] =    (0.08,   "<=", "M13 Ambiguous Match Rate"),
            ["repairSafeRate"] =        (0.70,   ">=", "M14 Repair-Safe Rate"),
            ["categoryRecognitionRate"]=(0.85,   ">=", "M15 Category Recognition Rate"),
        };

        foreach (var (key, (target, op, label)) in thresholds)
        {
            if (!report.AggregateMetrics.TryGetValue(key, out var value)) continue;
            bool pass = op == "<=" ? value <= target : value >= target;
            var status = pass ? "<span class=\"ok\">\u2713</span>" : "<span class=\"bad\">\u2717</span>";
            sb.AppendLine($"<tr><td>{E(label)}</td><td>{value:P3}</td><td>{op} {target:P1}</td><td>{status}</td></tr>");
        }

        // Additional metrics without thresholds
        foreach (var kv in report.AggregateMetrics.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (thresholds.ContainsKey(kv.Key)) continue;
            sb.AppendLine($"<tr><td>{E(kv.Key)}</td><td>{kv.Value:F4}</td><td>\u2014</td><td class=\"muted\">\u2014</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
    }

    private static void AppendRegressionDelta(StringBuilder sb, RegressionReport regression)
    {
        sb.AppendLine("<h2>Trend vs. Baseline</h2>");
        sb.AppendLine("<table><thead><tr><th>Metric</th><th>Delta</th><th>Status</th></tr></thead><tbody>");

        AppendDeltaRow("Wrong Match Rate \u0394", regression.WrongMatchRateDelta, 0.001, lowerIsBetter: true);
        AppendDeltaRow("Unsafe Sort Rate \u0394", regression.UnsafeSortRateDelta, 0.001, lowerIsBetter: true);
        AppendDeltaRow("UNKNOWN\u2192WRONG Migration", regression.UnknownToWrongMigrationRate, 0.02, lowerIsBetter: true);

        if (regression.PerSystemRegressions.Count > 0)
        {
            sb.AppendLine($"<tr><td>System Regressions</td><td class=\"bad\">{E(string.Join(", ", regression.PerSystemRegressions))}</td><td><span class=\"bad\">\u2717</span></td></tr>");
        }
        else
        {
            sb.AppendLine("<tr><td>System Regressions</td><td>None</td><td><span class=\"ok\">\u2713</span></td></tr>");
        }

        sb.AppendLine("</tbody></table>");

        void AppendDeltaRow(string label, double delta, double threshold, bool lowerIsBetter)
        {
            bool ok = lowerIsBetter ? delta <= threshold : delta >= -threshold;
            string cls = ok ? "delta-ok" : "delta-bad";
            string prefix = delta > 0 ? "+" : "";
            string status = ok ? "<span class=\"ok\">\u2713</span>" : "<span class=\"bad\">\u2717</span>";
            sb.AppendLine($"<tr><td>{E(label)}</td><td class=\"{cls}\">{prefix}{delta:P3}</td><td>{status}</td></tr>");
        }
    }

    private static void AppendPerSystemVerdicts(StringBuilder sb, BenchmarkReport report)
    {
        sb.AppendLine("<h2>Per System \u2014 Verdicts</h2>");
        sb.AppendLine("<table><thead><tr><th>System</th><th>Correct</th><th>Acceptable</th><th>Wrong</th><th>Missed</th><th>TrueNeg</th><th>JunkCls</th><th>FalsePos</th></tr></thead><tbody>");
        foreach (var pair in report.PerSystem.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var s = pair.Value;
            sb.AppendLine($"<tr><td>{E(pair.Key)}</td><td>{s.Correct}</td><td>{s.Acceptable}</td><td>{s.Wrong}</td><td>{s.Missed}</td><td>{s.TrueNegative}</td><td>{s.JunkClassified}</td><td>{s.FalsePositive}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendPerSystemPrecisionRecall(StringBuilder sb, IReadOnlyDictionary<string, SystemMetrics> metrics)
    {
        sb.AppendLine("<h2>Per System \u2014 Precision / Recall / F1</h2>");
        sb.AppendLine("<table><thead><tr><th>System</th><th>Precision</th><th>Recall</th><th>F1</th><th>TP</th><th>FP</th><th>FN</th></tr></thead><tbody>");
        foreach (var (system, m) in metrics.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            string pCls = m.Precision >= 0.98 ? "ok" : m.Precision >= 0.90 ? "warn" : "bad";
            string rCls = m.Recall >= 0.90 ? "ok" : m.Recall >= 0.75 ? "warn" : "bad";
            string fCls = m.F1 >= 0.92 ? "ok" : m.F1 >= 0.80 ? "warn" : "bad";
            sb.AppendLine($"<tr><td>{E(system)}</td><td class=\"{pCls}\">{m.Precision:P1}</td><td class=\"{rCls}\">{m.Recall:P1}</td><td class=\"{fCls}\">{m.F1:P1}</td><td>{m.TruePositive}</td><td>{m.FalsePositive}</td><td>{m.FalseNegative}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendConsoleConfusionPairs(StringBuilder sb, IReadOnlyList<ConsoleConfusionPair> pairs)
    {
        sb.AppendLine("<h2>Console Confusion Pairs (M10)</h2>");
        sb.AppendLine("<table><thead><tr><th>Expected</th><th>Detected As</th><th>Rate</th><th>Count</th></tr></thead><tbody>");
        foreach (var p in pairs)
        {
            string cls = p.Rate > 0.05 ? "heat-3" : p.Rate > 0.02 ? "heat-2" : p.Rate > 0.01 ? "heat-1" : "heat-0";
            sb.AppendLine($"<tr class=\"{cls}\"><td>{E(p.SystemA)}</td><td>{E(p.SystemB)}</td><td>{p.Rate:P2}</td><td>{p.Count}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendConfusionMatrix(StringBuilder sb, IReadOnlyList<ConfusionEntry> confusion)
    {
        sb.AppendLine("<h2>Console Confusion Matrix (Top Mismatches)</h2>");
        sb.AppendLine("<table><thead><tr><th>Expected</th><th>Detected As</th><th>Count</th></tr></thead><tbody>");
        foreach (var entry in confusion.Take(30))
        {
            string cls = entry.Count >= 5 ? "heat-3" : entry.Count >= 3 ? "heat-2" : entry.Count >= 2 ? "heat-1" : "heat-0";
            sb.AppendLine($"<tr class=\"{cls}\"><td>{E(entry.ExpectedSystem)}</td><td>{E(entry.ActualSystem)}</td><td>{entry.Count}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendCategoryConfusion(StringBuilder sb, IReadOnlyList<CategoryConfusionEntry> entries)
    {
        sb.AppendLine("<h2>Category Confusion (M9)</h2>");
        sb.AppendLine("<table><thead><tr><th>Expected</th><th>Classified As</th><th>Count</th></tr></thead><tbody>");
        foreach (var entry in entries)
        {
            bool offDiag = !string.Equals(entry.ExpectedCategory, entry.ActualCategory, StringComparison.OrdinalIgnoreCase);
            string cls = offDiag ? (entry.Count >= 3 ? "heat-3" : "heat-1") : "";
            sb.AppendLine($"<tr class=\"{cls}\"><td>{E(entry.ExpectedCategory)}</td><td>{E(entry.ActualCategory)}</td><td>{entry.Count}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendCalibration(StringBuilder sb, ConfidenceCalibrationResult calibration)
    {
        sb.AppendLine("<h2>Confidence Calibration (M16)</h2>");
        sb.AppendLine($"<p>Expected Calibration Error (ECE): <strong>{calibration.ExpectedCalibrationError:P2}</strong></p>");
        sb.AppendLine("<table><thead><tr><th>Bucket</th><th>Samples</th><th>Correct</th><th>Accuracy</th><th>Error</th></tr></thead><tbody>");
        foreach (var b in calibration.Buckets)
        {
            string cls = b.Error > 0.2 ? "heat-3" : b.Error > 0.1 ? "heat-2" : b.Error > 0.05 ? "heat-1" : "heat-0";
            sb.AppendLine($"<tr class=\"{cls}\"><td>{b.LowerBound}\u2013{b.UpperBound}%</td><td>{b.SampleCount}</td><td>{b.CorrectCount}</td><td>{b.Accuracy:P1}</td><td>{b.Error:P2}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
