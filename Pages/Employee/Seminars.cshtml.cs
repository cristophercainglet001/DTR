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
public sealed class SeminarsModel : PageModel
{
    private readonly DtrDbContext _db;

    public SeminarsModel(DtrDbContext db) => _db = db;

    [BindProperty]
    public SeminarForm Input { get; set; } = new();

    public IReadOnlyList<SeminarApplication> Applications { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadApplicationsAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Input.EndDate < Input.StartDate)
        {
            ModelState.AddModelError("Input.EndDate", "End date must be on or after the start date.");
        }

        if (!ModelState.IsValid)
        {
            await LoadApplicationsAsync(cancellationToken);
            return Page();
        }

        if (!int.TryParse(User.FindFirstValue("employee_id"), out var employeeId))
        {
            throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");
        }

        var employeeIsActive = await _db.Employees
            .AnyAsync(x => x.Id == employeeId && x.Status == "Active", cancellationToken);
        if (!employeeIsActive)
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;
        var application = new SeminarApplication
        {
            EmployeeId = employeeId,
            SeminarTitle = Input.SeminarTitle.Trim(),
            Organizer = Input.Organizer.Trim(),
            StartDate = Input.StartDate,
            EndDate = Input.EndDate,
            Venue = Input.Venue.Trim(),
            Purpose = Input.Purpose.Trim(),
            Status = "Pending",
            FiledAt = now,
            UpdatedAt = now,
        };
        _db.SeminarApplications.Add(application);
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "Submit",
            EntityName = "SeminarApplication",
            EntityId = application.Id.ToString(),
            ActorEmployeeId = employeeId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Your seminar information was submitted to the administrator for validation.";
        return RedirectToPage();
    }

    private async Task LoadApplicationsAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("employee_id"), out var employeeId))
        {
            throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");
        }

        Applications = await _db.SeminarApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .OrderByDescending(x => x.FiledAt)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public sealed class SeminarForm
    {
        [Required, StringLength(200)]
        public string SeminarTitle { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string Organizer { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [DataType(DataType.Date)]
        public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [Required, StringLength(300)]
        public string Venue { get; set; } = string.Empty;

        [Required, StringLength(1000)]
        public string Purpose { get; set; } = string.Empty;
    }
}
