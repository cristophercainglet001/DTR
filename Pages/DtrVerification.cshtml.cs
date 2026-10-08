using System.Globalization;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Services;
using DepEdDTRSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages;

[AllowAnonymous]
public class DtrVerificationModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly DtrReportVerificationService _verification;

    public DtrVerificationModel(DtrDbContext db, DtrReportVerificationService verification)
    {
        _db = db;
        _verification = verification;
    }

    public bool IsVerified { get; private set; }
    public string StatusMessage { get; private set; } = string.Empty;
    public string EmployeeName { get; private set; } = string.Empty;
    public string SchoolName { get; private set; } = string.Empty;
    public string ReportPeriod { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; private set; }

    public async Task OnGetAsync(string? token, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!_verification.TryReadToken(token, out var payload) || payload is null)
        {
            StatusMessage = "This QR code is invalid or could not be verified.";
            return;
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .Include(x => x.School)
            .FirstOrDefaultAsync(x => x.Id == payload.EmployeeId, cancellationToken);
        if (employee is null)
        {
            StatusMessage = "The employee record for this report could not be found.";
            return;
        }

        var firstDay = new DateOnly(payload.Year, payload.Month, 1);
        var nextMonth = firstDay.AddMonths(1);
        var records = await _db.DtrRecords
            .AsNoTracking()
            .Where(x => x.EmployeeId == employee.Id && x.DtrDate >= firstDay && x.DtrDate < nextMonth)
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
        var report = DtrForm48ViewModel.Create(employee, records, payload.Month, payload.Year, approvedLeaves, approvedSeminars);

        if (!_verification.MatchesCurrentReport(payload, report))
        {
            StatusMessage = "The report no longer matches the current official records. Generate a new Form 48 to verify the latest information.";
            return;
        }

        IsVerified = true;
        StatusMessage = "This Form 48 matches the current official records in the DTR system.";
        EmployeeName = report.EmployeeName;
        SchoolName = report.SchoolName;
        ReportPeriod = $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(report.Month)} {report.Year}";
        IssuedAtUtc = payload.IssuedAtUtc;
    }
}
