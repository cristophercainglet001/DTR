using System.ComponentModel.DataAnnotations;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
public class DtrModel : PageModel
{
    private const int PageSize = 20;

    private readonly DtrDbContext _db;

    public DtrModel(DtrDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public DtrForm Input { get; set; } = new();

    public IReadOnlyList<DtrRecord> Records { get; private set; } = [];

    public IReadOnlyList<SelectListItem> EmployeeOptions { get; private set; } = [];

    public DateOnly FilterDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    public int? EmployeeFilter { get; private set; }

    public int PageNumber { get; private set; } = 1;

    public int TotalPages { get; private set; } = 1;

    public int TotalCount { get; private set; }

    public int CompleteDayCount { get; private set; }

    public int UndertimeRecordCount { get; private set; }

    public bool IsEditing => Input.Id > 0;

    public bool ShowFormModal { get; private set; }

    public async Task OnGetAsync(
        DateOnly? dateFilter,
        int? employeeFilter,
        int pageNumber = 1,
        int? editId = null)
    {
        Input.DtrDate = dateFilter ?? DateOnly.FromDateTime(DateTime.Today);

        if (editId is > 0)
        {
            var record = await _db.DtrRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == editId.Value);

            if (record is null)
            {
                TempData["StatusMessage"] = "That DTR record could not be found.";
            }
            else
            {
                Input = DtrForm.FromRecord(record);
                ShowFormModal = true;
            }
        }

        await LoadPageAsync(dateFilter, employeeFilter, pageNumber);
    }

