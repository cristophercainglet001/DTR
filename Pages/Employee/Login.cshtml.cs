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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public class LoginModel : PageModel
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<EmployeeAccount> _passwordHasher;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        DtrDbContext db,
        IPasswordHasher<EmployeeAccount> passwordHasher,
        ILogger<LoginModel> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [BindProperty, Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string ErrorMessage { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Employee"))
        {
            return RedirectToPage("/Employee/Dashboard");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Invalid email or password.";
            return Page();
        }

        var normalizedEmail = Email.Trim().ToUpperInvariant();
        var account = await _db.EmployeeAccounts
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

        if (account?.Employee?.Status != "Active"
            || account.PasswordHash is null
            || account.LockoutEnd > DateTime.UtcNow)
        {
            _logger.LogWarning("Rejected employee login attempt from {RemoteIp}.", HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "Invalid email or password.";
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
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Rejected employee login attempt from {RemoteIp}.", HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "Invalid email or password.";
            return Page();
        }

        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = _passwordHasher.HashPassword(account, Password);
        }
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var mustChangePassword = account.MustChangePassword;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Employee.EmployeeId),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Role, mustChangePassword ? "EmployeePasswordChangeRequired" : "Employee"),
            new Claim("employee_id", account.EmployeeId.ToString()),
            new Claim("security_stamp", account.SecurityStamp),
        };
        await HttpContext.SignInAsync(
            EmployeeAuthDefaults.Scheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, EmployeeAuthDefaults.Scheme)),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
            });

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "Login",
            EntityName = "EmployeeAccount",
            EntityId = account.EmployeeId.ToString(),
            ActorEmployeeId = account.EmployeeId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);

        if (mustChangePassword)
        {
            return RedirectToPage("/Employee/ChangePassword");
        }

        return Url.IsLocalUrl(ReturnUrl)
            ? LocalRedirect(ReturnUrl!)
            : RedirectToPage("/Employee/Dashboard");
    }
}
