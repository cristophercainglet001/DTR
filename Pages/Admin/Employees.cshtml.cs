using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EmployeeRecord = DepEdDTRSystem.Models.Employee;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
[RequestSizeLimit(2 * 1024 * 1024)]
public class EmployeesModel : PageModel
{
    private const int PageSize = 10;
    private const int MaxImportFileBytes = 1024 * 1024;
    public const string OtherOptionValue = "__Other";

    public static readonly string[] StatusOptions = ["Active", "Inactive", "On Leave"];
    public static readonly string[] TeachingPositionOptions =
    [
        "Teacher I", "Teacher II", "Teacher III", "Teacher IV", "Teacher V", "Teacher VI", "Teacher VII",
        "Master Teacher I", "Master Teacher II", "Master Teacher III", "Master Teacher IV", "Master Teacher V",
        "Head Teacher I", "Head Teacher II", "Head Teacher III", "Head Teacher IV", "Head Teacher V", "Head Teacher VI",
        "Teacher-in-Charge", "Kindergarten Teacher", "SPED Teacher", "ALS Teacher",
    ];
    public static readonly string[] NonTeachingPositionOptions =
    [
        "Principal", "Principal I", "Principal II", "Principal III", "Principal IV",
        "Administrative Aide I", "Administrative Aide II", "Administrative Aide III",
        "Administrative Aide IV", "Administrative Aide V", "Administrative Aide VI",
        "Administrative Assistant I", "Administrative Assistant II",
        "Administrative Officer I", "Administrative Officer II", "Administrative Officer III", "Administrative Officer IV",
        "Accountant I", "Accountant II", "Accountant III", "Bookkeeper", "Clerk",
        "Guidance Counselor", "Librarian", "Nurse", "Registrar", "School Property Custodian",
        "Utility Worker", "Driver",
    ];
    public static readonly string[] DepartmentOptions =
    [
        "Teaching", "Elementary", "Junior High School", "Senior High School", "Kindergarten",
        "Special Education", "Alternative Learning System", "Administration", "School Office",
        "Guidance and Counseling", "School Health Services", "Library", "ICT", "Property and Supply",
    ];

    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<EmployeeAccount> _passwordHasher;
    private readonly ILogger<EmployeesModel> _logger;

