using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using DepEdDTRSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[Authorize(AuthenticationSchemes = EmployeeAuthDefaults.Scheme, Roles = "Employee")]
public sealed class DashboardModel : PageModel
{
    private readonly DtrDbContext _db;

    public DashboardModel(DtrDbContext db)
    {
        _db = db;
    }

    public sealed record UpcomingItem(string Kind, string Title, DateOnly Start, DateOnly End);

    public Models.Employee Employee { get; private set; } = null!;

    public EmployeeAvatarViewModel Avatar { get; private set; } = null!;

    public DateOnly Today { get; private set; }

    public DtrRecord? TodayRecord { get; private set; }

    public bool IsWeekend { get; private set; }

    public int DaysRecordedThisMonth { get; private set; }

    public int UndertimeMinutesThisMonth { get; private set; }

    public int PendingLeaveCount { get; private set; }

    public int PendingSeminarCount { get; private set; }

    public decimal ApprovedLeaveDaysThisYear { get; private set; }

    public IReadOnlyList<UpcomingItem> Upcoming { get; private set; } = [];

    public string FullName => string.Join(' ', new[] { Employee.FirstName, Employee.LastName }
        .Where(x => !string.IsNullOrWhiteSpace(x)));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!int.TryParse(User.FindFirstValue("employee_id"), out var employeeId))
        {
            throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");
        }

        Employee = await _db.Employees
            .AsNoTracking()
            .Include(x => x.School)
            .FirstOrDefaultAsync(x => x.Id == employeeId && x.Status == "Active", cancellationToken)
            ?? throw new InvalidOperationException("The signed-in employee record is not active or no longer exists.");

        var photoVersion = await _db.EmployeeProfiles
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PhotoData != null)
            .Select(x => x.PhotoUpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        Avatar = EmployeeDisplay.CreateAvatar(
            Employee,
            photoVersion is null ? null : Url.Page("/Employee/Profile", "Photo", new { v = photoVersion.Value.Ticks }),
            "xl");

        Today = DateOnly.FromDateTime(DateTime.Today);
        IsWeekend = Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var monthRecords = await _db.DtrRecords
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.DtrDate >= monthStart && x.DtrDate < monthStart.AddMonths(1))
            .ToListAsync(cancellationToken);
        TodayRecord = monthRecords.FirstOrDefault(x => x.DtrDate == Today);
        DaysRecordedThisMonth = monthRecords.Count(x => x.AmTimeIn != null || x.PmTimeIn != null);
        UndertimeMinutesThisMonth = monthRecords.Sum(x => x.UndertimeMinutes);

        PendingLeaveCount = await _db.LeaveApplications
            .CountAsync(x => x.EmployeeId == employeeId && x.Status == "Pending", cancellationToken);
        PendingSeminarCount = await _db.SeminarApplications
            .CountAsync(x => x.EmployeeId == employeeId && x.Status == "Pending", cancellationToken);

        var yearStart = new DateOnly(Today.Year, 1, 1);
        ApprovedLeaveDaysThisYear = (await _db.LeaveApplications
            .Where(x => x.EmployeeId == employeeId
                        && x.Status == "Approved"
                        && x.StartDate >= yearStart
                        && x.StartDate < yearStart.AddYears(1))
            .Select(x => x.NumberOfDays)
            .ToListAsync(cancellationToken)).Sum();

        var leaves = await _db.LeaveApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.Status == "Approved" && x.EndDate >= Today)
            .OrderBy(x => x.StartDate)
            .Take(5)
            .ToListAsync(cancellationToken);
        var seminars = await _db.SeminarApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.Status == "Approved" && x.EndDate >= Today)
            .OrderBy(x => x.StartDate)
            .Take(5)
            .ToListAsync(cancellationToken);

        Upcoming = leaves
            .Select(x => new UpcomingItem("Leave", x.LeaveType, x.StartDate, x.EndDate))
            .Concat(seminars.Select(x => new UpcomingItem("Seminar", x.SeminarTitle, x.StartDate, x.EndDate)))
            .OrderBy(x => x.Start)
            .Take(5)
            .ToList();
    }
}
