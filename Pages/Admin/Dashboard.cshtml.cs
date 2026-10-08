using DepEdDTRSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DashboardModel : PageModel
{
    private readonly DtrDbContext _db;

    public DashboardModel(DtrDbContext db)
    {
        _db = db;
    }

    public int SchoolCount { get; private set; }

    public int SchoolYearCount { get; private set; }

    public string CurrentSchoolYear { get; private set; } = "Not set";

    public int EmployeeCount { get; private set; }

    public int ActiveEmployeeCount { get; private set; }

    public int DtrRecordCount { get; private set; }

    public int TodayDtrRecordCount { get; private set; }

    public int HolidayCount { get; private set; }

    public int LeaveApplicationCount { get; private set; }

    public int PendingLeaveCount { get; private set; }

    public int AuditLogCount { get; private set; }

    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);

    public int EmployeesWithoutTodayRecord => Math.Max(0, ActiveEmployeeCount - TodayDtrRecordCount);

    public async Task OnGetAsync()
    {
        SchoolCount = await _db.Schools.CountAsync();
        SchoolYearCount = await _db.SchoolYears.CountAsync();
        CurrentSchoolYear = await _db.SchoolYears
            .AsNoTracking()
            .Where(x => x.IsCurrent)
            .OrderByDescending(x => x.StartDate)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? "Not set";
        EmployeeCount = await _db.Employees.CountAsync();
        ActiveEmployeeCount = await _db.Employees.CountAsync(x => x.Status == "Active");
        DtrRecordCount = await _db.DtrRecords.CountAsync();
        TodayDtrRecordCount = await _db.DtrRecords.CountAsync(x => x.DtrDate == Today);
        HolidayCount = await _db.Holidays.CountAsync();
        LeaveApplicationCount = await _db.LeaveApplications.CountAsync();
        PendingLeaveCount = await _db.LeaveApplications.CountAsync(x => x.Status == "Pending");
        AuditLogCount = await _db.AuditLogs.CountAsync();
    }
}
