using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using DepEdDTRSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
public class ReportsModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly DtrReportVerificationService _verification;

    public ReportsModel(DtrDbContext db, DtrReportVerificationService verification)
    {
        _db = db;
        _verification = verification;
    }

    public IReadOnlyList<SelectListItem> EmployeeOptions { get; private set; } = [];
    public DtrForm48ViewModel? Report { get; private set; }
    public IReadOnlyList<DtrForm48ViewModel> Reports { get; private set; } = [];
    public DtrReportEditorViewModel? ReportEditor { get; private set; }
    public IReadOnlyList<DtrReportEditorViewModel> ReportEditors { get; private set; } = [];
    public int? SelectedEmployeeId { get; private set; }
    public bool IsAllEmployeesSelected { get; private set; }
    public int Month { get; private set; } = DateTime.Today.Month;
    public int Year { get; private set; } = DateTime.Today.Year;

    public async Task OnGetAsync(string? employeeId, int? month, int? year, CancellationToken cancellationToken)
    {
        IsAllEmployeesSelected = string.Equals(employeeId, "all", StringComparison.OrdinalIgnoreCase);
        SelectedEmployeeId = int.TryParse(employeeId, out var parsedEmployeeId) && parsedEmployeeId > 0
            ? parsedEmployeeId
            : null;
        Month = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        Year = year is >= 2000 and <= 9998 ? year.Value : DateTime.Today.Year;

        var employees = await _db.Employees
            .AsNoTracking()
            .Include(x => x.School)
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);
        EmployeeOptions = employees.Select(x => new SelectListItem(
            $"{x.LastName}, {x.FirstName} · {x.EmployeeId} · {x.School?.SchoolName}",
            x.Id.ToString(),
            x.Id == SelectedEmployeeId)).ToArray();

        if (IsAllEmployeesSelected)
        {
            var allEmployeesFirstDay = new DateOnly(Year, Month, 1);
            var allEmployeesNextMonth = allEmployeesFirstDay.AddMonths(1);
            var employeeIds = employees.Select(x => x.Id).ToArray();
            var allEmployeeRecords = await _db.DtrRecords
                .AsNoTracking()
                .Where(x => employeeIds.Contains(x.EmployeeId)
                            && x.DtrDate >= allEmployeesFirstDay
                            && x.DtrDate < allEmployeesNextMonth)
                .ToListAsync(cancellationToken);
            var allEmployeeLeaves = await _db.LeaveApplications
                .AsNoTracking()
                .Where(x => employeeIds.Contains(x.EmployeeId)
                            && x.Status == "Approved"
                            && x.StartDate < allEmployeesNextMonth
                            && x.EndDate >= allEmployeesFirstDay)
                .ToListAsync(cancellationToken);
            var allEmployeeSeminars = await _db.SeminarApplications
                .AsNoTracking()
                .Where(x => employeeIds.Contains(x.EmployeeId)
                            && x.Status == "Approved"
                            && x.StartDate < allEmployeesNextMonth
                            && x.EndDate >= allEmployeesFirstDay)
                .ToListAsync(cancellationToken);
            var recordsByEmployee = allEmployeeRecords.GroupBy(x => x.EmployeeId)
                .ToDictionary(x => x.Key, x => (IReadOnlyCollection<DtrRecord>)x.ToArray());
            var allRecordIds = allEmployeeRecords.Select(x => x.Id.ToString()).ToArray();
            var reportEditLogs = allRecordIds.Length == 0
                ? []
                : await _db.AuditLogs
                    .AsNoTracking()
                    .Where(x => x.EntityName == "DtrRecord"
                                && x.Action == "ReportEdit"
                                && allRecordIds.Contains(x.EntityId!))
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync(cancellationToken);
            var recordOwnerById = allEmployeeRecords.ToDictionary(x => x.Id.ToString(), x => x.EmployeeId);
            var editLogsByEmployee = reportEditLogs
                .Where(log => log.EntityId is not null && recordOwnerById.ContainsKey(log.EntityId))
                .GroupBy(log => recordOwnerById[log.EntityId!])
                .ToDictionary(x => x.Key, x => (IReadOnlyCollection<AuditLog>)x.ToArray());
            var leavesByEmployee = allEmployeeLeaves.GroupBy(x => x.EmployeeId)
                .ToDictionary(x => x.Key, x => (IReadOnlyCollection<LeaveApplication>)x.ToArray());
            var seminarsByEmployee = allEmployeeSeminars.GroupBy(x => x.EmployeeId)
                .ToDictionary(x => x.Key, x => (IReadOnlyCollection<SeminarApplication>)x.ToArray());
            var allReports = new List<DtrForm48ViewModel>(employees.Count);
            var monthHolidays = await _db.Holidays
                .AsNoTracking()
                .Where(x => x.HolidayDate >= allEmployeesFirstDay && x.HolidayDate < allEmployeesNextMonth)
                .ToListAsync(cancellationToken);
            var allReportEditors = new List<DtrReportEditorViewModel>(employees.Count);
            foreach (var reportEmployee in employees)
            {
                recordsByEmployee.TryGetValue(reportEmployee.Id, out var employeeRecords);
                leavesByEmployee.TryGetValue(reportEmployee.Id, out var employeeLeaves);
                seminarsByEmployee.TryGetValue(reportEmployee.Id, out var employeeSeminars);
                editLogsByEmployee.TryGetValue(reportEmployee.Id, out var employeeEditLogs);
                var dtrRecords = employeeRecords ?? Array.Empty<DtrRecord>();
                var report = DtrForm48ViewModel.Create(
                    reportEmployee,
                    dtrRecords,
                    Month,
                    Year,
                    employeeLeaves ?? Array.Empty<LeaveApplication>(),
                    employeeSeminars ?? Array.Empty<SeminarApplication>());
                _verification.PrepareForVerification(report, Request);
                allReports.Add(report);
                allReportEditors.Add(DtrReportEditorViewModel.Create(
                    reportEmployee,
                    dtrRecords,
                    employeeEditLogs ?? Array.Empty<AuditLog>(),
                    Month,
                    Year,
                    employeeLeaves ?? Array.Empty<LeaveApplication>(),
                    employeeSeminars ?? Array.Empty<SeminarApplication>(),
                    monthHolidays.Where(h => h.SchoolId == reportEmployee.SchoolId).ToArray()));
            }
            Reports = allReports;
            ReportEditors = allReportEditors;
            return;
        }

        if (SelectedEmployeeId is not > 0)
        {
            return;
        }

        var employee = employees.FirstOrDefault(x => x.Id == SelectedEmployeeId.Value);
        if (employee is null)
        {
            ModelState.AddModelError(string.Empty, "Select an employee from the directory.");
            return;
        }

        var firstDay = new DateOnly(Year, Month, 1);
        var nextMonth = firstDay.AddMonths(1);
        var records = await _db.DtrRecords
            .AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.DtrDate >= firstDay && x.DtrDate < nextMonth)
            .ToListAsync(cancellationToken);
        var reportRecordIds = records.Select(x => x.Id.ToString()).ToArray();
        var reportEditHistory = reportRecordIds.Length == 0
            ? []
            : await _db.AuditLogs
                .AsNoTracking()
                .Where(x => x.EntityName == "DtrRecord"
                            && x.Action == "ReportEdit"
                            && reportRecordIds.Contains(x.EntityId!))
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken);
        var approvedLeaves = await _db.LeaveApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id
                        && x.Status == "Approved"
                        && x.StartDate < nextMonth
                        && x.EndDate >= firstDay)
            .ToListAsync(cancellationToken);
        var approvedSeminars = await _db.SeminarApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id
                        && x.Status == "Approved"
                        && x.StartDate < nextMonth
                        && x.EndDate >= firstDay)
            .ToListAsync(cancellationToken);
        Report = DtrForm48ViewModel.Create(employee, records, Month, Year, approvedLeaves, approvedSeminars);
        var employeeHolidays = await _db.Holidays
            .AsNoTracking()
            .Where(x => x.SchoolId == employee.SchoolId && x.HolidayDate >= firstDay && x.HolidayDate < nextMonth)
            .ToListAsync(cancellationToken);
        ReportEditor = DtrReportEditorViewModel.Create(
            employee, records, reportEditHistory, Month, Year, approvedLeaves, approvedSeminars, employeeHolidays);
        _verification.PrepareForVerification(Report, Request);
    }

    public async Task<IActionResult> OnPostSaveAttendanceAsync(
        int employeeId,
        DateOnly date,
        int month,
        int year,
        TimeOnly? amTimeIn,
        TimeOnly? amTimeOut,
        TimeOnly? pmTimeIn,
        TimeOnly? pmTimeOut,
        string? note,
        CancellationToken cancellationToken)
    {
        month = month is >= 1 and <= 12 ? month : DateTime.Today.Month;
        year = year is >= 2000 and <= 9998 ? year : DateTime.Today.Year;
        var reportStart = new DateOnly(year, month, 1);
        var reportEnd = reportStart.AddMonths(1);
        var trimmedNote = note?.Trim();

        if (date < reportStart || date >= reportEnd || employeeId <= 0)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "The attendance edit does not match a valid employee and report month.";
            return RedirectToPage(new { employeeId, month, year });
        }
        if (trimmedNote?.Length > 500)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "The edit note must be no longer than 500 characters.";
            TempData["OpenEditor"] = employeeId;
            return RedirectToPage(new { employeeId, month, year });
        }
        if ((amTimeIn.HasValue && amTimeOut.HasValue && amTimeOut <= amTimeIn)
            || (pmTimeIn.HasValue && pmTimeOut.HasValue && pmTimeOut <= pmTimeIn))
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "Each departure time must be later than its matching arrival time.";
            TempData["OpenEditor"] = employeeId;
            return RedirectToPage(new { employeeId, month, year });
        }

        var employeeExists = await _db.Employees
            .AnyAsync(x => x.Id == employeeId, cancellationToken);
        if (!employeeExists)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "That employee could not be found.";
            return RedirectToPage(new { month, year });
        }

        var record = await _db.DtrRecords
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.DtrDate == date, cancellationToken);
        var isNewRecord = record is null;
        if (record is null)
        {
            if (!amTimeIn.HasValue && !amTimeOut.HasValue && !pmTimeIn.HasValue && !pmTimeOut.HasValue)
            {
                TempData["StatusTone"] = "warning";
                TempData["StatusMessage"] = "Enter at least one attendance time before adding an empty DTR record.";
                TempData["OpenEditor"] = employeeId;
                return RedirectToPage(new { employeeId, month, year });
            }

            record = new DtrRecord
            {
                EmployeeId = employeeId,
                DtrDate = date,
                CreatedAt = DateTime.UtcNow,
            };
            _db.DtrRecords.Add(record);
        }

        var now = DateTime.UtcNow;
        var hasChanges = isNewRecord
            || amTimeIn != record.AmTimeIn
            || amTimeOut != record.AmTimeOut
            || pmTimeIn != record.PmTimeIn
            || pmTimeOut != record.PmTimeOut;
        if (!hasChanges)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = $"No changes to save for {date:MMM d, yyyy}.";
            TempData["OpenEditor"] = employeeId;
            return RedirectToPage(new { employeeId, month, year });
        }

        var oldValues = new
        {
            AmTimeIn = FormatAuditTime(record.AmTimeIn),
            AmTimeOut = FormatAuditTime(record.AmTimeOut),
            PmTimeIn = FormatAuditTime(record.PmTimeIn),
            PmTimeOut = FormatAuditTime(record.PmTimeOut),
        };
        record.AmTimeIn = amTimeIn;
        record.AmTimeOut = amTimeOut;
        record.PmTimeIn = pmTimeIn;
        record.PmTimeOut = pmTimeOut;
        record.EntrySource = "Manual";
        record.Remarks = MarkEdited(record.Remarks);
        record.UpdatedAt = now;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _db.AuditLogs.Add(CreateEditAudit(record, oldValues, date, employeeId, isNewRecord, trimmedNote, now));
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = $"Attendance for {date:MMM d, yyyy} was saved and marked Edited. The mark and note are visible only to administrators, not on Form 48.";
        TempData["OpenEditor"] = employeeId;
        return RedirectToPage(new { employeeId, month, year });
    }

    public sealed class BulkEditEntry
    {
        public string? Date { get; set; }
        public string? AmTimeIn { get; set; }
        public string? AmTimeOut { get; set; }
        public string? PmTimeIn { get; set; }
        public string? PmTimeOut { get; set; }
    }

    public async Task<IActionResult> OnPostSaveManyAsync(
        int employeeId,
        int month,
        int year,
        string? entriesJson,
        string? note,
        CancellationToken cancellationToken)
    {
        month = month is >= 1 and <= 12 ? month : DateTime.Today.Month;
        year = year is >= 2000 and <= 9998 ? year : DateTime.Today.Year;
        var reportStart = new DateOnly(year, month, 1);
        var reportEnd = reportStart.AddMonths(1);
        var trimmedNote = note?.Trim();

        IActionResult Reject(string message)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = message;
            TempData["OpenEditor"] = employeeId;
            return RedirectToPage(new { employeeId, month, year });
        }

        if (trimmedNote?.Length > 500)
        {
            return Reject("The edit note must be no longer than 500 characters.");
        }

        List<BulkEditEntry>? rawEntries;
        try
        {
            rawEntries = string.IsNullOrWhiteSpace(entriesJson) || entriesJson.Length > 60_000
                ? null
                : JsonSerializer.Deserialize<List<BulkEditEntry>>(
                    entriesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            rawEntries = null;
        }

        if (rawEntries is null || rawEntries.Count == 0 || rawEntries.Count > 31)
        {
            return Reject("There are no changed days to save.");
        }

        if (!await _db.Employees.AnyAsync(x => x.Id == employeeId, cancellationToken))
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "That employee could not be found.";
            return RedirectToPage(new { month, year });
        }

        var entries = new List<(DateOnly Date, TimeOnly? AmIn, TimeOnly? AmOut, TimeOnly? PmIn, TimeOnly? PmOut)>();
        foreach (var raw in rawEntries)
        {
            if (!DateOnly.TryParseExact(raw.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date < reportStart || date >= reportEnd
                || entries.Any(x => x.Date == date))
            {
                return Reject("One of the changed days is not valid for this report month.");
            }

            if (!TryParseEditTime(raw.AmTimeIn, out var amIn) || !TryParseEditTime(raw.AmTimeOut, out var amOut)
                || !TryParseEditTime(raw.PmTimeIn, out var pmIn) || !TryParseEditTime(raw.PmTimeOut, out var pmOut))
            {
                return Reject($"A time entered for {date:MMM d} is not valid.");
            }

            if ((amIn.HasValue && amOut.HasValue && amOut <= amIn) || (pmIn.HasValue && pmOut.HasValue && pmOut <= pmIn))
            {
                return Reject($"{date:MMM d}: each departure time must be later than its matching arrival time. Nothing was saved.");
            }

            entries.Add((date, amIn, amOut, pmIn, pmOut));
        }

        var dates = entries.Select(x => x.Date).ToList();
        var records = (await _db.DtrRecords
                .Where(x => x.EmployeeId == employeeId && dates.Contains(x.DtrDate))
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DtrDate);

        var now = DateTime.UtcNow;
        var changed = new List<(DtrRecord Record, object Old, DateOnly Date, bool IsNew)>();
        foreach (var (date, amIn, amOut, pmIn, pmOut) in entries)
        {
            var isNew = !records.TryGetValue(date, out var record);
            if (record is null)
            {
                if (!amIn.HasValue && !amOut.HasValue && !pmIn.HasValue && !pmOut.HasValue)
                {
                    continue;
                }

                record = new DtrRecord { EmployeeId = employeeId, DtrDate = date, CreatedAt = now };
                _db.DtrRecords.Add(record);
            }
            else if (amIn == record.AmTimeIn && amOut == record.AmTimeOut && pmIn == record.PmTimeIn && pmOut == record.PmTimeOut)
            {
                continue;
            }

            var old = new
            {
                AmTimeIn = FormatAuditTime(record.AmTimeIn),
                AmTimeOut = FormatAuditTime(record.AmTimeOut),
                PmTimeIn = FormatAuditTime(record.PmTimeIn),
                PmTimeOut = FormatAuditTime(record.PmTimeOut),
            };
            record.AmTimeIn = amIn;
            record.AmTimeOut = amOut;
            record.PmTimeIn = pmIn;
            record.PmTimeOut = pmOut;
            record.EntrySource = "Manual";
            record.Remarks = MarkEdited(record.Remarks);
            record.UpdatedAt = now;
            changed.Add((record, old, date, isNew));
        }

        if (changed.Count == 0)
        {
            return Reject("No changes to save.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var (record, old, date, isNew) in changed)
        {
            _db.AuditLogs.Add(CreateEditAudit(record, old, date, employeeId, isNew, trimmedNote, now));
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = $"{changed.Count} day(s) saved and marked Edited. The mark and note are visible only to administrators, not on Form 48.";
        TempData["OpenEditor"] = employeeId;
        return RedirectToPage(new { employeeId, month, year });
    }

    private AuditLog CreateEditAudit(
        DtrRecord record,
        object oldValues,
        DateOnly date,
        int employeeId,
        bool isNewRecord,
        string? note,
        DateTime now) => new()
    {
        Action = "ReportEdit",
        EntityName = "DtrRecord",
        EntityId = record.Id.ToString(),
        OldValues = JsonSerializer.Serialize(oldValues),
        NewValues = JsonSerializer.Serialize(new
        {
            AmTimeIn = FormatAuditTime(record.AmTimeIn),
            AmTimeOut = FormatAuditTime(record.AmTimeOut),
            PmTimeIn = FormatAuditTime(record.PmTimeIn),
            PmTimeOut = FormatAuditTime(record.PmTimeOut),
            Note = string.IsNullOrWhiteSpace(note) ? "Edited" : note,
            EditedBy = User.Identity?.Name ?? "Administrator",
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EmployeeId = employeeId,
            IsNewRecord = isNewRecord,
        }),
        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        CreatedAt = now,
    };

    private static bool TryParseEditTime(string? value, out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (TimeOnly.TryParseExact(value.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            time = parsed;
            return true;
        }

        return false;
    }

    internal static string MarkEdited(string? remarks)
    {
        const string mark = "Edited";
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return mark;
        }

        if (remarks.Contains(mark, StringComparison.OrdinalIgnoreCase))
        {
            return remarks;
        }

        var combined = $"{remarks.Trim()}; {mark}";
        return combined.Length <= 500 ? combined : remarks;
    }

    private static string? FormatAuditTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture);
}
