using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(
    AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme,
    Roles = "AdminPasswordChangeRequired")]
public sealed class ChangePasswordModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _passwordHasher;

    public ChangePasswordModel(DtrDbContext db, IPasswordHasher<AdminAccount> passwordHasher)
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
        return await GetCurrentAccountAsync(cancellationToken) is null ? Forbid() : Page();
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
            && _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, NewPassword)
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

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "PasswordChangedAfterLocalRecovery",
            EntityName = "AdminAccount",
            EntityId = account.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["StatusMessage"] = "Your password has been changed. Sign in with your new password.";
        return RedirectToPage("/Admin/Login");
    }

    private async Task<AdminAccount?> GetCurrentAccountAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            return null;
        }

        var account = await _db.AdminAccounts
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);

        return account is { MustChangePassword: true } ? account : null;
    }
}
