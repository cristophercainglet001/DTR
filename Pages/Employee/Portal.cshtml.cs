using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Services;
using DepEdDTRSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[Authorize(AuthenticationSchemes = EmployeeAuthDefaults.Scheme, Roles = "Employee")]
public class PortalModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly DtrReportVerificationService _verification;

    public PortalModel(DtrDbContext db, DtrReportVerificationService verification)
    {
        _db = db;
        _verification = verification;
    }

    public DtrForm48ViewModel? Report { get; private set; }
    public int Month { get; private set; } = DateTime.Today.Month;
    public int Year { get; private set; } = DateTime.Today.Year;

    public async Task OnGetAsync(int? month, int? year, CancellationToken cancellationToken)
    {
        Month = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;
        Year = year is >= 2000 and <= 9998 ? year.Value : DateTime.Today.Year;

        if (!int.TryParse(User.FindFirstValue("employee_id"), out var employeeId))
        {
            throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .Include(x => x.School)
            .FirstOrDefaultAsync(x => x.Id == employeeId && x.Status == "Active", cancellationToken);
        if (employee is null)
        {
            throw new InvalidOperationException("The signed-in employee record is not active or no longer exists.");
        }

        var startDate = new DateOnly(Year, Month, 1);
        var endDate = startDate.AddMonths(1);
        var records = await _db.DtrRecords
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId
                        && x.DtrDate >= startDate
                        && x.DtrDate < endDate)
            .ToListAsync(cancellationToken);
        var approvedLeaves = await _db.LeaveApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId
                        && x.Status == "Approved"
                        && x.StartDate < endDate
                        && x.EndDate >= startDate)
            .ToListAsync(cancellationToken);
        var approvedSeminars = await _db.SeminarApplications
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId
                        && x.Status == "Approved"
                        && x.StartDate < endDate
                        && x.EndDate >= startDate)
            .ToListAsync(cancellationToken);

        Report = DtrForm48ViewModel.Create(employee, records, Month, Year, approvedLeaves, approvedSeminars);
        _verification.PrepareForVerification(Report, Request);
    }
}
