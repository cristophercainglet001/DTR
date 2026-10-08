using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[Authorize(AuthenticationSchemes = EmployeeAuthDefaults.Scheme, Roles = "Employee")]
public class LeaveModel : PageModel
{
    public static readonly string[] LeaveTypes =
    [
        "Vacation Leave",
        "Sick Leave",
        "Emergency Leave",
        "Maternity Leave",
        "Paternity Leave",
        "Special Privilege Leave",
        "Other",
    ];

    private readonly DtrDbContext _db;

    public LeaveModel(DtrDbContext db) => _db = db;

    [BindProperty]
    public LeaveForm Input { get; set; } = new();

    public IReadOnlyList<LeaveApplication> Applications { get; private set; } = [];

    public IReadOnlyList<DateOnly> SchoolHolidayDates { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadApplicationsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("employee_id"), out var employeeId))
        {
            throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");
        }

        if (Input.EndDate < Input.StartDate)
        {
            ModelState.AddModelError("Input.EndDate", "End date must be on or after the start date.");
        }
        if (!LeaveTypes.Contains(Input.LeaveType, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Input.LeaveType", "Choose a valid leave type.");
        }
        if (!ModelState.IsValid)
        {
            await LoadApplicationsAsync(cancellationToken);
            return Page();
        }

        var schoolId = await _db.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId && x.Status == "Active")
            .Select(x => (int?)x.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);
        if (schoolId is null)
        {
            return Forbid();
        }

        var holidayDates = await _db.Holidays
            .AsNoTracking()
            .Where(x => x.SchoolId == schoolId.Value
                        && x.IsActive
                        && x.HolidayDate >= Input.StartDate
                        && x.HolidayDate <= Input.EndDate)
            .Select(x => x.HolidayDate)
            .ToListAsync(cancellationToken);
        var workingDays = CountWorkingDays(Input.StartDate, Input.EndDate, holidayDates);
        if (workingDays == 0)
        {
            ModelState.AddModelError("Input.NumberOfDays", "The selected dates contain no working days after weekends and school holidays are excluded.");
        }
        else if (decimal.Ceiling(Input.NumberOfDays) != workingDays)
        {
            ModelState.AddModelError(
                "Input.NumberOfDays",
                $"The selected dates contain {workingDays} working day(s). Update the number of leave days or the date range.");
        }

        if (!ModelState.IsValid)
        {
            await LoadApplicationsAsync(cancellationToken);
            return Page();
        }

        var application = new LeaveApplication
        {
            EmployeeId = employeeId,
            LeaveType = Input.LeaveType.Trim(),
            StartDate = Input.StartDate,
            EndDate = Input.EndDate,
            NumberOfDays = Input.NumberOfDays,
            Reason = Input.Reason.Trim(),
            Status = "Pending",
            FiledAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.LeaveApplications.Add(application);
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "Submit",
            EntityName = "LeaveApplication",
            EntityId = application.Id.ToString(),
            ActorEmployeeId = employeeId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Your leave request was sent to the administrator for review.";
        return RedirectToPage();
    }

    private async Task LoadApplicationsAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("employee_id"), out var employeeId))
        {
            throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");
        }

        Applications = await _db.LeaveApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.FiledAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var schoolId = await _db.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => (int?)x.SchoolId)
            .FirstOrDefaultAsync(cancellationToken);
        if (schoolId is null)
        {
            throw new InvalidOperationException("The signed-in employee record no longer exists.");
        }

        SchoolHolidayDates = await _db.Holidays
            .AsNoTracking()
            .Where(x => x.SchoolId == schoolId.Value && x.IsActive)
            .Select(x => x.HolidayDate)
            .ToListAsync(cancellationToken);
    }

    private static int CountWorkingDays(DateOnly startDate, DateOnly endDate, IReadOnlyCollection<DateOnly> holidayDates)
    {
        var totalDays = endDate.DayNumber - startDate.DayNumber + 1;
        if (totalDays <= 0)
        {
            return 0;
        }

        var fullWeeks = totalDays / 7;
        var workingDays = fullWeeks * 5;
        var remainingDays = totalDays % 7;
        for (var offset = 0; offset < remainingDays; offset++)
        {
            var weekday = ((int)startDate.DayOfWeek + offset) % 7;
            if (weekday is not (0 or 6))
            {
                workingDays++;
            }
        }

        workingDays -= holidayDates
            .Where(date => date >= startDate
                           && date <= endDate
                           && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Distinct()
            .Count();
        return workingDays;
    }

    public sealed class LeaveForm
    {
        [Required, StringLength(100)]
        public string LeaveType { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [DataType(DataType.Date)]
        public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Range(typeof(decimal), "0.5", "365")]
        public decimal NumberOfDays { get; set; } = 1;

        [Required, StringLength(1000)]
        public string Reason { get; set; } = string.Empty;
    }
}
