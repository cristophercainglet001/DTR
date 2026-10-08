using System.Text.Json;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
public class LeavesModel : PageModel
{
    private readonly DtrDbContext _db;

    public LeavesModel(DtrDbContext db) => _db = db;

    public string StatusFilter { get; private set; } = "Pending";
    public IReadOnlyList<LeaveApplication> Applications { get; private set; } = [];
    public int PendingCount { get; private set; }

    public async Task OnGetAsync(string? status, CancellationToken cancellationToken)
    {
        StatusFilter = NormalizeStatus(status);
        PendingCount = await _db.LeaveApplications.CountAsync(x => x.Status == "Pending", cancellationToken);

        var query = _db.LeaveApplications
            .AsNoTracking()
            .Include(x => x.Employee)
                .ThenInclude(x => x!.School)
            .AsQueryable();
        if (StatusFilter != "All")
        {
            query = query.Where(x => x.Status == StatusFilter);
        }
        Applications = await query
            .OrderBy(x => x.Status == "Pending" ? 0 : 1)
            .ThenByDescending(x => x.FiledAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostReviewAsync(
        int id,
        string? decision,
        string? remarks,
        string? status,
        CancellationToken cancellationToken)
    {
        StatusFilter = NormalizeStatus(status);
        if (decision is not ("Approved" or "Rejected"))
        {
            TempData["StatusMessage"] = "Choose approve or reject for this request.";
            return RedirectToPage(new { status = StatusFilter });
        }
        if (remarks?.Length > 1000)
        {
            TempData["StatusMessage"] = "Remarks must be no longer than 1,000 characters.";
            return RedirectToPage(new { status = StatusFilter });
        }

        var now = DateTime.UtcNow;
        var normalizedRemarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _db.LeaveApplications
            .Where(x => x.Id == id && x.Status == "Pending")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, decision)
                    .SetProperty(x => x.Remarks, normalizedRemarks)
                    .SetProperty(x => x.ReviewedAt, (DateTime?)now)
                    .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            TempData["StatusMessage"] = "This request was not found or has already been reviewed.";
            return RedirectToPage(new { status = StatusFilter });
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Action = decision,
            EntityName = "LeaveApplication",
            EntityId = id.ToString(),
            NewValues = JsonSerializer.Serialize(new
            {
                Status = decision,
                Remarks = normalizedRemarks,
                ReviewedBy = User.Identity?.Name,
            }),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = $"Leave request {decision.ToLowerInvariant()}.";
        return RedirectToPage(new { status = StatusFilter });
    }

    private static string NormalizeStatus(string? status) =>
        status is "Approved" or "Rejected" or "Pending" ? status : "All";
}