    public EmployeesModel(
        DtrDbContext db,
        IPasswordHasher<EmployeeAccount> passwordHasher,
        ILogger<EmployeesModel> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [BindProperty]
    public EmployeeForm Input { get; set; } = new();

    public IReadOnlyList<EmployeeRecord> Employees { get; private set; } = [];

    public IReadOnlyList<SelectListItem> Schools { get; private set; } = [];

    public string SearchTerm { get; private set; } = string.Empty;

    public string StatusFilter { get; private set; } = string.Empty;

    public int PageNumber { get; private set; } = 1;

    public int TotalPages { get; private set; } = 1;

    public int TotalCount { get; private set; }

    public IReadOnlyDictionary<int, bool> AccountReady { get; private set; } = new Dictionary<int, bool>();

    public bool IsEditing => Input.Id > 0;

    public bool ShowFormModal { get; private set; }

    public EmployeeImportSummary? ImportSummary { get; private set; }

    public sealed class EmployeeImportSummary
    {
        public string? FatalError { get; set; }

        public int Imported { get; set; }

        public List<EmployeeImportIssue> Errors { get; } = [];

        public List<EmployeeImportIssue> Skipped { get; } = [];

        public List<EmployeeImportIssue> Warnings { get; } = [];
    }

    public IActionResult OnGetTemplate() =>
        File(EmployeeCsv.BuildTemplate(), "text/csv; charset=utf-8", "employee-import-template.csv");

    public async Task<IActionResult> OnPostImportAsync(
        IFormFile? csvFile,
        int importSchoolId,
        CancellationToken cancellationToken)
    {
        ModelState.Clear();
        var summary = new EmployeeImportSummary();
        ImportSummary = summary;

        async Task<IActionResult> RenderAsync()
        {
            await LoadPageAsync(null, null, 1);
            return Page();
        }

        if (csvFile is null || csvFile.Length == 0)
        {
            summary.FatalError = "Choose a CSV file to import.";
            return await RenderAsync();
        }

        if (csvFile.Length > MaxImportFileBytes)
        {
            summary.FatalError = "The file is larger than 1 MB. Split it into smaller files.";
            return await RenderAsync();
        }

        if (!string.Equals(Path.GetExtension(csvFile.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            summary.FatalError = "Upload a .csv file (use Save As → CSV in Excel).";
            return await RenderAsync();
        }

        if (!await _db.Schools.AnyAsync(x => x.Id == importSchoolId, cancellationToken))
        {
            summary.FatalError = "Select an existing school.";
            return await RenderAsync();
        }

        byte[] bytes;
        await using (var stream = csvFile.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        var parsed = EmployeeCsv.Parse(EmployeeCsv.Decode(bytes));
        if (parsed.FatalError is not null)
        {
            summary.FatalError = parsed.FatalError;
            return await RenderAsync();
        }

        summary.Errors.AddRange(parsed.Errors);

        var upperIds = parsed.Rows.Select(x => x.BadgeNumber.ToUpper()).ToList();
        var existingIds = (await _db.Employees
                .AsNoTracking()
                .Where(x => upperIds.Contains(x.EmployeeId.ToUpper()))
                .Select(x => x.EmployeeId)
                .ToListAsync(cancellationToken))
            .Select(x => x.ToUpperInvariant())
            .ToHashSet();

        var now = DateTime.UtcNow;
        var newEmployees = new List<EmployeeRecord>();
        foreach (var row in parsed.Rows)
        {
            if (existingIds.Contains(row.BadgeNumber.ToUpperInvariant()))
            {
                summary.Skipped.Add(new EmployeeImportIssue(
                    row.RowNumber, row.BadgeNumber, "A record with this Badgenumber already exists."));
                continue;
            }

            newEmployees.Add(new EmployeeRecord
            {
                SchoolId = importSchoolId,
                EmployeeId = row.BadgeNumber,
                FirstName = row.FirstName,
                MiddleName = row.MiddleName,
                LastName = row.LastName,
                Suffix = row.Suffix,
                Position = NormalizePosition(row.Position),
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now,
            });

            if (row.Warning is not null)
            {
                summary.Warnings.Add(new EmployeeImportIssue(row.RowNumber, row.BadgeNumber, row.Warning));
            }
        }

        if (newEmployees.Count > 0)
        {
            try
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                _db.Employees.AddRange(newEmployees);
                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "BulkImport",
                    EntityName = "Employee",
                    NewValues = $"Imported {newEmployees.Count} employees from CSV.",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    CreatedAt = now,
                });
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Employee CSV import failed.");
                _db.ChangeTracker.Clear();
                summary.FatalError = "The import could not be saved, so nothing was added. Another change may have used the same Badgenumber; please try again.";
                summary.Warnings.Clear();
                return await RenderAsync();
            }

            summary.Imported = newEmployees.Count;
            _logger.LogInformation("Imported {Count} employees from CSV.", newEmployees.Count);
        }

        return await RenderAsync();
    }

    private static string NormalizePosition(string position) =>
        TeachingPositionOptions.Concat(NonTeachingPositionOptions)
            .FirstOrDefault(x => string.Equals(x, position, StringComparison.OrdinalIgnoreCase))
        ?? position;

    public async Task OnGetAsync(
        string? search,
        string? status,
        int pageNumber = 1,
        int? editId = null)
    {
        if (editId is > 0)
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == editId.Value);

            if (employee is null)
            {
                TempData["StatusTone"] = "warning";
                TempData["StatusMessage"] = "That employee record could not be found.";
            }
            else
            {
                Input = EmployeeForm.FromEmployee(employee);
                ShowFormModal = true;
            }
        }

