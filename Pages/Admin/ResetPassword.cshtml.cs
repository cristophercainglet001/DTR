using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public class ResetPasswordModel : PageModel
{
    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _passwordHasher;

    public ResetPasswordModel(DtrDbContext db, IPasswordHasher<AdminAccount> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    [BindProperty(SupportsGet = true)]
    [Required]
    public string Token { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(24, MinimumLength = 6, ErrorMessage = "Use a password between 6 and 24 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool IsTokenValid { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        IsTokenValid = await FindValidAccountAsync(cancellationToken) is not null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var account = await FindValidAccountAsync(cancellationToken);
        if (account is null)
        {
            IsTokenValid = false;
            ModelState.AddModelError(string.Empty, "This password reset link is invalid or has expired.");
            return Page();
        }

        IsTokenValid = true;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var passwordHash = _passwordHasher.HashPassword(account, Password);
        var securityStamp = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _db.AdminAccounts
            .Where(x => x.Id == account.Id
                        && x.PasswordResetTokenHash == Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(Token)))
                        && x.PasswordResetTokenExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.PasswordHash, passwordHash)
                    .SetProperty(x => x.SecurityStamp, securityStamp)
                    .SetProperty(x => x.PasswordResetTokenHash, (string?)null)
                    .SetProperty(x => x.PasswordResetTokenExpiresAt, (DateTime?)null)
                    .SetProperty(x => x.FailedLoginAttempts, 0)
                    .SetProperty(x => x.LockoutEnd, (DateTime?)null)
                    .SetProperty(x => x.UpdatedAt, now),
                cancellationToken);

        if (updated != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            IsTokenValid = false;
            ModelState.AddModelError(string.Empty, "This password reset link is invalid or has expired.");
            return Page();
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "PasswordReset",
            EntityName = "AdminAccount",
            EntityId = account.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["StatusMessage"] = "Your password has been reset. Sign in with your new password.";
        return RedirectToPage("/Admin/Login");
    }

    private async Task<AdminAccount?> FindValidAccountAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            return null;
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token)));
        var account = await _db.AdminAccounts
            .FirstOrDefaultAsync(x => x.PasswordResetTokenHash == tokenHash, cancellationToken);

        if (account?.PasswordResetTokenExpiresAt is not DateTime expiresAt
            || expiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return account;
    }
}
