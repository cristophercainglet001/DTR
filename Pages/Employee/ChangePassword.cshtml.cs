using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[Authorize(
    AuthenticationSchemes = EmployeeAuthDefaults.Scheme,
    Roles = "EmployeePasswordChangeRequired")]
public sealed class ChangePasswordModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<EmployeeAccount> _passwordHasher;

    public ChangePasswordModel(DtrDbContext db, IPasswordHasher<EmployeeAccount> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty, Required, StringLength(24, MinimumLength = 6,
        ErrorMessage = "Use a password between 6 and 24 characters."), DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty, Required, Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match."),
        DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await GetCurrentAccountAsync(cancellationToken);
        return account is null ? Forbid() : Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (account is null)
        {
            return Forbid();
        }

        if (ModelState.IsValid
            && _passwordHasher.VerifyHashedPassword(account, account.PasswordHash!, NewPassword)
                != PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(nameof(NewPassword), "Choose a password different from the temporary password.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var now = DateTime.UtcNow;
        account.PasswordHash = _passwordHasher.HashPassword(account, NewPassword);
        account.MustChangePassword = false;
        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "PasswordChangedAfterReset",
            EntityName = "EmployeeAccount",
            EntityId = account.EmployeeId.ToString(),
            ActorEmployeeId = account.EmployeeId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        await HttpContext.SignOutAsync(EmployeeAuthDefaults.Scheme);
        TempData["StatusMessage"] = "Your password has been changed. Sign in with your new password.";
        return RedirectToPage("/Employee/Login");
    }

    private async Task<EmployeeAccount?> GetCurrentAccountAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            return null;
        }

        var account = await _db.EmployeeAccounts
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);

        return account is { MustChangePassword: true, PasswordHash: not null }
               && account.Employee?.Status == "Active"
            ? account
            : null;
    }
}