        await LoadPageAsync(search, status, pageNumber);
    }

    public async Task<IActionResult> OnPostSaveAsync(
        string? search,
        string? status,
        int pageNumber = 1)
    {
        SearchTerm = search?.Trim() ?? string.Empty;
        StatusFilter = NormalizeStatusFilter(status);
        PageNumber = Math.Max(1, pageNumber);

        if (Input.Position == OtherOptionValue)
        {
            Input.Position = NormalizeOptional(Input.PositionOther) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(Input.Position))
            {
                ModelState.AddModelError("Input.PositionOther", "Enter the employee’s position.");
            }
        }
        else if (!TeachingPositionOptions.Concat(NonTeachingPositionOptions)
                     .Contains(Input.Position, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Input.Position", "Choose a position from the list or select Other.");
        }

        if (Input.Department == OtherOptionValue)
        {
            Input.Department = NormalizeOptional(Input.DepartmentOther);
            if (Input.Department is null)
            {
                ModelState.AddModelError("Input.DepartmentOther", "Enter the employee’s department.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(Input.Department)
                 && !DepartmentOptions.Contains(Input.Department, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Input.Department", "Choose a department from the list or select Other.");
        }

        if (!StatusOptions.Contains(Input.Status, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Input.Status", "Choose a valid employee status.");
        }

        if (ModelState.IsValid)
        {
            var normalizedEmployeeId = Input.EmployeeId.Trim();
            var employeeIdExists = await _db.Employees.AnyAsync(x =>
                x.EmployeeId.ToUpper() == normalizedEmployeeId.ToUpper()
                && x.Id != Input.Id);

            if (employeeIdExists)
            {
                ModelState.AddModelError(
                    "Input.EmployeeId",
                    "That employee ID is already in use.");
            }
        }

        if (ModelState.IsValid)
        {
            var schoolExists = await _db.Schools.AnyAsync(x => x.Id == Input.SchoolId);

            if (!schoolExists)
            {
                ModelState.AddModelError("Input.SchoolId", "Select an existing school.");
            }
        }

        if (ModelState.IsValid && !string.IsNullOrWhiteSpace(Input.Email))
        {
            var normalizedEmail = Input.Email.Trim().ToUpperInvariant();
            var accountEmailExists = await _db.EmployeeAccounts.AnyAsync(x =>
                x.NormalizedEmail == normalizedEmail
                && x.EmployeeId != Input.Id);
            if (accountEmailExists)
            {
                ModelState.AddModelError("Input.Email", "That email address is already used by another staff account.");
            }
        }

        if (ModelState.IsValid
            && Input.Id > 0
            && string.IsNullOrWhiteSpace(Input.Email)
            && await _db.EmployeeAccounts.AnyAsync(x => x.EmployeeId == Input.Id))
        {
            ModelState.AddModelError("Input.Email", "An employee email is required while a staff portal account exists.");
        }

        if (!ModelState.IsValid)
        {
            ShowFormModal = true;
            await LoadPageAsync(SearchTerm, StatusFilter, PageNumber);
            return Page();
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var isNewEmployee = Input.Id == 0;
        var employee = isNewEmployee
            ? new EmployeeRecord()
            : await _db.Employees.FirstOrDefaultAsync(x => x.Id == Input.Id);

        if (employee is null)
        {
            await transaction.RollbackAsync();
            TempData["StatusMessage"] = "That employee record no longer exists.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        var previousEmail = employee.Email;
        var previousStatus = employee.Status;
        employee.SchoolId = Input.SchoolId;
        employee.EmployeeId = Input.EmployeeId.Trim();
        employee.FirstName = Input.FirstName.Trim();
        employee.MiddleName = NormalizeOptional(Input.MiddleName);
        employee.LastName = Input.LastName.Trim();
        employee.Suffix = NormalizeOptional(Input.Suffix);
        employee.Position = Input.Position.Trim();
        employee.Department = NormalizeOptional(Input.Department);
        employee.Email = NormalizeOptional(Input.Email);
        employee.Status = Input.Status;
        employee.DateHired = Input.DateHired;
        employee.UpdatedAt = DateTime.UtcNow;

        var employeeAccount = await _db.EmployeeAccounts
            .FirstOrDefaultAsync(x => x.EmployeeId == employee.Id);
        var emailChanged = !string.Equals(previousEmail, employee.Email, StringComparison.OrdinalIgnoreCase);
        var statusChanged = !string.Equals(previousStatus, employee.Status, StringComparison.Ordinal);
        if (employeeAccount is not null && emailChanged)
        {
            employeeAccount.Email = employee.Email!;
            employeeAccount.NormalizedEmail = employeeAccount.Email.ToUpperInvariant();
            if (employeeAccount.PasswordHash is null)
            {
                employeeAccount.InvitationTokenHash = null;
                employeeAccount.InvitationExpiresAt = null;
            }
            employeeAccount.UpdatedAt = DateTime.UtcNow;
        }
        if (employeeAccount is not null && (emailChanged || statusChanged))
        {
            employeeAccount.SecurityStamp = Guid.NewGuid().ToString("N");
            employeeAccount.UpdatedAt = DateTime.UtcNow;
        }

        if (isNewEmployee)
        {
            employee.CreatedAt = DateTime.UtcNow;
            _db.Employees.Add(employee);
        }

        await _db.SaveChangesAsync();

        _db.AuditLogs.Add(new AuditLog
        {
            Action = isNewEmployee ? "Create" : "Update",
            EntityName = "Employee",
            EntityId = employee.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["StatusMessage"] = isNewEmployee
            ? "Employee added successfully."
            : "Employee details updated successfully.";

        return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostDeleteAsync(
        int employeeId,
        string? search,
        string? status,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        SearchTerm = search?.Trim() ?? string.Empty;
        StatusFilter = NormalizeStatusFilter(status);
        PageNumber = Math.Max(1, pageNumber);

        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "That employee record no longer exists.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        var displayName = $"{employee.FirstName} {employee.LastName}";
        var badge = employee.EmployeeId;

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Keep other people's reviewed leave requests; only drop the link to this reviewer.
            await _db.LeaveApplications
                .Where(x => x.ReviewedByEmployeeId == employeeId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReviewedByEmployeeId, (int?)null), cancellationToken);

            _db.Employees.Remove(employee);
            _db.AuditLogs.Add(new AuditLog
            {
                Action = "Delete",
                EntityName = "Employee",
                EntityId = employeeId.ToString(),
                OldValues = $"{badge} - {displayName} ({employee.Position})",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Employee {EmployeeId} could not be deleted.", employeeId);
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "That employee could not be deleted because other records still depend on it. Set the employee to Inactive instead.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        _logger.LogInformation("Employee {EmployeeId} deleted.", employeeId);
        TempData["StatusMessage"] = $"{displayName} (Badgenumber {badge}) was deleted.";
        return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostInviteAsync(
        int employeeId,
        string? search,
        string? status,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        SearchTerm = search?.Trim() ?? string.Empty;
        StatusFilter = NormalizeStatusFilter(status);
        PageNumber = Math.Max(1, pageNumber);

        var employee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            TempData["StatusMessage"] = "That employee record could not be found.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }
        if (!string.Equals(employee.Status, "Active", StringComparison.Ordinal))
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "Only active employees can be invited to the staff portal.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }
        if (string.IsNullOrWhiteSpace(employee.Email))
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "Add an email address to this employee record before sending an invitation.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        var normalizedEmail = employee.Email.Trim().ToUpperInvariant();
        var account = await _db.EmployeeAccounts
            .FirstOrDefaultAsync(x => x.EmployeeId == employee.Id, cancellationToken);
        if (account?.PasswordHash is not null)
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "This employee already activated their staff account.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        if (await _db.EmployeeAccounts.AnyAsync(
                x => x.NormalizedEmail == normalizedEmail && x.EmployeeId != employee.Id,
                cancellationToken))
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "Another staff account already uses this email address.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        var token = AccountTokens.Create();
        account ??= new EmployeeAccount
        {
            EmployeeId = employee.Id,
            CreatedAt = DateTime.UtcNow,
        };
        account.Email = employee.Email.Trim();
        account.NormalizedEmail = normalizedEmail;
        account.InvitationTokenHash = AccountTokens.Hash(token);
        account.InvitationExpiresAt = DateTime.UtcNow.AddHours(48);
        account.UpdatedAt = DateTime.UtcNow;

        if (_db.Entry(account).State == EntityState.Detached)
        {
            _db.EmployeeAccounts.Add(account);
        }

        var invitationPath = Url.Page(
            "/Employee/AcceptInvite",
            pageHandler: null,
            values: new { token });
        if (string.IsNullOrWhiteSpace(invitationPath))
        {
            throw new InvalidOperationException("The staff invitation URL could not be generated.");
        }

        await _db.SaveChangesAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "Invite",
            EntityName = "EmployeeAccount",
            EntityId = employee.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = $"A staff portal invitation link was generated for {account.Email}. Copy it below and send it to the employee.";
        TempData["InvitationPath"] = invitationPath;
        _logger.LogInformation("Staff portal invitation link generated for employee {EmployeeId}.", employee.Id);
        return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(
        int employeeId,
        string? search,
        string? status,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        SearchTerm = search?.Trim() ?? string.Empty;
        StatusFilter = NormalizeStatusFilter(status);
        PageNumber = Math.Max(1, pageNumber);

        var account = await _db.EmployeeAccounts
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);
        if (account?.Employee is null
            || account.PasswordHash is null
            || !string.Equals(account.Employee.Status, "Active", StringComparison.Ordinal))
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "Only active employees with an activated staff account can have their password reset.";
            return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
        }

        var now = DateTime.UtcNow;
        account.PasswordHash = _passwordHasher.HashPassword(account, EmployeeAuthDefaults.TemporaryPassword);
        account.MustChangePassword = true;
        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        account.InvitationTokenHash = null;
        account.InvitationExpiresAt = null;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "PasswordReset",
            EntityName = "EmployeeAccount",
            EntityId = account.EmployeeId.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = $"Temporary password for {account.Email} is deped123. The employee must change it at next sign-in.";
        _logger.LogInformation("Temporary password reset for employee {EmployeeId}.", account.EmployeeId);
        return RedirectToPage(new { search = SearchTerm, status = StatusFilter, pageNumber = PageNumber });
    }

    private async Task LoadPageAsync(string? search, string? status, int pageNumber)
    {
        SearchTerm = search?.Trim() ?? string.Empty;
        StatusFilter = NormalizeStatusFilter(status);

        var query = _db.Employees
            .AsNoTracking()
            .Include(x => x.School)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var searchTerm = SearchTerm.ToLower();
            query = query.Where(x =>
                x.EmployeeId.ToLower().Contains(searchTerm)
                || x.FirstName.ToLower().Contains(searchTerm)
                || x.MiddleName != null && x.MiddleName.ToLower().Contains(searchTerm)
                || x.LastName.ToLower().Contains(searchTerm)
                || x.Position.ToLower().Contains(searchTerm)
                || x.Department != null && x.Department.ToLower().Contains(searchTerm)
                || x.School != null && x.School.SchoolName.ToLower().Contains(searchTerm));
        }

        if (!string.IsNullOrEmpty(StatusFilter))
        {
            query = query.Where(x => x.Status == StatusFilter);
        }

        TotalCount = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNumber = Math.Clamp(pageNumber, 1, TotalPages);

        Employees = await query
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.EmployeeId)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var employeeIds = Employees.Select(x => x.Id).ToArray();
        AccountReady = await _db.EmployeeAccounts
            .AsNoTracking()
            .Where(x => employeeIds.Contains(x.EmployeeId))
            .ToDictionaryAsync(x => x.EmployeeId, x => x.PasswordHash != null);

        Schools = await _db.Schools
            .AsNoTracking()
            .OrderBy(x => x.SchoolName)
            .Select(x => new SelectListItem(
                $"{x.SchoolName} ({x.SchoolId})",
                x.Id.ToString()))
            .ToListAsync();
    }

    private static string NormalizeStatusFilter(string? status) =>
        StatusOptions.Contains(status, StringComparer.Ordinal) ? status! : string.Empty;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class EmployeeForm
    {
        public int Id { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Select a school.")]
        public int SchoolId { get; set; }

        [Required]
        [StringLength(50)]
        public string EmployeeId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? MiddleName { get; set; }

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Suffix { get; set; }

        [Required]
        [StringLength(150)]
        public string Position { get; set; } = string.Empty;

        [StringLength(150)]
        public string? PositionOther { get; set; }

        [StringLength(150)]
        public string? Department { get; set; }

        [StringLength(150)]
        public string? DepartmentOther { get; set; }

        [EmailAddress]
        [StringLength(200)]
        public string? Email { get; set; }

        [Required]
        public string Status { get; set; } = "Active";

        [DataType(DataType.Date)]
        public DateOnly? DateHired { get; set; }

        public static EmployeeForm FromEmployee(EmployeeRecord employee)
        {
            var knownPositions = TeachingPositionOptions.Concat(NonTeachingPositionOptions);
            var knownPosition = knownPositions.Contains(employee.Position, StringComparer.Ordinal);
            var knownDepartment = DepartmentOptions.Contains(employee.Department ?? string.Empty, StringComparer.Ordinal);

            return new EmployeeForm
            {
                Id = employee.Id,
                SchoolId = employee.SchoolId,
                EmployeeId = employee.EmployeeId,
                FirstName = employee.FirstName,
                MiddleName = employee.MiddleName,
                LastName = employee.LastName,
                Suffix = employee.Suffix,
                Position = knownPosition ? employee.Position : OtherOptionValue,
                PositionOther = knownPosition ? null : employee.Position,
                Department = knownDepartment ? employee.Department : string.IsNullOrWhiteSpace(employee.Department) ? null : OtherOptionValue,
                DepartmentOther = knownDepartment ? null : employee.Department,
                Email = employee.Email,
                Status = employee.Status,
                DateHired = employee.DateHired,
            };
        }
    }
}
