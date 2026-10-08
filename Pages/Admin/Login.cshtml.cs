using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace DepEdDTRSystem.Pages.Admin;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public class LoginModel : PageModel
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _passwordHasher;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        DtrDbContext db,
        IPasswordHasher<AdminAccount> passwordHasher,
        ILogger<LoginModel> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [BindProperty]
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;

    public string ErrorMessage { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Admin/Dashboard");
        }

        AdminSetupRequired = !await _db.AdminAccounts.AnyAsync();
        return Page();
    }

    public bool AdminSetupRequired { get; private set; }

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers.CacheControl = "no-store";
        AdminSetupRequired = !await _db.AdminAccounts.AnyAsync();
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var normalizedUsername = Username.Trim().ToUpper();
        var account = await _db.AdminAccounts
            .FirstOrDefaultAsync(x => x.Username.ToUpper() == normalizedUsername);

        if (account is null || account.LockoutEnd > DateTime.UtcNow)
        {
            _logger.LogWarning("Rejected admin login attempt from {RemoteIp}.", HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        var verification = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.FailedLoginAttempts = 0;
                account.LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            }
            account.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogWarning("Rejected admin login attempt from {RemoteIp}.", HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = _passwordHasher.HashPassword(account, Password);
        }
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("security_stamp", account.SecurityStamp),
        };
        if (account.MustChangePassword)
        {
            claims.Add(new Claim(ClaimTypes.Role, "AdminPasswordChangeRequired"));
        }

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
            });

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "Login",
            EntityName = "AdminAccount",
            EntityId = account.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        return account.MustChangePassword
            ? RedirectToPage("/Admin/ChangePassword")
            : RedirectToPage("/Admin/Dashboard");
    }
}
