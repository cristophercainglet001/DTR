using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
[RequestSizeLimit(6 * 1024 * 1024)]
public sealed class AttendanceImportModel : PageModel
{
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);
    private static readonly AttendanceSlot[] Slots = Enum.GetValues<AttendanceSlot>();

    private readonly DtrDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AttendanceImportModel> _logger;

    public AttendanceImportModel(DtrDbContext db, IMemoryCache cache, ILogger<AttendanceImportModel> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public AttendanceImportPlan? Plan { get; private set; }

    public string? ErrorMessage { get; private set; }

    public static AttendanceSlot[] AllSlots => Slots;

    public void OnGet()
    {
        Response.Headers.CacheControl = "no-store";
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? logFile, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (logFile is null || logFile.Length == 0)
        {
            ErrorMessage = "Choose the attendance text file to upload.";
            return Page();
        }

        if (logFile.Length > MaxFileBytes)
        {
            ErrorMessage = "The file is larger than 5 MB. Split it into smaller files.";
            return Page();
        }

        var extension = Path.GetExtension(logFile.FileName);
        if (!new[] { ".txt", ".dat", ".log", ".csv" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            ErrorMessage = "Upload a text file (.txt, .dat, .log or .csv).";
            return Page();
        }

        byte[] bytes;
        await using (var stream = logFile.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        var parsed = AttendanceLog.Parse(EmployeeCsv.Decode(bytes));
        if (parsed.FatalError is not null)
        {
            ErrorMessage = parsed.FatalError;
            return Page();
        }

        var plan = await BuildPlanAsync(parsed, Path.GetFileName(logFile.FileName), cancellationToken);
        _cache.Set(CacheKey(plan.Token), plan, PlanLifetime);
        Plan = plan;
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(
        string? token,
        Dictionary<string, string>? choices,
        CancellationToken cancellationToken)
    {
        choices ??= [];
        if (string.IsNullOrWhiteSpace(token)
            || !_cache.TryGetValue(CacheKey(token), out AttendanceImportPlan? plan)
            || plan is null
            || plan.Owner != CurrentOwner())
        {
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "The import preview expired. Upload the file again.";
            return RedirectToPage();
        }

        if (plan.Rows.Count == 0)
        {
            _cache.Remove(CacheKey(plan.Token));
            TempData["StatusMessage"] = "There was nothing new to import from that file.";
            return RedirectToPage("/Admin/Reports");
        }

        var employeeIds = plan.Rows.Select(x => x.EmployeeDbId).Distinct().ToList();
        var minDate = plan.Rows.Min(x => x.Date);
        var maxDate = plan.Rows.Max(x => x.Date);
        var records = (await _db.DtrRecords
                .Where(x => employeeIds.Contains(x.EmployeeId) && x.DtrDate >= minDate && x.DtrDate <= maxDate)
                .ToListAsync(cancellationToken))
            .ToDictionary(x => (x.EmployeeId, x.DtrDate));

        var now = DateTime.UtcNow;
        int created = 0, updated = 0, unchanged = 0;
        var skipped = new List<string>();
        var audits = new List<(DtrRecord Record, string? Old, string New)>();

        for (var index = 0; index < plan.Rows.Count; index++)
        {
            var row = plan.Rows[index];
            var picks = Slots.ToDictionary(
                s => s,
                s => row.Resolve(s, choices.GetValueOrDefault($"{index}_{s}")));

            var isNew = !records.TryGetValue((row.EmployeeDbId, row.Date), out var record);
            record ??= new DtrRecord { EmployeeId = row.EmployeeDbId, DtrDate = row.Date, EntrySource = "Biometric Import" };

            var before = Describe(record);
            var amIn = picks[AttendanceSlot.AmIn] ?? record.AmTimeIn;
            var amOut = picks[AttendanceSlot.AmOut] ?? record.AmTimeOut;
            var pmIn = picks[AttendanceSlot.PmIn] ?? record.PmTimeIn;
            var pmOut = picks[AttendanceSlot.PmOut] ?? record.PmTimeOut;

            if ((amIn is { } ai && amOut is { } ao && ao <= ai) || (pmIn is { } pi && pmOut is { } po && po <= pi))
            {
                skipped.Add($"{row.BadgeNumber} {row.Date:MMM d}");
                continue;
            }

            if (!isNew && amIn == record.AmTimeIn && amOut == record.AmTimeOut
                && pmIn == record.PmTimeIn && pmOut == record.PmTimeOut)
            {
                unchanged++;
                continue;
            }

            record.AmTimeIn = amIn;
            record.AmTimeOut = amOut;
            record.PmTimeIn = pmIn;
            record.PmTimeOut = pmOut;
            record.UpdatedAt = now;
            if (isNew)
            {
                record.CreatedAt = now;
                _db.DtrRecords.Add(record);
                created++;
            }
            else
            {
                record.Remarks = ReportsModel.MarkEdited(record.Remarks);
                updated++;
            }

            audits.Add((record, isNew ? null : before, Describe(record)));
        }

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var (record, old, current) in audits)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    Action = "AttendanceImport",
                    EntityName = "DtrRecord",
                    EntityId = record.Id.ToString(),
                    OldValues = old,
                    NewValues = current,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    CreatedAt = now,
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _logger.LogWarning(ex, "Attendance import hit an existing record.");
            _db.ChangeTracker.Clear();
            TempData["StatusTone"] = "warning";
            TempData["StatusMessage"] = "Another change added a matching record while importing, so nothing was saved. Upload the file again.";
            return RedirectToPage();
        }

        _cache.Remove(CacheKey(plan.Token));
        _logger.LogInformation("Attendance import saved: {Created} new, {Updated} updated.", created, updated);

        var message = $"Attendance import complete: {created} new record(s), {updated} updated, {unchanged} unchanged.";
        if (skipped.Count > 0)
        {
            TempData["StatusTone"] = "warning";
            message += $" {skipped.Count} day(s) were not saved because a Time Out was not later than its Time In: "
                       + string.Join(", ", skipped.Take(5)) + (skipped.Count > 5 ? ", …" : ".");
        }

        TempData["StatusMessage"] = message;
        return RedirectToPage("/Admin/Reports");
    }

    private async Task<AttendanceImportPlan> BuildPlanAsync(
        AttendanceLogResult parsed,
        string fileName,
        CancellationToken cancellationToken)
    {
        var plan = new AttendanceImportPlan
        {
            Owner = CurrentOwner(),
            FileName = fileName,
            PunchCount = parsed.Punches.Count,
            Issues = parsed.Issues,
        };

        var employees = await _db.Employees
            .AsNoTracking()
            .Select(x => new { x.Id, x.EmployeeId, x.FirstName, x.LastName })
            .ToListAsync(cancellationToken);
        var exact = employees
            .GroupBy(x => x.EmployeeId.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var numeric = employees
            .Where(x => long.TryParse(x.EmployeeId, out _))
            .GroupBy(x => long.Parse(x.EmployeeId))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<(int Id, string Badge, string Name, AttendancePunch Punch)>();
        var unmatched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var punch in parsed.Punches)
        {
            var key = punch.BadgeNumber.Trim().ToUpperInvariant();
            var employee = exact.GetValueOrDefault(key)
                ?? (long.TryParse(punch.BadgeNumber, out var number) ? numeric.GetValueOrDefault(number) : null);
            if (employee is null)
            {
                unmatched[punch.BadgeNumber] = unmatched.GetValueOrDefault(punch.BadgeNumber) + 1;
                continue;
            }

            matched.Add((employee.Id, employee.EmployeeId, $"{employee.LastName}, {employee.FirstName}", punch));
        }

        plan.UnmatchedEmployees.AddRange(unmatched.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)));
        if (matched.Count == 0)
        {
            return plan;
        }

        var employeeIds = matched.Select(x => x.Id).Distinct().ToList();
        var minDate = DateOnly.FromDateTime(matched.Min(x => x.Punch.Timestamp));
        var maxDate = DateOnly.FromDateTime(matched.Max(x => x.Punch.Timestamp));
        var existing = (await _db.DtrRecords
                .AsNoTracking()
                .Where(x => employeeIds.Contains(x.EmployeeId) && x.DtrDate >= minDate && x.DtrDate <= maxDate)
                .ToListAsync(cancellationToken))
            .ToDictionary(x => (x.EmployeeId, x.DtrDate));

        var rows = new List<AttendanceImportRow>();
        foreach (var day in matched.GroupBy(x => (x.Id, Date: DateOnly.FromDateTime(x.Punch.Timestamp))))
        {
            var first = day.First();
            var slots = AttendanceLog.Assign(day.Select(x => x.Punch));
            existing.TryGetValue((day.Key.Id, day.Key.Date), out var record);
            var row = new AttendanceImportRow
            {
                EmployeeDbId = day.Key.Id,
                BadgeNumber = first.Badge,
                EmployeeName = first.Name,
                Date = day.Key.Date,
                Existing = [record?.AmTimeIn, record?.AmTimeOut, record?.PmTimeIn, record?.PmTimeOut],
            };
            foreach (var slot in Slots)
            {
                row.Candidates[(int)slot].AddRange(slots[slot]);
            }

            if (row.HasConflict || row.HasChange)
            {
                rows.Add(row);
            }
            else
            {
                plan.AlreadyMatchingDays++;
            }
        }

        plan.Rows.AddRange(rows.OrderBy(x => x.EmployeeName).ThenBy(x => x.Date));
        return plan;
    }

    private string CurrentOwner() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private static string CacheKey(string token) => $"attendance-import:{token}";

    private static string Describe(DtrRecord record)
    {
        static string T(TimeOnly? time) => time?.ToString("HH:mm") ?? "--:--";
        return $"AM {T(record.AmTimeIn)}-{T(record.AmTimeOut)}; PM {T(record.PmTimeIn)}-{T(record.PmTimeOut)}";
    }
}
