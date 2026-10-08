namespace DepEdDTRSystem.Services;

public sealed class AttendanceImportRow
{
    public int EmployeeDbId { get; init; }

    public string BadgeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public DateOnly Date { get; init; }

    public TimeOnly?[] Existing { get; init; } = new TimeOnly?[4];

    public List<TimeOnly>[] Candidates { get; init; } = Enumerable.Range(0, 4).Select(_ => new List<TimeOnly>()).ToArray();

    // Imported times that differ from what is already saved, in time order.
    public List<TimeOnly> Options(AttendanceSlot slot) =>
        Candidates[(int)slot].Where(t => t != Existing[(int)slot]).OrderBy(t => t).ToList();

    public bool IsConflict(AttendanceSlot slot)
    {
        var options = Options(slot).Count;
        return Existing[(int)slot] is null ? options > 1 : options >= 1;
    }

    public bool HasConflict => Enum.GetValues<AttendanceSlot>().Any(IsConflict);

    public bool HasChange =>
        Enum.GetValues<AttendanceSlot>().Any(s => Existing[(int)s] is null && Options(s).Count > 0);

    public bool RecordExists => Existing.Any(x => x is not null);

    // "e" keeps the saved time, "x" leaves the slot alone, a number picks that option.
    public string DefaultChoice(AttendanceSlot slot)
    {
        if (Existing[(int)slot] is not null)
        {
            return "e";
        }

        var options = Options(slot);
        return IsInSlot(slot) ? "0" : (options.Count - 1).ToString();
    }

    // Returns the time to write, or null when the slot should stay as it is.
    public TimeOnly? Resolve(AttendanceSlot slot, string? choice)
    {
        var options = Options(slot);
        if (options.Count == 0)
        {
            return null;
        }

        if (!IsConflict(slot))
        {
            return Existing[(int)slot] is null ? options[0] : null;
        }

        choice = string.IsNullOrEmpty(choice) ? DefaultChoice(slot) : choice;
        return int.TryParse(choice, out var index) && index >= 0 && index < options.Count
            ? options[index]
            : null;
    }

    public static bool IsInSlot(AttendanceSlot slot) => slot is AttendanceSlot.AmIn or AttendanceSlot.PmIn;

    public static string Label(AttendanceSlot slot) => slot switch
    {
        AttendanceSlot.AmIn => "AM In",
        AttendanceSlot.AmOut => "AM Out",
        AttendanceSlot.PmIn => "PM In",
        _ => "PM Out",
    };
}

public sealed class AttendanceImportPlan
{
    public string Token { get; init; } = Guid.NewGuid().ToString("N");

    public string Owner { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public int PunchCount { get; init; }

    public int AlreadyMatchingDays { get; set; }

    public List<AttendanceImportRow> Rows { get; } = [];

    public List<(string BadgeNumber, int PunchCount)> UnmatchedEmployees { get; } = [];

    public List<AttendanceLineIssue> Issues { get; init; } = [];
}
