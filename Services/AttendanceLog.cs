using System.Globalization;

namespace DepEdDTRSystem.Services;

public enum PunchKind { In, Out }

public enum AttendanceSlot { AmIn = 0, AmOut = 1, PmIn = 2, PmOut = 3 }

public sealed record AttendancePunch(string BadgeNumber, DateTime Timestamp, PunchKind Kind, int LineNumber);

public sealed record AttendanceLineIssue(int LineNumber, string Text, string Message);

public sealed class AttendanceLogResult
{
    public List<AttendancePunch> Punches { get; } = [];

    public List<AttendanceLineIssue> Issues { get; } = [];

    public string? FatalError { get; set; }

    public int LineCount { get; set; }
}

// Reads biometric text logs: column 1 = employee ID, column 2 = date and time, column 6 = I (in) or O (out).
public static class AttendanceLog
{
    public const int MaxLines = 100_000;

    public static readonly TimeOnly Noon = new(12, 0);
    public static readonly TimeOnly LunchWindowEnd = new(13, 0);

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm",
        "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd H:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/MM/dd H:mm",
        "yyyy-MM-ddTHH:mm:ss", "yyyyMMddHHmmss",
        "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm", "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt",
        "M/d/yy H:mm:ss", "M/d/yy H:mm", "M/d/yy h:mm:ss tt", "M/d/yy h:mm tt",
        "d/M/yyyy H:mm:ss", "d/M/yyyy H:mm", "d/M/yyyy h:mm:ss tt", "d/M/yyyy h:mm tt",
        "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm", "M-d-yyyy H:mm:ss", "M-d-yyyy H:mm",
    ];

    private static readonly string[] DateOnlyFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "M/d/yy", "d/M/yyyy", "dd-MM-yyyy", "M-d-yyyy", "yyyyMMdd",
    ];

    private static readonly string[] TimeOnlyFormats =
    [
        "H:mm:ss", "H:mm", "h:mm:ss tt", "h:mm tt", "HHmmss",
    ];

    public static AttendanceLogResult Parse(string text)
    {
        var result = new AttendanceLogResult();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        result.LineCount = lines.Length;
        if (lines.Length > MaxLines + 1)
        {
            result.FatalError = $"The file has more than {MaxLines:N0} lines. Split it into smaller files.";
            return result;
        }

        var delimiter = DetectDelimiter(lines);
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim('\uFEFF', ' ', '\t');
            if (raw.Length == 0)
            {
                continue;
            }

            var lineNumber = i + 1;
            var fields = Split(raw, delimiter);
            if (!TryReadPunch(fields, lineNumber, out var punch, out var error))
            {
                // The first line is often a header row; don't report it as a problem.
                if (result.Punches.Count == 0 && result.Issues.Count == 0 && lineNumber == FirstDataLine(lines))
                {
                    continue;
                }

                result.Issues.Add(new AttendanceLineIssue(lineNumber, Shorten(raw), error!));
                continue;
            }

            result.Punches.Add(punch!);
        }

        if (result.Punches.Count == 0 && result.FatalError is null)
        {
            result.FatalError = result.Issues.Count == 0
                ? "The file has no attendance lines."
                : "No valid attendance lines were found. Check that column 1 is the employee ID, column 2 the date and time, and column 6 is I or O.";
        }

        return result;
    }

    // Before 12:00 = AM, 12:00 onward = PM. A Time Out from 12:00 to 12:59 is the lunch break, so it is
    // the AM Time Out unless that day already has an afternoon Time In before it.
    public static Dictionary<AttendanceSlot, List<TimeOnly>> Assign(IEnumerable<AttendancePunch> dayPunches)
    {
        var slots = Enum.GetValues<AttendanceSlot>().ToDictionary(s => s, _ => new List<TimeOnly>());
        var ordered = dayPunches.OrderBy(p => p.Timestamp).ToList();
        var firstPmIn = ordered
            .Where(p => p.Kind == PunchKind.In && ToMinute(p.Timestamp) >= Noon)
            .Select(p => (TimeOnly?)ToMinute(p.Timestamp))
            .FirstOrDefault();

        foreach (var punch in ordered)
        {
            var time = ToMinute(punch.Timestamp);
            AttendanceSlot slot;
            if (punch.Kind == PunchKind.In)
            {
                slot = time < Noon ? AttendanceSlot.AmIn : AttendanceSlot.PmIn;
            }
            else if (time < Noon)
            {
                slot = AttendanceSlot.AmOut;
            }
            else if (time < LunchWindowEnd && !(firstPmIn is { } pmIn && pmIn < time))
            {
                slot = AttendanceSlot.AmOut;
            }
            else
            {
                slot = AttendanceSlot.PmOut;
            }

            if (!slots[slot].Contains(time))
            {
                slots[slot].Add(time);
            }
        }

        return slots;
    }

    public static TimeOnly ToMinute(DateTime value) => new(value.Hour, value.Minute);

    private static bool TryReadPunch(string[] fields, int lineNumber, out AttendancePunch? punch, out string? error)
    {
        punch = null;
        error = null;

        // Some exports put the date and the time in two separate columns.
        if (fields.Length > 2 && TryParseDateOnly(fields[1], out var datePart) && TryParseTimeOnly(fields[2], out var timePart))
        {
            fields = fields.Take(1).Append($"{datePart:yyyy-MM-dd} {timePart:HH:mm:ss}").Append(string.Empty).Concat(fields.Skip(3)).ToArray();
        }

        if (fields.Length < 6)
        {
            error = "Expected at least 6 columns (employee ID, date and time, …, I or O in column 6).";
            return false;
        }

        var badge = fields[0].Trim();
        if (badge.Length == 0)
        {
            error = "Column 1 (employee ID) is empty.";
            return false;
        }

        if (!DateTime.TryParseExact(fields[1].Trim(), DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            error = $"Column 2 (\"{fields[1].Trim()}\") is not a recognised date and time.";
            return false;
        }

        PunchKind kind;
        switch (fields[5].Trim().ToUpperInvariant())
        {
            case "I" or "IN":
                kind = PunchKind.In;
                break;
            case "O" or "OUT":
                kind = PunchKind.Out;
                break;
            default:
                error = $"Column 6 (\"{fields[5].Trim()}\") must be I or O.";
                return false;
        }

        punch = new AttendancePunch(badge, timestamp, kind, lineNumber);
        return true;
    }

    private static bool TryParseDateOnly(string value, out DateTime date) =>
        DateTime.TryParseExact(value.Trim(), DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseTimeOnly(string value, out DateTime time) =>
        DateTime.TryParseExact(value.Trim(), TimeOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static int FirstDataLine(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim('\uFEFF', ' ', '\t').Length > 0)
            {
                return i + 1;
            }
        }

        return 1;
    }

    private static char? DetectDelimiter(string[] lines)
    {
        var sample = lines.FirstOrDefault(l => l.Trim('\uFEFF', ' ', '\t').Length > 0) ?? string.Empty;
        if (sample.Contains('\t')) return '\t';
        if (sample.Contains(',')) return ',';
        if (sample.Contains(';')) return ';';
        if (sample.Contains('|')) return '|';
        return null;
    }

    private static string[] Split(string line, char? delimiter) =>
        delimiter is { } d
            ? line.Split(d).Select(f => f.Trim().Trim('"')).ToArray()
            : line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
