using DepEdDTRSystem.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages;

public class DatabaseTestModel : PageModel
{
    private readonly DtrDbContext _db;

    public DatabaseTestModel(DtrDbContext db)
    {
        _db = db;
    }

    public bool IsConnected { get; private set; }

    public int SchoolCount { get; private set; }

    public int SchoolYearCount { get; private set; }

    public int EmployeeCount { get; private set; }

    public int DtrRecordCount { get; private set; }

    public int HolidayCount { get; private set; }

    public int LeaveApplicationCount { get; private set; }

    public int AuditLogCount { get; private set; }

    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        try
        {
            var connectionString = _db.Database.GetConnectionString();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                IsConnected = false;
                ErrorMessage =
                    "The database connection string 'DefaultConnection' is not configured. Add it to appsettings.json or user secrets before testing the connection.";

                return;
            }

            IsConnected = await _db.Database.CanConnectAsync();

            if (!IsConnected)
            {
                ErrorMessage =
                    "The application could not connect to the PostgreSQL database.";

                return;
            }

            SchoolCount = await _db.Schools.CountAsync();

            SchoolYearCount = await _db.SchoolYears.CountAsync();

            EmployeeCount = await _db.Employees.CountAsync();

            DtrRecordCount = await _db.DtrRecords.CountAsync();

            HolidayCount = await _db.Holidays.CountAsync();

            LeaveApplicationCount =
                await _db.LeaveApplications.CountAsync();

            AuditLogCount =
                await _db.AuditLogs.CountAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            ErrorMessage = ex.GetBaseException().Message;
        }
    }
}