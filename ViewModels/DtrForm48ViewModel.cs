using DepEdDTRSystem.Models;

namespace DepEdDTRSystem.ViewModels;

public sealed class DtrForm48ViewModel
{
    public int EmployeeDatabaseId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNumber { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string SchoolName { get; init; } = string.Empty;
    public string SchoolId { get; init; } = string.Empty;
    public string SchoolHeadName { get; init; } = string.Empty;
    public string SchoolHeadPosition { get; init; } = string.Empty;
    public string VerificationQrCodeDataUri { get; set; } = string.Empty;
    public string VerificationUrl { get; set; } = string.Empty;
    public int Month { get; init; }
    public int Year { get; init; }
    public int UndertimeMinutes { get; init; }
    public int UndertimeHours => UndertimeMinutes / 60;
    public int UndertimeRemainderMinutes => UndertimeMinutes % 60;
    public IReadOnlyList<DtrForm48Day> Days { get; init; } = [];
    public IReadOnlyList<DtrForm48ApprovedLeave> ApprovedLeaves { get; init; } = [];
    public IReadOnlyList<DtrForm48ApprovedSeminar> ApprovedSeminars { get; init; } = [];

    public static DtrForm48ViewModel Create(
        Employee employee,
        IReadOnlyCollection<DtrRecord> records,
        int month,
        int year,
        IReadOnlyCollection<LeaveApplication>? approvedLeaves = null,
        IReadOnlyCollection<SeminarApplication>? approvedSeminars = null)
    {
        var firstDay = new DateOnly(year, month, 1);
        var nextMonth = firstDay.AddMonths(1);
        var reportLeaves = (approvedLeaves ?? Array.Empty<LeaveApplication>())
            .Where(x => x.Status == "Approved" && x.StartDate < nextMonth && x.EndDate >= firstDay)
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.EndDate)
            .ThenBy(x => x.LeaveType)
            .Select(x => new DtrForm48ApprovedLeave
            {
                LeaveType = x.LeaveType,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                NumberOfDays = x.NumberOfDays,
            })
            .ToArray();
        var reportSeminars = (approvedSeminars ?? Array.Empty<SeminarApplication>())
            .Where(x => x.Status == "Approved" && x.StartDate < nextMonth && x.EndDate >= firstDay)
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.EndDate)
            .ThenBy(x => x.SeminarTitle)
            .Select(x => new DtrForm48ApprovedSeminar
            {
                SeminarTitle = x.SeminarTitle,
                Organizer = x.Organizer,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Venue = x.Venue,
                Purpose = x.Purpose,
            })
            .ToArray();
        var recordByDate = records.ToDictionary(x => x.DtrDate);
        var days = Enumerable.Range(0, DateTime.DaysInMonth(year, month))
            .Select(offset =>
            {
                var date = firstDay.AddDays(offset);
                recordByDate.TryGetValue(date, out var record);
                var leaveTypes = reportLeaves
                    .Where(leave => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                                    && date >= leave.StartDate
                                    && date <= leave.EndDate)
                    .Select(leave => leave.LeaveType)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var seminarTitles = reportSeminars
                    .Where(seminar => date >= seminar.StartDate && date <= seminar.EndDate)
                    .Select(seminar => seminar.SeminarTitle)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return new DtrForm48Day
                {
                    Date = date,
                    AmTimeIn = leaveTypes.Length > 0 || seminarTitles.Length > 0 ? string.Empty : FormatTime(record?.AmTimeIn),
                    AmTimeOut = leaveTypes.Length > 0 || seminarTitles.Length > 0 ? string.Empty : FormatTime(record?.AmTimeOut),
                    PmTimeIn = leaveTypes.Length > 0 || seminarTitles.Length > 0 ? string.Empty : FormatTime(record?.PmTimeIn),
                    PmTimeOut = leaveTypes.Length > 0 || seminarTitles.Length > 0 ? string.Empty : FormatTime(record?.PmTimeOut),
                    OnLeaveLabel = leaveTypes.Length > 0
                        ? $"On Leave ({string.Join(", ", leaveTypes)})"
                        : string.Empty,
                    OnSeminarLabel = seminarTitles.Length > 0
                        ? $"On Seminar ({string.Join(", ", seminarTitles)})"
                        : string.Empty,
                    UndertimeMinutes = record?.UndertimeMinutes ?? 0,
                    ApprovedLeaveTypes = leaveTypes,
                };
            })
            .ToArray();

        return new DtrForm48ViewModel
        {
            EmployeeDatabaseId = employee.Id,
            EmployeeName = string.Join(' ', new[]
            {
                employee.FirstName,
                employee.MiddleName,
                employee.LastName,
                employee.Suffix,
            }.Where(x => !string.IsNullOrWhiteSpace(x))),
            EmployeeNumber = employee.EmployeeId,
            Position = employee.Position,
            SchoolName = employee.School?.SchoolName ?? string.Empty,
            SchoolId = employee.School?.SchoolId ?? string.Empty,
            SchoolHeadName = employee.School?.SchoolHeadName ?? string.Empty,
            SchoolHeadPosition = employee.School?.SchoolHeadPosition ?? string.Empty,
            Month = month,
            Year = year,
            UndertimeMinutes = days.Sum(x => x.UndertimeMinutes),
            Days = days,
            ApprovedLeaves = reportLeaves,
            ApprovedSeminars = reportSeminars,
        };
    }

    private static string FormatTime(TimeOnly? time) => time?.ToString("h:mm") ?? string.Empty;
}

public sealed class DtrForm48ApprovedLeave
{
    public string LeaveType { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal NumberOfDays { get; init; }
}

public sealed class DtrForm48ApprovedSeminar
{
    public string SeminarTitle { get; init; } = string.Empty;
    public string Organizer { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string Venue { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed class DtrForm48Day
{
    public DateOnly Date { get; init; }
    public string AmTimeIn { get; init; } = string.Empty;
    public string AmTimeOut { get; init; } = string.Empty;
    public string PmTimeIn { get; init; } = string.Empty;
    public string PmTimeOut { get; init; } = string.Empty;
    public string OnLeaveLabel { get; init; } = string.Empty;
    public string OnSeminarLabel { get; init; } = string.Empty;
    public int UndertimeMinutes { get; init; }
    public string UndertimeHours => UndertimeMinutes > 0 ? (UndertimeMinutes / 60).ToString() : string.Empty;
    public string UndertimeRemainderMinutes => UndertimeMinutes > 0 ? (UndertimeMinutes % 60).ToString() : string.Empty;
    public IReadOnlyList<string> ApprovedLeaveTypes { get; init; } = [];
}