    public async Task<IActionResult> OnPostSaveAsync(
        DateOnly? dateFilter,
        int? employeeFilter,
        int pageNumber = 1)
    {
        FilterDate = dateFilter ?? DateOnly.FromDateTime(DateTime.Today);
        EmployeeFilter = employeeFilter;
        PageNumber = Math.Max(1, pageNumber);

        ValidateTimeRange(Input.AmTimeIn, Input.AmTimeOut, "Input.AmTimeOut", "Morning time out must be later than morning time in.");
        ValidateTimeRange(Input.PmTimeIn, Input.PmTimeOut, "Input.PmTimeOut", "Afternoon time out must be later than afternoon time in.");

        if (ModelState.IsValid)
        {
            var employeeExists = await _db.Employees.AnyAsync(x => x.Id == Input.EmployeeId);
            if (!employeeExists)
            {
                ModelState.AddModelError("Input.EmployeeId", "Select an existing employee.");
            }
        }

        if (ModelState.IsValid)
        {
            var recordExists = await _db.DtrRecords.AnyAsync(x =>
                x.EmployeeId == Input.EmployeeId
                && x.DtrDate == Input.DtrDate
                && x.Id != Input.Id);

            if (recordExists)
            {
                ModelState.AddModelError(
                    "Input.EmployeeId",
                    "A DTR record already exists for this employee and date.");
            }
        }

        if (!ModelState.IsValid)
        {
            ShowFormModal = true;
            await LoadPageAsync(FilterDate, EmployeeFilter, PageNumber);
            return Page();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var isNewRecord = Input.Id == 0;
        var record = isNewRecord
            ? new DtrRecord()
            : await _db.DtrRecords.FirstOrDefaultAsync(x => x.Id == Input.Id);

        if (record is null)
        {
            await transaction.RollbackAsync();
            TempData["StatusMessage"] = "That DTR record no longer exists.";
            return RedirectToPage(new
            {
                dateFilter = FilterDate.ToString("yyyy-MM-dd"),
                employeeFilter = EmployeeFilter,
                pageNumber = PageNumber,
            });
        }

        record.EmployeeId = Input.EmployeeId;
        record.DtrDate = Input.DtrDate;
        record.AmTimeIn = Input.AmTimeIn;
        record.AmTimeOut = Input.AmTimeOut;
        record.PmTimeIn = Input.PmTimeIn;
        record.PmTimeOut = Input.PmTimeOut;
        record.UndertimeMinutes = Input.UndertimeMinutes;
        record.Remarks = string.IsNullOrWhiteSpace(Input.Remarks) ? null : Input.Remarks.Trim();
        record.EntrySource = "Manual";
        record.UpdatedAt = DateTime.UtcNow;

        if (isNewRecord)
        {
            record.CreatedAt = DateTime.UtcNow;
            _db.DtrRecords.Add(record);
        }

        try
        {
            await _db.SaveChangesAsync();

            _db.AuditLogs.Add(new AuditLog
            {
                Action = isNewRecord ? "Create" : "Update",
                EntityName = "DtrRecord",
                EntityId = record.Id.ToString(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        })
        {
            await transaction.RollbackAsync();
            _db.Entry(record).State = isNewRecord ? EntityState.Detached : EntityState.Unchanged;
            ModelState.AddModelError(
                "Input.EmployeeId",
                "A DTR record already exists for this employee and date.");
            ShowFormModal = true;
            await LoadPageAsync(FilterDate, EmployeeFilter, PageNumber);
            return Page();
        }

        TempData["StatusMessage"] = isNewRecord
            ? "Attendance record saved successfully."
            : "DTR record updated successfully.";

        return RedirectToPage(new
        {
            dateFilter = Input.DtrDate.ToString("yyyy-MM-dd"),
            employeeFilter = EmployeeFilter,
            pageNumber = PageNumber,
        });
    }

    public string FormatTime(TimeOnly? time) =>
        time?.ToString("h:mm tt") ?? "—";

    private void ValidateTimeRange(TimeOnly? timeIn, TimeOnly? timeOut, string key, string message)
    {
        if (timeIn.HasValue && timeOut.HasValue && timeOut.Value <= timeIn.Value)
        {
            ModelState.AddModelError(key, message);
        }
    }

    private async Task LoadPageAsync(DateOnly? dateFilter, int? employeeFilter, int pageNumber)
    {
        FilterDate = dateFilter ?? DateOnly.FromDateTime(DateTime.Today);
        EmployeeFilter = employeeFilter;

        var query = _db.DtrRecords
            .AsNoTracking()
            .Include(x => x.Employee)
                .ThenInclude(x => x!.School)
            .Where(x => x.DtrDate == FilterDate);

        if (EmployeeFilter is > 0)
        {
            query = query.Where(x => x.EmployeeId == EmployeeFilter.Value);
        }

        TotalCount = await query.CountAsync();
        CompleteDayCount = await query.CountAsync(x =>
            x.AmTimeIn != null
            && x.AmTimeOut != null
            && x.PmTimeIn != null
            && x.PmTimeOut != null);
        UndertimeRecordCount = await query.CountAsync(x => x.UndertimeMinutes > 0);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNumber = Math.Clamp(pageNumber, 1, TotalPages);

        Records = await query
            .OrderBy(x => x.Employee!.LastName)
            .ThenBy(x => x.Employee!.FirstName)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        EmployeeOptions = await _db.Employees
            .AsNoTracking()
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .Select(x => new SelectListItem(
                $"{x.LastName}, {x.FirstName} ({x.EmployeeId})",
                x.Id.ToString(),
                x.Id == EmployeeFilter))
            .ToListAsync();
    }

    public sealed class DtrForm
    {
        public int Id { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Select an employee.")]
        public int EmployeeId { get; set; }

        [DataType(DataType.Date)]
        public DateOnly DtrDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

        [DataType(DataType.Time)]
        public TimeOnly? AmTimeIn { get; set; }

        [DataType(DataType.Time)]
        public TimeOnly? AmTimeOut { get; set; }

        [DataType(DataType.Time)]
        public TimeOnly? PmTimeIn { get; set; }

        [DataType(DataType.Time)]
        public TimeOnly? PmTimeOut { get; set; }

        [Range(0, 1440)]
        public int UndertimeMinutes { get; set; }

        [StringLength(500)]
        public string? Remarks { get; set; }

        public static DtrForm FromRecord(DtrRecord record) => new()
        {
            Id = record.Id,
            EmployeeId = record.EmployeeId,
            DtrDate = record.DtrDate,
            AmTimeIn = record.AmTimeIn,
            AmTimeOut = record.AmTimeOut,
            PmTimeIn = record.PmTimeIn,
            PmTimeOut = record.PmTimeOut,
            UndertimeMinutes = record.UndertimeMinutes,
            Remarks = record.Remarks,
        };
    }
}
