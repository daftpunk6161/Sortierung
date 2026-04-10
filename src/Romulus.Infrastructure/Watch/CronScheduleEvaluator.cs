namespace Romulus.Infrastructure.Watch;

/// <summary>
/// Shared cron matcher used by GUI, CLI, and API schedule automation.
/// Supports five-field expressions: minute hour day month day-of-week.
/// </summary>
public static class CronScheduleEvaluator
{
    public static bool TestCronMatch(string cronExpression, DateTime dateTime)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        var fields = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            return false;

        return CronFieldMatch(fields[0], dateTime.Minute)
               && CronFieldMatch(fields[1], dateTime.Hour)
               && CronFieldMatch(fields[2], dateTime.Day)
               && CronFieldMatch(fields[3], dateTime.Month)
               && CronFieldMatch(fields[4], (int)dateTime.DayOfWeek);
    }

    public static bool CronFieldMatch(string field, int value)
    {
        if (field == "*")
            return true;

        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var segments = part.Split('/');
                if (segments.Length == 2 && int.TryParse(segments[1], out var step) && step > 0)
                {
                    if (segments[0].Contains('-'))
                    {
                        var range = segments[0].Split('-');
                        if (range.Length == 2
                            && int.TryParse(range[0], out var rangeStart)
                            && int.TryParse(range[1], out var rangeEnd)
                            && value >= rangeStart
                            && value <= rangeEnd
                            && (value - rangeStart) % step == 0)
                        {
                            return true;
                        }
                    }
                    else if (segments[0] == "*")
                    {
                        const int effectiveStart = 0;
                        if (value >= effectiveStart && (value - effectiveStart) % step == 0)
                            return true;
                    }
                    else if (int.TryParse(segments[0], out var start))
                    {
                        var effectiveStart = start;
                        if (value >= effectiveStart && (value - effectiveStart) % step == 0)
                            return true;
                    }
                }

                continue;
            }

            if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (range.Length == 2
                    && int.TryParse(range[0], out var start)
                    && int.TryParse(range[1], out var end)
                    && value >= start
                    && value <= end)
                {
                    return true;
                }

                continue;
            }

            if (int.TryParse(part, out var exact) && exact == value)
                return true;
        }

        return false;
    }
}
