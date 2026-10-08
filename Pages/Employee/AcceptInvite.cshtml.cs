using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public class AcceptInviteModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<EmployeeAccount> _passwordHasher;

    public AcceptInviteModel(DtrDbContext db, IPasswordHasher<EmployeeAccount> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty(SupportsGet = true), Required]
    public string Token { get; set; } = string.Empty;

    [BindProperty, Required, StringLength(24, MinimumLength = 6,
        ErrorMessage = "Use a password between 6 and 24 characters."), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty, Required, Compare(nameof(Password), ErrorMessage = "The passwords do not match."),
        DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool InvitationValid { get; private set; }
    public string? EmployeeName { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await FindValidInvitationAsync(cancellationToken);
        InvitationValid = account is not null;
        EmployeeName = account?.Employee is null
            ? null
            : $"{account.Employee.FirstName} {account.Employee.LastName}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await FindValidInvitationAsync(cancellationToken);
        if (account is null)
        {
            ModelState.AddModelError(string.Empty, "This invitation is invalid or has expired.");
            InvitationValid = false;
            return Page();
        }

        InvitationValid = true;
        EmployeeName = $"{account.Employee!.FirstName} {account.Employee.LastName}";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var tokenHash = AccountTokens.Hash(Token);
        var passwordHash = _passwordHasher.HashPassword(account, Password);
        var securityStamp = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _db.EmployeeAccounts
            .Where(x => x.Id == account.Id
                        && x.PasswordHash == null
                        && x.InvitationTokenHash == tokenHash
                        && x.InvitationExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.PasswordHash, passwordHash)
                    .SetProperty(x => x.MustChangePassword, false)
                    .SetProperty(x => x.InvitationTokenHash, (string?)null)
                    .SetProperty(x => x.InvitationExpiresAt, (DateTime?)null)
                    .SetProperty(x => x.SecurityStamp, securityStamp)
                    .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, "This invitation has already been used or has expired.");
            InvitationValid = false;
            return Page();
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "AccountActivated",
            EntityName = "EmployeeAccount",
            EntityId = account.EmployeeId.ToString(),
            ActorEmployeeId = account.EmployeeId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["StatusMessage"] = "Your staff account is ready. Sign in to view your records.";
        return RedirectToPage("/Employee/Login");
    }

    private async Task<EmployeeAccount?> FindValidInvitationAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            return null;
        }
        var hash = AccountTokens.Hash(Token);
        return await _db.EmployeeAccounts
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(
                x => x.PasswordHash == null
                     && x.InvitationTokenHash == hash
                     && x.InvitationExpiresAt > DateTime.UtcNow
                     && x.Employee!.Status == "Active",
                cancellationToken);
    }
}
