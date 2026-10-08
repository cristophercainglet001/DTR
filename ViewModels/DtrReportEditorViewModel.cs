using System.Globalization;
using System.Text.Json;
using DepEdDTRSystem.Models;

namespace DepEdDTRSystem.ViewModels;

public sealed class DtrReportEditorViewModel
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public int Month { get; init; }
    public int Year { get; init; }
    public IReadOnlyList<DtrReportEditDay> Days { get; init; } = [];
    public IReadOnlyList<DtrReportEditHistory> History { get; init; } = [];

    public static DtrReportEditorViewModel Create(
        Employee employee,
        IReadOnlyCollection<DtrRecord> records,
        IReadOnlyCollection<AuditLog> editLogs,
        int month,
        int year,
        IReadOnlyCollection<LeaveApplication>? approvedLeaves = null,
        IReadOnlyCollection<SeminarApplication>? approvedSeminars = null,
        IReadOnlyCollection<Holiday>? holidays = null)
    {
        var firstDay = new DateOnly(year, month, 1);
        var recordsByDate = records.ToDictionary(x => x.DtrDate);
        var history = editLogs
            .OrderByDescending(x => x.CreatedAt)
            .Select(log =>
            {
                var note = "Edited";
                var editor = "Administrator";
                DateOnly? attendanceDate = null;
                if (!string.IsNullOrWhiteSpace(log.NewValues))
                {
                    using var document = JsonDocument.Parse(log.NewValues);
                    if (document.RootElement.TryGetProperty("Note", out var noteElement)
                        && noteElement.ValueKind == JsonValueKind.String)
                    {
                        note = noteElement.GetString() ?? note;
                    }

                    if (document.RootElement.TryGetProperty("EditedBy", out var editorElement)
                        && editorElement.ValueKind == JsonValueKind.String)
                    {
                        editor = editorElement.GetString() ?? editor;
                    }

                    if (document.RootElement.TryGetProperty("Date", out var dateElement)
                        && dateElement.ValueKind == JsonValueKind.String
                        && DateOnly.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, out var parsedDate))
                    {
                        attendanceDate = parsedDate;
                    }
                }

                return new DtrReportEditHistory
                {
                    EditedAt = log.CreatedAt,
                    EditedBy = editor,
                    Note = note,
                    AttendanceDate = attendanceDate,
                    RecordId = log.EntityId,
                };
            })
            .ToArray();

        var latestEditByRecord = history
            .Where(x => x.RecordId is not null)
            .GroupBy(x => x.RecordId!)
            .ToDictionary(g => g.Key, g => (Latest: g.First(), Count: g.Count()));

        var days = Enumerable.Range(0, DateTime.DaysInMonth(year, month))
            .Select(offset =>
            {
                var date = firstDay.AddDays(offset);
                recordsByDate.TryGetValue(date, out var record);
                var edit = record is not null && latestEditByRecord.TryGetValue(record.Id.ToString(), out var found)
                    ? found
                    : default;
                var holiday = holidays?.FirstOrDefault(x => x.HolidayDate == date);
                var onLeave = approvedLeaves?.Any(x => x.StartDate <= date && x.EndDate >= date) == true;
                var inSeminar = approvedSeminars?.Any(x => x.StartDate <= date && x.EndDate >= date) == true;
                return new DtrReportEditDay
                {
                    Date = date,
                    IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    SpecialLabel = holiday is not null
                        ? $"Holiday: {holiday.HolidayName}"
                        : onLeave ? "On leave" : inSeminar ? "Seminar" : null,
                    AmTimeIn = FormatTime(record?.AmTimeIn),
                    AmTimeOut = FormatTime(record?.AmTimeOut),
                    PmTimeIn = FormatTime(record?.PmTimeIn),
                    PmTimeOut = FormatTime(record?.PmTimeOut),
                    EditCount = edit.Latest is null ? 0 : edit.Count,
                    LastEditedAt = edit.Latest?.EditedAt,
                    LastEditedBy = edit.Latest?.EditedBy,
                };
            })
            .ToArray();

        return new DtrReportEditorViewModel
        {
            EmployeeId = employee.Id,
            EmployeeName = string.Join(' ', new[]
            {
                employee.FirstName,
                employee.MiddleName,
                employee.LastName,
                employee.Suffix,
            }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Month = month,
            Year = year,
            Days = days,
            History = history,
        };
    }

    private static string FormatTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed class DtrReportEditDay
{
    public DateOnly Date { get; init; }
    public bool IsWeekend { get; init; }
    public string? SpecialLabel { get; init; }
    public string AmTimeIn { get; init; } = string.Empty;
    public string AmTimeOut { get; init; } = string.Empty;
    public string PmTimeIn { get; init; } = string.Empty;
    public string PmTimeOut { get; init; } = string.Empty;
    public int EditCount { get; init; }
    public DateTime? LastEditedAt { get; init; }
    public string? LastEditedBy { get; init; }
}

public sealed class DtrReportEditHistory
{
    public DateTime EditedAt { get; init; }
    public string EditedBy { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public DateOnly? AttendanceDate { get; init; }
    public string? RecordId { get; init; }
}
